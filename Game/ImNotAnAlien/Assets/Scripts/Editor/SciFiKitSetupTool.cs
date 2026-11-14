using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

// Missão 20 — Sci-Fi Essentials Kit (Quaternius, CC0), em Assets/Art/Props/SciFiEssentials.
// Menu: ImNotAnAlien > Preparar Sci-Fi Kit (Missao 20)
// 1. Texturas: mapas de relevo (Normal) como Normal Map; mapas de metal/brilho e oclusão (pasta
//    TexturesURP, convertidos do ORM do pacote) sem sRGB; tudo no máximo 2048.
// 2. Materiais em Materials/ (shader do horizonte curvo), um por material do pacote (MI_*).
// 3. Modelos: cada FBX passa a usar esses materiais; os inimigos (Enemy_*) importam as animações
//    com nomes simples (Idle, Walk, Run, Attack...) e as que repetem ficam em loop.
// 4. Kit de Blocos (Assets/Art/BlockKit.asset): antena, caixas, barris e móveis.
// 5. Kit de Armas (Assets/Resources/WeaponKit.asset): pistola da polícia, rifle do exército.
// 6. Inimigos (Assets/Resources/Enemies/): Drone, QuadShell e Trilobite, cada um com o seu
//    Animator (Animators/*.controller, um estado por animação) e os componentes de IA.
// Pode rodar de novo: refaz tudo.
public static class SciFiKitSetupTool
{
    private const string Root = "Assets/Art/Props/SciFiEssentials/";
    private const string Models = Root + "Models/";
    private const string Textures = Root + "Textures/";
    private const string TexturesUrp = Root + "TexturesURP/";
    private const string Materials = Root + "Materials/";
    private const string KitPath = "Assets/Art/BlockKit.asset";

    // Material do pacote -> (cor base, prefixo dos outros mapas, tem emissão)
    private static readonly Dictionary<string, (string baseColor, string prefix, bool emissive)> MaterialMaps =
        new Dictionary<string, (string, string, bool)>
        {
            { "MI_Enemies", ("T_Enemies_BaseColor", "T_Enemies", true) },
            { "MI_Enemies_Large", ("T_Enemies_Large_BaseColor", "T_Enemies_Large", true) },
            { "MI_Guns_Batch1", ("T_Guns_Batch1_BaseColor", "T_Guns_Batch1", true) },
            { "MI_Guns_Batch2", ("T_Guns_Batch2_BaseColor", "T_Guns_Batch2", true) },
            { "MI_Props_Batch1", ("T_Props_Batch1_BaseColor", "T_Props_Batch1", false) },
            { "MI_Props_Crates", ("T_Props_Crates_BaseColor", "T_Props_Crates", false) },
            { "MI_Trim_01", ("T_Trim_01_BaseColor", "T_Trim_01", false) },
            { "MI_Trim_02", ("T_Trim_02_BaseColor", "T_Trim_02", false) },
            { "MI_Trim_03", ("T_Trim_03_BaseColor", "T_Trim_03", false) },
            { "MI_Trim_03_Dark", ("T_Trim_03_Dark", "T_Trim_03", false) },
        };

    private static readonly string[] LoopClips = { "Idle", "Walk", "Run", "Charging", "Charge", "Hanging", "AttackAuto" };

    [MenuItem("ImNotAnAlien/Preparar Sci-Fi Kit (Missao 20)")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(Root.TrimEnd('/')))
        {
            EditorUtility.DisplayDialog("Sci-Fi Kit", $"Não achei a pasta {Root}.", "OK");
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            SetupTextures();
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.Refresh();
        Dictionary<string, Material> materials = BuildMaterials();
        int models = SetupModels(materials);
        bool kitOk = FillKit();
        FillWeapons();
        int enemies = BuildEnemies();
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Sci-Fi Kit",
            $"Pronto: {materials.Count} materiais e {models} modelos preparados." +
            (kitOk ? "\nKit de Blocos atualizado (antena, caixas, barris e móveis)." : "\nNão achei o Kit de Blocos em " + KitPath + ".") +
            "\nKit de Armas em " + WeaponKitPath + " (pistola da polícia, rifle do exército)." +
            $"\n{enemies} inimigos em Assets/Resources/Enemies (drone, robô de 4 patas, trilobite).",
            "OK");
    }

    // ---------- Texturas ----------

    private static void SetupTextures()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Textures.TrimEnd('/'), TexturesUrp.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                continue;
            }

            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool changed = false;
            if (file.EndsWith("_Normal") && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                changed = true;
            }

            bool linear = file.EndsWith("_MetalSmooth") || file.EndsWith("_AO");
            if (linear && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                changed = true;
            }

            if (importer.maxTextureSize > 2048)
            {
                importer.maxTextureSize = 2048;
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
        Shader shader = Shader.Find(CurvedHorizon.ShaderName) ?? Shader.Find("Universal Render Pipeline/Lit");
        if (!AssetDatabase.IsValidFolder(Materials.TrimEnd('/')))
        {
            AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Materials");
        }

        Dictionary<string, Material> result = new Dictionary<string, Material>();
        foreach (KeyValuePair<string, (string baseColor, string prefix, bool emissive)> pair in MaterialMaps)
        {
            string path = Materials + pair.Key + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = pair.Key };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            Texture2D baseMap = Tex(Textures + pair.Value.baseColor + ".png");
            Texture2D normal = Tex(Textures + pair.Value.prefix + "_Normal.png");
            Texture2D metal = Tex(TexturesUrp + pair.Value.prefix + "_MetalSmooth.png");
            Texture2D occlusion = Tex(TexturesUrp + pair.Value.prefix + "_AO.png");
            Texture2D emission = pair.Value.emissive ? Tex(Textures + pair.Value.prefix + "_Emissive.png") : null;

            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", baseMap);
            material.mainTexture = baseMap;

            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            Keyword(material, "_NORMALMAP", normal != null);

            material.SetTexture("_MetallicGlossMap", metal);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 0.85f); // com o mapa, multiplica o brilho dele
            Keyword(material, "_METALLICSPECGLOSSMAP", metal != null);

            material.SetTexture("_OcclusionMap", occlusion);
            material.SetFloat("_OcclusionStrength", 1f);
            Keyword(material, "_OCCLUSIONMAP", occlusion != null);

            material.SetTexture("_EmissionMap", emission);
            material.SetColor("_EmissionColor", emission != null ? Color.white * 2f : Color.black);
            Keyword(material, "_EMISSION", emission != null);
            material.globalIlluminationFlags = emission != null ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            EditorUtility.SetDirty(material);
            result[pair.Key] = material;
        }

        return result;
    }

    private static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    private static void Keyword(Material material, string keyword, bool on)
    {
        if (on)
        {
            material.EnableKeyword(keyword);
        }
        else
        {
            material.DisableKeyword(keyword);
        }
    }

    // ---------- Modelos ----------

    private static int SetupModels(Dictionary<string, Material> materials)
    {
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Models.TrimEnd('/') }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
            {
                continue;
            }

            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool enemy = file.StartsWith("Enemy_");

            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            foreach (KeyValuePair<string, Material> pair in materials)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            }

            importer.importCameras = false;
            importer.importLights = false;

            if (enemy)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.importAnimation = true;
                ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    string name = clip.takeName.Contains("|") ? clip.takeName.Substring(clip.takeName.LastIndexOf('|') + 1) : clip.takeName;
                    clip.name = name;
                    clip.loopTime = LoopClips.Contains(name);
                }

                importer.clipAnimations = clips;
            }
            else
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }

            importer.SaveAndReimport();
            count++;
        }

        return count;
    }

    // ---------- Kit de Blocos ----------

    private static bool FillKit()
    {
        BlockKit kit = AssetDatabase.LoadAssetAtPath<BlockKit>(KitPath);
        if (kit == null)
        {
            return false;
        }

        kit.satelliteDish = Model("Prop_SatelliteDish");
        kit.crates = Models_("Prop_Crate", "Prop_Crate_Tarp");
        kit.largeCrates = Models_("Prop_Crate_Large", "Prop_Crate_Tarp_Large");
        kit.barrels = Models_("Prop_Barrel1", "Prop_Barrel2_Closed", "Prop_Barrel2_Open");
        kit.desks = Models_("Prop_Desk_Small", "Prop_Desk_Medium", "Prop_Desk_L");
        kit.shelves = Models_("Prop_Shelves_ThinShort", "Prop_Shelves_ThinTall", "Prop_Shelves_WideShort", "Prop_Shelves_WideTall");
        kit.locker = Model("Prop_Locker");
        kit.chair = Model("Prop_Chair");
        kit.mug = Model("Prop_Mug");
        EditorUtility.SetDirty(kit);
        return true;
    }

    private const string WeaponKitPath = "Assets/Resources/WeaponKit.asset";
    private const string EnemiesPath = "Assets/Resources/Enemies/";
    private const string Animators = Root + "Animators/";

    // ---------- Inimigos ----------

    private static int BuildEnemies()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources/Enemies"))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Enemies");
        }

        if (!AssetDatabase.IsValidFolder(Animators.TrimEnd('/')))
        {
            AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Animators");
        }

        int count = 0;
        count += BuildEnemy("Enemy_EyeDrone", "Drone", 1.3f, SetupDrone) ? 1 : 0;
        count += BuildEnemy("Enemy_QuadShell", "QuadShell", 2.2f, SetupQuad) ? 1 : 0;
        count += BuildEnemy("Enemy_Trilobite", "Trilobite", 0.85f, SetupTrilobite) ? 1 : 0;
        return count;
    }

    private static bool BuildEnemy(string modelName, string prefabName, float scale, System.Action<GameObject, Bounds> setup)
    {
        string modelPath = Models + modelName + ".fbx";
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            return false;
        }

        AnimatorController controller = BuildController(modelPath, prefabName);

        GameObject root = new GameObject(prefabName);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Modelo";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * scale;
        foreach (Collider extra in visual.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(extra);
        }

        Animator animator = visual.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = visual.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (Renderer part in visual.GetComponentsInChildren<Renderer>(true))
        {
            if (!any)
            {
                bounds = part.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(part.bounds);
            }
        }

        // Modelo com os pés no chão do objeto raiz.
        visual.transform.localPosition = new Vector3(0f, -bounds.min.y, 0f);
        bounds.center += Vector3.up * -bounds.min.y;

        setup(root, bounds);
        foreach (Renderer part in visual.GetComponentsInChildren<Renderer>(true))
        {
            if (part is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true; // o "tanque" grande não some na borda da tela
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, EnemiesPath + prefabName + ".prefab");
        Object.DestroyImmediate(root);
        return true;
    }

    // Um estado por animação do FBX (Idle é o primeiro). A IA troca com CrossFade pelo nome.
    private static AnimatorController BuildController(string modelPath, string name)
    {
        string path = Animators + name + ".controller";
        AssetDatabase.DeleteAsset(path);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        foreach (AnimationClip clip in clips.OrderBy(c => c.name == "Idle" ? 0 : 1))
        {
            AnimatorState state = machine.AddState(clip.name);
            state.motion = clip;
            if (clip.name == "Idle")
            {
                machine.defaultState = state;
            }
        }

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void SetupDrone(GameObject root, Bounds bounds)
    {
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 25f;
        body.useGravity = false;
        SphereCollider sphere = root.AddComponent<SphereCollider>();
        sphere.center = bounds.center;
        sphere.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 0.85f;
        root.AddComponent<TelekinesisTarget>();
        root.AddComponent<EnemyDrone>();
    }

    private static void SetupQuad(GameObject root, Bounds bounds)
    {
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.mass = 3000f;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.7f, 0.5f, 2.5f);
        agent.height = bounds.size.y;
        agent.speed = 3f;
        agent.angularSpeed = 140f;
        agent.acceleration = 6f;
        agent.stoppingDistance = 3f;
        root.AddComponent<EnemyQuadShell>();
    }

    private static void SetupTrilobite(GameObject root, Bounds bounds)
    {
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.mass = 150f;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size * 0.85f;
        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.radius = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.6f, 0.4f, 1.5f);
        agent.height = bounds.size.y;
        agent.speed = 1.6f;
        agent.angularSpeed = 300f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1.5f;
        root.AddComponent<AlienHealth>();
        root.AddComponent<EnemyTrilobite>();
        root.AddComponent<TelekinesisTarget>();
    }

    private static void FillWeapons()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        WeaponKit kit = AssetDatabase.LoadAssetAtPath<WeaponKit>(WeaponKitPath);
        if (kit == null)
        {
            kit = ScriptableObject.CreateInstance<WeaponKit>();
            AssetDatabase.CreateAsset(kit, WeaponKitPath);
        }

        kit.pistol = Model("Gun_Pistol");
        kit.revolver = Model("Gun_Revolver");
        kit.rifle = Model("Gun_Rifle");
        kit.sniper = Model("Gun_Sniper");
        EditorUtility.SetDirty(kit);
    }

    private static GameObject Model(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".fbx");

    private static GameObject[] Models_(params string[] names) => names.Select(Model).Where(m => m != null).ToArray();
}
