using UnityEngine;
using UnityEngine.InputSystem;

// Telecinese básica da Versão 0.1 (fica no jogador).
// Tudo num botão só: botão direito do mouse (ou gatilho esquerdo do controle).
// - Mire com o centro da tela. O ponto fica ciano quando há algo que dá para levantar.
// - Toque rápido: levanta o alvo. Se já estiver segurando algo, solta.
// - Segurar e soltar: arremessa na direção da mira (também funciona direto no alvo:
//   aperta, levanta, solta o botão e ele já é arremessado).
[DisallowMultipleComponent]
public sealed class TelekinesisPower : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Câmera do jogo. Se ficar vazio, usa a Main Camera.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Mira")]
    [Tooltip("Distância máxima, a partir da câmera, para alcançar um alvo.")]
    [SerializeField, Min(1f)] private float maxRange = 35f;

    [Header("Segurar")]
    [Tooltip("Distância à frente do personagem, na linha da mira, em que o objeto flutua (objetos grandes ficam mais longe).")]
    [SerializeField, Min(0.5f)] private float holdDistance = 3f;
    [Tooltip("Quanto o objeto fica acima da linha da mira, para não tampar o ponto de mira.")]
    [SerializeField] private float aimLineOffset = 0.7f;
    [Tooltip("Altura do centro do personagem em relação aos pés (para o objeto não afundar no chão).")]
    [SerializeField, Min(0f)] private float playerHalfHeight = 1f;
    [Tooltip("Quão rápido objetos leves acompanham a mira.")]
    [SerializeField, Min(0.1f)] private float followStrengthLight = 12f;
    [Tooltip("Quão rápido objetos no peso máximo acompanham a mira.")]
    [SerializeField, Min(0.1f)] private float followStrengthHeavy = 3.5f;
    [SerializeField, Min(1f)] private float maxFollowSpeed = 25f;
    [Tooltip("Massa máxima (kg) que a telecinese consegue levantar.")]
    [SerializeField, Min(1f)] private float maxLiftMass = 1500f;

    [Header("Arremesso")]
    [Tooltip("Segurar o botão por mais tempo que isso (segundos) e soltar = arremessar. Menos que isso = toque.")]
    [SerializeField, Min(0.05f)] private float holdToThrowTime = 0.25f;
    [SerializeField, Min(1f)] private float throwSpeed = 28f;
    [Tooltip("Giro aleatório aplicado ao arremessar (para ficar mais caótico).")]
    [SerializeField, Min(0f)] private float throwSpin = 6f;

    [Header("Brilho do objeto segurado")]
    [SerializeField] private Color glowColor = new Color(0.55f, 0.4f, 1f, 1f);
    [SerializeField, Min(0f)] private float glowIntensity = 4f;
    [SerializeField, Min(0.1f)] private float glowRange = 5f;

    [Header("Mira na tela")]
    [SerializeField] private Color crosshairIdle = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] private Color crosshairTarget = new Color(0.25f, 0.95f, 1f, 1f);
    [SerializeField] private Color crosshairHolding = new Color(0.9f, 0.35f, 0.8f, 1f);

    private readonly RaycastHit[] hits = new RaycastHit[16];

    private InputAction telekinesisAction;
    private float pressStartTime;
    private bool pressIsActive;
    private bool grabbedThisPress;

    private Rigidbody held;
    private AlienBrain heldAlien;
    private float heldExtraDistance;
    private float heldHalfHeight;
    private Collider[] heldColliders;
    private Collider[] playerColliders;
    private float heldFollowStrength;
    private Rigidbody hovered;
    private bool cursorWasLocked;
    private GUIStyle hintStyle;
    private Light glow;

    private void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Luz que acompanha o objeto segurado (o "brilho" da telecinese).
        GameObject glowObject = new GameObject("Brilho_Telecinese");
        glowObject.transform.SetParent(transform, false);
        glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = glowColor;
        glow.intensity = glowIntensity;
        glow.range = glowRange;
        glow.shadows = LightShadows.None;
        glow.enabled = false;

        telekinesisAction = new InputAction("Telecinese", InputActionType.Button, "<Mouse>/rightButton");
        telekinesisAction.AddBinding("<Gamepad>/leftTrigger");
    }

    private void OnEnable()
    {
        telekinesisAction.Enable();
    }

    private void OnDisable()
    {
        telekinesisAction.Disable();
        pressIsActive = false;
        Release(false, Vector3.zero);
    }

    private void OnDestroy()
    {
        telekinesisAction.Dispose();
    }

    private bool IsChargingThrow =>
        held != null && pressIsActive && Time.time - pressStartTime >= holdToThrowTime;

    private void Update()
    {
        if (cameraTransform == null)
        {
            return;
        }

        // O objeto segurado pode ter sido destruído ou desativado.
        if (held != null && !held.gameObject.activeInHierarchy)
        {
            Release(false, Vector3.zero);
        }

        hovered = held == null ? FindTarget() : null;

        // O clique que trava o cursor (depois do Esc) não deve levantar nada.
        if (!cursorWasLocked)
        {
            return;
        }

        if (telekinesisAction.WasPressedThisFrame())
        {
            pressIsActive = true;
            pressStartTime = Time.time;
            grabbedThisPress = false;

            if (held == null && hovered != null)
            {
                Grab(hovered);
                grabbedThisPress = true;
            }
        }

        if (telekinesisAction.WasReleasedThisFrame() && pressIsActive)
        {
            pressIsActive = false;
            if (held == null)
            {
                return;
            }

            bool wasHold = Time.time - pressStartTime >= holdToThrowTime;
            if (wasHold)
            {
                Throw(); // segurou e soltou: arremessa
            }
            else if (!grabbedThisPress)
            {
                Release(false, held.linearVelocity * 0.3f); // toque com algo na mão: solta
            }

            // toque que acabou de levantar: o objeto continua flutuando
        }
    }

    private void LateUpdate()
    {
        cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;

        glow.enabled = held != null;
        if (held != null)
        {
            glow.transform.position = held.worldCenterOfMass;
            // Pulsa mais forte enquanto carrega o arremesso.
            float pulse = IsChargingThrow ? 1.6f + Mathf.Sin(Time.time * 30f) * 0.4f : 1f;
            glow.intensity = glowIntensity * pulse;
        }
    }

    private void FixedUpdate()
    {
        if (held == null)
        {
            return;
        }

        // O objeto flutua sobre a linha da mira: acompanha a câmera para cima e para baixo.
        Vector3 focus = transform.position + Vector3.up * 1.2f;
        float cameraToPlayer = Vector3.Distance(cameraTransform.position, focus);
        Vector3 holdPoint = cameraTransform.position
                            + cameraTransform.forward * (cameraToPlayer + holdDistance + heldExtraDistance)
                            + cameraTransform.up * aimLineOffset;

        // Nunca abaixo do chão onde o personagem pisa.
        float minHeight = transform.position.y - playerHalfHeight + heldHalfHeight + 0.2f;
        holdPoint.y = Mathf.Max(holdPoint.y, minHeight);

        Vector3 velocity = (holdPoint - held.worldCenterOfMass) * heldFollowStrength;
        held.linearVelocity = Vector3.ClampMagnitude(velocity, maxFollowSpeed);
        // Um giro lento enquanto flutua.
        held.angularVelocity = Vector3.Lerp(held.angularVelocity, new Vector3(0.4f, 1.2f, 0f), 0.1f);
    }

    // ---------- Ações ----------

    private void Grab(Rigidbody target)
    {
        held = target;
        heldAlien = target.GetComponent<AlienBrain>();

        if (heldAlien != null && heldAlien.enabled)
        {
            heldAlien.OnTelekinesisGrab();
        }

        held.isKinematic = false;
        held.useGravity = false;
        held.linearVelocity += Vector3.up * 3f; // pequeno "pulo" ao ser levantado

        // Efeitos: estilhaços ciano, som de "energia" e detector de impacto no objeto.
        if (!held.TryGetComponent(out ImpactFeedback _))
        {
            held.gameObject.AddComponent<ImpactFeedback>();
        }

        GameFeel.Lift(held.worldCenterOfMass);

        Bounds bounds = GetBounds(target);
        heldExtraDistance = bounds.extents.magnitude;
        heldHalfHeight = bounds.extents.y;

        SetIgnorePlayerCollision(true);
        heldFollowStrength = Mathf.Lerp(followStrengthLight, followStrengthHeavy, held.mass / maxLiftMass);
    }

    private void Throw()
    {
        Vector3 aimPoint = GetAimPoint();
        Vector3 direction = (aimPoint - held.worldCenterOfMass).normalized;
        GameFeel.Throw(held.worldCenterOfMass);
        Release(true, direction * throwSpeed);
    }

    private void Release(bool thrown, Vector3 velocity)
    {
        if (held == null)
        {
            return;
        }

        held.useGravity = true;
        held.linearVelocity = velocity;
        if (thrown)
        {
            held.angularVelocity = Random.insideUnitSphere * throwSpin;
        }

        if (heldAlien != null)
        {
            heldAlien.OnTelekinesisRelease();
        }

        SetIgnorePlayerCollision(false);
        held = null;
        heldAlien = null;
    }

    // ---------- Mira ----------

    private Rigidbody FindTarget()
    {
        if (!TryRaycastFromCamera(out RaycastHit hit))
        {
            return null;
        }

        Rigidbody body = hit.collider.attachedRigidbody;
        return body != null && IsLiftable(body) ? body : null;
    }

    private Vector3 GetAimPoint()
    {
        return TryRaycastFromCamera(out RaycastHit hit)
            ? hit.point
            : cameraTransform.position + cameraTransform.forward * 100f;
    }

    // Raio a partir do centro da tela, ignorando o próprio jogador e o objeto segurado.
    private bool TryRaycastFromCamera(out RaycastHit nearest)
    {
        nearest = default;
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        int count = Physics.RaycastNonAlloc(ray, hits, maxRange, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (held != null && hit.collider.attachedRigidbody == held)
            {
                continue;
            }

            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                nearest = hit;
                found = true;
            }
        }

        return found;
    }

    private bool IsLiftable(Rigidbody body)
    {
        if (body.mass > maxLiftMass)
        {
            return false;
        }

        return body.GetComponent<TelekinesisTarget>() != null || body.GetComponent<AlienHealth>() != null;
    }

    // Enquanto segurado, o objeto atravessa o próprio personagem (evita empurrões e dano falso).
    private void SetIgnorePlayerCollision(bool ignore)
    {
        playerColliders ??= GetComponentsInChildren<Collider>(true);
        if (ignore)
        {
            heldColliders = held.GetComponentsInChildren<Collider>();
        }

        if (heldColliders == null)
        {
            return;
        }

        foreach (Collider heldCollider in heldColliders)
        {
            if (heldCollider == null)
            {
                continue;
            }

            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider != null)
                {
                    Physics.IgnoreCollision(heldCollider, playerCollider, ignore);
                }
            }
        }

        if (!ignore)
        {
            heldColliders = null;
        }
    }

    private static Bounds GetBounds(Rigidbody body)
    {
        Collider[] colliders = body.GetComponentsInChildren<Collider>();
        Bounds bounds = new Bounds(body.worldCenterOfMass, Vector3.zero);
        foreach (Collider collider in colliders)
        {
            if (collider.enabled)
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    // ---------- Mira na tela (provisória) ----------

    private void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Color color = held != null ? crosshairHolding : hovered != null ? crosshairTarget : crosshairIdle;
        float size = IsChargingThrow ? 16f : held != null || hovered != null ? 10f : 6f;
        GUI.color = color;
        GUI.DrawTexture(new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f, size, size),
            Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (held != null)
        {
            hintStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 16,
                normal = { textColor = Color.white },
            };
            GUI.Label(new Rect(0f, Screen.height - 60f, Screen.width, 30f),
                IsChargingThrow
                    ? "Solte para arremessar!"
                    : "Toque (botão direito): soltar   |   Segure e solte: arremessar",
                hintStyle);
        }
    }
}
