using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 12 — população dinâmica e cotas (decisão D19).
// Menu: ImNotAnAlien > Preparar Populacao (Missao 12)
// Rode com a cena Regiao1_VilarejoDoBilu aberta. A ferramenta:
// 1. Remove os alienígenas fixos (agora eles surgem sozinhos perto do jogador).
// 2. Configura a região "Vilarejo do Bilu" com 4 setores e cota total de 250.
// 3. Adiciona o PopulationSpawner ao objeto "Sistemas".
public static class PopulationSetupTool
{
    private struct SectorSetup
    {
        public string name;
        public Rect area;
        public int quota;
    }

    // Mesmas áreas usadas pelo gerador da região (vistas de cima: X e Z).
    private static readonly SectorSetup[] Sectors =
    {
        new SectorSetup { name = "Esquina", area = Rect.MinMaxRect(-60f, -95f, 90f, 60f), quota = 50 },
        new SectorSetup { name = "Vila das Casas", area = Rect.MinMaxRect(90f, -95f, 240f, 60f), quota = 75 },
        new SectorSetup { name = "Parque e Lago", area = Rect.MinMaxRect(-60f, -240f, 90f, -120f), quota = 55 },
        new SectorSetup { name = "Margem do Rio", area = Rect.MinMaxRect(90f, -240f, 240f, -120f), quota = 70 },
    };

    private const int SectorBonus = 40;
    private const int RegionBonus = 100;

    [MenuItem("ImNotAnAlien/Preparar Populacao (Missao 12)")]
    public static void Setup()
    {
        GameObject region = GameObject.Find("Vilarejo_do_Bilu");
        GameObject systems = GameObject.Find("Sistemas");
        SectorTracker tracker = Object.FindFirstObjectByType<SectorTracker>();
        if (region == null || systems == null || tracker == null)
        {
            EditorUtility.DisplayDialog("Cena errada",
                "Abra a cena Regiao1_VilarejoDoBilu (gerada na Missão 11) e rode de novo.", "OK");
            return;
        }

        GameObject civilPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Alienigena.prefab");
        GameObject policePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Alienigena_Policial.prefab");
        if (civilPrefab == null || policePrefab == null)
        {
            EditorUtility.DisplayDialog("Prefabs não encontrados",
                "Não achei Assets/Prefabs/Alienigena.prefab e Alienigena_Policial.prefab.", "OK");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Populacao");

        // 1. Alienígenas fixos saem.
        GameObject oldGroup = GameObject.Find("Alienigenas");
        int removed = 0;
        if (oldGroup != null)
        {
            removed = oldGroup.transform.childCount;
            Undo.DestroyObjectImmediate(oldGroup);
        }

        // 2. Região e setores.
        SerializedObject trackerData = new SerializedObject(tracker);
        trackerData.FindProperty("sectorName").stringValue = "Vilarejo do Bilu";
        trackerData.FindProperty("clearBonusDna").intValue = RegionBonus;
        SerializedProperty list = trackerData.FindProperty("sectors");
        list.arraySize = Sectors.Length;
        for (int i = 0; i < Sectors.Length; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("name").stringValue = Sectors[i].name;
            item.FindPropertyRelative("area").rectValue = Sectors[i].area;
            item.FindPropertyRelative("quota").intValue = Sectors[i].quota;
            item.FindPropertyRelative("clearBonusDna").intValue = SectorBonus;
        }

        trackerData.ApplyModifiedProperties();

        // 3. Surgimento dinâmico.
        PopulationSpawner spawner = systems.TryGetComponent(out PopulationSpawner existing)
            ? existing
            : Undo.AddComponent<PopulationSpawner>(systems);

        SerializedObject spawnerData = new SerializedObject(spawner);
        spawnerData.FindProperty("civilPrefab").objectReferenceValue = civilPrefab;
        spawnerData.FindProperty("policePrefab").objectReferenceValue = policePrefab;

        string[] colors = { "Lilas", "Verde", "Azul", "Rosa", "Laranja" };
        SerializedProperty skins = spawnerData.FindProperty("civilSkins");
        skins.arraySize = 0;
        foreach (string color in colors)
        {
            Material skin = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Art/Materials/Aliens/M_Alien_{color}.mat");
            if (skin == null)
            {
                continue;
            }

            skins.arraySize++;
            skins.GetArrayElementAtIndex(skins.arraySize - 1).objectReferenceValue = skin;
        }

        spawnerData.ApplyModifiedProperties();

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(systems.scene);
        Selection.activeGameObject = systems;

        string message = $"{removed} alienígenas fixos removidos.\nRegião: Vilarejo do Bilu, cota 250 em 4 setores.\n" +
                         "Agora os alienígenas surgem perto de você.\n\nSALVE A CENA com Ctrl+S e dê Play.";
        Debug.Log(message);
        EditorUtility.DisplayDialog("População dinâmica pronta", message, "OK");
    }
}
