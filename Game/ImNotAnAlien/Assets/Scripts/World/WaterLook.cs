using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Água transparente (Missão 20, pedido do autor: ver o personagem nadando).
// Troca o material de água do Kit (opaco) por uma cópia com o shader "ImNotAnAlien/Water"
// (transparente, ondinhas e brilho do sol), mantendo a cor. Chamado ao montar cada bloco
// (BlockBuilder.Build) e ao carregar os blocos do Vilarejo (WorldStreamer).
// Sem o shader (ex.: build sem ele incluído) a água continua como era.
public static class WaterLook
{
    public const string ShaderName = "ImNotAnAlien/Water";
    private static readonly Dictionary<Material, Material> cache = new Dictionary<Material, Material>();

    public static Material For(Material source)
    {
        if (source == null || source.shader != null && source.shader.name == ShaderName)
        {
            return source;
        }

        if (cache.TryGetValue(source, out Material made) && made != null)
        {
            return made;
        }

        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            return source;
        }

        Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
        made = new Material(shader) { name = source.name + "_Transparente" };
        made.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
        made.SetColor("_DeepColor", new Color(color.r * 0.45f, color.g * 0.6f, color.b * 0.8f, 1f));
        cache[source] = made;
        return made;
    }

    // Troca em todos os renderers de "root" que usam o material "source".
    public static void Apply(Transform root, Material source)
    {
        if (root == null || source == null)
        {
            return;
        }

        Material water = For(source);
        if (water == source)
        {
            return;
        }

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sharedMaterial == source)
            {
                renderer.sharedMaterial = water;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }
}
