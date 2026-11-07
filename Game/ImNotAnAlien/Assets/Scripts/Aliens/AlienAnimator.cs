using UnityEngine;
using UnityEngine.AI;

// Liga o modelo 3D do alienígena (Missão 15) ao comportamento que já existia.
// Fica na raiz do alienígena (junto do AlienBrain). O modelo é o filho "Corpo/Visual_Alien".
// - Esconde as peças antigas de cápsula (os colisores delas continuam funcionando).
// - Animações: andar/correr pela velocidade do NavMeshAgent e pose de pistola quando o policial
//   está atacando. Segurado pela telecinese: mexe braços e pernas devagar, procurando o chão.
//   Arremessado ou morto: braços, pernas e cabeça ficam moles (AlienRagdoll).
// - A arma antiga do policial passa a acompanhar a mão direita do modelo.
[DisallowMultipleComponent]
public sealed class AlienAnimator : MonoBehaviour
{
    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int TumblingId = Animator.StringToHash("Tumbling");
    private static readonly int DeadId = Animator.StringToHash("Dead");
    private static readonly int AimingId = Animator.StringToHash("Aiming");
    private static readonly int HeldId = Animator.StringToHash("Held");

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

    // Segurado: braços e pernas se mexem devagar por cima da animação.
    [Header("Flutuando (segurado)")]
    [SerializeField, Range(0f, 60f)] private float armFlail = 32f;
    [SerializeField, Range(0f, 60f)] private float legFlail = 24f;
    [SerializeField, Min(0.1f)] private float flailSpeed = 2.2f;
    private Transform leftArm, rightArm, leftLeg, rightLeg;
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
            rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        }

        weapon = transform.Find("Corpo/Arma");
        flailPhase = Random.Range(0f, 10f);

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
        }

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
        UpdateFlail();
        if (weapon == null || rightHand == null)
        {
            return;
        }

        Vector3 forward = transform.forward;
        weapon.position = rightHand.position + forward * weaponOffset.x + Vector3.up * weaponOffset.y;
        weapon.rotation = Quaternion.LookRotation(forward, Vector3.up);
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
