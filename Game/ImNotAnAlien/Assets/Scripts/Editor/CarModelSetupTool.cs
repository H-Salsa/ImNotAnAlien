using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Ferramenta de editor da Missão 15 — carros flutuantes 3D (feitos no Blender por car_builder.py).
// Menu: ImNotAnAlien > Montar Carros 3D (Missao 15)
// Rode com a cena Regiao1_VilarejoDoBilu aberta. A ferramenta:
// 1. Ajusta a importação de Art/Vehicles/Carro_Civil.fbx e Viatura.fbx e troca os materiais do FBX
//    por materiais URP (vidro transparente; faróis, lanternas, discos e giroflex que brilham).
// 2. Liga o Carro_Civil no TrafficSystem: o trânsito passa a usar o carro 3D com pintura sorteada.
//    (A Viatura fica pronta para quando a polícia tiver carros.)
public static class CarModelSetupTool
{
    private static readonly string[] Models =
    {
        "Assets/Art/Vehicles/Carro_Civil.fbx",
        "Assets/Art/Vehicles/Viatura.fbx",
    };

    private const string MaterialFolder = "Assets/Art/Materials/Veiculos";

    // Nome no Blender -> cor, suavidade, brilho (0 = não brilha), transparência (1 = opaco)
    private static readonly (string name, string hex, float smooth, float glow, float alpha)[] MaterialMap =
    {
        ("Carro_Pintura", "F1DFC0", 0.55f, 0f, 1f),
        ("Carro_Detalhe", "E2735A", 0.55f, 0f, 1f),
        ("Carro_Parachoque", "F1DFC0", 0.5f, 0f, 1f),
        ("Carro_Cromado", "9AA3AD", 0.8f, 0f, 1f),
        ("Carro_Vidro", "8FD6E8", 0.9f, 0f, 0.35f),
        ("Carro_Interior", "D9CBB4", 0.2f, 0f, 1f),
        ("Carro_Escuro", "2A2D33", 0.35f, 0f, 1f),
        ("Carro_Interior_Escuro", "5A4E44", 0.1f, 0f, 1f),
        ("Carro_Farol", "FFF1C9", 0.8f, 1.5f, 1f),
        ("Carro_Lanterna", "E0473F", 0.7f, 0.8f, 1f),
        ("Carro_Disco", "3A3D44", 0.5f, 0f, 1f),
        ("Carro_Disco_Luz", "5FF2D8", 0.7f, 2.5f, 1f),
        ("Policia_Giroflex_Azul", "2F6BFF", 0.8f, 2.5f, 1f),
        ("Policia_Giroflex_Vermelho", "FF3B3B", 0.8f, 2.5f, 1f),
        ("Policia_Giroflex_Branco", "F2F2F2", 0.8f, 1f, 1f),
        ("Policia_Distintivo", "E8D8B0", 0.5f, 0f, 1f),
    };

    [MenuItem("ImNotAnAlien/Montar Carros 3D (Missao 15)")]
    public static void Setup()
    {
        int imported = 0;
        foreach (string path in Models)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"Missão 15: não achei {path} (exporte do Blender).");
                continue;
            }

            importer.animationType = ModelImporterAnimationType.None;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true; // a carcaça é amassada na explosão (HoverCar)
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var (name, hex, smooth, glow, alpha) in MaterialMap)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name),
                    MakeMaterial(name, hex, smooth, glow, alpha));
            }

            importer.SaveAndReimport();
            imported++;
        }

        string trafficMessage;
        TrafficSystem traffic = Object.FindFirstObjectByType<TrafficSystem>();
        GameObject civil = AssetDatabase.LoadAssetAtPath<GameObject>(Models[0]);
        if (traffic != null && civil != null)
        {
            SerializedObject data = new SerializedObject(traffic);
            data.FindProperty("carModel").objectReferenceValue = civil;
            data.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(traffic.gameObject.scene);
            trafficMessage = "O trânsito agora usa o carro 3D. SALVE A CENA (Ctrl+S).";
        }
        else
        {
            trafficMessage = "Trânsito não encontrado nesta cena: abra a Regiao1_VilarejoDoBilu e rode de novo.";
        }

        AssetDatabase.SaveAssets();
        string message = $"{imported} modelo(s) de carro configurado(s).\n{trafficMessage}";
        Debug.Log(message);
        EditorUtility.DisplayDialog("Carros 3D prontos", message, "OK");
    }

    private static Material MakeMaterial(string name, string hex, float smoothness, float glow, float alpha)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets/Art/Materials", "Veiculos");
        }

        string path = $"{MaterialFolder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        color.a = alpha;
        material.color = color;
        material.SetFloat("_Smoothness", smoothness);

        if (glow > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * glow);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }

        if (alpha < 1f)
        {
            // Vidro: transparente.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f); // mostra os dois lados
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
        }

        EditorUtility.SetDirty(material);
        return material;
    }
}
