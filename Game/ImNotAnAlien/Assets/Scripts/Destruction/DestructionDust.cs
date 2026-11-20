using UnityEngine;

// Nuvem de poeira da destruição (Missão 20) — estilo desenho animado.
// Cada partícula é um "chumaço" de nuvem (vários círculos juntos, claro em cima e mais escuro embaixo,
// borda suave) desenhado por código numa textura, sempre virado para a câmera. As nuvens nascem
// pequenas, crescem, giram devagar, sobem um pouco com turbulência e somem aos poucos.
// Disfarça a troca da casa/prédio pelos cacos e deixa o desabamento mais "sujo".
// Some sozinha: para de soltar depois de "duration" segundos e se apaga quando a última nuvem some.
public sealed class DestructionDust : MonoBehaviour
{
    private static Material material;
    private static Texture2D puff;

    // Caixa de nuvens (reaproveitamento, 18/11): a nuvem que termina fica guardada, desligada, e é usada
    // de novo na próxima destruição (um desabamento solta até 7 de uma vez).
    private const int PoolCap = 24;
    private static readonly System.Collections.Generic.Stack<DestructionDust> FreeDust = new System.Collections.Generic.Stack<DestructionDust>();

    private ParticleSystem system;
    private float stopAt;
    private float recycleAt;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        FreeDust.Clear();
        impactSystem = null;
        impactBudget = 0f;
        impactTime = 0f;
    }

    // ---------- Fumacinha de impacto (20/11, pedido do autor) ----------
    // Quando um caco de casa/prédio bate no chão: um "puf" pequeno de poeira, nada grandioso.
    // Um sistema de partículas só para o jogo todo (Emit na posição do impacto): não cria objeto por caco.
    // Limites para não pesar quando cai um prédio inteiro: só perto da câmera e no máximo ~25 por segundo.
    private const float ImpactMaxDistance = 70f;
    private const float ImpactsPerSecond = 25f;
    private static ParticleSystem impactSystem;
    private static float impactBudget;
    private static float impactTime;

    public static void Impact(Vector3 point, float size)
    {
        // Orçamento: enche 25 por segundo, guarda no máximo 8 de uma vez.
        float now = Time.time;
        impactBudget = Mathf.Min(8f, impactBudget + (now - impactTime) * ImpactsPerSecond);
        impactTime = now;
        if (impactBudget < 1f)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam != null && (cam.transform.position - point).sqrMagnitude > ImpactMaxDistance * ImpactMaxDistance)
        {
            return;
        }

        ParticleSystem ps = ImpactSystem();
        if (ps == null)
        {
            return;
        }

        impactBudget -= 1f;
        size = Mathf.Clamp(size, 0.4f, 1.6f);
        int count = size > 1f ? 4 : 3;
        ParticleSystem.EmitParams p = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            p.position = point + new Vector3(dir.x, 0.05f, dir.y) * (0.15f * size);
            p.velocity = new Vector3(dir.x * Random.Range(0.6f, 1.4f), Random.Range(0.3f, 0.9f), dir.y * Random.Range(0.6f, 1.4f)) * size;
            p.startSize = Random.Range(0.55f, 0.9f) * size;
            p.startLifetime = Random.Range(0.7f, 1.2f);
            p.rotation = Random.Range(0f, 360f);
            p.startColor = Color.Lerp(new Color(0.82f, 0.78f, 0.7f), new Color(0.95f, 0.93f, 0.88f), Random.value);
            ps.Emit(p, 1);
        }
    }

    private static ParticleSystem ImpactSystem()
    {
        if (impactSystem != null)
        {
            return impactSystem;
        }

        Material mat = DustMaterial();
        if (mat == null)
        {
            return null;
        }

        GameObject go = new GameObject("Poeira_Impacto");
        Object.DontDestroyOnLoad(go);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startSpeed = 0f;
        main.gravityModifier = -0.02f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false; // só Emit

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // Nasce pequeno, abre rápido e some.
        ParticleSystem.SizeOverLifetimeModule growth = ps.sizeOverLifetime;
        growth.enabled = true;
        growth.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.5f)));

        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.1f), new GradientAlphaKey(0.5f, 0.55f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

        // Freia logo (poeira pesada, fica perto do chão).
        ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 0.6f;
        drag.dampen = 0.25f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.sortMode = ParticleSystemSortMode.None;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 2f;

        ps.Play();
        impactSystem = ps;
        return ps;
    }

    public static void Spawn(Vector3 position, float radius, float duration)
    {
        Material mat = DustMaterial();
        if (mat == null)
        {
            return;
        }

        DestructionDust dust = null;
        while (FreeDust.Count > 0 && dust == null)
        {
            dust = FreeDust.Pop();
        }

        if (dust == null)
        {
            dust = Build(mat);
        }

        // O que muda de uma nuvem para outra: tamanho, quantidade e por quanto tempo solta.
        ParticleSystem ps = dust.system;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        dust.transform.position = position;

        ParticleSystem.MainModule main = ps.main;
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.7f, radius * 1.3f);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = Mathf.Clamp(4f * radius, 3f, 20f);
        emission.burstCount = 1;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.Clamp(radius * 4f, 4f, 22f)));

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.radius = radius;

        dust.stopAt = Time.time + duration;
        dust.recycleAt = 0f;
        dust.enabled = true;
        dust.gameObject.SetActive(true);
        ps.Play();
    }

    // Monta uma nuvem nova (só quando a caixa está vazia). As partes fixas ficam configuradas para sempre.
    private static DestructionDust Build(Material mat)
    {
        GameObject go = new GameObject("Poeira_Destruicao");
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.4f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.86f, 0.82f, 0.75f), new Color(0.97f, 0.95f, 0.9f));
        main.gravityModifier = -0.03f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 140;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;

        // Cresce (nasce em 40% do tamanho e termina em 140%).
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.4f)));

        // Aparece rápido e some devagar.
        ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.88f, 0.85f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.08f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
        color.color = fade;

        // Gira devagar e sobe com turbulência; freia com o tempo (poeira pesada).
        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.2f;

        ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 3f;
        drag.dampen = 0.15f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 3f;

        DestructionDust dust = go.AddComponent<DestructionDust>();
        dust.system = ps;
        return dust;
    }

    private void Update()
    {
        if (system == null)
        {
            Destroy(gameObject);
            return;
        }

        if (recycleAt <= 0f)
        {
            if (Time.time >= stopAt)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                recycleAt = Time.time + 4.5f; // espera a última nuvem sumir
            }

            return;
        }

        if (Time.time >= recycleAt)
        {
            Store();
        }
    }

    private void Store()
    {
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (FreeDust.Count >= PoolCap)
        {
            Destroy(gameObject);
            return;
        }

        gameObject.SetActive(false);
        FreeDust.Push(this);
    }

    // ---------- Material e textura (feitos uma vez) ----------

    private static Material DustMaterial()
    {
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return null;
        }

        material = new Material(shader) { name = "Poeira (nuvem)" };
        Texture2D texture = PuffTexture();
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        // Transparente com mistura alfa normal.
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
    }

    // "Chumaço" de nuvem: 6 círculos juntos, luz de cima (topo claro, base mais escura), borda suave.
    private static Texture2D PuffTexture()
    {
        if (puff != null)
        {
            return puff;
        }

        const int size = 128;
        Vector3[] blobs =
        {
            new Vector3(0.50f, 0.50f, 0.30f), new Vector3(0.33f, 0.45f, 0.20f), new Vector3(0.67f, 0.46f, 0.21f),
            new Vector3(0.42f, 0.66f, 0.20f), new Vector3(0.62f, 0.64f, 0.19f), new Vector3(0.50f, 0.32f, 0.19f),
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float inside = 0f;   // > 0 dentro da nuvem (distância até a borda)
                float light = 0f;    // luz de cima, do círculo mais "por dentro"
                foreach (Vector3 b in blobs)
                {
                    float dx = u - b.x, dy = v - b.y;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = b.z - d;
                    if (edge > inside)
                    {
                        inside = edge;
                        light = Mathf.Clamp01(0.5f + dy / b.z * 0.5f - dx / b.z * 0.15f); // topo-esquerda mais claro
                    }
                }

                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(inside / 0.035f));
                float shade = Mathf.Lerp(0.72f, 1f, light);
                byte c = (byte)Mathf.RoundToInt(shade * 255f);
                pixels[y * size + x] = new Color32(c, c, c, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        puff = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Poeira_Chumaco", wrapMode = TextureWrapMode.Clamp };
        puff.SetPixels32(pixels);
        puff.Apply(true, true);
        return puff;
    }
}
