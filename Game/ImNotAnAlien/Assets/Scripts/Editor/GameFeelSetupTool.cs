using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ferramenta de editor da Missão 5.
// Menu: ImNotAnAlien > Preparar Efeitos
// 1. Cria o material das partículas (estilhaços).
// 2. Cria o objeto "GameFeel" na cena (efeitos, sons, pausa de impacto).
// 3. Adiciona a tremida (CameraShake) à Main Camera.
public static class GameFeelSetupTool
{
    private const string MaterialPath = "Assets/Art/Materials/FX/M_Particula.mat";

    [MenuItem("ImNotAnAlien/Preparar Efeitos")]
    public static void Setup()
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Preparar Efeitos");

        Material particleMaterial = GetOrCreateParticleMaterial();

        GameFeel feel = Object.FindFirstObjectByType<GameFeel>();
        if (feel == null)
        {
            GameObject go = new GameObject("GameFeel");
            Undo.RegisterCreatedObjectUndo(go, "Preparar Efeitos");
            feel = go.AddComponent<GameFeel>();
        }

        SerializedObject feelData = new SerializedObject(feel);
        feelData.FindProperty("particleMaterial").objectReferenceValue = particleMaterial;
        feelData.ApplyModifiedProperties();

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogWarning("Main Camera não encontrada: a tremida de câmera não foi adicionada.");
        }
        else if (!mainCamera.TryGetComponent(out CameraShake _))
        {
            Undo.AddComponent<CameraShake>(mainCamera.gameObject);
        }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(feel.gameObject.scene);
        Selection.activeGameObject = feel.gameObject;
        Debug.Log("Efeitos preparados. Salve a cena com Ctrl+S e dê Play.");
    }

    private static Material GetOrCreateParticleMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
        {
            return material;
        }

        EnsureFolder("Assets", "Art");
        EnsureFolder("Assets/Art", "Materials");
        EnsureFolder("Assets/Art/Materials", "FX");

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("Shader 'Universal Render Pipeline/Particles/Unlit' não encontrado; usando Unlit comum.");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        material = new Material(shader) { color = Color.white };
        AssetDatabase.CreateAsset(material, MaterialPath);
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
