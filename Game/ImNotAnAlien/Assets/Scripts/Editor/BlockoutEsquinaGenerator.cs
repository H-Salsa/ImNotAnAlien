using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 2: monta o blockout (versão com formas simples)
// da esquina da Versão 0.1. Use pelo menu: ImNotAnAlien > Gerar Blockout da Esquina.
// Tudo é criado dentro do objeto "Esquina_Blockout" e pode ser desfeito com Ctrl+Z.
public static class BlockoutEsquinaGenerator
{
    private const string RootName = "Esquina_Blockout";
    private const string MaterialFolder = "Assets/Art/Materials/Blockout";

    // Medidas em metros. O cruzamento fica no centro (0, 0, 0).
    private const float MapSize = 80f;       // área total: 80 x 80 m
    private const float RoadWidth = 10f;     // largura de cada rua
    private const float SidewalkHeight = 0.15f;
    private const float LawnInset = 3f;      // largura da calçada em volta de cada quarteirão

    private static float BlockSize => (MapSize - RoadWidth) / 2f;    // 35 m
    private static float BlockCenter => RoadWidth / 2f + BlockSize / 2f; // 22.5 m
    private static float LawnSize => BlockSize - LawnInset * 2f;     // 29 m
    private static float LawnTop => SidewalkHeight + 0.02f;

    private static Material grass, asphalt, sidewalk, laneMark, path, water;
    private static Material trunk, canopyA, canopyB, bench;
    private static Material building, buildingSign, door, window, lampPole, lampLight;
    private static Material[] houseWalls, houseRoofs;

    [MenuItem("ImNotAnAlien/Gerar Blockout da Esquina")]
    public static void Generate()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            bool regenerate = EditorUtility.DisplayDialog(
                "Blockout já existe",
                "Já existe um 'Esquina_Blockout' na cena.\nApagar e gerar de novo? (Ctrl+Z desfaz)",
                "Gerar de novo",
                "Cancelar");
            if (!regenerate)
            {
                return;
            }
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Gerar Blockout da Esquina");

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        CreateMaterials();

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Gerar Blockout da Esquina");

        BuildGroundAndRoads(root.transform);
        BuildSidewalks(root.transform);
        BuildPark(Group("Parque", root.transform, new Vector3(BlockCenter, 0f, BlockCenter)));
        BuildBuilding(Group("Predio", root.transform, new Vector3(-BlockCenter, 0f, BlockCenter)));
        BuildHouseBlock(Group("Casas_Oeste", root.transform, new Vector3(-BlockCenter, 0f, -BlockCenter)), 0);
        BuildHouseBlock(Group("Casas_Leste", root.transform, new Vector3(BlockCenter, 0f, -BlockCenter)), 2);
        BuildStreetLamps(Group("Postes", root.transform, Vector3.zero));
        BuildBoundaries(Group("Limites_Invisiveis", root.transform, Vector3.zero));

        DisableOldGround();
        MovePlayerToStart();

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("Blockout da esquina gerado. Salve a cena com Ctrl+S.");
    }

    // ---------- Partes do cenário ----------

    private static void BuildGroundAndRoads(Transform root)
    {
        Transform streets = Group("Ruas", root, Vector3.zero);

        Box("Chao_Base", streets, new Vector3(0f, -0.1f, 0f), new Vector3(MapSize, 0.2f, MapSize), grass);
        Box("Rua_LesteOeste", streets, new Vector3(0f, 0.01f, 0f), new Vector3(MapSize, 0.02f, RoadWidth), asphalt);
        Box("Rua_NorteSul", streets, new Vector3(0f, 0.01f, 0f), new Vector3(RoadWidth, 0.02f, MapSize), asphalt);

        // Faixas tracejadas no meio das ruas (pulando o cruzamento).
        Transform marks = Group("Faixas", streets, Vector3.zero);
        for (float p = -MapSize / 2f + 2f; p < MapSize / 2f; p += 4f)
        {
            if (Mathf.Abs(p) < RoadWidth / 2f + 1f)
            {
                continue;
            }

            NoCollider(Box("Faixa", marks, new Vector3(p, 0.025f, 0f), new Vector3(2f, 0.01f, 0.25f), laneMark));
            NoCollider(Box("Faixa", marks, new Vector3(0f, 0.025f, p), new Vector3(0.25f, 0.01f, 2f), laneMark));
        }
    }

    private static void BuildSidewalks(Transform root)
    {
        Transform sidewalks = Group("Calcadas", root, Vector3.zero);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Box("Calcada",
                    sidewalks,
                    new Vector3(sx * BlockCenter, SidewalkHeight / 2f, sz * BlockCenter),
                    new Vector3(BlockSize, SidewalkHeight, BlockSize),
                    sidewalk);
            }
        }
    }

    private static void BuildPark(Transform park)
    {
        Lawn(park);

        // Caminho diagonal ligando a esquina do cruzamento ao fundo do parque.
        Box("Caminho", park, new Vector3(0f, LawnTop + 0.01f, 0f),
            new Vector3(2.5f, 0.02f, LawnSize * 1.41f), path, new Vector3(0f, 45f, 0f));

        // Lago (sem colisor: dá para andar por cima por enquanto).
        GameObject pond = Primitive(PrimitiveType.Cylinder, "Lago", park,
            new Vector3(6f, LawnTop + 0.02f, -6f), new Vector3(8f, 0.02f, 6f), water);
        NoCollider(pond);

        Vector2[] trees =
        {
            new Vector2(-10f, -3f), new Vector2(-11f, 6f), new Vector2(-4f, 10f), new Vector2(3f, 11f),
            new Vector2(-6f, -11f), new Vector2(11f, 1f), new Vector2(12f, -11f), new Vector2(1f, -12f),
        };
        Transform treeGroup = Group("Arvores", park, Vector3.zero);
        for (int i = 0; i < trees.Length; i++)
        {
            float canopySize = 3.5f + (i % 3) * 0.6f;
            Transform tree = Group("Arvore", treeGroup, new Vector3(trees[i].x, LawnTop, trees[i].y));
            Primitive(PrimitiveType.Cylinder, "Tronco", tree, new Vector3(0f, 2f, 0f), new Vector3(0.5f, 2f, 0.5f), trunk);
            Primitive(PrimitiveType.Sphere, "Copa", tree, new Vector3(0f, 4f + canopySize * 0.35f, 0f),
                Vector3.one * canopySize, i % 2 == 0 ? canopyA : canopyB);
        }

        Transform benches = Group("Bancos", park, Vector3.zero);
        Vector2[] benchSpots = { new Vector2(-2.5f, 2.5f), new Vector2(2.5f, -2.5f), new Vector2(5f, 9f) };
        foreach (Vector2 spot in benchSpots)
        {
            Box("Banco", benches, new Vector3(spot.x, LawnTop + 0.25f, spot.y), new Vector3(2f, 0.5f, 0.6f),
                bench, new Vector3(0f, -45f, 0f)); // paralelo ao caminho
        }
    }

    private static void BuildBuilding(Transform block)
    {
        Lawn(block);

        const float width = 16f;
        const float height = 20f;
        Box("Corpo", block, new Vector3(0f, LawnTop + height / 2f, 0f), new Vector3(width, height, width), building);

        // Fachada voltada para a rua Leste-Oeste (lado sul do quarteirão).
        float front = -width / 2f - 0.1f;
        Box("Letreiro", block, new Vector3(0f, LawnTop + 6f, front), new Vector3(8f, 1.5f, 0.3f), buildingSign);
        Box("Porta", block, new Vector3(0f, LawnTop + 1.4f, front), new Vector3(3f, 2.8f, 0.2f), door);
        Primitive(PrimitiveType.Cylinder, "Antena", block, new Vector3(4f, LawnTop + height + 3f, 4f),
            new Vector3(0.3f, 3f, 0.3f), lampPole);
        Primitive(PrimitiveType.Sphere, "Antena_Luz", block, new Vector3(4f, LawnTop + height + 6.2f, 4f),
            Vector3.one * 0.8f, lampLight);
    }

    private static void BuildHouseBlock(Transform block, int colorOffset)
    {
        Lawn(block);
        // Frente das casas voltada para a rua Leste-Oeste (lado norte do quarteirão).
        BuildHouse(Group("Casa_A", block, new Vector3(-7f, LawnTop, 3f)), colorOffset);
        BuildHouse(Group("Casa_B", block, new Vector3(7f, LawnTop, 3f)), colorOffset + 1);
    }

    private static void BuildHouse(Transform house, int colorIndex)
    {
        const float size = 9f;
        const float wallHeight = 4.5f;
        Material wall = houseWalls[colorIndex % houseWalls.Length];
        Material roof = houseRoofs[colorIndex % houseRoofs.Length];

        Box("Paredes", house, new Vector3(0f, wallHeight / 2f, 0f), new Vector3(size, wallHeight, size), wall);
        // Telhado: um cubo girado 45 graus forma o "triângulo" de duas águas.
        float roofSide = size / Mathf.Sqrt(2f);
        Box("Telhado", house, new Vector3(0f, wallHeight, 0f), new Vector3(roofSide, roofSide, size + 0.6f),
            roof, new Vector3(0f, 0f, 45f));

        float front = size / 2f + 0.1f;
        Box("Porta", house, new Vector3(0f, 1.3f, front), new Vector3(1.6f, 2.6f, 0.2f), door);
        Box("Janela_E", house, new Vector3(-2.8f, 2.6f, front), new Vector3(1.6f, 1.4f, 0.15f), window);
        Box("Janela_D", house, new Vector3(2.8f, 2.6f, front), new Vector3(1.6f, 1.4f, 0.15f), window);
    }

    private static void BuildStreetLamps(Transform lamps)
    {
        float corner = RoadWidth / 2f + 1.5f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Transform lamp = Group("Poste", lamps, new Vector3(sx * corner, SidewalkHeight, sz * corner));
                Primitive(PrimitiveType.Cylinder, "Haste", lamp, new Vector3(0f, 3f, 0f), new Vector3(0.2f, 3f, 0.2f), lampPole);
                Primitive(PrimitiveType.Sphere, "Luz", lamp, new Vector3(0f, 6.3f, 0f), Vector3.one * 0.7f, lampLight);
            }
        }
    }

    // Paredes invisíveis na borda do mapa para o jogador não cair.
    private static void BuildBoundaries(Transform walls)
    {
        float half = MapSize / 2f;
        Vector3 sideX = new Vector3(1f, 20f, MapSize);
        Vector3 sideZ = new Vector3(MapSize, 20f, 1f);
        Invisible(Box("Limite_Leste", walls, new Vector3(half + 0.5f, 10f, 0f), sideX, grass));
        Invisible(Box("Limite_Oeste", walls, new Vector3(-half - 0.5f, 10f, 0f), sideX, grass));
        Invisible(Box("Limite_Norte", walls, new Vector3(0f, 10f, half + 0.5f), sideZ, grass));
        Invisible(Box("Limite_Sul", walls, new Vector3(0f, 10f, -half - 0.5f), sideZ, grass));
    }

    // ---------- Ajustes na cena existente ----------

    private static void DisableOldGround()
    {
        GameObject oldGround = GameObject.Find("Chao");
        if (oldGround != null)
        {
            Undo.RecordObject(oldGround, "Desativar chão antigo");
            oldGround.SetActive(false);
            Debug.Log("O chão antigo 'Chao' foi desativado (não apagado). Você pode apagá-lo se quiser.");
        }
    }

    private static void MovePlayerToStart()
    {
        PrototypeThirdPersonController player = Object.FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player == null)
        {
            return;
        }

        Undo.RecordObject(player.transform, "Mover jogador para o início");
        player.transform.SetPositionAndRotation(new Vector3(2.5f, 1.2f, -18f), Quaternion.identity);
    }

    // ---------- Ajudantes ----------

    private static void Lawn(Transform parent)
    {
        Box("Gramado", parent, new Vector3(0f, SidewalkHeight + 0.01f, 0f), new Vector3(LawnSize, 0.02f, LawnSize), grass);
    }

    private static Transform Group(string name, Transform parent, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.isStatic = true;
        return go.transform;
    }

    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material,
        Vector3? euler = null)
    {
        return Primitive(PrimitiveType.Cube, name, parent, position, size, material, euler);
    }

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position,
        Vector3 scale, Material material, Vector3? euler = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        if (euler.HasValue)
        {
            go.transform.localRotation = Quaternion.Euler(euler.Value);
        }

        go.GetComponent<Renderer>().sharedMaterial = material;
        go.isStatic = true; // objetos estáticos serão usados pelo NavMesh na Missão 3
        return go;
    }

    private static void NoCollider(GameObject go)
    {
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    private static void Invisible(GameObject go)
    {
        go.GetComponent<Renderer>().enabled = false;
    }

    // ---------- Materiais ----------
    // Cores provisórias inspiradas na Direção Artística (tons pastel e retrô-futuristas).
    // Os materiais ficam em Assets/Art/Materials/Blockout. Se você mudar uma cor lá,
    // gerar de novo NÃO sobrescreve a sua mudança.

    private static void CreateMaterials()
    {
        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "Materials");
        EnsureFolder("Assets/Art/Materials", "Blockout");

        grass = Mat("Grama", Hex("6FBF8E"));
        asphalt = Mat("Asfalto", Hex("3B4252"));
        sidewalk = Mat("Calcada", Hex("CFC8B8"));
        laneMark = Mat("Faixa", Hex("F2C14E"));
        path = Mat("Caminho", Hex("E3D5B0"));
        water = Mat("Agua", Hex("5EC8D8"));
        trunk = Mat("Tronco", Hex("7A5C48"));
        canopyA = Mat("Copa_Turquesa", Hex("3FB8A8"));
        canopyB = Mat("Copa_Lilas", Hex("B48AD8"));
        bench = Mat("Banco", Hex("C9733D"));
        building = Mat("Predio", Hex("7F93AD"));
        buildingSign = Mat("Letreiro", Hex("E055A8"));
        door = Mat("Porta", Hex("5A3E5D"));
        window = Mat("Janela", Hex("BFF3F7"));
        lampPole = Mat("Poste", Hex("4C5566"));
        lampLight = Mat("Luz", Hex("7DF9FF"));

        houseWalls = new[]
        {
            Mat("Casa_Creme", Hex("F1E3C6")),
            Mat("Casa_Turquesa", Hex("8FD6CF")),
            Mat("Casa_Lilas", Hex("C8B2E6")),
            Mat("Casa_Verde", Hex("B9D99A")),
        };
        houseRoofs = new[]
        {
            Mat("Telhado_Laranja", Hex("C9603A")),
            Mat("Telhado_Roxo", Hex("6E4A8C")),
        };
    }

    private static Material Mat(string name, Color color)
    {
        string assetPath = $"{MaterialFolder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        material = new Material(shader) { color = color };
        material.SetFloat("_Smoothness", 0.15f);
        AssetDatabase.CreateAsset(material, assetPath);
        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return color;
    }
}
