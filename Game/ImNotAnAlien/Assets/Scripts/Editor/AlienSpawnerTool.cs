using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Ferramenta de editor da Missão 3.
// Menu: ImNotAnAlien > Criar Alienigenas
// 1. Cria o prefab Assets/Prefabs/Alienigena.prefab (se ainda não existir).
// 2. Coloca três alienígenas na cena, cada um com uma cor.
// 3. Adiciona o NavMeshSurface ao 'Esquina_Blockout' (você só precisa clicar em Bake).
public static class AlienSpawnerTool
{
    private const string PrefabFolder = "Assets/Prefabs";
    private const string PrefabPath = PrefabFolder + "/Alienigena.prefab";
    private const string MaterialFolder = "Assets/Art/Materials/Aliens";
    private const string GroupName = "Alienigenas";
    private const string BlockoutRootName = "Esquina_Blockout";

    // Um em cada região: parque, frente das casas do leste e calçada do prédio.
    private static readonly (string name, Vector3 position, string colorName, string hex)[] Spawns =
    {
        ("Alien_Parque", new Vector3(14f, 0.3f, 26f), "Lilas", "B79AE8"),
        ("Alien_Casas", new Vector3(22.5f, 0.3f, -11.5f), "Verde", "8EDB7A"),
        ("Alien_Predio", new Vector3(-22.5f, 0.3f, 11.5f), "Azul", "7FB8F0"),
    };

    [MenuItem("ImNotAnAlien/Criar Alienigenas")]
    public static void CreateAliens()
    {
        GameObject existing = GameObject.Find(GroupName);
        if (existing != null && !EditorUtility.DisplayDialog(
                "Alienígenas já existem",
                "Já existe um grupo 'Alienigenas' na cena.\nApagar e criar de novo? (Ctrl+Z desfaz)",
                "Criar de novo",
                "Cancelar"))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Criar Alienigenas");

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        GameObject prefab = GetOrCreatePrefab();

        GameObject group = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(group, "Criar Alienigenas");

        foreach (var spawn in Spawns)
        {
            GameObject alien = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            alien.name = spawn.name;
            alien.transform.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            Material skin = Mat("Alien_" + spawn.colorName, Hex(spawn.hex));
            SetMaterial(alien.transform.Find("Corpo/Tronco"), skin);
            SetMaterial(alien.transform.Find("Corpo/Cabeca"), skin);
        }

        bool needsBake = SetupNavMeshSurface();

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(group.scene);
        Selection.activeGameObject = group;

        Debug.Log(needsBake
            ? "Alienígenas criados. Agora selecione 'Esquina_Blockout' e clique em Bake no componente NavMeshSurface. Depois salve com Ctrl+S."
            : "Alienígenas criados. Salve a cena com Ctrl+S.");
    }

    // ---------- NavMesh ----------

    // Retorna true se o NavMesh ainda precisa ser "assado" (Bake).
    private static bool SetupNavMeshSurface()
    {
        GameObject blockout = GameObject.Find(BlockoutRootName);
        if (blockout == null)
        {
            Debug.LogWarning("'Esquina_Blockout' não encontrado. Gere a esquina primeiro (Missão 2).");
            return true;
        }

        if (!blockout.TryGetComponent(out NavMeshSurface surface))
        {
            surface = Undo.AddComponent<NavMeshSurface>(blockout);
        }

        Undo.RecordObject(surface, "Configurar NavMeshSurface");
        surface.collectObjects = CollectObjects.Children;            // só o cenário, não os personagens
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders; // usa os colisores (inclui paredes invisíveis)
        return surface.navMeshData == null;
    }

    // ---------- Prefab ----------

    private static GameObject GetOrCreatePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null)
        {
            return prefab; // não sobrescreve: mudanças que você fizer no prefab são preservadas
        }

        EnsureFolder("Assets", "Prefabs");

        Material skin = Mat("Alien_Lilas", Hex("B79AE8"));
        Material eye = Mat("Alien_Olho", Hex("1B1530"));
        Material antenna = Mat("Alien_Antena", Hex("4C5566"));
        Material antennaTip = Mat("Alien_Antena_Ponta", Hex("7DF9FF"));
        Material alert = Mat("Alerta", Hex("FFD23F"));

        GameObject root = new GameObject("Alienigena");

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 2.2f;
        agent.speed = 1.8f;
        agent.acceleration = 14f;
        agent.angularSpeed = 540f;
        agent.stoppingDistance = 0.2f;

        AlienBrain brain = root.AddComponent<AlienBrain>();
        root.AddComponent<AlienHealth>();

        Transform body = Child("Corpo", root.transform, Vector3.zero);
        Part(PrimitiveType.Capsule, "Tronco", body, new Vector3(0f, 0.8f, 0f), new Vector3(0.8f, 0.8f, 0.8f), skin, true);
        Part(PrimitiveType.Sphere, "Cabeca", body, new Vector3(0f, 1.75f, 0f), Vector3.one * 0.9f, skin, true);
        // Olhos grandes e escuros, levemente inclinados: o "jeitão" alienígena.
        Part(PrimitiveType.Sphere, "Olho_E", body, new Vector3(-0.2f, 1.8f, 0.36f), new Vector3(0.26f, 0.34f, 0.15f), eye, false,
            new Vector3(0f, 0f, -20f));
        Part(PrimitiveType.Sphere, "Olho_D", body, new Vector3(0.2f, 1.8f, 0.36f), new Vector3(0.26f, 0.34f, 0.15f), eye, false,
            new Vector3(0f, 0f, 20f));
        Part(PrimitiveType.Cylinder, "Antena", body, new Vector3(0f, 2.4f, 0f), new Vector3(0.05f, 0.25f, 0.05f), antenna, false);
        Part(PrimitiveType.Sphere, "Antena_Ponta", body, new Vector3(0f, 2.68f, 0f), Vector3.one * 0.16f, antennaTip, false);

        // O "!" de susto, escondido até o alienígena ver o jogador.
        Transform alertIcon = Child("Alerta", body, Vector3.zero);
        Part(PrimitiveType.Cube, "Barra", alertIcon, new Vector3(0f, 3.3f, 0f), new Vector3(0.14f, 0.5f, 0.14f), alert, false);
        Part(PrimitiveType.Sphere, "Ponto", alertIcon, new Vector3(0f, 2.92f, 0f), Vector3.one * 0.16f, alert, false);

        SerializedObject brainData = new SerializedObject(brain);
        brainData.FindProperty("body").objectReferenceValue = body;
        brainData.FindProperty("alertIcon").objectReferenceValue = alertIcon.gameObject;
        brainData.ApplyModifiedPropertiesWithoutUndo();

        prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ---------- Ajudantes ----------

    private static Transform Child(string name, Transform parent, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go.transform;
    }

    private static void Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
        Material material, bool keepCollider, Vector3? euler = null)
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
        if (!keepCollider)
        {
            Object.DestroyImmediate(go.GetComponent<Collider>());
        }
    }

    private static void SetMaterial(Transform part, Material material)
    {
        if (part == null)
        {
            return;
        }

        Renderer renderer = part.GetComponent<Renderer>();
        Undo.RecordObject(renderer, "Cor do alienígena");
        renderer.sharedMaterial = material;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
    }

    private static Material Mat(string name, Color color)
    {
        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "Materials");
        EnsureFolder("Assets/Art/Materials", "Aliens");

        string assetPath = $"{MaterialFolder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        material.SetFloat("_Smoothness", 0.35f);
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
