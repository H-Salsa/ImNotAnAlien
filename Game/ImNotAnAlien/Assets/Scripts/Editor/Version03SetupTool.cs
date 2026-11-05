using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Ferramenta de editor da Versão 0.3.
// Menu: ImNotAnAlien > Preparar Versao 0.3
// 1. Cria o prefab do alienígena POLICIAL (farda azul-marinho, quepe com distintivo, arma).
// 2. Repopula o bairro: 8 civis + 3 policiais (substitui o grupo "Alienigenas").
// 3. Adiciona ao jogador a vida (PlayerHealth) e o Devorar (DevourAbility).
public static class Version03SetupTool
{
    private const string PolicePrefabPath = "Assets/Prefabs/Alienigena_Policial.prefab";
    private const string MaterialFolder = "Assets/Art/Materials/Aliens";
    private const string GroupName = "Alienigenas";

    private static readonly (string colorName, string hex)[] CivilColors =
    {
        ("Lilas", "B79AE8"),
        ("Verde", "8EDB7A"),
        ("Azul", "7FB8F0"),
        ("Rosa", "F29AC8"),
        ("Laranja", "F2B66B"),
    };

    // Pontos em calçadas, gramados e no parque (todos sobre o NavMesh).
    private static readonly Vector3[] CivilSpots =
    {
        new Vector3(14f, 0.3f, 26f),
        new Vector3(22.5f, 0.3f, 16.5f),
        new Vector3(31f, 0.3f, 31f),
        new Vector3(22.5f, 0.3f, -11.5f),
        new Vector3(15f, 0.3f, -32f),
        new Vector3(-22.5f, 0.3f, -11.5f),
        new Vector3(-30f, 0.3f, -30f),
        new Vector3(-22.5f, 0.3f, 11.5f),
    };

    // Longe do ponto inicial do jogador, para não começar levando tiro.
    private static readonly Vector3[] PoliceSpots =
    {
        new Vector3(-20f, 0.3f, 6.5f),
        new Vector3(6.5f, 0.3f, 20f),
        new Vector3(30f, 0.3f, -6.5f),
    };

    [MenuItem("ImNotAnAlien/Preparar Versao 0.3")]
    public static void Setup()
    {
        GameObject existing = GameObject.Find(GroupName);
        if (existing != null && !EditorUtility.DisplayDialog(
                "Repopular o bairro",
                "O grupo 'Alienigenas' atual será substituído por 8 civis e 3 policiais.\n(Ctrl+Z desfaz)",
                "Repopular",
                "Cancelar"))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Versao 0.3");

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        GameObject civilPrefab = AlienSpawnerTool.GetOrCreatePrefab();
        GameObject policePrefab = GetOrCreatePolicePrefab();

        GameObject group = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(group, "Preparar Versao 0.3");

        for (int i = 0; i < CivilSpots.Length; i++)
        {
            GameObject civil = Place(civilPrefab, group.transform, $"Civil_{i + 1:00}", CivilSpots[i]);
            var color = CivilColors[i % CivilColors.Length];
            Material skin = Mat("Alien_" + color.colorName, color.hex);
            SetMaterial(civil.transform.Find("Corpo/Tronco"), skin);
            SetMaterial(civil.transform.Find("Corpo/Cabeca"), skin);
        }

        for (int i = 0; i < PoliceSpots.Length; i++)
        {
            Place(policePrefab, group.transform, $"Policial_{i + 1:00}", PoliceSpots[i]);
        }

        SetupPlayer();

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(group.scene);
        Selection.activeGameObject = group;
        Debug.Log("Versão 0.3 preparada: 8 civis, 3 policiais, vida do jogador e Devorar. Salve a cena com Ctrl+S.");
    }

    private static GameObject Place(GameObject prefab, Transform parent, string name, Vector3 position)
    {
        GameObject alien = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        alien.name = name;
        alien.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        return alien;
    }

    private static void SetupPlayer()
    {
        PrototypeThirdPersonController player = Object.FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player == null)
        {
            Debug.LogWarning("Jogador não encontrado: vida e Devorar não foram adicionados.");
            return;
        }

        if (!player.TryGetComponent(out PlayerHealth _))
        {
            Undo.AddComponent<PlayerHealth>(player.gameObject);
        }

        if (!player.TryGetComponent(out DevourAbility devour))
        {
            devour = Undo.AddComponent<DevourAbility>(player.gameObject);
        }

        if (Camera.main != null)
        {
            SerializedObject data = new SerializedObject(devour);
            data.FindProperty("cameraTransform").objectReferenceValue = Camera.main.transform;
            data.ApplyModifiedProperties();
        }
    }

    // ---------- Prefab do policial ----------

    private static GameObject GetOrCreatePolicePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PolicePrefabPath);
        if (prefab != null)
        {
            return prefab; // não sobrescreve: mudanças que você fizer no prefab são preservadas
        }

        Material uniform = Mat("Policial_Farda", "26346E");
        Material skin = Mat("Policial_Pele", "8FE3D0");
        Material eye = Mat("Alien_Olho", "1B1530");
        Material cap = Mat("Policial_Quepe", "1B2448");
        Material visor = Mat("Policial_Viseira", "101320");
        Material badge = Mat("Policial_Distintivo", "FFC83D");
        Material gun = Mat("Policial_Arma", "2B2B33");
        Material alert = Mat("Alerta", "FFD23F");
        Material projectile = UnlitMat("Projetil_Policial", "FF4090");

        GameObject root = new GameObject("Alienigena_Policial");

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 2.4f;
        agent.speed = 1.8f;
        agent.acceleration = 14f;
        agent.angularSpeed = 540f;
        agent.stoppingDistance = 0.2f;

        AlienBrain brain = root.AddComponent<AlienBrain>();
        AlienHealth health = root.AddComponent<AlienHealth>();
        AlienPoliceWeapon weapon = root.AddComponent<AlienPoliceWeapon>();

        Transform body = Child("Corpo", root.transform, Vector3.zero);
        Part(PrimitiveType.Capsule, "Tronco", body, new Vector3(0f, 0.8f, 0f), new Vector3(0.85f, 0.8f, 0.85f), uniform, true);
        Part(PrimitiveType.Sphere, "Cabeca", body, new Vector3(0f, 1.75f, 0f), Vector3.one * 0.9f, skin, true);
        Part(PrimitiveType.Sphere, "Olho_E", body, new Vector3(-0.2f, 1.78f, 0.36f), new Vector3(0.26f, 0.3f, 0.15f), eye, false,
            new Vector3(0f, 0f, -20f));
        Part(PrimitiveType.Sphere, "Olho_D", body, new Vector3(0.2f, 1.78f, 0.36f), new Vector3(0.26f, 0.3f, 0.15f), eye, false,
            new Vector3(0f, 0f, 20f));

        // Quepe de polícia: copa + viseira na frente + distintivo dourado.
        Transform hat = Child("Quepe", body, Vector3.zero);
        Part(PrimitiveType.Cylinder, "Copa", hat, new Vector3(0f, 2.2f, -0.02f), new Vector3(0.82f, 0.13f, 0.82f), cap, false);
        Part(PrimitiveType.Cylinder, "Faixa", hat, new Vector3(0f, 2.08f, -0.02f), new Vector3(0.7f, 0.05f, 0.7f), visor, false);
        Part(PrimitiveType.Cylinder, "Viseira", hat, new Vector3(0f, 2.06f, 0.2f), new Vector3(0.55f, 0.015f, 0.4f), visor, false,
            new Vector3(-12f, 0f, 0f));
        Part(PrimitiveType.Cube, "Distintivo", hat, new Vector3(0f, 2.2f, 0.4f), new Vector3(0.17f, 0.17f, 0.04f), badge, false,
            new Vector3(0f, 0f, 45f));
        // Distintivo também no peito.
        Part(PrimitiveType.Cube, "Distintivo_Peito", body, new Vector3(-0.2f, 1.15f, 0.36f), new Vector3(0.14f, 0.14f, 0.04f), badge, false,
            new Vector3(0f, 0f, 45f));

        // Arma flutuando ao lado (estilo desenho animado, sem braços).
        Transform gunRoot = Child("Arma", body, new Vector3(0.5f, 1.1f, 0.25f));
        Part(PrimitiveType.Cube, "Corpo_Arma", gunRoot, Vector3.zero, new Vector3(0.14f, 0.18f, 0.5f), gun, false);
        Part(PrimitiveType.Cube, "Cabo", gunRoot, new Vector3(0f, -0.14f, -0.12f), new Vector3(0.12f, 0.22f, 0.12f), gun, false,
            new Vector3(-15f, 0f, 0f));
        Transform tip = Child("Ponta", gunRoot, new Vector3(0f, 0.02f, 0.32f));
        GameObject chargeLight = Part(PrimitiveType.Sphere, "Luz_Arma", tip, Vector3.zero, Vector3.one * 0.22f, projectile, false);

        Transform alertIcon = Child("Alerta", body, Vector3.zero);
        Part(PrimitiveType.Cube, "Barra", alertIcon, new Vector3(0f, 3.3f, 0f), new Vector3(0.14f, 0.5f, 0.14f), alert, false);
        Part(PrimitiveType.Sphere, "Ponto", alertIcon, new Vector3(0f, 2.92f, 0f), Vector3.one * 0.16f, alert, false);

        SerializedObject brainData = new SerializedObject(brain);
        brainData.FindProperty("role").enumValueIndex = (int)AlienBrain.Role.Policial;
        brainData.FindProperty("detectionRadius").floatValue = 16f;
        brainData.FindProperty("calmDownDistance").floatValue = 30f;
        brainData.FindProperty("body").objectReferenceValue = body;
        brainData.FindProperty("alertIcon").objectReferenceValue = alertIcon.gameObject;
        brainData.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject healthData = new SerializedObject(health);
        healthData.FindProperty("maxHealth").floatValue = 160f;
        healthData.FindProperty("dnaValue").intValue = 20;
        healthData.FindProperty("orbCount").intValue = 8;
        healthData.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject weaponData = new SerializedObject(weapon);
        weaponData.FindProperty("muzzle").objectReferenceValue = tip;
        weaponData.FindProperty("chargeIndicator").objectReferenceValue = chargeLight;
        weaponData.FindProperty("projectileMaterial").objectReferenceValue = projectile;
        weaponData.ApplyModifiedPropertiesWithoutUndo();

        prefab = PrefabUtility.SaveAsPrefabAsset(root, PolicePrefabPath);
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

    private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
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

        return go;
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

    private static Material Mat(string name, string hex)
    {
        return LoadOrCreate(name, hex, "Universal Render Pipeline/Lit", 0.35f);
    }

    // Material que "brilha" (ignora a luz da cena).
    private static Material UnlitMat(string name, string hex)
    {
        return LoadOrCreate(name, hex, "Universal Render Pipeline/Unlit", 0f);
    }

    private static Material LoadOrCreate(string name, string hex, string shaderName, float smoothness)
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

        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        material = new Material(Shader.Find(shaderName)) { color = color };
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

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
}
