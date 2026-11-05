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

        if (characterController.isGrounded && verticalSpeed < 0f)
        {
            verticalSpeed = groundedVerticalSpeed;
        }

        verticalSpeed += gravity * Time.deltaTime;
        Vector3 velocity = moveDirection * moveSpeed + Vector3.up * verticalSpeed;
        characterController.Move(velocity * Time.deltaTime);
    }

    private void LateUpdate()
    {
        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focusPoint = transform.position + Vector3.up * cameraFocusHeight;
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
