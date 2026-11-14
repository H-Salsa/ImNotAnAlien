using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Ferramenta de editor da Missão 19 — horizonte curvo do planeta.
// Menu: ImNotAnAlien > Horizonte Curvo (Missao 19)
//   Troca o shader de todos os materiais do projeto que usam "Universal Render Pipeline/Lit" pelo
//   "ImNotAnAlien/Curved Lit" (cópia do Lit que entorta o mundo ao longe). As propriedades são as mesmas:
//   cor, textura e brilho continuam iguais.
// Menu: ImNotAnAlien > Desfazer Horizonte Curvo (Missao 19)
//   Volta todos para o URP Lit.
// Materiais dentro de arquivos FBX (embutidos) não podem ser trocados e ficam planos; a ferramenta avisa.
public static class CurvedHorizonTool
{
    private const string LitName = "Universal Render Pipeline/Lit";

    [MenuItem("ImNotAnAlien/Horizonte Curvo (Missao 19)")]
    public static void Apply()
    {
        Swap(LitName, CurvedHorizon.ShaderName, true);
    }

    [MenuItem("ImNotAnAlien/Desfazer Horizonte Curvo (Missao 19)")]
    public static void Revert()
    {
        Swap(CurvedHorizon.ShaderName, LitName, false);
    }

    private static void Swap(string fromName, string toName, bool applying)
    {
        Shader to = Shader.Find(toName);
        if (to == null)
        {
            EditorUtility.DisplayDialog("Horizonte Curvo",
                $"Não achei o shader '{toName}'.\n\nConfira se Assets/Shaders/CurvedLit.shader e CurvedWorld.hlsl estão no projeto e sem erro no Console.", "OK");
            return;
        }

        int changed = 0;
        List<string> embedded = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/"))
            {
                continue; // materiais dos pacotes do Unity ficam como estão
            }

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is Material material) || material.shader == null || material.shader.name != fromName)
                {
                    continue;
                }

                if (!path.EndsWith(".mat"))
                {
                    embedded.Add($"{path} ({material.name})");
                    continue;
                }

                // Mesmas propriedades: trocar o shader mantém cor, textura, brilho e transparência.
                int queue = material.renderQueue;
                material.shader = to;
                material.renderQueue = queue;
                EditorUtility.SetDirty(material);
                changed++;
            }
        }

        AssetDatabase.SaveAssets();
        string message = applying
            ? $"Horizonte curvo ligado em {changed} materiais.\n\nNo jogo, o chão vai \"caindo\" ao longe. Com o modo teste (F8), a tecla F7 liga e desliga a curva para comparar."
            : $"{changed} materiais voltaram para o URP Lit (mundo plano).";
        if (embedded.Count > 0)
        {
            message += $"\n\n{embedded.Count} material(is) dentro de FBX não foram trocados (ficam planos). Veja o Console.";
            Debug.LogWarning("Horizonte curvo: materiais embutidos em FBX que não foram trocados:\n" + string.Join("\n", embedded));
        }

        Debug.Log("Horizonte curvo: " + message.Replace("\n", " "));
        EditorUtility.DisplayDialog("Horizonte Curvo", message, "OK");
    }
}
