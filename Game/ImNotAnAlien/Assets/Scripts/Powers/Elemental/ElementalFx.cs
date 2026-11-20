using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Os três elementos do "Poder Elemental" (Missão 21). A ordem é a da troca (roda do mouse / Q).
public enum ElementKind
{
    Fire = 0,
    Ice = 1,
    Lightning = 2,
}

// Fábrica da ARTE dos poderes elementais: cores, materiais, malhas, partículas, clarões e marcas no chão.
// Tudo é criado por código (nada na cena). As texturas vêm de Resources/Elemental (feitas por
// Art/Elemental/make_elemental_art.py); se faltarem, um brilho redondo gerado na hora entra no lugar.
// O material usa o shader "ImNotAnAlien/Elemental FX" (Assets/Shaders/ElementalFx.shader).
public static class ElementalFx
{
    public const string ShaderName = "ImNotAnAlien/Elemental FX";

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int RimColorId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
    // Materiais guardados (18/11: chaves sem montar texto a cada efeito, para não gerar lixo de memória).
    private static readonly Dictionary<(string, int), Material> AdditiveMaterials = new Dictionary<(string, int), Material>();
    private static readonly Dictionary<string, Material> BlendedMaterials = new Dictionary<string, Material>();
    private static readonly Dictionary<string, Material> SolidMaterials = new Dictionary<string, Material>();
    // Degradês e curvas repetidos: feitos uma vez e reaproveitados.
    private static readonly Dictionary<(Color, Color, float, float, float), Gradient> Fades = new Dictionary<(Color, Color, float, float, float), Gradient>();
    private static readonly Dictionary<(float, float, float), AnimationCurve> Curves = new Dictionary<(float, float, float), AnimationCurve>();
    private static Texture2D fallbackTexture;
    private static Mesh crystalMesh;
    private static Mesh sphereMesh;
    private static Mesh cubeMesh;
    private static bool warnedShader;

    // ---------- Caixa de efeitos (reaproveitamento, 18/11) ----------
    // Em vez de criar um objeto com partículas a cada efeito e destruir no fim, os efeitos que terminam
    // voltam para esta caixa (desligados) e são usados de novo no próximo. Menos lixo de memória = menos
    // travadinhas na briga. Ficam dentro do objeto "Efeitos_Caixa" da cena.
    private const int PoolCap = 240;          // mais que isso guardado: o excedente é destruído
    private static readonly Stack<ParticleSystem> FreeSystems = new Stack<ParticleSystem>();
    private static Transform poolRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Textures.Clear();
        AdditiveMaterials.Clear();
        BlendedMaterials.Clear();
        SolidMaterials.Clear();
        Fades.Clear();
        Curves.Clear();
        FreeSystems.Clear();
        poolRoot = null;
        fallbackTexture = null;
        crystalMesh = null;
        sphereMesh = null;
        cubeMesh = null;
        warnedShader = false;
    }

    // ---------- Cores ----------

    public static string Name(ElementKind kind) => kind == ElementKind.Fire ? "FOGO" : kind == ElementKind.Ice ? "GELO" : "RAIO";

    // Cor principal (ícone, luz, barra de energia).
    public static Color Main(ElementKind kind)
    {
        switch (kind)
        {
            case ElementKind.Fire: return new Color(1f, 0.45f, 0.1f);
            case ElementKind.Ice: return new Color(0.45f, 0.85f, 1f);
            default: return new Color(1f, 0.9f, 0.25f);
        }
    }

    // Miolo, quase branco.
    public static Color Hot(ElementKind kind)
    {
        switch (kind)
        {
            case ElementKind.Fire: return new Color(1f, 0.92f, 0.55f);
            case ElementKind.Ice: return new Color(0.92f, 0.98f, 1f);
            default: return new Color(1f, 1f, 0.85f);
        }
    }

    // Tom mais fundo (fim da labareda, sombra do gelo, borda do raio).
    public static Color Deep(ElementKind kind)
    {
        switch (kind)
        {
            case ElementKind.Fire: return new Color(0.85f, 0.12f, 0.03f);
            case ElementKind.Ice: return new Color(0.15f, 0.45f, 0.9f);
            default: return new Color(1f, 0.55f, 0.05f);
        }
    }

    // ---------- Texturas e materiais ----------

    public static Texture2D Tex(string name)
    {
        if (Textures.TryGetValue(name, out Texture2D texture) && texture != null)
        {
            return texture;
        }

        texture = Resources.Load<Texture2D>("Elemental/" + name);
        if (texture == null)
        {
            texture = Fallback();
        }

        Textures[name] = texture;
        return texture;
    }

    private static Texture2D Fallback()
    {
        if (fallbackTexture != null)
        {
            return fallbackTexture;
        }

        const int size = 64;
        fallbackTexture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Elemental_Brilho", wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        fallbackTexture.SetPixels32(pixels);
        fallbackTexture.Apply(true, true);
        return fallbackTexture;
    }

    private static Shader FxShader()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            if (!warnedShader)
            {
                warnedShader = true;
                Debug.LogWarning("Poderes elementais: shader '" + ShaderName + "' não encontrado (Assets/Shaders/ElementalFx.shader). Usando o de partículas do URP.");
            }

            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
        }

        return shader;
    }

    // Material que SOMA luz (fogo, raio, brilhos). "intensity" acima de 1 estoura para o branco.
    public static Material Additive(string texture, float intensity = 1f)
    {
        (string, int) key = (texture, Mathf.RoundToInt(intensity * 100f));
        if (AdditiveMaterials.TryGetValue(key, out Material material) && material != null)
        {
            return material;
        }

        material = NewMaterial("Elemental_" + texture + "_Soma", Tex(texture), new Color(intensity, intensity, intensity, 1f), true);
        AdditiveMaterials[key] = material;
        return material;
    }

    // Material transparente comum (fumaça, névoa, vapor, marcas escuras).
    public static Material Blended(string texture)
    {
        if (BlendedMaterials.TryGetValue(texture, out Material material) && material != null)
        {
            return material;
        }

        material = NewMaterial("Elemental_" + texture + "_Transp", Tex(texture), Color.white, false);
        BlendedMaterials[texture] = material;
        return material;
    }

    // Material para objetos 3D que brilham: miolo da bola de fogo, cristal de gelo.
    public static Material Solid(string key, Color color, Color rim, float rimPower, bool additive)
    {
        if (SolidMaterials.TryGetValue(key, out Material material) && material != null)
        {
            return material;
        }

        material = NewMaterial("Elemental_S|" + key, Texture2D.whiteTexture, color, additive);
        material.SetColor(RimColorId, rim);
        material.SetFloat(RimPowerId, rimPower);
        material.SetFloat(CullId, (float)CullMode.Back);
        SolidMaterials[key] = material;
        return material;
    }

    private static Material NewMaterial(string name, Texture texture, Color color, bool additive)
    {
        Material material = new Material(FxShader()) { name = name };
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        if (material.HasProperty(ColorId))
        {
            material.SetColor(ColorId, color);
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
        material.SetFloat(DstBlendId, (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        material.SetFloat(ZWriteId, 0f);
        material.SetFloat(CullId, (float)CullMode.Off);
        material.renderQueue = (int)RenderQueue.Transparent + (additive ? 20 : 10);
        return material;
    }

    // ---------- Malhas ----------

    public static Mesh Sphere
    {
        get
        {
            if (sphereMesh == null)
            {
                sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            }

            return sphereMesh;
        }
    }

    public static Mesh Cube
    {
        get
        {
            if (cubeMesh == null)
            {
                cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }

            return cubeMesh;
        }
    }

    // Cristal de gelo: prisma de 6 lados com ponta em cima. A base fica na origem e a ponta em Y = 1.
    public static Mesh Crystal
    {
        get
        {
            if (crystalMesh == null)
            {
                crystalMesh = BuildCrystal();
            }

            return crystalMesh;
        }
    }

    private static Mesh BuildCrystal()
    {
        const int sides = 6;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 outward = (a + b + c) / 3f;
            outward.y = 0f;
            if (Vector3.Dot(normal, outward) < 0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }

            int index = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            for (int i = 0; i < 3; i++)
            {
                normals.Add(normal);
            }

            uvs.Add(new Vector2(0.5f, 0.5f));
            uvs.Add(new Vector2(0.5f, 0.5f));
            uvs.Add(new Vector2(0.5f, 0.5f));
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
        }

        Vector3 tip = new Vector3(0.03f, 1f, -0.02f);
        Vector3 foot = Vector3.zero;
        Vector3[] low = new Vector3[sides];
        Vector3[] high = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            float radius = i % 2 == 0 ? 0.2f : 0.165f;
            low[i] = new Vector3(Mathf.Cos(angle) * radius * 0.8f, 0.12f, Mathf.Sin(angle) * radius * 0.8f);
            high[i] = new Vector3(Mathf.Cos(angle) * radius, 0.66f, Mathf.Sin(angle) * radius);
        }

        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Triangle(foot, low[i], low[next]);
            Triangle(low[i], high[i], high[next]);
            Triangle(low[i], high[next], low[next]);
            Triangle(high[i], tip, high[next]);
        }

        Mesh mesh = new Mesh { name = "Cristal_Gelo" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Material IceMaterial() =>
        Solid("Gelo", new Color(0.55f, 0.85f, 1f, 0.55f), new Color(1.6f, 2.2f, 2.6f, 0.9f), 2.2f, false);

    // Objeto 3D simples com um material dos efeitos (sem colisão e sem sombra).
    public static GameObject MeshObject(string name, Mesh mesh, Material material, Transform parent)
    {
        GameObject go = new GameObject(name);
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    // ---------- Partículas ----------

    // Receita de um emissor. Só o que foge do padrão precisa ser preenchido.
    public sealed class Spec
    {
        public float rate;                      // partículas por segundo (emissor contínuo)
        public int burst;                       // quantas de uma vez (explosão)
        public Vector2 life = new Vector2(0.4f, 0.8f);
        public Vector2 speed = new Vector2(0f, 1f);
        public Vector2 size = new Vector2(0.2f, 0.4f);
        public Color colorA = Color.white;      // cor ao nascer: sorteada entre A e B
        public Color colorB = Color.white;
        public Gradient fade;                   // cor ao longo da vida (multiplica); vazio = some no fim
        public AnimationCurve sizeCurve;        // tamanho ao longo da vida; vazio = encolhe
        public float gravity;                   // negativo = sobe
        public ParticleSystemShapeType shape = ParticleSystemShapeType.Sphere;
        public float radius = 0.1f;
        public float angle = 20f;               // abertura (cone)
        public Vector3 boxSize = Vector3.one;   // (caixa)
        public bool local;                      // acompanha o dono (senão fica no mundo)
        public bool loop;
        public float duration = 1f;
        public int max = 200;
        public Mesh mesh;                       // partícula 3D (cubinho, cristal)
        public float stretch;                   // esticada na direção do movimento (faísca)
        public bool horizontal;                 // deitada no chão (anel, marca)
        public float tilt = -1f;                // < 0: giro sorteado; senão graus máximos para cada lado
        public float spin;                      // giro ao longo da vida (graus/s, para os dois lados)
        public float damping;                   // freio (0 a 1)
        public float noise;                     // turbulência
        public bool destroyWhenDone = true;
        public float sortFudge;
    }

    // Degradê de cor ao longo da vida. Guardado: o mesmo pedido devolve o mesmo degradê (o sistema de
    // partículas copia os valores, então dá para dividir entre vários efeitos).
    public static Gradient Fade(Color start, Color end, float alphaIn = 0.08f, float alphaHold = 0.5f, float alphaStart = 1f)
    {
        (Color, Color, float, float, float) key = (start, end, alphaIn, alphaHold, alphaStart);
        if (Fades.TryGetValue(key, out Gradient cached))
        {
            return cached;
        }

        if (Fades.Count > 512)
        {
            Fades.Clear(); // cores sempre diferentes (não deveria acontecer): não deixa crescer sem fim
        }

        Gradient gradient = new Gradient();
        Fades[key] = gradient;
        gradient.SetKeys(
            new[] { new GradientColorKey(start, 0f), new GradientColorKey(end, 1f) },
            new[]
            {
                new GradientAlphaKey(alphaIn <= 0f ? alphaStart : 0f, 0f),
                new GradientAlphaKey(alphaStart, Mathf.Max(0.001f, alphaIn)),
                new GradientAlphaKey(alphaStart, Mathf.Max(alphaIn + 0.001f, alphaHold)),
                new GradientAlphaKey(0f, 1f),
            });
        return gradient;
    }

    public static AnimationCurve Curve(float start, float middle, float end)
    {
        (float, float, float) key = (start, middle, end);
        if (!Curves.TryGetValue(key, out AnimationCurve curve))
        {
            if (Curves.Count > 512)
            {
                Curves.Clear();
            }

            curve = new AnimationCurve(new Keyframe(0f, start), new Keyframe(0.35f, middle), new Keyframe(1f, end));
            Curves[key] = curve;
        }

        return curve;
    }

    public static ParticleSystem Emitter(string name, Transform parent, Vector3 position, Quaternion rotation, Material material, Spec spec)
    {
        ParticleSystem system = Take(name);
        Transform t = system.transform;
        if (parent != null)
        {
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = rotation;
        }
        else
        {
            if (t.parent != poolRoot)
            {
                t.SetParent(poolRoot, false);
            }

            t.SetPositionAndRotation(position, rotation);
        }

        t.localScale = Vector3.one;
        ParticleSystem.MainModule before = system.main;
        before.stopAction = ParticleSystemStopAction.None; // parar agora não pode avisar "acabei"
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Configure(system, material, spec);

        // Efeito de uma vez só: avisa quando acabar (volta para a caixa). Rede de segurança: um pouco
        // depois do tempo máximo que ele poderia durar, volta de qualquer jeito.
        bool once = spec.destroyWhenDone && !spec.loop;
        system.GetComponent<ElementalFxPooled>().Arm(once ? Mathf.Max(0.05f, spec.duration) + spec.life.y + 1f : 0f);

        system.gameObject.SetActive(true);
        system.Play();
        return system;
    }

    // Monta TODAS as opções do sistema de partículas a partir da receita. Como o sistema pode ter sido
    // usado antes por outro efeito, tudo que um efeito liga, aqui também é desligado quando não usado.
    private static void Configure(ParticleSystem system, Material material, Spec spec)
    {
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.05f, spec.duration);
        main.loop = spec.loop;
        main.startLifetime = new ParticleSystem.MinMaxCurve(spec.life.x, spec.life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(spec.speed.x, spec.speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(spec.size.x, spec.size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(spec.colorA, spec.colorB);
        main.gravityModifier = spec.gravity;
        main.simulationSpace = spec.local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.maxParticles = spec.max;
        main.stopAction = spec.destroyWhenDone && !spec.loop ? ParticleSystemStopAction.Callback : ParticleSystemStopAction.None;
        main.startRotation3D = spec.mesh != null;
        if (spec.mesh != null)
        {
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }
        else if (spec.tilt < 0f)
        {
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }
        else
        {
            main.startRotation = new ParticleSystem.MinMaxCurve(-spec.tilt * Mathf.Deg2Rad, spec.tilt * Mathf.Deg2Rad);
        }

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = spec.rate;
        if (spec.burst > 0)
        {
            emission.burstCount = 1; // sem criar lista nova a cada efeito
            emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.Min(spec.burst, short.MaxValue)));
        }
        else
        {
            emission.burstCount = 0;
        }

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = spec.shape;
        shape.radius = Mathf.Max(0.001f, spec.radius);
        shape.angle = spec.angle;
        shape.scale = spec.shape == ParticleSystemShapeType.Box ? spec.boxSize : Vector3.one;

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(spec.fade ?? Fade(Color.white, Color.white));

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, spec.sizeCurve ?? DefaultShrink());

        ParticleSystem.RotationOverLifetimeModule rotation3 = system.rotationOverLifetime;
        rotation3.enabled = spec.spin > 0f;
        if (spec.spin > 0f)
        {
            float spin = spec.spin * Mathf.Deg2Rad;
            rotation3.separateAxes = spec.mesh != null;
            if (spec.mesh != null)
            {
                rotation3.x = new ParticleSystem.MinMaxCurve(-spin, spin);
                rotation3.y = new ParticleSystem.MinMaxCurve(-spin, spin);
            }

            rotation3.z = new ParticleSystem.MinMaxCurve(-spin, spin);
        }

        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = spec.damping > 0f;
        if (spec.damping > 0f)
        {
            limit.limit = 0f;
            limit.dampen = Mathf.Clamp01(spec.damping);
        }

        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = spec.noise > 0f;
        if (spec.noise > 0f)
        {
            noise.strength = spec.noise;
            noise.frequency = 0.9f;
            noise.scrollSpeed = 0.6f;
        }

        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingFudge = spec.sortFudge;
        renderer.enableGPUInstancing = false; // o shader dos efeitos não usa instância de partículas
        if (spec.mesh != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = spec.mesh;
        }
        else
        {
            renderer.mesh = null;
            if (spec.horizontal)
            {
                renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            }
            else if (spec.stretch > 0f)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 1f;
                renderer.velocityScale = spec.stretch;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
        }
    }

    private static AnimationCurve defaultShrink;

    private static AnimationCurve DefaultShrink()
    {
        if (defaultShrink == null)
        {
            defaultShrink = AnimationCurve.Linear(0f, 1f, 1f, 0.15f);
        }

        return defaultShrink;
    }

    // Emissor de uma vez só no mundo (volta para a caixa sozinho quando termina).
    public static ParticleSystem Burst(string name, Vector3 position, Quaternion rotation, Material material, Spec spec)
    {
        spec.loop = false;
        spec.destroyWhenDone = true;
        return Emitter(name, null, position, rotation, material, spec);
    }

    // Para de emitir e volta para a caixa quando as partículas que sobraram acabarem (rastro do projétil,
    // chamas de quem parou de queimar).
    public static void Release(ParticleSystem system, float extra = 0f)
    {
        if (system == null)
        {
            return;
        }

        float wait = system.main.startLifetime.constantMax + 0.3f + extra;
        if (!system.TryGetComponent(out ElementalFxPooled tag) || PoolRoot() == null)
        {
            // Não veio da caixa: some como antes.
            system.transform.SetParent(null, true);
            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Object.Destroy(system.gameObject, wait);
            return;
        }

        if (tag.InBox)
        {
            return; // já foi devolvido antes
        }

        system.transform.SetParent(poolRoot, true);
        ParticleSystem.MainModule main = system.main;
        main.stopAction = ParticleSystemStopAction.Callback;
        system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        tag.Arm(wait);
    }

    // Devolve para a caixa NA HORA (some na hora, sem esperar as partículas).
    public static void Recycle(ParticleSystem system)
    {
        if (system == null)
        {
            return;
        }

        if (!system.TryGetComponent(out ElementalFxPooled tag))
        {
            Object.Destroy(system.gameObject);
            return;
        }

        if (tag.InBox)
        {
            return; // já está guardado
        }

        ParticleSystem.MainModule main = system.main;
        main.stopAction = ParticleSystemStopAction.None;
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (poolRoot == null || FreeSystems.Count >= PoolCap)
        {
            Object.Destroy(system.gameObject);
            return;
        }

        tag.Store();
        system.transform.SetParent(poolRoot, false);
        system.gameObject.SetActive(false);
        FreeSystems.Push(system);
    }

    // Enche a caixa antes da briga (no carregamento), para o primeiro tiro não travar criando tudo.
    public static void Prewarm(int count)
    {
        Transform root = PoolRoot();
        for (int i = FreeSystems.Count; i < count && i < PoolCap; i++)
        {
            ParticleSystem system = NewPooledSystem("Efeito_Guardado", root);
            system.GetComponent<ElementalFxPooled>().Store();
            system.gameObject.SetActive(false);
            FreeSystems.Push(system);
        }
    }

    private static Transform PoolRoot()
    {
        if (poolRoot == null)
        {
            FreeSystems.Clear(); // a cena mudou: os guardados foram embora junto com ela
            poolRoot = new GameObject("Efeitos_Caixa").transform;
        }

        return poolRoot;
    }

    private static ParticleSystem Take(string name)
    {
        Transform root = PoolRoot();
        while (FreeSystems.Count > 0)
        {
            ParticleSystem pooled = FreeSystems.Pop();
            if (pooled != null)
            {
                pooled.gameObject.name = name;
                return pooled;
            }
        }

        return NewPooledSystem(name, root);
    }

    private static ParticleSystem NewPooledSystem(string name, Transform root)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        ParticleSystem system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        go.AddComponent<ElementalFxPooled>();
        return system;
    }

    // Clarão de luz que apaga sozinho.
    public static void Flash(Vector3 position, Color color, float intensity, float range, float duration)
    {
        ElementalFxRunner.Instance.AddFlash(position, color, intensity, range, duration);
    }

    // Marca deitada no chão (anel de choque, rachadura de gelo, chamuscado).
    public static void GroundMark(Vector3 point, string texture, bool additive, Color color, float size, float seconds, bool expand)
    {
        Spec spec = new Spec
        {
            burst = 1,
            life = new Vector2(seconds, seconds),
            speed = Vector2.zero,
            size = new Vector2(size, size),
            colorA = color,
            colorB = color,
            radius = 0.001f,
            horizontal = true,
            duration = 0.1f,
            max = 2,
            fade = Fade(Color.white, Color.white, expand ? 0f : 0.03f, expand ? 0.15f : 0.7f),
            sizeCurve = expand ? AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f) : AnimationCurve.Linear(0f, 1f, 1f, 1f),
            sortFudge = 5f,
        };
        Burst("Elemental_Marca", point + Vector3.up * 0.06f, Quaternion.identity, additive ? Additive(texture, 1.5f) : Blended(texture), spec);
    }

    // ---------- Efeitos prontos ----------

    // Clarão curto na mão ao disparar.
    public static void Muzzle(ElementKind kind, Vector3 position, Vector3 direction, float power)
    {
        Quaternion look = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        Color main = Main(kind);
        Burst("Elemental_Clarao_Mao", position, look, Additive("fx_glow", 2f), new Spec
        {
            burst = 1,
            life = new Vector2(0.12f, 0.12f),
            speed = Vector2.zero,
            size = Vector2.one * Mathf.Lerp(0.9f, 1.8f, power),
            colorA = main,
            colorB = main,
            radius = 0.001f,
            sizeCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.3f),
            fade = Fade(Hot(kind), main, 0f, 0.2f),
        });
        Burst("Elemental_Faiscas_Mao", position, look, Additive("fx_spark", 2f), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(6f, 16f, power)),
            life = new Vector2(0.12f, 0.3f),
            speed = new Vector2(3f, 9f),
            size = new Vector2(0.12f, 0.3f),
            colorA = Hot(kind),
            colorB = main,
            shape = ParticleSystemShapeType.Cone,
            angle = 28f,
            radius = 0.05f,
            stretch = 0.06f,
            damping = 0.12f,
        });
        Flash(position, main, Mathf.Lerp(3f, 7f, power), Mathf.Lerp(5f, 9f, power), 0.18f);
    }

    // Não tinha energia: uma fumacinha e pronto.
    public static void Fizzle(ElementKind kind, Vector3 position)
    {
        Burst("Elemental_Falhou", position, Quaternion.identity, Blended("fx_smoke"), new Spec
        {
            burst = 5,
            life = new Vector2(0.4f, 0.7f),
            speed = new Vector2(0.2f, 0.8f),
            size = new Vector2(0.15f, 0.3f),
            colorA = Color.Lerp(Main(kind), Color.gray, 0.6f),
            colorB = new Color(0.5f, 0.5f, 0.5f),
            gravity = -0.2f,
            fade = Fade(Color.white, Color.white, 0.1f, 0.3f, 0.5f),
            sizeCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f),
        });
    }

    // Vapor (fogo na água, gelo derretendo).
    public static void Steam(Vector3 position, float size)
    {
        Burst("Elemental_Vapor", position, Quaternion.identity, Blended("fx_smoke"), new Spec
        {
            burst = Mathf.RoundToInt(10f * size),
            life = new Vector2(0.8f, 1.6f),
            speed = new Vector2(0.4f, 1.6f),
            size = new Vector2(0.6f, 1.2f) * size,
            colorA = new Color(1f, 1f, 1f),
            colorB = new Color(0.85f, 0.92f, 1f),
            gravity = -0.35f,
            radius = 0.3f * size,
            fade = Fade(Color.white, Color.white, 0.1f, 0.3f, 0.45f),
            sizeCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f),
            spin = 40f,
            max = 40,
        });
    }

    // Explosão de fogo: clarão, labaredas para fora, brasas em cubinhos, fumaça e anel no chão.
    public static void FireBlast(Vector3 point, float radius, float power)
    {
        Color main = Main(ElementKind.Fire);
        Color hot = Hot(ElementKind.Fire);
        Color deep = Deep(ElementKind.Fire);

        Burst("Fogo_Clarao", point, Quaternion.identity, Additive("fx_glow", 2.5f), new Spec
        {
            burst = 1,
            life = new Vector2(0.22f, 0.22f),
            speed = Vector2.zero,
            size = Vector2.one * radius * 2.4f,
            colorA = hot,
            colorB = hot,
            radius = 0.001f,
            sizeCurve = AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f),
            fade = Fade(hot, main, 0f, 0.25f),
        });
        Burst("Fogo_Labaredas", point, Quaternion.identity, Additive("fx_flame", 1.6f), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(16f, 42f, power)),
            life = new Vector2(0.35f, 0.75f),
            speed = new Vector2(radius * 1.2f, radius * 3.2f),
            size = new Vector2(0.5f, 1.1f) * Mathf.Lerp(0.8f, 1.5f, power),
            colorA = hot,
            colorB = main,
            radius = radius * 0.2f,
            gravity = -0.35f,
            damping = 0.14f,
            tilt = 35f,
            fade = Fade(Color.white, deep, 0.05f, 0.35f),
            sizeCurve = Curve(0.5f, 1.2f, 0.3f),
            max = 80,
        });
        Burst("Fogo_Brasas", point, Quaternion.identity, Additive("fx_glow", 2f), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(14f, 40f, power)),
            life = new Vector2(0.5f, 1.2f),
            speed = new Vector2(radius * 1.5f, radius * 4.5f),
            size = new Vector2(0.06f, 0.14f),
            colorA = hot,
            colorB = main,
            radius = radius * 0.15f,
            gravity = 1.4f,
            mesh = Cube,
            spin = 360f,
            fade = Fade(Color.white, deep, 0f, 0.6f),
            max = 80,
        });
        Burst("Fogo_Fumaca", point + Vector3.up * 0.3f, Quaternion.identity, Blended("fx_smoke"), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(6f, 16f, power)),
            life = new Vector2(1.1f, 2.2f),
            speed = new Vector2(0.6f, radius * 0.9f),
            size = new Vector2(1f, 2f) * Mathf.Lerp(0.8f, 1.6f, power),
            colorA = new Color(0.16f, 0.14f, 0.14f),
            colorB = new Color(0.32f, 0.28f, 0.26f),
            radius = radius * 0.3f,
            gravity = -0.25f,
            damping = 0.08f,
            spin = 30f,
            fade = Fade(Color.white, Color.white, 0.15f, 0.35f, 0.6f),
            sizeCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.7f),
            max = 40,
            sortFudge = 2f,
        });
        GroundMark(point, "fx_ring", true, main, radius * 2.3f, 0.35f, true);
        GroundMark(point, "fx_crack", false, new Color(0.06f, 0.04f, 0.03f, 0.85f), radius * 1.5f, 6f, false);
        Flash(point + Vector3.up * 0.5f, main, Mathf.Lerp(8f, 16f, power), radius * 3.5f, 0.4f);
        CameraShake.AddTrauma(Mathf.Lerp(0.15f, 0.5f, power) * Nearness(point, 40f));
    }

    // Estouro de gelo: lascas de cristal, névoa fria, flocos e anel no chão.
    public static void IceBurst(Vector3 point, float radius, float power)
    {
        Color main = Main(ElementKind.Ice);
        Color hot = Hot(ElementKind.Ice);

        Burst("Gelo_Clarao", point, Quaternion.identity, Additive("fx_glow", 1.6f), new Spec
        {
            burst = 1,
            life = new Vector2(0.2f, 0.2f),
            speed = Vector2.zero,
            size = Vector2.one * Mathf.Max(1.2f, radius * 1.8f),
            colorA = main,
            colorB = main,
            radius = 0.001f,
            sizeCurve = AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f),
            fade = Fade(hot, main, 0f, 0.25f),
        });
        Burst("Gelo_Lascas", point, Quaternion.identity, IceMaterial(), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(10f, 28f, power)),
            life = new Vector2(0.6f, 1.2f),
            speed = new Vector2(3f, 9f) * Mathf.Lerp(1f, 1.5f, power),
            size = new Vector2(0.12f, 0.34f),
            colorA = Color.white,
            colorB = new Color(0.8f, 0.95f, 1f),
            radius = 0.15f,
            gravity = 1.6f,
            mesh = Crystal,
            spin = 420f,
            fade = Fade(Color.white, Color.white, 0f, 0.7f),
            sizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.5f),
            max = 60,
        });
        Burst("Gelo_Nevoa", point, Quaternion.identity, Blended("fx_smoke"), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(6f, 18f, power)),
            life = new Vector2(0.8f, 1.6f),
            speed = new Vector2(0.5f, Mathf.Max(1.5f, radius * 1.2f)),
            size = new Vector2(0.8f, 1.6f) * Mathf.Lerp(0.8f, 1.5f, power),
            colorA = new Color(0.85f, 0.95f, 1f),
            colorB = Color.white,
            radius = 0.3f,
            gravity = 0.05f,
            damping = 0.1f,
            spin = 25f,
            fade = Fade(Color.white, Color.white, 0.1f, 0.3f, 0.4f),
            sizeCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f),
            max = 40,
        });
        Burst("Gelo_Flocos", point, Quaternion.identity, Additive("fx_flake", 1.4f), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(8f, 24f, power)),
            life = new Vector2(0.8f, 1.6f),
            speed = new Vector2(1f, 5f),
            size = new Vector2(0.1f, 0.26f),
            colorA = hot,
            colorB = main,
            radius = 0.3f,
            gravity = 0.25f,
            damping = 0.1f,
            spin = 120f,
            noise = 0.6f,
            max = 60,
        });
        if (radius > 0.5f)
        {
            GroundMark(point, "fx_ring", true, main, radius * 2.2f, 0.4f, true);
            GroundMark(point, "fx_crack", true, new Color(0.7f, 0.9f, 1f, 0.8f), radius * 1.6f, 5f, false);
        }

        Flash(point + Vector3.up * 0.4f, main, Mathf.Lerp(3f, 8f, power), Mathf.Max(5f, radius * 3f), 0.3f);
        CameraShake.AddTrauma(Mathf.Lerp(0.06f, 0.3f, power) * Nearness(point, 35f));
    }

    // Faíscas onde o raio bate.
    public static void BoltHit(Vector3 point, Vector3 normal, float power)
    {
        Color main = Main(ElementKind.Lightning);
        Color hot = Hot(ElementKind.Lightning);
        Quaternion look = normal.sqrMagnitude > 0.001f ? Quaternion.LookRotation(normal) : Quaternion.identity;

        Burst("Raio_Clarao", point, Quaternion.identity, Additive("fx_glow", 3f), new Spec
        {
            burst = 1,
            life = new Vector2(0.14f, 0.14f),
            speed = Vector2.zero,
            size = Vector2.one * Mathf.Lerp(1.4f, 3.2f, power),
            colorA = hot,
            colorB = hot,
            radius = 0.001f,
            fade = Fade(hot, main, 0f, 0.3f),
            sizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.6f),
        });
        Burst("Raio_Faiscas", point, look, Additive("fx_spark", 2.5f), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(14f, 36f, power)),
            life = new Vector2(0.2f, 0.55f),
            speed = new Vector2(4f, 13f),
            size = new Vector2(0.14f, 0.36f),
            colorA = hot,
            colorB = main,
            shape = ParticleSystemShapeType.Hemisphere,
            radius = 0.1f,
            gravity = 1.2f,
            stretch = 0.05f,
            damping = 0.06f,
            max = 60,
        });
        Burst("Raio_Estrelas", point, Quaternion.identity, Additive("fx_spark", 2f), new Spec
        {
            burst = Mathf.RoundToInt(Mathf.Lerp(3f, 7f, power)),
            life = new Vector2(0.1f, 0.25f),
            speed = new Vector2(0f, 1f),
            size = new Vector2(0.6f, 1.4f),
            colorA = hot,
            colorB = main,
            radius = 0.35f,
            spin = 200f,
        });
        Flash(point, main, Mathf.Lerp(6f, 14f, power), Mathf.Lerp(7f, 14f, power), 0.2f);
        CameraShake.AddTrauma(Mathf.Lerp(0.08f, 0.35f, power) * Nearness(point, 40f));
    }

    // Gelo quebrando em volta de um corpo (congelado que levou uma pancada ou morreu).
    public static void IceShatter(Vector3 center, float size)
    {
        IceBurst(center, 0f, Mathf.Clamp01(0.5f + size * 0.25f));
        ElementalFxRunner.Play(ElementalFxRunner.Sound.Shatter, center, 0.9f);
    }

    // 1 perto da câmera, 0 a partir de "range" metros (tremida de tela, volume).
    public static float Nearness(Vector3 point, float range)
    {
        Camera cam = Camera.main;
        return cam == null ? 1f : Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, point) / range);
    }
}
