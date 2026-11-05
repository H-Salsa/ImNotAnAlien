using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 4.
// Menu: ImNotAnAlien > Preparar Telecinese
// 1. Adiciona o poder TelekinesisPower ao jogador.
// 2. Cria objetos arremessáveis pela esquina: caixas, lixeiras e um carro.
public static class TelekinesisSetupTool
{
    private const string GroupName = "Arremessaveis";
    private const string MaterialFolder = "Assets/Art/Materials/Props";
    private const float Sidewalk = 0.15f;

    [MenuItem("ImNotAnAlien/Preparar Telecinese")]
    public static void Setup()
    {
        GameObject existing = GameObject.Find(GroupName);
        if (existing != null && !EditorUtility.DisplayDialog(
                "Objetos já existem",
                "Já existe um grupo 'Arremessaveis' na cena.\nApagar e criar de novo? (Ctrl+Z desfaz)",
                "Criar de novo",
                "Cancelar"))
        {
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Telecinese");

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }

        AddPowerToPlayer();

        GameObject group = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(group, "Preparar Telecinese");
        CreateProps(group.transform);

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(group.scene);
        Selection.activeGameObject = group;
        Debug.Log("Telecinese preparada. Salve a cena com Ctrl+S e dê Play.");
    }

    private static void AddPowerToPlayer()
    {
        PrototypeThirdPersonController player = Object.FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player == null)
        {
            Debug.LogWarning("Jogador (PrototypeThirdPersonController) não encontrado na cena.");
            return;
        }

        if (!player.TryGetComponent(out TelekinesisPower power))
        {
            power = Undo.AddComponent<TelekinesisPower>(player.gameObject);
        }

        if (Camera.main != null)
        {
            SerializedObject data = new SerializedObject(power);
            data.FindProperty("cameraTransform").objectReferenceValue = Camera.main.transform;
            data.ApplyModifiedProperties();
        }
    }

    // ---------- Objetos ----------

    private static void CreateProps(Transform group)
    {
        Material crate = Mat("Caixa", "D9A066");
        Material bin = Mat("Lixeira", "5FA8A0");
        Material carBody = Mat("Carro", "E0607E");
        Material carGlass = Mat("Carro_Vidro", "BFF3F7");
        Material wheel = Mat("Roda", "2B2B33");

        // Caixas perto do ponto inicial do jogador (calçada das casas do leste) e nas outras esquinas.
        Crate(group, crate, new Vector3(6.6f, Sidewalk + 0.5f, -10f));
        Crate(group, crate, new Vector3(6.6f, Sidewalk + 1.5f, -10f)); // empilhada
        Crate(group, crate, new Vector3(7.4f, Sidewalk + 0.5f, -12.5f));
        Crate(group, crate, new Vector3(-6.6f, Sidewalk + 0.5f, -12f));
        Crate(group, crate, new Vector3(-7f, Sidewalk + 0.5f, 9f));
        Crate(group, crate, new Vector3(9f, Sidewalk + 0.5f, 7f));

        Bin(group, bin, new Vector3(6.6f, Sidewalk + 0.55f, 7.6f));
        Bin(group, bin, new Vector3(-6.6f, Sidewalk + 0.55f, -7.6f));
        Bin(group, bin, new Vector3(12f, Sidewalk + 0.55f, -6.6f));

        Car(group, carBody, carGlass, wheel, new Vector3(-14f, 0.05f, -2.6f));
    }

    private static void Crate(Transform group, Material material, Vector3 position)
    {
        GameObject go = Shape(PrimitiveType.Cube, "Caixa", group, position, Vector3.one, material);
        MakeThrowable(go, 40f);
    }

    private static void Bin(Transform group, Material material, Vector3 position)
    {
        GameObject go = Shape(PrimitiveType.Cylinder, "Lixeira", group, position, new Vector3(0.7f, 0.55f, 0.7f), material);
        MakeThrowable(go, 25f);
    }

    private static void Car(Transform group, Material bodyMat, Material glassMat, Material wheelMat, Vector3 position)
    {
        GameObject car = new GameObject("Carro");
        car.transform.SetParent(group, false);
        car.transform.localPosition = position;

        // Um colisor único cobre a carroceria até o chão (as rodas são só visuais).
        BoxCollider hull = car.AddComponent<BoxCollider>();
        hull.center = new Vector3(0f, 0.65f, 0f);
        hull.size = new Vector3(4.2f, 1.3f, 2f);

        NoCollider(Shape(PrimitiveType.Cube, "Carroceria", car.transform, new Vector3(0f, 0.8f, 0f),
            new Vector3(4.2f, 1f, 2f), bodyMat));
        Shape(PrimitiveType.Cube, "Cabine", car.transform, new Vector3(-0.2f, 1.7f, 0f),
            new Vector3(2.2f, 0.8f, 1.8f), glassMat);

        for (int x = -1; x <= 1; x += 2)
        {
            for (int z = -1; z <= 1; z += 2)
            {
                NoCollider(Shape(PrimitiveType.Cylinder, "Roda", car.transform, new Vector3(x * 1.4f, 0.4f, z * 1f),
                    new Vector3(0.8f, 0.15f, 0.8f), wheelMat, new Vector3(90f, 0f, 0f)));
            }
        }

        MakeThrowable(car, 900f);
    }

    private static void MakeThrowable(GameObject go, float mass)
    {
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // evita atravessar paredes em alta velocidade
        go.AddComponent<TelekinesisTarget>();
    }

    // ---------- Ajudantes ----------

    private static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
        Material material, Vector3? euler = null)
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
        return go;
    }

    private static void NoCollider(GameObject go)
    {
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    private static Material Mat(string name, string hex)
    {
        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "Materials");
        EnsureFolder("Assets/Art/Materials", "Props");

        string assetPath = $"{MaterialFolder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material != null)
        {
            return material;
        }

        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        material.SetFloat("_Smoothness", 0.3f);
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
