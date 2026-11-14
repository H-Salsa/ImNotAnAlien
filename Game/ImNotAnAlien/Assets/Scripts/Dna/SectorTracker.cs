using System;
using System.Collections.Generic;
using UnityEngine;

// Cotas da região e dos setores (decisão D19).
// - A região é dividida em setores (áreas no mapa, vistas de cima: X e Z). Cada setor tem uma COTA
//   de alienígenas a consumir e um bônus de DNA ao ser limpo. A cota da região é a soma dos setores.
// - Um alienígena conta quando o ÚLTIMO orbe de DNA dele é absorvido (ou quando é devorado),
//   e conta para o setor onde surgiu. Alienígenas marcados como "não conta" (ex.: policiais dos
//   níveis de procurado 3 a 5) dão DNA mas não entram na cota.
// - Sem setores configurados (ex.: cena Prototype), funciona como antes: um setor só, com cota
//   igual ao número de alienígenas que existem ao dar Play.
// - Planeta (Missão 19, "Planet Mode"): os setores vêm da grade do planeta — 16 regiões × 16 setores
//   de 8 × 8 blocos (veja Planet). "Região" no HUD é a região onde o jogador está. Limpar todos os
//   setores de uma região limpa a região (bônus maior).
[DisallowMultipleComponent]
public sealed class SectorTracker : MonoBehaviour
{
    [Serializable]
    public sealed class SectorInfo
    {
        public string name = "Setor";
        [HideInInspector] public string id; // planeta: identificador fixo para o jogo salvo
        [Tooltip("Área do setor vista de cima: X/Y do retângulo = X/Z do mundo.")]
        public Rect area;
        [Min(1)] public int quota = 50;
        [Min(0)] public int clearBonusDna = 40;

        [NonSerialized] public int consumed;
        [NonSerialized] public bool cleared;

        public int Remaining => Mathf.Max(0, quota - consumed);
    }

    private struct PendingAlien
    {
        public int sector;
        public bool counts;
        public int orbsLeft;
    }

    public static SectorTracker Instance { get; private set; }

    [Tooltip("Nome da região (aparece no HUD).")]
    [SerializeField] private string sectorName = "Esquina";
    [Tooltip("DNA de bônus ao limpar a REGIÃO inteira (no modo de setor único, é o bônus do setor).")]
    [SerializeField, Min(0)] private int clearBonusDna = 25;
    [SerializeField] private List<SectorInfo> sectors = new List<SectorInfo>();

    [Header("Planeta (Missão 19)")]
    [Tooltip("Setores e regiões vêm da grade do planeta (a lista acima é ignorada).")]
    [SerializeField] private bool planetMode;
    [SerializeField, Min(1)] private int planetSectorQuota = 50;
    [SerializeField, Min(0)] private int planetSectorBonus = 40;
    [SerializeField, Min(0)] private int planetRegionBonus = 400;

    private bool planetBuilt;
    private bool[] regionCleared;

    private readonly Dictionary<int, PendingAlien> pending = new Dictionary<int, PendingAlien>();
    private readonly List<AlienHealth> tracked = new List<AlienHealth>();
    private bool singleSectorMode;
    private Transform player;
    private float cleanupTimer;

    public bool PlanetMode => planetMode;

    // Região onde o jogador está (planeta). Fora do planeta (polos) = -1.
    public int CurrentRegion { get; private set; } = -1;

    public string RegionName => planetMode
        ? (CurrentRegion >= 0 ? Planet.RegionName(CurrentRegion) : "Polo")
        : sectorName;
    public IReadOnlyList<SectorInfo> Sectors => sectors;
    public IReadOnlyList<AlienHealth> Tracked => tracked;
    public int CurrentSector { get; private set; } = -1;
    private bool singleRegionCleared;

    // Região atual limpa (no planeta: a região onde o jogador está).
    public bool IsCleared => planetMode ? CurrentRegion >= 0 && regionCleared[CurrentRegion] : singleRegionCleared;
    public bool SingleSectorMode => singleSectorMode;

    public int RegionQuota => planetMode ? RegionQuotaOf(CurrentRegion) : SumQuota(0, sectors.Count);

    public int RegionConsumed => planetMode ? RegionConsumedOf(CurrentRegion) : SumConsumed(0, sectors.Count);

    public int RegionQuotaOf(int region) =>
        region < 0 ? 0 : SumQuota(region * Planet.SectorsPerRegion, Planet.SectorsPerRegion);

    public int RegionConsumedOf(int region) =>
        region < 0 ? 0 : SumConsumed(region * Planet.SectorsPerRegion, Planet.SectorsPerRegion);

    public bool IsRegionCleared(int region) => planetMode && region >= 0 && region < Planet.RegionCount && regionCleared[region];

    public string RegionNameOfSector(int sector) => planetMode ? Planet.RegionName(Planet.RegionOfSector(sector)) : sectorName;

    // Planeta inteiro (o teletransporte para casa precisa de tudo).
    public int PlanetQuota => SumQuota(0, sectors.Count);
    public int PlanetConsumed => SumConsumed(0, sectors.Count);

    private int SumQuota(int from, int count)
    {
        int total = 0;
        for (int i = from; i < from + count && i < sectors.Count; i++) total += sectors[i].quota;
        return total;
    }

    private int SumConsumed(int from, int count)
    {
        int total = 0;
        for (int i = from; i < from + count && i < sectors.Count; i++) total += Mathf.Min(sectors[i].consumed, sectors[i].quota);
        return total;
    }

    // Regiões orgânicas (Missão 19): a cota de cada setor acompanha o tamanho dele
    // (um setor médio tem 64 blocos e cota 50). O progresso salvo continua valendo; setor que já
    // tinha consumido a cota nova fica limpo (sem bônus de novo).
    private void ApplyPlanetQuotas()
    {
        if (!planetMode || !Planet.RegionsBuilt)
        {
            return;
        }

        float average = (float)(Planet.Width * Planet.Height) / Planet.SectorCount;
        for (int i = 0; i < sectors.Count; i++)
        {
            SectorInfo sector = sectors[i];
            int blocks = Planet.SectorBlockCount(i);
            sector.quota = blocks > 0 ? Mathf.Max(8, Mathf.RoundToInt(planetSectorQuota * blocks / average)) : 0;
            sector.clearBonusDna = Mathf.Max(10, Mathf.RoundToInt(planetSectorBonus * Mathf.Max(blocks, 1) / average));
            if (!sector.cleared && sector.consumed >= sector.quota)
            {
                sector.cleared = true;
            }
        }

        for (int r = 0; r < Planet.RegionCount; r++)
        {
            regionCleared[r] = AllCleared(r * Planet.SectorsPerRegion, Planet.SectorsPerRegion);
        }
    }

    // Monta os 256 setores do planeta (antes de tudo: o SaveManager devolve o progresso já no Awake dele).
    private void EnsurePlanetSectors()
    {
        if (!planetMode || planetBuilt)
        {
            return;
        }

        planetBuilt = true;
        sectors.Clear();
        for (int i = 0; i < Planet.SectorCount; i++)
        {
            sectors.Add(new SectorInfo
            {
                name = Planet.SectorName(i),
                id = Planet.SectorId(i),
                quota = planetSectorQuota,
                clearBonusDna = planetSectorBonus,
            });
        }

        regionCleared = new bool[Planet.RegionCount];
    }

    public event Action Changed;
    public event Action<int> SectorEntered;
    public event Action<int> SectorCleared;
    public event Action RegionCleared;

    private void Awake()
    {
        Instance = this;
        EnsurePlanetSectors();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;

        EnsurePlanetSectors();
        ApplyPlanetQuotas();
        if (sectors.Count == 0)
        {
            // Modo antigo: conta os alienígenas que já estão na cena.
            singleSectorMode = true;
            AlienHealth[] existing = FindObjectsByType<AlienHealth>(FindObjectsSortMode.None);
            tracked.AddRange(existing);
            sectors.Add(new SectorInfo
            {
                name = sectorName,
                area = Rect.MinMaxRect(-100000f, -100000f, 100000f, 100000f),
                quota = Mathf.Max(1, existing.Length),
                clearBonusDna = clearBonusDna,
            });
        }

        Debug.Log(planetMode
            ? $"Planeta: {Planet.RegionCount} regiões, {sectors.Count} setores, cota total {PlanetQuota} ({PlanetConsumed} já consumidos)."
            : $"Região '{sectorName}': cota {RegionQuota} em {sectors.Count} setor(es).");
        UpdateCurrentSector();
        Changed?.Invoke();
    }

    private void Update()
    {
        UpdateCurrentSector();

        cleanupTimer -= Time.deltaTime;
        if (cleanupTimer <= 0f)
        {
            cleanupTimer = 1f;
            tracked.RemoveAll(alien => alien == null);
        }
    }

    private void UpdateCurrentSector()
    {
        if (player == null)
        {
            return;
        }

        int sector = SectorAt(player.position);
        if (sector != CurrentSector)
        {
            CurrentSector = sector;
            if (planetMode)
            {
                CurrentRegion = sector >= 0 ? Planet.RegionOfSector(sector) : -1;
            }

            SectorEntered?.Invoke(sector);
            Changed?.Invoke();
        }
    }

    // ---------- Consultas ----------

    // "position" é de MUNDO; as áreas dos setores são LÓGICAS, fixas no mapa (Missão 16).
    public int SectorAt(Vector3 position)
    {
        Vector3 logical = WorldOrigin.ToLogical(position);
        if (planetMode)
        {
            return Planet.Active ? Planet.SectorAt(Planet.BlockAt(logical)) : -1;
        }

        Vector2 flat = new Vector2(logical.x, logical.z);
        for (int i = 0; i < sectors.Count; i++)
        {
            if (sectors[i].area.Contains(flat))
            {
                return i;
            }
        }

        return -1;
    }

    public bool IsSectorCleared(int sector) => sector >= 0 && sector < sectors.Count && sectors[sector].cleared;

    // Quantos alienígenas que CONTAM e estão vivos pertencem a este setor (usado pelo surgimento).
    public int AliveCountingIn(int sector)
    {
        int alive = 0;
        foreach (AlienHealth alien in tracked)
        {
            if (alien != null && !alien.IsDead && alien.CountsForQuota && SectorOf(alien) == sector)
            {
                alive++;
            }
        }

        return alive;
    }

    public int SectorOf(AlienHealth alien) => alien.QuotaSector >= 0 ? alien.QuotaSector : SectorAt(alien.transform.position);

    // Jogo salvo (Missão 17): devolve o progresso dos setores, pelo nome. Chamado antes do Start.
    public void RestoreProgress(IReadOnlyList<SaveData.SectorSave> saved)
    {
        EnsurePlanetSectors();
        if (saved == null)
        {
            return;
        }

        foreach (SaveData.SectorSave item in saved)
        {
            SectorInfo sector = sectors.Find(s => (string.IsNullOrEmpty(s.id) ? s.name : s.id) == item.name);
            if (sector == null)
            {
                continue;
            }

            sector.consumed = Mathf.Clamp(item.consumed, 0, sector.quota);
            sector.cleared = item.cleared || sector.consumed >= sector.quota;
        }

        if (planetMode)
        {
            for (int r = 0; r < Planet.RegionCount; r++)
            {
                regionCleared[r] = AllCleared(r * Planet.SectorsPerRegion, Planet.SectorsPerRegion);
            }
        }
        else
        {
            singleRegionCleared = sectors.Count > 0 && sectors.TrueForAll(s => s.cleared);
        }

        Changed?.Invoke();
    }

    private bool AllCleared(int from, int count)
    {
        for (int i = from; i < from + count && i < sectors.Count; i++)
        {
            if (!sectors[i].cleared)
            {
                return false;
            }
        }

        return true;
    }

    public void Track(AlienHealth alien)
    {
        if (alien != null && !tracked.Contains(alien))
        {
            tracked.Add(alien);
        }
    }

    // ---------- Consumo ----------

    // Chamado quando um alienígena morre e solta orbes (DnaOrbSystem).
    public void OnAlienDied(AlienHealth alien, int orbCount)
    {
        int sector = SectorOf(alien);
        if (orbCount <= 0)
        {
            MarkConsumed(sector, alien.CountsForQuota);
            return;
        }

        pending[alien.GetInstanceID()] = new PendingAlien { sector = sector, counts = alien.CountsForQuota, orbsLeft = orbCount };
    }

    public void OnOrbAbsorbed(int alienId)
    {
        if (!pending.TryGetValue(alienId, out PendingAlien info))
        {
            return;
        }

        info.orbsLeft--;
        if (info.orbsLeft > 0)
        {
            pending[alienId] = info;
            return;
        }

        pending.Remove(alienId);
        MarkConsumed(info.sector, info.counts);
    }

    // Devorar: consome na hora, sem orbes.
    public void ConsumeDirectly(int quotaSector, bool counts, Vector3 position)
    {
        MarkConsumed(quotaSector >= 0 ? quotaSector : SectorAt(position), counts);
    }

    private void MarkConsumed(int sector, bool counts)
    {
        if (!counts || sector < 0 || sector >= sectors.Count || sectors[sector].cleared)
        {
            Changed?.Invoke();
            return;
        }

        SectorInfo info = sectors[sector];
        info.consumed++;
        Changed?.Invoke();

        if (info.consumed < info.quota)
        {
            return;
        }

        // Setor limpo.
        info.cleared = true;
        if (DnaInventory.Instance != null)
        {
            DnaInventory.Instance.Add(info.clearBonusDna);
        }

        GameFeel.SectorCleared();
        SectorCleared?.Invoke(sector);
        Debug.Log($"Setor '{info.name}' limpo! +{info.clearBonusDna} DNA.");

        if (planetMode)
        {
            int region = Planet.RegionOfSector(sector);
            if (!AllCleared(region * Planet.SectorsPerRegion, Planet.SectorsPerRegion))
            {
                return;
            }

            regionCleared[region] = true;
            if (DnaInventory.Instance != null)
            {
                DnaInventory.Instance.Add(planetRegionBonus);
            }

            RegionCleared?.Invoke();
            Debug.Log($"Região '{Planet.RegionName(region)}' limpa! +{planetRegionBonus} DNA.");
            return;
        }

        foreach (SectorInfo other in sectors)
        {
            if (!other.cleared)
            {
                return;
            }
        }

        // Região inteira limpa.
        singleRegionCleared = true;
        if (!singleSectorMode && DnaInventory.Instance != null)
        {
            DnaInventory.Instance.Add(clearBonusDna);
        }

        RegionCleared?.Invoke();
        Debug.Log($"Região '{sectorName}' limpa!");
    }
}
