using System.Collections.Generic;
using UnityEngine;

// "Boneco de pano" do alienígena (Missão 15).
// Enquanto ele anda (ou flutua segurado pela telecinese), as animações mandam. Quando é arremessado
// ou morre, o corpo vira um conjunto de peças com física presas por juntas que dobram como um
// corpo de verdade:
// - cotovelo e joelho dobram só para o lado certo; ombro, quadril, pescoço e cintura giram com limite;
// - cada junta tem um pouco de "músculo" (mola fraca) que puxa de volta para a pose normal.
//   Por isso, ao bater na parede o braço dobra com o impacto e depois volta, e o tranco passa
//   para o peito e para as pernas de forma natural, sem virar macarrão. Morto, o músculo afrouxa.
// O quadril continua sendo o corpo físico de sempre (telecinese, dano e contagem não mudam).
// Montado sozinho no início do jogo (o AlienAnimator adiciona este componente).
[DisallowMultipleComponent]
public sealed class AlienRagdoll : MonoBehaviour
{
    [Header("Peso (kg) — o quadril/tronco principal tem 60")]
    [SerializeField, Min(0.1f)] private float chestMass = 12f;
    [SerializeField, Min(0.1f)] private float headMass = 4f;
    [SerializeField, Min(0.1f)] private float upperArmMass = 2.5f;
    [SerializeField, Min(0.1f)] private float forearmMass = 1.8f;
    [SerializeField, Min(0.1f)] private float thighMass = 5f;
    [SerializeField, Min(0.1f)] private float shinMass = 3.5f;

    [Header("Músculo")]
    [Tooltip("Força que puxa cada junta de volta para a pose normal (vivo). 0 = totalmente mole.")]
    [SerializeField, Min(0f)] private float muscle = 25f;
    [Tooltip("Fração do músculo quando morto (0,15 = bem mais mole).")]
    [SerializeField, Range(0f, 1f)] private float deadMuscle = 0.15f;
    [Tooltip("Amortecimento das juntas (maior = movimentos mais suaves, menos tremidos).")]
    [SerializeField, Min(0f)] private float damping = 3f;

    [Header("Volta para a animação")]
    [SerializeField, Min(0.01f)] private float blendBackTime = 0.35f;

    private const int IgnoreRaycastLayer = 2; // a mira da telecinese não pega braço solto

    private enum Kind
    {
        Spine,
        Neck,
        Ball,   // ombro, quadril
        Hinge,  // cotovelo, joelho
    }

    private struct Part
    {
        public Transform bone;
        public Rigidbody body;
        public Collider collider;
        public ConfigurableJoint joint;
        public Vector3 restLocalPosition;
        public Quaternion savedRotation;
        public Vector3 savedPosition;
    }

    private readonly List<Part> parts = new List<Part>();
    private Animator animator;
    private Rigidbody rootBody;
    private bool built;
    private bool floppy;
    private bool limp;
    private float blendTimer;

    public bool IsFloppy => floppy;

    public void Init(Animator modelAnimator)
    {
        animator = modelAnimator;
    }

    private void Start()
    {
        Build();
    }

    // ---------- Montagem ----------

    private void Build()
    {
        rootBody = GetComponent<Rigidbody>();
        if (animator == null || !animator.isHuman || rootBody == null)
        {
            return;
        }

        Vector3 up = transform.up;
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        // Peito (dobra na cintura), preso ao quadril.
        Transform chestBone = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (chestBone == null)
        {
            chestBone = animator.GetBoneTransform(HumanBodyBones.Spine);
        }

        Rigidbody chest = AddPart(chestBone, animator.GetBoneTransform(HumanBodyBones.Neck), rootBody, 0.13f, chestMass,
            Kind.Spine, right, forward);
        Rigidbody shoulderParent = chest != null ? chest : rootBody;

        // Cabeça (grande), presa ao peito.
        AddHead(shoulderParent, right);

        // Braços: cotovelo dobra para a frente.
        foreach (bool left in new[] { true, false })
        {
            Transform upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Rigidbody upperBody = AddPart(upper, lower, shoulderParent, 0.04f, upperArmMass, Kind.Ball, Vector3.zero, forward);
            AddPart(lower, hand, upperBody != null ? upperBody : shoulderParent, 0.035f, forearmMass, Kind.Hinge, up, forward);
        }

        // Pernas: joelho dobra para trás.
        foreach (bool left in new[] { true, false })
        {
            Transform thigh = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Transform shin = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Transform foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Rigidbody thighBody = AddPart(thigh, shin, rootBody, 0.06f, thighMass, Kind.Ball, Vector3.zero, forward);
            AddPart(shin, foot, thighBody != null ? thighBody : rootBody, 0.05f, shinMass, Kind.Hinge, right, -forward);
        }

        IgnoreOwnCollisions();
        built = true;
    }

    private void AddHead(Rigidbody parent, Vector3 right)
    {
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null)
        {
            return;
        }

        float scale = Mathf.Max(head.lossyScale.x, 0.0001f);
        SphereCollider sphere = head.gameObject.AddComponent<SphereCollider>();
        Vector3 center = head.position + transform.up * 0.17f - transform.forward * 0.04f;
        sphere.center = head.InverseTransformPoint(center);
        sphere.radius = 0.15f / scale;
        AddBody(head, sphere, parent, headMass, Kind.Neck, right, transform.up, transform.forward);
    }

    // Peça em forma de cápsula, do osso até o próximo osso.
    private Rigidbody AddPart(Transform bone, Transform next, Rigidbody parent, float radius, float mass, Kind kind,
        Vector3 bendAxis, Vector3 bendTowards)
    {
        if (bone == null || next == null || parent == null)
        {
            return null;
        }

        float scale = Mathf.Max(bone.lossyScale.x, 0.0001f);
        Vector3 worldDir = next.position - bone.position;
        float worldLength = worldDir.magnitude;
        Vector3 localDir = bone.InverseTransformDirection(worldDir).normalized;
        float localLength = worldLength / scale;

        CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = LargestAxis(localDir);
        capsule.center = localDir * (localLength * 0.5f);
        capsule.radius = radius / scale;
        capsule.height = (worldLength + radius * 2f) / scale;

        return AddBody(bone, capsule, parent, mass, kind, bendAxis, worldDir.normalized, bendTowards);
    }

    private Rigidbody AddBody(Transform bone, Collider collider, Rigidbody parent, float mass, Kind kind,
        Vector3 bendAxisWorld, Vector3 boneDirWorld, Vector3 bendTowardsWorld)
    {
        bone.gameObject.layer = IgnoreRaycastLayer;
        collider.enabled = false;

        Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
        body.mass = mass;
        body.linearDamping = 0.05f;
        body.angularDamping = 0.5f;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.solverIterations = 12;
        body.solverVelocityIterations = 4;
        body.maxDepenetrationVelocity = 3f;

        ConfigurableJoint joint = bone.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = parent;
        joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;

        Vector3 primary;   // eixo X da junta
        Vector3 secondary; // eixo Y da junta
        switch (kind)
        {
            case Kind.Hinge:
            {
                // X = eixo da dobra (perpendicular ao osso e ao lado para onde ele dobra);
                // assim, ângulo positivo = dobrar para o lado certo.
                Vector3 axis = Vector3.Cross(boneDirWorld, bendTowardsWorld);
                if (axis.sqrMagnitude < 0.0001f)
                {
                    axis = bendAxisWorld;
                }

                axis.Normalize();

                primary = axis;
                secondary = boneDirWorld;
                SetLimits(joint, -5f, 140f, 8f, 8f);
                break;
            }
            case Kind.Ball:
                primary = boneDirWorld;                                   // X = torcer o braço/perna
                secondary = Vector3.Cross(boneDirWorld, transform.up);
                if (secondary.sqrMagnitude < 0.01f)
                {
                    secondary = Vector3.Cross(boneDirWorld, transform.forward);
                }

                SetLimits(joint, -30f, 30f, 75f, 75f);
                break;
            case Kind.Neck:
                primary = bendAxisWorld;                                  // X = acenar "sim"
                secondary = transform.up;
                SetLimits(joint, -35f, 35f, 40f, 25f);
                break;
            default: // Spine
                primary = bendAxisWorld;                                  // X = dobrar para frente/trás
                secondary = transform.up;
                SetLimits(joint, -25f, 35f, 20f, 25f);
                break;
        }

        joint.axis = bone.InverseTransformDirection(primary.normalized);
        joint.secondaryAxis = bone.InverseTransformDirection(secondary.normalized);
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = 0.05f;
        joint.projectionAngle = 10f;
        joint.enablePreprocessing = false;
        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.targetRotation = Quaternion.identity; // a pose do momento em que a junta foi criada
        SetJointActive(joint, false); // desligada enquanto a animação manda (veja SetJointActive)

        parts.Add(new Part { bone = bone, body = body, collider = collider, joint = joint, restLocalPosition = bone.localPosition });
        return body;
    }

    // IMPORTANTE: com o alien animado, as peças ficam "cinemáticas" (paradas para a física).
    // Uma junta ligada a uma peça cinemática prende o corpo principal nela — era isso que puxava
    // o alien vivo para dentro do chão quando a telecinese o levantava. Então, fora do modo mole,
    // a junta fica totalmente solta e sem músculo.
    private void SetJointActive(ConfigurableJoint joint, bool active)
    {
        ConfigurableJointMotion linear = active ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Free;
        ConfigurableJointMotion angular = active ? ConfigurableJointMotion.Limited : ConfigurableJointMotion.Free;
        joint.xMotion = joint.yMotion = joint.zMotion = linear;
        joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = angular;
        if (!active)
        {
            joint.slerpDrive = new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = 0f };
        }
    }

    private static void SetLimits(ConfigurableJoint joint, float lowX, float highX, float y, float z)
    {
        joint.lowAngularXLimit = new SoftJointLimit { limit = lowX };
        joint.highAngularXLimit = new SoftJointLimit { limit = highX };
        joint.angularYLimit = new SoftJointLimit { limit = y };
        joint.angularZLimit = new SoftJointLimit { limit = z };
        // Limite "macio": encosta no fim do movimento sem quicar.
        joint.angularXLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
    }

    private void SetMuscle(ConfigurableJoint joint, float mass, float factor)
    {
        joint.slerpDrive = new JointDrive
        {
            positionSpring = muscle * mass * factor,
            positionDamper = damping * mass,
            maximumForce = float.MaxValue,
        };
    }

    private void IgnoreOwnCollisions()
    {
        Collider[] own = GetComponentsInChildren<Collider>(true);
        PrototypeThirdPersonController player = FindFirstObjectByType<PrototypeThirdPersonController>();
        Collider playerCollider = player != null ? player.GetComponent<Collider>() : null;
        foreach (Part part in parts)
        {
            foreach (Collider other in own)
            {
                if (other != part.collider)
                {
                    Physics.IgnoreCollision(part.collider, other);
                }
            }

            if (playerCollider != null)
            {
                Physics.IgnoreCollision(part.collider, playerCollider);
            }
        }
    }

    private static int LargestAxis(Vector3 v)
    {
        Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
    }

    // ---------- Liga / desliga ----------

    // floppy = física nos membros; dead = músculo frouxo.
    public void SetFloppy(bool value, bool dead = false)
    {
        if (!built)
        {
            return;
        }

        if (value && floppy && dead != limp)
        {
            limp = dead;
            foreach (Part part in parts)
            {
                SetMuscle(part.joint, part.body.mass, limp ? deadMuscle : 1f);
            }
        }

        if (value == floppy)
        {
            return;
        }

        floppy = value;
        if (floppy)
        {
            animator.enabled = false; // os ossos param onde estão e a física assume
            blendTimer = 0f;
            Vector3 velocity = rootBody.isKinematic ? Vector3.zero : rootBody.linearVelocity;
            Vector3 spin = rootBody.isKinematic ? Vector3.zero : rootBody.angularVelocity;
            limp = dead;
            foreach (Part part in parts)
            {
                SetJointActive(part.joint, true);
                SetMuscle(part.joint, part.body.mass, limp ? deadMuscle : 1f);
                part.collider.enabled = true;
                part.body.isKinematic = false;
                part.body.linearVelocity = velocity;
                part.body.angularVelocity = spin;
            }

            return;
        }

        // Volta para a animação: guarda a pose mole e mistura com a animada por um instante.
        for (int i = 0; i < parts.Count; i++)
        {
            Part part = parts[i];
            part.body.isKinematic = true;
            part.collider.enabled = false;
            SetJointActive(part.joint, false);
            part.savedRotation = part.bone.localRotation;
            part.savedPosition = part.bone.localPosition;
            parts[i] = part;
        }

        animator.enabled = true;
        blendTimer = blendBackTime;
    }

    private void LateUpdate()
    {
        if (!built || floppy || blendTimer <= 0f)
        {
            return;
        }

        blendTimer -= Time.deltaTime;
        float t = 1f - Mathf.Clamp01(blendTimer / blendBackTime);
        t = t * t * (3f - 2f * t);
        foreach (Part part in parts)
        {
            part.bone.localRotation = Quaternion.Slerp(part.savedRotation, part.bone.localRotation, t);
            part.bone.localPosition = Vector3.Lerp(part.savedPosition, part.restLocalPosition, t);
        }
    }
}
