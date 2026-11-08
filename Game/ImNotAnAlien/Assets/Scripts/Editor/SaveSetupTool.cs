using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ferramenta de editor da Missão 17 — salvamento e menu inicial.
// Menu: ImNotAnAlien > Preparar Salvamento (Missao 17)
// Rode depois de "Cortar Vilarejo em Blocos (Missao 16)". A ferramenta:
// 1. Põe o SaveManager no objeto "Sistemas" da cena Regiao1_VilarejoDoBilu_Blocos.
// 2. Cria a cena MenuInicial (câmera + menu com os três espaços de jogo).
// 3. Ordem do Build: MenuInicial primeiro, depois a cena do jogo.
// Pode rodar de novo sem problema.
public static class SaveSetupTool
{
    private const string GameScenePath = "Assets/Scenes/Regiao1_VilarejoDoBilu_Blocos.unity";
    private const string MenuScenePath = "Assets/Scenes/MenuInicial.unity";

    [MenuItem("ImNotAnAlien/Preparar Salvamento (Missao 17)")]
    public static void Setup()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Pare o Play", "Saia do Play Mode antes de preparar o salvamento.", "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) == null)
        {
            EditorUtility.DisplayDialog("Falta a cena de blocos",
                "Não achei " + GameScenePath + ".\nRode antes 'Cortar Vilarejo em Blocos (Missao 16)'.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        // 1. SaveManager na cena do jogo.
        Scene game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        GameObject systems = null;
        foreach (GameObject root in game.GetRootGameObjects())
        {
            if (root.name == "Sistemas")
            {
                systems = root;
            }
        }

        if (systems == null)
        {
            EditorUtility.DisplayDialog("Cena errada", "A cena de blocos não tem o objeto 'Sistemas'.", "OK");
            return;
        }

        if (systems.GetComponent<SaveManager>() == null)
        {
            systems.AddComponent<SaveManager>();
        }

        EditorSceneManager.MarkSceneDirty(game);
        EditorSceneManager.SaveScene(game);

        // 2. Cena do menu inicial.
        Scene menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.03f, 0.12f, 1f);
        cameraObject.AddComponent<AudioListener>();

        GameObject menuObject = new GameObject("MenuInicial");
        MainMenu mainMenu = menuObject.AddComponent<MainMenu>();
        SerializedObject data = new SerializedObject(mainMenu);
        data.FindProperty("gameSceneName").stringValue = System.IO.Path.GetFileNameWithoutExtension(GameScenePath);
        data.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(menu, MenuScenePath);

        // 3. Build: menu primeiro, depois o jogo.
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == MenuScenePath || s.path == GameScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(GameScenePath, true));
        scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        string message = "Salvamento pronto.\n\n" +
                         "A cena MenuInicial está aberta: dê Play para escolher um dos 3 espaços de jogo.\n" +
                         "No jogo: F5 salva; salva sozinho a cada 3 min, ao limpar setor e ao fechar.\n" +
                         "Tab > 'Salvar e voltar ao menu'.\n\n" +
                         "Play direto na cena do jogo usa o Jogo 1.";
        Debug.Log(message + "\nArquivos em: " + SaveSystem.Folder);
        EditorUtility.DisplayDialog("Missão 17", message, "OK");
    }
}
