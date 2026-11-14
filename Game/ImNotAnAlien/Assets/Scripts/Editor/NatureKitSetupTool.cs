using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Missão 20 — natureza do planeta: Stylized Nature MegaKit + Textured Stylized Trees (Quaternius, CC0),
// em Assets/Art/Nature (MegaKit, Trees e Generated = texturas de folha clarinhas para pintar por bioma).
// Menu: ImNotAnAlien > Preparar Natureza (Missao 20)
// 1. Texturas: mapas de relevo como Normal Map; folhas com transparência que não some ao longe.
// 2. Materiais em Assets/Art/Nature/Materials (shader do horizonte curvo): folhas e grama recortadas
//    (alpha clip) e com as duas faces; tudo com GPU instancing (milhares de plantas na tela).
// 3. Modelos usam esses materiais.
// 4. Material da grama de triângulos ao vento (NK_GrassBlades, shader "ImNotAnAlien/Grass Wind").
// 5. Kit de Natureza (Assets/Art/Nature/NatureKit.asset), ligado ao Kit de Blocos.
// Pode rodar de novo.
public static class NatureKitSetupTool
{
    private const string Root = "Assets/Art/Nature/";
    private const string MegaModels = Root + "MegaKit/Models/";
    private const string MegaTextures = Root + "MegaKit/Textures/";
    private const string TreeModels = Root + "Trees/Models/";
    private const string TreeTextures = Root + "Trees/Textures/";
    private const string Generated = Root + "Generated/";
    private const string Materials = Root + "Materials/";
    private const string KitPath = Root + "NatureKit.asset";
    private const string BlockKitPath = "Assets/Art/BlockKit.asset";

    private enum Kind { Opaque, Cutout, CutoutTint, OpaqueTint }

    private struct Def
    {
        public string texture;
        public string normal;
        public Kind kind;
        public Color color;
    }

    private static Def D(string texture, Kind kind, string normal = null, Color? color = null) =>
        new Def { texture = texture, normal = normal, kind = kind, color = color ?? Color.white };

    private static readonly Color Leaf = new Color(0.43f, 0.70f, 0.31f);

    // Nome do material dentro do FBX -> como montar.
    private static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>
    {
        // MegaKit
        { "Bark_NormalTree", D(MegaTextures + "Bark_NormalTree.png", Kind.Opaque, MegaTextures + "Bark_NormalTree_Normal.png") },
        { "Bark_DeadTree", D(MegaTextures + "Bark_DeadTree.png", Kind.Opaque, MegaTextures + "Bark_DeadTree_Normal.png") },
        { "Bark_TwistedTree", D(MegaTextures + "Bark_TwistedTree.png", Kind.Opaque, MegaTextures + "Bark_TwistedTree_Normal.png") },
        { "Material", D(MegaTextures + "Bark_NormalTree.png", Kind.Opaque, MegaTextures + "Bark_NormalTree_Normal.png") },
        { "Leaves_NormalTree", D(MegaTextures + "Leaves_NormalTree.png", Kind.CutoutTint, null, Leaf) },
        { "Leaves_TwistedTree", D(MegaTextures + "Leaves_TwistedTree.png", Kind.CutoutTint, null, new Color(0.89f, 0.45f, 0.62f)) },
        { "Leaves_Pine", D(MegaTextures + "Leaf_Pine.png", Kind.CutoutTint, null, new Color(0.30f, 0.52f, 0.32f)) },
        { "Leaves", D(MegaTextures + "Leaves.png", Kind.Cutout) },
        { "Flowers", D(MegaTextures + "Flowers.png", Kind.Cutout) },
        { "Grass", D(Generated + "Grass_Tint.png", Kind.OpaqueTint, null, Leaf) },
        { "Mushrooms", D(MegaTextures + "Mushrooms.png", Kind.Opaque) },
        { "PathRocks", D(MegaTextures + "PathRocks_Diffuse.png", Kind.Opaque) },
        { "Rocks", D(MegaTextures + "Rocks_Diffuse.png", Kind.Opaque) },
        // Trees
        { "Bark", D(TreeTextures + "Tree_Bark.jpg", Kind.Opaque) },
        { "Birch_Bark", D(TreeTextures + "Birch_Bark.png", Kind.Opaque) },
        { "Birch_Leaves", D(Generated + "Birch_Leaves_Tint.png", Kind.CutoutTint, null, Leaf) },
        { "Pine_Leaves", D(Generated + "Pine_Leaves_Tint.png", Kind.CutoutTint, null, new Color(0.30f, 0.52f, 0.32f)) },
        { "Tree_Leaves", D(Generated + "Tree_Leaves_Tint.png", Kind.CutoutTint, null, Leaf) },
    };

    [MenuItem("ImNotAnAlien/Preparar Natureza (Missao 20)")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(Root.TrimEnd('/')))
        {
            EditorUtility.DisplayDialog("Natureza", $"Não achei a pasta {Root}.", "OK");
            return;
        }

        SetupTextures();
        Dictionary<string, Material> materials = BuildMaterials();
        Material desert = BuildMaterial("Rocks_Desert", D(MegaTextures + "Rocks_Desert_Diffuse.png", Kind.Opaque));
        int models = SetupModels(materials);
        NatureKit kit = FillKit(materials, desert);
        kit.grassBlades = GrassMaterial();
        EditorUtility.SetDirty(kit);
        bool linked = LinkBlockKit(kit);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Natureza",
            $"Pronto: {materials.Count + 1} materiais e {models} modelos preparados.\nKit de Natureza em {KitPath}." +
            (kit.grassBlades != null ? "\nGrama ao vento: " + GrassPath : "\nShader 'ImNotAnAlien/Grass Wind' não encontrado: ficou a grama de modelos.") +
            (linked ? "\nLigado ao Kit de Blocos: comece um jogo novo para ver." : "\nNão achei o Kit de Blocos em " + BlockKitPath + "."),
            "OK");
    }

    // ---------- Texturas ----------

    private static void SetupTextures()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                continue;
            }

            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool normal = file.EndsWith("_Normal");
            bool foliage = !normal && (file.Contains("Leaves") || file.Contains("Leaf") || file.Contains("Flowers") || file.Contains("Grass"));
            bool changed = false;

            if (normal && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }

            if (foliage && (!importer.alphaIsTransparency || !importer.mipMapsPreserveCoverage))
            {
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = true; // folha não "some" ao longe
                importer.alphaTestReferenceValue = 0.4f;
                changed = true;
            }

            if (importer.maxTextureSize > 1024 && !normal)
            {
                importer.maxTextureSize = 1024; // estilo cartunesco: 1024 basta e economiza memória
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }
    }

    // ---------- Materiais ----------

    private static Dictionary<string, Material> BuildMaterials()
    {
        if (!AssetDatabase.IsValidFolder(Materials.TrimEnd('/')))
        {
            AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Materials");
        }

        Dictionary<string, Material> result = new Dictionary<string, Material>();
        foreach (KeyValuePair<string, Def> pair in Defs)
        {
            result[pair.Key] = BuildMaterial(pair.Key, pair.Value);
        }

        return result;
    }

    private static Material BuildMaterial(string name, Def def)
    {
        Shader shader = Shader.Find(CurvedHorizon.ShaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
        string path = Materials + "NK_" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = "NK_" + name };
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(def.texture);
        Texture2D normal = def.normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(def.normal) : null;
        material.SetTexture("_BaseMap", baseMap);
        material.mainTexture = baseMap;
        material.SetColor("_BaseColor", def.color);
        material.color = def.color;
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0.12f);
        material.SetTexture("_BumpMap", normal);
        Keyword(material, "_NORMALMAP", normal != null);

        bool cutout = def.kind == Kind.Cutout || def.kind == Kind.CutoutTint;
        bool twoSided = cutout || def.kind == Kind.OpaqueTint; // folhas, flores e grama: as duas faces
        material.SetFloat("_AlphaClip", cutout ? 1f : 0f);
        material.SetFloat("_Cutoff", 0.4f);
        Keyword(material, "_ALPHATEST_ON", cutout);
        material.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
        material.renderQueue = cutout ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
        material.SetFloat("_Cull", twoSided ? 0f : 2f);
        material.doubleSidedGI = twoSided;
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void Keyword(Material material, string keyword, bool on)
    {
        if (on) material.EnableKeyword(keyword);
        else material.DisableKeyword(keyword);
    }

    // ---------- Modelos ----------

    private static int SetupModels(Dictionary<string, Material> materials)
    {
        int count = 0;
        foreach (string folder in new[] { MegaModels, TreeModels })
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    continue;
                }

                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (KeyValuePair<string, Material> pair in materials)
                {
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
                }

                importer.importCameras = false;
                importer.importLights = false;
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
                count++;
            }
        }

        return count;
    }

    // ---------- Grama ao vento ----------

    private const string GrassPath = Materials + "NK_GrassBlades.mat";

    // Material da grama de triângulos. Os ajustes (vento, empurrão, distância) ficam no material:
    // dá para mexer nele com o jogo rodando.
    private static Material GrassMaterial()
    {
        Shader shader = Shader.Find("ImNotAnAlien/Grass Wind");
        if (shader == null)
        {
            return null;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(GrassPath);
        if (material == null)
        {
            material = new Material(shader) { name = "NK_GrassBlades" };
            AssetDatabase.CreateAsset(material, GrassPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- Kit ----------

    private static NatureKit FillKit(Dictionary<string, Material> materials, Material desert)
    {
        NatureKit kit = AssetDatabase.LoadAssetAtPath<NatureKit>(KitPath);
        if (kit == null)
        {
            kit = ScriptableObject.CreateInstance<NatureKit>();
            AssetDatabase.CreateAsset(kit, KitPath);
        }

        kit.commonTrees = Models(MegaModels, "CommonTree_");
        kit.roundTrees = Models(TreeModels, "Tree_");
        kit.birches = Models(TreeModels, "Birch_");
        kit.pines = Models(MegaModels, "Pine_").Concat(Models(TreeModels, "Pine_")).ToArray();
        kit.twistedTrees = Models(MegaModels, "TwistedTree_");
        kit.deadTrees = Models(MegaModels, "DeadTree_").Concat(Models(TreeModels, "DeadTree_")).ToArray();
        kit.deadBirches = Models(TreeModels, "DeadBirch_");
        kit.bushes = new[] { Model(MegaModels + "Bush_Common.fbx") }.Where(m => m != null).ToArray();
        kit.flowerBushes = new[] { Model(MegaModels + "Bush_Common_Flowers.fbx") }.Where(m => m != null).ToArray();
        kit.grasses = Models(MegaModels, "Grass_");
        kit.flowers = Models(MegaModels, "Flower_"); // Petal_ são pétalas soltas no chão: ficam de fora
        kit.ferns = Models(MegaModels, "Fern_").Concat(Models(MegaModels, "Plant_")).Concat(Models(MegaModels, "Clover_")).ToArray();
        kit.mushrooms = Models(MegaModels, "Mushroom_");
        kit.rocks = Models(MegaModels, "Rock_Medium_");
        kit.pebbles = Models(MegaModels, "Pebble_");
        kit.rockPaths = Models(MegaModels, "RockPath_");
        kit.normalRock = materials["Rocks"];
        kit.desertRock = desert;
        kit.tintable = Defs.Where(p => p.Value.kind == Kind.CutoutTint || p.Value.kind == Kind.OpaqueTint)
            .Select(p => materials[p.Key]).ToArray();
        EditorUtility.SetDirty(kit);
        return kit;
    }

    private static bool LinkBlockKit(NatureKit nature)
    {
        BlockKit kit = AssetDatabase.LoadAssetAtPath<BlockKit>(BlockKitPath);
        if (kit == null)
        {
            return false;
        }

        kit.nature = nature;
        EditorUtility.SetDirty(kit);
        return true;
    }

    private static GameObject Model(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    // Modelos da pasta cujo nome começa com o prefixo (Tree_ não pega DeadTree_/CommonTree_).
    private static GameObject[] Models(string folder, string prefix) =>
        AssetDatabase.FindAssets("t:Model", new[] { folder.TrimEnd('/') })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix))
            .OrderBy(p => p)
            .Select(Model)
            .Where(m => m != null)
            .ToArray();
}
