using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// Devorar (evolução da telecinese, decisão D12).
// - Mire num alienígena FERIDO e próximo (ou segure um com a telecinese).
// - Aparece "[1] DEVORAR" em cima dele. Tecla 1 (ou botão Y/triângulo do controle):
//   o personagem puxa o alienígena e o devora, rendendo o DOBRO de DNA.
// - Também mostra uma barrinha de vida sobre o alienígena mirado.
// Começa bloqueado: é desbloqueado no menu de evoluções (Tab) com DNA (EvolutionSystem).
[DisallowMultipleComponent]
public sealed class DevourAbility : MonoBehaviour
{
    [Header("Evolução")]
    [Tooltip("Se a habilidade já foi desbloqueada. Na Missão 9 isso passa a ser comprado com DNA.")]
    [SerializeField] private bool unlocked = true;

    [Header("Referências")]
    [Tooltip("Câmera do jogo. Se ficar vazio, usa a Main Camera.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Regras")]
    [Tooltip("Distância máxima (do personagem) para devorar.")]
    [SerializeField, Min(1f)] private float devourRange = 20f;
    [Tooltip("Alcance da mira para mostrar a vida do alienígena.")]
    [SerializeField, Min(1f)] private float lookRange = 40f;
    [SerializeField, Min(1)] private int dnaMultiplier = 2;
    [Tooltip("Duração do puxão até a boca (segundos).")]
    [SerializeField, Min(0.05f)] private float pullDuration = 0.45f;

    [Header("Visual")]
    [SerializeField] private Color promptColor = new Color(1f, 0.35f, 0.3f, 1f);
    [SerializeField] private Color healthyColor = new Color(0.4f, 0.95f, 0.45f, 1f);
    [SerializeField] private Color woundedColor = new Color(1f, 0.45f, 0.2f, 1f);

    private readonly RaycastHit[] hits = new RaycastHit[16];

    private InputAction devourAction;
    private TelekinesisPower telekinesis;
    private AlienHealth looked;   // alienígena sob a mira (para a barra de vida)
    private AlienHealth target;   // alienígena que pode ser devorado agora
    private bool devouring;
    private GUIStyle promptStyle;

    public bool Unlocked
    {
        get => unlocked;
        set => unlocked = value;
    }

    // Quantas vezes o DNA normal o Devorar rende (evolução "Mordida gulosa" aumenta).
    public int DnaMultiplier
    {
        get => dnaMultiplier;
        set => dnaMultiplier = Mathf.Max(1, value);
    }

    private void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        telekinesis = GetComponent<TelekinesisPower>();
        devourAction = new InputAction("Devorar", InputActionType.Button, "<Keyboard>/1");
        devourAction.AddBinding("<Gamepad>/buttonNorth");
    }

    private void OnEnable()
    {
        devourAction.Enable();
    }

    private void OnDisable()
    {
        devourAction.Disable();
        looked = null;
        target = null;
    }

    private void OnDestroy()
    {
        devourAction.Dispose();
    }

    private void Update()
    {
        if (cameraTransform == null || devouring)
        {
            looked = null;
            target = null;
            return;
        }

        FindTargets();

        if (unlocked && target != null && Cursor.lockState == CursorLockMode.Locked &&
            devourAction.WasPressedThisFrame())
        {
            StartCoroutine(DevourRoutine(target));
        }
    }

    private void FindTargets()
    {
        looked = null;

        // 1. Um alienígena segurado pela telecinese tem prioridade.
        Rigidbody heldBody = telekinesis != null ? telekinesis.HeldBody : null;
        if (heldBody != null && heldBody.TryGetComponent(out AlienHealth heldAlien) && !heldAlien.IsDead)
        {
            looked = heldAlien;
        }
        else
        {
            // 2. Senão, o alienígena sob a mira.
            looked = RaycastAlien();
        }

        bool closeEnough = looked != null &&
                           (heldBody != null && looked.gameObject == heldBody.gameObject ||
                            Vector3.Distance(transform.position, looked.transform.position) <= devourRange);
        target = looked != null && looked.IsWounded && closeEnough ? looked : null;
    }

    private AlienHealth RaycastAlien()
    {
        Ray ray = new Ray(cameraTransform.position, cameraTransform.forward);
        int count = Physics.RaycastNonAlloc(ray, hits, lookRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        Collider nearest = null;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= best)
            {
                continue;
            }

            best = hits[i].distance;
            nearest = hits[i].collider;
        }

        if (nearest == null)
        {
            return null;
        }

        AlienHealth alien = nearest.GetComponentInParent<AlienHealth>();
        return alien != null && !alien.IsDead ? alien : null;
    }

    // ---------- Devorar ----------

    private IEnumerator DevourRoutine(AlienHealth alien)
    {
        devouring = true;

        if (telekinesis != null && telekinesis.HeldBody != null && telekinesis.HeldBody.gameObject == alien.gameObject)
        {
            telekinesis.ReleaseHeld();
        }

        Renderer skin = alien.GetComponentInChildren<Renderer>();
        Color skinColor = skin != null && skin.sharedMaterial != null ? skin.sharedMaterial.color : Color.white;

        int quotaSector = alien.QuotaSector;
        bool countsForQuota = alien.CountsForQuota;
        int dna = alien.Devour(); // para o alienígena e marca como consumido
        foreach (Collider part in alien.GetComponentsInChildren<Collider>())
        {
            part.enabled = false; // atravessa tudo enquanto é puxado
        }

        GameFeel.AlienScream(alien.transform.position + Vector3.up * 1.8f);

        Transform victim = alien.transform;
        Vector3 start = victim.position;
        Vector3 startScale = victim.localScale;
        float spin = Random.Range(540f, 900f) * (Random.value < 0.5f ? -1f : 1f);
        float elapsed = 0f;

        while (elapsed < pullDuration && victim != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / pullDuration);
            float eased = t * t; // acelera no fim: "sugado"

            Vector3 mouth = transform.position + Vector3.up * 0.6f + transform.forward * 0.5f;
            victim.position = Vector3.Lerp(start, mouth, eased);
            victim.Rotate(0f, spin * Time.deltaTime, 0f, Space.World);

            // Estica como macarrão e encolhe ao chegar.
            float shrink = 1f - eased * eased * 0.9f;
            victim.localScale = Vector3.Scale(startScale, new Vector3(1f - 0.6f * t, 1f + 0.9f * t, 1f - 0.6f * t)) * shrink;
            yield return null;
        }

        Vector3 endPosition = victim != null ? victim.position : transform.position;
        if (victim != null)
        {
            Destroy(victim.gameObject);
        }

        int total = dna * dnaMultiplier;
        if (DnaInventory.Instance != null)
        {
            DnaInventory.Instance.Add(total);
        }

        if (SectorTracker.Instance != null)
        {
            SectorTracker.Instance.ConsumeDirectly(quotaSector, countsForQuota, endPosition);
        }

        GameFeel.Devour(endPosition, skinColor);
        WantedSystem.ReportDevour(endPosition);
        devouring = false;
    }

    // ---------- Barra de vida e aviso ----------

    private void OnGUI()
    {
        if (looked == null || cameraTransform == null || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 head = looked.transform.position + Vector3.up * 3.1f;
        Vector3 screen = cam.WorldToScreenPoint(head);
        if (screen.z <= 0f)
        {
            return; // atrás da câmera
        }

        float x = screen.x;
        float y = Screen.height - screen.y;

        // Barra de vida.
        const float width = 90f;
        const float height = 9f;
        Rect back = new Rect(x - width / 2f, y, width, height);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = looked.IsWounded ? woundedColor : healthyColor;
        GUI.DrawTexture(new Rect(back.x + 1f, back.y + 1f, (width - 2f) * Mathf.Clamp01(looked.HealthFraction), height - 2f),
            Texture2D.whiteTexture);
        GUI.color = Color.white;

        // O aviso só aparece quando dá para devorar agora (ferido e ao alcance).
        if (!unlocked || target != looked)
        {
            return;
        }

        promptStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 20,
            fontStyle = FontStyle.Bold,
        };

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 10f) * 0.08f;
        promptStyle.fontSize = Mathf.RoundToInt(20 * pulse);
        promptStyle.normal.textColor = promptColor;
        const string text = "[1] DEVORAR";

        // Sombra + texto, para ler em qualquer fundo.
        Rect label = new Rect(x - 120f, y - 34f, 240f, 30f);
        Color textColor = promptStyle.normal.textColor;
        promptStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(label.x + 2f, label.y + 2f, label.width, label.height), text, promptStyle);
        promptStyle.normal.textColor = textColor;
        GUI.Label(label, text, promptStyle);
    }
}
