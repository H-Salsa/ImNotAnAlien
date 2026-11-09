using UnityEngine;
using UnityEngine.AI;

// Liga o modelo 3D do alienígena (Missão 15) ao comportamento que já existia.
// Fica na raiz do alienígena (junto do AlienBrain). O modelo é o filho "Corpo/Visual_Alien".
// - Esconde as peças antigas de cápsula (os colisores delas continuam funcionando).
// - Animações: andar/correr pela velocidade do NavMeshAgent e pose de pistola quando o policial
//   está atacando. Segurado pela telecinese: mexe braços e pernas devagar, procurando o chão.
//   Arremessado ou morto: braços, pernas e cabeça ficam moles (AlienRagdoll).
// - A arma antiga do policial passa a acompanhar a mão direita do modelo.
// - Missão 19: golpes do policial no nível 1 (camada "Golpes" do Animator). Cada policial é "de soco"
//   (alterna jab e direto) ou "de cassetete" (leva um bastão na mão direita). Na briga a pistola some.
[DisallowMultipleComponent]
public sealed class AlienAnimator : MonoBehaviour
{
    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int TumblingId = Animator.StringToHash("Tumbling");
    private static readonly int DeadId = Animator.StringToHash("Dead");
    private static readonly int AimingId = Animator.StringToHash("Aiming");
    private static readonly int HeldId = Animator.StringToHash("Held");
    private static readonly int PunchId = Animator.StringToHash("Soco");
    private static readonly int JabId = Animator.StringToHash("Jab");
    private static readonly int BatonId = Animator.StringToHash("Cassetete");

    [SerializeField] private Animator animator;
    [Tooltip("Posição da arma em relação à mão direita (para frente, para cima).")]
    [SerializeField] private Vector2 weaponOffset = new Vector2(0.08f, 0.02f);

    private AlienBrain brain;
    private AlienHealth health;
    private NavMeshAgent agent;
    private Transform rightHand;
    private Transform weapon;
    private float smoothedSpeed;
    private AlienRagdoll ragdoll;

    // Golpes (Missão 19).
    private bool hasMeleeParams;
    private int lastSwing;
    private bool nextJab;
    private Transform baton;
    private Renderer[] weaponRenderers;
    private bool weaponShown = true;

    // Expressões (blend shapes do rosto feitas no Blender).
    [Header("Expressões")]
    [Tooltip("Velocidade da troca de expressão (pontos por segundo, de 0 a 100).")]
    [SerializeField, Min(1f)] private float expressionSpeed = 400f;
    [Tooltip("Cara de bravo do policial quando não está atirando (0 a 100).")]
    [SerializeField, Range(0f, 100f)] private float policeIdleAnger = 60f;
    public enum ExpressionTest { Automatica, Normal, Apavorado, Bravo }
    [Tooltip("Para conferir as expressões: força uma cara neste alien (deixe em Automatica no jogo).")]
    [SerializeField] private ExpressionTest expressionTest = ExpressionTest.Automatica;
    private bool lastDead;
    private bool lastTumbling;
    private SkinnedMeshRenderer face;
    private int scaredIndex = -1;
    private int angryIndex = -1;
    private float scared;
    private float angry;

    // Segurado: braços e pernas se mexem devagar por cima da animação.
    [Header("Flutuando (segurado)")]
    [SerializeField, Range(0f, 60f)] private float armFlail = 32f;
    [SerializeField, Range(0f, 60f)] private float legFlail = 24f;
    [SerializeField, Min(0.1f)] private float flailSpeed = 2.2f;
    private Transform leftArm, rightArm, leftLeg, rightLeg;

    // Fugindo: olha para trás, por cima do ombro, de vez em quando (assim dá para ver a cara de pânico).
    [Header("Fugindo")]
    [Tooltip("Fração do tempo em que o civil fugindo fica olhando para trás (0 a 1).")]
    [SerializeField, Range(0f, 1f)] private float lookBackShare = 0.6f;
    [Tooltip("Giro máximo (graus) somando cintura, pescoço e cabeça.")]
    [SerializeField, Range(0f, 160f)] private float lookBackMaxAngle = 130f;
    private Transform spineBone, chestBone, neckBone, headBone;
    private float lookBackWeight;
    private float lookBackTimer;
    private bool lookingBack;
    private float lookYaw;          // giro atual (suavizado)
    private float lookSide = 1f;    // ombro escolhido (+1 / -1)
    [Tooltip("Velocidade máxima do giro da cabeça (graus por segundo). Menor = troca de ombro mais suave.")]
    [SerializeField, Min(30f)] private float lookTurnSpeed = 200f;
    private bool held;
    private float heldWeight;
    private float flailPhase;
    private Rigidbody rootBody;
    [Tooltip("Segurado: quão rápido ele fica de pé e vira de frente para o jogador.")]
    [SerializeField, Min(0.1f)] private float uprightSpeed = 6f;

    private void Awake()
    {
        brain = GetComponent<AlienBrain>();
        health = GetComponent<AlienHealth>();
        agent = GetComponent<NavMeshAgent>();
        // Com dois modelos (masculino e feminino), o AlienAppearance desliga um deles antes daqui.
        if (animator == null || !animator.gameObject.activeInHierarchy)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            enabled = false;
            return;
        }

        animator.applyRootMotion = false;
        HideOldParts();
        if (animator.isHuman)
        {
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            spineBone = animator.GetBoneTransform(HumanBodyBones.Spine);
            chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
            neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        }

        weapon = transform.Find("Corpo/Arma");
        weaponRenderers = weapon != null ? weapon.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == BatonId)
            {
                hasMeleeParams = true;
            }
        }
        flailPhase = Random.Range(0f, 10f);

        // O rosto é a malha que tem as expressões (fica dentro de "Armature" no FBX, por isso a busca
        // olha todos os filhos).
        foreach (SkinnedMeshRenderer candidate in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null && candidate.sharedMesh.GetBlendShapeIndex("Apavorado") >= 0)
            {
                face = candidate;
                scaredIndex = face.sharedMesh.GetBlendShapeIndex("Apavorado");
                angryIndex = face.sharedMesh.GetBlendShapeIndex("Bravo");
                break;
            }
        }

        if (face == null)
        {
            Debug.LogWarning($"{name}: modelo sem as expressões (Apavorado/Bravo). Rode a ferramenta Montar Aliens 3D de novo.", this);
        }


        // Membros moles quando voa ou morre.
        if (!TryGetComponent(out ragdoll))
        {
            ragdoll = gameObject.AddComponent<AlienRagdoll>();
        }

        ragdoll.Init(animator);
    }

    private void Start()
    {
        rootBody = GetComponent<Rigidbody>();

        // A cor da pele e as roupas ficam por conta do AlienAppearance.
    }

    private void Update()
    {
        bool dead = health != null && health.IsDead;
        held = brain != null && brain.IsHeld && !dead;
        bool tumbling = brain != null && (brain.IsTumbling || held);

        float speed = 0f;
        if (!dead && !tumbling && agent != null && agent.enabled)
        {
            speed = agent.velocity.magnitude;
        }

        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-10f * Time.deltaTime));

        // Com o Animator desligado (membros moles) ou ainda não iniciado, não dá para mexer nos
        // parâmetros: o Unity reclama "Parameter does not exist".
        if (animator.isActiveAndEnabled && animator.isInitialized)
        {
            animator.SetFloat(SpeedId, smoothedSpeed);
            animator.SetBool(TumblingId, tumbling && !dead);
            animator.SetBool(HeldId, held && !dead);
            animator.SetBool(DeadId, dead);
            animator.SetBool(AimingId, !dead && brain != null && brain.IsAttacking);
            UpdateSwing(dead || tumbling);
        }

        UpdateHeldItems(dead);

        lastDead = dead;
        lastTumbling = tumbling;

        // Segurado: anima (procurando o chão). Arremessado ou morto: membros moles.
        ragdoll.SetFloppy((tumbling && !held) || dead, dead);
    }

    // Segurado: fica de pé (sem virar de cabeça para baixo) e de frente para o jogador, assustado.
    private void FixedUpdate()
    {
        if (!held || rootBody == null || rootBody.isKinematic)
        {
            return;
        }

        Vector3 look = rootBody.rotation * Vector3.forward;
        if (PlayerHealth.Instance != null)
        {
            look = PlayerHealth.Instance.transform.position - rootBody.position;
        }

        look = Vector3.ProjectOnPlane(look, Vector3.up);
        if (look.sqrMagnitude < 0.0001f)
        {
            look = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }

        if (look.sqrMagnitude < 0.0001f)
        {
            look = Vector3.forward;
        }

        Quaternion upright = Quaternion.LookRotation(look.normalized, Vector3.up);
        rootBody.angularVelocity = Vector3.zero;
        rootBody.MoveRotation(Quaternion.Slerp(rootBody.rotation, upright, 1f - Mathf.Exp(-uprightSpeed * Time.fixedDeltaTime)));
    }

    private void LateUpdate()
    {
        // Depois do Animator: se alguma animação mexer nos blend shapes, a expressão ganha mesmo assim.
        UpdateExpression(lastDead, lastTumbling);
        UpdateFlail();
        UpdateLookBack();
        if (weapon == null || rightHand == null)
        {
            return;
        }

        Vector3 forward = transform.forward;
        weapon.position = rightHand.position + forward * weaponOffset.x + Vector3.up * weaponOffset.y;
        weapon.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    // Civil fugindo, segurado ou voando: APAVORADO. Policial: BRAVO (mais ainda atirando);
    // se a telecinese pegar o policial, ele também fica apavorado.
    private void UpdateExpression(bool dead, bool tumbling)
    {
        if (face == null)
        {
            return;
        }

        bool police = brain != null && brain.AlienRole == AlienBrain.Role.Policial;
        bool fleeing = brain != null && brain.IsFleeing;
        float wantScared = tumbling || dead || (!police && fleeing) ? 100f : 0f;
        float wantAngry = police && !tumbling && !dead ? (brain.IsAttacking || brain.IsMelee ? 100f : policeIdleAnger) : 0f;

        if (expressionTest != ExpressionTest.Automatica)
        {
            wantScared = expressionTest == ExpressionTest.Apavorado ? 100f : 0f;
            wantAngry = expressionTest == ExpressionTest.Bravo ? 100f : 0f;
        }

        float step = expressionSpeed * Time.deltaTime;
        scared = Mathf.MoveTowards(scared, wantScared, step);
        angry = Mathf.MoveTowards(angry, wantAngry, step);
        if (scaredIndex >= 0)
        {
            face.SetBlendShapeWeight(scaredIndex, scared);

        }

        if (angryIndex >= 0)
        {
            face.SetBlendShapeWeight(angryIndex, angry);
        }
    }

    // ---------- Golpes (Missão 19) ----------

    private void UpdateSwing(bool busy)
    {
        if (brain == null || brain.SwingCount == lastSwing)
        {
            return;
        }

        lastSwing = brain.SwingCount;
        if (busy || !hasMeleeParams)
        {
            return;
        }

        if (brain.UsesBaton)
        {
            animator.SetTrigger(BatonId);
        }
        else
        {
            animator.SetTrigger(nextJab ? JabId : PunchId);
            nextJab = !nextJab;
        }
    }

    // Cassetete na mão (policial "de cassetete", menos quando está atirando) e pistola escondida na briga.
    private void UpdateHeldItems(bool dead)
    {
        if (brain == null || brain.AlienRole != AlienBrain.Role.Policial)
        {
            return;
        }

        bool showWeapon = !brain.IsMelee;
        if (showWeapon != weaponShown)
        {
            weaponShown = showWeapon;
            foreach (Renderer part in weaponRenderers)
            {
                if (part != null)
                {
                    part.enabled = showWeapon;
                }
            }
        }

        if (!brain.UsesBaton)
        {
            return;
        }

        if (baton == null)
        {
            baton = CreateBaton();
            if (baton == null)
            {
                return;
            }
        }

        bool showBaton = !brain.IsAttacking;
        if (baton.gameObject.activeSelf != showBaton)
        {
            baton.gameObject.SetActive(showBaton);
        }
    }

    // Cassetete simples feito de cilindros, preso ao osso da mão direita. O eixo do bastão atravessa
    // o punho fechado (do dedo mínimo para o indicador) e a maior parte sai por cima, do lado do polegar.
    private Transform CreateBaton()
    {
        if (rightHand == null || !animator.isHuman)
        {
            return null;
        }

        Transform index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
        Transform little = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
        Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        Vector3 axis = index != null && little != null ? (index.position - little.position).normalized : rightHand.up;
        Vector3 fist = middle != null ? Vector3.Lerp(rightHand.position, middle.position, 0.75f) : rightHand.position;

        GameObject root = new GameObject("Cassetete");
        root.transform.SetParent(rightHand, false);
        root.transform.position = fist;
        root.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);

        if (batonMaterial == null)
        {
            batonMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Cassetete" };
            batonMaterial.color = new Color(0.08f, 0.08f, 0.09f);
            batonMaterial.SetFloat("_Smoothness", 0.35f);
        }

        // Para o tamanho ficar certo mesmo com o alien em escala diferente.
        float scale = 1f / Mathf.Max(0.01f, rightHand.lossyScale.x);
        // Bastão de ~56 cm (o cilindro do Unity tem 2 m de altura com escala 1) e uma pegada mais grossa.
        MakeCylinder(root.transform, batonMaterial, new Vector3(0f, 0.18f, 0f), new Vector3(0.036f, 0.28f, 0.036f), scale);
        MakeCylinder(root.transform, batonMaterial, new Vector3(0f, -0.02f, 0f), new Vector3(0.046f, 0.07f, 0.046f), scale);
        return root.transform;
    }

    private static Material batonMaterial;

    private static void MakeCylinder(Transform parent, Material material, Vector3 localPosition, Vector3 size, float scale)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition * scale;
        part.transform.localScale = size * scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    private void UpdateLookBack()
    {
        bool fleeing = brain != null && brain.IsFleeing && animator.enabled && !ragdoll.IsFloppy
                       && brain.AlienRole == AlienBrain.Role.Civil;
        lookBackTimer -= Time.deltaTime;
        if (lookBackTimer <= 0f)
        {
            lookingBack = Random.value < lookBackShare;
            lookBackTimer = Random.Range(0.6f, 1.4f);
        }

        float want = fleeing && lookingBack ? 1f : 0f;
        lookBackWeight = Mathf.MoveTowards(lookBackWeight, want, 4f * Time.deltaTime);
        if (lookBackWeight <= 0.001f || PlayerHealth.Instance == null || headBone == null)
        {
            lookYaw = 0f;
            return;
        }

        Vector3 toPlayer = Vector3.ProjectOnPlane(PlayerHealth.Instance.transform.position - transform.position, Vector3.up);
        if (toPlayer.sqrMagnitude < 0.01f)
        {
            return;
        }

        float raw = Vector3.SignedAngle(Vector3.ProjectOnPlane(transform.forward, Vector3.up), toPlayer, Vector3.up);

        // Jogador quase bem atrás: o ângulo "pula" de um lado para o outro. Só troca de ombro quando
        // o jogador passa claramente para o outro lado (folga de 25°); assim não fica alternando.
        if (Mathf.Abs(raw) < 155f || Mathf.Sign(raw) == lookSide)
        {
            lookSide = Mathf.Sign(raw);
        }

        float target = Mathf.Abs(raw) >= 155f ? lookSide * 180f : raw;
        target = Mathf.Clamp(target, -lookBackMaxAngle, lookBackMaxAngle);

        // Gira até o alvo com velocidade limitada: trocar de ombro passa pela frente, suave.
        lookYaw = Mathf.MoveTowards(lookYaw, target, lookTurnSpeed * Time.deltaTime);
        float yaw = lookYaw * Mathf.SmoothStep(0f, 1f, lookBackWeight);
        TwistBone(spineBone, yaw * 0.2f);
        TwistBone(chestBone, yaw * 0.25f);
        TwistBone(neckBone, yaw * 0.25f);
        TwistBone(headBone, yaw * 0.3f);
    }

    private static void TwistBone(Transform bone, float degrees)
    {
        if (bone != null)
        {
            bone.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * bone.rotation;
        }
    }

    // Segurado pela telecinese: bate os braços e "pedala" com as pernas, devagar, procurando o chão.
    private void UpdateFlail()
    {
        bool active = held && !ragdoll.IsFloppy && animator.enabled;
        heldWeight = Mathf.MoveTowards(heldWeight, active ? 1f : 0f, 3f * Time.deltaTime);
        if (heldWeight <= 0f || leftArm == null || rightArm == null || leftLeg == null || rightLeg == null)
        {
            return;
        }

        float t = Time.time * flailSpeed + flailPhase;
        float w = Mathf.SmoothStep(0f, 1f, heldWeight);
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Rotate(leftArm, Quaternion.AngleAxis(Mathf.Sin(t) * armFlail * w, forward));
        Rotate(rightArm, Quaternion.AngleAxis(-Mathf.Sin(t + 1.3f) * armFlail * w, forward));
        Rotate(leftLeg, Quaternion.AngleAxis(Mathf.Sin(t * 0.85f) * legFlail * w, right));
        Rotate(rightLeg, Quaternion.AngleAxis(Mathf.Sin(t * 0.85f + Mathf.PI) * legFlail * w, right));
    }

    private static void Rotate(Transform bone, Quaternion worldRotation)
    {
        bone.rotation = worldRotation * bone.rotation;
    }

    // Desliga os desenhos da cápsula antiga (tronco, cabeça, olhos, antena, quepe...),
    // mas mantém o modelo novo, o "!" de alerta e a arma.
    private void HideOldParts()
    {
        Transform model = animator.transform;
        Transform alert = transform.Find("Corpo/Alerta");
        Transform gun = transform.Find("Corpo/Arma");
        foreach (Renderer part in GetComponentsInChildren<Renderer>(true))
        {
            Transform t = part.transform;
            if (t.IsChildOf(model) || (alert != null && t.IsChildOf(alert)) || (gun != null && t.IsChildOf(gun)))
            {
                continue;
            }

            part.enabled = false;
        }
    }
}
