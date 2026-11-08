using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Carrega o mundo sob demanda (Missão 16, decisão D21).
// - O mapa é uma grade de blocos (prefabs), cada um com posição fixa na grade.
// - Blocos perto do jogador são carregados; os que ficam longe são descarregados.
//   Há uma folga entre as duas distâncias para um bloco não ficar entrando e saindo.
// - Ao carregar ou descarregar, o NavMesh (WorldNavMesh) é refeito em segundo plano.
// - Ao descarregar, avisa a população e o trânsito (BlockUnloading) para tirarem quem está lá,
//   e tudo do bloco some: ao voltar, casas e carros estão novos (decisão D23).
//   Um objeto com física que foi arremessado para dentro de outro bloco carregado muda de dono
//   e continua lá.
// - Névoa leve esconde a borda do que está carregado.
//
// Teste: F8 liga o MODO TESTE (raio pequeno e sem névoa), para ver os blocos entrando e saindo.
[DefaultExecutionOrder(-860)]
[DisallowMultipleComponent]
public sealed class WorldStreamer : MonoBehaviour
{
    [Serializable]
    public struct BlockEntry
    {
        public Vector2Int coord;
        public GameObject prefab;
    }

    public static WorldStreamer Instance { get; private set; }

    [Header("Grade")]
    [SerializeField, Min(10f)] private float blockSize = 100f;
    [Tooltip("Canto sudoeste do bloco (0, 0), em coordenadas LÓGICAS (X, Z).")]
    [SerializeField] private Vector2 gridOrigin = new Vector2(-60f, -240f);
    [SerializeField] private List<BlockEntry> blocks = new List<BlockEntry>();

    [Header("Distâncias (m, do jogador até a borda do bloco)")]
    [SerializeField, Min(10f)] private float loadRadius = 170f;
    [SerializeField, Min(10f)] private float unloadRadius = 200f;

    [Header("Modo teste (F8)")]
    [SerializeField] private bool testKey = true;
    [SerializeField, Min(1f)] private float testLoadRadius = 25f;
    [SerializeField, Min(1f)] private float testUnloadRadius = 40f;

    [Header("Ritmo")]
    [SerializeField, Min(0.05f)] private float checkInterval = 0.2f;
    [Tooltip("Quantos blocos podem ser carregados por verificação (espalha o trabalho).")]
    [SerializeField, Min(1)] private int maxLoadsPerCheck = 1;

    [Header("Queda no vazio")]
    [Tooltip("Objetos com física abaixo desta altura (caíram fora do que está carregado) são apagados.")]
    [SerializeField] private float killHeight = -40f;

    [Header("Névoa")]
    [SerializeField] private bool useFog = true;
    [SerializeField] private Color fogColor = new Color(0.72f, 0.80f, 0.88f, 1f);
    [SerializeField, Min(0f)] private float fogStart = 70f;
    [SerializeField, Min(1f)] private float fogEnd = 165f;

    private readonly Dictionary<Vector2Int, BlockEntry> entries = new Dictionary<Vector2Int, BlockEntry>();
    private readonly Dictionary<Vector2Int, WorldBlock> loaded = new Dictionary<Vector2Int, WorldBlock>();
    private readonly List<Vector2Int> scratch = new List<Vector2Int>();

    private Transform player;
    private TelekinesisPower telekinesis;
    private WorldNavMesh navMesh;
    private float timer;
    private float killTimer;
    private bool testMode;

    // Um bloco vai sair: retângulo dele no MUNDO (X/Z). Quem tem gente ou carros lá tira agora.
    public event Action<Rect> BlockUnloading;
    public event Action<WorldBlock> BlockLoaded;

    public IEnumerable<WorldBlock> LoadedBlocks => loaded.Values;
    public int LoadedCount => loaded.Count;
    public float BlockSize => blockSize;
    public bool TestMode => testMode;

    private float LoadRadius => testMode ? testLoadRadius : loadRadius;
    private float UnloadRadius => testMode ? Mathf.Max(testUnloadRadius, testLoadRadius + 5f) : Mathf.Max(unloadRadius, loadRadius + 5f);

    private void Awake()
    {
        Instance = this;
        foreach (BlockEntry entry in blocks)
        {
            if (entry.prefab != null)
            {
                entries[entry.coord] = entry;
            }
        }

        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;
        telekinesis = controller != null ? controller.GetComponent<TelekinesisPower>() : null;

        navMesh = GetComponent<WorldNavMesh>();
        if (navMesh == null)
        {
            navMesh = gameObject.AddComponent<WorldNavMesh>();
        }

        navMesh.EnsureInitialized();
        ApplyFog();

        if (player == null)
        {
            Debug.LogWarning("WorldStreamer: jogador não encontrado; carregando todos os blocos.", this);
        }

        // Começo do jogo: carrega tudo o que está perto, na hora, e monta o NavMesh antes de
        // a população começar a surgir.
        foreach (Vector2Int coord in entries.Keys)
        {
            if (player == null || DistanceTo(coord) < LoadRadius)
            {
                Load(coord, false);
            }
        }

        navMesh.BuildNow();
        Debug.Log($"Mundo em blocos: {loaded.Count} de {entries.Count} blocos carregados no início.");
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (testKey && keyboard != null && keyboard.f8Key.wasPressedThisFrame)
        {
            testMode = !testMode;
            ApplyFog();
            Debug.Log(testMode
                ? $"Modo teste dos blocos LIGADO: carrega a {testLoadRadius:0} m, descarrega a {testUnloadRadius:0} m, sem névoa."
                : "Modo teste dos blocos desligado.");
        }

        // O bloco onde o jogador está nunca pode faltar (ex.: ao renascer longe).
        Vector2Int here = CoordAt(WorldOrigin.ToLogical(player.position));
        if (entries.ContainsKey(here) && !loaded.ContainsKey(here))
        {
            Load(here, true);
        }

        killTimer -= Time.deltaTime;
        if (killTimer <= 0f)
        {
            killTimer = 2f;
            RemoveFallen();
        }

        timer -= Time.deltaTime;
        if (timer > 0f)
        {
            return;
        }

        timer = checkInterval;
        UpdateBlocks();
    }

    // ---------- Carregar e descarregar ----------

    private void UpdateBlocks()
    {
        // Saem os que ficaram longe.
        scratch.Clear();
        foreach (Vector2Int coord in loaded.Keys)
        {
            if (DistanceTo(coord) > UnloadRadius)
            {
                scratch.Add(coord);
            }
        }

        foreach (Vector2Int coord in scratch)
        {
            Unload(coord);
        }

        // Entram os que ficaram perto, do mais próximo para o mais longe.
        scratch.Clear();
        foreach (Vector2Int coord in entries.Keys)
        {
            if (!loaded.ContainsKey(coord) && DistanceTo(coord) < LoadRadius)
            {
                scratch.Add(coord);
            }
        }

        scratch.Sort((a, b) => DistanceTo(a).CompareTo(DistanceTo(b)));
        for (int i = 0; i < scratch.Count && i < maxLoadsPerCheck; i++)
        {
            Load(scratch[i], true);
        }
    }

    private void Load(Vector2Int coord, bool rebuildNavMesh)
    {
        BlockEntry entry = entries[coord];
        Rect rect = BlockRectWorld(coord);
        GameObject go = Instantiate(entry.prefab, new Vector3(rect.xMin, 0f, rect.yMin), Quaternion.identity);
        go.name = entry.prefab.name;

        WorldBlock block = go.GetComponent<WorldBlock>();
        if (block == null)
        {
            Debug.LogWarning($"O prefab {entry.prefab.name} não tem WorldBlock. Rode de novo 'Cortar Vilarejo em Blocos'.", go);
            Destroy(go);
            entries.Remove(coord);
            return;
        }

        loaded[coord] = block;
        BlockLoaded?.Invoke(block);
        if (rebuildNavMesh)
        {
            navMesh.RequestRebuild();
        }
    }

    private void Unload(Vector2Int coord)
    {
        if (!loaded.TryGetValue(coord, out WorldBlock block))
        {
            return;
        }

        loaded.Remove(coord);
        BlockUnloading?.Invoke(BlockRectWorld(coord));

        // Objetos com física que foram parar em outro bloco carregado mudam de dono.
        if (block != null && block.DynamicRoot != null)
        {
            List<Transform> movers = new List<Transform>();
            foreach (Transform child in block.DynamicRoot)
            {
                movers.Add(child);
            }

            Rigidbody held = telekinesis != null ? telekinesis.HeldBody : null;
            foreach (Transform child in movers)
            {
                bool isHeld = held != null && held.transform.IsChildOf(child);
                Vector2Int target = CoordAt(WorldOrigin.ToLogical(child.position));
                if (loaded.TryGetValue(target, out WorldBlock other) && other != null && other.DynamicRoot != null)
                {
                    child.SetParent(other.DynamicRoot, true);
                }
                else if (isHeld)
                {
                    child.SetParent(null, true); // está na mão do jogador: não some
                }
            }
        }

        if (block != null)
        {
            Destroy(block.gameObject);
        }

        navMesh.RequestRebuild();
    }

    // Objetos com física que caíram para fora do mundo carregado.
    private void RemoveFallen()
    {
        foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (body == null || body.isKinematic || body.position.y > killHeight)
            {
                continue;
            }

            Component owner = body.GetComponentInParent<AlienHealth>();
            if (owner == null)
            {
                owner = body.GetComponentInParent<TrafficCar>();
            }

            Destroy(owner != null ? owner.gameObject : body.gameObject);
        }
    }

    // ---------- Consultas ----------

    public Vector2Int CoordAt(Vector3 logical)
    {
        return new Vector2Int(
            Mathf.FloorToInt((logical.x - gridOrigin.x) / blockSize),
            Mathf.FloorToInt((logical.z - gridOrigin.y) / blockSize));
    }

    public Rect BlockRectLogical(Vector2Int coord)
    {
        return new Rect(gridOrigin.x + coord.x * blockSize, gridOrigin.y + coord.y * blockSize, blockSize, blockSize);
    }

    public Rect BlockRectWorld(Vector2Int coord)
    {
        Rect rect = BlockRectLogical(coord);
        rect.position -= new Vector2(WorldOrigin.Offset.x, WorldOrigin.Offset.z);
        return rect;
    }

    // O ponto (MUNDO) está sobre um bloco carregado, com essa folga em volta?
    // Pontos fora da grade (além das bordas do mapa) não atrapalham.
    public bool IsLoaded(Vector3 world, float margin = 0f)
    {
        Vector3 logical = WorldOrigin.ToLogical(world);
        if (!IsCoordReady(CoordAt(logical)))
        {
            return false;
        }

        if (margin <= 0f)
        {
            return true;
        }

        return IsCoordReady(CoordAt(logical + new Vector3(margin, 0f, margin))) &&
               IsCoordReady(CoordAt(logical + new Vector3(-margin, 0f, margin))) &&
               IsCoordReady(CoordAt(logical + new Vector3(margin, 0f, -margin))) &&
               IsCoordReady(CoordAt(logical + new Vector3(-margin, 0f, -margin)));
    }

    private bool IsCoordReady(Vector2Int coord)
    {
        return !entries.ContainsKey(coord) || loaded.ContainsKey(coord);
    }

    // Distância do jogador até o bloco (0 = dentro dele), no plano X/Z.
    private float DistanceTo(Vector2Int coord)
    {
        if (player == null)
        {
            return 0f;
        }

        Vector3 p = WorldOrigin.ToLogical(player.position);
        Rect rect = BlockRectLogical(coord);
        float dx = Mathf.Max(rect.xMin - p.x, 0f, p.x - rect.xMax);
        float dz = Mathf.Max(rect.yMin - p.z, 0f, p.z - rect.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ---------- Névoa ----------

    private void ApplyFog()
    {
        RenderSettings.fog = useFog && !testMode;
        if (!useFog)
        {
            return;
        }

        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;
    }

    // ---------- Depuração ----------

    private void OnGUI()
    {
        if (!testMode || player == null)
        {
            return;
        }

        Vector3 logical = WorldOrigin.ToLogical(player.position);
        Vector2Int here = CoordAt(logical);
        string text = $"MODO TESTE DOS BLOCOS (F8)\n" +
                      $"Bloco atual: ({here.x}, {here.y})   carregados: {loaded.Count} de {entries.Count}\n" +
                      $"Posição lógica: ({logical.x:0}, {logical.z:0})   origem: ({WorldOrigin.Offset.x:0}, {WorldOrigin.Offset.z:0})\n" +
                      $"NavMesh: {(navMesh != null && navMesh.IsBuilding ? "montando..." : "pronto")}   F9 = recentralizar";
        GUI.Label(new Rect(12f, Screen.height - 86f, 640f, 80f), text);
    }

    private void OnDrawGizmos()
    {
        foreach (BlockEntry entry in blocks)
        {
            Rect rect = BlockRectWorld(entry.coord);
            bool isLoaded = Application.isPlaying && loaded.ContainsKey(entry.coord);
            Gizmos.color = isLoaded ? new Color(0.2f, 1f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireCube(new Vector3(rect.center.x, 0.5f, rect.center.y), new Vector3(rect.width, 1f, rect.height));
        }
    }
}
