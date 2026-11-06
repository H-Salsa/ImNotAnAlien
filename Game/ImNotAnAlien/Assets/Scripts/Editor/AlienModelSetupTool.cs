using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Ferramenta de editor da Missão 15 — alienígenas com o modelo 3D feito no Blender.
// Menu: ImNotAnAlien > Montar Aliens 3D (Missao 15)
// Pode rodar com qualquer cena aberta. A ferramenta:
// 1. Ajusta a importação de Art/Characters/Aliens/Alien_Civil.fbx (Humanoid, mesmo esqueleto do
//    protagonista) e troca os materiais do FBX por materiais URP.
// 2. Cria o Animator Controller dos aliens (andar/correr, voando, derrota, pose de pistola).
// 3. Coloca o modelo dentro dos prefabs Alienigena e Alienigena_Policial (em "Corpo/Visual_Alien")
//    e adiciona o AlienAnimator. As peças de cápsula ficam escondidas, não apagadas
//    (os colisores delas continuam sendo usados).
// Pode rodar de novo depois de exportar uma versão nova do Blender.
public static class AlienModelSetupTool
{
    private const string ModelPath = "Assets/Art/Characters/Aliens/Alien_Civil.fbx";
    private const string AnimationsFbx = "Assets/Art/Characters/Quaternius/Animations/UAL1_Standard.fbx";
    private const string ControllerPath = "Assets/Art/Characters/Aliens/Alien.controller";
    private const string ArmsMaskPath = "Assets/Art/Characters/Aliens/Alien_Bracos.mask";
    private const string MaterialFolder = "Assets/Art/Materials/Aliens";
    private const string VisualName = "Visual_Alien";

    private static readonly string[] Prefabs =
    {
        "Assets/Prefabs/Alienigena.prefab",
        "Assets/Prefabs/Alienigena_Policial.prefab",
    };

    // Nome do material no Blender -> (material no Unity, cor, suavidade)
    private static readonly (string blender, string unity, string hex, float smooth)[] MaterialMap =
    {
        ("Alien_Pele", "M_Alien3D_Pele", "3FA59B", 0.35f),
        ("Alien_Olho_Branco", "M_Alien3D_Olho_Branco", "F2EEE3", 0.7f),
        ("Alien_Olho_Iris", "M_Alien3D_Olho_Iris", "E8922B", 0.7f),
        ("Alien_Olho_Pupila", "M_Alien3D_Olho_Pupila", "141018", 0.8f),
        ("Alien_Boca", "M_Alien3D_Boca", "16262A", 0.3f),
    };

    [MenuItem("ImNotAnAlien/Montar Aliens 3D (Missao 15)")]
    public static void Setup()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Modelo não encontrado", $"Não achei {ModelPath}.\nExporte o alien do Blender primeiro.", "OK");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Missão 15", "Importando o modelo do alien...", 0.2f);
            ConfigureImport(importer);

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                EditorUtility.DisplayDialog("Esqueleto com problema",
                    "O esqueleto humanoide do Alien_Civil.fbx não ficou válido.\nSelecione o arquivo, aba Rig > Configure... e me mande um print.", "OK");
                return;
            }

            EditorUtility.DisplayProgressBar("Missão 15", "Criando as animações...", 0.5f);
            AnimatorController controller = CreateController(out List<string> missing);

            EditorUtility.DisplayProgressBar("Missão 15", "Colocando o modelo nos prefabs...", 0.8f);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            int done = 0;
            foreach (string path in Prefabs)
            {
                if (AddModelToPrefab(path, model, avatar, controller))
                {
                    done++;
                }
            }

            AssetDatabase.SaveAssets();
            string message = $"Modelo 3D colocado em {done} prefab(s) de alienígena." +
                             (missing.Count > 0 ? $"\nClipes não encontrados: {string.Join(", ", missing)}" : "") +
                             "\n\nDê Play na Regiao1_VilarejoDoBilu para testar.";
            Debug.Log(message);
            EditorUtility.DisplayDialog("Aliens 3D prontos", message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---------- Importação ----------

    private static void ConfigureImport(ModelImporter importer)
    {
        importer.bakeAxisConversion = true;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

        foreach (var (blender, unity, hex, smooth) in MaterialMap)
        {
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), blender), MakeMaterial(unity, hex, smooth));
        }

        importer.SaveAndReimport();
    }

    private static Material MakeMaterial(string name, string hex, float smoothness)
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            AssetDatabase.CreateFolder("Assets/Art/Materials", "Aliens");
        }

        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        material.color = color;
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- Animações ----------

    private static AnimatorController CreateController(out List<string> missing)
    {
        missing = new List<string>();
        List<string> missingLocal = missing;
        Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(AnimationsFbx)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .GroupBy(c => c.name)
            .ToDictionary(g => g.Key, g => g.First());

        AnimationClip Clip(string name)
        {
            if (clips.TryGetValue(name, out AnimationClip clip))
            {
                return clip;
            }

            missingLocal.Add(name);
            return null;
        }

        AssetDatabase.DeleteAsset(ControllerPath);
        AssetDatabase.DeleteAsset(ArmsMaskPath);
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Tumbling", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Aiming", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Held", AnimatorControllerParameterType.Bool);

        // Corpo inteiro
        AnimatorStateMachine body = controller.layers[0].stateMachine;
        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomocao", out BlendTree tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(Clip("Idle_Loop"), 0f);
        tree.AddChild(Clip("Walk_Loop"), 1.6f);
        tree.AddChild(Clip("Jog_Fwd_Loop"), 4.2f);
        tree.AddChild(Clip("Sprint_Loop"), 6.5f);
        body.defaultState = locomotion;

        AnimatorState tumble = body.AddState("Voando");
        tumble.motion = Clip("Jump_Loop");
        AnimatorState dead = body.AddState("Derrotado");
        dead.motion = Clip("Death01");

        // Segurado pela telecinese: pose de queda (o AlienAnimator mexe braços e pernas por cima,
        // como quem procura um chão firme).
        AnimatorState held = body.AddState("Segurado");
        held.motion = Clip("Jump_Loop");
        held.speed = 0.7f;

        Link(locomotion, held, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Held");
        Link(tumble, held, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Held");
        Link(held, locomotion, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Tumbling");
        AnimatorStateTransition heldToTumble = Link(held, tumble, 0.2f);
        heldToTumble.AddCondition(AnimatorConditionMode.IfNot, 0f, "Held");
        heldToTumble.AddCondition(AnimatorConditionMode.If, 0f, "Tumbling");
        AnimatorStateTransition toTumble = Link(locomotion, tumble, 0.1f);
        toTumble.AddCondition(AnimatorConditionMode.If, 0f, "Tumbling");
        toTumble.AddCondition(AnimatorConditionMode.IfNot, 0f, "Held");
        Link(tumble, locomotion, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Tumbling");
        AnimatorStateTransition toDead = body.AddAnyStateTransition(dead);
        toDead.duration = 0.15f;
        toDead.canTransitionToSelf = false;
        toDead.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

        // Braços: pose de pistola do policial
        AvatarMask mask = new AvatarMask();
        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            mask.SetHumanoidBodyPartActive(part,
                part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK ||
                part == AvatarMaskBodyPart.Head);
        }

        AssetDatabase.CreateAsset(mask, ArmsMaskPath);
        controller.AddLayer("Bracos");
        AnimatorControllerLayer[] layers = controller.layers;
        layers[1].avatarMask = mask;
        layers[1].defaultWeight = 1f;
        controller.layers = layers;

        AnimatorStateMachine arms = controller.layers[1].stateMachine;
        AnimatorState empty = arms.AddState("Vazio");
        arms.defaultState = empty;
        AnimatorState aim = arms.AddState("Mirando");
        aim.motion = Clip("Pistol_Idle_Loop");
        AnimatorStateTransition toAim = Link(empty, aim, 0.2f);
        toAim.AddCondition(AnimatorConditionMode.If, 0f, "Aiming");
        toAim.AddCondition(AnimatorConditionMode.IfNot, 0f, "Tumbling");
        Link(aim, empty, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");
        Link(aim, empty, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Tumbling");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.duration = duration;
        transition.hasExitTime = false;
        return transition;
    }

    // ---------- Prefabs ----------

    private static bool AddModelToPrefab(string path, GameObject model, Avatar avatar, AnimatorController controller)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"Missão 15: prefab não encontrado: {path}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Transform body = root.transform.Find("Corpo");
            if (body == null)
            {
                Debug.LogWarning($"Missão 15: {path} não tem o filho 'Corpo'.");
                return false;
            }

            Transform old = body.Find(VisualName);
            if (old != null)
            {
                Object.DestroyImmediate(old.gameObject);
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

            AlienAnimator alienAnimator = root.GetComponent<AlienAnimator>();
            if (alienAnimator == null)
            {
                alienAnimator = root.AddComponent<AlienAnimator>();
            }

            SerializedObject data = new SerializedObject(alienAnimator);
            data.FindProperty("animator").objectReferenceValue = animator;
            data.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
