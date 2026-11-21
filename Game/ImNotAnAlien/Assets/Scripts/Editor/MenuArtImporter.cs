using UnityEditor;

// Arte da tela inicial (Assets/Resources/Menu, 20/11): imagens sem perda de qualidade e com fundo
// transparente certo (logo), sem mipmaps (tela 2D). Roda sozinho quando a imagem é importada.
public sealed class MenuArtImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Menu/"))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
    }
}
