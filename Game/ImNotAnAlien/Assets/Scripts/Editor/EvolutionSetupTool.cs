using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 9.
// Menu: ImNotAnAlien > Preparar Evolucoes
// 1. Adiciona ao objeto "Sistemas" o EvolutionSystem (evoluções e custos) e o EvolutionMenu (Tab).
// 2. Adiciona ao jogador a Onda de Impacto (ShockwaveAbility), que começa bloqueada.
// Rodar de novo não apaga nada nem muda custos que você já tenha editado.
public static class EvolutionSetupTool
{
    [MenuItem("ImNotAnAlien/Preparar Evolucoes")]
    public static void Setup()
    {
        GameObject systems = GameObject.Find("Sistemas");
        if (systems == null)
        {
            EditorUtility.DisplayDialog("Sistemas não encontrado",
                "Rode antes: ImNotAnAlien > Preparar Versao 0.2", "OK");
            return;
        }

        PrototypeThirdPersonController player = Object.FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Jogador não encontrado",
                "Não há um personagem com PrototypeThirdPersonController na cena.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Evolucoes");

        GetOrAdd<EvolutionSystem>(systems);
        GetOrAdd<EvolutionMenu>(systems);
        GetOrAdd<ShockwaveAbility>(player.gameObject);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(systems.scene);
        Selection.activeGameObject = systems;
        Debug.Log("Evoluções preparadas. SALVE A CENA com Ctrl+S e dê Play. No jogo, aperte Tab.");
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.TryGetComponent(out T existing) ? existing : Undo.AddComponent<T>(target);
    }
}
