using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ferramenta de editor da Missão 19 — viaturas com dois policiais.
// Menu: ImNotAnAlien > Preparar Viaturas (Missao 19)
// Liga o modelo Viatura.fbx ao TrafficSystem da cena Mundo_Sorteado (os policiais sentados usam os
// mesmos modelos de alien dos motoristas, vestidos de policial). Pode rodar de novo.
public static class PoliceCarSetupTool
{
    private const string WorldScenePath = "Assets/Scenes/Mundo_Sorteado.unity";
    private const string PoliceCarPath = "Assets/Art/Vehicles/Viatura.fbx";

    [MenuItem("ImNotAnAlien/Preparar Viaturas (Missao 19)")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Pare o Play", "Saia do Play Mode antes de preparar as viaturas.", "OK");
            return;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(PoliceCarPath);
        if (model == null)
        {
            EditorUtility.DisplayDialog("Falta a viatura", "Não achei " + PoliceCarPath + ".", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Single);
        TrafficSystem traffic = Object.FindFirstObjectByType<TrafficSystem>();
        if (traffic == null)
        {
            EditorUtility.DisplayDialog("Sem trânsito", "A cena Mundo_Sorteado não tem TrafficSystem.", "OK");
            return;
        }

        SerializedObject data = new SerializedObject(traffic);
        data.FindProperty("policeCarModel").objectReferenceValue = model;
        data.FindProperty("patrolPoliceCars").intValue = 1;
        data.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string message = "Viaturas prontas na cena Mundo_Sorteado.\n\n" +
                         "Uma viatura patrulha o trânsito com dois policiais dentro.\n" +
                         "Ao ganhar estrela de procurado, ela vai até o crime, abre as portas e os policiais descem.\n" +
                         "Dica: o F8 (modo teste) ajuda a ver os blocos; destrua uma casa perto de um civil para testar.";
        Debug.Log(message);
        EditorUtility.DisplayDialog("Missão 19", message, "OK");
    }
}
