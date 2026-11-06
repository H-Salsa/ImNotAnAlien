using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Ferramenta de editor da Missão 11 — Região 1 "Vilarejo do Bilu" (decisão D18).
// Menu: ImNotAnAlien > Gerar Regiao 1 (Vilarejo do Bilu)
// Rode com a cena Prototype aberta. A ferramenta:
// 1. Salva uma CÓPIA da cena como "Regiao1_VilarejoDoBilu" e abre a cópia (a Prototype não muda).
// 2. Mantém a esquina (com seus ajustes) como Setor 1 e constrói em volta os setores 2, 3 e 4,
//    as ruas, o rio raso com margens em rampa e as 2 pontes.
// 3. Espalha carros, caixas, lixeiras e contêineres; calcula o NavMesh; distribui os alienígenas.
//
// Mapa (norte = +Z). A esquina original fica centrada em (0, 0).
//   Setor 1 Esquina        x -60..90   z  -95..60
//   Setor 2 Vila das Casas x  90..240  z  -95..60
//   Rio (raso)             x -60..240  z -120..-95   pontes em x = 0 e x = 165
//   Setor 3 Parque e Lago  x -60..90   z -240..-120
//   Setor 4 Margem do Rio  x  90..240  z -240..-120
public static class Region1Generator
{
    private const string ScenePath = "Assets/Scenes/Regiao1_VilarejoDoBilu.unity";
    private const string DataFolder = "Assets/Scenes/Regiao1_VilarejoDoBilu";
    private const string RegionMaterialFolder = "Assets/Art/Materials/Regiao";
    private const string RegionName = "Vilarejo do Bilu";

    private const float MinX = -60f, MaxX = 240f, MinZ = -240f, MaxZ = 60f;
    private const float RiverNorth = -95f, RiverSouth = -120f;
    private const float RiverBedY = -1.2f, WaterY = -0.35f, BankLength = 5f;
    private const float RoadWidth = 10f;
    private static readonly float[] BridgeX = { 0f, 165f };

    private struct Sector
    {
        public string name;
        public Rect area; // x/z
        public int civils;
        public int police;
    }

    // Setor 1 já tem 8 civis e 3 policiais da esquina; aqui só os que faltam (total 50 + 15).
    private static readonly Sector[] Sectors =
    {
        new Sector { name = "Esquina", area = Rect.MinMaxRect(-60f, -95f, 90f, 60f), civils = 1, police = 0 },
        new Sector { name = "Vila das Casas", area = Rect.MinMaxRect(90f, -95f, 240f, 60f), civils = 14, police = 4 },
        new Sector { name = "Parque e Lago", area = Rect.MinMaxRect(-60f, -240f, 90f, -120f), civils = 12, police = 3 },
        new Sector { name = "Margem do Rio", area = Rect.MinMaxRect(90f, -240f, 240f, -120f), civils = 15, police = 5 },
    };

    private static System.Random random;
    private static Material grass, asphalt, laneMark, water, riverBed, wood, warehouse, warehouseRoof, door, window,
        trunk, canopyA, canopyB, bench, lampPole, lampLight, crate, bin, carBody, carGlass, wheel;
    private static Material[] houseWalls, houseRoofs, containerColors;

    [MenuItem("ImNotAnAlien/Gerar Regiao 1 (Vilarejo do Bilu)")]
    public static void Generate()
    {
        // ---------- Verificações ----------
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Pare o Play", "Saia do Play Mode antes de gerar a região.", "OK");
            return;
        }

        GameObject esquina = GameObject.Find("Esquina_Blockout");
        PrototypeThirdPersonController player = Object.FindFirstObjectByType<PrototypeThirdPersonController>();
        if (esquina == null || player == null || GameObject.Find("Sistemas") == null)
        {
            EditorUtility.DisplayDialog("Cena errada",
                "Abra a cena Prototype (com Esquina_Blockout, o personagem e o objeto Sistemas) e rode de novo.", "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Região já existe",
                "A cena Regiao1_VilarejoDoBilu já existe e será SUBSTITUÍDA por uma nova cópia da cena atual.\nContinuar?",
                "Substituir", "Cancelar"))
        {
            return;
        }

        if (!EditorUtility.DisplayDialog("Gerar Vilarejo do Bilu",
                "Vou salvar a cena atual e criar uma CÓPIA chamada Regiao1_VilarejoDoBilu.\n" +
                "A região será montada na cópia; a cena Prototype não muda.\n\nIsso pode levar alguns segundos.",
                "Gerar", "Cancelar"))
        {
            return;
        }

        Vector3 playerStart = player.transform.position; // a cena atual será fechada; guarda antes

        // ---------- 1. Cópia da cena ----------
        var current = EditorSceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(current);
        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets/Scenes", "Regiao1_VilarejoDoBilu");
        EditorSceneManager.SaveScene(current, ScenePath, true);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        random = new System.Random(1234); // mesma semente = mesma região toda vez
        LoadMaterials();

        try
        {
            EditorUtility.DisplayProgressBar("Vilarejo do Bilu", "Preparando a esquina...", 0.05f);
            esquina = GameObject.Find("Esquina_Blockout");
            GameObject root = new GameObject("Vilarejo_do_Bilu");
            esquina.transform.SetParent(root.transform, true);
            PrepareEsquina(esquina);

            EditorUtility.DisplayProgressBar("Vilarejo do Bilu", "Terreno, ruas e rio...", 0.15f);
            BuildTerrain(Group("Terreno", root.transform));
            BuildRoads(Group("Ruas", root.transform));
            BuildRiver(Group("Rio", root.transform));
            BuildBridges(Group("Pontes", root.transform));

            EditorUtility.DisplayProgressBar("Vilarejo do Bilu", "Setores...", 0.3f);
            BuildEsquinaExtension(Group("Setor1_Esquina_Ampliacao", root.transform));
            BuildVilaDasCasas(Group("Setor2_Vila_das_Casas", root.transform));
            BuildParque(Group("Setor3_Parque_e_Lago", root.transform));
            BuildDocas(Group("Setor4_Margem_do_Rio", root.transform));
            BuildBoundaries(Group("Limites_Invisiveis", root.transform));

            EditorUtility.DisplayProgressBar("Vilarejo do Bilu", "Objetos arremessáveis...", 0.45f);
            BuildThrowables();
            BuildWaterTrigger();

            EditorUtility.DisplayProgressBar("Vilarejo do Bilu", "Calculando o NavMesh (pode demorar)...", 0.6f);
            bool navMeshOk = BakeNavMesh(root);

            EditorUtility.DisplayProgressBar("Vilarejo do Bilu", "Espalhando alienígenas...", 0.85f);
            int spawned = navMeshOk ? SpawnAliens(playerStart) : 0;

            ConfigureSystems();
            AddSceneToBuild();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log($"Vilarejo do Bilu gerado: {spawned} alienígenas novos. Cena salva em {ScenePath}.");
            EditorUtility.DisplayDialog("Vilarejo do Bilu pronto!",
                navMeshOk
                    ? $"Região gerada e salva.\n{spawned} alienígenas novos foram espalhados pelos setores.\n\nDê Play para explorar!"
                    : "Região gerada, mas o NavMesh não foi calculado.\nSelecione 'Vilarejo_do_Bilu', clique em Bake no NavMeshSurface e rode 'Criar Alienigenas' manualmente.",
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---------- Esquina (Setor 1 original) ----------

    private static void PrepareEsquina(GameObject esquina)
    {
        // As paredes invisíveis da esquina bloqueariam a região nova.
        Transform oldLimits = esquina.transform.Find("Limites_Invisiveis");
        if (oldLimits != null)
        {
            Object.DestroyImmediate(oldLimits.gameObject);
        }

        // O NavMesh passa a ser calculado para a região inteira (no objeto raiz).
        if (esquina.TryGetComponent(out NavMeshSurface oldSurface))
        {
            Object.DestroyImmediate(oldSurface);
        }
    }

    // ---------- Terreno, ruas, rio, pontes ----------

    private static void BuildTerrain(Transform parent)
    {
        // Um pouco abaixo do chão da esquina (que fica por cima onde se sobrepõem).
        Box("Chao_Norte", parent, Center(MinX, MaxX, RiverNorth, MaxZ, -0.12f), new Vector3(MaxX - MinX, 0.2f, MaxZ - RiverNorth), grass);
        Box("Chao_Sul", parent, Center(MinX, MaxX, MinZ, RiverSouth, -0.12f), new Vector3(MaxX - MinX, 0.2f, RiverSouth - MinZ), grass);
    }

    private static void BuildRoads(Transform parent)
    {
        // Leste-Oeste principal (continua a rua da esquina) e Leste-Oeste do sul.
        Road(parent, "Rua_LO_Principal", MinX, MaxX, 0f, true);
        Road(parent, "Rua_LO_Sul", MinX, MaxX, -180f, true);

        // Norte-Sul nas duas pontes (interrompidas pelo rio; as pontes completam).
        foreach (float x in BridgeX)
        {
            Road(parent, $"Rua_NS_{x}_Norte", RiverNorth, MaxZ, x, false);
            Road(parent, $"Rua_NS_{x}_Sul", MinZ, RiverSouth, x, false);
        }
    }

    private static void Road(Transform parent, string name, float from, float to, float at, bool eastWest)
    {
        float length = to - from;
        float middle = (from + to) / 2f;
        Vector3 position = eastWest ? new Vector3(middle, 0.01f, at) : new Vector3(at, 0.01f, middle);
        Vector3 size = eastWest ? new Vector3(length, 0.02f, RoadWidth) : new Vector3(RoadWidth, 0.02f, length);
        Box(name, parent, position, size, asphalt);

        // Faixas tracejadas.
        for (float p = from + 2f; p < to - 1f; p += 4f)
        {
            Vector3 dash = eastWest ? new Vector3(p, 0.025f, at) : new Vector3(at, 0.025f, p);
            Vector3 dashSize = eastWest ? new Vector3(2f, 0.01f, 0.25f) : new Vector3(0.25f, 0.01f, 2f);
            NoCollider(Box("Faixa", parent, dash, dashSize, laneMark));
        }
    }

    private static void BuildRiver(Transform parent)
    {
        float width = MaxX - MinX;
        float centerX = (MinX + MaxX) / 2f;
        float bedNorth = RiverNorth - BankLength;
        float bedSouth = RiverSouth + BankLength;

        // Fundo do rio.
        Box("Fundo", parent, new Vector3(centerX, RiverBedY - 0.1f, (bedNorth + bedSouth) / 2f),
            new Vector3(width, 0.2f, bedNorth - bedSouth), riverBed);

        // Margens em rampa (dá para sair andando).
        float drop = -0.02f - RiverBedY;
        float slope = Mathf.Sqrt(BankLength * BankLength + drop * drop);
        float angle = Mathf.Atan2(drop, BankLength) * Mathf.Rad2Deg;
        float rampY = (-0.02f + RiverBedY) / 2f - 0.1f;
        Box("Margem_Norte", parent, new Vector3(centerX, rampY, RiverNorth - BankLength / 2f), new Vector3(width, 0.2f, slope),
            riverBed, new Vector3(-angle, 0f, 0f));
        Box("Margem_Sul", parent, new Vector3(centerX, rampY, RiverSouth + BankLength / 2f), new Vector3(width, 0.2f, slope),
            riverBed, new Vector3(angle, 0f, 0f));

        // Superfície da água (só visual).
        NoCollider(Box("Agua", parent, new Vector3(centerX, WaterY, (RiverNorth + RiverSouth) / 2f),
            new Vector3(width, 0.02f, RiverNorth - RiverSouth), water));
    }

    private static void BuildBridges(Transform parent)
    {
        float length = RiverNorth - RiverSouth + 6f;
        float centerZ = (RiverNorth + RiverSouth) / 2f;
        foreach (float x in BridgeX)
        {
            Transform bridge = Group($"Ponte_{x}", parent, new Vector3(x, 0f, centerZ));
            Box("Tabuleiro", bridge, new Vector3(0f, -0.18f, 0f), new Vector3(RoadWidth + 2f, 0.4f, length), asphalt);
            Box("Mureta_O", bridge, new Vector3(-(RoadWidth / 2f + 0.85f), 0.5f, 0f), new Vector3(0.3f, 1f, length), lampPole);
            Box("Mureta_L", bridge, new Vector3(RoadWidth / 2f + 0.85f, 0.5f, 0f), new Vector3(0.3f, 1f, length), lampPole);
        }
    }

    // ---------- Setores ----------

    // Setor 1: casas e árvores em volta da esquina original (que ocupa -40..40).
    private static void BuildEsquinaExtension(Transform parent)
    {
        int color = 0;
        foreach (float x in new[] { 58f, 76f })
        {
            House(parent, new Vector3(x, 0f, 17f), 180f, color++);
            House(parent, new Vector3(x, 0f, -17f), 0f, color++);
        }

        foreach (float z in new[] { -55f, -75f })
        {
            House(parent, new Vector3(17f, 0f, z), -90f, color++);
            House(parent, new Vector3(-17f, 0f, z), 90f, color++);
        }

        Transform trees = Group("Arvores", parent, Vector3.zero);
        ScatterTrees(trees, Rect.MinMaxRect(-58f, -93f, -44f, 58f), 7);   // faixa oeste
        ScatterTrees(trees, Rect.MinMaxRect(-38f, 44f, 88f, 58f), 8);     // faixa norte
        ScatterTrees(trees, Rect.MinMaxRect(30f, -93f, 88f, -45f), 6);    // faixa sudeste
        StreetLamps(parent, new[] { new Vector3(48f, 0f, 6.5f), new Vector3(86f, 0f, -6.5f), new Vector3(6.5f, 0f, -60f), new Vector3(-6.5f, 0f, -88f) });
    }

    // Setor 2: quadras de casas em volta do cruzamento (165, 0).
    private static void BuildVilaDasCasas(Transform parent)
    {
        int color = 1;
        float[] westColumn = { 104f, 121f, 138f };
        float[] eastColumn = { 182f, 199f, 216f };
        foreach (float[] columns in new[] { westColumn, eastColumn })
        {
            foreach (float x in columns)
            {
                House(parent, new Vector3(x, 0f, 17f), 180f, color++);  // de frente para a rua principal
                House(parent, new Vector3(x, 0f, 40f), 180f, color++);
                House(parent, new Vector3(x, 0f, -17f), 0f, color++);
                House(parent, new Vector3(x, 0f, -40f), 0f, color++);
            }
        }

        Transform trees = Group("Arvores", parent, Vector3.zero);
        ScatterTrees(trees, Rect.MinMaxRect(95f, -92f, 157f, -52f), 7);
        ScatterTrees(trees, Rect.MinMaxRect(173f, -92f, 236f, -52f), 7);
        StreetLamps(parent, new[] { new Vector3(120f, 0f, 6.5f), new Vector3(210f, 0f, -6.5f), new Vector3(171.5f, 0f, 30f), new Vector3(158.5f, 0f, -60f) });
    }

    // Setor 3: parque grande com lago, árvores e bancos.
    private static void BuildParque(Transform parent)
    {
        Vector3 lake = new Vector3(-30f, 0.02f, -150f);
        NoCollider(Primitive(PrimitiveType.Cylinder, "Lago", parent, lake, new Vector3(34f, 0.02f, 22f), water));

        Transform trees = Group("Arvores", parent, Vector3.zero);
        int planted = 0;
        for (int attempt = 0; attempt < 400 && planted < 32; attempt++)
        {
            Vector3 p = new Vector3(Range(-56f, 86f), 0f, Range(-236f, -126f));
            bool onRoad = Mathf.Abs(p.x) < 8f || Mathf.Abs(p.z + 180f) < 8f;
            bool inLake = Sqr((p.x - lake.x) / 19f) + Sqr((p.z - lake.z) / 13f) < 1f;
            if (onRoad || inLake)
            {
                continue;
            }

            Tree(trees, p, planted++);
        }

        Transform benches = Group("Bancos", parent, Vector3.zero);
        Vector3[] benchSpots =
        {
            new Vector3(-30f, 0f, -134f), new Vector3(-12f, 0f, -150f), new Vector3(-48f, 0f, -150f), new Vector3(-30f, 0f, -166f),
            new Vector3(12f, 0f, -140f), new Vector3(-12f, 0f, -200f), new Vector3(12f, 0f, -220f), new Vector3(40f, 0f, -170f),
        };
        foreach (Vector3 spot in benchSpots)
        {
            Box("Banco", benches, spot + Vector3.up * 0.25f, new Vector3(2f, 0.5f, 0.6f), bench, new Vector3(0f, Range(0f, 360f), 0f));
        }

        StreetLamps(parent, new[] { new Vector3(6.5f, 0f, -135f), new Vector3(-6.5f, 0f, -200f), new Vector3(40f, 0f, -173.5f) });
    }

    // Setor 4: docas com galpões, píeres sobre o rio e contêineres.
    private static void BuildDocas(Transform parent)
    {
        Transform buildings = Group("Galpoes", parent, Vector3.zero);
        Vector3[] warehouses = { new Vector3(118f, 0f, -150f), new Vector3(212f, 0f, -150f), new Vector3(118f, 0f, -212f), new Vector3(212f, 0f, -212f) };
        foreach (Vector3 spot in warehouses)
        {
            Transform shed = Group("Galpao", buildings, spot);
            Box("Paredes", shed, new Vector3(0f, 4.5f, 0f), new Vector3(22f, 9f, 14f), warehouse);
            Box("Teto", shed, new Vector3(0f, 9.2f, 0f), new Vector3(23f, 0.4f, 15f), warehouseRoof);
            Box("Portao", shed, new Vector3(0f, 2.8f, 7.05f), new Vector3(7f, 5.6f, 0.2f), door);
        }

        Transform piers = Group("Pieres", parent, Vector3.zero);
        foreach (float x in new[] { 110f, 135f, 195f, 225f })
        {
            Box("Pier", piers, new Vector3(x, -0.18f, RiverSouth + 6f), new Vector3(6f, 0.4f, 12f), wood);
        }

        StreetLamps(parent, new[] { new Vector3(158.5f, 0f, -140f), new Vector3(171.5f, 0f, -220f), new Vector3(140f, 0f, -186.5f) });
    }

    private static void BuildBoundaries(Transform parent)
    {
        float width = MaxX - MinX + 2f;
        float depth = MaxZ - MinZ + 2f;
        float cx = (MinX + MaxX) / 2f;
        float cz = (MinZ + MaxZ) / 2f;
        Invisible(Box("Limite_Leste", parent, new Vector3(MaxX + 0.5f, 10f, cz), new Vector3(1f, 20f, depth), grass));
        Invisible(Box("Limite_Oeste", parent, new Vector3(MinX - 0.5f, 10f, cz), new Vector3(1f, 20f, depth), grass));
        Invisible(Box("Limite_Norte", parent, new Vector3(cx, 10f, MaxZ + 0.5f), new Vector3(width, 20f, 1f), grass));
        Invisible(Box("Limite_Sul", parent, new Vector3(cx, 10f, MinZ - 0.5f), new Vector3(width, 20f, 1f), grass));
    }

    // ---------- Casas, árvores, postes ----------

    // Casa igual às da esquina (frente para +Z local), destrutível.
    private static void House(Transform parent, Vector3 position, float yaw, int colorIndex)
    {
        const float size = 9f;
        const float wallHeight = 4.5f;
        Transform house = Group($"Casa_{parent.childCount + 1:00}", parent, position);
        house.localRotation = Quaternion.Euler(0f, yaw, 0f);

        Box("Paredes", house, new Vector3(0f, wallHeight / 2f, 0f), new Vector3(size, wallHeight, size), houseWalls[colorIndex % houseWalls.Length]);
        float roofSide = size / Mathf.Sqrt(2f);
        Box("Telhado", house, new Vector3(0f, wallHeight, 0f), new Vector3(roofSide, roofSide, size + 0.6f),
            houseRoofs[colorIndex % houseRoofs.Length], new Vector3(0f, 0f, 45f));
        float front = size / 2f + 0.1f;
        Box("Porta", house, new Vector3(0f, 1.3f, front), new Vector3(1.6f, 2.6f, 0.2f), door);
        Box("Janela_E", house, new Vector3(-2.8f, 2.6f, front), new Vector3(1.6f, 1.4f, 0.15f), window);
        Box("Janela_D", house, new Vector3(2.8f, 2.6f, front), new Vector3(1.6f, 1.4f, 0.15f), window);

        house.gameObject.AddComponent<DestructibleStructure>();
        foreach (Transform part in house.GetComponentsInChildren<Transform>())
        {
            // Sem "static batching": a casa precisa poder tremer e sumir ao ser destruída.
            GameObjectUtility.SetStaticEditorFlags(part.gameObject,
                GameObjectUtility.GetStaticEditorFlags(part.gameObject) & ~StaticEditorFlags.BatchingStatic);
        }
    }

    private static void ScatterTrees(Transform parent, Rect area, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Tree(parent, new Vector3(Range(area.xMin, area.xMax), 0f, Range(area.yMin, area.yMax)), i);
        }
    }

    private static void Tree(Transform parent, Vector3 position, int index)
    {
        float canopy = 3.2f + (float)random.NextDouble() * 1.6f;
        Transform tree = Group("Arvore", parent, position);
        Primitive(PrimitiveType.Cylinder, "Tronco", tree, new Vector3(0f, 2f, 0f), new Vector3(0.5f, 2f, 0.5f), trunk);
        Primitive(PrimitiveType.Sphere, "Copa", tree, new Vector3(0f, 4f + canopy * 0.35f, 0f), Vector3.one * canopy, index % 2 == 0 ? canopyA : canopyB);
    }

    private static void StreetLamps(Transform parent, Vector3[] spots)
    {
        Transform lamps = Group("Postes", parent, Vector3.zero);
        foreach (Vector3 spot in spots)
        {
            Transform lamp = Group("Poste", lamps, spot);
            Primitive(PrimitiveType.Cylinder, "Haste", lamp, new Vector3(0f, 3f, 0f), new Vector3(0.2f, 3f, 0.2f), lampPole);
            Primitive(PrimitiveType.Sphere, "Luz", lamp, new Vector3(0f, 6.3f, 0f), Vector3.one * 0.7f, lampLight);
        }
    }

    // ---------- Objetos arremessáveis ----------

    private static void BuildThrowables()
    {
        Transform group = new GameObject("Arremessaveis_Regiao").transform;

        // Carros estacionados (900 kg).
        (Vector3 pos, float yaw)[] cars =
        {
            (new Vector3(66f, 0.05f, -2.6f), 0f), (new Vector3(128f, 0.05f, 2.6f), 180f), (new Vector3(204f, 0.05f, -2.6f), 0f),
            (new Vector3(2.6f, 0.05f, -66f), 90f), (new Vector3(167.6f, 0.05f, -62f), 90f), (new Vector3(52f, 0.05f, -182.6f), 0f),
            (new Vector3(214f, 0.05f, -177.4f), 180f), (new Vector3(-2.6f, 0.05f, -214f), 90f), (new Vector3(162.4f, 0.05f, 40f), 90f),
        };
        foreach (var car in cars)
        {
            Car(group, car.pos, car.yaw);
        }

        // Caixas (40 kg) e lixeiras (25 kg) perto das ruas.
        Vector3[] crates =
        {
            new Vector3(52f, 0.5f, 8f), new Vector3(53f, 1.5f, 8f), new Vector3(110f, 0.5f, -8f), new Vector3(150f, 0.5f, 8f),
            new Vector3(225f, 0.5f, -8f), new Vector3(8f, 0.5f, -140f), new Vector3(-9f, 0.5f, -190f), new Vector3(30f, 0.5f, -172f),
            new Vector3(105f, 0.5f, -130f), new Vector3(106f, 0.5f, -131.2f), new Vector3(106f, 1.5f, -130.6f), new Vector3(150f, 0.5f, -190f),
            new Vector3(230f, 0.5f, -130f), new Vector3(180f, 0.5f, -230f),
        };
        foreach (Vector3 c in crates)
        {
            Throwable(Primitive(PrimitiveType.Cube, "Caixa", group, c, Vector3.one, crate), 40f);
        }

        Vector3[] bins = { new Vector3(48f, 0.55f, -8f), new Vector3(140f, 0.55f, 8f), new Vector3(190f, 0.55f, -8f), new Vector3(8f, 0.55f, -100f), new Vector3(-8f, 0.55f, -160f), new Vector3(158f, 0.55f, -160f), new Vector3(200f, 0.55f, -188f) };
        foreach (Vector3 b in bins)
        {
            Throwable(Primitive(PrimitiveType.Cylinder, "Lixeira", group, b, new Vector3(0.7f, 0.55f, 0.7f), bin), 25f);
        }

        // Contêineres (2.200 kg): só com a evolução Força Telecinética.
        int colorIndex = 0;
        foreach (Vector3 origin in new[] { new Vector3(128f, 0f, -131f), new Vector3(186f, 0f, -200f) })
        {
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = origin + new Vector3(i * 3f, 1.3f, 0f);
                Throwable(Primitive(PrimitiveType.Cube, "Conteiner", group, p, new Vector3(2.4f, 2.6f, 6f), containerColors[colorIndex++ % containerColors.Length]), 2200f);
                if (i % 2 == 0)
                {
                    Throwable(Primitive(PrimitiveType.Cube, "Conteiner", group, p + Vector3.up * 2.65f, new Vector3(2.4f, 2.6f, 6f), containerColors[colorIndex++ % containerColors.Length]), 2200f);
                }
            }
        }
    }

    private static void Car(Transform parent, Vector3 position, float yaw)
    {
        GameObject car = new GameObject("Carro");
        car.transform.SetParent(parent, false);
        car.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        BoxCollider hull = car.AddComponent<BoxCollider>();
        hull.center = new Vector3(0f, 0.65f, 0f);
        hull.size = new Vector3(4.2f, 1.3f, 2f);
        NoCollider(Primitive(PrimitiveType.Cube, "Carroceria", car.transform, new Vector3(0f, 0.8f, 0f), new Vector3(4.2f, 1f, 2f), carBody));
        Primitive(PrimitiveType.Cube, "Cabine", car.transform, new Vector3(-0.2f, 1.7f, 0f), new Vector3(2.2f, 0.8f, 1.8f), carGlass);
        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                NoCollider(Primitive(PrimitiveType.Cylinder, "Roda", car.transform, new Vector3(x * 1.4f, 0.4f, z * 1f),
                    new Vector3(0.8f, 0.15f, 0.8f), wheel, new Vector3(90f, 0f, 0f)));
            }
        }

        Throwable(car, 900f);
    }

    private static void Throwable(GameObject go, float mass)
    {
        go.isStatic = false;
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<TelekinesisTarget>();
        foreach (Transform part in go.GetComponentsInChildren<Transform>())
        {
            part.gameObject.isStatic = false;
        }
    }

    private static void BuildWaterTrigger()
    {
        // Fica fora da raiz da região para não entrar no cálculo do NavMesh.
        GameObject trigger = new GameObject("Rio_Agua_Gatilho");
        trigger.transform.position = new Vector3((MinX + MaxX) / 2f, (RiverBedY + WaterY) / 2f, (RiverNorth + RiverSouth) / 2f);
        BoxCollider box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(MaxX - MinX, WaterY - RiverBedY + 0.2f, RiverNorth - RiverSouth);
        trigger.AddComponent<WaterZone>();
    }

    // ---------- NavMesh e alienígenas ----------

    private static bool BakeNavMesh(GameObject root)
    {
        NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

        try
        {
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                return false;
            }

            string path = DataFolder + "/NavMesh_VilarejoDoBilu.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(surface);
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Não consegui calcular o NavMesh automaticamente: " + exception.Message);
            return false;
        }
    }

    private static int SpawnAliens(Vector3 playerStart)
    {
        GameObject civilPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Alienigena.prefab");
        GameObject policePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Alienigena_Policial.prefab");
        if (civilPrefab == null || policePrefab == null)
        {
            Debug.LogWarning("Prefabs dos alienígenas não encontrados (rode antes as ferramentas das versões 0.1 e 0.3).");
            return 0;
        }

        GameObject group = GameObject.Find("Alienigenas") ?? new GameObject("Alienigenas");
        List<Vector3> used = new List<Vector3>();
        foreach (Transform existing in group.transform)
        {
            used.Add(existing.position);
        }

        string[] colors = { "Lilas", "Verde", "Azul", "Rosa", "Laranja" };
        int spawned = 0;
        foreach (Sector sector in Sectors)
        {
            for (int i = 0; i < sector.civils + sector.police; i++)
            {
                bool police = i >= sector.civils;
                if (!TryFindSpot(sector.area, used, playerStart, out Vector3 spot))
                {
                    continue;
                }

                GameObject alien = (GameObject)PrefabUtility.InstantiatePrefab(police ? policePrefab : civilPrefab, group.transform);
                alien.name = $"{(police ? "Policial" : "Civil")}_{sector.name.Replace(' ', '_')}_{i + 1:00}";
                alien.transform.SetPositionAndRotation(spot + Vector3.up * 0.1f, Quaternion.Euler(0f, Range(0f, 360f), 0f));

                if (!police)
                {
                    Material skin = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/Aliens/M_Alien_{colors[spawned % colors.Length]}.mat");
                    if (skin != null)
                    {
                        SetMaterial(alien.transform.Find("Corpo/Tronco"), skin);
                        SetMaterial(alien.transform.Find("Corpo/Cabeca"), skin);
                    }
                }

                used.Add(spot);
                spawned++;
            }
        }

        return spawned;
    }

    // Ponto sobre o NavMesh, em terra firme, longe dos outros alienígenas e do início do jogador.
    private static bool TryFindSpot(Rect area, List<Vector3> used, Vector3 playerStart, out Vector3 spot)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            Vector3 guess = new Vector3(Range(area.xMin + 4f, area.xMax - 4f), 0.5f, Range(area.yMin + 4f, area.yMax - 4f));
            if (!NavMesh.SamplePosition(guess, out NavMeshHit hit, 4f, NavMesh.AllAreas) || hit.position.y < -0.3f || hit.position.y > 1f)
            {
                continue;
            }

            if (Vector3.Distance(hit.position, playerStart) < 25f)
            {
                continue;
            }

            bool crowded = false;
            foreach (Vector3 other in used)
            {
                if ((other - hit.position).sqrMagnitude < 10f * 10f)
                {
                    crowded = true;
                    break;
                }
            }

            if (!crowded)
            {
                spot = hit.position;
                return true;
            }
        }

        spot = Vector3.zero;
        return false;
    }

    private static void ConfigureSystems()
    {
        SectorTracker tracker = Object.FindFirstObjectByType<SectorTracker>();
        if (tracker != null)
        {
            SerializedObject data = new SerializedObject(tracker);
            data.FindProperty("sectorName").stringValue = RegionName;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AddSceneToBuild()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == ScenePath))
        {
            return;
        }

        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // ---------- Materiais ----------

    private static void LoadMaterials()
    {
        grass = Existing("Blockout", "Grama", "6FBF8E");
        asphalt = Existing("Blockout", "Asfalto", "3B4252");
        laneMark = Existing("Blockout", "Faixa", "F2C14E");
        water = Existing("Blockout", "Agua", "5EC8D8");
        trunk = Existing("Blockout", "Tronco", "7A5C48");
        canopyA = Existing("Blockout", "Copa_Turquesa", "3FB8A8");
        canopyB = Existing("Blockout", "Copa_Lilas", "B48AD8");
        bench = Existing("Blockout", "Banco", "C9733D");
        door = Existing("Blockout", "Porta", "5A3E5D");
        window = Existing("Blockout", "Janela", "BFF3F7");
        lampPole = Existing("Blockout", "Poste", "4C5566");
        lampLight = Existing("Blockout", "Luz", "7DF9FF");
        houseWalls = new[]
        {
            Existing("Blockout", "Casa_Creme", "F1E3C6"), Existing("Blockout", "Casa_Turquesa", "8FD6CF"),
            Existing("Blockout", "Casa_Lilas", "C8B2E6"), Existing("Blockout", "Casa_Verde", "B9D99A"),
        };
        houseRoofs = new[] { Existing("Blockout", "Telhado_Laranja", "C9603A"), Existing("Blockout", "Telhado_Roxo", "6E4A8C") };

        crate = Existing("Props", "Caixa", "D9A066");
        bin = Existing("Props", "Lixeira", "5FA8A0");
        carBody = Existing("Props", "Carro", "E0607E");
        carGlass = Existing("Props", "Carro_Vidro", "BFF3F7");
        wheel = Existing("Props", "Roda", "2B2B33");

        riverBed = Existing("Regiao", "Fundo_Rio", "4E6E66");
        wood = Existing("Regiao", "Madeira", "9C6B45");
        warehouse = Existing("Regiao", "Galpao", "9AA3AE");
        warehouseRoof = Existing("Regiao", "Galpao_Teto", "5B6470");
        containerColors = new[]
        {
            Existing("Regiao", "Conteiner_Vermelho", "C8503C"), Existing("Regiao", "Conteiner_Azul", "3E78B2"),
            Existing("Regiao", "Conteiner_Amarelo", "E3B23C"), Existing("Regiao", "Conteiner_Verde", "4E9A63"),
        };
    }

    // Usa o material existente (mantendo cores que você mudou) ou cria um novo.
    private static Material Existing(string folder, string name, string hex)
    {
        string assetPath = $"Assets/Art/Materials/{folder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
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
        AssetDatabase.CreateAsset(material, assetPath);
        return material;
    }

    // ---------- Ajudantes ----------

    private static Transform Group(string name, Transform parent, Vector3 position = default)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.isStatic = true;
        return go.transform;
    }

    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, Vector3? euler = null)
    {
        return Primitive(PrimitiveType.Cube, name, parent, position, size, material, euler);
    }

    // Posição e rotação sempre RELATIVAS ao grupo (parent). Os grupos de nível de setor ficam
    // na origem, então para eles a posição relativa é igual à posição no mundo.
    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale,
        Material material, Vector3? euler = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        if (euler.HasValue)
        {
            go.transform.localRotation = Quaternion.Euler(euler.Value);
        }

        go.GetComponent<Renderer>().sharedMaterial = material;
        go.isStatic = true;
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

    private static void SetMaterial(Transform part, Material material)
    {
        if (part == null)
        {
            return;
        }

        Renderer renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
    }

    private static Vector3 Center(float x0, float x1, float z0, float z1, float y)
    {
        return new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f);
    }

    private static float Range(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private static float Sqr(float v) => v * v;

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
