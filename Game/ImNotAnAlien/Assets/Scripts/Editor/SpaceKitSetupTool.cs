using System.Linq;
using UnityEditor;
using UnityEngine;

// Missão 20 — Ultimate Space Kit (Quaternius, CC0): árvores, plantas e pedras alienígenas + planetas no céu.
// Arquivos em Assets/Art/SpaceKit (Models = natureza, Planets = planetas, SK_Atlas.png = paleta de cores).
// Menu: ImNotAnAlien > Preparar Space Kit (Missao 20)
// 1. Paleta: filtro "ponto" e sem mipmap (é uma tabela de cores: não pode misturar uma cor com a vizinha).
// 2. Materiais: SK_Atlas (horizonte curvo, igual ao resto do mundo) e SK_SkyPlanet (planetas no céu).
// 3. Modelos usam o SK_Atlas.
// 4. Liga tudo ao Kit de Natureza (Assets/Art/Nature/NatureKit.asset): os biomas passam a misturar
//    árvores alienígenas e o céu ganha planetas. Rode antes "Preparar Natureza" se o kit não existir.
// Pode rodar de novo.
public static class SpaceKitSetupTool
{
    private const string Root = "Assets/Art/SpaceKit/";
    private const string ModelsFolder = Root + "Models";
    private const string PlanetsFolder = Root + "Planets";
    private const string AtlasPath = Root + "SK_Atlas.png";
    private const string AtlasMaterialPath = Root + "SK_Atlas.mat";
    private const string SkyMaterialPath = Root + "SK_SkyPlanet.mat";
    private const string NatureKitPath = "Assets/Art/Nature/NatureKit.asset";

    [MenuItem("ImNotAnAlien/Preparar Space Kit (Missao 20)")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(Root.TrimEnd('/')))
        {
            EditorUtility.DisplayDialog("Space Kit", $"Não achei a pasta {Root}.", "OK");
            return;
        }

        Texture2D atlas = SetupAtlas();
        Material atlasMaterial = AtlasMaterial(atlas);
        Material skyMaterial = SkyMaterial(atlas);
        int models = SetupModels(ModelsFolder, atlasMaterial) + SetupModels(PlanetsFolder, atlasMaterial);

        NatureKit kit = AssetDatabase.LoadAssetAtPath<NatureKit>(NatureKitPath);
        if (kit != null)
        {
            kit.alienBlob = new GameObject[0]; // removida a pedido do autor (não gostou)
            kit.alienFloating = Models(ModelsFolder, "Tree_Floating_");
            kit.alienLava = Models(ModelsFolder, "Tree_Lava_");
            kit.alienLight = Models(ModelsFolder, "Tree_Light_");
            kit.alienSpikes = Models(ModelsFolder, "Tree_Spikes_");
            kit.alienSpiral = Models(ModelsFolder, "Tree_Spiral_");
            kit.alienSwirl = Models(ModelsFolder, "Tree_Swirl_");
            kit.alienPlants = Models(ModelsFolder, "Plant_").Concat(Models(ModelsFolder, "Bush_")).ToArray();
            kit.alienGrass = Models(ModelsFolder, "Grass_");
            kit.alienRocks = Models(ModelsFolder, "Rock_");
            kit.skyPlanets = Models(PlanetsFolder, "Planet_");
            kit.skyPlanetMaterial = skyMaterial;
            kit.alienMaterial = atlasMaterial;
            EditorUtility.SetDirty(kit);
        }

        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog("Space Kit",
            $"Pronto: {models} modelos preparados." +
            (kit != null
                ? $"\nKit de Natureza: {kit.alienFloating.Length + kit.alienLava.Length + kit.alienLight.Length + kit.alienSpikes.Length + kit.alienSpiral.Length + kit.alienSwirl.Length} árvores alienígenas, " +
                  $"{kit.alienPlants.Length} plantas, {kit.alienRocks.Length} pedras e {kit.skyPlanets.Length} planetas.\nComece um jogo novo (ou recarregue) para ver."
                : "\nNão achei o Kit de Natureza em " + NatureKitPath + ": rode antes 'Preparar Natureza (Missao 20)'."),
            "OK");
    }

    private static Texture2D SetupAtlas()
    {
        if (AssetImporter.GetAtPath(AtlasPath) is TextureImporter importer)
        {
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.isReadable = true; // o jogo faz uma cópia recolorida por bioma
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
    }

    private static Material AtlasMaterial(Texture2D atlas)
    {
        Shader shader = Shader.Find(CurvedHorizon.ShaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
        Material material = LoadOrCreate(AtlasMaterialPath, shader, "SK_Atlas");
        material.SetTexture("_BaseMap", atlas);
        material.mainTexture = atlas;
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.15f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material SkyMaterial(Texture2D atlas)
    {
        Shader shader = Shader.Find("ImNotAnAlien/Sky Planet");
        if (shader == null)
        {
            Debug.LogWarning("Space Kit: shader 'ImNotAnAlien/Sky Planet' não encontrado (Assets/Shaders/SkyPlanet.shader).");
            return null;
        }

        Material material = LoadOrCreate(SkyMaterialPath, shader, "SK_SkyPlanet");
        material.SetTexture("_BaseMap", atlas);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material LoadOrCreate(string path, Shader shader, string name)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        return material;
    }

    // Todos os materiais de dentro do FBX (é um só: o atlas) passam a ser o SK_Atlas.
    private static int SetupModels(string folder, Material atlasMaterial)
    {
        if (!AssetDatabase.IsValidFolder(folder))
        {
            return 0;
        }

        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                continue;
            }

            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SaveAndReimport();

            foreach (Material embedded in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), atlasMaterial);
            }

            importer.SaveAndReimport();
            count++;
        }

        return count;
    }

    private static GameObject[] Models(string folder, string prefix) =>
        AssetDatabase.FindAssets("t:Model", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix))
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(m => m != null)
            .ToArray();
}
