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
public static class PoliceMeleeSetupTool
{
    private const string AnimationsFbx = "Assets/Art/Characters/Quaternius/Animations/UAL1_Standard.fbx";
    private const string ControllerPath = "Assets/Art/Characters/Aliens/Alien.controller";
    private const string MaskPath = "Assets/Art/Characters/Aliens/Alien_Golpes.mask";
    private const string LayerName = "Golpes";
    private static readonly string[] Triggers = { "Soco", "Jab", "Cassetete" };

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

        EditorUtility.SetDirty(controller);
        return missing;
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
