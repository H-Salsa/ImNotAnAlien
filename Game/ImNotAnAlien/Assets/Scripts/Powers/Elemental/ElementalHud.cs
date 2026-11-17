using UnityEngine;
using UnityEngine.UI;

// Interface do Poder Elemental (Missão 21), no canto de BAIXO, à DIREITA:
//   [fogo] [gelo] [raio]     <- os três ícones; o escolhido fica maior e aceso
//   FOGO
//   [=========     ]         <- barra de energia, na cor do elemento
//   Q / roda: trocar · botão esquerdo: disparar (segure para carregar)
// Também desenha, no meio da tela, um anel que fecha enquanto o disparo carrega.
// Montada por código dentro do HUD do jogo (some junto com ele quando o mapa abre).
[DisallowMultipleComponent]
public sealed class ElementalHud : MonoBehaviour
{
    private const float IconSize = 66f;
    private const float PanelWidth = 300f;
    private const float PanelHeight = 168f;

    private ElementalPower power;
    private Canvas ownCanvas;
    private RectTransform panel;
    private readonly Image[] icons = new Image[3];
    private readonly RectTransform[] iconRects = new RectTransform[3];
    private Image ring;
    private Text nameText;
    private Text hintText;
    private RectTransform fill;
    private Image fillImage;
    private Image barBack;
    private Texture2D ringTexture;

    private float shownEnergy = 1f;
    private float punch;
    private float denyFlash;
    private int lastSwitch;
    private int lastDeny;
    private float searchTime;
    private bool built;

    public void Bind(ElementalPower owner)
    {
        power = owner;
    }

    private void Update()
    {
        if (power == null)
        {
            return;
        }

        if (!built)
        {
            TryBuild();
            return;
        }

        float dt = Time.unscaledDeltaTime;
        ElementKind active = power.Element;
        Color main = ElementalFx.Main(active);

        if (power.SwitchCount != lastSwitch)
        {
            lastSwitch = power.SwitchCount;
            punch = 1f;
        }

        if (power.DenyCount != lastDeny)
        {
            lastDeny = power.DenyCount;
            denyFlash = 1f;
        }

        punch = Mathf.MoveTowards(punch, 0f, dt * 4f);
        denyFlash = Mathf.MoveTowards(denyFlash, 0f, dt * 2.5f);

        // Ícones: o escolhido maior e aceso; os outros apagados (bloqueados: quase invisíveis).
        for (int i = 0; i < icons.Length; i++)
        {
            bool isActive = i == (int)active;
            bool unlocked = power.IsUnlocked((ElementKind)i);
            float scale = isActive ? 1.22f + 0.3f * punch : 0.82f;
            iconRects[i].localScale = Vector3.Lerp(iconRects[i].localScale, Vector3.one * scale, 1f - Mathf.Exp(-18f * dt));
            Color color = icons[i].color;
            color.a = Mathf.Lerp(color.a, !unlocked ? 0.12f : isActive ? 1f : 0.5f, 1f - Mathf.Exp(-14f * dt));
            icons[i].color = color;
        }

        // Aro em volta do ícone escolhido.
        ring.rectTransform.anchoredPosition = Vector2.Lerp(ring.rectTransform.anchoredPosition, iconRects[(int)active].anchoredPosition,
            1f - Mathf.Exp(-20f * dt));
        Color ringColor = main;
        ringColor.a = 0.55f + 0.45f * punch;
        ring.color = ringColor;
        ring.rectTransform.localScale = Vector3.one * (1f + 0.25f * punch);

        nameText.text = ElementalFx.Name(active);
        nameText.color = main;

        // Barra de energia (anda macio; pisca vermelha quando falta energia).
        shownEnergy = Mathf.Lerp(shownEnergy, power.Energy01, 1f - Mathf.Exp(-12f * dt));
        fill.anchorMax = new Vector2(Mathf.Clamp01(shownEnergy), 1f);
        Color barColor = power.IsCharging ? Color.Lerp(main, Color.white, 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 18f)) : main;
        fillImage.color = Color.Lerp(barColor, new Color(1f, 0.2f, 0.2f), denyFlash);
        barBack.color = Color.Lerp(new Color(0.05f, 0.04f, 0.1f, 0.75f), new Color(0.5f, 0.05f, 0.05f, 0.85f), denyFlash);
    }

    // O HUD do jogo monta a tela no Start dele: espera um pouco; se não houver, usa uma tela própria.
    private void TryBuild()
    {
        searchTime += Time.unscaledDeltaTime;
        Transform parent = null;
        if (GameHud.Instance != null)
        {
            parent = GameHud.Instance.transform.Find("HUD_Canvas");
        }

        if (parent == null)
        {
            if (searchTime < 1.5f)
            {
                return;
            }

            GameObject canvasObject = new GameObject("HUD_Elemental_Canvas");
            canvasObject.transform.SetParent(transform, false);
            ownCanvas = canvasObject.AddComponent<Canvas>();
            ownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            ownCanvas.sortingOrder = 11;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            parent = canvasObject.transform;
        }

        Build(parent);
        built = true;
    }

    private void OnDestroy()
    {
        if (panel != null)
        {
            Destroy(panel.gameObject);
        }
    }

    private void Build(Transform parent)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ringTexture = ElementalFx.Tex("fx_ring");

        panel = NewRect("Poder_Elemental", parent);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-24f, 24f);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.05f, 0.04f, 0.1f, 0.45f);
        background.raycastTarget = false;

        // Aro (atrás dos ícones).
        ring = NewRect("Aro", panel).gameObject.AddComponent<Image>();
        ring.sprite = MakeSprite(ringTexture);
        ring.raycastTarget = false;
        RectTransform ringRect = ring.rectTransform;
        ringRect.anchorMin = ringRect.anchorMax = new Vector2(0.5f, 1f);
        ringRect.pivot = new Vector2(0.5f, 0.5f);
        ringRect.sizeDelta = Vector2.one * IconSize * 1.75f;

        string[] names = { "icon_fogo", "icon_gelo", "icon_raio" };
        for (int i = 0; i < 3; i++)
        {
            RectTransform rect = NewRect("Icone_" + ElementalFx.Name((ElementKind)i), panel);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * IconSize;
            rect.anchoredPosition = new Vector2((i - 1) * 90f, -52f);
            Image image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            Texture2D texture = Resources.Load<Texture2D>("Elemental/" + names[i]);
            if (texture != null)
            {
                image.sprite = MakeSprite(texture);
            }
            else
            {
                image.sprite = MakeSprite(ElementalFx.Tex("fx_glow")); // sem o ícone: uma bolinha na cor do elemento
                image.color = ElementalFx.Main((ElementKind)i);
            }

            icons[i] = image;
            iconRects[i] = rect;
        }

        ringRect.anchoredPosition = iconRects[0].anchoredPosition;

        nameText = NewText("Nome", panel, font, 22, TextAnchor.MiddleLeft);
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 0f);
        nameRect.pivot = new Vector2(0.5f, 0f);
        nameRect.sizeDelta = new Vector2(-32f, 26f);
        nameRect.anchoredPosition = new Vector2(0f, 46f);
        nameText.fontStyle = FontStyle.Bold;

        Text energyLabel = NewText("Energia_Texto", panel, font, 16, TextAnchor.MiddleRight);
        RectTransform energyRect = energyLabel.rectTransform;
        energyRect.anchorMin = new Vector2(0f, 0f);
        energyRect.anchorMax = new Vector2(1f, 0f);
        energyRect.pivot = new Vector2(0.5f, 0f);
        energyRect.sizeDelta = new Vector2(-32f, 26f);
        energyRect.anchoredPosition = new Vector2(0f, 46f);
        energyLabel.text = "ENERGIA";
        energyLabel.color = new Color(1f, 1f, 1f, 0.7f);

        RectTransform bar = NewRect("Energia", panel);
        bar.anchorMin = new Vector2(0f, 0f);
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.sizeDelta = new Vector2(-32f, 16f);
        bar.anchoredPosition = new Vector2(0f, 28f);
        barBack = bar.gameObject.AddComponent<Image>();
        barBack.raycastTarget = false;

        fill = NewRect("Energia_Preenchimento", bar);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.raycastTarget = false;

        hintText = NewText("Dica", panel, font, 13, TextAnchor.MiddleCenter);
        RectTransform hintRect = hintText.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.sizeDelta = new Vector2(-12f, 20f);
        hintRect.anchoredPosition = new Vector2(0f, 5f);
        hintText.color = new Color(1f, 1f, 1f, 0.75f);
        hintText.text = "Q / roda: trocar  ·  botão esq.: disparar (segure: carrega)";
    }

    private static Sprite MakeSprite(Texture2D texture)
    {
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Text NewText(string name, Transform parent, Font font, int size, TextAnchor alignment)
    {
        RectTransform rect = NewRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    // Anel de carga no meio da tela: fecha conforme a carga sobe; pisca quando está cheia.
    private void OnGUI()
    {
        if (power == null || !power.IsCharging || ringTexture == null || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        float charge = power.Charge01;
        float scale = Screen.height / 1080f;
        float size = Mathf.Lerp(110f, 44f, charge) * scale;
        Color color = ElementalFx.Main(power.Element);
        color.a = charge >= 0.999f ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 22f) : Mathf.Lerp(0.35f, 0.9f, charge);
        GUI.color = color;
        GUI.DrawTexture(new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f, size, size), ringTexture);
        GUI.color = Color.white;
    }
}
