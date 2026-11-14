using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// NavMesh do mundo em blocos (Missão 16).
// Em vez de um NavMesh por bloco (que precisaria de "pontes" nas bordas para os alienígenas
// passarem de um bloco para o outro), existe UM NavMesh só, cobrindo os blocos carregados.
// Quando um bloco entra, sai ou uma casa desmorona, ele é refeito em segundo plano; a Unity só
// recalcula os pedaços (tiles) que mudaram, então os alienígenas no resto continuam andando.
//
// Só entra no NavMesh o que está em "Estatico" de cada bloco (chão, ruas, casas, árvores).
// Objetos com física (caixas, carros) e os alienígenas não abrem buracos nele.
[DefaultExecutionOrder(-880)]
[DisallowMultipleComponent]
public sealed class WorldNavMesh : MonoBehaviour
{
    public static WorldNavMesh Instance { get; private set; }

    [Tooltip("Tipo de agente (0 = Humanoid, o mesmo dos alienígenas).")]
    [SerializeField] private int agentTypeId = 0;
    [Tooltip("Camadas que entram no NavMesh.")]
    [SerializeField] private LayerMask layers = ~0;
    [Tooltip("Altura coberta pelo NavMesh (de baixo até em cima, em metros), a partir de -10 m.")]
    [SerializeField, Min(5f)] private float height = 50f;

    private readonly List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
    private readonly List<NavMeshBuildSource> blockSources = new List<NavMeshBuildSource>();
    private readonly List<NavMeshBuildMarkup> markups = new List<NavMeshBuildMarkup>();

    private NavMeshData data;
    private NavMeshDataInstance instance;
    private AsyncOperation building;
    private bool dirty;

    public bool IsBuilding => building != null && !building.isDone;

    private void Awake()
    {
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (data != null)
        {
            return;
        }

        Instance = this;
        data = new NavMeshData(agentTypeId) { name = "NavMesh_Mundo" };
        instance = NavMesh.AddNavMeshData(data);
    }

    private void OnDestroy()
    {
        if (instance.valid)
        {
            instance.Remove();
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (dirty && !IsBuilding)
        {
            dirty = false;
            if (Collect(out Bounds bounds))
            {
                building = NavMeshBuilder.UpdateNavMeshDataAsync(data, Settings(), sources, bounds);
            }
        }
    }

    // Pede para refazer (bloco entrou/saiu, casa caiu). Acontece em segundo plano.
    public void RequestRebuild()
    {
        dirty = true;
    }

    // Monta na hora (início do jogo), para os alienígenas já terem onde surgir.
    public void BuildNow()
    {
        EnsureInitialized();
        if (IsBuilding)
        {
            dirty = true;
            return;
        }

        dirty = false;
        if (Collect(out Bounds bounds))
        {
            NavMeshBuilder.UpdateNavMeshData(data, Settings(), sources, bounds);
        }
    }

    // O mundo recentralizou: o NavMesh anda junto, sem precisar refazer.
    public void OnOriginShifted(Vector3 delta)
    {
        if (instance.valid)
        {
            instance.Remove();
        }

        data.position -= delta;
        instance = NavMesh.AddNavMeshData(data);
    }

    private NavMeshBuildSettings Settings()
    {
        return NavMesh.GetSettingsByID(agentTypeId);
    }

    // Junta as formas de todos os blocos carregados. Os limites ficam no espaço do NavMesh.
    private bool Collect(out Bounds localBounds)
    {
        sources.Clear();
        localBounds = default;
        WorldStreamer streamer = WorldStreamer.Instance;
        if (streamer == null)
        {
            return false;
        }

        bool any = false;
        Bounds world = default;
        foreach (WorldBlock block in streamer.LoadedBlocks)
        {
            if (block == null || block.StaticRoot == null)
            {
                continue;
            }

            blockSources.Clear();
            NavMeshBuilder.CollectSources(block.StaticRoot, layers, NavMeshCollectGeometry.PhysicsColliders, 0, markups, blockSources);
            sources.AddRange(blockSources);

            Rect rect = streamer.BlockRectWorld(block.Coord);
            float tall = Mathf.Max(height, 100f); // Missão 20: ondulação + morros (até ~65 m nos Picos Gelados)
            Bounds blockBounds = new Bounds(
                new Vector3(rect.center.x, -10f + tall / 2f, rect.center.y),
                new Vector3(rect.width + 2f, tall, rect.height + 2f));
            if (any)
            {
                world.Encapsulate(blockBounds);
            }
            else
            {
                world = blockBounds;
                any = true;
            }
        }

        if (!any)
        {
            // Nada carregado: um NavMesh vazio (some tudo).
            world = new Bounds(Vector3.zero, Vector3.one);
        }

        localBounds = new Bounds(world.center - data.position, world.size);
        return true;
    }
}
