using System.IO;
using UnityEditor;
using UnityEngine;

// Missão 19 — fonte do mapa planetário. O globo carrega a Fredoka por Resources.Load, então os
// arquivos precisam estar em Assets/Resources/Fonts. Este script copia sozinho ao abrir o projeto
// (e pelo menu ImNotAnAlien > Preparar Globo (Missao 19)). A original fica onde o autor colocou.
[InitializeOnLoad]
public static class GlobeFontsSetup
{
    private const string Source = "Assets/Fonts/Fredoka,Montserrat/Fredoka/static/";
    private const string Target = "Assets/Resources/Fonts/";
    private static readonly string[] Files = { "Fredoka-Bold.ttf", "Fredoka-SemiBold.ttf", "Fredoka-Medium.ttf" };

    static GlobeFontsSetup()
    {
        EditorApplication.delayCall += () => Copy(false);
    }

    [MenuItem("ImNotAnAlien/Preparar Globo (Missao 19)")]
    public static void Run()
    {
        Copy(true);
    }

    private static void Copy(bool loud)
    {
        int copied = 0;
        int missing = 0;
        foreach (string file in Files)
        {
            string from = Source + file;
            string to = Target + file;
            if (AssetDatabase.LoadAssetAtPath<Font>(to) != null)
            {
                continue;
            }

            if (AssetDatabase.LoadAssetAtPath<Font>(from) == null)
            {
                missing++;
                continue;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts"))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Fonts");
            }

            if (AssetDatabase.CopyAsset(from, to))
            {
                copied++;
            }
        }

        if (copied > 0)
        {
            AssetDatabase.Refresh();
            Debug.Log($"Globo: {copied} arquivo(s) da fonte Fredoka copiados para {Target}.");
        }

        if (loud)
        {
            EditorUtility.DisplayDialog("Globo", missing > 0
                ? $"Não achei {missing} arquivo(s) da Fredoka em {Source}."
                : "Fonte Fredoka pronta para o mapa planetário.", "OK");
        }
    }
}
