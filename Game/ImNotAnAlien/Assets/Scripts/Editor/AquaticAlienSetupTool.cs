using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Missão 20 — alienígena AQUÁTICA (maiô, barbatanas com borda que brilha, nadadeiras nos pés).
// Menu: ImNotAnAlien > Preparar Alien Aquatico (Missao 20)
// Arquivos exportados do Blender (Art/Blender/aquatic_builder.py) em Assets/Art/Characters/Aliens:
//   Alien_Aquatico.fbx       modelo (corpo + peças aquáticas num só, mesmo esqueleto dos outros aliens)
//   Alien_Aquatico_Nado.fbx  dois clipes de nado próprios (golfinho/sereia): Aq_Swim_Fwd_Loop e Aq_Swim_Idle_Loop
// A ferramenta:
// 1. Importa o modelo como Humanoid e troca os materiais do Blender pelos URP (borda das barbatanas brilha).
// 2. Importa os clipes de nado (em loop) usando o mesmo esqueleto.
// 3. Cria Alien_Aquatico.overrideController: o Alien.controller de sempre, mas no estado "Nadando"
//    usa os clipes próprios no lugar do nado do protagonista.
// 4. Cria Assets/Resources/Alienigena_Aquatica.prefab (cópia do Alienigena com só o modelo aquático e o
//    componente AquaticAlien). O PopulationSpawner acha esse prefab sozinho e solta algumas na água funda.
// Pode rodar de novo depois de exportar uma versão nova do Blender.
public static class AquaticAlienSetupTool
{
    private const string ModelPath = "Assets/Art/Characters/Aliens/Alien_Aquatico.fbx";
    private const string SwimPath = "Assets/Art/Characters/Aliens/Alien_Aquatico_Nado.fbx";
    private const string ControllerPath = "Assets/Art/Characters/Aliens/Alien.controller";
    private const string OverridePath = "Assets/Art/Characters/Aliens/Alien_Aquatico.overrideController";
    private const string SourcePrefab = "Assets/Prefabs/Alienigena.prefab";
    private const string TargetPrefab = "Assets/Resources/Alienigena_Aquatica.prefab";
    private const string VisualName = "Visual_Alien_Aq";
    private static readonly string[] OldVisuals = { "Visual_Alien", "Visual_Alien_M", "Visual_Alien_F", VisualName };

    // Materiais só da aquática (os de pele, olhos e boca vêm do AlienModelSetupTool).
    // "_Luz" no fim do nome = material que brilha.
    private static readonly (string blender, string unity, string hex, float smooth)[] AquaticMaterials =
    {
        ("Aq_Maio", "M_Alien3D_Aq_Maio", "243D65", 0.35f),
        ("Aq_Maio_Borda", "M_Alien3D_Aq_Maio_Borda", "EF694F", 0.4f),
        ("Aq_Fivela", "M_Alien3D_Aq_Fivela", "F1E3C2", 0.6f),
        ("Aq_Barbatana", "M_Alien3D_Aq_Barbatana", "8DE5D4", 0.65f),
        ("Aq_Brilho", "M_Alien3D_Aq_Luz", "62FFD7", 0.8f),
        ("Aq_Guelra", "M_Alien3D_Aq_Guelra", "226A65", 0.5f),
    };

    private static readonly (string blender, string original)[] SwimClips =
    {
        ("Aq_Swim_Fwd_Loop", "Swim_Fwd_Loop"),
        ("Aq_Swim_Idle_Loop", "Swim_Idle_Loop"),
    };

    [MenuItem("ImNotAnAlien/Preparar Alien Aquatico (Missao 20)")]
    public static void Run()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Alien aquático", "Importando o modelo...", 0.1f);
            if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter modelImporter))
            {
                EditorUtility.DisplayDialog("Alien aquático", $"Não achei {ModelPath}. Exporte do Blender primeiro.", "OK");
                return;
            }

            ConfigureModel(modelImporter);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Alien aquático",
                    "O esqueleto humanoide do Alien_Aquatico.fbx não ficou válido.\nSelecione o arquivo, aba Rig > Configure... e me mande um print.", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Alien aquático", "Importando o nado...", 0.35f);
            List<string> problems = new List<string>();
            Dictionary<string, AnimationClip> clips = ImportSwim(avatar, problems);

            EditorUtility.DisplayProgressBar("Alien aquático", "Criando o controlador...", 0.6f);
            AnimatorController baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (baseController == null)
            {
                EditorUtility.DisplayDialog("Alien aquático", $"Não achei {ControllerPath}. Rode antes ImNotAnAlien > Montar Aliens 3D.", "OK");
                return;
            }

            if (!baseController.parameters.Any(p => p.name == "Swimming"))
            {
                AlienSwimSetupTool.Run(); // cria o estado "Nadando" que vamos trocar
            }

            AnimatorOverrideController overrides = CreateOverride(baseController, clips, problems);

            EditorUtility.DisplayProgressBar("Alien aquático", "Montando o prefab...", 0.8f);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!BuildPrefab(model, avatar, overrides, problems))
            {
                return;
            }

            AssetDatabase.SaveAssets();
            string message = "Pronto! Alienígena aquática criada em " + TargetPrefab +
                             ".\nElas surgem na água funda (lago, mar e rios) perto de você." +
                             (problems.Count > 0 ? "\n\nAvisos:\n- " + string.Join("\n- ", problems) : "");
            Debug.Log("Alien aquático: " + message);
            EditorUtility.DisplayDialog("Alien aquático", message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void ConfigureModel(ModelImporter importer)
    {
        importer.bakeAxisConversion = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.importBlendShapes = true;
        importer.weldVertices = false; // igual aos outros aliens (expressões do rosto)
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        foreach (var (blender, unity, hex, smooth) in AlienModelSetupTool.MaterialMap.Concat(AquaticMaterials))
        {
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), blender),
                AlienModelSetupTool.MakeMaterial(unity, hex, smooth));
        }

        importer.SaveAndReimport();
    }

    private static Dictionary<string, AnimationClip> ImportSwim(Avatar avatar, List<string> problems)
    {
        Dictionary<string, AnimationClip> result = new Dictionary<string, AnimationClip>();
        if (!(AssetImporter.GetAtPath(SwimPath) is ModelImporter importer))
        {
            problems.Add($"Não achei {SwimPath}: ela vai nadar com o nado comum.");
            return result;
        }

        importer.bakeAxisConversion = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importAnimation = true;
        importer.importBlendShapes = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport(); // para os clipes padrão aparecerem

        // Os "takes" do FBX vêm como "Armature|Aq_Swim_Fwd_Loop": o clipe fica só com o fim do nome.
        List<ModelImporterClipAnimation> list = new List<ModelImporterClipAnimation>();
        List<string> found = new List<string>();
        foreach (TakeInfo take in importer.importedTakeInfos)
        {
            found.Add(take.name);
            string name = ShortName(take.name);
            if (!SwimClips.Any(c => c.blender == name) || list.Any(c => c.name == name))
            {
                continue;
            }

            float rate = take.sampleRate > 0f ? take.sampleRate : 24f;
            list.Add(LoopClip(new ModelImporterClipAnimation
            {
                name = name,
                takeName = take.name,
                firstFrame = Mathf.Round(take.startTime * rate),
                lastFrame = Mathf.Round(take.stopTime * rate),
            }));
        }

        if (list.Count == 0)
        {
            foreach (ModelImporterClipAnimation clip in importer.defaultClipAnimations)
            {
                string name = ShortName(clip.name);
                if (SwimClips.Any(c => c.blender == name))
                {
                    clip.name = name;
                    list.Add(LoopClip(clip));
                }
            }
        }

        importer.clipAnimations = list.ToArray();
        importer.SaveAndReimport();

        foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(SwimPath).OfType<AnimationClip>())
        {
            if (!clip.name.StartsWith("__preview__"))
            {
                result[ShortName(clip.name)] = clip;
            }
        }

        foreach (var (blender, _) in SwimClips)
        {
            if (!result.ContainsKey(blender))
            {
                problems.Add($"Clipe {blender} não encontrado em {SwimPath} (takes no arquivo: {string.Join(", ", found)}).");
            }
        }

        return result;
    }

    private static string ShortName(string name) =>
        name.Contains("|") ? name.Substring(name.LastIndexOf('|') + 1) : name;

    private static ModelImporterClipAnimation LoopClip(ModelImporterClipAnimation clip)
    {
        clip.loopTime = true;
        clip.loopPose = false;
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = true;
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionY = true;
        clip.heightFromFeet = false;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalPositionXZ = true;
        return clip;
    }

    private static AnimatorOverrideController CreateOverride(AnimatorController baseController,
        Dictionary<string, AnimationClip> clips, List<string> problems)
    {
        AnimatorOverrideController overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
        if (overrides == null)
        {
            overrides = new AnimatorOverrideController(baseController) { name = "Alien_Aquatico" };
            AssetDatabase.CreateAsset(overrides, OverridePath);
        }
        else
        {
            overrides.runtimeAnimatorController = baseController;
        }

        List<KeyValuePair<AnimationClip, AnimationClip>> pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrides.overridesCount);
        overrides.GetOverrides(pairs);
        int swapped = 0;
        for (int i = 0; i < pairs.Count; i++)
        {
            AnimationClip original = pairs[i].Key;
            AnimationClip replacement = null;
            foreach (var (blender, originalName) in SwimClips)
            {
                if (original != null && original.name == originalName && clips.TryGetValue(blender, out AnimationClip clip))
                {
                    replacement = clip;
                    swapped++;
                }
            }

            pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
        }

        overrides.ApplyOverrides(pairs);
        EditorUtility.SetDirty(overrides);
        if (swapped < SwimClips.Length)
        {
            problems.Add($"Só {swapped} de {SwimClips.Length} animações de nado foram trocadas (clipes do Alien.controller: " +
                         string.Join(", ", pairs.Where(p => p.Key != null && p.Key.name.Contains("Swim")).Select(p => p.Key.name)) +
                         "; clipes importados: " + string.Join(", ", clips.Keys) + ").");
        }

        return overrides;
    }

    private static bool BuildPrefab(GameObject model, Avatar avatar, AnimatorOverrideController controller, List<string> problems)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab) == null)
        {
            EditorUtility.DisplayDialog("Alien aquático", $"Não achei {SourcePrefab}.", "OK");
            return false;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        GameObject root = PrefabUtility.LoadPrefabContents(SourcePrefab);
        try
        {
            root.name = "Alienigena_Aquatica";
            Transform body = root.transform.Find("Corpo");
            if (body == null)
            {
                EditorUtility.DisplayDialog("Alien aquático", $"{SourcePrefab} não tem o filho 'Corpo'.", "OK");
                return false;
            }

            foreach (string oldName in OldVisuals)
            {
                Transform old = body.Find(oldName);
                if (old != null)
                {
                    Object.DestroyImmediate(old.gameObject);
                }
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, body);
            visual.name = VisualName;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            Animator animator = visual.GetComponent<Animator>();
            if (animator == null)
            {
                animator = visual.AddComponent<Animator>();
            }

            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Sem roupas para sortear: o AquaticAlien cuida do tamanho e da cor da pele.
            AlienAppearance appearance = root.GetComponent<AlienAppearance>();
            if (appearance != null)
            {
                Object.DestroyImmediate(appearance);
            }

            if (root.GetComponent<AquaticAlien>() == null)
            {
                root.AddComponent<AquaticAlien>();
            }

            AlienAnimator alienAnimator = root.GetComponent<AlienAnimator>();
            if (alienAnimator == null)
            {
                alienAnimator = root.AddComponent<AlienAnimator>();
            }

            SerializedObject data = new SerializedObject(alienAnimator);
            data.FindProperty("animator").objectReferenceValue = animator;
            data.ApplyModifiedPropertiesWithoutUndo();

            if (root.TryGetComponent(out AlienBrain brain) && brain.AlienRole != AlienBrain.Role.Civil)
            {
                problems.Add("O prefab de origem não é civil.");
            }

            PrefabUtility.SaveAsPrefabAsset(root, TargetPrefab);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
