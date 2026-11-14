using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Shaders que o jogo procura pelo NOME (Shader.Find) quando cria materiais por código: na build o Unity
// só leva shaders usados por materiais salvos, então estes precisam estar em
// Project Settings > Graphics > Always Included Shaders (senão grama, água e globo ficam rosa/somem).
// Roda sozinho ao abrir/compilar o projeto (só acrescenta o que falta) e também pelo menu:
// ImNotAnAlien > Incluir Shaders na Build
[InitializeOnLoad]
public static class BuildShadersSetup
{
    private static readonly string[] Names =
    {
        "ImNotAnAlien/Grass Wind",
        "ImNotAnAlien/Water",
        "ImNotAnAlien/Sky Planet",
        "ImNotAnAlien/Globe Relief",
        "ImNotAnAlien/Globe Mini",
        "ImNotAnAlien/Globe Atmosphere",
        "ImNotAnAlien/Curved Lit",
        "Universal Render Pipeline/Particles/Unlit",
        "Sprites/Default",
    };

    static BuildShadersSetup()
    {
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("ImNotAnAlien/Incluir Shaders na Build")]
    private static void Menu() => Run(true);

    private static void Run(bool showDialog)
    {
        Object settings = GraphicsSettings.GetGraphicsSettings();
        if (settings == null)
        {
            return;
        }

        SerializedObject so = new SerializedObject(settings);
        SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null)
        {
            return;
        }

        // Tira espaços vazios (ex.: tamanho aumentado à mão sem preencher).
        bool changed = false;
        HashSet<Shader> present = new HashSet<Shader>();
        for (int i = list.arraySize - 1; i >= 0; i--)
        {
            Shader shader = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
            if (shader == null)
            {
                list.DeleteArrayElementAtIndex(i);

                changed = true;
            }
            else
            {
                present.Add(shader);
            }
        }

        List<string> added = new List<string>();
        List<string> missing = new List<string>();
        foreach (string name in Names)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
            {
                missing.Add(name);
                continue;
            }

            if (present.Add(shader))
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                added.Add(name);
                changed = true;
            }
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        if (added.Count > 0)
        {
            Debug.Log("Shaders incluídos na build: " + string.Join(", ", added));
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning("Shaders não encontrados (não incluídos): " + string.Join(", ", missing));
        }

        if (showDialog)
        {
            EditorUtility.DisplayDialog("Shaders na Build",
                (added.Count > 0 ? "Incluídos agora: " + string.Join(", ", added) + "." : "Todos já estavam incluídos.") +
                (missing.Count > 0 ? "\nNão achei: " + string.Join(", ", missing) + "." : ""),
                "OK");
        }
    }
}
