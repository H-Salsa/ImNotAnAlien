using UnityEngine;

// Efeito de água mexendo em volta de quem nada (Missão 20): protagonista e aliens.
// - Anéis: ondinhas redondas que crescem e somem na superfície (devagar parado, seguidas nadando).
// - Espuma: manchas brancas que ficam para trás e se abrem para os lados (rastro em "V").
// - Gotas: respingos dos braços quando nada para a frente.
// Tudo com partículas e texturas feitas por código (sem arquivos). Criado por SwimWake.For(dono)
// e alimentado a cada quadro com Feed(); some junto com o dono.
public sealed class SwimWake : MonoBehaviour
{
    private static Material ringMaterial, dotMaterial;

    private ParticleSystem rings, foam, drops;
    private float ringTimer;
    private float foamCarry, dropCarry;

    public static SwimWake For(Transform owner)
    {
        GameObject go = new GameObject("Rastro_Agua");
        go.transform.SetParent(owner, false);
        SwimWake wake = go.AddComponent<SwimWake>();
        wake.Build();
        return wake;
    }

    // swimming = na superfície; head = ponto da frente do corpo (onde a água "abre").
    public void Feed(bool swimming, float surfaceY, Vector3 velocity, Vector3 head)
    {
        if (!swimming || rings == null)
        {
            return;
        }

        float dt = Time.deltaTime;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = flat.magnitude;
        Vector3 at = new Vector3(head.x, surfaceY + 0.03f, head.z);
        Vector3 forward = speed > 0.05f ? flat / speed : transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, forward);

        ringTimer -= dt;
        if (ringTimer <= 0f)
        {
            bool moving = speed > 0.5f;
            ringTimer = moving ? 0.22f : 0.75f + Random.value * 0.3f;
            Emit(rings, at + Random.insideUnitSphere.Flat() * 0.2f, Vector3.zero, moving ? 1.1f : 0.8f, moving ? 1.3f : 2f);
        }

        if (speed > 0.4f)
        {
            foamCarry += speed * dt * 12f;
            while (foamCarry >= 1f)
            {
                foamCarry -= 1f;
                float s = Random.value < 0.5f ? -1f : 1f;
                Vector3 spread = side * s * Random.Range(0.4f, 1.1f) - forward * Random.Range(0.1f, 0.4f);
                Emit(foam, at + side * s * Random.Range(0.1f, 0.4f) - forward * Random.Range(0f, 0.6f), spread, Random.Range(0.35f, 0.7f), Random.Range(1.2f, 2f));
            }

            dropCarry += speed * dt * 9f;
            while (dropCarry >= 1f)
            {
                dropCarry -= 1f;
                float s = Random.value < 0.5f ? -1f : 1f;
                Vector3 v = Vector3.up * Random.Range(1.4f, 2.6f) + side * s * Random.Range(0.3f, 0.9f) + forward * Random.Range(0f, 0.6f);
                Emit(drops, at + side * s * 0.35f + Vector3.up * 0.1f, v, Random.Range(0.05f, 0.1f), Random.Range(0.4f, 0.7f));
            }
        }
    }

    private static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life)
    {
        ParticleSystem.EmitParams p = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startSize = size,
            startLifetime = life,
            rotation = Random.Range(0f, 360f),
            applyShapeToPosition = false,
        };
        ps.Emit(p, 1);
    }

    // ---------- Montagem ----------

    private void Build()
    {
        if (ringMaterial == null)
        {
            ringMaterial = MakeMaterial(MakeTexture(true), "Agua_Anel");
            dotMaterial = MakeMaterial(MakeTexture(false), "Agua_Espuma");
        }

        if (ringMaterial == null)
        {
            return;
        }

        rings = MakeSystem("Aneis", ringMaterial, ParticleSystemRenderMode.HorizontalBillboard, 0f,
            new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 3.2f)), 0.55f);
        foam = MakeSystem("Espuma", dotMaterial, ParticleSystemRenderMode.HorizontalBillboard, 0f,
            new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)), 0.7f);
        drops = MakeSystem("Gotas", dotMaterial, ParticleSystemRenderMode.Billboard, 1f,
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.5f)), 0.9f);

        // Espuma freia (a água segura).
        ParticleSystem.LimitVelocityOverLifetimeModule drag = foam.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 0.2f;
        drag.dampen = 0.08f;
    }

    private ParticleSystem MakeSystem(string name, Material material, ParticleSystemRenderMode mode, float gravity, AnimationCurve grow, float alpha)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = gravity;
        main.maxParticles = 120;
        main.startColor = new Color(1f, 1f, 1f, alpha);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, grow);

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingFudge = -10f; // desenha depois (por cima) da lâmina de água
        ps.Play();
        return ps;
    }

    private static Material MakeMaterial(Texture2D texture, string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null) return null;

        Material material = new Material(shader) { name = name };
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;
        return material;
    }

    // Anel (onda redonda) ou mancha macia (espuma/gota).
    private static Texture2D MakeTexture(bool ring)
    {
        const int size = 64;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a;
                if (ring)
                {
                    float band = Mathf.Abs(r - 0.82f) / 0.1f;
                    a = Mathf.Clamp01(1f - band) * Mathf.Clamp01((1f - r) * 8f);
                    a += Mathf.Clamp01(1f - Mathf.Abs(r - 0.6f) / 0.06f) * 0.35f; // anel de dentro, fraco
                }
                else
                {
                    a = Mathf.Clamp01(1f - r);
                    a = a * a * (3f - 2f * a);
                }

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = ring ? "Agua_Anel" : "Agua_Espuma", wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }
}

internal static class SwimWakeVectorExtensions
{
    public static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);
}
