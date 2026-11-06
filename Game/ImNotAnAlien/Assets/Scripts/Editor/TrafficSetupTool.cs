using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 14 — trânsito.
// Menu: ImNotAnAlien > Preparar Transito (Missao 14)
// Rode com a cena Regiao1_VilarejoDoBilu aberta. A ferramenta:
// 1. Adiciona o TrafficSystem ao objeto "Sistemas", com as 4 ruas do Vilarejo do Bilu.
// 2. Cria/usa as cores dos carros e liga os materiais (vidro, rodas, pele dos motoristas, olhos).
// 3. Tira os carros estacionados de cima das faixas (para não travarem o trânsito).
public static class TrafficSetupTool
{
    private struct RoadSetup
    {
        public string name;
        public bool eastWest;
        public float at;
        public float from;
        public float to;
    }

    // As mesmas ruas do gerador da região.
    private static readonly RoadSetup[] Roads =
    {
        new RoadSetup { name = "Principal (Leste-Oeste)", eastWest = true, at = 0f, from = -60f, to = 240f },
        new RoadSetup { name = "Sul (Leste-Oeste)", eastWest = true, at = -180f, from = -60f, to = 240f },
        new RoadSetup { name = "Ponte Oeste (Norte-Sul)", eastWest = false, at = 0f, from = -240f, to = 60f },
        new RoadSetup { name = "Ponte Leste (Norte-Sul)", eastWest = false, at = 165f, from = -240f, to = 60f },
    };

    private static readonly (string name, string hex)[] CarColors =
    {
        ("Carro", "E0607E"), ("Carro_Turquesa", "3FB8A8"), ("Carro_Amarelo", "F2C14E"),
        ("Carro_Lilas", "9F7FD9"), ("Carro_Laranja", "E8894A"), ("Carro_Azul", "4F86C6"),
    };

    [MenuItem("ImNotAnAlien/Preparar Transito (Missao 14)")]
    public static void Setup()
    {
        GameObject region = GameObject.Find("Vilarejo_do_Bilu");
        GameObject systems = GameObject.Find("Sistemas");
        if (region == null || systems == null)
        {
            EditorUtility.DisplayDialog("Cena errada", "Abra a cena Regiao1_VilarejoDoBilu e rode de novo.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Transito");

        TrafficSystem traffic = systems.TryGetComponent(out TrafficSystem existing) ? existing : Undo.AddComponent<TrafficSystem>(systems);
        SerializedObject data = new SerializedObject(traffic);

        SerializedProperty roads = data.FindProperty("roads");
        roads.arraySize = Roads.Length;
        for (int i = 0; i < Roads.Length; i++)
        {
            SerializedProperty road = roads.GetArrayElementAtIndex(i);
            road.FindPropertyRelative("name").stringValue = Roads[i].name;
            road.FindPropertyRelative("eastWest").boolValue = Roads[i].eastWest;
            road.FindPropertyRelative("at").floatValue = Roads[i].at;
            road.FindPropertyRelative("from").floatValue = Roads[i].from;
            road.FindPropertyRelative("to").floatValue = Roads[i].to;
        }

        SerializedProperty colors = data.FindProperty("bodyColors");
        colors.arraySize = CarColors.Length;
        for (int i = 0; i < CarColors.Length; i++)
        {
            colors.GetArrayElementAtIndex(i).objectReferenceValue = Mat("Props", CarColors[i].name, CarColors[i].hex);
        }

        data.FindProperty("glass").objectReferenceValue = Mat("Props", "Carro_Vidro", "BFF3F7");
        data.FindProperty("wheel").objectReferenceValue = Mat("Props", "Roda", "2B2B33");
        data.FindProperty("eye").objectReferenceValue = Mat("Aliens", "Alien_Olho", "1B1530");

        string[] skins = { "Lilas", "Verde", "Azul", "Rosa", "Laranja" };
        SerializedProperty driverSkins = data.FindProperty("driverSkins");
        driverSkins.arraySize = 0;
        foreach (string skin in skins)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/Aliens/M_Alien_{skin}.mat");
            if (material != null)
            {
                driverSkins.arraySize++;
                driverSkins.GetArrayElementAtIndex(driverSkins.arraySize - 1).objectReferenceValue = material;
            }
        }

        data.ApplyModifiedProperties();

        int moved = MoveParkedCarsOffLanes();

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(systems.scene);
        Selection.activeGameObject = systems;

        string message = $"Trânsito preparado nas {Roads.Length} ruas.\n{moved} carro(s) estacionado(s) tirados das faixas.\n\nSALVE A CENA com Ctrl+S e dê Play.";
        Debug.Log(message);
        EditorUtility.DisplayDialog("Trânsito pronto", message, "OK");
    }

    // Carros estacionados no meio das faixas vão para a beira da rua.
    private static int MoveParkedCarsOffLanes()
    {
        int moved = 0;
        foreach (Rigidbody body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (!body.name.StartsWith("Carro") || body.GetComponent<TrafficCar>() != null)
            {
                continue;
            }

            Vector3 position = body.transform.position;
            foreach (RoadSetup road in Roads)
            {
                float across = road.eastWest ? position.z - road.at : position.x - road.at;
                float along = road.eastWest ? position.x : position.z;
                if (Mathf.Abs(across) >= 5f || along < road.from || along > road.to)
                {
                    continue;
                }

                float side = across >= 0f ? 1f : -1f;
                Undo.RecordObject(body.transform, "Tirar carro da faixa");
                if (road.eastWest)
                {
                    position.z = road.at + side * 6.4f;
                }
                else
                {
                    position.x = road.at + side * 6.4f;
                }

                body.transform.position = position;
                moved++;
                break;
            }
        }

        return moved;
    }

    private static Material Mat(string folder, string name, string hex)
    {
        string path = $"Assets/Art/Materials/{folder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        if (!AssetDatabase.IsValidFolder($"Assets/Art/Materials/{folder}"))
        {
            AssetDatabase.CreateFolder("Assets/Art/Materials", folder);
        }

        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        material.SetFloat("_Smoothness", 0.45f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
