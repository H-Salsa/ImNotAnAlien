using UnityEngine;

// Liga o modelo 3D do protagonista às animações (Missão 15).
// Fica no jogador (mesmo objeto do PrototypeThirdPersonController). O modelo é um filho com Animator,
// montado pela ferramenta "ImNotAnAlien > Montar Protagonista (Missao 15)".
// Este script só OBSERVA o jogo e repassa para o Animator:
// - Speed: velocidade no chão (m/s) -> parado, andando, correndo, disparando.
// - Grounded (no chão), Swimming (água funda), Dead (derrotado).
// - Holding: a telecinese está segurando algo (braços erguidos).
// - Throw: arremesso da telecinese ou Onda de Impacto. Hit: levou dano.
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerAnimator : MonoBehaviour
{
    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");
    private static readonly int SwimmingId = Animator.StringToHash("Swimming");
    private static readonly int DeadId = Animator.StringToHash("Dead");
    private static readonly int HoldingId = Animator.StringToHash("Holding");
    private static readonly int ThrowId = Animator.StringToHash("Throw");
    private static readonly int HitId = Animator.StringToHash("Hit");
    private static readonly int JumpId = Animator.StringToHash("Jump");

    [Tooltip("Animator do modelo (filho do jogador).")]
    [SerializeField] private Animator animator;

    [Header("Movimento")]
    [Tooltip("Quão rápido a animação acompanha mudanças de velocidade.")]
    [SerializeField, Min(0.1f)] private float speedSmoothing = 10f;
    [Tooltip("Segundos fora do chão até tocar a animação de queda (evita piscar em degraus).")]
    [SerializeField, Min(0f)] private float airborneDelay = 0.2f;

    [Header("Telecinese")]
    [Tooltip("Objeto solto mais rápido que isso (m/s) conta como ARREMESSO; mais devagar, só soltou.")]
    [SerializeField, Min(0f)] private float throwSpeedThreshold = 12f;
    [Tooltip("Quanto o braço aponta para o objeto segurado (0 = só a animação, 1 = aponta direto).")]
    [SerializeField, Range(0f, 1f)] private float armAimWeight = 1f;
    [Tooltip("Quanto o tronco gira na direção do objeto segurado (1 = o peito fica de frente para ele).")]
    [SerializeField, Range(0f, 1f)] private float torsoAimAmount = 1f;
    [Tooltip("Giro máximo do tronco (graus) em relação às pernas.")]
    [SerializeField, Range(0f, 120f)] private float maxTorsoYaw = 85f;
    [Tooltip("Quanto o braço pode cruzar na frente do peito (0 = nunca passa da frente do ombro).")]
    [SerializeField, Range(0f, 1f)] private float maxArmCross = 0.15f;
    [Tooltip("Parado e segurando algo, o personagem vira de frente para o objeto nesta velocidade.")]
    [SerializeField, Min(0f)] private float faceHeldTurnSpeed = 8f;

    [Header("Tomar dano (tranco)")]
    [Tooltip("Usa a animação Hit_Chest do pacote em vez do tranco no tronco.")]
    [SerializeField] private bool useHitAnimation = false;
    [Tooltip("Graus que o tronco é jogado para trás, para longe do tiro.")]
    [SerializeField, Range(0f, 45f)] private float flinchAngle = 18f;
    [Tooltip("Quão rápido o tranco volta ao normal (maior = mais seco).")]
    [SerializeField, Min(0.5f)] private float flinchRecovery = 7f;

    [Header("Natação")]
    [Tooltip("Profundidade mínima da água (m) para nadar. Mais raso que isso, ele anda dentro do rio.")]
    [SerializeField, Min(0f)] private float minSwimDepth = 0.8f;
    [Tooltip("Enquanto nada, o 'pé' do modelo fica esta distância abaixo da superfície. Ajuste até o corpo ficar na linha da água.\n" +
             "Missão 20: mais alto que antes (era 1,1), para as costas e a cabeça ficarem fora d'água.")]
    [SerializeField] private float swimFloatDepth = 0.9f;

    private CharacterController controller;
    private PrototypeThirdPersonController mover;
    private int lastJump;
    private bool hasJumpParam;
    private float jumpGraceUntil;
    private TelekinesisPower telekinesis;
    private ShockwaveAbility shockwave;
    private PlayerHealth health;

    private float smoothedSpeed;
    private float airTime;
    private Rigidbody lastHeld;
    private float lastShockwaveCooldown;
    private float waterCheckTimer;
    private bool swimming;
    private float waterSurfaceY;
    private float modelBaseY;
    private float swimPitch;
    [Tooltip("Nadando para a frente, o modelo sobe até isto (m) para a cabeça ficar fora d'água.")]
    [SerializeField] private float swimForwardLift = 0.15f;
    [Tooltip("Nadando para a frente na superfície, o corpo inclina este ângulo (graus): cabeça para cima, pernas para baixo.")]
    [SerializeField] private float swimSurfaceTilt = 12f;
    private SwimWake wake;
    private Quaternion modelBaseRotation = Quaternion.identity;
    private readonly Collider[] waterHits = new Collider[8];

    // Ossos do esqueleto humanoide (para os ajustes feitos por cima da animação).
    private Transform spine;
    private Transform chest;
    private Transform head;
    private Transform rightUpperArm, rightLowerArm, rightHand;
    private Transform leftUpperArm, leftLowerArm, leftHand;

    private float aimWeight;
    private Vector3 aimTarget;
    private bool aimWithRightHand = true;

    private float flinchTime = 100f;
    private Vector3 flinchAway;
    private float flinchTwist;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        mover = GetComponent<PrototypeThirdPersonController>();
        telekinesis = GetComponent<TelekinesisPower>();
        shockwave = GetComponent<ShockwaveAbility>();
        health = GetComponent<PlayerHealth>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (animator == null)
        {
            Debug.LogWarning("PlayerAnimator: nenhum Animator encontrado no modelo. Rode 'ImNotAnAlien > Montar Protagonista (Missao 15)'.", this);
            enabled = false;
            return;
        }

        animator.applyRootMotion = false; // quem move o personagem é o CharacterController
        modelBaseY = animator.transform.localPosition.y;
        modelBaseRotation = animator.transform.localRotation;

        if (animator.isHuman)
        {
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            head = animator.GetBoneTransform(HumanBodyBones.Head);
            rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }
    }

    // O mundo recentralizou (Missão 16): o ponto mirado anda junto.
    private void OnOriginShifted(Vector3 delta)
    {
        aimTarget -= delta;
    }

    private void OnEnable()
    {
        WorldOrigin.Shifted += OnOriginShifted;
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            health.Respawned += OnRespawned;
        }
    }

    private void OnDisable()
    {
        WorldOrigin.Shifted -= OnOriginShifted;
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
            health.Respawned -= OnRespawned;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return; // jogo pausado
        }

        // Velocidade no chão.
        Vector3 velocity = controller.enabled ? controller.velocity : Vector3.zero;
        velocity.y = 0f;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, velocity.magnitude, 1f - Mathf.Exp(-speedSmoothing * dt));
        animator.SetFloat(SpeedId, smoothedSpeed);

        // Pulo (Missão 19): impulso na hora, e já conta como "no ar" (sem esperar a tolerância).
        bool jumped = false;
        if (mover != null && mover.JumpCount != lastJump)
        {
            lastJump = mover.JumpCount;
            jumped = true;
            jumpGraceUntil = Time.time + 0.2f; // logo depois do pulo o chão ainda "encosta"
            if (hasJumpParam || HasParameter(JumpId))
            {
                hasJumpParam = true;
                animator.SetTrigger(JumpId);
            }
        }

        // No chão / no ar (com tolerância para degraus e rampas).
        bool wasAirborne = airTime >= airborneDelay;
        airTime = jumped || Time.time < jumpGraceUntil
            ? Mathf.Max(airTime, airborneDelay + 0.01f)
            : controller.isGrounded || !controller.enabled ? 0f : airTime + dt;

        // Aterrissou: um gatilho de pulo que tenha sobrado não pode tocar o impulso depois.
        if (wasAirborne && airTime < airborneDelay && hasJumpParam)
        {
            animator.ResetTrigger(JumpId);
        }
        animator.SetBool(GroundedId, airTime < airborneDelay);

        UpdateWater(dt);
        UpdateTelekinesis();
        UpdateShockwave();
    }

    private bool HasParameter(int id)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == id)
            {
                return true;
            }
        }

        return false;
    }

    // ---------- Água ----------

    private void UpdateWater(float dt)
    {
        // Missão 20: o próprio controle do personagem nada (boia, mergulha e sobe); aqui só a animação
        // e a inclinação do corpo (cabeça para baixo descendo, para cima subindo).
        float targetY = modelBaseY;
        float targetPitch = 0f;
        if (mover != null)
        {
            swimming = mover.IsSwimming;
            if (swimming)
            {
                // Nadando para a frente (Swim_Fwd) o corpo deitado fica mais baixo que boiando parado:
                // sobe o modelo conforme a velocidade, para a cabeça e as costas ficarem para fora.
                targetY = modelBaseY + swimForwardLift * Mathf.Clamp01(smoothedSpeed / 2f);
            }

            if (swimming && mover.IsSubmerged)
            {
                Vector3 flat = controller.velocity;
                flat.y = 0f;
                targetPitch = Mathf.Clamp(Mathf.Atan2(-mover.SwimVerticalSpeed, Mathf.Max(flat.magnitude, 1.2f)) * Mathf.Rad2Deg, -55f, 55f);
            }
            else if (swimming)
            {
                // Na superfície, nadando para a frente: cabeça mais alta e pernas mais fundas (como gente nadando).
                targetPitch = -swimSurfaceTilt * Mathf.Clamp01(smoothedSpeed / 2f);
            }

            if (wake == null)
            {
                wake = SwimWake.For(transform);
            }

            wake.Feed(swimming && !mover.IsSubmerged, mover.WaterSurfaceY, controller.velocity, transform.position + transform.forward * 0.6f);
        }
        else
        {
            // Sem o controle (cena antiga): o modelo sobe até a linha da água.
            waterCheckTimer -= dt;
            if (waterCheckTimer <= 0f)
            {
                waterCheckTimer = 0.15f;
                swimming = FindWater(out waterSurfaceY);
            }

            if (swimming)
            {
                targetY = modelBaseY + (waterSurfaceY - swimFloatDepth - FeetY());
            }
        }

        animator.SetBool(SwimmingId, swimming);
        Transform model = animator.transform;
        Vector3 local = model.localPosition;
        float blend = 1f - Mathf.Exp(-6f * dt);
        local.y = Mathf.Lerp(local.y, targetY, blend);
        model.localPosition = local;
        swimPitch = Mathf.Lerp(swimPitch, targetPitch, blend);
        model.localRotation = modelBaseRotation * Quaternion.Euler(swimPitch, 0f, 0f);
    }

    private bool FindWater(out float surfaceY)
    {
        surfaceY = 0f;
        Vector3 feet = new Vector3(transform.position.x, FeetY() + 0.1f, transform.position.z);
        int count = Physics.OverlapSphereNonAlloc(feet, 0.3f, waterHits, Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (waterHits[i].GetComponent<WaterZone>() == null)
            {
                continue;
            }

            surfaceY = waterHits[i].bounds.max.y;
            return surfaceY - FeetY() >= minSwimDepth;
        }

        return false;
    }

    private float FeetY()
    {
        return transform.position.y + controller.center.y - controller.height * 0.5f;
    }

    // ---------- Poderes ----------

    private void UpdateTelekinesis()
    {
        Rigidbody held = telekinesis != null && telekinesis.enabled ? telekinesis.HeldBody : null;
        animator.SetBool(HoldingId, held != null);

        if (held != null)
        {
            aimTarget = held.worldCenterOfMass;

            // Parado: vira de frente para o objeto (andando, quem gira é o controle de movimento).
            Vector3 flat = Vector3.ProjectOnPlane(aimTarget - transform.position, Vector3.up);
            if (smoothedSpeed < 0.5f && flat.sqrMagnitude > 0.01f)
            {
                Quaternion facing = Quaternion.LookRotation(flat, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, facing, 1f - Mathf.Exp(-faceHeldTurnSpeed * Time.deltaTime));
            }
        }

        // Liga rápido ao agarrar; ao soltar desliga devagar para o gesto de arremesso aparecer.
        float wanted = held != null ? 1f : 0f;
        float rate = held != null ? 12f : 4f;
        aimWeight = Mathf.MoveTowards(aimWeight, wanted, rate * Time.deltaTime);

        // Acabou de soltar: rápido = arremesso.
        if (held == null && lastHeld != null && lastHeld.linearVelocity.magnitude >= throwSpeedThreshold)
        {
            animator.SetTrigger(ThrowId);
        }

        lastHeld = held;
    }

    private void UpdateShockwave()
    {
        if (shockwave == null)
        {
            return;
        }

        // O tempo de recarga "pulou" para cima: a onda acabou de ser usada.
        float cooldown = shockwave.CooldownRemaining;
        if (cooldown > lastShockwaveCooldown + 0.5f)
        {
            animator.SetTrigger(ThrowId);
        }

        lastShockwaveCooldown = cooldown;
    }

    // ---------- Vida ----------

    private void OnDamaged(Vector3 source)
    {
        if (useHitAnimation)
        {
            animator.SetTrigger(HitId);
            return;
        }

        flinchAway = Vector3.ProjectOnPlane(transform.position - source, Vector3.up).normalized;
        if (flinchAway == Vector3.zero)
        {
            flinchAway = -transform.forward;
        }

        flinchTwist = Random.Range(-1f, 1f);
        flinchTime = 0f;
    }

    // ---------- Ajustes por cima da animação (rodam depois do Animator) ----------

    private void LateUpdate()
    {
        if (!animator.isHuman || Time.deltaTime <= 0f)
        {
            return;
        }

        ApplyFlinch();
        ApplyAim();
    }

    // Tranco: o tronco é jogado para trás (longe do tiro) e volta, com a cabeça acompanhando.
    private void ApplyFlinch()
    {
        flinchTime += Time.deltaTime;
        const float attack = 0.05f;
        float amount = flinchTime < attack
            ? flinchTime / attack
            : Mathf.Exp(-(flinchTime - attack) * flinchRecovery);
        if (amount < 0.01f || spine == null)
        {
            return;
        }

        Vector3 axis = Vector3.Cross(Vector3.up, flinchAway);
        float angle = flinchAngle * amount;
        Rotate(spine, Quaternion.AngleAxis(angle * 0.45f, axis) * Quaternion.AngleAxis(flinchTwist * angle * 0.4f, Vector3.up));
        Rotate(chest, Quaternion.AngleAxis(angle * 0.35f, axis));
        Rotate(head, Quaternion.AngleAxis(angle * 0.5f, axis));
    }

    // Telecinese: tronco gira e a mão aponta para o objeto segurado.
    private void ApplyAim()
    {
        if (aimWeight <= 0.001f)
        {
            return;
        }

        PickRaisedArm();
        Transform upper = aimWithRightHand ? rightUpperArm : leftUpperArm;
        Transform lower = aimWithRightHand ? rightLowerArm : leftLowerArm;
        Transform hand = aimWithRightHand ? rightHand : leftHand;
        if (upper == null || lower == null || hand == null)
        {
            return;
        }

        float weight = Mathf.SmoothStep(0f, 1f, aimWeight);

        // Objeto atrás das costas: aponta menos (o braço não dobra para trás).
        Vector3 flatTarget = Vector3.ProjectOnPlane(aimTarget - transform.position, Vector3.up);
        float behind = Vector3.Angle(transform.forward, flatTarget);
        weight *= Mathf.InverseLerp(150f, 100f, behind);
        if (weight <= 0.001f)
        {
            return;
        }

        // Tronco: o peito vira na direção do objeto (as pernas seguem correndo para onde iam).
        float appliedYaw = 0f;
        if (chest != null && flatTarget.sqrMagnitude > 0.01f)
        {
            float yaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, flatTarget, Vector3.up), -maxTorsoYaw, maxTorsoYaw);
            appliedYaw = yaw * torsoAimAmount * weight;
            Rotate(spine, Quaternion.AngleAxis(appliedYaw * 0.5f, Vector3.up));
            Rotate(chest, Quaternion.AngleAxis(appliedYaw * 0.5f, Vector3.up));
        }

        // Braço: aponta para o objeto, mas sem cruzar para dentro do corpo. Se o objeto estiver
        // do outro lado, o braço para na frente do ombro (o giro do tronco cobre o resto).
        Vector3 chestRight = Quaternion.AngleAxis(appliedYaw, Vector3.up) * transform.right;
        Vector3 armTarget = LimitCross(upper.position, aimTarget, aimWithRightHand ? chestRight : -chestRight);

        float armWeight = armAimWeight * weight;
        AimBone(upper, hand.position, armTarget, armWeight);
        AimBone(lower, hand.position, armTarget, armWeight * 0.7f);
    }

    // "outward" aponta para o lado de fora do braço (direita do peito para o braço direito).
    private Vector3 LimitCross(Vector3 shoulder, Vector3 target, Vector3 outward)
    {
        Vector3 toTarget = target - shoulder;
        float distance = toTarget.magnitude;
        if (distance < 0.001f)
        {
            return target;
        }

        Vector3 direction = toTarget / distance;
        float side = Vector3.Dot(direction, outward); // negativo = cruzando o corpo
        if (side >= -maxArmCross)
        {
            return target;
        }

        direction += outward * (-maxArmCross - side);
        return shoulder + direction.normalized * distance;
    }

    // Usa só o braço que a animação já levantou: a mão mais alta em relação ao próprio ombro.
    // Só troca de braço se o outro ficar claramente mais alto (evita ficar alternando).
    private void PickRaisedArm()
    {
        if (rightHand == null || leftHand == null || rightUpperArm == null || leftUpperArm == null)
        {
            return;
        }

        float right = rightHand.position.y - rightUpperArm.position.y;
        float left = leftHand.position.y - leftUpperArm.position.y;
        if (aimWithRightHand && left > right + 0.15f)
        {
            aimWithRightHand = false;
        }
        else if (!aimWithRightHand && right > left + 0.15f)
        {
            aimWithRightHand = true;
        }
    }

    // Gira o osso para que a linha osso -> ponta passe a apontar para o alvo.
    private static void AimBone(Transform bone, Vector3 tip, Vector3 target, float weight)
    {
        Vector3 current = tip - bone.position;
        Vector3 desired = target - bone.position;
        if (current.sqrMagnitude < 0.0001f || desired.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion fix = Quaternion.FromToRotation(current, desired);
        bone.rotation = Quaternion.Slerp(Quaternion.identity, fix, weight) * bone.rotation;
    }

    private static void Rotate(Transform bone, Quaternion worldRotation)
    {
        if (bone != null)
        {
            bone.rotation = worldRotation * bone.rotation;
        }
    }

    private void OnDied()
    {
        animator.SetBool(DeadId, true);
    }

    private void OnRespawned()
    {
        animator.SetBool(DeadId, false);
        animator.ResetTrigger(HitId);
        animator.ResetTrigger(ThrowId);
    }
}
