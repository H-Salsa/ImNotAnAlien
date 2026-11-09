using System;
using UnityEngine;

// Nível de procurado (decisão D19, níveis revistos na Missão 19 — D27), de 0 a 5 estrelas.
// - Sem estrela: ficar perto de um policial (6 m, com visão) por 4 s levanta suspeita → nível 1.
// - Crimes geram "calor", mas SÓ se houver testemunha: civil perto ou policial mais longe, com linha de visão.
// - O calor define as estrelas, mas elas sobem com calma: no máximo uma estrela a cada
//   "minSecondsBetweenStars" (a primeira sobe na hora).
// - Nível 1: polícia tenta prender (cassetete, socos, algemas). Nível 2: tiro. Nível 3: viaturas vêm
//   direto até você. Nível 4: mais policiais e capacete. Nível 5: roupa verde (exército) e dano maior.
// - Cada estrela pede mais policiais ao PopulationSpawner; policiais que surgem nos níveis 1 e 2
//   contam para a cota, nos níveis 3 a 5 não contam.
// - Para despistar: ficar um tempo sem crimes e fora da vista dos policiais. Aí o calor cai.
// - Morrer, ser preso ou limpar a região zera o nível.
[DisallowMultipleComponent]
public sealed class WantedSystem : MonoBehaviour
{
    public static WantedSystem Instance { get; private set; }

    [Header("Estrelas")]
    [Tooltip("Calor necessário para 1, 2, 3, 4 e 5 estrelas.")]
    [SerializeField] private float[] levelHeat = { 10f, 70f, 160f, 280f, 450f };
    [SerializeField, Min(1f)] private float heatCap = 500f;
    [Tooltip("Tempo mínimo (s) entre uma estrela e a próxima (a primeira sobe na hora).")]
    [SerializeField, Min(0f)] private float minSecondsBetweenStars = 25f;

    [Header("Suspeita (sem estrela)")]
    [SerializeField, Min(0.5f)] private float suspicionRadius = 6f;
    [SerializeField, Min(0.5f)] private float suspicionTime = 4f;

    [Header("Nível 5 (exército)")]
    [SerializeField, Min(1f)] private float level5DamageMultiplier = 1.6f;

    [Header("Calor por crime")]
    [SerializeField, Min(0f)] private float civilKillHeat = 10f;
    [SerializeField, Min(0f)] private float policeKillHeat = 15f;
    [SerializeField, Min(0f)] private float devourHeat = 15f;
    [SerializeField, Min(0f)] private float destructionHeat = 20f;
    [Tooltip("Fugir das algemas também é crime (resistir à prisão).")]
    [SerializeField, Min(0f)] private float resistArrestHeat = 60f;

    [Header("Testemunhas")]
    [SerializeField, Min(1f)] private float civilWitnessRadius = 25f;
    [SerializeField, Min(1f)] private float policeWitnessRadius = 40f;

    [Header("Fuga")]
    [Tooltip("Segundos sem crimes e fora da vista da polícia até o calor começar a cair.")]
    [SerializeField, Min(0f)] private float coolDownDelay = 10f;
    [SerializeField, Min(0.1f)] private float heatDecayPerSecond = 6f;
    [Tooltip("Distância em que um policial com linha de visão 'te vê' (impede o calor de cair).")]
    [SerializeField, Min(1f)] private float policeSightRadius = 30f;

    [Header("Polícia por estrela")]
    [Tooltip("Policiais extras por perto para 0, 1, 2, 3, 4 e 5 estrelas.")]
    [SerializeField] private int[] policePerLevel = { 0, 1, 2, 3, 6, 9 };
    [Tooltip("Até este nível, os policiais que surgem contam para a cota.")]
    [SerializeField, Range(0, 5)] private int maxCountingLevel = 2;

    private float heat;
    private float lastSeenTime = -100f;
    private float sightTimer;
    private float lastStarUpTime = -1000f;
    private float suspicion;
    private float suspicionTimer;
    private Transform suspiciousOfficer;
    private Camera mainCamera;
    private GUIStyle suspicionStyle;
    private Transform player;

    public int Stars { get; private set; }

    // Dano dos tiros da polícia (nível 5 = exército, mais forte).
    public static float DamageMultiplier => Instance != null && Instance.Stars >= 5 ? Instance.level5DamageMultiplier : 1f;
    public float Suspicion01 => suspicion / Mathf.Max(0.01f, suspicionTime);

    // Onde a polícia viu o jogador pela última vez (crime testemunhado ou policial com visão).
    // Policiais que não te veem vão até aqui e procuram em volta.
    public Vector3 LastKnownPosition { get; private set; }
    public float Heat => heat;

    // Procurando sem te ver: as estrelas piscam no HUD.
    public bool IsSearching => Stars > 0 && Time.time - lastSeenTime > 2f;

    // (estrelas antes, estrelas agora)
    public event Action<int, int> StarsChanged;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        WorldOrigin.Shifted += OnOriginShifted;
    }

    private void OnDisable()
    {
        WorldOrigin.Shifted -= OnOriginShifted;
    }

    // O mundo recentralizou (Missão 16): o último lugar visto anda junto.
    private void OnOriginShifted(Vector3 delta)
    {
        LastKnownPosition -= delta;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (PlayerHealth.Instance != null) PlayerHealth.Instance.Died -= ResetWanted;
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.Arrested -= ResetWanted;
        if (SectorTracker.Instance != null) SectorTracker.Instance.RegionCleared -= ResetWanted;
    }

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;

        if (PlayerHealth.Instance != null) PlayerHealth.Instance.Died += ResetWanted;
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.Arrested += ResetWanted;
        if (player != null && !player.TryGetComponent(out PoliceArrest _))
        {
            player.gameObject.AddComponent<PoliceArrest>();
        }

        mainCamera = Camera.main;
        if (SectorTracker.Instance != null) SectorTracker.Instance.RegionCleared += ResetWanted;
        ApplyToSpawner();
    }

    private void Update()
    {
        if (Stars > 0)
        {
            sightTimer -= Time.deltaTime;
            if (sightTimer <= 0f)
            {
                sightTimer = 0.5f;
                if (PoliceCanSeePlayer())
                {
                    lastSeenTime = Time.time;
                    LastKnownPosition = player.position;
                }
            }
        }

        if (heat > 0f && Time.time - lastSeenTime > coolDownDelay)
        {
            heat = Mathf.Max(0f, heat - heatDecayPerSecond * Time.deltaTime);
        }

        UpdateSuspicion();
        UpdateStars(); // também sobe estrelas que estavam esperando a cadência
    }

    // ---------- Suspeita (sem estrela) ----------

    private void UpdateSuspicion()
    {
        if (Stars > 0 || player == null || PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead)
        {
            suspicion = 0f;
            suspiciousOfficer = null;
            return;
        }

        suspicionTimer -= Time.deltaTime;
        if (suspicionTimer <= 0f)
        {
            suspicionTimer = 0.2f;
            suspiciousOfficer = NearestWatchingOfficer();
        }

        suspicion = suspiciousOfficer != null
            ? suspicion + Time.deltaTime
            : Mathf.Max(0f, suspicion - Time.deltaTime * 0.6f);

        if (suspicion >= suspicionTime)
        {
            // "Ei, você aí!": vira nível 1 na hora.
            suspicion = 0f;
            heat = Mathf.Max(heat, levelHeat[0]);
            LastKnownPosition = player.position;
            lastSeenTime = Time.time;
            if (suspiciousOfficer != null)
            {
                GameFeel.AlienScream(suspiciousOfficer.position + Vector3.up * 1.8f);
            }
        }
    }

    private Transform NearestWatchingOfficer()
    {
        SectorTracker tracker = SectorTracker.Instance;
        if (tracker == null)
        {
            return null;
        }

        Vector3 target = player.position + Vector3.up * 0.5f;
        Transform best = null;
        float bestDistance = suspicionRadius * suspicionRadius;
        foreach (AlienHealth alien in tracker.Tracked)
        {
            if (alien == null || alien.IsDead || !IsPolice(alien))
            {
                continue;
            }

            Vector3 eyes = alien.transform.position + Vector3.up * 1.8f;
            float d = (eyes - target).sqrMagnitude;
            if (d <= bestDistance && HasLineOfSight(eyes, target, alien.transform))
            {
                best = alien.transform;
                bestDistance = d;
            }
        }

        return best;
    }

    // "?" e barrinha em cima do policial que está desconfiando.
    private void OnGUI()
    {
        if (suspicion <= 0.05f || suspiciousOfficer == null || mainCamera == null)
        {
            return;
        }

        Vector3 screen = mainCamera.WorldToScreenPoint(suspiciousOfficer.position + Vector3.up * 2.6f);
        if (screen.z <= 0f)
        {
            return;
        }

        float scale = Screen.height / 1080f;
        float x = screen.x;
        float y = Screen.height - screen.y;
        if (suspicionStyle == null)
        {
            suspicionStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        suspicionStyle.fontSize = Mathf.RoundToInt(34 * scale);
        suspicionStyle.normal.textColor = Color.Lerp(new Color(1f, 0.9f, 0.3f), new Color(1f, 0.3f, 0.2f), Suspicion01);
        GUI.Label(new Rect(x - 30f * scale, y - 46f * scale, 60f * scale, 40f * scale), "?", suspicionStyle);

        float w = 60f * scale;
        float h = 7f * scale;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x - w / 2f, y - 6f * scale, w, h), Texture2D.whiteTexture);
        GUI.color = suspicionStyle.normal.textColor;
        GUI.DrawTexture(new Rect(x - w / 2f, y - 6f * scale, w * Mathf.Clamp01(Suspicion01), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // ---------- Crimes (chamados por outros scripts) ----------

    public static void ReportKill(Vector3 position, bool victimWasPolice)
    {
        if (Instance != null)
        {
            Instance.ReportCrime(position, victimWasPolice ? Instance.policeKillHeat : Instance.civilKillHeat);
        }
    }

    public static void ReportDevour(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.ReportCrime(position, Instance.devourHeat);
        }
    }

    // Fugiu das algemas: resistir à prisão (a própria polícia é testemunha).
    public static void ReportResistArrest(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.heat = Mathf.Min(Instance.heatCap, Instance.heat + Instance.resistArrestHeat);
            Instance.LastKnownPosition = position;
            Instance.lastSeenTime = Time.time;
        }
    }

    public static void ReportDestruction(Vector3 position)
    {
        if (Instance != null)
        {
            Instance.ReportCrime(position, Instance.destructionHeat);
        }
    }

    private void ReportCrime(Vector3 position, float amount)
    {
        if (amount <= 0f || !Witnessed(position))
        {
            return;
        }

        heat = Mathf.Min(heatCap, heat + amount);
        LastKnownPosition = player != null ? player.position : position;
        lastSeenTime = Time.time;
    }

    // ---------- Testemunhas ----------

    private bool Witnessed(Vector3 position)
    {
        SectorTracker tracker = SectorTracker.Instance;
        if (tracker == null)
        {
            return true; // sem lista de alienígenas: considera visto
        }

        Vector3 target = position + Vector3.up;
        foreach (AlienHealth alien in tracker.Tracked)
        {
            if (alien == null || alien.IsDead)
            {
                continue;
            }

            bool police = IsPolice(alien);
            float radius = police ? policeWitnessRadius : civilWitnessRadius;
            Vector3 eyes = alien.transform.position + Vector3.up * 1.8f;
            if ((eyes - target).sqrMagnitude <= radius * radius && HasLineOfSight(eyes, target, alien.transform))
            {
                return true;
            }
        }

        return false;
    }

    private bool PoliceCanSeePlayer()
    {
        SectorTracker tracker = SectorTracker.Instance;
        if (tracker == null || player == null)
        {
            return false;
        }

        Vector3 target = player.position + Vector3.up * 0.5f;
        foreach (AlienHealth alien in tracker.Tracked)
        {
            if (alien == null || alien.IsDead || !IsPolice(alien))
            {
                continue;
            }

            Vector3 eyes = alien.transform.position + Vector3.up * 1.8f;
            if ((eyes - target).sqrMagnitude <= policeSightRadius * policeSightRadius && HasLineOfSight(eyes, target, alien.transform))
            {
                return true;
            }
        }

        return false;
    }

    // Visão livre até perto do alvo (ignora o próprio observador e coisas a menos de 2 m do alvo,
    // como o corpo da vítima ou o objeto arremessado).
    private static bool HasLineOfSight(Vector3 from, Vector3 to, Transform observer)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        RaycastHit[] hits = Physics.RaycastAll(from, direction / distance, distance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(observer) || hit.distance > distance - 2f)
            {
                continue;
            }

            if (hit.collider.attachedRigidbody != null)
            {
                continue; // objetos soltos (caixas, alienígenas) não bloqueiam a visão
            }

            return false;
        }

        return true;
    }

    private static bool IsPolice(AlienHealth alien)
    {
        return alien.TryGetComponent(out AlienBrain brain) && brain.AlienRole == AlienBrain.Role.Policial;
    }

    // ---------- Estrelas ----------

    private void UpdateStars()
    {
        int stars = 0;
        for (int i = 0; i < levelHeat.Length; i++)
        {
            if (heat >= levelHeat[i])
            {
                stars = i + 1;
            }
        }

        // Sobe uma estrela por vez, com intervalo mínimo entre elas (a primeira sobe na hora).
        if (stars > Stars)
        {
            if (Stars > 0 && Time.time - lastStarUpTime < minSecondsBetweenStars)
            {
                return;
            }

            stars = Stars + 1;
            lastStarUpTime = Time.time;
        }

        if (stars == Stars)
        {
            return;
        }

        int previous = Stars;
        Stars = stars;
        if (stars > previous)
        {
            GameFeel.WantedUp();
        }

        ApplyToSpawner();
        StarsChanged?.Invoke(previous, stars);
    }

    private void ApplyToSpawner()
    {
        PopulationSpawner spawner = PopulationSpawner.Instance;
        if (spawner == null)
        {
            return;
        }

        spawner.ExtraPolice = policePerLevel[Mathf.Clamp(Stars, 0, policePerLevel.Length - 1)];
        spawner.ExtraPoliceCount = Stars <= maxCountingLevel;
    }

    private void ResetWanted()
    {
        heat = 0f;
        suspicion = 0f;
        UpdateStars();
    }
}
