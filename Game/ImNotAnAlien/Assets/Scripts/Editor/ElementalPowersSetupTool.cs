using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Missão 21 — PODER ELEMENTAL (fogo, gelo e eletricidade no botão esquerdo).
// Menu: ImNotAnAlien > Preparar Poderes Elementais (Missao 21)
// O poder em si já funciona sem rodar nada (o ElementalPower entra sozinho no jogador ao dar Play).
// Esta ferramenta deixa a ARTE no ponto:
// 1. Texturas e ícones (Assets/Resources/Elemental): transparência certa, sem repetir nas bordas.
// 2. Animações do braço: importa Assets/Art/Characters/Protagonista/Protagonista_Poderes.fbx
//    (feito no Blender por Art/Blender/powers_anim_builder.py) e acrescenta ao Protagonista.controller,
//    na camada dos braços, os estados de CARREGAR e DISPARAR de cada elemento.
// 3. Cria o arquivo de ajustes Assets/Resources/Elemental/ElementalTuning.asset (dano, energia, alcance...).
// Pode rodar de novo quando quiser. A ferramenta "Montar Protagonista" também chama o passo 2.
public static class ElementalPowersSetupTool
{
    private const string PowersFbx = "Assets/Art/Characters/Protagonista/Protagonista_Poderes.fbx";
    private const string H19Fbx = "Assets/Art/Characters/Protagonista/Protagonista_H19.fbx";
    private const string MaleFbx = "Assets/Art/Characters/Quaternius/Base/Superhero_Male_FullBody.fbx";
    private const string ControllerPath = "Assets/Art/Characters/Protagonista/Protagonista.controller";
    private const string ArtFolder = "Assets/Resources/Elemental";
    private const string TuningPath = ArtFolder + "/ElementalTuning.asset";

    // (nome do estado, clipe de carregar, clipe de disparar) na ordem do ElementKind: fogo, gelo, raio.
    private static readonly (string label, string charge, string cast)[] Elements =
    {
        ("Fogo", "El_Fogo_Carga_Loop", "El_Fogo_Disparo"),
        ("Gelo", "El_Gelo_Carga_Loop", "El_Gelo_Disparo"),
        ("Raio", "El_Raio_Carga_Loop", "El_Raio_Disparo"),
    };

    [MenuItem("ImNotAnAlien/Preparar Poderes Elementais (Missao 21)")]
    public static void Run()
    {
        List<string> problems = new List<string>();
        try
        {
            EditorUtility.DisplayProgressBar("Poderes elementais", "Ajustando texturas e ícones...", 0.15f);
            int textures = SetupTextures();

            EditorUtility.DisplayProgressBar("Poderes elementais", "Importando as animações...", 0.45f);
            ImportAnimations(problems);

            EditorUtility.DisplayProgressBar("Poderes elementais", "Ligando as animações ao protagonista...", 0.75f);
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            bool animated = false;
            if (controller == null)
            {
                problems.Add($"Não achei {ControllerPath}. Rode antes 'Montar Protagonista (Missao 15)'.");
            }
            else
            {
                animated = AddToController(controller, problems);
            }

            bool createdTuning = EnsureTuning();
            AssetDatabase.SaveAssets();

            string message =
                $"Texturas e ícones: {textures} ajustados.\n" +
                (animated ? "Animações de carregar e disparar: prontas (fogo, gelo e raio).\n" : "Animações: NÃO ligadas (veja os avisos).\n") +
                (createdTuning ? $"Ajustes criados em {TuningPath}.\n" : $"Ajustes: {TuningPath} (mantido).\n") +
                "\nDê Play: botão esquerdo dispara (segure para carregar), Q ou a roda do mouse troca o elemento." +
                (problems.Count > 0 ? "\n\nAvisos:\n- " + string.Join("\n- ", problems) : "");
            Debug.Log("Poderes elementais: " + message);
            EditorUtility.DisplayDialog("Poderes elementais", message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---------- 1. Texturas ----------

    private static int SetupTextures()
    {
        if (!AssetDatabase.IsValidFolder(ArtFolder))
        {
            return 0;
        }

        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                continue;
            }

            bool icon = System.IO.Path.GetFileName(path).StartsWith("icon_");
            bool changed = false;
            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            if (importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.wrapMode = TextureWrapMode.Clamp;
                changed = true;
            }

            if (importer.mipmapEnabled == icon)
            {
                importer.mipmapEnabled = !icon;
                changed = true;
            }

            TextureImporterCompression compression = icon ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            if (importer.textureCompression != compression)
            {
                importer.textureCompression = compression;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }

            count++;
        }

        return count;
    }

    // ---------- 2. Animações ----------

    private static void ImportAnimations(List<string> problems)
    {
        if (!(AssetImporter.GetAtPath(PowersFbx) is ModelImporter importer))
        {
            problems.Add($"Não achei {PowersFbx} (as animações dos poderes). O disparo usa o gesto antigo.");
            return;
        }

        // Mesmo esqueleto do protagonista: copia o Avatar dele (se não houver, cria um do próprio arquivo).
        Avatar avatar = LoadAvatar(H19Fbx) ?? LoadAvatar(MaleFbx);
        ConfigureModel(importer, avatar);
        if (LoadClips().Count < Elements.Length * 2 && avatar != null)
        {
            ConfigureModel(AssetImporter.GetAtPath(PowersFbx) as ModelImporter, null);
        }

        Dictionary<string, AnimationClip> clips = LoadClips();
        foreach (var (_, charge, cast) in Elements)
        {
            foreach (string name in new[] { charge, cast })
            {
                if (!clips.ContainsKey(name))
                {
                    problems.Add($"Clipe {name} não encontrado em {PowersFbx}.");
                }
            }
        }
    }

    private static Avatar LoadAvatar(string path)
    {
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        return avatar != null && avatar.isValid && avatar.isHuman ? avatar : null;
    }

    private static void ConfigureModel(ModelImporter importer, Avatar avatar)
    {
        if (importer == null)
        {
            return;
        }

        importer.bakeAxisConversion = true;
        importer.animationType = ModelImporterAnimationType.Human;
        if (avatar != null)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
        }
        else
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }

        importer.importAnimation = true;
        importer.importBlendShapes = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport(); // para os "takes" do arquivo aparecerem

        // Depois de reimportar, pega o importador de novo (o antigo pode estar desatualizado).
        importer = AssetImporter.GetAtPath(PowersFbx) as ModelImporter;
        if (importer == null)
        {
            return;
        }

        // Os takes vêm como "Armature|El_Fogo_Disparo": o clipe fica só com o fim do nome.
        List<ModelImporterClipAnimation> list = new List<ModelImporterClipAnimation>();
        foreach (TakeInfo take in importer.importedTakeInfos)
        {
            string name = ShortName(take.name);
            if (!IsPowerClip(name) || list.Any(c => c.name == name))
            {
                continue;
            }

            float rate = take.sampleRate > 0f ? take.sampleRate : 24f;
            list.Add(ClipSettings(new ModelImporterClipAnimation
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
                string name = ShortName(string.IsNullOrEmpty(clip.takeName) ? clip.name : clip.takeName);
                if (IsPowerClip(name) && !list.Any(c => c.name == name))
                {
                    clip.name = name;
                    list.Add(ClipSettings(clip));
                }
            }
        }

        if (list.Count > 0)
        {
            importer.clipAnimations = list.ToArray();
            importer.SaveAndReimport();
        }
    }

    private static bool IsPowerClip(string name) => Elements.Any(e => e.charge == name || e.cast == name);

    private static string ShortName(string name) =>
        name.Contains("|") ? name.Substring(name.LastIndexOf('|') + 1) : name;

    // "_Loop" em loop; tudo "assado" na pose (quem move o personagem é o CharacterController).
    private static ModelImporterClipAnimation ClipSettings(ModelImporterClipAnimation clip)
    {
        clip.loopTime = clip.name.EndsWith("_Loop");
        clip.loopPose = false;
        clip.lockRootRotation = true;
        clip.keepOriginalOrientation = true;
        clip.lockRootHeightY = true;
        clip.keepOriginalPositionY = true;
        clip.lockRootPositionXZ = true;
        clip.keepOriginalPositionXZ = true;
        return clip;
    }

    private static Dictionary<string, AnimationClip> LoadClips()
    {
        Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(PowersFbx).OfType<AnimationClip>())
        {
            if (!clip.name.StartsWith("__preview__"))
            {
                clips[ShortName(clip.name)] = clip;
            }
        }

        return clips;
    }

    // Acrescenta (ou refaz) os estados dos poderes na camada dos braços. Usada também pelo "Montar Protagonista".
    public static bool AddToController(AnimatorController controller, List<string> problems = null)
    {
        void Problem(string text)
        {
            if (problems != null)
            {
                problems.Add(text);
            }
            else
            {
                Debug.LogWarning("Poderes elementais: " + text);
            }
        }

        // Arquivo ainda não preparado (ex.: "Montar Protagonista" rodou antes desta ferramenta): prepara agora.
        if (AssetImporter.GetAtPath(PowersFbx) is ModelImporter powers &&
            (powers.animationType != ModelImporterAnimationType.Human || powers.clipAnimations == null || powers.clipAnimations.Length == 0))
        {
            ImportAnimations(new List<string>());
        }

        Dictionary<string, AnimationClip> clips = LoadClips();
        if (Elements.Any(e => !clips.ContainsKey(e.charge) || !clips.ContainsKey(e.cast)))
        {
            Problem($"faltam clipes dos poderes em {PowersFbx}; rode 'Preparar Poderes Elementais (Missao 21)'.");
            return false;
        }

        if (controller.layers.Length < 2)
        {
            Problem("o Protagonista.controller não tem a camada dos braços. Rode 'Montar Protagonista (Missao 15)'.");
            return false;
        }

        AddParameter(controller, "ElCharging", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "ElCast", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Element", AnimatorControllerParameterType.Int);
        bool hasDead = controller.parameters.Any(p => p.name == "Dead");
        bool hasHolding = controller.parameters.Any(p => p.name == "Holding");

        AnimatorStateMachine arms = controller.layers[1].stateMachine;
        foreach (ChildAnimatorState child in arms.states.ToArray())
        {
            if (child.state.name.StartsWith("El_"))
            {
                arms.RemoveState(child.state);
            }
        }

        ProtagonistJumpSetup.CleanTransitions(arms);
        AnimatorState empty = arms.states.Select(c => c.state).FirstOrDefault(s => s.name == "Vazio") ?? arms.defaultState;
        AnimatorState grab = arms.states.Select(c => c.state).FirstOrDefault(s => s.name == "TK_Inicio");
        if (empty == null)
        {
            Problem("a camada dos braços não tem o estado 'Vazio'.");
            return false;
        }

        for (int i = 0; i < Elements.Length; i++)
        {
            var (label, chargeClip, castClip) = Elements[i];
            AnimatorState charge = arms.AddState("El_Carga_" + label);
            charge.motion = clips[chargeClip];
            AnimatorState cast = arms.AddState("El_Disparo_" + label);
            cast.motion = clips[castClip];
            cast.speed = 1.2f;

            // Qualquer estado --(carregando este elemento)--> carga.
            AnimatorStateTransition toCharge = arms.AddAnyStateTransition(charge);
            toCharge.duration = 0.12f;
            toCharge.hasExitTime = false;
            toCharge.canTransitionToSelf = false;
            toCharge.AddCondition(AnimatorConditionMode.If, 0f, "ElCharging");
            toCharge.AddCondition(AnimatorConditionMode.Equals, i, "Element");
            if (hasDead)
            {
                toCharge.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            }

            // Qualquer estado --(disparou este elemento)--> disparo. Cliques seguidos recomeçam o gesto.
            AnimatorStateTransition toCast = arms.AddAnyStateTransition(cast);
            toCast.duration = 0.05f;
            toCast.hasExitTime = false;
            toCast.canTransitionToSelf = true;
            toCast.AddCondition(AnimatorConditionMode.If, 0f, "ElCast");
            toCast.AddCondition(AnimatorConditionMode.Equals, i, "Element");
            if (hasDead)
            {
                toCast.AddCondition(AnimatorConditionMode.IfNot, 0f, "Dead");
            }

            // Soltou sem disparar (faltou energia, trocou de elemento): volta ao normal.
            AnimatorStateTransition cancel = charge.AddTransition(empty);
            cancel.duration = 0.15f;
            cancel.hasExitTime = false;
            cancel.AddCondition(AnimatorConditionMode.IfNot, 0f, "ElCharging");

            // Fim do disparo: volta para a telecinese (se estiver segurando algo) ou ao normal.
            if (grab != null && hasHolding)
            {
                AnimatorStateTransition toGrab = cast.AddTransition(grab);
                toGrab.duration = 0.12f;
                toGrab.hasExitTime = true;
                toGrab.exitTime = 0.6f;
                toGrab.AddCondition(AnimatorConditionMode.If, 0f, "Holding");
            }

            AnimatorStateTransition finish = cast.AddTransition(empty);
            finish.duration = 0.2f;
            finish.hasExitTime = true;
            finish.exitTime = 0.8f;
        }

        EditorUtility.SetDirty(controller);
        return true;
    }

    private static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (!controller.parameters.Any(p => p.name == name))
        {
            controller.AddParameter(name, type);
        }
    }

    // ---------- 3. Ajustes ----------

    private static bool EnsureTuning()
    {
        if (AssetDatabase.LoadAssetAtPath<ElementalTuning>(TuningPath) != null)
        {
            return false;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder(ArtFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Elemental");
        }

        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<ElementalTuning>(), TuningPath);
        return true;
    }
}
