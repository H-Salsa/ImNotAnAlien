using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Versão 0.2 (Missão 7).
// Menu: ImNotAnAlien > Preparar Versao 0.2
// 1. Cria o material brilhante dos orbes de DNA.
// 2. Cria o objeto "Sistemas" com: DnaOrbSystem (orbes), SectorTracker (objetivo) e GameHud (interface).
// 3. Adiciona o DnaInventory (DNA acumulado) ao jogador.
public static class Version02SetupTool
{
    private const string OrbMaterialPath = "Assets/Art/Materials/FX/M_DNA.mat";
    private const string ParticleMaterialPath = "Assets/Art/Materials/FX/M_Particula.mat";
    private const string SystemsName = "Sistemas";

    [MenuItem("ImNotAnAlien/Preparar Versao 0.2")]
    public static void Setup()
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Versao 0.2");

        Material orbMaterial = GetOrCreateOrbMaterial();
        Material trailMaterial = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterialPath);
        if (trailMaterial == null)
        {
            Debug.LogWarning("M_Particula não encontrado: rode antes ImNotAnAlien > Preparar Efeitos. O rastro dos orbes usará o material do orbe.");
            trailMaterial = orbMaterial;
        }

        GameObject systems = GameObject.Find(SystemsName);
        if (systems == null)
        {
            systems = new GameObject(SystemsName);
            Undo.RegisterCreatedObjectUndo(systems, "Preparar Versao 0.2");
        }

        DnaOrbSystem orbs = GetOrAdd<DnaOrbSystem>(systems);
        SerializedObject orbData = new SerializedObject(orbs);
        orbData.FindProperty("orbMaterial").objectReferenceValue = orbMaterial;
        orbData.FindProperty("trailMaterial").objectReferenceValue = trailMaterial;
        orbData.ApplyModifiedProperties();

        GetOrAdd<SectorTracker>(systems);
        GetOrAdd<GameHud>(systems);

        PrototypeThirdPersonController player = Object.FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player != null)
        {
            GetOrAdd<DnaInventory>(player.gameObject);
        }
        else
        {
            Debug.LogWarning("Jogador não encontrado: DnaInventory não foi adicionado.");
        }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(systems.scene);
        Selection.activeGameObject = systems;
        Debug.Log("Versão 0.2 preparada. Salve a cena com Ctrl+S e dê Play.");
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.TryGetComponent(out T existing) ? existing : Undo.AddComponent<T>(target);
    }

    private static Material GetOrCreateOrbMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(OrbMaterialPath);
        if (material != null)
        {
            return material;
        }

        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "Materials");
        EnsureFolder("Assets/Art/Materials", "FX");

        // "Unlit" ignora a luz da cena: o orbe parece brilhar sozinho.
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        {
            color = new Color(0.3f, 1f, 0.6f, 1f),
        };
        AssetDatabase.CreateAsset(material, OrbMaterialPath);
        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
