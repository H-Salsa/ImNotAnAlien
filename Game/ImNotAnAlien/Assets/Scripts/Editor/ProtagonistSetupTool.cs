using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 15 — protagonista com modelo 3D e animações (Quaternius, CC0).
// Menu: ImNotAnAlien > Montar Protagonista (Missao 15)
// Rode com uma cena aberta que tenha o jogador (Prototype ou Regiao1_VilarejoDoBilu). A ferramenta:
// 1. Ajusta a importação dos FBX: Bake Axis Conversion, Rig Humanoid, clipes com "_Loop" em loop,
//    nomes dos clipes sem o prefixo "Armature|", mapas normais marcados como Normal Map.
// 2. Cria os materiais do protagonista (pele, olhos, cabelo) em Art/Materials/Personagens.
// 3. Cria o Animator Controller (andar/correr, queda, nado, derrota + braços da telecinese,
//    arremesso e dano) em Art/Characters/Protagonista.
// 4. Coloca o modelo dentro do jogador, com cabelo, esconde a cápsula e adiciona o PlayerAnimator.
// Pode rodar de novo quando quiser: ela refaz o modelo e o controller do zero.
public static class ProtagonistSetupTool
{
    private const string Root = "Assets/Art/Characters/Quaternius";
    private const string AnimationsFbx = Root + "/Animations/UAL1_Standard.fbx";
    private const string MaleFbx = Root + "/Base/Superhero_Male_FullBody.fbx";
    private const string FemaleFbx = Root + "/Base/Superhero_Female_FullBody.fbx";
    private const string HairFbx = Root + "/Hair/Hair_SimpleParted.fbx";
    private const string BaseTextures = Root + "/Base/Textures";
    private const string HairTextures = Root + "/Hair/Textures";

    private const string OutputFolder = "Assets/Art/Characters/Protagonista";
    private const string ControllerPath = OutputFolder + "/Protagonista.controller";
    private const string UpperBodyMaskPath = OutputFolder + "/Protagonista_Bracos.mask";
    private const string MaterialFolder = "Assets/Art/Materials/Personagens";
    private const string VisualName = "Visual_Protagonista";

    // Os mapas normais soltos na pasta Textures do pacote são no padrão DirectX (os do padrão
    // Unity ficavam em "Normals Unity - Godot"). Inverter o canal verde corrige isso.
    // Se o relevo da pele parecer "afundado" em vez de saltado, troque para false e rode de novo.
    private const bool FlipNormalGreen = true;

    // Velocidades (m/s) de cada animação no blend de locomoção. O jogador anda a 5 m/s.
    private const float WalkSpeed = 1.8f;
    private const float JogSpeed = 4.5f;
    private const float SprintSpeed = 8f;

    [MenuItem("ImNotAnAlien/Montar Protagonista (Missao 15)")]
    public static void Setup()
    {
        PrototypeThirdPersonController player = Object.FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Jogador não encontrado", "Abra a cena Prototype ou Regiao1_VilarejoDoBilu e rode de novo.", "OK");
            return;
        }

        int skin = EditorUtility.DisplayDialogComplex("Tom de pele do protagonista",
            "Qual textura de pele usar? (Dá para trocar depois rodando a ferramenta de novo.)",
            "Clara", "Cancelar", "Escura");
        if (skin == 1)
        {
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Missão 15", "Ajustando a importação dos modelos (o FBX das animações é grande)...", 0.2f);
            ConfigureImports();

            EditorUtility.DisplayProgressBar("Missão 15", "Conferindo o esqueleto humanoide...", 0.5f);
            if (!CheckAvatar(MaleFbx, out Avatar avatar) || !CheckAvatar(AnimationsFbx, out _))
            {
                return;
            }

            EditorUtility.DisplayProgressBar("Missão 15", "Criando materiais e Animator Controller...", 0.7f);
            Materials materials = CreateMaterials(darkSkin: skin == 2);
            AnimatorController controller = CreateController(out List<string> missingClips);

            EditorUtility.DisplayProgressBar("Missão 15", "Colocando o modelo no jogador...", 0.9f);
            bool hasHair = BuildVisual(player, avatar, controller, materials);

            AssetDatabase.SaveAssets();

            string message = "Protagonista montado!\n\n" +
                             (hasHair ? "" : "(Cabelo não encontrado; seguiu sem.)\n") +
                             (missingClips.Count > 0 ? $"Clipes não encontrados: {string.Join(", ", missingClips)}\n" : "") +
                             "\nSALVE A CENA com Ctrl+S e dê Play.\nRode também na outra cena (Prototype / Regiao1) se quiser o modelo nas duas.";
            Debug.Log(message.Replace("\n\n", "\n"));
            EditorUtility.DisplayDialog("Protagonista pronto", message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---------- 1. Importação ----------

    private static void ConfigureImports()
    {
        ConfigureModel(AnimationsFbx, humanoid: true, animations: true);
        ConfigureModel(MaleFbx, humanoid: true, animations: false);
        ConfigureModel(FemaleFbx, humanoid: true, animations: false);

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Hair" }))
        {
            ConfigureModel(AssetDatabase.GUIDToAssetPath(guid), humanoid: false, animations: false);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { BaseTextures, HairTextures }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path).EndsWith("_Normal"))
            {
                ConfigureNormalMap(path);
            }
        }
    }

    private static void ConfigureModel(string path, bool humanoid, bool animations)
    {
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
        {
            Debug.LogWarning($"Missão 15: não encontrei {path}");
            return;
        }

        bool dirty = false;
        if (!importer.bakeAxisConversion)
        {
            importer.bakeAxisConversion = true;
            dirty = true;
        }

        ModelImporterAnimationType type = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
        if (importer.animationType != type)
        {
            importer.animationType = type;
            dirty = true;
        }

        if (humanoid && importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            dirty = true;
        }

        if (importer.importAnimation != animations)
        {
            importer.importAnimation = animations;
            dirty = true;
        }

        if (animations && NeedsClipSetup(importer))
        {
            importer.clipAnimations = BuildClipSettings(importer.defaultClipAnimations);
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
        }
    }

    private static bool NeedsClipSetup(ModelImporter importer)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        return clips == null || clips.Length == 0 || clips.Any(c => c.name.Contains("|"));
    }

    // Nomes limpos, "_Loop" em loop e tudo "assado" na pose (o personagem anda pelo CharacterController,
    // não pela animação).
    private static ModelImporterClipAnimation[] BuildClipSettings(ModelImporterClipAnimation[] defaults)
    {
        foreach (ModelImporterClipAnimation clip in defaults)
        {
            string name = clip.takeName;
            int bar = name.LastIndexOf('|');
            clip.name = bar >= 0 ? name.Substring(bar + 1) : name;

            clip.loopTime = clip.name.EndsWith("_Loop") || clip.name == "Sword_Idle";
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = true;
        }

        return defaults;
    }

    private static void ConfigureNormalMap(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
        {
            return;
        }

        bool dirty = false;
        if (importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            dirty = true;
        }

        if (importer.flipGreenChannel != FlipNormalGreen)
        {
            importer.flipGreenChannel = FlipNormalGreen;
            dirty = true;
        }

        if (dirty)
        {
            importer.SaveAndReimport();
        }
    }

    private static bool CheckAvatar(string path, out Avatar avatar)
    {
        avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        if (avatar != null && avatar.isValid && avatar.isHuman)
        {
            return true;
        }

        string message = $"O esqueleto humanoide de\n{path}\nnão ficou válido.\n\n" +
                         "Selecione o arquivo no Project, aba Rig, clique em Configure... e veja o que ficou vermelho. " +
                         "Me mande um print que eu ajusto.";
        Debug.LogError("Missão 15: " + message);
        EditorUtility.DisplayDialog("Esqueleto com problema", message, "OK");
        return false;
    }

    // ---------- 2. Materiais ----------

    private struct Materials
    {
        public Material skin;
        public Material eyes;
        public Material hair;
    }

    private static Materials CreateMaterials(bool darkSkin)
    {
        Texture2D skinColor = darkSkin
            ? LoadTexture(BaseTextures, "T_Superhero_Male_Dark")
            : LoadTexture(BaseTextures, "T_Superhero_Male_Ligh") ?? LoadTexture(BaseTextures, "T_Superhero_Male_Light");

        Texture2D hairColor = LoadTexture(HairTextures, "T_Hair_1_BaseColor") ?? LoadTexture(BaseTextures, "T_Hair_1_BaseColor");
        Texture2D hairNormal = LoadTexture(HairTextures, "T_Hair_1_Normal") ?? LoadTexture(BaseTextures, "T_Hair_1_Normal");
        bool hairHasAlpha = hairColor != null &&
                            AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(hairColor)) is TextureImporter hairImporter &&
                            hairImporter.DoesSourceTextureHaveAlpha();

        return new Materials
        {
            skin = MakeMaterial("Protagonista_Pele", skinColor, LoadTexture(BaseTextures, "T_Superhero_Male_Normal"), 0.35f, false),
            eyes = MakeMaterial("Protagonista_Olhos", LoadTexture(BaseTextures, "T_Eye_Brown"), LoadTexture(BaseTextures, "T_Eye_Normal"), 0.8f, false),
            hair = MakeMaterial("Protagonista_Cabelo", hairColor, hairNormal, 0.3f, hairHasAlpha),
        };
    }

    private static Texture2D LoadTexture(string folder, string name)
    {
        return AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{name}.png");
    }

    private static Material MakeMaterial(string name, Texture2D baseMap, Texture2D normalMap, float smoothness, bool alphaClip)
    {
        EnsureFolder(MaterialFolder);
        string path = $"{MaterialFolder}/M_{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", Color.white);
        material.SetTexture("_BaseMap", baseMap);
        material.SetFloat("_Smoothness", smoothness);

        if (normalMap != null)
        {
            material.SetTexture("_BumpMap", normalMap);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }

        // Cabelo e sobrancelhas costumam ser "cartões" com transparência: recorta o fundo e mostra os dois lados.
        material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
        material.SetFloat("_Cutoff", 0.5f);
        material.SetFloat("_Cull", alphaClip ? 0f : 2f);
        if (alphaClip)
        {
            material.EnableKeyword("_ALPHATEST_ON");
        }
        else
        {
            material.DisableKeyword("_ALPHATEST_ON");
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- 3. Animator Controller ----------

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

        EnsureFolder(OutputFolder);
        AssetDatabase.DeleteAsset(ControllerPath);
        AssetDatabase.DeleteAsset(UpperBodyMaskPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
        controller.AddParameter("Swimming", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Holding", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Throw", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

        // ----- Camada base: corpo inteiro -----
        AnimatorStateMachine body = controller.layers[0].stateMachine;

        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomocao", out BlendTree locomotionTree, 0);
        locomotionTree.blendParameter = "Speed";
        locomotionTree.useAutomaticThresholds = false;
        locomotionTree.AddChild(Clip("Idle_Loop"), 0f);
        locomotionTree.AddChild(Clip("Walk_Loop"), WalkSpeed);
        locomotionTree.AddChild(Clip("Jog_Fwd_Loop"), JogSpeed);
        locomotionTree.AddChild(Clip("Sprint_Loop"), SprintSpeed);
        body.defaultState = locomotion;

        AnimatorState swim = controller.CreateBlendTreeInController("Nadando", out BlendTree swimTree, 0);
        swimTree.blendParameter = "Speed";
        swimTree.useAutomaticThresholds = false;
        swimTree.AddChild(Clip("Swim_Idle_Loop"), 0f);
        swimTree.AddChild(Clip("Swim_Fwd_Loop"), 2f);

        AnimatorState air = body.AddState("NoAr");
        air.motion = Clip("Jump_Loop");

        AnimatorState dead = body.AddState("Derrotado");
        dead.motion = Clip("Death01");

        Link(locomotion, air, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        Link(air, locomotion, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        Link(locomotion, swim, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Swimming");
        Link(air, swim, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, "Swimming");
        Link(swim, locomotion, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Swimming");
        Link(dead, locomotion, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

        AnimatorStateTransition toDead = body.AddAnyStateTransition(dead);
        toDead.duration = 0.15f;
        toDead.canTransitionToSelf = false;
        toDead.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

        // ----- Camada dos braços: telecinese, arremesso, dano -----
        AvatarMask mask = new AvatarMask();
        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            bool upper = part == AvatarMaskBodyPart.Head ||
                         part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                         part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                         part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, upper);
        }

        AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);

        controller.AddLayer("Bracos");
        AnimatorControllerLayer[] layers = controller.layers;
        layers[1].avatarMask = mask;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;

        AnimatorStateMachine arms = controller.layers[1].stateMachine;
        AnimatorState empty = arms.AddState("Vazio"); // sem animação: mostra a camada base
        arms.defaultState = empty;

        AnimatorState grabStart = arms.AddState("TK_Inicio");
        grabStart.motion = Clip("Spell_Simple_Enter");
        AnimatorState grabHold = arms.AddState("TK_Segurando");
        grabHold.motion = Clip("Spell_Simple_Idle_Loop");
        AnimatorState grabEnd = arms.AddState("TK_Fim");
        grabEnd.motion = Clip("Spell_Simple_Exit");
        AnimatorState cast = arms.AddState("Arremesso");
        cast.motion = Clip("Spell_Simple_Shoot");
        AnimatorState hit = arms.AddState("Dano");
        hit.motion = Clip("Hit_Chest");

        AnimatorStateTransition grab = Link(empty, grabStart, 0.1f);
        grab.AddCondition(AnimatorConditionMode.If, 0f, "Holding");
        grab.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
        Link(grabStart, grabHold, 0.1f, exitTime: 0.9f);
        Link(grabStart, grabEnd, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Holding");
        Link(grabHold, grabEnd, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Holding");
        Link(grabEnd, grabStart, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Holding");
        Link(grabEnd, empty, 0.2f, exitTime: 0.85f);
        Link(cast, grabStart, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Holding");
        Link(cast, empty, 0.25f, exitTime: 0.8f);
        Link(hit, empty, 0.2f, exitTime: 0.8f);

        AnimatorStateTransition silence = arms.AddAnyStateTransition(empty);
        silence.duration = 0.1f;
        silence.canTransitionToSelf = false;
        silence.AddCondition(AnimatorConditionMode.If, 0f, "Dead");

        AnimatorStateTransition toCast = arms.AddAnyStateTransition(cast);
        toCast.duration = 0.05f;
        toCast.AddCondition(AnimatorConditionMode.If, 0f, "Throw");
        toCast.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

        AnimatorStateTransition toHit = arms.AddAnyStateTransition(hit);
        toHit.duration = 0.05f;
        toHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
        toHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.duration = duration;
        transition.hasExitTime = exitTime >= 0f;
        if (exitTime >= 0f)
        {
            transition.exitTime = exitTime;
        }

        return transition;
    }

    // ---------- 4. Modelo no jogador ----------

    private static bool BuildVisual(PrototypeThirdPersonController player, Avatar avatar, AnimatorController controller, Materials materials)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MaleFbx);
        Transform playerTransform = player.transform;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Montar Protagonista");

        Transform old = playerTransform.Find(VisualName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, playerTransform);
        Undo.RegisterCreatedObjectUndo(visual, "Modelo do protagonista");
        visual.name = VisualName;

        // Pés na base do CharacterController (altura 2, centro no meio, mais a "pele" de contato).
        CharacterController characterController = player.GetComponent<CharacterController>();
        float skinWidth = characterController != null ? characterController.skinWidth : 0.08f;
        visual.transform.localPosition = new Vector3(0f, -1f - skinWidth, 0f);
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

        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            ApplyMaterials(renderer, materials);
        }

        bool hasHair = AddHair(visual, materials.hair);

        // Esconde a cápsula (o colisor de verdade é o CharacterController).
        if (player.TryGetComponent(out MeshRenderer capsule))
        {
            Undo.RecordObject(capsule, "Esconder cápsula");
            capsule.enabled = false;
        }

        PlayerAnimator playerAnimator = player.GetComponent<PlayerAnimator>();
        if (playerAnimator == null)
        {
            playerAnimator = Undo.AddComponent<PlayerAnimator>(player.gameObject);
        }

        SerializedObject data = new SerializedObject(playerAnimator);
        data.FindProperty("animator").objectReferenceValue = animator;
        data.ApplyModifiedProperties();

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Selection.activeGameObject = player.gameObject;
        return hasHair;
    }

    // Troca os materiais do FBX pelos nossos, pelo nome (MI_Superhero_Male, MI_Eyes, MI_Hair_1).
    private static void ApplyMaterials(Renderer renderer, Materials materials)
    {
        Material[] current = renderer.sharedMaterials;
        Material[] replaced = new Material[current.Length];
        for (int i = 0; i < current.Length; i++)
        {
            string name = current[i] != null ? current[i].name : "";
            replaced[i] = name.Contains("Eye") ? materials.eyes
                : name.Contains("Hair") ? materials.hair
                : materials.skin;
        }

        renderer.sharedMaterials = replaced;
    }

    // O cabelo vem num FBX separado com o MESMO esqueleto: pega a malha e liga nos ossos do corpo.
    private static bool AddHair(GameObject visual, Material hairMaterial)
    {
        GameObject hairAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HairFbx);
        if (hairAsset == null)
        {
            return false;
        }

        Dictionary<string, Transform> bones = visual.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name)
            .ToDictionary(g => g.Key, g => g.First());

        GameObject temp = Object.Instantiate(hairAsset);
        bool added = false;
        try
        {
            foreach (SkinnedMeshRenderer hair in temp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (hair.transform == temp.transform)
                {
                    continue;
                }

                hair.transform.SetParent(visual.transform, false);
                hair.gameObject.name = "Cabelo";
                hair.bones = hair.bones
                    .Select(b => b != null && bones.TryGetValue(b.name, out Transform bone) ? bone : null)
                    .ToArray();
                if (hair.rootBone != null && bones.TryGetValue(hair.rootBone.name, out Transform root))
                {
                    hair.rootBone = root;
                }

                hair.sharedMaterial = hairMaterial;
                added = true;
            }
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }

        return added;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
