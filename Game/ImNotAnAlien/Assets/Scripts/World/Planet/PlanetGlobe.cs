using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Mapa planetário (Missão 19, versão 2 — esboço aprovado pelo autor). Tecla M abre e fecha (o jogo pausa).
// - Globo 3D com as 16 regiões orgânicas pintadas pela cor do bioma, divisas creme, rio, polos e
//   um ícone 3D por região (montanhas, árvores, dunas...).
// - Marcas: você (seta laranja apontando para onde o personagem olha), o Vilarejo, as bases (torres
//   acesas se visitadas, apagadas se não) e o marcador de destino. Linha pontilhada até o que estiver selecionado.
// - Clique seleciona (base ou ponto); o painel da direita mostra nome, distância, progresso da região
//   e do setor, e os botões MARCAR DESTINO e VIAJAR (só de dentro de uma base, para bases visitadas).
// - Rodinha aproxima: de perto aparecem os selos dos setores (dourado com ✓ = limpo; anel = em andamento).
// - Arrastar gira; botão direito tira o marcador.
// - Fora do mapa, o marcador aparece na tela com a distância (some ao chegar).
// Textos com a fonte Fredoka (copiada para Resources pela ferramenta "Preparar Globo (Missao 19)").
[DisallowMultipleComponent]
public sealed class PlanetGlobe : MonoBehaviour
{
    public static PlanetGlobe Instance { get; private set; }

    private const int GlobeLayer = 31;
    private const float Radius = 1f;
    private const int PixelsPerBlock = 8; // texto do mapa mais fino: divisas curvas

    private static readonly Color Cream = new Color32(0xF3, 0xE3, 0xC8, 255);
    private static readonly Color Ink = new Color32(0x3B, 0x2A, 0x22, 255);
    private static readonly Color Coral = new Color32(0xE8, 0x73, 0x5A, 255);
    private static readonly Color CoralDark = new Color32(0x8C, 0x3A, 0x2A, 255);
    private static readonly Color Panel = new Color32(0x1F, 0x4A, 0x40, 255);
    private static readonly Color Mint = new Color32(0x4B, 0xE3, 0xA0, 255);
    private static readonly Color MintSoft = new Color32(0xBF, 0xE6, 0xD6, 255);
    private static readonly Color Night = new Color32(0x12, 0x16, 0x2B, 255);
    private static readonly Color Gold = new Color32(0xFF, 0xD2, 0x5A, 255);
    private static readonly Color Arrow = new Color32(0xFF, 0x6A, 0x4D, 255);
    private static readonly Color Village = new Color32(0x9B, 0x6B, 0xD6, 255);

    [SerializeField] private float dragSpeed = 0.25f;
    [SerializeField] private Vector2 zoomRange = new Vector2(1.45f, 4.2f);
    [Tooltip("Abaixo desta distância da câmera aparecem os selos dos setores.")]
    [SerializeField] private float sectorZoom = 2.1f;
    [Tooltip("Chegou a esta distância (m) do marcador: ele some.")]
    [SerializeField, Min(1f)] private float arriveDistance = 20f;

    private Transform player;
    private Camera mainCamera;
    private bool open;
    private float yaw;
    private float pitch;
    private float distance = 3f;
    private Vector2 pressPosition;
    private bool dragging;
    private bool previousFog;
    private string notice;
    private float noticeUntil;

    // Seleção (clique): uma base ou um ponto do planeta.
    private bool hasSelection;
    private Vector2 selectedPosition;
    private int selectedBase = -1;

    // ----- 3D -----
    private GameObject root;
    private Transform globe;
    private Camera globeCamera;
    private Texture2D texture;
    private Transform playerMark;
    private Transform waypointMark;
    private Transform selectionRing;
    private readonly List<Transform> pathDots = new List<Transform>();
    private readonly List<(Transform mark, Renderer[] parts)> baseMarks = new List<(Transform, Renderer[])>();
    private Material litBase;
    private Material unlitBase;

    // ----- Tela (uGUI) -----
    private Canvas canvas;
    private Font fontBold;
    private Font fontSemi;
    private Font fontRegular;
    private Sprite pill;
    private Sprite roundBox;
    private Sprite circle;
    private RectTransform labelLayer;
    private readonly List<(RectTransform label, Vector2 planetPosition, CanvasGroup group)> regionLabels = new List<(RectTransform, Vector2, CanvasGroup)>();
    private readonly List<(RectTransform label, int baseIndex, Text text, Image back, CanvasGroup group)> baseLabels = new List<(RectTransform, int, Text, Image, CanvasGroup)>();
    private readonly List<(RectTransform badge, int sector, Image fill, Image check, CanvasGroup group)> sectorBadges = new List<(RectTransform, int, Image, Image, CanvasGroup)>();
    private RectTransform youLabel;
    private CanvasGroup youGroup;
    private Text panelKicker;
    private Text panelTitle;
    private Text panelDistance;
    private Text panelDistanceNote;
    private Text regionName;
    private Text regionCount;
    private Image regionBar;
    private Text sectorName;
    private Text sectorCount;
    private Button markButton;
    private Text markLabel;
    private Button travelButton;
    private Text travelLabel;
    private Image panelIcon;
    private GameObject hintSelect;

    public bool IsOpen => open;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (open)
        {
            GamePause.Resume();
        }
    }

    private void Start()
    {
        PlayerHealth health = PlayerHealth.Instance;
        player = health != null ? health.transform : null;
        mainCamera = Camera.main;
        fontBold = LoadFont("Fonts/Fredoka-Bold");
        fontSemi = LoadFont("Fonts/Fredoka-SemiBold");
        fontRegular = LoadFont("Fonts/Fredoka-Medium");
        StartPainting();
    }

    private static Font LoadFont(string path)
    {
        Font font = Resources.Load<Font>(path);
        return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || player == null || !Planet.Active)
        {
            return;
        }

        if (keyboard.mKey.wasPressedThisFrame)
        {
            if (open)
            {
                Close();
            }
            else if (!GamePause.IsPaused && (PlayerHealth.Instance == null || !PlayerHealth.Instance.IsDead))
            {
                Open();
            }
        }
        else if (open && keyboard.escapeKey.wasPressedThisFrame)
        {
            Close();
        }

        if (open)
        {
            UpdateControls();
            UpdateScreen();
            if (cloudPivot != null)
            {
                cloudPivot.localRotation = Quaternion.AngleAxis(Time.unscaledTime * 1.5f, Vector3.up); // nuvens andando devagar
            }

            UpdateDetail();
        }
        else
        {
            CheckArrival();
        }
    }

    // ---------- Abrir e fechar ----------

    private void Open()
    {
        if (root == null)
        {
            BuildGlobe();
            BuildScreen();
        }

        PaintTexture();
        RefreshMarks();

        Vector2 here = Planet.PlanetPosition(WorldOrigin.ToLogical(player.position));
        Vector2 latLong = LatLongOf(here);
        yaw = latLong.y;
        pitch = Mathf.Clamp(-latLong.x, -80f, 80f);
        distance = 3f;

        // Começa com o marcador selecionado (se houver), senão sem seleção.
        SaveData data = SaveManager.Instance != null ? SaveManager.Instance.Data : null;
        hasSelection = data != null && data.hasWaypoint;
        selectedPosition = hasSelection ? data.waypoint : here;
        selectedBase = -1;

        EnsureEventSystem();
        root.transform.position = player.position + Vector3.down * 5000f; // longe de tudo
        root.SetActive(true);
        canvas.gameObject.SetActive(true);
        previousFog = RenderSettings.fog;
        RenderSettings.fog = false;
        IsolateLighting(true);
        if (GameHud.Instance != null)
        {
            GameHud.Instance.SetVisible(false);
        }

        GamePause.Pause();
        open = true;
        RefreshPanel();
    }

    private void Close()
    {
        open = false;
        if (root != null)
        {
            root.SetActive(false);
            canvas.gameObject.SetActive(false);
        }

        RenderSettings.fog = previousFog;
        IsolateLighting(false);
        if (GameHud.Instance != null)
        {
            GameHud.Instance.SetVisible(true);
        }

        GamePause.Resume();
    }

    // Só a luz do globo ilumina o globo (o sol do mundo e a luz ambiente deixavam as cores lavadas).
    // (o mundo fica escondido atrás do mapa, então as outras luzes podem ser desligadas enquanto ele está aberto)
    private readonly List<Light> otherLights = new List<Light>();
    private UnityEngine.Rendering.AmbientMode previousAmbientMode;
    private Color previousAmbient;
    private UnityEngine.Rendering.SphericalHarmonicsL2 previousProbe;

    private void IsolateLighting(bool isolate)
    {
        if (isolate)
        {
            otherLights.Clear();
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.transform.IsChildOf(root.transform))
                {
                    continue;
                }

                if (light.enabled)
                {
                    otherLights.Add(light);
                    light.enabled = false;
                }
            }

            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.52f);
            // O URP usa a sonda ambiente (calculada do céu), não a ambientLight: troca pela cor fixa.
            previousProbe = RenderSettings.ambientProbe;
            UnityEngine.Rendering.SphericalHarmonicsL2 flat = new UnityEngine.Rendering.SphericalHarmonicsL2();
            flat.AddAmbientLight(new Color(0.42f, 0.44f, 0.52f));
            RenderSettings.ambientProbe = flat;
        }
        else
        {
            foreach (Light light in otherLights)
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }

            otherLights.Clear();
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbient;
            RenderSettings.ambientProbe = previousProbe;
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    // ================================================================
    // 3D: globo, ícones e marcas
    // ================================================================

    private void BuildGlobe()
    {
        root = new GameObject("Globo_Planeta");
        root.layer = GlobeLayer;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            lit = CurvedHorizon.LitShader();
        }

        litBase = new Material(lit);
        unlitBase = litBase;

        // Relevo 3D (Missão 20): espera as alturas do planeta (calculadas junto com a pintura) e
        // levanta/abaixa a superfície — montanhas saltam, mar e lagos ficam mais baixos.
        StartPainting();
        if (paintTask != null)
        {
            paintTask.Wait();
        }
        else
        {
            reliefCache = null; // fora do planeta: globo liso
        }

        GameObject sphere = new GameObject("Globo");
        sphere.layer = GlobeLayer;
        sphere.transform.SetParent(root.transform, false);
        sphere.AddComponent<MeshFilter>().sharedMesh = reliefCache != null ? BuildReliefMesh(240, 480) : BuildSphereMesh(72, 144);
        // Unlit: a cor da tela é exatamente a da textura (com Lit, a luz ambiente do céu lavava tudo).
        // O volume vem da película de sombra na frente do globo (BuildShade).
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
        // Com relevo 3D: shader próprio com luz presa à câmera (as montanhas aparecem ao girar).
        Shader relief = reliefCache != null ? Shader.Find("ImNotAnAlien/Globe Relief") : null;
        Material globeMaterial = new Material(relief != null ? relief : unlit != null ? unlit : lit) { name = "Globo" };
        if (globeMaterial.HasProperty("_Smoothness"))
        {
            globeMaterial.SetFloat("_Smoothness", 0.05f);
        }

        texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, true)
        {
            name = "Mapa_Planeta",
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp,
        };
        globeMaterial.mainTexture = texture;
        if (globeMaterial.HasProperty("_BaseMap"))
        {
            globeMaterial.SetTexture("_BaseMap", texture);
        }

        sphere.AddComponent<MeshRenderer>().sharedMaterial = globeMaterial;
        globeMaterialRef = globeMaterial;
        BuildShade(unlit);
        globe = sphere.transform;
        BuildWater(unlit != null ? unlit : lit);
        BuildMiniatures();
        BuildClouds();
        BuildAtmosphere();
        BuildStand();

        // Câmera e luz só do globo.
        GameObject camObject = new GameObject("Camera_Globo");
        camObject.layer = GlobeLayer;
        camObject.transform.SetParent(root.transform, false);
        globeCamera = camObject.AddComponent<Camera>();
        globeCamera.clearFlags = CameraClearFlags.SolidColor;
        globeCamera.backgroundColor = Night;
        globeCamera.cullingMask = 1 << GlobeLayer;
        globeCamera.fieldOfView = 38f;
        globeCamera.nearClipPlane = 0.05f;
        globeCamera.farClipPlane = 60f;
        globeCamera.depth = (mainCamera != null ? mainCamera.depth : 0f) + 50f;
        // O globo fica um pouco à esquerda (o painel ocupa a direita).
        globeCamera.rect = new Rect(0f, 0f, 1f, 1f);

        GameObject lightObject = new GameObject("Luz_Globo");
        lightObject.transform.SetParent(root.transform, false);
        lightObject.transform.rotation = Quaternion.Euler(22f, 48f, 0f); // luz de lado: o globo ganha volume
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        light.color = new Color(1f, 0.97f, 0.92f);
        light.cullingMask = 1 << GlobeLayer;
        light.shadows = LightShadows.None;

        // Estrelas.
        System.Random random = new System.Random(7);
        Material star = Glow(new Color(1f, 1f, 1f), 2f);
        for (int i = 0; i < 220; i++)
        {
            Vector3 dir = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 0.6f + 0.4f).normalized;
            Transform s = Prim(PrimitiveType.Sphere, "Estrela", root.transform, star, Vector3.one * (0.03f + (float)random.NextDouble() * 0.04f));
            s.localPosition = dir * 28f;
        }

        // Ícones das regiões.
        for (int region = 0; region < Planet.RegionCount; region++)
        {
            Transform icon = BuildIcon(Planet.RegionBiome(region));
            icon.SetParent(globe, false);
            Vector2 at = Planet.RegionLabelPosition(region) + new Vector2(0f, -5f);
            Place(icon, at, 0.995f);
            icon.localScale = Vector3.one * 0.085f;
        }

        // Vilarejo.
        Transform village = BuildIcon(Planet.Biome.Vale);
        village.SetParent(globe, false);
        Place(village, new Vector2(Planet.VillageCorner.x + 1.5f, Planet.VillageCorner.y + 1.5f), 1.0f);
        village.localScale = Vector3.one * 0.06f;
        Transform villageDot = Prim(PrimitiveType.Sphere, "Vilarejo", globe, Glow(Village, 1.2f), Vector3.one * 0.022f);
        Place(villageDot, new Vector2(Planet.VillageCorner.x + 1.5f, Planet.VillageCorner.y + 1.5f), 1.012f);

        // Bases: torrezinhas.
        foreach (Planet.BaseInfo info in Planet.Bases)
        {
            Transform tower = BuildTower(out Renderer[] parts);
            tower.SetParent(globe, false);
            Place(tower, new Vector2(info.coord.x + 0.5f, info.coord.y + 0.5f), 1.0f);
            tower.localScale = Vector3.one * 0.07f;
            baseMarks.Add((tower, parts));
        }

        // Você: seta laranja com contorno escuro.
        playerMark = BuildArrow();
        playerMark.SetParent(globe, false);

        // Marcador de destino: alfinete vermelho.
        waypointMark = new GameObject("Marcador").transform;
        waypointMark.gameObject.layer = GlobeLayer;
        waypointMark.SetParent(globe, false);
        Material pinMaterial = Glow(new Color(1f, 0.25f, 0.18f), 2.5f);
        Prim(PrimitiveType.Cylinder, "Haste", waypointMark, pinMaterial, new Vector3(0.012f, 0.035f, 0.012f)).localPosition = new Vector3(0f, 0.035f, 0f);
        Prim(PrimitiveType.Sphere, "Cabeca", waypointMark, pinMaterial, Vector3.one * 0.035f).localPosition = new Vector3(0f, 0.08f, 0f);

        // Anel da seleção.
        selectionRing = Prim(PrimitiveType.Cylinder, "Selecao", globe, Glow(Mint, 1.6f), new Vector3(0.07f, 0.002f, 0.07f));
        Material dot = Glow(new Color(1f, 0.97f, 0.9f), 1.2f);
        for (int i = 0; i < 40; i++)
        {
            pathDots.Add(Prim(PrimitiveType.Sphere, "Caminho", globe, dot, Vector3.one * 0.012f));
        }

        root.SetActive(false);
    }

    // ---------- Película de sombra (dá volume ao globo sem luz) ----------

    private Transform shade;

    private void BuildShade(Shader unlit)
    {
        if (unlit == null)
        {
            return;
        }

        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Sombra_Globo",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        Vector3 sun = new Vector3(-0.55f, 0.6f, 0.58f).normalized; // luz vem de cima, à esquerda
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float inside = Mathf.Clamp01((1f - r) * size * 0.5f);
                if (inside <= 0f)
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float rr = Mathf.Min(r, 1f);
                Vector3 n = new Vector3(u, v, Mathf.Sqrt(Mathf.Max(0f, 1f - rr * rr)));
                float light = Vector3.Dot(n, sun);
                float dark = Mathf.Clamp01((0.45f - light) * 0.55f) + Mathf.Pow(rr, 6f) * 0.22f;
                dark = Mathf.Min(dark, 0.62f);
                float bright = Mathf.Pow(Mathf.Max(0f, light), 18f) * 0.22f;
                Color32 c = bright > dark
                    ? new Color32(255, 250, 235, (byte)(bright * inside * 255f))
                    : new Color32(8, 10, 30, (byte)(dark * inside * 255f));
                pixels[y * size + x] = c;
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();

        Material material = new Material(unlit) { name = "Sombra_Globo" };
        SetTransparent(material, Color.white);
        material.mainTexture = tex;
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", tex);
        }

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Sombra_Globo";
        quad.layer = GlobeLayer;
        quad.transform.SetParent(root.transform, false);
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shade = quad.transform;
    }

    // Põe a película entre a câmera e o globo, cobrindo exatamente o disco que aparece na tela.
    private void PlaceShade()
    {
        if (shade == null)
        {
            return;
        }

        Vector3 cam = globeCamera.transform.localPosition;
        Vector3 toCenter = -cam;
        float d = toCenter.magnitude;
        if (d <= Radius * 1.2f)
        {
            shade.gameObject.SetActive(false);
            return;
        }

        shade.gameObject.SetActive(true);
        Vector3 dir = toCenter / d;
        float along = d - Radius * 1.15f; // na frente de tudo que está sobre o globo
        float half = along * Mathf.Tan(Mathf.Asin(Radius / d));
        shade.localPosition = cam + dir * along;
        shade.localRotation = Quaternion.LookRotation(dir, Vector3.up);
        shade.localScale = new Vector3(half * 2f, half * 2f, 1f);
    }

    private Material Glow(Color color, float strength)
    {
        Material material = new Material(litBase) { color = color };
        if (strength > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * strength);
        }

        return material;
    }

    private Material Paint(Color color)
    {
        Material material = new Material(litBase) { color = color };
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.25f);
        }

        return material;
    }

    private static void SetTransparent(Material material, Color color)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.color = color;
    }

    private static Transform Prim(PrimitiveType type, string name, Transform parent, Material material, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.layer = GlobeLayer;
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go.transform;
    }

    private static Transform Part(Mesh mesh, string name, Transform parent, Material material, Vector3 position, Vector3 scale, Vector3 euler = default)
    {
        GameObject go = new GameObject(name);
        go.layer = GlobeLayer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go.transform;
    }

    private static Mesh coneMesh;

    // Cone simples (base no chão, ponta em cima, altura 1, raio 0,5).
    private static Mesh Cone()
    {
        if (coneMesh != null)
        {
            return coneMesh;
        }

        const int sides = 10;
        List<Vector3> v = new List<Vector3> { new Vector3(0f, 1f, 0f), Vector3.zero };
        List<int> t = new List<int>();
        for (int i = 0; i <= sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides;
            v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
        }

        for (int i = 0; i < sides; i++)
        {
            int a = 2 + i;
            int b = 3 + i;
            t.Add(0); t.Add(b); t.Add(a);  // lados
            t.Add(1); t.Add(a); t.Add(b);  // fundo
        }

        coneMesh = new Mesh { name = "Cone" };
        coneMesh.SetVertices(v);
        coneMesh.SetTriangles(t, 0);
        coneMesh.RecalculateNormals();
        coneMesh.RecalculateBounds();
        return coneMesh;
    }

    private static Mesh primitiveMesh(PrimitiveType type)
    {
        GameObject temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return mesh;
    }

    private Mesh sphereMesh;
    private Mesh cubeMesh;
    private Mesh cylinderMesh;

    // Ícone 3D de um bioma, em pé sobre o eixo Y (tamanho ~1 antes da escala).
    private Transform BuildIcon(Planet.Biome biome)
    {
        if (sphereMesh == null)
        {
            sphereMesh = primitiveMesh(PrimitiveType.Sphere);
            cubeMesh = primitiveMesh(PrimitiveType.Cube);
            cylinderMesh = primitiveMesh(PrimitiveType.Cylinder);
        }

        Transform icon = new GameObject("Icone_" + biome).transform;
        icon.gameObject.layer = GlobeLayer;
        Material white = Paint(Color.white);
        switch (biome)
        {
            case Planet.Biome.Picos:
            case Planet.Biome.Geleira:
            case Planet.Biome.Gelo:
            case Planet.Biome.Tundra:
            {
                Material rock = Paint(biome == Planet.Biome.Picos ? new Color(0.55f, 0.48f, 0.44f) : new Color(0.62f, 0.74f, 0.82f));
                Part(Cone(), "Monte", icon, rock, new Vector3(-0.35f, 0f, 0f), new Vector3(0.9f, 1.1f, 0.9f));
                Part(Cone(), "Neve", icon, white, new Vector3(-0.35f, 0.62f, 0f), new Vector3(0.38f, 0.5f, 0.38f));
                Part(Cone(), "Monte", icon, rock, new Vector3(0.4f, 0f, 0.1f), new Vector3(1.1f, 1.5f, 1.1f));
                Part(Cone(), "Neve", icon, white, new Vector3(0.4f, 0.9f, 0.1f), new Vector3(0.45f, 0.62f, 0.45f));
                break;
            }
            case Planet.Biome.Floresta:
            {
                Material trunk = Paint(new Color(0.54f, 0.35f, 0.27f));
                Material leaf = Paint(new Color(0.88f, 0.48f, 0.62f));
                Vector3[] spots = { new Vector3(-0.45f, 0f, 0f), new Vector3(0.05f, 0f, 0.15f), new Vector3(0.5f, 0f, -0.1f) };
                foreach (Vector3 p in spots)
                {
                    Part(cylinderMesh, "Tronco", icon, trunk, p + new Vector3(0f, 0.25f, 0f), new Vector3(0.1f, 0.25f, 0.1f));
                    Part(sphereMesh, "Copa", icon, leaf, p + new Vector3(0f, 0.75f, 0f), Vector3.one * 0.6f);
                }

                break;
            }
            case Planet.Biome.Dunas:
            {
                Material sand = Paint(new Color(0.6f, 0.48f, 0.75f));
                Part(sphereMesh, "Duna", icon, sand, new Vector3(-0.3f, 0f, 0f), new Vector3(1.1f, 0.6f, 0.7f));
                Part(sphereMesh, "Duna", icon, Paint(new Color(0.54f, 0.42f, 0.7f)), new Vector3(0.35f, 0f, 0.1f), new Vector3(1.0f, 0.75f, 0.7f));
                break;
            }
            case Planet.Biome.Pantano:
            {
                Part(cylinderMesh, "Lama", icon, Paint(new Color(0.3f, 0.48f, 0.33f)), new Vector3(0f, 0.03f, 0f), new Vector3(1.4f, 0.03f, 0.8f));
                Material reed = Paint(new Color(0.48f, 0.36f, 0.23f));
                Part(cylinderMesh, "Junco", icon, reed, new Vector3(-0.25f, 0.4f, 0f), new Vector3(0.05f, 0.4f, 0.05f));
                Part(cylinderMesh, "Junco", icon, reed, new Vector3(0.2f, 0.32f, 0.1f), new Vector3(0.05f, 0.32f, 0.05f));
                break;
            }
            case Planet.Biome.Cratera:
            {
                Part(cylinderMesh, "Borda", icon, Paint(new Color(0.56f, 0.43f, 0.33f)), new Vector3(0f, 0.08f, 0f), new Vector3(1.4f, 0.08f, 1.0f));
                Part(cylinderMesh, "Fundo", icon, Paint(new Color(0.36f, 0.27f, 0.2f)), new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.06f, 0.6f));
                break;
            }
            case Planet.Biome.Campos:
            case Planet.Biome.Planicie:
            case Planet.Biome.Estepe:
            {
                Part(cubeMesh, "Plantacao", icon, Paint(new Color(0.89f, 0.78f, 0.35f)), new Vector3(-0.15f, 0.06f, 0f), new Vector3(1.1f, 0.12f, 0.7f));
                Part(cubeMesh, "Celeiro", icon, Paint(new Color(0.75f, 0.33f, 0.25f)), new Vector3(0.5f, 0.25f, -0.1f), new Vector3(0.35f, 0.4f, 0.35f));
                Part(Cone(), "Telhado", icon, Paint(new Color(0.56f, 0.23f, 0.18f)), new Vector3(0.5f, 0.45f, -0.1f), new Vector3(0.5f, 0.25f, 0.5f));
                break;
            }
            case Planet.Biome.Colinas:
            {
                Part(sphereMesh, "Colina", icon, Paint(new Color(0.5f, 0.64f, 0.37f)), new Vector3(-0.3f, 0f, 0f), new Vector3(1.0f, 0.75f, 0.8f));
                Part(sphereMesh, "Colina", icon, Paint(new Color(0.43f, 0.58f, 0.31f)), new Vector3(0.35f, 0f, 0.1f), new Vector3(0.9f, 0.6f, 0.7f));
                break;
            }
            case Planet.Biome.Baia:
            {
                Part(cylinderMesh, "Agua", icon, Paint(new Color(0.3f, 0.6f, 0.82f)), new Vector3(0f, 0.03f, 0f), new Vector3(1.3f, 0.03f, 0.9f));
                Material tentacle = Paint(new Color(0.49f, 0.31f, 0.62f));
                Part(cylinderMesh, "Tentaculo", icon, tentacle, new Vector3(-0.2f, 0.35f, 0f), new Vector3(0.12f, 0.35f, 0.12f), new Vector3(0f, 0f, 18f));
                Part(cylinderMesh, "Tentaculo", icon, tentacle, new Vector3(0.25f, 0.28f, 0.1f), new Vector3(0.1f, 0.28f, 0.1f), new Vector3(0f, 0f, -22f));
                break;
            }
            case Planet.Biome.Cidade:
            {
                Material grey = Paint(new Color(0.5f, 0.53f, 0.6f));
                Part(cubeMesh, "Predio", icon, grey, new Vector3(-0.35f, 0.4f, 0f), new Vector3(0.3f, 0.8f, 0.3f));
                Part(cubeMesh, "Predio", icon, Paint(new Color(0.6f, 0.63f, 0.69f)), new Vector3(0f, 0.6f, 0.05f), new Vector3(0.32f, 1.2f, 0.32f));
                Part(cubeMesh, "Predio", icon, grey, new Vector3(0.35f, 0.3f, -0.05f), new Vector3(0.3f, 0.6f, 0.3f));
                Part(sphereMesh, "Nuvem", icon, white, new Vector3(0f, 1.35f, 0f), new Vector3(1.0f, 0.35f, 0.5f));
                break;
            }
            case Planet.Biome.Antenas:
            {
                Part(sphereMesh, "Morro", icon, Paint(new Color(0.65f, 0.54f, 0.39f)), new Vector3(0f, 0f, 0f), new Vector3(1.2f, 0.6f, 0.9f));
                Part(cylinderMesh, "Antena", icon, Paint(new Color(0.42f, 0.42f, 0.45f)), new Vector3(0f, 0.75f, 0f), new Vector3(0.05f, 0.5f, 0.05f));
                Part(sphereMesh, "Luz", icon, Glow(new Color(1f, 0.42f, 0.36f), 2f), new Vector3(0f, 1.28f, 0f), Vector3.one * 0.14f);
                break;
            }
            default: // Vale
            {
                Material wall = Paint(new Color(0.95f, 0.89f, 0.79f));
                Material roof = Paint(new Color(0.75f, 0.33f, 0.25f));
                Part(cubeMesh, "Casa", icon, wall, new Vector3(-0.3f, 0.2f, 0f), new Vector3(0.4f, 0.4f, 0.4f));
                Part(Cone(), "Telhado", icon, roof, new Vector3(-0.3f, 0.4f, 0f), new Vector3(0.6f, 0.3f, 0.6f));
                Part(cubeMesh, "Casa", icon, wall, new Vector3(0.3f, 0.17f, 0.15f), new Vector3(0.36f, 0.34f, 0.36f));
                Part(Cone(), "Telhado", icon, roof, new Vector3(0.3f, 0.34f, 0.15f), new Vector3(0.55f, 0.28f, 0.55f));
                break;
            }
        }

        return icon;
    }

    private Transform BuildTower(out Renderer[] parts)
    {
        Transform tower = new GameObject("Base").transform;
        tower.gameObject.layer = GlobeLayer;
        Material body = Glow(Mint, 0.8f);
        Part(Cone(), "Corpo", tower, body, Vector3.zero, new Vector3(0.6f, 1.2f, 0.6f));
        Part(cylinderMesh ?? primitiveMesh(PrimitiveType.Cylinder), "Topo", tower, body, new Vector3(0f, 0.95f, 0f), new Vector3(0.45f, 0.08f, 0.45f));
        Part(sphereMesh ?? primitiveMesh(PrimitiveType.Sphere), "Luz", tower, Glow(Color.white, 2f), new Vector3(0f, 1.25f, 0f), Vector3.one * 0.2f);
        parts = tower.GetComponentsInChildren<Renderer>();
        return tower;
    }

    private Transform BuildArrow()
    {
        Mesh mesh = new Mesh { name = "Seta" };
        Vector3[] v =
        {
            new Vector3(0f, 0f, 1f), new Vector3(0.7f, 0f, -0.75f), new Vector3(0f, 0f, -0.3f), new Vector3(-0.7f, 0f, -0.75f),
        };
        mesh.vertices = v;
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.RecalculateBounds();

        Transform arrow = new GameObject("Voce").transform;
        arrow.gameObject.layer = GlobeLayer;
        Part(mesh, "Contorno", arrow, Paint(new Color(0.35f, 0.12f, 0.08f)), new Vector3(0f, -0.02f, 0f), Vector3.one * 1.25f);
        Part(mesh, "Seta", arrow, Glow(Arrow, 1.2f), Vector3.zero, Vector3.one);
        return arrow;
    }

    // Esfera com latitude/longitude iguais às do mapa (u = longitude, v = latitude, polo a polo).
    private static Mesh BuildSphereMesh(int latSegments, int lonSegments)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (int i = 0; i <= latSegments; i++)
        {
            float lat = -90f + 180f * i / latSegments;
            for (int j = 0; j <= lonSegments; j++)
            {
                float lon = 360f * j / lonSegments;
                vertices.Add(Direction(lat, lon) * Radius);
                uvs.Add(new Vector2((float)j / lonSegments, (float)i / latSegments));
            }
        }

        int row = lonSegments + 1;
        for (int i = 0; i < latSegments; i++)
        {
            for (int j = 0; j < lonSegments; j++)
            {
                int a = i * row + j;
                int b = (i + 1) * row + j;
                triangles.Add(a); triangles.Add(b); triangles.Add(b + 1);
                triangles.Add(a); triangles.Add(b + 1); triangles.Add(a + 1);
            }
        }

        Mesh mesh = new Mesh { name = "Globo", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Longitude 0 olha para a câmera (-Z), leste para a direita, norte para cima.
    private static Vector3 Direction(float latDegrees, float lonDegrees)
    {
        float lat = latDegrees * Mathf.Deg2Rad;
        float lon = lonDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), -Mathf.Cos(lat) * Mathf.Cos(lon));
    }

    private static Vector2 LatLongOf(Vector2 planetPosition)
    {
        float lon = planetPosition.x / Planet.Width * 360f;
        float lat = ((planetPosition.y + Planet.PoleRows) / (Planet.Height + 2f * Planet.PoleRows) - 0.5f) * 180f;
        return new Vector2(Mathf.Clamp(lat, -90f, 90f), lon);
    }

    private static Vector2 PlanetPositionOf(float latDegrees, float lonDegrees)
    {
        float x = Mathf.Repeat(lonDegrees, 360f) / 360f * Planet.Width;
        float z = (latDegrees / 180f + 0.5f) * (Planet.Height + 2f * Planet.PoleRows) - Planet.PoleRows;
        return new Vector2(x, z);
    }

    // Pontos sobre o globo acompanham o relevo (ícones, bases, marcador e seta ficam em cima do morro).
    private static Vector3 LocalPoint(Vector2 planetPosition, float height)
    {
        Vector2 latLong = LatLongOf(planetPosition);
        return Direction(latLong.x, latLong.y) * Radius * (height + SurfaceLift(planetPosition));
    }

    private static void Place(Transform mark, Vector2 planetPosition, float height)
    {
        Vector2 latLong = LatLongOf(planetPosition);
        Vector3 dir = Direction(latLong.x, latLong.y);
        mark.localPosition = dir * Radius * (height + SurfaceLift(planetPosition));
        mark.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
    }

    // ---------- Relevo 3D e oceano (Missão 20) ----------

    private static float[] reliefCache;            // alturas do planeta (grade de 25 m), da pintura
    private const float ReliefScale = 0.09f;       // ~60 m de altura = 9% do raio (bem exagerado de propósito)
    private const float PoleLift = 0.004f;         // gelo dos polos um pouco acima do mar

    // Quanto a superfície sobe (+) ou desce (-) em relação ao raio, numa posição do planeta (em blocos).
    private static float SurfaceLift(Vector2 planetPosition)
    {
        if (reliefCache == null)
        {
            return 0f;
        }

        if (planetPosition.y < 0f || planetPosition.y >= Planet.Height)
        {
            return PoleLift;
        }

        return LiftOfHeight(ReliefAt(reliefCache, planetPosition.x * ReliefPerBlock, planetPosition.y * ReliefPerBlock));
    }

    // Terra sempre um degrau acima da água (litoral nítido); acima disso cresce com a altura (curva
    // que realça morros baixos). Mar e lagos afundam um pouco.
    private static float LiftOfHeight(float h)
    {
        float water = PlanetTerrain.WaterLevel;
        if (h <= water)
        {
            return -0.004f - Mathf.Clamp01((water - h) / 2f) * 0.004f;
        }

        return 0.003f + Mathf.Pow(Mathf.Clamp01((h - water) / 60f), 0.75f) * ReliefScale;
    }

    private static Mesh BuildReliefMesh(int latSegments, int lonSegments)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (int i = 0; i <= latSegments; i++)
        {
            float lat = -90f + 180f * i / latSegments;
            for (int j = 0; j <= lonSegments; j++)
            {
                float lon = 360f * j / lonSegments;
                float lift = SurfaceLift(PlanetPositionOf(lat, lon));
                vertices.Add(Direction(lat, lon) * Radius * (1f + lift));
                uvs.Add(new Vector2((float)j / lonSegments, (float)i / latSegments));
            }
        }

        int row = lonSegments + 1;
        for (int i = 0; i < latSegments; i++)
        {
            for (int j = 0; j < lonSegments; j++)
            {
                int a = i * row + j;
                int b = (i + 1) * row + j;
                triangles.Add(a); triangles.Add(b); triangles.Add(b + 1);
                triangles.Add(a); triangles.Add(b + 1); triangles.Add(a + 1);
            }
        }

        Mesh mesh = new Mesh { name = "Globo_Relevo", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------- Miniaturas, nuvens, atmosfera e pedestal (Missão 20) ----------
    // O kit vem do Blender (Assets/Resources/Globe/GloboKit.fbx): cada peça tem ~1 m e as cores pintadas
    // nos vértices. Milhares de miniaturas viram UMA malha só (barato de desenhar).

    private Transform cloudPivot;
    private Material globeMaterialRef;
    private Material cloudMaterial;
    private Renderer miniFar;
    private Renderer miniNear;
    private Renderer cloudRenderer;

    // Detalhe conforme o zoom: de longe, poucas miniaturas grandes e nuvens (dá para reconhecer cada
    // bioma); de perto, a vegetação completa e as divisas dos setores, sem nuvens na frente.
    private void UpdateDetail()
    {
        float near = 1f - Mathf.InverseLerp(1.9f, 2.7f, distance); // 0 longe, 1 perto
        bool showNear = near > 0.5f;
        if (miniFar != null) miniFar.enabled = !showNear;
        if (miniNear != null) miniNear.enabled = showNear;
        if (cloudMaterial != null)
        {
            float alpha = (1f - near) * 0.88f;
            cloudMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, alpha));
            if (cloudRenderer != null) cloudRenderer.enabled = alpha > 0.02f;
        }

        if (globeMaterialRef != null && globeMaterialRef.HasProperty("_SectorStrength"))
        {
            globeMaterialRef.SetFloat("_SectorStrength", Mathf.Lerp(0.15f, 1f, near));
        }
    }
    private static GameObject globeKit;
    private static readonly Dictionary<string, Mesh> kitMeshes = new Dictionary<string, Mesh>();

    private static Mesh KitMesh(string name)
    {
        if (kitMeshes.TryGetValue(name, out Mesh cached) && cached != null)
        {
            return cached;
        }

        if (globeKit == null)
        {
            globeKit = Resources.Load<GameObject>("Globe/GloboKit");
        }

        if (globeKit == null)
        {
            return null;
        }

        foreach (MeshFilter filter in globeKit.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.name.StartsWith(name) && filter.sharedMesh != null)
            {
                if (!filter.sharedMesh.isReadable)
                {
                    Debug.LogWarning("Globo: GloboKit.fbx não está legível. Clique com o botão direito nele (Assets/Resources/Globe) e escolha Reimport.");
                    return null;
                }

                kitMeshes[name] = filter.sharedMesh;
                return filter.sharedMesh;
            }
        }

        return null;
    }

    private static Material MiniMaterial(Color color, bool transparent)
    {
        Shader shader = Shader.Find("ImNotAnAlien/Globe Mini");
        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader) { name = "Globo_Mini" };
        material.SetColor("_BaseColor", color);
        material.renderQueue = transparent ? (int)UnityEngine.Rendering.RenderQueue.Transparent - 5 : (int)UnityEngine.Rendering.RenderQueue.Geometry;
        return material;
    }

    // Junta várias cópias de malhas do kit numa malha só (cor do vértice; branco puro = cor do bioma).
    private sealed class Combiner
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        private readonly bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

        public int Count => vertices.Count;

        public void Add(Mesh mesh, Matrix4x4 matrix, Color tint)
        {
            Vector3[] v = mesh.vertices;
            Vector3[] n = mesh.normals;
            Color[] c = mesh.colors;
            int[] t = mesh.triangles;
            int start = vertices.Count;
            for (int i = 0; i < v.Length; i++)
            {
                vertices.Add(matrix.MultiplyPoint3x4(v[i]));
                normals.Add(n.Length > i ? matrix.MultiplyVector(n[i]).normalized : Vector3.up);
                Color color = c.Length > i ? c[i] : Color.white;
                if (color.r > 0.97f && color.g > 0.97f && color.b > 0.97f && tint.a > 0f)
                {
                    color = tint;
                }

                color.a = 1f;
                colors.Add(linear ? color.linear : color);
            }

            for (int i = 0; i < t.Length; i++)
            {
                triangles.Add(start + t[i]);
            }
        }

        public Mesh ToMesh(string name)
        {
            Mesh mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // Que miniatura vai em cada bioma (nome no kit, chance de aparecer, tamanho).
    private static (string mesh, float chance, float size) MiniFor(Planet.Biome biome, double roll)
    {
        switch (biome)
        {
            case Planet.Biome.Gelo: return ("Mini_Pinheiro", 0.3f, 0.022f);
            case Planet.Biome.Geleira: return ("Mini_Pinheiro", 0.25f, 0.02f);
            case Planet.Biome.Tundra: return ("Mini_Pinheiro", 0.55f, 0.022f);
            case Planet.Biome.Picos: return ("Mini_Pinheiro", 0.35f, 0.024f);
            case Planet.Biome.Floresta: return roll < 0.12 ? ("Mini_Cogumelo", 0.9f, 0.02f) : ("Mini_Arvore", 0.9f, 0.026f);
            case Planet.Biome.Pantano: return roll < 0.5 ? ("Mini_Cogumelo", 0.45f, 0.022f) : ("Mini_Arvore", 0.35f, 0.022f);
            case Planet.Biome.Dunas: return ("Mini_Cacto", 0.32f, 0.022f);
            case Planet.Biome.Cratera: return ("Mini_Cristal", 0.45f, 0.026f);
            case Planet.Biome.Antenas: return ("Mini_Antena", 0.4f, 0.028f);
            case Planet.Biome.Cidade: return ("Mini_Predio", 0.75f, 0.026f);
            case Planet.Biome.Vale: return roll < 0.35 ? ("Mini_Pinheiro", 0.4f, 0.022f) : ("Mini_Arvore", 0.4f, 0.024f);
            case Planet.Biome.Estepe: return ("Mini_Arvore", 0.12f, 0.02f);
            case Planet.Biome.Planicie: return ("Mini_Arvore", 0.18f, 0.022f);
            default: return ("Mini_Arvore", 0.3f, 0.024f); // Campos, Colinas, Baía
        }
    }

    // Miniaturas espalhadas pela terra conforme o bioma de cada lugar (mudam a cada jogo, como o mapa).
    private void BuildMiniatures()
    {
        if (reliefCache == null || KitMesh("Mini_Arvore") == null)
        {
            return;
        }

        Material material = MiniMaterial(Color.white, false);
        if (material == null)
        {
            return;
        }

        Combiner combiner = new Combiner();   // perto: tudo
        Combiner far = new Combiner();        // longe: 1 em cada 6, maiores
        System.Random rng = new System.Random(4242);
        int placed = 0;
        for (int attempt = 0; attempt < 22000 && combiner.Count < 1500000; attempt++)
        {
            float lat = Mathf.Asin((float)rng.NextDouble() * 2f - 1f) * Mathf.Rad2Deg;
            float lon = (float)rng.NextDouble() * 360f;
            double roll = rng.NextDouble();
            double chance = rng.NextDouble();
            float spin = (float)rng.NextDouble() * 360f;
            float variation = 0.8f + (float)rng.NextDouble() * 0.45f;

            Vector2 position = PlanetPositionOf(lat, lon);
            if (position.y < 0f || position.y >= Planet.Height)
            {
                continue;
            }

            float lift = SurfaceLift(position);
            if (lift < 0.0025f)
            {
                continue; // água
            }

            int region = Planet.RegionAt(new Vector2Int(Mathf.Clamp((int)position.x, 0, Planet.Width - 1), Mathf.Clamp((int)position.y, 0, Planet.Height - 1)));
            Planet.Biome biome = Planet.RegionBiome(region);
            (string name, float probability, float size) = MiniFor(biome, roll);
            if (chance > probability)
            {
                continue;
            }

            Mesh mesh = KitMesh(name);
            if (mesh == null)
            {
                continue;
            }

            Vector3 dir = Direction(lat, lon);
            Vector3 at = dir * Radius * (1f + lift - 0.002f);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.AngleAxis(spin, Vector3.up);
            Color tint = PlanetTerrain.StyleOf(biome).foliage;
            combiner.Add(mesh, Matrix4x4.TRS(at, rotation, Vector3.one * size * variation * 0.8f), tint);
            if (placed++ % 6 == 0)
            {
                far.Add(mesh, Matrix4x4.TRS(at, rotation, Vector3.one * size * variation * 1.5f), tint);
            }
        }

        if (combiner.Count == 0)
        {
            return;
        }

        miniNear = MiniLayer("Miniaturas_Perto", combiner, material);
        miniFar = MiniLayer("Miniaturas_Longe", far, material);
    }

    private Renderer MiniLayer(string name, Combiner combiner, Material material)
    {
        if (combiner.Count == 0)
        {
            return null;
        }

        GameObject layer = new GameObject(name);
        layer.layer = GlobeLayer;
        layer.transform.SetParent(globe, false);
        layer.AddComponent<MeshFilter>().sharedMesh = combiner.ToMesh("Globo_" + name);
        MeshRenderer renderer = layer.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    // Nuvens fofas numa camada acima do relevo, girando devagar (não cobrem muito: dá para ver o mapa).
    private void BuildClouds()
    {
        Mesh cloud = KitMesh("Mini_Nuvem");
        Material material = cloud != null ? MiniMaterial(new Color(1f, 1f, 1f, 0.88f), true) : null;
        if (material == null)
        {
            return;
        }

        Combiner combiner = new Combiner();
        System.Random rng = new System.Random(777);
        for (int i = 0; i < 38; i++)
        {
            float lat = Mathf.Asin((float)rng.NextDouble() * 1.7f - 0.85f) * Mathf.Rad2Deg;
            float lon = (float)rng.NextDouble() * 360f;
            float size = 0.06f + (float)rng.NextDouble() * 0.06f;
            Vector3 dir = Direction(lat, lon);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.AngleAxis((float)rng.NextDouble() * 360f, Vector3.up);
            combiner.Add(cloud, Matrix4x4.TRS(dir * Radius * (1.11f + (float)rng.NextDouble() * 0.02f), rotation, new Vector3(size, size * 0.55f, size)), new Color(0f, 0f, 0f, 0f));
        }

        cloudPivot = new GameObject("Nuvens").transform;
        cloudPivot.gameObject.layer = GlobeLayer;
        cloudPivot.SetParent(globe, false);
        GameObject clouds = new GameObject("Nuvens_Malha");
        clouds.layer = GlobeLayer;
        clouds.transform.SetParent(cloudPivot, false);
        clouds.AddComponent<MeshFilter>().sharedMesh = combiner.ToMesh("Globo_Nuvens");
        cloudRenderer = clouds.AddComponent<MeshRenderer>();
        cloudRenderer.sharedMaterial = material;
        cloudMaterial = material;
    }

    // Brilho azulado na borda do globo (atmosfera).
    private void BuildAtmosphere()
    {
        Shader shader = Shader.Find("ImNotAnAlien/Globe Atmosphere");
        if (shader == null)
        {
            return;
        }

        GameObject atmosphere = new GameObject("Atmosfera");
        atmosphere.layer = GlobeLayer;
        atmosphere.transform.SetParent(root.transform, false);
        atmosphere.transform.localScale = Vector3.one * 1.2f;
        atmosphere.AddComponent<MeshFilter>().sharedMesh = BuildSphereMesh(48, 96);
        atmosphere.AddComponent<MeshRenderer>().sharedMaterial = new Material(shader) { name = "Atmosfera" };
    }

    // Pedestal retrô de globo de escritório (arco + pé), parado enquanto o globo gira.
    private void BuildStand()
    {
        Mesh mesh = KitMesh("Pedestal");
        Material material = mesh != null ? MiniMaterial(Color.white, false) : null;
        if (material == null)
        {
            return;
        }

        Combiner combiner = new Combiner();
        combiner.Add(mesh, Matrix4x4.identity, new Color(0f, 0f, 0f, 0f));
        GameObject stand = new GameObject("Pedestal");
        stand.layer = GlobeLayer;
        stand.transform.SetParent(root.transform, false);
        stand.AddComponent<MeshFilter>().sharedMesh = combiner.ToMesh("Globo_Pedestal");
        stand.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // Oceano: esfera de água meio transparente no nível do mar, por cima do fundo pintado — a costa e
    // os lagos ganham contorno sozinhos (a terra fica sempre um degrau acima).
    private void BuildWater(Shader shader)
    {
        if (reliefCache == null || shader == null)
        {
            return;
        }

        GameObject water = new GameObject("Oceano");
        water.layer = GlobeLayer;
        water.transform.SetParent(globe, false);
        water.AddComponent<MeshFilter>().sharedMesh = BuildSphereMesh(96, 192);
        Material material = new Material(shader) { name = "Oceano" };
        Color sea = new Color(0.2f, 0.52f, 0.95f, 0.62f);
        SetTransparent(material, sea);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", sea);
        }

        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 10; // antes da película de sombra
        water.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ---------- Pintura ----------

    // Pinta o mapa uma vez por jogo (as regiões não mudam). Divisas suaves: cada pixel olha os blocos
    // em volta com pesos (filtro de "maioria"), o que arredonda os degraus dos blocos em curvas.
    // Cada região ganha um tom que varia devagar, uma sombra perto da divisa e um contorno creme.
    private bool painted;
    private System.Threading.Tasks.Task<Color32[]> paintTask;

    private static int TextureWidth => Planet.Width * PixelsPerBlock;
    private static int TextureHeight => (Planet.Height + 2 * Planet.PoleRows) * PixelsPerBlock;

    // A pintura é pesada (1 milhão de pixels): começa em segundo plano assim que o jogo abre.
    private void StartPainting()
    {
        if (paintTask != null || !Planet.RegionsBuilt)
        {
            return;
        }

        WorldLayout layout = WorldStreamer.Instance != null ? WorldStreamer.Instance.Layout : null;
        paintTask = System.Threading.Tasks.Task.Run(() => ComputePixels(layout, TextureWidth, TextureHeight));
    }

    private void PaintTexture()
    {
        if (painted)
        {
            return;
        }

        StartPainting();
        Color32[] result = paintTask != null ? paintTask.Result : ComputePixels(WorldStreamer.Instance != null ? WorldStreamer.Instance.Layout : null, TextureWidth, TextureHeight);
        texture.SetPixels32(result);
        texture.Apply(true);
        painted = true;
    }

    private static Color32[] ComputePixels(WorldLayout layout, int width, int height)
    {
        float ppb = PixelsPerBlock;
        Color32[] pixels = new Color32[width * height];
        Color snow = new Color(0.97f, 0.98f, 1f);
        Color border = new Color(1f, 0.95f, 0.85f);
        Color river = new Color(0.22f, 0.55f, 0.88f);
        const float sigma2 = 1.1f; // largura do arredondamento (em blocos²)

        int[] keys = new int[25];
        float[] scores = new float[25];
        float[] relief = ReliefGrid(); // Missão 20: alturas do relevo (null fora do planeta)
        reliefCache = relief;          // também levanta o relevo 3D do globo
        for (int py = 0; py < height; py++)
        {
            float z = py / ppb - Planet.PoleRows; // posição em blocos
            for (int px = 0; px < width; px++)
            {
                float x = px / ppb;
                if (z < 0f || z >= Planet.Height)
                {
                    float edge = z < 0f ? -z : z - Planet.Height;
                    pixels[py * width + px] = Color.Lerp(snow, new Color(0.88f, 0.93f, 0.98f), Mathf.Clamp01(1f - edge / Planet.PoleRows) * 0.4f);
                    continue;
                }

                // Entorta o ponto (litoral ondulado) e pontua cada setor por perto (até ~2 blocos).
                float sx = x + (Noise(x, z, 0.35f, 41) - 0.5f) * 1.6f + (Noise(x, z, 1.1f, 43) - 0.5f) * 0.6f;
                float sz = z + (Noise(x, z, 0.35f, 47) - 0.5f) * 1.6f + (Noise(x, z, 1.1f, 49) - 0.5f) * 0.6f;
                int count = 0;
                int bx0 = Mathf.FloorToInt(sx - 2f);
                int bz0 = Mathf.FloorToInt(sz - 2f);
                for (int bz = bz0; bz <= bz0 + 4; bz++)
                {
                    if (bz < 0 || bz >= Planet.Height) continue;
                    for (int bx = bx0; bx <= bx0 + 4; bx++)
                    {
                        float dx = bx + 0.5f - sx;
                        float dz = bz + 0.5f - sz;
                        float w = Mathf.Exp(-(dx * dx + dz * dz) / sigma2);
                        if (w < 0.01f) continue;
                        int key = Planet.SectorAt(new Vector2Int(bx, bz));
                        int i = 0;
                        while (i < count && keys[i] != key) i++;
                        if (i == count)
                        {
                            if (count >= keys.Length) continue;
                            keys[count] = key;
                            scores[count] = 0f;
                            count++;
                        }

                        scores[i] += w;
                    }
                }

                // Região: soma dos setores dela. Melhor e segunda melhor (para a divisa).
                int bestSector = 0;
                float bestSectorScore = -1f, secondSectorScore = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (scores[i] > bestSectorScore) { secondSectorScore = Mathf.Max(secondSectorScore, bestSectorScore); bestSectorScore = scores[i]; bestSector = keys[i]; }
                    else secondSectorScore = Mathf.Max(secondSectorScore, scores[i]);
                }

                int region = Planet.RegionOfSector(bestSector);
                float mine = 0f, other = 0f;
                for (int i = 0; i < count; i++)
                {
                    if (Planet.RegionOfSector(keys[i]) == region) mine += scores[i];
                }

                for (int i = 0; i < count; i++)
                {
                    int r = Planet.RegionOfSector(keys[i]);
                    if (r == region) continue;
                    float sum = 0f;
                    for (int j = 0; j < count; j++)
                    {
                        if (Planet.RegionOfSector(keys[j]) == r) sum += scores[j];
                    }

                    other = Mathf.Max(other, sum);
                }

                float regionRatio = mine > 0f ? other / mine : 0f;          // 1 = em cima da divisa
                float sectorRatio = bestSectorScore > 0f ? secondSectorScore / bestSectorScore : 0f;

                // Cor do bioma, com manchas suaves (claro/escuro) para não ficar chapado.
                Color c = Planet.RegionColor(region);
                float patches = Noise(x, z, 0.12f, 3) * 0.5f + Noise(x, z, 0.31f, 7) * 0.3f + Noise(x, z, 0.83f, 9) * 0.2f;
                c *= Mathf.Lerp(0.94f, 1.04f, patches);
                c = BiomeDetail(Planet.RegionBiome(region), c, x, z);
                if (relief != null)
                {
                    c = ShadeRelief(relief, c, x, z);
                }

                // Sombra por dentro, chegando na divisa, e o contorno creme.
                c *= 1f - 0.16f * Mathf.SmoothStep(0.15f, 0.85f, regionRatio);
                float line = Mathf.SmoothStep(0.78f, 0.97f, regionRatio);
                c = Color.Lerp(c, border, line * 0.9f);

                // Divisas dos setores: guardadas no alfa da textura (o shader do globo mostra mais quando
                // o zoom chega perto e some de longe).
                float sectorLine = regionRatio < 0.5f ? Mathf.SmoothStep(0.8f, 0.98f, sectorRatio) : 0f;

                // Rio: faixa azul no meio dos blocos de rio, na direção dos lados com rio.
                if (layout != null)
                {
                    int bx = Mathf.FloorToInt(x);
                    int bzi = Mathf.FloorToInt(z);
                    BlockSpec spec = layout.Get(new Vector2Int(bx, bzi));
                    if (spec.type == BlockType.Rio)
                    {
                        // Do meio do bloco até cada lado com rio (retas, curvas e nascentes).
                        float fx = x - bx - 0.5f;
                        float fz = z - bzi - 0.5f;
                        float d = 1f;
                        if (spec.north == Socket.Rio) d = Mathf.Min(d, fz >= 0f ? Mathf.Abs(fx) : Mathf.Sqrt(fx * fx + fz * fz));
                        if (spec.south == Socket.Rio) d = Mathf.Min(d, fz <= 0f ? Mathf.Abs(fx) : Mathf.Sqrt(fx * fx + fz * fz));
                        if (spec.east == Socket.Rio) d = Mathf.Min(d, fx >= 0f ? Mathf.Abs(fz) : Mathf.Sqrt(fx * fx + fz * fz));
                        if (spec.west == Socket.Rio) d = Mathf.Min(d, fx <= 0f ? Mathf.Abs(fz) : Mathf.Sqrt(fx * fx + fz * fz));
                        c = Color.Lerp(c, river, Mathf.Clamp01((0.2f - d) * 12f));
                    }
                }

                // Missão 20: rios naturais (GreatLake), da cor da água do mar; mais largos no mapa
                // para aparecerem de longe.
                if (GreatLake.NearestRiver(x * 100f, z * 100f, out float riverDistance, out float riverHalf))
                {
                    float shown = Mathf.Max(riverHalf * 0.85f, 15f);
                    c = Color.Lerp(c, new Color(0.30f, 0.62f, 0.88f), Mathf.Clamp01((shown - riverDistance) / 6f));
                }

                c = Vivid(c);
                c.a = 1f;
                c.a = 1f - sectorLine;
                pixels[py * width + px] = c;
            }
        }

        return pixels;
    }

    // Cores mais vivas no mapa: mais saturação e um pouco menos de brilho (tons pastel sumiam na tela).
    private static Color Vivid(Color c)
    {
        Color.RGBToHSV(c, out float h, out float sat, out float val);
        sat = Mathf.Clamp01(sat * 1.45f + 0.04f);
        val = Mathf.Clamp01(val * 0.9f);
        return Color.HSVToRGB(h, sat, val);
    }

    // ---------- Relevo no globo (Missão 20) ----------

    private const int ReliefPerBlock = 4; // uma altura a cada 25 m

    // Alturas do planeta numa grade de 25 m (calculado junto com a pintura, fora da linha principal).
    private static float[] ReliefGrid()
    {
        if (!PlanetTerrain.Active)
        {
            return null;
        }

        int columns = Planet.Width * ReliefPerBlock;
        int rows = Planet.Height * ReliefPerBlock + 1;
        float[] grid = new float[columns * rows];
        float step = Planet.BlockSize / ReliefPerBlock;
        for (int j = 0; j < rows; j++)
        {
            float lz = Planet.GridOrigin.y + j * step;
            for (int i = 0; i < columns; i++)
            {
                grid[j * columns + i] = PlanetTerrain.Height(Planet.GridOrigin.x + i * step, lz);
            }
        }

        return grid;
    }

    private static float ReliefAt(float[] grid, float gx, float gz)
    {
        int columns = Planet.Width * ReliefPerBlock;
        int rows = Planet.Height * ReliefPerBlock + 1;
        gz = Mathf.Clamp(gz, 0f, rows - 1.001f);
        int i0 = Mathf.FloorToInt(gx);
        int j0 = Mathf.FloorToInt(gz);
        float tx = gx - i0;
        float tz = gz - j0;
        int a = ((i0 % columns) + columns) % columns;
        int b = (a + 1) % columns;
        float h00 = grid[j0 * columns + a], h10 = grid[j0 * columns + b];
        float h01 = grid[(j0 + 1) * columns + a], h11 = grid[(j0 + 1) * columns + b];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    // Sombreado de relevo (luz do noroeste, exagerado para aparecer no globo), mar azul, neve no alto
    // e curvas de nível bem fracas a cada 10 m. x, z em blocos.
    private static Color ShadeRelief(float[] grid, Color c, float x, float z)
    {
        float gx = x * ReliefPerBlock;
        float gz = z * ReliefPerBlock;
        float h = ReliefAt(grid, gx, gz);
        float step = Planet.BlockSize / ReliefPerBlock;
        float dx = (ReliefAt(grid, gx + 1f, gz) - ReliefAt(grid, gx - 1f, gz)) / (2f * step);
        float dz = (ReliefAt(grid, gx, gz + 1f) - ReliefAt(grid, gx, gz - 1f)) / (2f * step);

        // Encosta virada para o noroeste clareia; para o sudeste escurece.
        float light = (dx - dz) * 0.7071f;
        float shade = Mathf.Clamp(1f + light * 2.6f, 0.62f, 1.3f);
        c *= shade;

        // Mais alto = um pouco mais claro (altitude).
        c = Color.Lerp(c, Color.white, Mathf.Clamp01(h / 60f) * 0.18f);

        if (h > 24f)
        {
            c = Color.Lerp(c, new Color(0.96f, 0.98f, 1f), Mathf.Clamp01((h - 24f) / 8f) * 0.85f);
        }

        // Curvas de nível.
        if (h > 2f)
        {
            float band = Mathf.Abs(Mathf.Repeat(h, 10f) - 5f) / 5f; // 1 = em cima da curva
            c *= 1f - Mathf.SmoothStep(0.86f, 1f, band) * 0.10f;
        }

        // Mar e lagoas do relevo.
        if (h < PlanetTerrain.WaterLevel + 0.4f)
        {
            Color sea = Color.Lerp(new Color(0.40f, 0.72f, 0.92f), new Color(0.12f, 0.42f, 0.72f),
                Mathf.Clamp01((PlanetTerrain.WaterLevel - h) / 1.6f));
            c = Color.Lerp(c, sea, Mathf.Clamp01((PlanetTerrain.WaterLevel + 0.4f - h) / 0.5f));
        }

        c.a = 1f;
        return c;
    }

    // Detalhe por bioma: manchas de floresta, faixas de plantação, brilho de gelo...
    private static Color BiomeDetail(Planet.Biome biome, Color c, float x, float z)
    {
        switch (biome)
        {
            case Planet.Biome.Floresta:
            case Planet.Biome.Pantano:
            case Planet.Biome.Vale:
                return c * (0.9f + 0.14f * (Noise(x, z, 0.9f, 11) * 0.6f + Noise(x, z, 2.1f, 12) * 0.4f));
            case Planet.Biome.Campos:
            case Planet.Biome.Planicie:
            case Planet.Biome.Estepe:
                return c * (0.97f + 0.05f * Mathf.Sin((x + z * 0.4f) * 3.2f));
            case Planet.Biome.Dunas:
                return c * (0.95f + 0.07f * Mathf.Sin(x * 1.6f + Noise(x, z, 0.3f, 5) * 6f));
            case Planet.Biome.Gelo:
            case Planet.Biome.Tundra:
            case Planet.Biome.Picos:
            case Planet.Biome.Geleira:
                return c * 0.93f + (Color.white - c) * 0.18f * Noise(x, z, 0.7f, 13);
            case Planet.Biome.Baia:
                return c * (0.94f + 0.08f * Noise(x, z, 0.8f, 17));
            default:
                return c;
        }
    }

    // Ruído suave (0..1). "frequency" em ciclos por bloco; arredondada para caber certinho na volta
    // do planeta (sem emenda na longitude 0).
    private static float Noise(float x, float z, float frequency, int seed)
    {
        int period = Mathf.Max(1, Mathf.RoundToInt(Planet.Width * frequency));
        float f = period / (float)Planet.Width;
        x *= f;
        z *= f;
        int xi = Mathf.FloorToInt(x);
        int zi = Mathf.FloorToInt(z);
        float fx = x - xi;
        float fz = z - zi;
        float u = fx * fx * (3f - 2f * fx);
        float w = fz * fz * (3f - 2f * fz);
        int x0 = ((xi % period) + period) % period;
        int x1 = (x0 + 1) % period;
        float a = Hash(x0 + seed * 1013, zi);
        float b = Hash(x1 + seed * 1013, zi);
        float c = Hash(x0 + seed * 1013, zi + 1);
        float d = Hash(x1 + seed * 1013, zi + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), w);
    }

    private static float Hash(int x, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + z * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 0xFFFF) / 65535f;
        }
    }

    private void RefreshMarks()
    {
        IReadOnlyList<string> visited = SaveManager.Instance != null ? SaveManager.Instance.VisitedBases : null;
        for (int i = 0; i < baseMarks.Count; i++)
        {
            bool known = IsVisited(visited, Planet.Bases[i].id);
            foreach (Renderer part in baseMarks[i].parts)
            {
                Material m = part.material;
                Color c = known ? (part.name == "Luz" ? Color.white : Mint) : new Color(0.55f, 0.58f, 0.64f);
                m.color = c;
                m.SetColor("_EmissionColor", known ? c * (part.name == "Luz" ? 2f : 0.8f) : Color.black);
            }
        }

        PlacePlayerArrow();
        SaveData data = SaveManager.Instance != null ? SaveManager.Instance.Data : null;
        waypointMark.gameObject.SetActive(data != null && data.hasWaypoint);
        if (data != null && data.hasWaypoint)
        {
            Place(waypointMark, data.waypoint, 1.0f);
        }
    }

    private static bool IsVisited(IReadOnlyList<string> list, string id)
    {
        if (list == null) return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == id) return true;
        }

        return false;
    }

    // Seta deitada no globo, virada para onde o personagem olha (+X = leste, +Z = norte).
    private void PlacePlayerArrow()
    {
        Vector2 latLong = LatLongOf(Planet.PlanetPosition(WorldOrigin.ToLogical(player.position)));
        float lat = latLong.x * Mathf.Deg2Rad;
        float lon = latLong.y * Mathf.Deg2Rad;
        Vector3 up = Direction(latLong.x, latLong.y);
        Vector3 east = new Vector3(Mathf.Cos(lon), 0f, Mathf.Sin(lon));
        Vector3 north = new Vector3(-Mathf.Sin(lat) * Mathf.Sin(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Cos(lon));
        Vector3 forward = player.forward;
        Vector3 heading = east * forward.x + north * forward.z;
        if (heading.sqrMagnitude < 0.0001f)
        {
            heading = north;
        }

        playerMark.localPosition = up * Radius * 1.014f;
        playerMark.localRotation = Quaternion.LookRotation(heading.normalized, up);
    }

    // ================================================================
    // Controles e seleção
    // ================================================================

    private void UpdateControls()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        Vector2 position = mouse.position.ReadValue();
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (mouse.leftButton.wasPressedThisFrame)
        {
            pressPosition = position;
            dragging = false;
        }

        if (mouse.leftButton.isPressed && !overUi)
        {
            Vector2 delta = mouse.delta.ReadValue();
            if ((position - pressPosition).sqrMagnitude > 36f)
            {
                dragging = true;
            }

            if (dragging)
            {
                float speed = dragSpeed * distance / 3f;
                yaw -= delta.x * speed;
                pitch = Mathf.Clamp(pitch + delta.y * speed, -85f, 85f);
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame && !dragging && !overUi && TryPick(position, out Vector2 picked))
        {
            Select(picked);
        }

        if (mouse.rightButton.wasPressedThisFrame && !overUi)
        {
            ClearWaypoint();
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f && !overUi)
        {
            distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * 0.2f, zoomRange.x, zoomRange.y);
        }

        // O globo fica no centro da área livre (à esquerda do painel).
        globe.localRotation = Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(yaw, Vector3.up);
        float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
        // Centro do globo a ~35% da largura (o painel ocupa ~1/3 da direita).
        float shift = 0.3f * distance * Mathf.Tan(globeCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * aspect;
        globeCamera.transform.localPosition = new Vector3(shift, 0f, -distance);
        globeCamera.transform.localRotation = Quaternion.identity;
        PlaceShade();

        float pulse = 0.05f + Mathf.PingPong(Time.unscaledTime * 0.04f, 0.012f);
        playerMark.localScale = Vector3.one * pulse;
        UpdatePath();
    }

    private void Select(Vector2 planetPosition)
    {
        hasSelection = true;
        selectedBase = -1;
        selectedPosition = planetPosition;

        // Clicou perto de uma base: seleciona a base.
        for (int i = 0; i < Planet.Bases.Length; i++)
        {
            Vector2 at = new Vector2(Planet.Bases[i].coord.x + 0.5f, Planet.Bases[i].coord.y + 0.5f);
            float dx = Mathf.Repeat(at.x - planetPosition.x + Planet.Width / 2f, Planet.Width) - Planet.Width / 2f;
            if (dx * dx + (at.y - planetPosition.y) * (at.y - planetPosition.y) < 5f * 5f)
            {
                selectedBase = i;
                selectedPosition = at;
            }
        }

        RefreshPanel();
    }

    private bool TryPick(Vector2 screen, out Vector2 planetPosition)
    {
        planetPosition = default;
        Ray ray = globeCamera.ScreenPointToRay(screen);
        Vector3 center = globe.position;
        Vector3 oc = ray.origin - center;
        float b = Vector3.Dot(oc, ray.direction);
        float c = oc.sqrMagnitude - Radius * Radius;
        float disc = b * b - c;
        if (disc < 0f)
        {
            return false;
        }

        float t = -b - Mathf.Sqrt(disc);
        Vector3 local = Quaternion.Inverse(globe.rotation) * (ray.origin + ray.direction * t - center).normalized;
        float lat = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
        float lon = Mathf.Atan2(local.x, -local.z) * Mathf.Rad2Deg;
        planetPosition = PlanetPositionOf(lat, lon);
        return true;
    }

    // Linha pontilhada de você até a seleção, pelo caminho mais curto sobre o globo.
    private void UpdatePath()
    {
        bool show = hasSelection;
        Vector3 from = LocalPoint(Planet.PlanetPosition(WorldOrigin.ToLogical(player.position)), 1f).normalized;
        Vector3 to = LocalPoint(selectedPosition, 1f).normalized;
        float angle = Vector3.Angle(from, to);
        int count = show ? Mathf.Clamp(Mathf.RoundToInt(angle / 2.2f), 2, pathDots.Count) : 0;
        for (int i = 0; i < pathDots.Count; i++)
        {
            bool on = i < count && i > 0 && i < count - 1;
            pathDots[i].gameObject.SetActive(on);
            if (on)
            {
                pathDots[i].localPosition = Vector3.Slerp(from, to, i / (float)(count - 1)) * Radius * 1.008f;
            }
        }

        selectionRing.gameObject.SetActive(show);
        if (show)
        {
            Place(selectionRing, selectedPosition, 1.003f);
        }
    }

    // ---------- Marcador ----------

    private void MarkSelection()
    {
        SaveData data = SaveManager.Instance != null ? SaveManager.Instance.Data : null;
        if (data == null || !hasSelection)
        {
            return;
        }

        data.hasWaypoint = true;
        data.waypoint = selectedPosition;
        RefreshMarks();
        RefreshPanel();
    }

    private void ClearWaypoint()
    {
        SaveData data = SaveManager.Instance != null ? SaveManager.Instance.Data : null;
        if (data != null && data.hasWaypoint)
        {
            data.hasWaypoint = false;
            if (root != null)
            {
                RefreshMarks();
                RefreshPanel();
            }
        }
    }

    private bool TryWaypointWorld(out Vector3 world)
    {
        world = default;
        SaveData data = SaveManager.Instance != null ? SaveManager.Instance.Data : null;
        if (data == null || !data.hasWaypoint || player == null)
        {
            return false;
        }

        Vector3 here = WorldOrigin.ToLogical(player.position);
        float x = Planet.GridOrigin.x + data.waypoint.x * Planet.BlockSize;
        float dx = Mathf.Repeat(x - here.x + Planet.Circumference / 2f, Planet.Circumference) - Planet.Circumference / 2f;
        Vector3 logical = new Vector3(here.x + dx, here.y, Planet.GridOrigin.y + data.waypoint.y * Planet.BlockSize);
        world = WorldOrigin.ToWorld(logical);
        return true;
    }

    private void CheckArrival()
    {
        if (!TryWaypointWorld(out Vector3 target))
        {
            return;
        }

        Vector3 offset = target - player.position;
        offset.y = 0f;
        if (offset.magnitude < arriveDistance)
        {
            ClearWaypoint();
            notice = "Você chegou ao destino";
            noticeUntil = Time.unscaledTime + 2.5f;
        }
    }

    private void TravelTo(int baseIndex)
    {
        Vector3 logical = Planet.BaseSpawnLogical(Planet.Bases[baseIndex], WorldOrigin.ToLogical(player.position));
        Close();
        if (PlayerHealth.Instance != null)
        {
            PlayerHealth.Instance.Teleport(WorldOrigin.ToWorld(logical), Quaternion.Euler(0f, 180f, 0f));
        }
    }

    // Distância em metros pelo planeta (leste-oeste dá a volta).
    private float DistanceTo(Vector2 planetPosition)
    {
        Vector2 here = Planet.PlanetPosition(WorldOrigin.ToLogical(player.position));
        float dx = Mathf.Repeat(planetPosition.x - here.x + Planet.Width / 2f, Planet.Width) - Planet.Width / 2f;
        float dz = planetPosition.y - here.y;
        return Mathf.Sqrt(dx * dx + dz * dz) * Planet.BlockSize;
    }

    private static string FormatDistance(float meters) =>
        meters >= 1000f ? $"{meters / 1000f:0.0} km".Replace('.', ',') : $"{Mathf.RoundToInt(meters / 10f) * 10} m";

    // ================================================================
    // Tela (uGUI)
    // ================================================================

    private void BuildScreen()
    {
        pill = MakeRoundedSprite(64, 32, "Pilula");
        roundBox = MakeRoundedSprite(96, 36, "Caixa");
        circle = MakeRoundedSprite(64, 32, "Circulo");

        GameObject canvasObject = new GameObject("Mapa_Tela");
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();
        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

        labelLayer = NewRect("Etiquetas", canvasRect);
        Stretch(labelLayer);

        // Título.
        RectTransform title = PillLabel(canvasRect, "MAPA PLANETÁRIO", fontBold, 38, Ink, Cream, new Vector2(34f, 18f), Coral, 6f);
        Anchor(title, new Vector2(0f, 1f), new Vector2(44f, -36f));

        // M FECHAR.
        RectTransform close = NewRect("Fechar", canvasRect);
        Anchor(close, new Vector2(1f, 1f), new Vector2(-44f, -40f));
        HorizontalLayoutGroup closeRow = close.gameObject.AddComponent<HorizontalLayoutGroup>();
        closeRow.spacing = 14f;
        closeRow.childAlignment = TextAnchor.MiddleRight;
        closeRow.childControlWidth = closeRow.childControlHeight = true;
        closeRow.childForceExpandWidth = closeRow.childForceExpandHeight = false;
        ContentSizeFitter closeFit = close.gameObject.AddComponent<ContentSizeFitter>();
        closeFit.horizontalFit = closeFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        close.pivot = new Vector2(1f, 1f);
        RectTransform key = PillLabel(close, "M", fontBold, 30, Ink, Cream, new Vector2(16f, 8f), new Color(0.55f, 0.48f, 0.39f), 0f, roundBox);
        key.gameObject.name = "Tecla";
        AddText(close, "FECHAR", fontSemi, 30, Cream);

        BuildPanel(canvasRect);
        BuildHints(canvasRect);

        // Etiquetas no globo.
        for (int region = 0; region < Planet.RegionCount; region++)
        {
            RectTransform label = PillLabel(labelLayer, Planet.RegionName(region).ToUpperInvariant(), fontSemi, 19, Ink, Cream, new Vector2(16f, 7f), Color.clear, 0f);
            label.pivot = new Vector2(0.5f, 0.5f);
            regionLabels.Add((label, Planet.RegionLabelPosition(region) + new Vector2(0f, 3f), label.gameObject.AddComponent<CanvasGroup>()));
        }

        for (int i = 0; i < Planet.Bases.Length; i++)
        {
            RectTransform label = PillLabel(labelLayer, Planet.Bases[i].name.ToUpperInvariant(), fontSemi, 17, Cream, new Color32(0x22, 0x28, 0x3A, 255), new Vector2(14f, 6f), Mint, 3f);
            label.pivot = new Vector2(0.5f, 1f);
            baseLabels.Add((label, i, label.GetComponentInChildren<Text>(), label.GetComponent<Image>(), label.gameObject.AddComponent<CanvasGroup>()));
        }

        youLabel = PillLabel(labelLayer, "VOCÊ", fontBold, 17, new Color(1f, 0.97f, 0.9f), Ink, new Vector2(14f, 6f), Color.clear, 0f);
        youLabel.pivot = new Vector2(0.5f, 1f);
        youGroup = youLabel.gameObject.AddComponent<CanvasGroup>();

        // Selos dos setores (aparecem com zoom).
        for (int sector = 0; sector < Planet.SectorCount; sector++)
        {
            RectTransform badge = NewRect("Setor", labelLayer);
            badge.sizeDelta = new Vector2(42f, 42f);
            Image back = badge.gameObject.AddComponent<Image>();
            back.sprite = circle;
            back.color = new Color32(0x22, 0x28, 0x3A, 230);
            Image fill = NewImage("Progresso", badge, circle, Mint);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            Stretch(fill.rectTransform, 4f);
            Image hole = NewImage("Miolo", badge, circle, new Color32(0x22, 0x28, 0x3A, 255));
            Stretch(hole.rectTransform, 11f);
            Image check = NewImage("Limpo", badge, circle, Gold);
            Stretch(check.rectTransform, 0f);
            // "✓" desenhado com dois traços (a fonte pode não ter o símbolo).
            Color tick = new Color32(0x5A, 0x3D, 0x0B, 255);
            Image shortStroke = NewImage("Tique", check.rectTransform, null, tick);
            shortStroke.rectTransform.sizeDelta = new Vector2(5f, 12f);
            shortStroke.rectTransform.anchoredPosition = new Vector2(-6f, -2f);
            shortStroke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 40f);
            Image longStroke = NewImage("Tique", check.rectTransform, null, tick);
            longStroke.rectTransform.sizeDelta = new Vector2(5f, 22f);
            longStroke.rectTransform.anchoredPosition = new Vector2(4f, 2f);
            longStroke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -38f);
            CanvasGroup group = badge.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            sectorBadges.Add((badge, sector, fill, check, group));
        }

        foreach (CanvasGroup g in labelLayer.GetComponentsInChildren<CanvasGroup>())
        {
            g.blocksRaycasts = false;
            g.interactable = false;
        }

        canvasObject.SetActive(false);
    }

    private void BuildPanel(RectTransform canvasRect)
    {
        RectTransform panel = NewRect("Painel", canvasRect);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-56f, -160f);
        panel.sizeDelta = new Vector2(560f, 0f);
        Image back = panel.gameObject.AddComponent<Image>();
        back.sprite = roundBox;
        back.type = Image.Type.Sliced;
        back.color = Cream;
        VerticalLayoutGroup frame = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        frame.padding = new RectOffset(6, 6, 6, 6);
        frame.childControlWidth = frame.childControlHeight = true;
        frame.childForceExpandHeight = false;
        ContentSizeFitter fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform inner = NewRect("Dentro", panel);
        Image innerBack = inner.gameObject.AddComponent<Image>();
        innerBack.sprite = roundBox;
        innerBack.type = Image.Type.Sliced;
        innerBack.color = Panel;
        VerticalLayoutGroup column = inner.gameObject.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(36, 36, 30, 32);
        column.spacing = 16f;
        column.childControlWidth = column.childControlHeight = true;
        column.childForceExpandHeight = false;

        // Cabeçalho: ícone + tipo + nome.
        RectTransform head = NewRect("Cabecalho", inner);
        HorizontalLayoutGroup headRow = head.gameObject.AddComponent<HorizontalLayoutGroup>();
        headRow.spacing = 18f;
        headRow.childAlignment = TextAnchor.MiddleLeft;
        headRow.childControlWidth = headRow.childControlHeight = true;
        headRow.childForceExpandWidth = false;
        panelIcon = NewImage("Icone", head, circle, Mint);
        LayoutElement iconSize = panelIcon.gameObject.AddComponent<LayoutElement>();
        iconSize.preferredWidth = iconSize.preferredHeight = 58f;
        RectTransform names = NewRect("Nomes", head);
        VerticalLayoutGroup namesColumn = names.gameObject.AddComponent<VerticalLayoutGroup>();
        namesColumn.childControlWidth = namesColumn.childControlHeight = true;
        namesColumn.childForceExpandHeight = false;
        namesColumn.spacing = 2f;
        panelKicker = AddText(names, "", fontSemi, 18, new Color32(0x9F, 0xDC, 0xC3, 255));
        panelTitle = AddText(names, "", fontBold, 40, new Color(1f, 0.97f, 0.9f));

        Divider(inner, Mint, 0.5f);

        panelDistance = AddText(inner, "", fontBold, 72, new Color(1f, 0.97f, 0.9f));
        panelDistance.alignment = TextAnchor.MiddleCenter;
        panelDistanceNote = AddText(inner, "", fontRegular, 19, MintSoft);
        panelDistanceNote.alignment = TextAnchor.MiddleCenter;

        markButton = MakeButton(inner, "MARCAR DESTINO", Coral, CoralDark, out markLabel, 30, 74f);
        markButton.onClick.AddListener(MarkSelection);
        travelButton = MakeButton(inner, "VIAJAR", new Color32(0x2E, 0x6B, 0x5C, 255), new Color32(0x14, 0x33, 0x2C, 255), out travelLabel, 22, 56f);
        travelButton.onClick.AddListener(() =>
        {
            if (selectedBase >= 0)
            {
                TravelTo(selectedBase);
            }
        });

        Divider(inner, Cream, 0.35f);

        RectTransform regionRow = Row(inner);
        regionName = AddText(regionRow, "", fontSemi, 23, Cream);
        regionCount = AddText(regionRow, "", fontSemi, 23, Cream);
        regionCount.alignment = TextAnchor.MiddleRight;

        RectTransform bar = NewRect("Barra", inner);
        LayoutElement barSize = bar.gameObject.AddComponent<LayoutElement>();
        barSize.preferredHeight = 12f;
        Image barBack = bar.gameObject.AddComponent<Image>();
        barBack.sprite = pill;
        barBack.type = Image.Type.Sliced;
        barBack.color = new Color32(0x12, 0x30, 0x29, 255);
        regionBar = NewImage("Cheio", bar, pill, Mint);
        regionBar.type = Image.Type.Sliced;
        regionBar.rectTransform.anchorMin = Vector2.zero;
        regionBar.rectTransform.anchorMax = new Vector2(0f, 1f);
        regionBar.rectTransform.pivot = new Vector2(0f, 0.5f);
        regionBar.rectTransform.offsetMin = regionBar.rectTransform.offsetMax = Vector2.zero;

        RectTransform sectorRow = Row(inner);
        sectorName = AddText(sectorRow, "", fontSemi, 21, MintSoft);
        sectorCount = AddText(sectorRow, "", fontSemi, 21, MintSoft);
        sectorCount.alignment = TextAnchor.MiddleRight;

        hintSelect = AddText(inner, "Clique no globo para escolher um lugar ou uma base.", fontRegular, 18, MintSoft).gameObject;
    }

    private void BuildHints(RectTransform canvasRect)
    {
        RectTransform hints = NewRect("Dicas", canvasRect);
        hints.anchorMin = hints.anchorMax = hints.pivot = new Vector2(0f, 0f);
        hints.anchoredPosition = new Vector2(44f, 36f);
        VerticalLayoutGroup column = hints.gameObject.AddComponent<VerticalLayoutGroup>();
        column.spacing = 8f;
        column.childControlWidth = column.childControlHeight = true;
        ContentSizeFitter fit = hints.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        AddText(hints, "ARRASTE PARA GIRAR", fontSemi, 22, Cream);
        AddText(hints, "RODINHA PARA ZOOM (DE PERTO: SETORES)", fontSemi, 22, Cream);
        AddText(hints, "CLIQUE PARA SELECIONAR  ·  BOTÃO DIREITO TIRA O DESTINO", fontSemi, 22, Cream);

        RectTransform legend = NewRect("Legenda", canvasRect);
        legend.anchorMin = legend.anchorMax = legend.pivot = new Vector2(1f, 0f);
        legend.anchoredPosition = new Vector2(-56f, 36f);
        HorizontalLayoutGroup row = legend.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 26f;
        row.childControlWidth = row.childControlHeight = true;
        row.childAlignment = TextAnchor.MiddleRight;
        ContentSizeFitter legendFit = legend.gameObject.AddComponent<ContentSizeFitter>();
        legendFit.horizontalFit = legendFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        LegendItem(legend, Mint, "base visitada");
        LegendItem(legend, new Color(0.55f, 0.58f, 0.64f), "não visitada");
        LegendItem(legend, Village, "Vilarejo");
        LegendItem(legend, Gold, "setor limpo");
    }

    private void LegendItem(RectTransform parent, Color color, string text)
    {
        RectTransform item = NewRect("Item", parent);
        HorizontalLayoutGroup row = item.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = row.childControlHeight = true;
        Image dot = NewImage("Cor", item, circle, color);
        LayoutElement size = dot.gameObject.AddComponent<LayoutElement>();
        size.preferredWidth = size.preferredHeight = 16f;
        AddText(item, text, fontRegular, 19, new Color(0.79f, 0.76f, 0.84f));
    }

    // ---------- Painel: conteúdo ----------

    private void RefreshPanel()
    {
        if (canvas == null)
        {
            return;
        }

        SectorTracker tracker = SectorTracker.Instance;
        Vector2 at = hasSelection ? selectedPosition : Planet.PlanetPosition(WorldOrigin.ToLogical(player.position));
        Vector2Int block = new Vector2Int(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.y));
        int region = Planet.RegionAt(block);
        int sector = Planet.SectorAt(block);
        IReadOnlyList<string> visited = SaveManager.Instance != null ? SaveManager.Instance.VisitedBases : null;
        SaveData data = SaveManager.Instance != null ? SaveManager.Instance.Data : null;

        if (selectedBase >= 0)
        {
            Planet.BaseInfo info = Planet.Bases[selectedBase];
            bool known = IsVisited(visited, info.id);
            panelKicker.text = $"BASE · {info.compass.ToUpperInvariant()}";
            panelTitle.text = known ? info.name.ToUpperInvariant() : "BASE NÃO DESCOBERTA";
            panelIcon.color = known ? Mint : new Color(0.55f, 0.58f, 0.64f);
        }
        else if (hasSelection)
        {
            panelKicker.text = region >= 0 ? "LUGAR NO MAPA" : "POLO";
            panelTitle.text = region >= 0 ? Planet.RegionName(region).ToUpperInvariant() : (at.y >= Planet.Height ? "POLO NORTE" : "POLO SUL");
            panelIcon.color = region >= 0 ? Planet.RegionColor(region) : Color.white;
        }
        else
        {
            panelKicker.text = "VOCÊ ESTÁ EM";
            panelTitle.text = region >= 0 ? Planet.RegionName(region).ToUpperInvariant() : "POLO";
            panelIcon.color = Arrow;
        }

        bool showDistance = hasSelection;
        panelDistance.gameObject.SetActive(showDistance);
        panelDistanceNote.gameObject.SetActive(showDistance);
        if (showDistance)
        {
            panelDistance.text = FormatDistance(DistanceTo(at));
            panelDistanceNote.text = "em linha reta, pelo caminho pontilhado";
        }

        bool isWaypoint = data != null && data.hasWaypoint && (data.waypoint - at).sqrMagnitude < 0.5f;
        markButton.gameObject.SetActive(hasSelection);
        markButton.interactable = !isWaypoint;
        markLabel.text = isWaypoint ? "DESTINO MARCADO" : "MARCAR DESTINO";

        // Viajar: só de dentro de uma base, para outra base já visitada.
        BaseSite inside = BaseSite.PlayerInside;
        bool canTravel = selectedBase >= 0 && inside != null && IsVisited(visited, Planet.Bases[selectedBase].id) &&
                         inside.BaseId != Planet.Bases[selectedBase].id;
        travelButton.gameObject.SetActive(selectedBase >= 0);
        travelButton.interactable = canTravel;
        travelLabel.text = canTravel ? "VIAJAR PARA CÁ"
            : inside == null ? "VIAJAR · só de dentro de uma base"
            : !IsVisited(visited, Planet.Bases[selectedBase >= 0 ? selectedBase : 0].id) ? "VIAJAR · visite esta base antes"
            : "VOCÊ ESTÁ AQUI";

        bool hasRegion = region >= 0 && tracker != null && tracker.PlanetMode;
        regionName.transform.parent.gameObject.SetActive(hasRegion);
        regionBar.transform.parent.gameObject.SetActive(hasRegion);
        sectorName.transform.parent.gameObject.SetActive(hasRegion && sector >= 0);
        if (hasRegion)
        {
            int quota = Mathf.Max(1, tracker.RegionQuotaOf(region));
            int done = tracker.RegionConsumedOf(region);
            regionName.text = Planet.RegionName(region).ToUpperInvariant();
            regionCount.text = tracker.IsRegionCleared(region) ? "LIMPA" : $"{done} / {quota}";
            regionBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(done / (float)quota), 1f);
            SectorTracker.SectorInfo info = tracker.Sectors[sector];
            sectorName.text = $"SETOR {info.name.ToUpperInvariant()}";
            sectorCount.text = info.cleared ? "LIMPO" : $"{info.consumed} / {info.quota}";
        }

        hintSelect.SetActive(!hasSelection);
    }

    // ---------- Etiquetas e selos (a cada quadro) ----------

    private void UpdateScreen()
    {
        RectTransform canvasRect = (RectTransform)canvas.transform;
        foreach ((RectTransform label, Vector2 position, CanvasGroup group) in regionLabels)
        {
            PlaceOnScreen(label, group, position, 1.03f, canvasRect, 0.35f, distance < sectorZoom ? 0.55f : 1f);
        }

        IReadOnlyList<string> visited = SaveManager.Instance != null ? SaveManager.Instance.VisitedBases : null;
        foreach ((RectTransform label, int index, Text text, Image back, CanvasGroup group) in baseLabels)
        {
            Planet.BaseInfo info = Planet.Bases[index];
            bool known = IsVisited(visited, info.id);
            text.text = known ? info.name.ToUpperInvariant() : "BASE ?";
            Outline outline = back.GetComponent<Outline>();
            if (outline != null)
            {
                outline.effectColor = known ? Mint : new Color(0.55f, 0.58f, 0.64f);
            }

            PlaceOnScreen(label, group, new Vector2(info.coord.x + 0.5f, info.coord.y - 1f), 1.02f, canvasRect, 0.2f, 1f);
        }

        PlaceOnScreen(youLabel, youGroup, Planet.PlanetPosition(WorldOrigin.ToLogical(player.position)) + new Vector2(0f, -1.6f), 1.02f, canvasRect, 0.2f, 1f);

        // Selos dos setores: só de perto.
        bool zoomed = distance < sectorZoom;
        SectorTracker tracker = SectorTracker.Instance;
        foreach ((RectTransform badge, int sector, Image fill, Image check, CanvasGroup group) in sectorBadges)
        {
            if (!zoomed || tracker == null || !tracker.PlanetMode || sector >= tracker.Sectors.Count)
            {
                group.alpha = 0f;
                continue;
            }

            SectorTracker.SectorInfo info = tracker.Sectors[sector];
            check.gameObject.SetActive(info.cleared);
            fill.fillAmount = info.quota > 0 ? Mathf.Clamp01(info.consumed / (float)info.quota) : 1f;
            PlaceOnScreen(badge, group, Planet.SectorLabelPosition(sector), 1.01f, canvasRect, 0.45f, 1f);
        }
    }

    // Põe um elemento da tela em cima de um ponto do globo; some quando o ponto está do outro lado.
    private void PlaceOnScreen(RectTransform element, CanvasGroup group, Vector2 planetPosition, float height, RectTransform canvasRect, float minFacing, float maxAlpha)
    {
        Vector3 local = LocalPoint(planetPosition, height);
        Vector3 world = globe.TransformPoint(local);
        Vector3 normal = globe.TransformDirection(local.normalized);
        float facing = Vector3.Dot(normal, (globeCamera.transform.position - world).normalized);
        float alpha = Mathf.Clamp01((facing - minFacing) / 0.25f) * maxAlpha;
        group.alpha = alpha;
        if (alpha <= 0.01f)
        {
            return;
        }

        Vector3 screen = globeCamera.WorldToScreenPoint(world);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 point);
        element.anchorMin = element.anchorMax = new Vector2(0.5f, 0.5f);
        element.anchoredPosition = point;
        float size = Mathf.Lerp(0.8f, 1f, facing);
        element.localScale = Vector3.one * size;
    }

    // ---------- Peças de interface ----------

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void Anchor(RectTransform rect, Vector2 corner, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = corner;
        rect.pivot = corner;
        rect.anchoredPosition = position;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        RectTransform rect = NewRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    private Text AddText(Transform parent, string value, Font font, int size, Color color)
    {
        RectTransform rect = NewRect("Texto", parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.text = value;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    // Etiqueta arredondada que se ajusta ao texto.
    private RectTransform PillLabel(Transform parent, string value, Font font, int size, Color textColor, Color back,
        Vector2 padding, Color border, float borderWidth, Sprite sprite = null)
    {
        RectTransform rect = NewRect("Etiqueta", parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite != null ? sprite : pill;
        image.type = Image.Type.Sliced;
        image.color = back;
        image.raycastTarget = false;
        if (borderWidth > 0f)
        {
            Outline outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = border;
            outline.effectDistance = new Vector2(borderWidth, -borderWidth);
        }

        HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset((int)padding.x, (int)padding.x, (int)padding.y, (int)padding.y);
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = row.childControlHeight = true;
        ContentSizeFitter fit = rect.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        Text text = AddText(rect, value, font, size, textColor);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.alignment = TextAnchor.MiddleCenter;
        return rect;
    }

    private Button MakeButton(Transform parent, string value, Color color, Color shadowColor, out Text label, int size, float height)
    {
        RectTransform rect = NewRect("Botao", parent);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = pill;
        image.type = Image.Type.Sliced;
        image.color = color;
        Shadow shadow = rect.gameObject.AddComponent<Shadow>();
        shadow.effectColor = shadowColor;
        shadow.effectDistance = new Vector2(0f, -6f);
        Button button = rect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
        colors.disabledColor = new Color(0.55f, 0.6f, 0.6f, 0.6f);
        button.colors = colors;
        label = AddText(rect, value, fontBold, size, new Color(1f, 0.97f, 0.9f));
        Stretch(label.rectTransform);
        label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private static RectTransform Row(Transform parent)
    {
        RectTransform rect = NewRect("Linha", parent);
        HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = true;
        return rect;
    }

    private static void Divider(Transform parent, Color color, float alpha)
    {
        RectTransform rect = NewRect("Linha", parent);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 2f;
        Image image = rect.gameObject.AddComponent<Image>();
        color.a = alpha;
        image.color = color;
    }

    // Sprite branco arredondado (9 fatias), para pílulas, caixas e círculos.
    private static Sprite MakeRoundedSprite(int size, int radius, string name)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                byte a = (byte)(Mathf.Clamp01(radius - d + 0.5f) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
    }

    // ---------- Marcador na tela do jogo (fora do mapa) ----------

    private GUIStyle hudStyle;
    private GUIStyle iconStyle;

    private void OnGUI()
    {
        if (open)
        {
            return;
        }

        float scale = Screen.height / 1080f;
        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            if (fontBold != null)
            {
                hudStyle.font = fontBold;
            }
        }

        hudStyle.fontSize = Mathf.RoundToInt(22 * scale);
        if (!string.IsNullOrEmpty(notice) && Time.unscaledTime < noticeUntil)
        {
            hudStyle.normal.textColor = Cream;
            GUI.Label(new Rect(0f, Screen.height * 0.2f, Screen.width, 40f * scale), notice, hudStyle);
        }

        if (mainCamera == null || Cursor.lockState != CursorLockMode.Locked || !TryWaypointWorld(out Vector3 target))
        {
            return;
        }

        Vector3 screen = mainCamera.WorldToScreenPoint(target + Vector3.up * 2f);
        bool behind = screen.z < 0f;
        if (behind)
        {
            screen = -screen;
        }

        float margin = 40f * scale;
        Vector2 point = new Vector2(Mathf.Clamp(screen.x, margin, Screen.width - margin), Mathf.Clamp(screen.y, margin, Screen.height - margin));
        if (behind)
        {
            point.y = margin;
        }

        bool offScreen = behind || point.x != screen.x || point.y != screen.y;
        Vector3 flat = target - player.position;
        flat.y = 0f;
        if (iconStyle == null)
        {
            iconStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter }; // fonte padrão: tem ◆ e ▼
        }

        iconStyle.fontSize = Mathf.RoundToInt(26 * scale);
        iconStyle.normal.textColor = Arrow;
        GUI.Label(new Rect(point.x - 60f * scale, Screen.height - point.y - 26f * scale, 120f * scale, 30f * scale), offScreen ? "◆" : "▼", iconStyle);
        hudStyle.normal.textColor = Cream;
        GUI.Label(new Rect(point.x - 90f * scale, Screen.height - point.y + 2f * scale, 180f * scale, 28f * scale), FormatDistance(flat.magnitude), hudStyle);
    }
}
