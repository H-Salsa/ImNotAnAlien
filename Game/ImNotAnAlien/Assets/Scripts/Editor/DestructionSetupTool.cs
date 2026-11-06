using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 10.
// Menu: ImNotAnAlien > Preparar Destruicao
// Torna destrutíveis todas as CASAS dentro de "Esquina_Blockout": objetos cujo nome começa
// com "Casa" e que têm uma parte filha chamada "Paredes" (inclui casas duplicadas, como "Casa_A (1)").
// Também desliga o "Static Batching" das partes das casas, para elas poderem tremer e sumir.
// O prédio NÃO é incluído (fica para a Superforça). Para tornar outra estrutura destrutível,
// selecione o objeto que agrupa as partes e use Add Component > Destructible Structure.
public static class DestructionSetupTool
{
    [MenuItem("ImNotAnAlien/Preparar Destruicao")]
    public static void Setup()
    {
        GameObject blockout = GameObject.Find("Esquina_Blockout");
        if (blockout == null)
        {
            EditorUtility.DisplayDialog("Esquina não encontrada", "Não há um objeto 'Esquina_Blockout' na cena.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Destruicao");

        List<string> houses = new List<string>();
        foreach (Transform candidate in blockout.GetComponentsInChildren<Transform>(true))
        {
            if (!candidate.name.StartsWith("Casa") || candidate.Find("Paredes") == null)
            {
                continue;
            }

            if (!candidate.TryGetComponent(out DestructibleStructure _))
            {
                Undo.AddComponent<DestructibleStructure>(candidate.gameObject);
            }

            foreach (Transform part in candidate.GetComponentsInChildren<Transform>(true))
            {
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(part.gameObject);
                if ((flags & StaticEditorFlags.BatchingStatic) != 0)
                {
                    Undo.RecordObject(part.gameObject, "Desligar static batching");
                    GameObjectUtility.SetStaticEditorFlags(part.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                }
            }

            houses.Add(candidate.parent != null ? $"{candidate.parent.name}/{candidate.name}" : candidate.name);
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(blockout.scene);

        string list = houses.Count > 0 ? string.Join("\n", houses) : "(nenhuma)";
        Debug.Log($"Destruição preparada em {houses.Count} casa(s):\n{list}\nSALVE A CENA com Ctrl+S.");
        EditorUtility.DisplayDialog("Destruição preparada",
            $"{houses.Count} casa(s) agora são destrutíveis:\n\n{list}\n\nSalve a cena com Ctrl+S.", "OK");
    }
}
