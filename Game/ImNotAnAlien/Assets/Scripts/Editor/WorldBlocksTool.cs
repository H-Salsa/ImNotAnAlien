using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 16 — cortar o Vilarejo do Bilu em blocos (decisão D21).
// Menu: ImNotAnAlien > Cortar Vilarejo em Blocos (Missao 16)
// Rode com a cena Regiao1_VilarejoDoBilu aberta. A ferramenta:
// 1. Salva uma CÓPIA da cena como "Regiao1_VilarejoDoBilu_Blocos" e trabalha nela
//    (a cena original não muda; dá para rodar de novo quando quiser).
// 2. Divide o mapa numa grade 3 × 3 de blocos de 100 m (canto sudoeste em x = -60, z = -240).
//    Cada objeto vai para o bloco onde está o seu centro; casas, árvores, postes, galpões e pontes
//    ficam inteiros. Peças compridas (ruas, chão, rio) são cortadas nas bordas dos blocos.
// 3. Objetos com física (caixas, lixeiras, contêineres, carros estacionados) vão para a parte
//    "Dinamico" do bloco; o resto vai para "Estatico" (que entra no NavMesh).
// 4. Salva cada bloco como prefab em Assets/Prefabs/Blocos/Vilarejo e tira os blocos da cena.
// 5. Cria o objeto "Mundo" (WorldOrigin + WorldNavMesh + WorldStreamer), liga a névoa e põe a
//    cena nova em primeiro lugar no Build.
//
// Para mudar o cenário depois: abra o prefab do bloco (duplo clique) e edite lá.
public static class WorldBlocksTool
{
    private const string SourceScenePath = "Assets/Scenes/Regiao1_VilarejoDoBilu.unity";
    private const string BlocksScenePath = "Assets/Scenes/Regiao1_VilarejoDoBilu_Blocos.unity";
    private const string PrefabFolder = "Assets/Prefabs/Blocos/Vilarejo";

    private const float BlockSize = 100f;
    private static readonly Vector2 GridOrigin = new Vector2(-60f, -240f);
    private const int Columns = 3; // ao longo de X
    private const int Rows = 3;    // ao longo de Z
    // Um objeto pode passar até esta distância da borda e continuar inteiro no bloco (casas na beirada).
    private const float FitTolerance = 12f;

    // Raízes da cena com o cenário fixo e com os objetos de física.
    private const string RegionRootName = "Vilarejo_do_Bilu";
    private static readonly string[] ExtraStaticRoots = { "Chao" };
    private static readonly string[] DynamicRoots = { "Arremessaveis", "Arremessaveis_Regiao" };
    // Ficam na cena, fora dos blocos (paredes invisíveis em volta do mapa inteiro).
    private static readonly string[] KeepGlobal = { "Limites_Invisiveis" };

    private sealed class Block
    {
        public Vector2Int coord;
        public GameObject root;
        public Transform staticRoot;
        public Transform dynamicRoot;
        public int staticCount;
        public int dynamicCount;
    }

    private static Dictionary<Vector2Int, Block> blocks;
    private static List<string> warnings;
    private static int splitCount;

    [MenuItem("ImNotAnAlien/Cortar Vilarejo em Blocos (Missao 16)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Pare o Play", "Saia do Play Mode antes de cortar o mapa.", "OK");
            return;
        }

        var current = EditorSceneManager.GetActiveScene();
        if (current.path != SourceScenePath || FindRoot(current, RegionRootName) == null)
        {
            EditorUtility.DisplayDialog("Cena errada",
                "Abra a cena Regiao1_VilarejoDoBilu (a original, com o objeto Vilarejo_do_Bilu) e rode de novo.", "OK");
            return;
        }

        bool exists = AssetDatabase.LoadAssetAtPath<SceneAsset>(BlocksScenePath) != null;
        if (!EditorUtility.DisplayDialog("Cortar o Vilarejo em blocos",
                "Vou salvar a cena atual e criar uma CÓPIA chamada Regiao1_VilarejoDoBilu_Blocos.\n" +
                "O mapa será dividido em 9 blocos de 100 m, salvos como prefabs em\n" + PrefabFolder + ".\n\n" +
                (exists ? "A cena de blocos e os prefabs que já existem serão SUBSTITUÍDOS.\n\n" : "") +
                "A cena original não muda.",
                exists ? "Substituir" : "Cortar", "Cancelar"))
        {
            return;
        }

        EditorSceneManager.SaveScene(current);
        EditorSceneManager.SaveScene(current, BlocksScenePath, true);
        var scene = EditorSceneManager.OpenScene(BlocksScenePath, OpenSceneMode.Single);

        blocks = new Dictionary<Vector2Int, Block>();
        warnings = new List<string>();
        splitCount = 0;

        try
        {
            EditorUtility.DisplayProgressBar("Blocos", "Criando a grade...", 0.05f);
            for (int x = 0; x < Columns; x++)
            {
                for (int z = 0; z < Rows; z++)
                {
                    CreateBlock(new Vector2Int(x, z));
                }
            }

            Physics.SyncTransforms();

            EditorUtility.DisplayProgressBar("Blocos", "Distribuindo o cenário...", 0.2f);
            GameObject region = FindRoot(scene, RegionRootName);
            foreach (string keep in KeepGlobal)
            {
                Transform global = region.transform.Find(keep);
                if (global != null)
                {
                    global.SetParent(null, true);
                }
            }

            foreach (Transform child in Children(region.transform))
            {
                Distribute(child, new List<string>());
            }

            Object.DestroyImmediate(region); // leva junto o NavMesh antigo (o novo é montado no jogo)

            foreach (string name in ExtraStaticRoots)
            {
                GameObject extra = FindRoot(scene, name);
                if (extra != null)
                {
                    Distribute(extra.transform, new List<string>());
                }
            }

            EditorUtility.DisplayProgressBar("Blocos", "Distribuindo os objetos com física...", 0.5f);
            foreach (string name in DynamicRoots)
            {
                GameObject group = FindRoot(scene, name);
                if (group == null)
                {
                    continue;
                }

                foreach (Transform child in Children(group.transform))
                {
                    Block block = blocks[CoordOf(child.position)];
                    child.SetParent(block.dynamicRoot, true);
                    block.dynamicCount++;
                }

                Object.DestroyImmediate(group);
            }

            EditorUtility.DisplayProgressBar("Blocos", "Salvando os prefabs...", 0.7f);
            List<(Vector2Int coord, GameObject prefab)> saved = SavePrefabs();

            EditorUtility.DisplayProgressBar("Blocos", "Montando o objeto Mundo...", 0.9f);
            CreateWorld(scene, saved);
            AddSceneToBuild();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Report(saved);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---------- Grade ----------

    private static void CreateBlock(Vector2Int coord)
    {
        Rect rect = BlockRect(coord);
        GameObject root = new GameObject($"Bloco_{coord.x}_{coord.y}");
        root.transform.position = new Vector3(rect.xMin, 0f, rect.yMin);

        Transform staticRoot = new GameObject("Estatico").transform;
        staticRoot.SetParent(root.transform, false);
        Transform dynamicRoot = new GameObject("Dinamico").transform;
        dynamicRoot.SetParent(root.transform, false);

        blocks[coord] = new Block { coord = coord, root = root, staticRoot = staticRoot, dynamicRoot = dynamicRoot };
    }

    private static Rect BlockRect(Vector2Int coord)
    {
        return new Rect(GridOrigin.x + coord.x * BlockSize, GridOrigin.y + coord.y * BlockSize, BlockSize, BlockSize);
    }

    // Bloco de um ponto (fora da grade: o bloco mais perto).
    private static Vector2Int CoordOf(Vector3 position)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt((position.x - GridOrigin.x) / BlockSize), 0, Columns - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt((position.z - GridOrigin.y) / BlockSize), 0, Rows - 1);
        return new Vector2Int(x, z);
    }

    private static bool Fits(Bounds bounds)
    {
        Rect rect = BlockRect(CoordOf(bounds.center));
        return bounds.min.x >= rect.xMin - FitTolerance && bounds.max.x <= rect.xMax + FitTolerance &&
               bounds.min.z >= rect.yMin - FitTolerance && bounds.max.z <= rect.yMax + FitTolerance;
    }

    // ---------- Distribuir o cenário ----------

    private static void Distribute(Transform item, List<string> path)
    {
        bool isUnit = item.GetComponent<DestructibleStructure>() != null || item.childCount == 0 ||
                      PrefabUtility.IsAnyPrefabInstanceRoot(item.gameObject);
        if (isUnit)
        {
            PlaceUnit(item, path);
            return;
        }

        if (!TryGetBounds(item, out Bounds bounds))
        {
            return; // grupo vazio
        }

        if (Fits(bounds))
        {
            Move(item, CoordOf(bounds.center), path);
            return;
        }

        // Grupo grande demais: distribui os filhos um por um.
        path.Add(item.name);
        foreach (Transform child in Children(item))
        {
            Distribute(child, path);
        }

        path.RemoveAt(path.Count - 1);

        // Um grupo que também tem forma própria (raro) vai inteiro depois dos filhos.
        if (item.GetComponent<Renderer>() != null || item.GetComponent<Collider>() != null)
        {
            PlaceUnit(item, path);
        }
    }

    private static void PlaceUnit(Transform item, List<string> path)
    {
        if (!TryGetBounds(item, out Bounds bounds))
        {
            Move(item, CoordOf(item.position), path);
            return;
        }

        if (Fits(bounds))
        {
            Move(item, CoordOf(bounds.center), path);
            return;
        }

        bool canSplit = item.childCount == 0 && item.GetComponent<DestructibleStructure>() == null;
        if (canSplit && TrySplit(item.gameObject, out List<GameObject> pieces))
        {
            splitCount++;
            foreach (GameObject piece in pieces)
            {
                Move(piece.transform, CoordOf(piece.transform.position), path);
            }

            return;
        }

        Vector2Int coord = CoordOf(bounds.center);
        warnings.Add($"\"{FullName(item)}\" é maior que um bloco e não pôde ser cortado: ficou inteiro no bloco ({coord.x}, {coord.y}).");
        Move(item, coord, path);
    }

    private static void Move(Transform item, Vector2Int coord, List<string> path)
    {
        Block block = blocks[coord];
        Transform parent = block.staticRoot;
        foreach (string name in path)
        {
            Transform next = parent.Find(name);
            if (next == null)
            {
                next = new GameObject(name).transform;
                next.SetParent(parent, false);
                next.gameObject.isStatic = true;
            }

            parent = next;
        }

        item.SetParent(parent, true);
        block.staticCount++;
    }

    // ---------- Cortar peças compridas ----------

    // Só caixas (cubo da Unity), sem escala no pai. Corta em X e depois em Z, nas bordas dos blocos.
    private static bool TrySplit(GameObject item, out List<GameObject> pieces)
    {
        pieces = null;
        if (PrefabUtility.IsPartOfPrefabInstance(item))
        {
            return false;
        }

        MeshFilter filter = item.GetComponent<MeshFilter>();
        BoxCollider box = item.GetComponent<BoxCollider>();
        bool cubeMesh = filter != null && filter.sharedMesh != null && filter.sharedMesh.name == "Cube";
        bool unitBox = box != null && box.center == Vector3.zero && box.size == Vector3.one;
        if (!(cubeMesh && (box == null || unitBox)) && !(filter == null && unitBox))
        {
            return false;
        }

        Transform parent = item.transform.parent;
        if (parent != null && (parent.lossyScale - Vector3.one).sqrMagnitude > 0.0001f)
        {
            return false;
        }

        pieces = new List<GameObject> { item };
        foreach (int axis in new[] { 0, 2 })
        {
            List<GameObject> next = new List<GameObject>();
            foreach (GameObject piece in pieces)
            {
                next.AddRange(SplitAlong(piece, axis));
            }

            pieces = next;
        }

        return true;
    }

    private static List<GameObject> SplitAlong(GameObject item, int worldAxis)
    {
        Transform t = item.transform;
        Vector3 axis = worldAxis == 0 ? Vector3.right : Vector3.forward;

        // Qual eixo da própria caixa está alinhado com esse eixo do mundo?
        int local = -1;
        for (int i = 0; i < 3; i++)
        {
            Vector3 direction = t.rotation * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
            if (Mathf.Abs(Vector3.Dot(direction, axis)) > 0.999f)
            {
                local = i;
                break;
            }
        }

        if (local < 0)
        {
            return new List<GameObject> { item };
        }

        float center = Vector3.Dot(t.position, axis);
        float half = t.localScale[local] * 0.5f;
        float from = center - half;
        float to = center + half;
        float origin = worldAxis == 0 ? GridOrigin.x : GridOrigin.y;
        int count = worldAxis == 0 ? Columns : Rows;

        List<float> cuts = new List<float> { from };
        for (int n = 1; n < count; n++)
        {
            float line = origin + n * BlockSize;
            if (line > from + 0.05f && line < to - 0.05f)
            {
                cuts.Add(line);
            }
        }

        cuts.Add(to);
        if (cuts.Count == 2)
        {
            return new List<GameObject> { item };
        }

        List<GameObject> result = new List<GameObject>();
        for (int s = 0; s < cuts.Count - 1; s++)
        {
            float a = cuts[s];
            float b = cuts[s + 1];
            GameObject piece = Object.Instantiate(item, t.parent);
            piece.name = item.name;
            piece.transform.SetPositionAndRotation(t.position + axis * ((a + b) * 0.5f - center), t.rotation);
            Vector3 scale = t.localScale;
            scale[local] = b - a;
            piece.transform.localScale = scale;
            result.Add(piece);
        }

        Object.DestroyImmediate(item);
        return result;
    }

    // ---------- Prefabs e objeto Mundo ----------

    private static List<(Vector2Int, GameObject)> SavePrefabs()
    {
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets/Prefabs", "Blocos");
        EnsureFolder("Assets/Prefabs/Blocos", "Vilarejo");

        List<(Vector2Int, GameObject)> saved = new List<(Vector2Int, GameObject)>();
        foreach (Block block in blocks.Values)
        {
            if (block.staticCount + block.dynamicCount == 0)
            {
                Object.DestroyImmediate(block.root);
                continue;
            }

            WorldBlock component = block.root.AddComponent<WorldBlock>();
            component.Setup(block.coord, block.staticRoot, block.dynamicRoot);

            string path = $"{PrefabFolder}/{block.root.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(block.root, path, out bool success);
            Object.DestroyImmediate(block.root);
            if (!success || prefab == null)
            {
                warnings.Add($"Não consegui salvar {path}.");
                continue;
            }

            saved.Add((block.coord, prefab));
        }

        AssetDatabase.SaveAssets();
        return saved;
    }

    private static void CreateWorld(UnityEngine.SceneManagement.Scene scene, List<(Vector2Int coord, GameObject prefab)> saved)
    {
        GameObject old = FindRoot(scene, "Mundo");
        if (old != null)
        {
            Object.DestroyImmediate(old);
        }

        GameObject world = new GameObject("Mundo");
        world.AddComponent<WorldOrigin>();
        world.AddComponent<WorldNavMesh>();
        WorldStreamer streamer = world.AddComponent<WorldStreamer>();

        SerializedObject data = new SerializedObject(streamer);
        data.FindProperty("blockSize").floatValue = BlockSize;
        data.FindProperty("gridOrigin").vector2Value = GridOrigin;
        SerializedProperty list = data.FindProperty("blocks");
        list.arraySize = saved.Count;
        for (int i = 0; i < saved.Count; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("coord").vector2IntValue = saved[i].coord;
            item.FindPropertyRelative("prefab").objectReferenceValue = saved[i].prefab;
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        // Névoa também no editor (os valores do jogo ficam no WorldStreamer).
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.88f, 1f);
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 165f;
    }

    private static void AddSceneToBuild()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == BlocksScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(BlocksScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void Report(List<(Vector2Int coord, GameObject prefab)> saved)
    {
        StringBuilder log = new StringBuilder();
        log.AppendLine($"Vilarejo cortado em {saved.Count} blocos ({splitCount} peças compridas cortadas nas bordas).");
        foreach (Block block in blocks.Values)
        {
            log.AppendLine($"  Bloco ({block.coord.x}, {block.coord.y}): {block.staticCount} itens fixos, {block.dynamicCount} com física.");
        }

        foreach (string warning in warnings)
        {
            log.AppendLine("  Aviso: " + warning);
        }

        Debug.Log(log.ToString());

        string message = $"{saved.Count} blocos salvos em {PrefabFolder}.\n" +
                         $"{splitCount} peças compridas (ruas, chão, rio) foram cortadas nas bordas.\n" +
                         (warnings.Count > 0 ? $"{warnings.Count} aviso(s): veja o Console.\n" : "") +
                         "\nA cena Regiao1_VilarejoDoBilu_Blocos está aberta e salva.\n" +
                         "Dê Play. Teclas de teste: F8 = modo teste dos blocos, F9 = recentralizar o mundo.";
        EditorUtility.DisplayDialog("Mundo em blocos pronto!", message, "OK");
    }

    // ---------- Ajudantes ----------

    private static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root;
            }
        }

        return null;
    }

    private static List<Transform> Children(Transform parent)
    {
        List<Transform> list = new List<Transform>();
        foreach (Transform child in parent)
        {
            list.Add(child);
        }

        return list;
    }

    private static bool TryGetBounds(Transform item, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
        {
            Encapsulate(ref bounds, ref any, renderer.bounds);
        }

        foreach (Collider collider in item.GetComponentsInChildren<Collider>(true))
        {
            if (collider.enabled && collider.gameObject.activeInHierarchy)
            {
                Encapsulate(ref bounds, ref any, collider.bounds);
            }
        }

        return any;
    }

    private static void Encapsulate(ref Bounds bounds, ref bool any, Bounds add)
    {
        if (any)
        {
            bounds.Encapsulate(add);
        }
        else
        {
            bounds = add;
            any = true;
        }
    }

    private static string FullName(Transform item)
    {
        string name = item.name;
        for (Transform p = item.parent; p != null; p = p.parent)
        {
            name = p.name + "/" + name;
        }

        return name;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
