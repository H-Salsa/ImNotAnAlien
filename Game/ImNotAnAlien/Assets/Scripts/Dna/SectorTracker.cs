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
[DisallowMultipleComponent]
public sealed class SectorTracker : MonoBehaviour
{
    [Serializable]
    public sealed class SectorInfo
    {
        public string name = "Setor";
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

    private readonly Dictionary<int, PendingAlien> pending = new Dictionary<int, PendingAlien>();
    private readonly List<AlienHealth> tracked = new List<AlienHealth>();
    private bool singleSectorMode;
    private Transform player;
    private float cleanupTimer;

    public string RegionName => sectorName;
    public IReadOnlyList<SectorInfo> Sectors => sectors;
    public IReadOnlyList<AlienHealth> Tracked => tracked;
    public int CurrentSector { get; private set; } = -1;
    public bool IsCleared { get; private set; }
    public bool SingleSectorMode => singleSectorMode;

    public int RegionQuota
    {
        get
        {
            int total = 0;
            foreach (SectorInfo sector in sectors) total += sector.quota;
            return total;
        }
    }

    public int RegionConsumed
    {
        get
        {
            int total = 0;
            foreach (SectorInfo sector in sectors) total += Mathf.Min(sector.consumed, sector.quota);
            return total;
        }
    }

    public event Action Changed;
    public event Action<int> SectorEntered;
    public event Action<int> SectorCleared;
    public event Action RegionCleared;

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

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;

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

        Debug.Log($"Região '{sectorName}': cota {RegionQuota} em {sectors.Count} setor(es).");
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
            SectorEntered?.Invoke(sector);
            Changed?.Invoke();
        }
    }

    // ---------- Consultas ----------

    // "position" é de MUNDO; as áreas dos setores são LÓGICAS, fixas no mapa (Missão 16).
    public int SectorAt(Vector3 position)
    {
        Vector3 logical = WorldOrigin.ToLogical(position);
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
        if (saved == null)
        {
            return;
        }

        foreach (SaveData.SectorSave item in saved)
        {
            SectorInfo sector = sectors.Find(s => s.name == item.name);
            if (sector == null)
            {
                continue;
            }

            sector.consumed = Mathf.Clamp(item.consumed, 0, sector.quota);
            sector.cleared = item.cleared || sector.consumed >= sector.quota;
        }

        IsCleared = sectors.Count > 0 && sectors.TrueForAll(s => s.cleared);
        Changed?.Invoke();
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

        foreach (SectorInfo other in sectors)
        {
            if (!other.cleared)
            {
                return;
            }
        }

        // Região inteira limpa.
        IsCleared = true;
        if (!singleSectorMode && DnaInventory.Instance != null)
        {
            DnaInventory.Instance.Add(clearBonusDna);
        }

        RegionCleared?.Invoke();
        Debug.Log($"Região '{sectorName}' limpa!");
    }
}
