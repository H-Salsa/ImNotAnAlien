using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor: carros explosivos.
// Menu: ImNotAnAlien > Preparar Carros Explosivos
// Adiciona o ExplosiveVehicle a todos os carros com física da cena aberta (os estacionados).
// Os carros do trânsito já nascem explosivos. Funciona na região e na cena Prototype.
public static class ExplosiveCarsSetupTool
{
    [MenuItem("ImNotAnAlien/Preparar Carros Explosivos")]
    public static void Setup()
    {
        int added = 0;
        foreach (Rigidbody body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (!body.name.StartsWith("Carro") || body.GetComponent<ExplosiveVehicle>() != null)
            {
                continue;
            }

            Undo.AddComponent<ExplosiveVehicle>(body.gameObject);
            added++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        string message = $"{added} carro(s) agora podem pegar fogo e explodir.\n\nSalve a cena com Ctrl+S e dê Play.";
        Debug.Log(message);
        EditorUtility.DisplayDialog("Carros explosivos", message, "OK");
    }
}
