using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 13.
// Menu: ImNotAnAlien > Preparar Procurado (Missao 13)
// Adiciona o WantedSystem (nível de procurado) ao objeto "Sistemas" da cena aberta.
// Funciona na região (com população dinâmica) e também na cena Prototype.
public static class WantedSetupTool
{
    [MenuItem("ImNotAnAlien/Preparar Procurado (Missao 13)")]
    public static void Setup()
    {
        GameObject systems = GameObject.Find("Sistemas");
        if (systems == null)
        {
            EditorUtility.DisplayDialog("Sistemas não encontrado", "Esta cena não tem o objeto 'Sistemas'.", "OK");
            return;
        }

        if (!systems.TryGetComponent(out WantedSystem _))
        {
            Undo.AddComponent<WantedSystem>(systems);
        }

        EditorSceneManager.MarkSceneDirty(systems.scene);
        Selection.activeGameObject = systems;
        Debug.Log("Nível de procurado preparado. SALVE A CENA com Ctrl+S e dê Play.");
        EditorUtility.DisplayDialog("Procurado pronto",
            "Nível de procurado adicionado.\nSalve a cena com Ctrl+S e dê Play.", "OK");
    }
}
