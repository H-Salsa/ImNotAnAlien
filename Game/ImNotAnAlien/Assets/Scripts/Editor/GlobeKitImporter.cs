using UnityEditor;

// Kit do globo do mapa (Assets/Resources/Globe/GloboKit.fbx, feito no Blender): miniaturas, nuvem e pedestal.
// O jogo junta milhares de miniaturas numa malha só, então as malhas precisam ser legíveis; as cores vêm
// pintadas nos vértices (sem materiais).
[InitializeOnLoad]
public sealed class GlobeKitImporter : AssetPostprocessor
{
    private const string KitPath = "Assets/Resources/Globe/GloboKit.fbx";

    // Se o kit foi importado antes deste script existir, importa de novo (uma vez) já com as regras abaixo.
    static GlobeKitImporter()
    {
        EditorApplication.delayCall += () =>
        {
            if (AssetImporter.GetAtPath(KitPath) is ModelImporter importer && !importer.isReadable)
            {
                AssetDatabase.ImportAsset(KitPath, ImportAssetOptions.ForceUpdate);
            }
        };
    }

    private void OnPreprocessModel()
    {
        if (!assetPath.Replace('\\', '/').Contains("Resources/Globe/"))
        {
            return;
        }

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.meshCompression = ModelImporterMeshCompression.Off;
    }
}
