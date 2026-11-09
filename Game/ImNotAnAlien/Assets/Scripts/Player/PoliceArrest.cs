using UnityEngine;
using UnityEngine.InputSystem;

// Algemas (Missão 19, decisão D27). Fica no jogador (o WantedSystem coloca sozinho).
// - No nível 1, o policial primeiro te bate; só com a vida em 30% ou menos ele consegue te algemar (TryCuff).
// - Algemado: não anda nem usa poderes. Aperte ESPAÇO várias vezes para encher a barra e se soltar
//   antes do tempo acabar. A barra vai esvaziando aos poucos.
// - Soltou: o policial é empurrado longe, você fica alguns segundos sem poder ser algemado de novo
//   e resistir à prisão é crime (o calor sobe).
// - Não soltou: PRESO — perde parte do DNA, volta ao ponto inicial e o procurado zera.
[DisallowMultipleComponent]
public sealed class PoliceArrest : MonoBehaviour
{
    public static PoliceArrest Instance { get; private set; }

    [SerializeField, Min(1f)] private float timeLimit = 4.5f;
    [Tooltip("Quanto cada toque no Espaço enche a barra (1 = cheia).")]
    [SerializeField, Range(0.02f, 0.5f)] private float pressGain = 0.12f;
    [Tooltip("Quanto a barra esvazia por segundo.")]
    [SerializeField, Min(0f)] private float drainPerSecond = 0.22f;
    [Tooltip("Segundos sem poder ser algemado de novo depois de se soltar.")]
    [SerializeField, Min(0f)] private float immunityAfterEscape = 6f;
    [Tooltip("Parte do DNA perdida ao ser preso (0,25 = 25%).")]
    [SerializeField, Range(0f, 1f)] private float dnaLoss = 0.25f;
    [Tooltip("Só dá para ser algemado com a vida nesta fração ou abaixo (0,3 = 30%). Antes disso, a polícia bate.")]
    [SerializeField, Range(0f, 1f)] private float cuffHealthFraction = 0.3f;

    // Vida baixa o bastante para ser algemado.
    public bool CanBeCuffed
    {
        get
        {
            PlayerHealth health = PlayerHealth.Instance;
            return health == null || health.CurrentHealth <= health.MaxHealth * cuffHealthFraction;
        }
    }

    private AlienBrain officer;
    private float progress;
    private float timeLeft;
    private float immuneUntil;
    private GUIStyle titleStyle;
    private GUIStyle hintStyle;

    public bool IsCuffed => officer != null;
    public AlienBrain Officer => officer;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Um policial tenta algemar. Devolve true se conseguiu.
    public bool TryCuff(AlienBrain police)
    {
        PlayerHealth health = PlayerHealth.Instance;
        if (officer != null || police == null || Time.time < immuneUntil ||
            health != null && (health.IsDead || health.IsArrested) || !CanBeCuffed)
        {
            return false;
        }

        officer = police;
        progress = 0f;
        timeLeft = timeLimit;
        if (health != null)
        {
            health.SetControlsLocked(true);
        }

        CameraShake.AddTrauma(0.3f);
        GameFeel.PlayerHit(transform.position);
        return true;
    }

    private void Update()
    {
        if (officer == null)
        {
            return;
        }

        // O policial que segurava saiu de cena (morreu, foi arremessado...): você está livre.
        if (!officer.isActiveAndEnabled || officer.IsTumbling ||
            officer.TryGetComponent(out AlienHealth officerHealth) && officerHealth.IsDead)
        {
            Release(false);
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            progress += pressGain;
            CameraShake.AddTrauma(0.08f);
        }

        progress = Mathf.Max(0f, progress - drainPerSecond * Time.deltaTime);
        timeLeft -= Time.deltaTime;

        if (progress >= 1f)
        {
            Release(true);
        }
        else if (timeLeft <= 0f)
        {
            officer = null;
            PlayerHealth.Instance?.Arrest(dnaLoss);
        }
    }

    private void Release(bool escaped)
    {
        AlienBrain holder = officer;
        officer = null;
        immuneUntil = Time.time + immunityAfterEscape;
        PlayerHealth.Instance?.SetControlsLocked(false);

        if (!escaped || holder == null)
        {
            return;
        }

        // Empurra o policial para longe (ele cai e levanta, como na telecinese).
        holder.OnTelekinesisGrab();
        holder.OnTelekinesisRelease();
        if (holder.TryGetComponent(out Rigidbody body))
        {
            Vector3 away = holder.transform.position - transform.position;
            away.y = 0f;
            body.linearVelocity = away.normalized * 9f + Vector3.up * 4f;
            body.angularVelocity = Random.insideUnitSphere * 6f;
        }

        CameraShake.AddTrauma(0.5f);
        WantedSystem.ReportResistArrest(transform.position);
    }

    private void OnGUI()
    {
        bool arrested = PlayerHealth.Instance != null && PlayerHealth.Instance.IsArrested;
        if (officer == null && !arrested)
        {
            return;
        }

        float scale = Screen.height / 1080f;
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hintStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }

        titleStyle.fontSize = Mathf.RoundToInt(48 * scale);
        titleStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);
        hintStyle.fontSize = Mathf.RoundToInt(26 * scale);
        hintStyle.normal.textColor = Color.white;

        float cx = Screen.width / 2f;
        float cy = Screen.height * 0.62f;
        if (arrested)
        {
            GUI.Label(new Rect(0f, cy - 90f * scale, Screen.width, 60f * scale), "PRESO!", titleStyle);
            GUI.Label(new Rect(0f, cy - 40f * scale, Screen.width, 36f * scale),
                $"Você perdeu {Mathf.RoundToInt(dnaLoss * 100f)}% do DNA. Voltando ao ponto inicial...", hintStyle);
            return;
        }
        GUI.Label(new Rect(0f, cy - 90f * scale, Screen.width, 60f * scale), "ALGEMADO!", titleStyle);
        GUI.Label(new Rect(0f, cy - 40f * scale, Screen.width, 36f * scale),
            $"Aperte ESPAÇO várias vezes para se soltar   ({Mathf.Max(0f, timeLeft):0.0} s)", hintStyle);

        float w = 420f * scale;
        float h = 22f * scale;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(cx - w / 2f, cy + 4f * scale, w, h), Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(0.35f, 1f, 0.65f), progress);
        GUI.DrawTexture(new Rect(cx - w / 2f, cy + 4f * scale, w * Mathf.Clamp01(progress), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
