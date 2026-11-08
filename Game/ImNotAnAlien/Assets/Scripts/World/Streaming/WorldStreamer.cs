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
//
// Missão 18 — mundo sorteado: com "Procedural" ligado, a grade inteira (ex.: 9 × 9) é sorteada pela
// semente do jogo salvo (WorldLayout). Os blocos da lista "Blocks" (o Vilarejo) ficam fixos; os outros
// são montados por código (BlockBuilder) quando entram, sempre iguais para a mesma semente.
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

    [Header("Mundo sorteado (Missão 18)")]
    [SerializeField] private bool procedural;
    [Tooltip("Tamanho da grade em blocos (X, Z).")]
    [SerializeField] private Vector2Int gridSize = new Vector2Int(9, 9);
    [SerializeField] private BlockKit kit;
    [Tooltip("Semente usada quando não há jogo salvo (Play direto sem SaveManager).")]
    [SerializeField] private int fallbackSeed = 12345;
    [Tooltip("Paredes invisíveis em volta da grade (até o planeta dar a volta, na Missão 19).")]
    [SerializeField] private bool mapWalls = true;

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
    private readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
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
    public WorldLayout Layout { get; private set; }
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
        PrepareCells();

        if (player == null)
        {
            Debug.LogWarning("WorldStreamer: jogador não encontrado; carregando todos os blocos.", this);
        }

        // Começo do jogo: carrega tudo o que está perto, na hora, e monta o NavMesh antes de
        // a população começar a surgir.
        foreach (Vector2Int coord in new List<Vector2Int>(cells)) // cópia: Load pode tirar blocos da lista
        {
            if (player == null || DistanceTo(coord) < LoadRadius)
            {
                Load(coord, false);
            }
        }

        navMesh.BuildNow();
        Debug.Log($"Mundo em blocos: {loaded.Count} de {cells.Count} blocos carregados no início.");
    }

    // Quais blocos existem: só os da lista (Missão 16) ou a grade sorteada inteira (Missão 18).
    private void PrepareCells()
    {
        if (!procedural)
        {
            cells.UnionWith(entries.Keys);
            return;
        }

        int seed = SaveManager.Instance != null && SaveManager.Instance.Data != null ? SaveManager.Instance.Data.worldSeed : fallbackSeed;
        Dictionary<Vector2Int, BlockSpec> fixedSpecs = new Dictionary<Vector2Int, BlockSpec>();
        foreach (BlockEntry entry in entries.Values)
        {
            WorldBlock block = entry.prefab.GetComponent<WorldBlock>();
            if (block != null && block.HasSpec)
            {
                fixedSpecs[entry.coord] = block.Spec;
            }
        }

        Layout = WorldLayout.Generate(gridSize.x, gridSize.y, gridOrigin, blockSize, seed, fixedSpecs);
        for (int x = 0; x < gridSize.x; x++)
        {
            for (int z = 0; z < gridSize.y; z++)
            {
                cells.Add(new Vector2Int(x, z));
            }
        }

        if (kit == null)
        {
            Debug.LogError("WorldStreamer: falta o Kit de Blocos (campo Kit no objeto Mundo); só os blocos fixos vão aparecer. " +
                           "Arraste Assets/Art/BlockKit.asset para o campo ou rode 'Gerar Mundo Sorteado' de novo.", this);
        }

        if (mapWalls)
        {
            BuildMapWalls();
        }

        Debug.Log($"Mundo sorteado: semente {seed}, {gridSize.x} × {gridSize.y} blocos.");
    }

    private void BuildMapWalls()
    {
        GameObject walls = new GameObject("Limites_Mapa");
        float width = gridSize.x * blockSize;
        float depth = gridSize.y * blockSize;
        Vector3 center = WorldOrigin.ToWorld(new Vector3(gridOrigin.x + width / 2f, 10f, gridOrigin.y + depth / 2f));
        Wall(walls.transform, "Limite_Leste", center + new Vector3(width / 2f + 0.5f, 0f, 0f), new Vector3(1f, 20f, depth + 2f));
        Wall(walls.transform, "Limite_Oeste", center - new Vector3(width / 2f + 0.5f, 0f, 0f), new Vector3(1f, 20f, depth + 2f));
        Wall(walls.transform, "Limite_Norte", center + new Vector3(0f, 0f, depth / 2f + 0.5f), new Vector3(width + 2f, 20f, 1f));
        Wall(walls.transform, "Limite_Sul", center - new Vector3(0f, 0f, depth / 2f + 0.5f), new Vector3(width + 2f, 20f, 1f));
    }

    private static void Wall(Transform parent, string name, Vector3 position, Vector3 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.position = position;
        wall.AddComponent<BoxCollider>().size = size;
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
        if (cells.Contains(here) && !loaded.ContainsKey(here))
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
        foreach (Vector2Int coord in cells)
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
        Rect rect = BlockRectWorld(coord);
        GameObject go;
        if (entries.TryGetValue(coord, out BlockEntry entry))
        {
            go = Instantiate(entry.prefab, new Vector3(rect.xMin, 0f, rect.yMin), Quaternion.identity);
            go.name = entry.prefab.name;
        }
        else if (Layout != null && kit != null)
        {
            go = BlockBuilder.Build(Layout.Get(coord), kit, coord);
            go.transform.position = new Vector3(rect.xMin, 0f, rect.yMin);
        }
        else
        {
            cells.Remove(coord);
            return;
        }

        WorldBlock block = go.GetComponent<WorldBlock>();
        if (block == null)
        {
            Debug.LogWarning($"O bloco {go.name} não tem WorldBlock.", go);
            Destroy(go);
            cells.Remove(coord);
            return;
        }

        block.SetCoord(coord);

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
        return !cells.Contains(coord) || loaded.ContainsKey(coord);
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
        string type = Layout != null && Layout.Contains(here) ? Layout.Get(here).ToString() : "";
        string text = $"MODO TESTE DOS BLOCOS (F8)\n" +
                      $"Bloco atual: ({here.x}, {here.y}) {type}   carregados: {loaded.Count} de {cells.Count}\n" +
                      $"Posição lógica: ({logical.x:0}, {logical.z:0})   origem: ({WorldOrigin.Offset.x:0}, {WorldOrigin.Offset.z:0})\n" +
                      $"NavMesh: {(navMesh != null && navMesh.IsBuilding ? "montando..." : "pronto")}   F9 = recentralizar";
        GUI.Label(new Rect(12f, Screen.height - 86f, 900f, 80f), text);
    }

    private void OnDrawGizmos()
    {
        List<Vector2Int> coords = new List<Vector2Int>();
        if (procedural)
        {
            for (int x = 0; x < gridSize.x; x++)
            {
                for (int z = 0; z < gridSize.y; z++)
                {
                    coords.Add(new Vector2Int(x, z));
                }
            }
        }
        else
        {
            foreach (BlockEntry entry in blocks)
            {
                coords.Add(entry.coord);
            }
        }

        foreach (Vector2Int coord in coords)
        {
            Rect rect = BlockRectWorld(coord);
            bool isLoaded = Application.isPlaying && loaded.ContainsKey(coord);
            Gizmos.color = isLoaded ? new Color(0.2f, 1f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireCube(new Vector3(rect.center.x, 0.5f, rect.center.y), new Vector3(rect.width, 1f, rect.height));
        }
    }
}
