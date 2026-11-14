using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Ferramenta de editor da Missão 19 — o planeta (decisões D21 e D28).
// Menu: ImNotAnAlien > Preparar Planeta (Missao 19)
// Transforma a cena Mundo_Sorteado (9 × 9 com paredes) no planeta:
// - WorldStreamer: modo Planeta (128 × 128, leste-oeste dá a volta, polos), sem paredes.
//   O Vilarejo vai para os blocos 59..61 do planeta SEM sair do lugar: muda só a origem da grade.
// - SectorTracker: setores e regiões do planeta (16 regiões × 16 setores, cota 50 por setor).
// Rode de novo sempre que rodar "Gerar Mundo Sorteado (Missao 18)" (que recria a cena).
public static class PlanetSetupTool
{
    private const string WorldScenePath = "Assets/Scenes/Mundo_Sorteado.unity";
    private const string KitPath = "Assets/Art/BlockKit.asset";

    // A grade 9 × 9 da Missão 18 tinha o canto em (-350, -550) e o Vilarejo nos blocos 3..5.
    // No planeta o Vilarejo fica nos blocos 59..61: a origem anda 56 blocos para o sudoeste.
    private const int Shift = 56;
    private static readonly Vector2 OldOrigin = new Vector2(-350f, -550f);
    private const float BlockSize = 100f;

    [MenuItem("ImNotAnAlien/Preparar Planeta (Missao 19)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Pare o Play", "Saia do Play Mode antes de preparar o planeta.", "OK");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(WorldScenePath) == null)
        {
            EditorUtility.DisplayDialog("Falta a cena", "Não achei Mundo_Sorteado. Rode antes 'Gerar Mundo Sorteado (Missao 18)'.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(WorldScenePath, OpenSceneMode.Single);
        WorldStreamer streamer = Object.FindFirstObjectByType<WorldStreamer>();
        SectorTracker tracker = Object.FindFirstObjectByType<SectorTracker>();
        if (streamer == null || tracker == null)
        {
            EditorUtility.DisplayDialog("Planeta", "A cena não tem WorldStreamer ou SectorTracker.", "OK");
            return;
        }

        // ---------- Streamer ----------
        SerializedObject data = new SerializedObject(streamer);
        data.FindProperty("planet").boolValue = true;
        data.FindProperty("procedural").boolValue = true;
        data.FindProperty("mapWalls").boolValue = false;
        data.FindProperty("blockSize").floatValue = BlockSize;
        data.FindProperty("gridSize").vector2IntValue = new Vector2Int(Planet.Width, Planet.Height);
        data.FindProperty("gridOrigin").vector2Value = OldOrigin - new Vector2(Shift, Shift) * BlockSize;

        BlockKit kit = AssetDatabase.LoadAssetAtPath<BlockKit>(KitPath);
        if (kit != null)
        {
            data.FindProperty("kit").objectReferenceValue = kit;
        }

        // Blocos fixos do Vilarejo: só muda o número (a posição no mundo é a mesma).
        SerializedProperty list = data.FindProperty("blocks");
        bool oldCoords = true;
        for (int i = 0; i < list.arraySize; i++)
        {
            Vector2Int coord = list.GetArrayElementAtIndex(i).FindPropertyRelative("coord").vector2IntValue;
            if (coord.x >= 9 || coord.y >= 9)
            {
                oldCoords = false;
            }
        }

        if (oldCoords)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty coord = list.GetArrayElementAtIndex(i).FindPropertyRelative("coord");
                coord.vector2IntValue += new Vector2Int(Shift, Shift);
            }
        }

        data.ApplyModifiedPropertiesWithoutUndo();

        // ---------- Setores ----------
        SerializedObject trackerData = new SerializedObject(tracker);
        trackerData.FindProperty("planetMode").boolValue = true;
        trackerData.FindProperty("sectors").arraySize = 0;
        trackerData.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        string message =
            "Planeta pronto!\n\n" +
            $"• {Planet.Width} × {Planet.Height} blocos ({Planet.Width * BlockSize / 1000f:0.#} km de volta leste-oeste), polos no norte e no sul.\n" +
            $"• {Planet.RegionCount} regiões × {Planet.SectorsPerRegion} setores, cota 50 por setor.\n" +
            "• Vilarejo do Bilu no meio da região Vale do Bilu.\n" +
            "• 4 bases: Nave Caída (noroeste), Torre de Rádio (nordeste), Laboratório Velho (sudoeste), Estufa Abandonada (sudeste).\n\n" +
            "Rode de novo 'Preparar Viaturas (Missao 19)' se tiver rodado 'Gerar Mundo Sorteado'.";
        Debug.Log("Preparar Planeta: " + message.Replace("\n", " "));
        EditorUtility.DisplayDialog("Planeta", message, "OK");
    }
}
