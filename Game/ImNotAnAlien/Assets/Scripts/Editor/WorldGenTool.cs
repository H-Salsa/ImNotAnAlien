using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ferramenta de editor da Missão 18 — mundo sorteado pela semente (decisão D21).
// Menu: ImNotAnAlien > Gerar Mundo Sorteado (Missao 18)
// Rode depois das Missões 16 e 17. A ferramenta:
// 1. Cria/atualiza o Kit de Blocos (Assets/Art/BlockKit.asset) com os materiais (mantém cores que você mudou).
// 2. Refaz o Vilarejo do Bilu no PADRÃO NOVO (ruas e rio no meio dos lados) como 9 prefabs em
//    Assets/Prefabs/Blocos/Vilarejo_Padrao. A esquina feita à mão é copiada da cena original.
// 3. Cria a cena Mundo_Sorteado (cópia da cena de blocos) com uma grade 9 × 9: o Vilarejo fixo no meio
//    e o resto sorteado pela semente de cada jogo salvo.
// 4. Ajusta os setores (Vilarejo + "Arredores" provisório), o menu inicial e a ordem do Build.
// Pode rodar de novo: refaz tudo (a cena original e a cena de blocos da Missão 16 não mudam).
public static class WorldGenTool
{
    private const string SourceScenePath = "Assets/Scenes/Regiao1_VilarejoDoBilu.unity";
    private const string BlocksScenePath = "Assets/Scenes/Regiao1_VilarejoDoBilu_Blocos.unity";
    private const string WorldScenePath = "Assets/Scenes/Mundo_Sorteado.unity";
    private const string MenuScenePath = "Assets/Scenes/MenuInicial.unity";
    private const string KitPath = "Assets/Art/BlockKit.asset";
    private const string PrefabFolder = "Assets/Prefabs/Blocos/Vilarejo_Padrao";
    private const string CarModelPath = "Assets/Art/Vehicles/Carro_Civil.fbx";

    private const float BlockSize = 100f;
    private static readonly Vector2Int GridSize = new Vector2Int(9, 9);
    private static readonly Vector2 GridOrigin = new Vector2(-350f, -550f); // a esquina (0, 0) fica no meio do bloco (3, 5)

    private struct FixedBlock
    {
        public Vector2Int coord;
        public string name;
        public BlockSpec spec;
    }

    private const Socket M = Socket.Mato;
    private const Socket R = Socket.Rua;
    private const Socket V = Socket.Rio;

    // O Vilarejo no padrão novo: ruas em x = 0 e x = 200, z = 0 e z = -200; rio em z = -100.
    private static readonly FixedBlock[] Vilarejo =
    {
        Fixed(3, 5, "Esquina", BlockSpec.Make(BlockType.Esquina, R, R, R, R), 1001),
        Fixed(4, 5, "Vila_Oeste", BlockSpec.Make(BlockType.Bairro, M, R, M, R), 1002),
        Fixed(5, 5, "Vila_Cruzamento", BlockSpec.Make(BlockType.Bairro, R, R, R, R), 1003),
        Fixed(3, 4, "Ponte_Oeste", BlockSpec.Make(BlockType.Rio, R, V, R, V), 1004),
        Fixed(4, 4, "Rio_Cais", BlockSpec.Make(BlockType.Rio, M, V, M, V, "cais"), 1005),
        Fixed(5, 4, "Ponte_Leste", BlockSpec.Make(BlockType.Rio, R, V, R, V, "cais"), 1006),
        Fixed(3, 3, "Parque", BlockSpec.Make(BlockType.Parque, R, R, R, R), 1007),
        Fixed(4, 3, "Galpoes_Oeste", BlockSpec.Make(BlockType.Galpoes, M, R, M, R), 1008),
        Fixed(5, 3, "Docas", BlockSpec.Make(BlockType.Galpoes, R, R, R, R), 1009),
    };

    private static FixedBlock Fixed(int x, int z, string name, BlockSpec spec, int seed)
    {
        spec.seed = seed;
        return new FixedBlock { coord = new Vector2Int(x, z), name = name, spec = spec };
    }

    [MenuItem("ImNotAnAlien/Gerar Mundo Sorteado (Missao 18)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Pare o Play", "Saia do Play Mode antes de gerar o mundo.", "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BlocksScenePath) == null || AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
        {
            EditorUtility.DisplayDialog("Faltam cenas",
                "Preciso de Regiao1_VilarejoDoBilu (original) e Regiao1_VilarejoDoBilu_Blocos (Missão 16).", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        if (!EditorUtility.DisplayDialog("Gerar mundo sorteado",
                "Vou refazer o Vilarejo no padrão novo (9 prefabs) e criar a cena Mundo_Sorteado (grade 9 × 9).\n" +
                "A cena original e a cena de blocos da Missão 16 não mudam.\n\n" +
                "Jogos salvos antigos podem começar fora do lugar: prefira um Novo jogo.",
                "Gerar", "Cancelar"))
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Mundo sorteado", "Kit de blocos...", 0.05f);
            CreateKit();

            EditorUtility.DisplayProgressBar("Mundo sorteado", "Vilarejo no padrão novo...", 0.2f);
            List<(Vector2Int coord, GameObject prefab)> prefabs = BuildVilarejo();

            EditorUtility.DisplayProgressBar("Mundo sorteado", "Cena Mundo_Sorteado...", 0.7f);
            SetupWorldScene(prefabs);
            SetupMenuAndBuild();

            EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Single);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        string message = "Mundo sorteado pronto!\n\n" +
                         "Cena Mundo_Sorteado aberta: grade 9 × 9 (900 m) com o Vilarejo no meio.\n" +
                         "Cada Novo jogo sorteia um mapa diferente em volta; o mesmo jogo é sempre igual.\n\n" +
                         "Dê Play pela cena MenuInicial (ou direto aqui, que usa o Jogo 1).\n" +
                         "F8 mostra o tipo de cada bloco.";
        Debug.Log(message);
        EditorUtility.DisplayDialog("Missão 18", message, "OK");
    }

    // ---------- Vilarejo ----------

    private static List<(Vector2Int, GameObject)> BuildVilarejo()
    {
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets/Prefabs", "Blocos");
        EnsureFolder("Assets/Prefabs/Blocos", "Vilarejo_Padrao");

        // A cena original tem a esquina feita à mão (Esquina_Blockout, Chao, Arremessaveis).
        Scene source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        BlockKit kit = AssetDatabase.LoadAssetAtPath<BlockKit>(KitPath); // recarregado depois de abrir a cena
        List<(Vector2Int, GameObject)> result = new List<(Vector2Int, GameObject)>();

        foreach (FixedBlock item in Vilarejo)
        {
            Rect rect = BlockRect(item.coord);
            GameObject block = BlockBuilder.Build(item.spec, kit, item.coord, new Vector3(rect.xMin, 0f, rect.yMin), "Vilarejo_" + item.name);

            if (item.spec.type == BlockType.Esquina)
            {
                AddHandmadeEsquina(source, block.GetComponent<WorldBlock>());
            }

            string path = $"{PrefabFolder}/Vilarejo_{item.name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(block, path, out bool ok);
            Object.DestroyImmediate(block);
            if (ok && prefab != null)
            {
                result.Add((item.coord, prefab));
            }
            else
            {
                Debug.LogWarning("Não consegui salvar " + path);
            }
        }

        AssetDatabase.SaveAssets();
        return result; // a cena original é fechada sem salvar (nada nela mudou)
    }

    private static void AddHandmadeEsquina(Scene source, WorldBlock block)
    {
        GameObject region = FindRoot(source, "Vilarejo_do_Bilu");
        Transform esquina = region != null ? region.transform.Find("Esquina_Blockout") : null;
        if (esquina == null)
        {
            Debug.LogWarning("Não achei Esquina_Blockout na cena original; a esquina ficou só com o anel.");
            return;
        }

        GameObject copy = Object.Instantiate(esquina.gameObject);
        copy.name = "Esquina_Blockout";
        foreach (NavMeshSurface surface in copy.GetComponentsInChildren<NavMeshSurface>(true))
        {
            Object.DestroyImmediate(surface);
        }

        foreach (Transform wall in copy.GetComponentsInChildren<Transform>(true))
        {
            if (wall != null && wall.name == "Limites_Invisiveis")
            {
                Object.DestroyImmediate(wall.gameObject);
                break;
            }
        }

        copy.transform.SetParent(block.StaticRoot, true);

        GameObject ground = FindRoot(source, "Chao");
        if (ground != null)
        {
            GameObject groundCopy = Object.Instantiate(ground);
            groundCopy.name = "Chao";
            groundCopy.transform.SetParent(block.StaticRoot, true);
        }

        GameObject props = FindRoot(source, "Arremessaveis");
        if (props != null)
        {
            foreach (Transform prop in props.transform)
            {
                GameObject propCopy = Object.Instantiate(prop.gameObject);
                propCopy.name = prop.name;
                propCopy.transform.SetParent(block.DynamicRoot, true);
            }
        }
    }

    // ---------- Cena do mundo ----------

    private static void SetupWorldScene(List<(Vector2Int coord, GameObject prefab)> prefabs)
    {
        BlockKit kit;
        AssetDatabase.DeleteAsset(WorldScenePath);
        AssetDatabase.CopyAsset(BlocksScenePath, WorldScenePath);
        Scene world = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Single);

        // O que era só do Vilarejo antigo sai (as paredes agora cercam a grade toda; a água vem nos blocos).
        foreach (string name in new[] { "Limites_Invisiveis", "Rio_Agua_Gatilho" })
        {
            GameObject old = FindRoot(world, name);
            if (old != null)
            {
                Object.DestroyImmediate(old);
            }
        }

        GameObject mundo = FindRoot(world, "Mundo");
        WorldStreamer streamer = mundo != null ? mundo.GetComponent<WorldStreamer>() : null;
        if (streamer == null)
        {
            Debug.LogError("A cena de blocos não tem o objeto Mundo com WorldStreamer. Rode a ferramenta da Missão 16.");
            return;
        }

        SerializedObject data = new SerializedObject(streamer);
        data.FindProperty("procedural").boolValue = true;
        data.FindProperty("gridSize").vector2IntValue = GridSize;
        data.FindProperty("gridOrigin").vector2Value = GridOrigin;
        data.FindProperty("blockSize").floatValue = BlockSize;
        // Recarrega o kit do disco: trocar de cena pode descarregar a referência antiga.
        kit = AssetDatabase.LoadAssetAtPath<BlockKit>(KitPath);
        if (kit == null)
        {
            Debug.LogError("Não achei " + KitPath + " para ligar ao WorldStreamer.");
        }

        data.FindProperty("kit").objectReferenceValue = kit;
        SerializedProperty list = data.FindProperty("blocks");
        list.arraySize = prefabs.Count;
        for (int i = 0; i < prefabs.Count; i++)
        {
            SerializedProperty entry = list.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("coord").vector2IntValue = prefabs[i].coord;
            entry.FindPropertyRelative("prefab").objectReferenceValue = prefabs[i].prefab;
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        // Setores: os 4 do Vilarejo nas posições novas + "Arredores" (provisório até as regiões da Missão 19).
        SectorTracker tracker = Object.FindFirstObjectByType<SectorTracker>();
        if (tracker != null)
        {
            SerializedObject trackerData = new SerializedObject(tracker);
            SerializedProperty sectors = trackerData.FindProperty("sectors");
            (string name, Rect area, int quota)[] values =
            {
                ("Esquina", Rect.MinMaxRect(-50f, -100f, 100f, 50f), 50),
                ("Vila das Casas", Rect.MinMaxRect(100f, -100f, 250f, 50f), 75),
                ("Parque e Lago", Rect.MinMaxRect(-50f, -250f, 100f, -100f), 55),
                ("Margem do Rio", Rect.MinMaxRect(100f, -250f, 250f, -100f), 70),
                ("Arredores", new Rect(GridOrigin.x, GridOrigin.y, GridSize.x * BlockSize, GridSize.y * BlockSize), 150),
            };
            sectors.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                SerializedProperty item = sectors.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("name").stringValue = values[i].name;
                item.FindPropertyRelative("area").rectValue = values[i].area;
                item.FindPropertyRelative("quota").intValue = values[i].quota;
                item.FindPropertyRelative("clearBonusDna").intValue = 40;
            }

            trackerData.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(world);
        EditorSceneManager.SaveScene(world);
    }

    private static void SetupMenuAndBuild()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) != null)
        {
            Scene menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            MainMenu mainMenu = Object.FindFirstObjectByType<MainMenu>();
            if (mainMenu != null)
            {
                SerializedObject data = new SerializedObject(mainMenu);
                data.FindProperty("gameSceneName").stringValue = "Mundo_Sorteado";
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(menu);
                EditorSceneManager.SaveScene(menu);
            }
        }

        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == MenuScenePath || s.path == WorldScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(WorldScenePath, true));
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath) != null)
        {
            scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ---------- Kit de blocos ----------

    private static BlockKit CreateKit()
    {
        BlockKit kit = AssetDatabase.LoadAssetAtPath<BlockKit>(KitPath);
        if (kit == null)
        {
            EnsureFolder("Assets", "Art");
            kit = ScriptableObject.CreateInstance<BlockKit>();
            AssetDatabase.CreateAsset(kit, KitPath);
        }

        kit.grass = Keep(kit.grass, "Blockout", "Grama", "6FBF8E");
        kit.asphalt = Keep(kit.asphalt, "Blockout", "Asfalto", "3B4252");
        kit.laneMark = Keep(kit.laneMark, "Blockout", "Faixa", "F2C14E");
        kit.sidewalk = Keep(kit.sidewalk, "Blockout", "Calcada", "CFC8B8");
        kit.path = Keep(kit.path, "Blockout", "Caminho", "E3D5B0");
        kit.sand = Keep(kit.sand, "Mundo", "Areia", "E8D9A8");
        kit.soil = Keep(kit.soil, "Mundo", "Terra", "8A5A3C");
        kit.water = Keep(kit.water, "Blockout", "Agua", "5EC8D8");
        kit.riverBed = Keep(kit.riverBed, "Regiao", "Fundo_Rio", "4E6E66");
        if (kit.houseWalls == null || kit.houseWalls.Length == 0)
        {
            kit.houseWalls = new[]
            {
                Mat("Blockout", "Casa_Creme", "F1E3C6"), Mat("Blockout", "Casa_Turquesa", "8FD6CF"),
                Mat("Blockout", "Casa_Lilas", "C8B2E6"), Mat("Blockout", "Casa_Verde", "B9D99A"),
                Mat("Mundo", "Casa_Rosa", "F4C2C2"), Mat("Mundo", "Casa_Amarela", "F4E3A1"),
            };
        }

        if (kit.houseRoofs == null || kit.houseRoofs.Length == 0)
        {
            kit.houseRoofs = new[]
            {
                Mat("Blockout", "Telhado_Laranja", "C9603A"), Mat("Blockout", "Telhado_Roxo", "6E4A8C"), Mat("Mundo", "Telhado_Verde", "4E7A5A"),
            };
        }

        kit.door = Keep(kit.door, "Blockout", "Porta", "5A3E5D");
        kit.window = Keep(kit.window, "Blockout", "Janela", "BFF3F7");
        if (kit.buildingColors == null || kit.buildingColors.Length == 0)
        {
            kit.buildingColors = new[]
            {
                Mat("Blockout", "Predio", "7F93AD"), Mat("Mundo", "Predio_Areia", "D8C7A6"),
                Mat("Mundo", "Predio_Rosa", "D99AA8"), Mat("Mundo", "Predio_Menta", "9ED2C0"),
            };
        }

        if (kit.awningColors == null || kit.awningColors.Length == 0)
        {
            kit.awningColors = new[]
            {
                Mat("Mundo", "Toldo_Vermelho", "E0607E"), Mat("Mundo", "Toldo_Amarelo", "F2C14E"),
                Mat("Mundo", "Toldo_Azul", "4F86C6"), Mat("Mundo", "Toldo_Verde", "4E9A63"),
            };
        }

        kit.warehouse = Keep(kit.warehouse, "Regiao", "Galpao", "9AA3AE");
        kit.warehouseRoof = Keep(kit.warehouseRoof, "Regiao", "Galpao_Teto", "5B6470");
        kit.barn = Keep(kit.barn, "Mundo", "Celeiro", "B8463A");
        kit.barnRoof = Keep(kit.barnRoof, "Mundo", "Celeiro_Teto", "5A3A34");
        kit.trunk = Keep(kit.trunk, "Blockout", "Tronco", "7A5C48");
        if (kit.canopies == null || kit.canopies.Length == 0)
        {
            kit.canopies = new[]
            {
                Mat("Blockout", "Copa_Turquesa", "3FB8A8"), Mat("Blockout", "Copa_Lilas", "B48AD8"), Mat("Mundo", "Copa_Verde", "5DBB63"),
            };
        }

        kit.bush = Keep(kit.bush, "Mundo", "Arbusto", "4FA36B");
        kit.rock = Keep(kit.rock, "Mundo", "Pedra", "9A97A0");
        kit.crop = Keep(kit.crop, "Mundo", "Plantacao", "7DBA4A");
        kit.flower = Keep(kit.flower, "Mundo", "Flor", "F28AB3");
        kit.wood = Keep(kit.wood, "Regiao", "Madeira", "9C6B45");
        kit.bench = Keep(kit.bench, "Blockout", "Banco", "C9733D");
        kit.fence = Keep(kit.fence, "Mundo", "Cerca", "D8C9A8");
        kit.lampPole = Keep(kit.lampPole, "Blockout", "Poste", "4C5566");
        kit.lampLight = Keep(kit.lampLight, "Blockout", "Luz", "7DF9FF");
        kit.crate = Keep(kit.crate, "Props", "Caixa", "D9A066");
        kit.bin = Keep(kit.bin, "Props", "Lixeira", "5FA8A0");
        kit.hay = Keep(kit.hay, "Mundo", "Feno", "E8C35A");
        if (kit.containerColors == null || kit.containerColors.Length == 0)
        {
            kit.containerColors = new[]
            {
                Mat("Regiao", "Conteiner_Vermelho", "C8503C"), Mat("Regiao", "Conteiner_Azul", "3E78B2"),
                Mat("Regiao", "Conteiner_Amarelo", "E3B23C"), Mat("Regiao", "Conteiner_Verde", "4E9A63"),
            };
        }

        if (kit.carModel == null)
        {
            kit.carModel = AssetDatabase.LoadAssetAtPath<GameObject>(CarModelPath);
            if (kit.carModel == null)
            {
                Debug.LogWarning("Não achei " + CarModelPath + ": os blocos sorteados ficam sem carros estacionados.");
            }
        }

        EditorUtility.SetDirty(kit);
        AssetDatabase.SaveAssets();
        return kit;
    }

    // Mantém o material já escolhido no kit; se não houver, usa/cria o material padrão.
    private static Material Keep(Material current, string folder, string name, string hex)
    {
        return current != null ? current : Mat(folder, name, hex);
    }

    // Usa o material existente (mantendo cores que você mudou) ou cria um novo.
    private static Material Mat(string folder, string name, string hex)
    {
        string path = $"Assets/Art/Materials/{folder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "Materials");
        EnsureFolder("Assets/Art/Materials", folder);
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        material.SetFloat("_Smoothness", 0.2f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // ---------- Ajudantes ----------

    private static Rect BlockRect(Vector2Int coord)
    {
        return new Rect(GridOrigin.x + coord.x * BlockSize, GridOrigin.y + coord.y * BlockSize, BlockSize, BlockSize);
    }

    private static GameObject FindRoot(Scene scene, string name)
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

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
