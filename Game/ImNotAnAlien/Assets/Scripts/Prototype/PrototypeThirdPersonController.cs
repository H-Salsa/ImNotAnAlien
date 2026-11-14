using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PrototypeThirdPersonController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 4.5f;
    [SerializeField, Min(0f)] private float turnSpeed = 12f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float groundedVerticalSpeed = -2f;

    [Header("Pulo (Espaço)")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.4f;
    [Tooltip("Ainda dá para pular logo depois de sair da beirada (s).")]
    [SerializeField, Min(0f)] private float coyoteTime = 0.12f;

    [Header("Corrida (Shift) e fôlego")]
    [SerializeField, Min(1f)] private float sprintMultiplier = 1.65f;
    [SerializeField, Min(1f)] private float maxStamina = 100f;
    [Tooltip("Fôlego gasto por segundo correndo.")]
    [SerializeField, Min(0f)] private float sprintDrain = 22f;
    [Tooltip("Fôlego recuperado por segundo sem correr.")]
    [SerializeField, Min(0f)] private float staminaRegen = 18f;
    [Tooltip("Segundos sem correr até o fôlego começar a voltar.")]
    [SerializeField, Min(0f)] private float regenDelay = 0.8f;
    [Tooltip("Fôlego acabou: só volta a correr com esta fração recuperada.")]
    [SerializeField, Range(0f, 1f)] private float exhaustedRecover = 0.3f;

    [Header("Natação (Missão 20)")]
    [Tooltip("Profundidade da água (superfície até o fundo) a partir da qual ele nada.")]
    [SerializeField, Min(0.5f)] private float swimDepthToFloat = 1.3f;
    [Tooltip("Boiando, a raiz do modelo (o 'pé' do objeto) fica esta distância abaixo da superfície.\n" +
             "As animações de nado já descem o corpo: no Swim_Idle a cabeça fica ~0,3 m ACIMA da raiz e o\n" +
             "quadril ~0,4 m abaixo; no Swim_Fwd o corpo fica deitado na altura da raiz. Por isso ~0.")]
    [SerializeField] private float swimRootBelowSurface = 0.05f;
    [Tooltip("Nadando, a câmera mira mais baixo (o corpo fica na linha da água).")]
    [SerializeField] private float swimCameraDrop = 1.4f;
    [SerializeField, Min(0f)] private float swimSpeed = 3.2f;
    [Tooltip("Velocidade para descer (Ctrl/C) e subir (Espaço).")]
    [SerializeField, Min(0f)] private float diveSpeed = 2.6f;
    [Tooltip("Velocidade com que volta sozinho para a superfície (empuxo).")]
    [SerializeField, Min(0f)] private float buoyancy = 0.7f;

    [Header("Camera")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField, Min(0f)] private float lookSensitivity = 0.1f;
    [SerializeField, Min(0.1f)] private float cameraDistance = 6f;
    [SerializeField, Min(0f)] private float cameraFocusHeight = 1.2f;
    [Tooltip("Ângulo mínimo da câmera. Valores negativos permitem olhar para cima.")]
    [SerializeField] private float minPitch = -35f;
    [Tooltip("Ângulo máximo da câmera (olhando de cima para baixo).")]
    [SerializeField] private float maxPitch = 65f;

    [Header("Camera Collision")]
    [Tooltip("Raio da esfera usada para impedir que a câmera atravesse chão e paredes.")]
    [SerializeField, Min(0.01f)] private float cameraCollisionRadius = 0.25f;
    [Tooltip("Distância mínima entre a câmera e o personagem quando ela é empurrada por um obstáculo.")]
    [SerializeField, Min(0.1f)] private float minCameraDistance = 0.8f;

    private readonly RaycastHit[] cameraHits = new RaycastHit[8];

    [Header("Cursor")]
    [Tooltip("Trava e esconde o cursor durante o jogo. Esc libera; clique na tela para travar de novo.")]
    [SerializeField] private bool lockCursor = true;

    // Multiplicador de velocidade (ex.: 0,5 dentro da água). 1 = normal.
    public float SpeedMultiplier { get; set; } = 1f;

    public float Stamina { get; private set; }
    public float Stamina01 => Stamina / maxStamina;
    public bool IsSprinting { get; private set; }
    public bool IsExhausted { get; private set; }

    // Sobe a cada pulo (o PlayerAnimator toca o impulso quando muda).
    public int JumpCount { get; private set; }

    // Natação (Missão 20).
    public bool IsSwimming { get; private set; }
    public bool IsSubmerged { get; private set; }      // cabeça debaixo d'água
    public float WaterSurfaceY { get; private set; }
    public float SwimVerticalSpeed { get; private set; }
    private static readonly Collider[] waterProbe = new Collider[8];
    private float noSwimUntil;
    private float waterCheckTimer;
    private bool inWater;
    private float waterDepth;
    private float cameraDrop;

    private float lastGroundedTime;
    private float lastSprintTime = -10f;
    private float staminaShownAlpha;

    private CharacterController characterController;
    private CapsuleCollider primitiveCollider;
    private InputActionMap playerActionMap;
    private InputAction moveAction;
    private InputAction lookAction;
    private float verticalSpeed;
    private float yaw;
    private float pitch;

    private void Awake()
    {
        Stamina = maxStamina;
        characterController = GetComponent<CharacterController>();
        characterController.height = 2f;
        characterController.radius = 0.5f;
        characterController.center = Vector3.zero;
        characterController.stepOffset = 0.3f;
        characterController.slopeLimit = 45f;

        if (TryGetComponent(out primitiveCollider))
        {
            primitiveCollider.enabled = false;
        }

        // Missão 20: ar debaixo d'água.
        if (Application.isPlaying && !TryGetComponent(out PlayerBreath _))
        {
            gameObject.AddComponent<PlayerBreath>();
        }
    }

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (inputActions == null)
        {
            DisableWithError("Input Actions asset is not assigned.");
            return;
        }

        playerActionMap = inputActions.FindActionMap("Player", throwIfNotFound: false);
        if (playerActionMap == null)
        {
            DisableWithError("Input Actions asset does not contain the 'Player' action map.");
            return;
        }

        moveAction = playerActionMap.FindAction("Move", throwIfNotFound: false);
        if (moveAction == null)
        {
            DisableWithError("The 'Player' action map does not contain the 'Move' action.");
            return;
        }

        lookAction = playerActionMap.FindAction("Look", throwIfNotFound: false);
        if (lookAction == null)
        {
            DisableWithError("The 'Player' action map does not contain the 'Look' action.");
            return;
        }

        if (cameraTransform == null)
        {
            DisableWithError("Main Camera is not assigned.");
            return;
        }

        Vector3 cameraAngles = cameraTransform.eulerAngles;
        yaw = cameraAngles.y;
        pitch = Mathf.Clamp(NormalizeAngle(cameraAngles.x), minPitch, maxPitch);
        playerActionMap.Enable();
        SetCursorLocked(lockCursor);
    }

    private void OnDisable()
    {
        if (playerActionMap != null)
        {
            playerActionMap.Disable();
        }

        SetCursorLocked(false);
    }

    private void Update()
    {
        // Menu aberto (jogo pausado): não mexe no cursor nem na câmera.
        if (GamePause.IsPaused)
        {
            return;
        }

        UpdateCursorLock();

        // A câmera só gira com o mouse enquanto o cursor está travado no jogo.
        if (!lockCursor || Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 lookInput = lookAction.ReadValue<Vector2>();
            yaw += lookInput.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - lookInput.y * lookSensitivity, minPitch, maxPitch);
        }

        Vector2 moveInput = moveAction.ReadValue<Vector2>();
        Vector3 cameraForward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        Vector3 moveDirection = cameraForward * moveInput.y + cameraRight * moveInput.x;
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.deltaTime);
        }

        UpdateWaterState();
        if (IsSwimming)
        {
            Swim(moveDirection);
            return;
        }

        if (characterController.isGrounded)
        {
            lastGroundedTime = Time.time;
            if (verticalSpeed < 0f)
            {
                verticalSpeed = groundedVerticalSpeed;
            }
        }

        Keyboard keyboard = Keyboard.current;

        // Pulo: Espaço no chão (ou logo depois de sair da beirada).
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && Time.time - lastGroundedTime <= coyoteTime && verticalSpeed <= 0.5f)
        {
            verticalSpeed = Mathf.Sqrt(2f * jumpHeight * -gravity);
            lastGroundedTime = -10f; // um pulo por vez
            JumpCount++;
        }

        // Corrida: Shift segurado andando, enquanto houver fôlego.
        bool wantsSprint = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) &&
                           moveDirection.sqrMagnitude > 0.1f;
        IsSprinting = wantsSprint && !IsExhausted && Stamina > 0f;
        if (IsSprinting)
        {
            Stamina = Mathf.Max(0f, Stamina - sprintDrain * Time.deltaTime);
            lastSprintTime = Time.time;
            if (Stamina <= 0f)
            {
                IsExhausted = true; // acabou o fôlego: precisa recuperar um pouco
            }
        }
        else if (Time.time - lastSprintTime >= regenDelay)
        {
            Stamina = Mathf.Min(maxStamina, Stamina + staminaRegen * Time.deltaTime);
        }

        if (IsExhausted && Stamina >= maxStamina * exhaustedRecover)
        {
            IsExhausted = false;
        }

        verticalSpeed += gravity * Time.deltaTime;
        float speedNow = moveSpeed * SpeedMultiplier * (IsSprinting ? sprintMultiplier : 1f);
        Vector3 velocity = moveDirection * speedNow + Vector3.up * verticalSpeed;
        characterController.Move(velocity * Time.deltaTime);
    }

    // ---------- Natação (Missão 20) ----------

    private float FeetY => transform.position.y + characterController.center.y - characterController.height * 0.5f;

    // Água debaixo dos pés (caixa WaterZone) e quão funda ela é ali.
    private void UpdateWaterState()
    {
        waterCheckTimer -= Time.deltaTime;
        if (waterCheckTimer <= 0f)
        {
            waterCheckTimer = 0.1f;
            inWater = false;
            Vector3 probe = new Vector3(transform.position.x, FeetY + 0.3f, transform.position.z);
            int count = Physics.OverlapSphereNonAlloc(probe, 0.35f, waterProbe, Physics.AllLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                if (waterProbe[i] != null && waterProbe[i].GetComponent<WaterZone>() != null)
                {
                    inWater = true;
                    WaterSurfaceY = waterProbe[i].bounds.max.y;
                    break;
                }
            }

            waterDepth = 0f;
            if (inWater)
            {
                // Fundo: raio para baixo a partir da superfície (ignora gatilhos e o próprio corpo).
                Vector3 from = new Vector3(transform.position.x, WaterSurfaceY + 0.1f, transform.position.z);
                float bottom = WaterSurfaceY - 30f;
                foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider.transform.IsChildOf(transform) && hit.collider.attachedRigidbody == null)
                    {
                        bottom = Mathf.Max(bottom, hit.point.y);
                    }
                }

                waterDepth = WaterSurfaceY - bottom;
            }
        }

        bool deep = inWater && waterDepth >= swimDepthToFloat;
        if (!IsSwimming)
        {
            // Entra nadando quando a água chega no peito (aí o empuxo levanta até a superfície).
            if (deep && Time.time >= noSwimUntil && FeetY < WaterSurfaceY - 1.0f)
            {
                IsSwimming = true;
                verticalSpeed = 0f;
            }
        }
        else if (!inWater || waterDepth < swimDepthToFloat - 0.3f)
        {
            IsSwimming = false; // chegou no raso: volta a andar
        }

        IsSubmerged = IsSwimming && FeetY + 0.35f < WaterSurfaceY - 0.05f; // cabeça (~0,3 m acima da raiz nadando) debaixo d'água
    }

    // Nadando: sem gravidade. Espaço sobe (na superfície: impulso para sair na margem), Ctrl/C mergulha;
    // solto, o empuxo traz de volta à superfície devagar.
    private void Swim(Vector3 moveDirection)
    {
        Keyboard keyboard = Keyboard.current;
        bool up = keyboard != null && keyboard.spaceKey.isPressed;
        bool down = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed);
        float floatFeet = WaterSurfaceY - swimRootBelowSurface;
        float feet = FeetY;
        bool atSurface = feet >= floatFeet - 0.15f;

        if (atSurface && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            // Pulo para fora d'água (sobe a margem).
            IsSwimming = false;
            noSwimUntil = Time.time + 0.6f;
            verticalSpeed = Mathf.Sqrt(2f * jumpHeight * 0.8f * -gravity);
            lastGroundedTime = -10f;
            JumpCount++;
            characterController.Move(Vector3.up * verticalSpeed * Time.deltaTime);
            return;
        }

        float vy;
        if (down)
        {
            vy = -diveSpeed;
        }
        else if (up && !atSurface)
        {
            vy = diveSpeed;
        }
        else
        {
            vy = feet < floatFeet - 0.05f ? buoyancy : 0f;
        }

        // Nunca passa da linha da água para cima (só boiando).
        float maxUp = (floatFeet - feet) / Mathf.Max(Time.deltaTime, 0.0001f);
        if (vy > maxUp)
        {
            vy = Mathf.Max(maxUp, -diveSpeed);
        }

        SwimVerticalSpeed = vy;
        IsSprinting = false;
        bool wantsSprint = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) && moveDirection.sqrMagnitude > 0.1f;
        if (wantsSprint && !IsExhausted && Stamina > 0f)
        {
            IsSprinting = true;
            Stamina = Mathf.Max(0f, Stamina - sprintDrain * Time.deltaTime);
            lastSprintTime = Time.time;
            if (Stamina <= 0f) IsExhausted = true;
        }
        else if (Time.time - lastSprintTime >= regenDelay)
        {
            Stamina = Mathf.Min(maxStamina, Stamina + staminaRegen * Time.deltaTime);
        }

        if (IsExhausted && Stamina >= maxStamina * exhaustedRecover)
        {
            IsExhausted = false;
        }

        float speed = swimSpeed * (IsSprinting ? 1.5f : 1f);
        Vector3 velocity = moveDirection * speed + Vector3.up * vy;
        characterController.Move(velocity * Time.deltaTime);
    }

    // Barra de fôlego: aparece embaixo, no meio, só quando não está cheia.
    private void OnGUI()
    {
        bool show = Stamina < maxStamina - 0.01f;
        staminaShownAlpha = Mathf.MoveTowards(staminaShownAlpha, show ? 1f : 0f, Time.unscaledDeltaTime * 3f);
        if (staminaShownAlpha <= 0.01f || GamePause.IsPaused)
        {
            return;
        }

        float scale = Screen.height / 1080f;
        float width = 260f * scale;
        float height = 10f * scale;
        Rect back = new Rect((Screen.width - width) / 2f, Screen.height - 120f * scale, width, height);
        GUI.color = new Color(0f, 0f, 0f, 0.55f * staminaShownAlpha);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        Color fill = IsExhausted ? new Color(1f, 0.4f, 0.3f) : new Color(0.55f, 0.95f, 0.45f);
        fill.a = staminaShownAlpha;
        GUI.color = fill;
        GUI.DrawTexture(new Rect(back.x, back.y, width * Stamina01, height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // Gira a câmera junto (ex.: ao atravessar o polo do planeta, Missão 19).
    public void AddYaw(float degrees)
    {
        yaw += degrees;
    }

    private void LateUpdate()
    {
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        cameraDrop = Mathf.Lerp(cameraDrop, IsSwimming ? swimCameraDrop : 0f, 1f - Mathf.Exp(-4f * Time.deltaTime));
        Vector3 focusPoint = transform.position + Vector3.up * (cameraFocusHeight - cameraDrop);
        Vector3 backDirection = orbitRotation * Vector3.back;
        float distance = GetUnblockedCameraDistance(focusPoint, backDirection);

        cameraTransform.SetPositionAndRotation(
            focusPoint + backDirection * distance,
            orbitRotation);
    }

    // Aproxima a câmera do personagem quando há chão ou parede entre os dois.
    private float GetUnblockedCameraDistance(Vector3 focusPoint, Vector3 backDirection)
    {
        int hitCount = Physics.SphereCastNonAlloc(
            focusPoint,
            cameraCollisionRadius,
            backDirection,
            cameraHits,
            cameraDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float distance = cameraDistance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = cameraHits[i];
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue; // ignora o próprio personagem
            }

            if (hit.collider.attachedRigidbody != null)
            {
                continue; // ignora objetos que se movem (caixas, alienígenas), senão a câmera "pula"
            }

            distance = Mathf.Min(distance, hit.distance);
        }

        return Mathf.Max(distance, minCameraDistance);
    }

    private void UpdateCursorLock()
    {
        if (!lockCursor)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
        }
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            SetCursorLocked(true);
        }
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void DisableWithError(string message)
    {
        Debug.LogError($"{nameof(PrototypeThirdPersonController)} on '{name}' could not start: {message}", this);
        enabled = false;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
