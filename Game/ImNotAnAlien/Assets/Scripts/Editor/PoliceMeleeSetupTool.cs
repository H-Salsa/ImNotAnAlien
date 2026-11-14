using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Ferramenta de editor da Missão 19 — golpes do policial no nível 1 (soco e cassetete).
// Menu: ImNotAnAlien > Preparar Golpes da Policia (Missao 19)
// Acrescenta ao Alien.controller (sem refazer o resto):
// - gatilhos "Soco", "Jab" e "Cassetete";
// - camada "Golpes" (tronco, braços e cabeça; as pernas continuam andando) com as animações
//   Punch_Cross, Punch_Jab e Sword_Attack da biblioteca da Quaternius.
// Pode rodar de novo à vontade: a camada é refeita. A ferramenta "Montar Aliens 3D" também chama esta.
// Também (Missão 19, animações extras):
// - na camada "Golpes": pancada na cabeça e no peito (Hit_Head / Hit_Chest), tiro e recarga da pistola;
// - na camada base: atividades dos civis — conversa, dança e sentar no banco (parâmetro "Atividade").
public static class PoliceMeleeSetupTool
{
    private const string AnimationsFbx = "Assets/Art/Characters/Quaternius/Animations/UAL1_Standard.fbx";
    private const string ControllerPath = "Assets/Art/Characters/Aliens/Alien.controller";
    private const string MaskPath = "Assets/Art/Characters/Aliens/Alien_Golpes.mask";
    private const string LayerName = "Golpes";
    private static readonly string[] Triggers = { "Soco", "Jab", "Cassetete", "PancadaCabeca", "PancadaPeito", "Tiro", "Recarga" };
    private static readonly string[] ActivityStates = { "Conversa", "Danca", "Sentando", "Sentado", "Levantando" };

    [MenuItem("ImNotAnAlien/Preparar Animacoes Extras (Missao 19)")]
    public static void SetupExtras()
    {
        Setup();
        ProtagonistJumpSetup.Run();
    }

    [MenuItem("ImNotAnAlien/Preparar Golpes da Policia (Missao 19)")]
    public static void Setup()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            EditorUtility.DisplayDialog("Golpes da Polícia",
                $"Não achei {ControllerPath}. Rode antes ImNotAnAlien > Montar Aliens 3D (Missao 15).", "OK");
            return;
        }

        List<string> missing = AddMeleeLayer(controller);
        AssetDatabase.SaveAssets();
        string message = missing.Count == 0
            ? "Pronto! Camada \"Golpes\" com soco, jab e cassetete criada no Alien.controller."
            : "Camada criada, mas faltaram estas animações no UAL1_Standard: " + string.Join(", ", missing);
        Debug.Log("Golpes da Polícia: " + message);
        EditorUtility.DisplayDialog("Golpes da Polícia", message, "OK");
    }

    // Devolve os nomes das animações que não foram encontradas.
    public static List<string> AddMeleeLayer(AnimatorController controller)
    {
        List<string> missing = new List<string>();
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

            missing.Add(name);
            return null;
        }

        // Refaz do zero: tira a camada e os gatilhos antigos.
        AnimatorControllerLayer[] layers = controller.layers;
        for (int i = layers.Length - 1; i >= 0; i--)
        {
            if (layers[i].name == LayerName)
            {
                controller.RemoveLayer(i);
            }
        }

        foreach (string trigger in Triggers)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == trigger)
                {
                    controller.RemoveParameter(parameter);
                    break;
                }
            }

            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        }

        if (!controller.parameters.Any(p => p.name == "Tumbling"))
        {
            controller.AddParameter("Tumbling", AnimatorControllerParameterType.Bool);
        }

        // Máscara: tronco, braços, mãos e cabeça. As pernas seguem a animação de andar.
        AssetDatabase.DeleteAsset(MaskPath);
        AvatarMask mask = new AvatarMask();
        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            mask.SetHumanoidBodyPartActive(part,
                part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
        }

        AssetDatabase.CreateAsset(mask, MaskPath);

        controller.AddLayer(LayerName);
        layers = controller.layers;
        int index = layers.Length - 1;
        layers[index].avatarMask = mask;
        layers[index].defaultWeight = 1f;
        layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;

        AnimatorStateMachine machine = controller.layers[index].stateMachine;
        AnimatorState empty = machine.AddState("Vazio");
        machine.defaultState = empty;
        AddStrike(machine, empty, "Soco", Clip("Punch_Cross"), 1.15f);
        AddStrike(machine, empty, "Jab", Clip("Punch_Jab"), 1.15f);
        AddStrike(machine, empty, "Cassetete", Clip("Sword_Attack"), 1.1f);
        AddStrike(machine, empty, "PancadaCabeca", Clip("Hit_Head"), 1.2f);
        AddStrike(machine, empty, "PancadaPeito", Clip("Hit_Chest"), 1.2f);
        AddPistolStates(controller, Clip("Pistol_Shoot"), Clip("Pistol_Reload"));

        AddActivities(controller, Clip);
        AlienSwimSetupTool.AddSwim(controller, Clip); // Missão 20: nadando

        EditorUtility.SetDirty(controller);
        return missing;
    }

    // Tiro e recarga ficam na camada "Bracos" (a mesma da mira, braços e cabeça), saindo e voltando
    // para "Mirando": a pose da arma continua igual, só com o coice e a recarga por cima.
    private static void AddPistolStates(AnimatorController controller, AnimationClip shoot, AnimationClip reload)
    {
        AnimatorControllerLayer arms = controller.layers.FirstOrDefault(l => l.name == "Bracos");
        if (arms == null)
        {
            Debug.LogWarning("Animações extras: não achei a camada 'Bracos' no Alien.controller.");
            return;
        }

        AnimatorStateMachine machine = arms.stateMachine;
        foreach (ChildAnimatorState child in machine.states.ToArray())
        {
            if (child.state.name == "Tiro" || child.state.name == "Recarga")
            {
                machine.RemoveState(child.state);
            }
        }

        ProtagonistJumpSetup.CleanTransitions(machine);
        AnimatorState aim = machine.states.Select(c => c.state).FirstOrDefault(s => s.name == "Mirando");
        if (aim == null)
        {
            return;
        }

        PistolState(machine, aim, "Tiro", shoot, 1.6f);
        PistolState(machine, aim, "Recarga", reload, 1.1f);
    }

    private static void PistolState(AnimatorStateMachine machine, AnimatorState aim, string trigger, AnimationClip clip, float speed)
    {
        AnimatorState state = machine.AddState(trigger);
        state.motion = clip;
        state.speed = speed;

        AnimatorStateTransition enter = aim.AddTransition(state);
        enter.hasExitTime = false;
        enter.duration = 0.04f;
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

        AnimatorStateTransition again = state.AddTransition(state); // segundo tiro da rajada
        again.hasExitTime = false;
        again.duration = 0.03f;
        again.AddCondition(AnimatorConditionMode.If, 0f, trigger);

        AnimatorStateTransition back = state.AddTransition(aim);
        back.hasExitTime = true;
        back.exitTime = 0.85f;
        back.duration = 0.15f;

        AnimatorStateTransition drop = state.AddTransition(machine.defaultState);
        drop.hasExitTime = false;
        drop.duration = 0.1f;
        drop.AddCondition(AnimatorConditionMode.IfNot, 0f, "Aiming");
    }

    // Camada base: conversa, dança e banco (o AlienBrain decide; o AlienAnimator passa "Atividade").
    // 1 = conversa, 2 = dança, 3 = sentado.
    private static void AddActivities(AnimatorController controller, System.Func<string, AnimationClip> clip)
    {
        if (!controller.parameters.Any(p => p.name == "Atividade"))
        {
            controller.AddParameter("Atividade", AnimatorControllerParameterType.Int);
        }

        AnimatorStateMachine body = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in body.states.ToArray())
        {
            if (ActivityStates.Contains(child.state.name))
            {
                body.RemoveState(child.state);
            }
        }

        ProtagonistJumpSetup.CleanTransitions(body);
        AnimatorState locomotion = body.states.Select(c => c.state).FirstOrDefault(state => state.name == "Locomocao");
        if (locomotion == null)
        {
            Debug.LogWarning("Animações extras: não achei o estado 'Locomocao' no Alien.controller.");
            return;
        }

        AnimatorState talk = body.AddState("Conversa");
        talk.motion = clip("Idle_Talking_Loop");
        AnimatorState dance = body.AddState("Danca");
        dance.motion = clip("Dance_Loop");
        AnimatorState sitting = body.AddState("Sentando");
        sitting.motion = clip("Sitting_Enter");
        AnimatorState seated = body.AddState("Sentado");
        seated.motion = clip("Sitting_Idle_Loop");
        AnimatorState standing = body.AddState("Levantando");
        standing.motion = clip("Sitting_Exit");

        ActivityLink(locomotion, talk, 0.35f, AnimatorConditionMode.Equals, 1);
        ActivityLink(talk, locomotion, 0.3f, AnimatorConditionMode.NotEqual, 1);
        ActivityLink(locomotion, dance, 0.35f, AnimatorConditionMode.Equals, 2);
        ActivityLink(dance, locomotion, 0.3f, AnimatorConditionMode.NotEqual, 2);
        ActivityLink(locomotion, sitting, 0.2f, AnimatorConditionMode.Equals, 3);
        ActivityLink(sitting, standing, 0.2f, AnimatorConditionMode.NotEqual, 3);
        ActivityLink(seated, standing, 0.2f, AnimatorConditionMode.NotEqual, 3);

        AnimatorStateTransition sat = sitting.AddTransition(seated);
        sat.hasExitTime = true;
        sat.exitTime = 0.95f;
        sat.duration = 0.15f;

        AnimatorStateTransition up = standing.AddTransition(locomotion);
        up.hasExitTime = true;
        up.exitTime = 0.9f;
        up.duration = 0.2f;
    }

    private static void ActivityLink(AnimatorState from, AnimatorState to, float duration, AnimatorConditionMode mode, int value)
    {
        AnimatorStateTransition link = from.AddTransition(to);
        link.hasExitTime = false;
        link.duration = duration;
        link.AddCondition(mode, value, "Atividade");
    }

    // Golpe: entra pelo gatilho (de qualquer estado da camada) e volta sozinho para "Vazio" no fim.
    private static void AddStrike(AnimatorStateMachine machine, AnimatorState empty, string trigger,
        AnimationClip clip, float speed)
    {
        AnimatorState state = machine.AddState(trigger);
        state.motion = clip;
        state.speed = speed;

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        enter.duration = 0.06f;
        enter.hasExitTime = false;
        enter.canTransitionToSelf = true;
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        enter.AddCondition(AnimatorConditionMode.IfNot, 0f, "Tumbling");

        AnimatorStateTransition back = state.AddTransition(empty);
        back.hasExitTime = true;
        back.exitTime = 0.82f;
        back.duration = 0.18f;
    }
}
