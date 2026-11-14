using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Missão 20 — aliens nadando.
// Menu: ImNotAnAlien > Preparar Nado dos Aliens (Missao 20)
// Acrescenta ao Alien.controller (sem refazer o resto) o parâmetro "Swimming" e o estado "Nadando"
// (Swim_Idle_Loop parado, Swim_Fwd_Loop andando; mesmas animações do protagonista). O AlienAnimator
// liga o parâmetro quando o alien está em água funda e sobe o modelo até a linha da água.
// Pode rodar de novo à vontade. "Preparar Golpes da Policia" (e Montar Aliens 3D) também chamam esta.
public static class AlienSwimSetupTool
{
    private const string AnimationsFbx = "Assets/Art/Characters/Quaternius/Animations/UAL1_Standard.fbx";
    private const string ControllerPath = "Assets/Art/Characters/Aliens/Alien.controller";
    private const string StateName = "Nadando";

    [MenuItem("ImNotAnAlien/Preparar Nado dos Aliens (Missao 20)")]
    public static void Run()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Nado dos Aliens", $"Não achei {ControllerPath}. Rode antes ImNotAnAlien > Montar Aliens 3D.", "OK");
            return;
        }

        Dictionary<string, AnimationClip> clips = AssetDatabase.LoadAllAssetsAtPath(AnimationsFbx)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .GroupBy(c => c.name)
            .ToDictionary(g => g.Key, g => g.First());
        List<string> missing = new List<string>();
        AnimationClip Clip(string name)
        {
            if (clips.TryGetValue(name, out AnimationClip clip)) return clip;
            missing.Add(name);
            return null;
        }

        bool ok = AddSwim(controller, Clip);
        AssetDatabase.SaveAssets();
        string message = !ok ? "Não achei o estado 'Locomocao' no Alien.controller."
            : missing.Count == 0 ? "Pronto! Estado \"Nadando\" criado no Alien.controller."
            : "Criado, mas faltaram estas animações: " + string.Join(", ", missing);
        Debug.Log("Nado dos Aliens: " + message);
        EditorUtility.DisplayDialog("Nado dos Aliens", message, "OK");
    }

    public static bool AddSwim(AnimatorController controller, System.Func<string, AnimationClip> clip)
    {
        if (!controller.parameters.Any(p => p.name == "Swimming"))
        {
            controller.AddParameter("Swimming", AnimatorControllerParameterType.Bool);
        }

        AnimatorStateMachine body = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in body.states.ToArray())
        {
            if (child.state.name == StateName)
            {
                body.RemoveState(child.state);
            }
        }

        ProtagonistJumpSetup.CleanTransitions(body);
        AnimatorState locomotion = body.states.Select(c => c.state).FirstOrDefault(s => s.name == "Locomocao");
        if (locomotion == null)
        {
            return false;
        }

        AnimatorState swim = controller.CreateBlendTreeInController(StateName, out BlendTree tree, 0);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(clip("Swim_Idle_Loop"), 0f);
        tree.AddChild(clip("Swim_Fwd_Loop"), 2f);

        Link(locomotion, swim, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Swimming");
        Link(swim, locomotion, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0f, "Swimming");
        AnimatorState tumble = body.states.Select(c => c.state).FirstOrDefault(s => s.name == "Voando");
        if (tumble != null && controller.parameters.Any(p => p.name == "Tumbling"))
        {
            Link(swim, tumble, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, "Tumbling");
        }

        AnimatorState held = body.states.Select(c => c.state).FirstOrDefault(s => s.name == "Segurado");
        if (held != null && controller.parameters.Any(p => p.name == "Held"))
        {
            Link(swim, held, 0.3f).AddCondition(AnimatorConditionMode.If, 0f, "Held");
        }

        EditorUtility.SetDirty(controller);
        return true;
    }

    private static AnimatorStateTransition Link(AnimatorState from, AnimatorState to, float duration)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = duration;
        return t;
    }
}
