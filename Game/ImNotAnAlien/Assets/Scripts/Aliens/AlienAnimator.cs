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
    private static readonly int HitHeadId = Animator.StringToHash("PancadaCabeca");
    private static readonly int HitChestId = Animator.StringToHash("PancadaPeito");
    private static readonly int ShotId = Animator.StringToHash("Tiro");
    private static readonly int ReloadId = Animator.StringToHash("Recarga");
    private static readonly int ActivityId = Animator.StringToHash("Atividade");
    private static readonly int SwimmingId = Animator.StringToHash("Swimming");

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
    private bool hasExtraParams; // pancada, tiro, recarga e atividades (Missão 19)
    private AlienPoliceWeapon gun;
    private bool gunChecked;
    private int lastStruck;
    private int lastShot;
    private int lastReload;
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
    // Natação (Missão 20): na água funda o modelo sobe até a linha da água e usa a animação de nadar
    // (estado "Nadando" do Alien.controller, criado por "Preparar Nado dos Aliens (Missao 20)").
    [Header("Natação")]
    [Tooltip("Profundidade mínima da água (m) para nadar.")]
    [SerializeField, Min(0f)] private float swimDepthToFloat = 1.3f;
    [Tooltip("Enquanto nada, a raiz do modelo fica esta distância abaixo da superfície (as animações de nado já descem o corpo).")]
    [SerializeField] private float swimRootBelowSurface = 0.05f;
    [Tooltip("Nadando para a frente, o alien sobe até isto (m) para a cabeça ficar fora d'água.")]
    [SerializeField] private float swimForwardLift = 0.15f;
    [Tooltip("Nadando para a frente, o corpo inclina este ângulo (graus): cabeça para cima, pernas para baixo.")]
    [SerializeField] private float swimSurfaceTilt = 12f;
    private float swimTilt;
    private Transform tiltModel;
    private Quaternion tiltBase;
    private bool tiltApplied;
    private static readonly Collider[] waterHits = new Collider[8];
    private bool hasSwimParam;
    private bool swimming;
    private float swimLift;
    private float waterTimer;
    private float waterSurfaceY;
    private SwimWake wake;
    private static bool warnedNoSwim;

    // Alienígena aquática (AquaticAlien): nada SUBMERSA, perto do fundo, e sobe na direção do jogador
    // quando ele entra na água por perto. As animações dela já são deitadas (golfinho): sem a
    // inclinação de superfície; o corpo aponta para cima/baixo quando ela sobe/desce.
    [Header("Natação (aquática)")]
    [Tooltip("Altura da raiz acima do fundo enquanto passeia (m).")]
    [SerializeField, Min(0.2f)] private float aquaticCruiseHeight = 0.6f;
    [Tooltip("Distância mínima entre a raiz e a superfície (fica sempre submersa).")]
    [SerializeField, Min(0.2f)] private float aquaticSurfaceGap = 0.55f;
    [Tooltip("Jogador na água a menos disto (m): ela sobe/desce até a altura dele.")]
    [SerializeField, Min(1f)] private float aquaticNoticeRange = 20f;
    [Tooltip("Inclinação máxima (graus) subindo ou descendo.")]
    [SerializeField, Range(0f, 60f)] private float aquaticMaxPitch = 35f;
    [Tooltip("A aquática já nada a partir desta profundidade (m); os outros, a partir de Swim Depth To Float.")]
    [SerializeField, Min(0.3f)] private float aquaticSwimDepth = 0.8f;
    private bool aquatic;

    public bool IsSwimming => swimming;

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
        aquatic = GetComponent<AquaticAlien>() != null;
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
        // Missão 20: arma do Sci-Fi Kit (pistola; rifle para o exército no nível 5).
        WeaponKit.Equip(weapon, WantedSystem.Instance != null && WantedSystem.Instance.Stars >= AlienAppearance.ArmyFromStars);
        weaponRenderers = weapon != null ? weapon.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == BatonId)
            {
                hasMeleeParams = true;
            }

            if (parameter.nameHash == ActivityId)
            {
                hasExtraParams = true;
            }

            if (parameter.nameHash == SwimmingId)
            {
                hasSwimParam = true;
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
        UpdateSwim(dead, tumbling);

        // Com o Animator desligado (membros moles) ou ainda não iniciado, não dá para mexer nos
        // parâmetros: o Unity reclama "Parameter does not exist".
        if (animator.isActiveAndEnabled && animator.isInitialized)
        {
            animator.SetFloat(SpeedId, smoothedSpeed);
            animator.SetBool(TumblingId, tumbling && !dead);
            animator.SetBool(HeldId, held && !dead);
            animator.SetBool(DeadId, dead);
            animator.SetBool(AimingId, !dead && brain != null && brain.IsAttacking && !swimming);
            if (hasSwimParam)
            {
                animator.SetBool(SwimmingId, swimming);
            }
            UpdateSwing(dead || tumbling);
            UpdateExtras(dead || tumbling);
        }

        UpdateHeldItems(dead);

        lastDead = dead;
        lastTumbling = tumbling;

        // Segurado: anima (procurando o chão). Arremessado ou morto: membros moles.
        ragdoll.SetFloppy((tumbling && !held) || dead, dead);
    }

    // Água funda debaixo do alien: nada na superfície. O NavMesh fica no fundo; o "baseOffset" do agente
    // sobe o alien inteiro (com os colisores, para dar para acertar) até a linha da água.
    private void UpdateSwim(bool dead, bool tumbling)
    {
        float ground = transform.position.y - (agent != null && agent.enabled ? agent.baseOffset : 0f);
        waterTimer -= Time.deltaTime;
        if (waterTimer <= 0f)
        {
            waterTimer = 0.2f + Random.value * 0.1f;
            swimming = !dead && !tumbling && agent != null && agent.enabled && FindWater(ground, out waterSurfaceY);
            if (swimming && !hasSwimParam)
            {
                // Sem o estado "Nadando" no Alien.controller ele "andaria sobre a água": fica no fundo.
                swimming = false;
                if (!warnedNoSwim)
                {
                    warnedNoSwim = true;
                    Debug.LogWarning("Aliens: o Alien.controller não tem o estado de nado. Rode ImNotAnAlien > Preparar Nado dos Aliens (Missao 20).");
                }
            }
        }

        // Nadando para a frente o corpo deitado fica mais baixo: sobe um pouco com a velocidade.
        float target = swimming ? Mathf.Max(0f, waterSurfaceY - swimRootBelowSurface - ground + swimForwardLift * Mathf.Clamp01(smoothedSpeed / 2f)) : 0f;
        if (swimming && aquatic)
        {
            target = AquaticHeight(ground);
        }

        float lift = Mathf.Lerp(swimLift, target, 1f - Mathf.Exp((aquatic ? -2f : -4f) * Time.deltaTime));

        // Inclinação nadando para a frente (igual ao protagonista). Aquática: aponta para onde está indo
        // (para cima subindo, para baixo descendo).
        float wantTilt = swimming ? -swimSurfaceTilt * Mathf.Clamp01(smoothedSpeed / 2f) : 0f;
        if (aquatic)
        {
            float climb = Time.deltaTime > 0f ? (lift - swimLift) / Time.deltaTime : 0f;
            wantTilt = swimming ? Mathf.Clamp(-Mathf.Atan2(climb, Mathf.Max(0.6f, smoothedSpeed)) * Mathf.Rad2Deg, -aquaticMaxPitch, aquaticMaxPitch) : 0f;
        }

        swimTilt = Mathf.Lerp(swimTilt, wantTilt, 1f - Mathf.Exp(-6f * Time.deltaTime));
        Transform model = animator.transform;
        if (model != tiltModel || (!tiltApplied && Mathf.Abs(swimTilt) <= 0.01f))
        {
            tiltModel = model;
            tiltBase = model.localRotation;
        }

        if (Mathf.Abs(swimTilt) > 0.01f || tiltApplied)
        {
            model.localRotation = tiltBase * Quaternion.Euler(swimTilt, 0f, 0f);
            tiltApplied = Mathf.Abs(swimTilt) > 0.01f;
        }
        if (swimming || wake != null)
        {
            if (wake == null)
            {
                wake = SwimWake.For(transform);
            }

            // A aquática só levanta espuma quando chega perto da superfície.
            bool atSurface = swimming && (!aquatic || waterSurfaceY - (ground + swimLift) < aquaticSurfaceGap + 0.35f);
            wake.Feed(atSurface, waterSurfaceY, agent != null && agent.enabled ? agent.velocity : Vector3.zero, transform.position + transform.forward * 0.5f);
        }

        if (Mathf.Abs(lift - swimLift) > 0.0001f && agent != null)
        {
            agent.baseOffset = lift;
            swimLift = lift;
        }
    }

    // Altura da raiz (acima do fundo) da aquática: perto do fundo passeando; na altura do jogador
    // quando ele está na água por perto. Sempre abaixo da superfície e sem raspar no fundo.
    private float AquaticHeight(float ground)
    {
        // Água rasa: a folga até a superfície diminui (até metade da profundidade).
        float depth = waterSurfaceY - ground;
        float top = depth - Mathf.Min(aquaticSurfaceGap, depth * 0.5f);
        float want = aquaticCruiseHeight;
        if (PlayerHealth.Instance != null)
        {
            Vector3 player = PlayerHealth.Instance.transform.position;
            Vector3 flat = player - transform.position;
            flat.y = 0f;
            if (player.y < waterSurfaceY + 0.3f && flat.sqrMagnitude < aquaticNoticeRange * aquaticNoticeRange)
            {
                want = player.y + 0.3f - ground;
            }
        }

        float bottom = Mathf.Min(0.3f, Mathf.Max(0f, top));
        return Mathf.Clamp(want, bottom, Mathf.Max(bottom, top));
    }

    private bool FindWater(float ground, out float surfaceY)
    {
        surfaceY = 0f;
        Vector3 feet = new Vector3(transform.position.x, ground + 0.2f, transform.position.z);
        int count = Physics.OverlapSphereNonAlloc(feet, 0.3f, waterHits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (waterHits[i] == null || waterHits[i].GetComponent<WaterZone>() == null)
            {
                continue;
            }

            surfaceY = waterHits[i].bounds.max.y;
            return surfaceY - ground >= (aquatic ? aquaticSwimDepth : swimDepthToFloat);
        }

        return false;
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

    // Pancada (cabeça/peito), tiro e recarga da pistola, e atividades dos civis (conversa, dança, banco).
    private void UpdateExtras(bool busy)
    {
        if (!hasExtraParams || brain == null)
        {
            return;
        }

        animator.SetInteger(ActivityId, busy ? 0 : (int)brain.CurrentActivity);

        if (brain.StruckCount != lastStruck)
        {
            lastStruck = brain.StruckCount;
            if (!busy)
            {
                animator.SetTrigger(brain.LastStruckStrong ? HitHeadId : HitChestId);
            }
        }

        if (!gunChecked)
        {
            gunChecked = true;
            gun = GetComponent<AlienPoliceWeapon>();
            if (gun != null)
            {
                lastShot = gun.ShotCount;
                lastReload = gun.ReloadCount;
            }
        }

        if (gun == null)
        {
            return;
        }

        if (gun.ShotCount != lastShot)
        {
            lastShot = gun.ShotCount;
            if (!busy)
            {
                animator.SetTrigger(ShotId);
            }
        }

        if (gun.ReloadCount != lastReload)
        {
            lastReload = gun.ReloadCount;
            if (!busy)
            {
                animator.SetTrigger(ReloadId);
            }
        }
    }

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
            batonMaterial = new Material(CurvedHorizon.LitShader()) { name = "Cassetete" };
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
