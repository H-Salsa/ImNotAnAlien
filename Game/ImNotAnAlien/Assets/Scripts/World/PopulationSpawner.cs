using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// População dinâmica (decisão D19).
// - Mantém por perto do jogador uma quantidade de civis e alguns policiais de patrulha.
// - Eles SURGEM a uma distância média, fora da visão (atrás de algo ou fora da tela),
//   e SOMEM quando ficam longe, fora de vista e calmos (nunca enquanto fogem, atacam ou voam).
// - Só surgem alienígenas que contam em setores que ainda têm cota; quando falta pouco,
//   surgem só os que faltam (os marcadores do HUD mostram onde estão).
// - Policiais extras por nível de procurado serão pedidos a este componente (Missão 13).
// - Mundo em blocos (Missão 16): só surgem sobre blocos carregados, e quem está num bloco
//   que vai ser descarregado some junto com ele.
[DisallowMultipleComponent]
public sealed class PopulationSpawner : MonoBehaviour
{
    public static PopulationSpawner Instance { get; private set; }

    [Header("Prefabs")]
    [SerializeField] private GameObject civilPrefab;
    [SerializeField] private GameObject policePrefab;
    [Tooltip("Cores possíveis para os civis.")]
    [SerializeField] private Material[] civilSkins;

    [Header("Quantidade por perto")]
    // Missão 19: 18 -> 30 (cidades estavam vazias). Nome novo para o valor novo valer na cena.
    [Tooltip("Civis por perto do jogador (raio de sumir).")]
    [SerializeField, Min(0)] private int nearbyCivilians = 30;
    [Tooltip("Policiais de patrulha (sem estar procurado). Contam para a cota.")]
    [SerializeField, Min(0)] private int patrolPolice = 2;

    [Header("Distâncias (m)")]
    [SerializeField, Min(5f)] private float spawnMinDistance = 40f;
    [SerializeField, Min(10f)] private float spawnMaxDistance = 80f;
    [SerializeField, Min(20f)] private float despawnDistance = 110f;
    [Tooltip("Ao começar o jogo, enche o bairro mais perto que isso (para não começar vazio).")]
    [SerializeField, Min(5f)] private float initialMinDistance = 15f;
    [Tooltip("Com estrelas de procurado, policiais novos surgem pelo menos a esta distância.")]
    [SerializeField, Min(10f)] private float wantedPoliceMinDistance = 55f;

    [Header("Ritmo")]
    [SerializeField, Min(0.1f)] private float checkInterval = 0.5f;
    [SerializeField, Min(1)] private int maxSpawnsPerCheck = 2;

    [Header("Aquáticas (Missão 20)")]
    [Tooltip("Vazio = Resources/Alienigena_Aquatica (criado por 'Preparar Alien Aquatico').")]
    [SerializeField] private GameObject aquaticPrefab;
    [Tooltip("Aliens aquáticas por perto, na água funda (não entram na conta dos civis; contam para a cota).")]
    [SerializeField, Min(0)] private int nearbyAquatics = 4;
    [Tooltip("Profundidade mínima (m) para uma aquática surgir.")]
    [SerializeField, Min(0.5f)] private float aquaticSpawnDepth = 1.0f;
    [Tooltip("De quanto em quanto tempo (s) procura água para soltar uma aquática.")]
    [SerializeField, Min(0.2f)] private float aquaticCheckInterval = 1.5f;
    private bool aquaticLoaded;
    private float aquaticTimer;

    [Header("Grupinhos (Missão 19)")]
    [Tooltip("Chance de um civil surgir com um colega do lado, já conversando.")]
    [SerializeField, Range(0f, 1f)] private float pairChance = 0.4f;

    private readonly List<AlienHealth> active = new List<AlienHealth>();
    private Transform player;
    private Camera mainCamera;
    private SectorTracker tracker;
    private float timer;
    private int serial;
    private WorldStreamer streamer;

    // Pedido extra de policiais (Missão 13: nível de procurado). Contam ou não para a cota.
    public int ExtraPolice { get; set; }
    public bool ExtraPoliceCount { get; set; } = true;

    public IReadOnlyList<AlienHealth> Active => active;

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

        if (streamer != null)
        {
            streamer.BlockUnloading -= OnBlockUnloading;
        }
    }

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;
        mainCamera = Camera.main;
        tracker = SectorTracker.Instance;

        if (player == null || tracker == null || civilPrefab == null || policePrefab == null)
        {
            Debug.LogWarning("PopulationSpawner: falta jogador, SectorTracker ou prefabs. Rode 'Preparar Populacao'.", this);
            enabled = false;
            return;
        }

        streamer = WorldStreamer.Instance;
        if (streamer != null)
        {
            streamer.BlockUnloading += OnBlockUnloading;
        }

        // Enche o bairro no começo (pode surgir à vista, a partir de uma distância menor).
        for (int i = 0; i < (nearbyCivilians + patrolPolice) * 3; i++)
        {
            if (!TrySpawnOne(true))
            {
                break;
            }
        }

        for (int i = 0; i < nearbyAquatics * 2; i++)
        {
            TrySpawnAquatic(true);
        }
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
        {
            return;
        }

        timer = checkInterval;
        Despawn();
        aquaticTimer -= checkInterval;
        if (aquaticTimer <= 0f)
        {
            aquaticTimer = aquaticCheckInterval;
            TrySpawnAquatic(false);
        }

        for (int i = 0; i < maxSpawnsPerCheck; i++)
        {
            if (!TrySpawnOne(false))
            {
                break;
            }
        }
    }

    // ---------- Surgir ----------

    private bool TrySpawnOne(bool initial)
    {
        if (tracker.IsCleared)
        {
            return false;
        }

        CountNearby(out int civilians, out int police);

        // Procurado: polícia nova só enquanto a polícia está te vendo, e sempre longe.
        // Enquanto procuram sem te ver (estrelas piscando), não surge policial: dá para se esconder.
        WantedSystem wanted = WantedSystem.Instance;
        bool wantedNow = wanted != null && wanted.Stars > 0;
        bool policeAllowed = !wantedNow || !wanted.IsSearching;

        bool spawnPolice;
        bool counts;
        if (!policeAllowed)
        {
            spawnPolice = false;
            counts = true;
            if (civilians >= nearbyCivilians)
            {
                return false;
            }
        }
        else if (police < patrolPolice)
        {
            spawnPolice = true;
            counts = true;
        }
        else if (police < patrolPolice + ExtraPolice)
        {
            spawnPolice = true;
            counts = ExtraPoliceCount;
        }
        else if (civilians < nearbyCivilians)
        {
            spawnPolice = false;
            counts = true;
        }
        else
        {
            return false; // já tem gente suficiente por perto
        }

        float minDistance = initial ? initialMinDistance : spawnMinDistance;
        if (spawnPolice && wantedNow)
        {
            minDistance = Mathf.Max(minDistance, wantedPoliceMinDistance);
        }

        if (!TryFindSpot(initial, minDistance, counts, out Vector3 spot, out int sector))
        {
            return false;
        }

        Spawn(spawnPolice, counts, spot, sector);

        // Às vezes o civil vem em dupla, já batendo papo.
        if (!spawnPolice && civilians + 1 < nearbyCivilians && Random.value < pairChance &&
            (!counts || tracker.AliveCountingIn(sector) < tracker.Sectors[sector].Remaining))
        {
            AlienHealth first = active[active.Count - 1];
            Vector2 side = Random.insideUnitCircle.normalized * 1.6f;
            if (NavMesh.SamplePosition(spot + new Vector3(side.x, 0f, side.y), out NavMeshHit near, 1f, NavMesh.AllAreas))
            {
                Spawn(false, counts, near.position, sector);
                StartCoroutine(PairChat(first, active[active.Count - 1]));
            }
        }

        return true;
    }

    private IEnumerator PairChat(AlienHealth a, AlienHealth b)
    {
        yield return null; // espera os dois "acordarem" (Start)
        if (a != null && b != null && a.TryGetComponent(out AlienBrain brainA) && b.TryGetComponent(out AlienBrain brainB))
        {
            brainA.StartConversationWith(brainB, Random.Range(10f, 25f));
        }
    }

    private void CountNearby(out int civilians, out int police)
    {
        civilians = 0;
        police = 0;
        float range = despawnDistance * despawnDistance;
        foreach (AlienHealth alien in active)
        {
            if (alien == null || alien.IsDead || (alien.transform.position - player.position).sqrMagnitude > range)
            {
                continue;
            }

            if (alien.TryGetComponent(out AlienBrain brain) && brain.AlienRole == AlienBrain.Role.Policial)
            {
                police++;
            }
            else if (brain != null && brain.IsAquatic)
            {
                // aquáticas têm a conta delas (CountAquatics)
            }
            else
            {
                civilians++;
            }
        }
    }

    private bool TryFindSpot(bool initial, float minDistance, bool counts, out Vector3 spot, out int sector)
    {
        float maxDistance = Mathf.Max(spawnMaxDistance, minDistance + 15f);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector2 direction = Random.insideUnitCircle.normalized;
            float distance = Random.Range(minDistance, maxDistance);
            Vector3 guess = player.position + new Vector3(direction.x, 0f, direction.y) * distance;

            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 point = hit.position;
            float ground = PlanetTerrain.SurfaceY(point); // Missão 20: relevo
            if (point.y < ground - 0.3f || point.y > ground + 1f)
            {
                continue; // nada de surgir no fundo do rio ou em telhados
            }

            if (streamer != null && !streamer.IsLoaded(point, 8f))
            {
                continue; // perto da borda do que está carregado
            }

            if (BaseSite.IsInsideAny(point, 12f))
            {
                continue; // bases são zona segura (Missão 19): ninguém surge lá
            }

            sector = tracker.SectorAt(point);
            if (sector < 0 || tracker.IsSectorCleared(sector))
            {
                continue;
            }

            // Não passa da cota: só surge quem ainda falta consumir neste setor.
            if (counts && tracker.AliveCountingIn(sector) >= tracker.Sectors[sector].Remaining)
            {
                continue;
            }

            if (!initial && IsVisible(point + Vector3.up * 1.2f))
            {
                continue;
            }

            spot = point;
            return true;
        }

        spot = Vector3.zero;
        sector = -1;
        return false;
    }

    // ---------- Aquáticas (Missão 20) ----------

    private GameObject AquaticPrefab
    {
        get
        {
            if (aquaticPrefab == null && !aquaticLoaded)
            {
                aquaticLoaded = true;
                aquaticPrefab = Resources.Load<GameObject>("Alienigena_Aquatica");
            }

            return aquaticPrefab;
        }
    }

    private int CountAquatics()
    {
        int count = 0;
        float range = despawnDistance * despawnDistance;
        foreach (AlienHealth alien in active)
        {
            if (alien != null && !alien.IsDead && (alien.transform.position - player.position).sqrMagnitude <= range &&
                alien.TryGetComponent(out AlienBrain brain) && brain.IsAquatic)
            {
                count++;
            }
        }

        return count;
    }

    // Solta uma aquática no fundo de uma água funda por perto (só se faltar e se ainda houver cota).
    private bool TrySpawnAquatic(bool initial)
    {
        if (tracker.IsCleared || AquaticPrefab == null || CountAquatics() >= nearbyAquatics)
        {
            return false;
        }

        float minDistance = initial ? initialMinDistance : spawnMinDistance;
        float maxDistance = Mathf.Max(spawnMaxDistance, minDistance + 15f);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 direction = Random.insideUnitCircle.normalized;
            Vector3 guess = player.position + new Vector3(direction.x, 0f, direction.y) * Random.Range(minDistance, maxDistance);
            if (AquaticAlien.WaterDepth(guess) < aquaticSpawnDepth ||
                !NavMesh.SamplePosition(guess, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                continue;
            }

            Vector3 point = hit.position;
            float ground = PlanetTerrain.SurfaceY(point);
            if (AquaticAlien.WaterDepth(point) < aquaticSpawnDepth || point.y < ground - 0.5f || point.y > ground + 1f)
            {
                continue; // tem que ser o fundo da água (não uma ponte ou cais)
            }

            if (streamer != null && !streamer.IsLoaded(point, 8f))
            {
                continue;
            }

            int sector = tracker.SectorAt(point);
            if (sector < 0 || tracker.IsSectorCleared(sector) || tracker.AliveCountingIn(sector) >= tracker.Sectors[sector].Remaining)
            {
                continue;
            }

            if (!initial && IsVisible(point + Vector3.up * 1.2f))
            {
                continue;
            }

            Spawn(false, true, point, sector, AquaticPrefab, "Aquatica");
            return true;
        }

        return false;
    }

    private void Spawn(bool police, bool counts, Vector3 spot, int sector, GameObject prefabOverride = null, string label = null)
    {
        GameObject prefab = prefabOverride != null ? prefabOverride : police ? policePrefab : civilPrefab;
        GameObject alien = Instantiate(prefab, spot + Vector3.up * 0.1f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        alien.name = $"{label ?? (police ? "Policial" : "Civil")}_{++serial:000}";

        if (!police && prefabOverride == null && civilSkins != null && civilSkins.Length > 0)
        {
            Material skin = civilSkins[Random.Range(0, civilSkins.Length)];
            SetSkin(alien.transform.Find("Corpo/Tronco"), skin);
            SetSkin(alien.transform.Find("Corpo/Cabeca"), skin);
        }

        AlienHealth health = alien.GetComponent<AlienHealth>();
        health.QuotaSector = sector;
        health.CountsForQuota = counts;
        active.Add(health);
        tracker.Track(health);
    }

    private static void SetSkin(Transform part, Material skin)
    {
        if (part != null && part.TryGetComponent(out Renderer renderer))
        {
            renderer.sharedMaterial = skin;
        }
    }

    // ---------- Motorista ejetado (trânsito) ----------

    // Um carro do trânsito foi levantado/atingido: o motorista sai voando e vira um civil de verdade.
    public void SpawnEjectedDriver(Vector3 position, Vector3 carVelocity)
    {
        if (!enabled || tracker == null || civilPrefab == null)
        {
            return;
        }

        StartCoroutine(EjectRoutine(position, carVelocity));
    }

    private IEnumerator EjectRoutine(Vector3 position, Vector3 carVelocity)
    {
        int sector = tracker.SectorAt(position);
        bool counts = sector >= 0 && !tracker.IsSectorCleared(sector) &&
                      tracker.AliveCountingIn(sector) < tracker.Sectors[sector].Remaining;
        Spawn(false, counts, position, sector);
        AlienHealth driver = active[active.Count - 1];

        yield return null; // espera o alienígena "acordar" (Start) antes de jogá-lo para o alto

        if (driver == null)
        {
            yield break;
        }

        if (driver.TryGetComponent(out AlienBrain brain))
        {
            brain.OnTelekinesisGrab();    // vira corpo físico...
            brain.OnTelekinesisRelease(); // ...e se levanta sozinho ao parar
        }

        if (driver.TryGetComponent(out Rigidbody body))
        {
            body.linearVelocity = carVelocity * 0.6f + Vector3.up * 6f + Random.insideUnitSphere * 2f;
            body.angularVelocity = Random.insideUnitSphere * 8f;
        }
    }

    // ---------- Policiais da viatura (Missão 19) ----------

    // Um policial desce da viatura (ou é ejetado dela, se "throwVelocity" vier preenchido).
    public void SpawnPoliceOfficer(Vector3 position, Quaternion rotation, Vector3? throwVelocity)
    {
        if (!enabled || tracker == null || policePrefab == null)
        {
            return;
        }

        StartCoroutine(OfficerRoutine(position, rotation, throwVelocity));
    }

    private IEnumerator OfficerRoutine(Vector3 position, Quaternion rotation, Vector3? throwVelocity)
    {
        int sector = tracker.SectorAt(position);
        bool wantedNow = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
        bool counts = sector >= 0 && !tracker.IsSectorCleared(sector) &&
                      tracker.AliveCountingIn(sector) < tracker.Sectors[sector].Remaining &&
                      (!wantedNow || ExtraPoliceCount);
        Spawn(true, counts, position, sector);
        AlienHealth officer = active[active.Count - 1];
        officer.transform.rotation = rotation;

        if (!throwVelocity.HasValue)
        {
            yield break;
        }

        yield return null; // espera o alienígena "acordar" antes de jogá-lo

        if (officer == null)
        {
            yield break;
        }

        if (officer.TryGetComponent(out AlienBrain brain))
        {
            brain.OnTelekinesisGrab();
            brain.OnTelekinesisRelease();
        }

        if (officer.TryGetComponent(out Rigidbody body))
        {
            body.linearVelocity = throwVelocity.Value;
            body.angularVelocity = Random.insideUnitSphere * 8f;
        }
    }

    // ---------- Sumir ----------

    private void Despawn()
    {
        float range = despawnDistance * despawnDistance;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            AlienHealth alien = active[i];
            if (alien == null)
            {
                active.RemoveAt(i);
                continue;
            }

            if ((alien.transform.position - player.position).sqrMagnitude < range)
            {
                continue;
            }

            // Vivo: só some se estiver calmo (passeando). Morto (corpo drenado): pode sumir.
            bool calm = alien.IsDead || alien.TryGetComponent(out AlienBrain brain) && brain.IsCalm;
            if (!calm || IsVisible(alien.transform.position + Vector3.up * 1.2f, alien.transform))
            {
                continue;
            }

            active.RemoveAt(i);
            Destroy(alien.gameObject);
        }
    }

    // Um bloco vai ser descarregado: quem está nele some agora (antes de o chão sumir).
    private void OnBlockUnloading(Rect worldArea)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            AlienHealth alien = active[i];
            if (alien == null)
            {
                active.RemoveAt(i);
                continue;
            }

            Vector3 p = alien.transform.position;
            if (!worldArea.Contains(new Vector2(p.x, p.z)))
            {
                continue;
            }

            if (alien.TryGetComponent(out AlienBrain brain) && brain.IsHeld)
            {
                continue; // está na mão do jogador
            }

            active.RemoveAt(i);
            Destroy(alien.gameObject);
        }
    }

    // Está na tela e sem nada na frente? ("target" = o próprio alienígena, que não conta como obstáculo)
    private bool IsVisible(Vector3 point, Transform target = null)
    {
        if (mainCamera == null)
        {
            return false;
        }

        Vector3 viewport = mainCamera.WorldToViewportPoint(point);
        bool onScreen = viewport.z > 0f && viewport.x > -0.1f && viewport.x < 1.1f && viewport.y > -0.1f && viewport.y < 1.1f;
        if (!onScreen)
        {
            return false;
        }

        // Algo no caminho (casa, galpão, árvore)? Então não está visível.
        if (Physics.Linecast(mainCamera.transform.position, point, out RaycastHit hit, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            Transform hitTransform = hit.collider.transform;
            return hitTransform.IsChildOf(player) || target != null && hitTransform.IsChildOf(target);
        }

        return true;
    }
}
