using System;
using UnityEngine;

// Nível de procurado (decisão D19), de 0 a 5 estrelas.
// - Crimes geram "calor", mas SÓ se houver testemunha: civil perto ou policial mais longe, com linha de visão.
// - O calor define as estrelas. Cada estrela pede mais policiais ao PopulationSpawner; policiais
//   que surgem nos níveis 1 e 2 contam para a cota, nos níveis 3 a 5 não contam.
// - Para despistar: ficar um tempo sem crimes e fora da vista dos policiais. Aí o calor cai.
// - Morrer ou limpar a região zera o nível.
[DisallowMultipleComponent]
public sealed class WantedSystem : MonoBehaviour
{
    public static WantedSystem Instance { get; private set; }

    [Header("Estrelas")]
    [Tooltip("Calor necessário para 1, 2, 3, 4 e 5 estrelas.")]
    [SerializeField] private float[] starThresholds = { 10f, 40f, 90f, 160f, 250f };
    [SerializeField, Min(1f)] private float maxHeat = 300f;

    [Header("Calor por crime")]
    [SerializeField, Min(0f)] private float civilKillHeat = 10f;
    [SerializeField, Min(0f)] private float policeKillHeat = 15f;
    [SerializeField, Min(0f)] private float devourHeat = 15f;
    [SerializeField, Min(0f)] private float destructionHeat = 20f;

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
    [SerializeField] private int[] extraPolicePerStar = { 0, 1, 3, 5, 7, 10 };
    [Tooltip("Até este nível, os policiais que surgem contam para a cota.")]
    [SerializeField, Range(0, 5)] private int maxCountingLevel = 2;

    private float heat;
    private float lastSeenTime = -100f;
    private float sightTimer;
    private Transform player;

    public int Stars { get; private set; }

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
        if (SectorTracker.Instance != null) SectorTracker.Instance.RegionCleared -= ResetWanted;
    }

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;

        if (PlayerHealth.Instance != null) PlayerHealth.Instance.Died += ResetWanted;
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
            UpdateStars();
        }
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

        heat = Mathf.Min(maxHeat, heat + amount);
        LastKnownPosition = player != null ? player.position : position;
        lastSeenTime = Time.time;
        UpdateStars();
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
        for (int i = 0; i < starThresholds.Length; i++)
        {
            if (heat >= starThresholds[i])
            {
                stars = i + 1;
            }
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

        spawner.ExtraPolice = extraPolicePerStar[Mathf.Clamp(Stars, 0, extraPolicePerStar.Length - 1)];
        spawner.ExtraPoliceCount = Stars <= maxCountingLevel;
    }

    private void ResetWanted()
    {
        heat = 0f;
        UpdateStars();
    }
}
