using UnityEngine;
using UnityEngine.UI;

// Interface da Versão 0.2 (decisão D11), no canto superior direito:
//   ALIENÍGENAS CONSUMIDOS   1 / 3   <- OBJETIVO do setor (cada alienígena conta 1)
//   DNA                         20   <- RECURSO (paga evoluções; "Devorar" vai render o dobro)
// O mesmo canto vai receber o minimapa no futuro: marque "Reserve Minimap Space"
// e os contadores descem para baixo dele.
// Também mostra o aviso "SETOR LIMPO!" quando todos os alienígenas são consumidos.
// A interface é montada por código ao dar Play; não aparece na cena fora do Play.
[DisallowMultipleComponent]
public sealed class GameHud : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private bool reserveMinimapSpace;
    [SerializeField, Min(0f)] private float minimapSize = 220f;
    [SerializeField, Min(0f)] private float margin = 24f;

    [Header("Cores")]
    [SerializeField] private Color alienColor = new Color(1f, 0.82f, 0.3f, 1f);
    [SerializeField] private Color dnaColor = new Color(0.35f, 1f, 0.65f, 1f);
    [SerializeField] private Color bannerColor = new Color(1f, 0.85f, 0.25f, 1f);

    private const float PanelWidth = 420f;
    private const float RowHeight = 46f;

    private Font font;
    private Text alienText;
    private Text dnaText;
    private Text bannerText;
    private RectTransform alienRow;
    private RectTransform dnaRow;
    private float alienPunch;
    private float dnaPunch;
    private float bannerTimer;

    private SectorTracker tracker;
    private DnaInventory inventory;

    private void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildInterface();

        tracker = SectorTracker.Instance;
        inventory = DnaInventory.Instance;

        if (tracker != null)
        {
            tracker.Changed += OnSectorChanged;
            tracker.Cleared += OnSectorCleared;
        }
        else
        {
            Debug.LogWarning("GameHud: SectorTracker não encontrado na cena.", this);
        }

        if (inventory != null)
        {
            inventory.Changed += OnDnaChanged;
        }
        else
        {
            Debug.LogWarning("GameHud: DnaInventory não encontrado no jogador.", this);
        }

        RefreshAlienText();
        RefreshDnaText();
    }

    private void OnDestroy()
    {
        if (tracker != null)
        {
            tracker.Changed -= OnSectorChanged;
            tracker.Cleared -= OnSectorCleared;
        }

        if (inventory != null)
        {
            inventory.Changed -= OnDnaChanged;
        }
    }

    private void Update()
    {
        // Tempo "sem escala": anima normalmente mesmo durante a pausa de impacto.
        float dt = Time.unscaledDeltaTime;

        alienPunch = Mathf.Max(0f, alienPunch - dt * 4f);
        dnaPunch = Mathf.Max(0f, dnaPunch - dt * 4f);
        alienRow.localScale = Vector3.one * (1f + 0.25f * alienPunch * alienPunch);
        dnaRow.localScale = Vector3.one * (1f + 0.25f * dnaPunch * dnaPunch);

        if (bannerTimer > 0f)
        {
            bannerTimer -= dt;
            Color color = bannerColor;
            color.a = Mathf.Clamp01(bannerTimer / 0.8f); // some nos últimos 0,8 s
            bannerText.color = color;
            float pop = Mathf.Clamp01((3.5f - bannerTimer) / 0.25f); // cresce nos primeiros 0,25 s
            bannerText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, pop);
            bannerText.enabled = bannerTimer > 0f;
        }
    }

    // ---------- Eventos ----------

    private void OnSectorChanged()
    {
        RefreshAlienText();
        alienPunch = 1f;
    }

    private void OnDnaChanged(int total, int delta)
    {
        RefreshDnaText();
        dnaPunch = 1f;
    }

    private void OnSectorCleared(SectorTracker sector)
    {
        bannerText.text = $"SETOR LIMPO!\n<size=30>+{sector.ClearBonusDna} DNA de bônus</size>";
        bannerText.enabled = true;
        bannerTimer = 3.5f;
    }

    private void RefreshAlienText()
    {
        int consumed = tracker != null ? tracker.ConsumedAliens : 0;
        int total = tracker != null ? tracker.TotalAliens : 0;
        alienText.text = $"<size=20>ALIENÍGENAS CONSUMIDOS</size>   <b>{consumed} / {total}</b>";
    }

    private void RefreshDnaText()
    {
        int total = inventory != null ? inventory.Total : 0;
        dnaText.text = $"<size=20>DNA</size>   <b>{total}</b>";
    }

    // ---------- Montagem da interface ----------

    private void BuildInterface()
    {
        GameObject canvasObject = new GameObject("HUD_Canvas");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Painel no canto superior direito (abaixo do minimapa, se reservado).
        float top = margin + (reserveMinimapSpace ? minimapSize + 12f : 0f);
        RectTransform panel = NewRect("Contadores", canvasObject.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-margin, -top);
        panel.sizeDelta = new Vector2(PanelWidth, RowHeight * 2f + 16f);
        Image background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.05f, 0.04f, 0.1f, 0.45f);

        alienText = NewText("Alienigenas", panel, 34, alienColor, TextAnchor.MiddleRight);
        alienRow = alienText.rectTransform;
        PlaceRow(alienRow, 0);

        dnaText = NewText("DNA", panel, 34, dnaColor, TextAnchor.MiddleRight);
        dnaRow = dnaText.rectTransform;
        PlaceRow(dnaRow, 1);

        // Aviso central de "SETOR LIMPO!".
        bannerText = NewText("Aviso_Setor", canvasObject.transform, 64, bannerColor, TextAnchor.MiddleCenter);
        RectTransform banner = bannerText.rectTransform;
        banner.anchorMin = banner.anchorMax = banner.pivot = new Vector2(0.5f, 0.72f);
        banner.sizeDelta = new Vector2(900f, 180f);
        bannerText.fontStyle = FontStyle.Bold;
        bannerText.enabled = false;
    }

    private static void PlaceRow(RectTransform row, int index)
    {
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(1f, 0.5f); // cresce "para dentro" no efeito de pulso
        row.sizeDelta = new Vector2(-24f, RowHeight);
        row.anchoredPosition = new Vector2(-12f, -8f - RowHeight * (index + 0.5f));
    }

    private RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private Text NewText(string name, Transform parent, int size, Color color, TextAnchor alignment)
    {
        RectTransform rect = NewRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        Outline outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }
}
