using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Missão 19 — pulo completo do protagonista: impulso (Jump_Start), no ar (Jump_Loop, já existia)
// e aterrissagem (Jump_Land). Acrescenta ao Protagonista.controller o gatilho "Jump" e os estados
// "PuloInicio" e "Aterrissagem". Pode rodar de novo: refaz só esses estados.
// Chamada pelo menu "Preparar Animacoes Extras (Missao 19)" e pela ferramenta do protagonista.
public static class ProtagonistJumpSetup
{
    private const string ControllerPath = "Assets/Art/Characters/Protagonista/Protagonista.controller";
    private const string AnimationsFbx = "Assets/Art/Characters/Quaternius/Animations/UAL1_Standard.fbx";

    public static void Run()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning("Pulo: não achei o Protagonista.controller.");
            return;
        }

        Add(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("Pulo do protagonista: impulso e aterrissagem prontos no Protagonista.controller.");
    }

    public static void Add(AnimatorController controller)
    {
        AnimationClip start = Clip("Jump_Start");
        AnimationClip land = Clip("Jump_Land");
        if (start == null || land == null)
        {
            Debug.LogWarning("Pulo: faltam Jump_Start ou Jump_Land no UAL1_Standard.");
            return;
        }

        if (!controller.parameters.Any(p => p.name == "Jump"))
        {
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        }

        AnimatorStateMachine body = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in body.states.ToArray())
        {
            if (child.state.name == "PuloInicio" || child.state.name == "Aterrissagem")
            {
                body.RemoveState(child.state);
            }
        }

        CleanTransitions(body);
        AnimatorState locomotion = Find(body, "Locomocao");
        AnimatorState air = Find(body, "NoAr");
        if (locomotion == null || air == null)
        {
            Debug.LogWarning("Pulo: o Protagonista.controller não tem os estados 'Locomocao' e 'NoAr'.");
            return;
        }

        AnimatorState jump = body.AddState("PuloInicio");
        jump.motion = start;
        jump.speed = 1.6f;
        AnimatorState landing = body.AddState("Aterrissagem");
        landing.motion = land;
        landing.speed = 1.3f;

        // Locomoção --(Jump)--> impulso --(fim)--> no ar.
        AnimatorStateTransition toJump = locomotion.AddTransition(jump);
        toJump.hasExitTime = false;
        toJump.duration = 0.05f;
        toJump.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
        toJump.AddCondition(AnimatorConditionMode.IfNot, 0f, "Swimming");

        // O pulo precisa ser a PRIMEIRA saída da locomoção: senão "no ar" (Grounded falso) ganha,
        // o impulso não toca e o gatilho sobra para depois da aterrissagem (aterrissagem dupla).
        System.Collections.Generic.List<AnimatorStateTransition> order = locomotion.transitions.ToList();
        order.Remove(toJump);
        order.Insert(0, toJump);
        locomotion.transitions = order.ToArray();

        AnimatorStateTransition rising = jump.AddTransition(air);
        rising.hasExitTime = true;
        rising.exitTime = 0.45f; // só o agachar e o impulso (o resto do clipe sobe o corpo sozinho)
        rising.duration = 0.12f;

        // No ar --(chão)--> aterrissagem --> locomoção. Troca a volta direta que existia.
        foreach (AnimatorStateTransition old in air.transitions.ToArray())
        {
            if (old.destinationState == locomotion)
            {
                air.RemoveTransition(old);
            }
        }

        AnimatorStateTransition touch = air.AddTransition(landing);
        touch.hasExitTime = false;
        touch.duration = 0.05f;
        touch.offset = 0.2f; // começa no toque no chão (o início do clipe ainda está no ar)
        touch.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        AnimatorStateTransition quickTouch = jump.AddTransition(landing);
        quickTouch.hasExitTime = false;
        quickTouch.duration = 0.05f;
        quickTouch.offset = 0.2f;
        quickTouch.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

        AnimatorStateTransition recover = landing.AddTransition(locomotion);
        recover.hasExitTime = true;
        recover.exitTime = 0.55f;
        recover.duration = 0.2f;

        AnimatorStateTransition fallAgain = landing.AddTransition(air);
        fallAgain.hasExitTime = false;
        fallAgain.duration = 0.1f;
        fallAgain.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

        AnimatorStateTransition swimFromJump = jump.AddTransition(Find(body, "Nadando") ?? air);
        swimFromJump.hasExitTime = false;
        swimFromJump.duration = 0.2f;
        swimFromJump.AddCondition(AnimatorConditionMode.If, 0f, "Swimming");

        EditorUtility.SetDirty(controller);
    }

    // Tira transições que apontam para estados que não existem mais (de uma rodada anterior).
    public static void CleanTransitions(AnimatorStateMachine machine)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            foreach (AnimatorStateTransition transition in child.state.transitions.ToArray())
            {
                if (transition.destinationState == null && transition.destinationStateMachine == null && !transition.isExit)
                {
                    child.state.RemoveTransition(transition);
                }
            }
        }

        foreach (AnimatorStateTransition transition in machine.anyStateTransitions.ToArray())
        {
            if (transition.destinationState == null && transition.destinationStateMachine == null)
            {
                machine.RemoveAnyStateTransition(transition);
            }
        }
    }

    private static AnimatorState Find(AnimatorStateMachine machine, string name) =>
        machine.states.Select(c => c.state).FirstOrDefault(state => state.name == name);

    private static AnimationClip Clip(string name) =>
        AssetDatabase.LoadAllAssetsAtPath(AnimationsFbx).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
}
