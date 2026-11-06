using UnityEngine;
using UnityEngine.UI;

// Interface da Versão 0.2 (decisão D11), no canto superior direito:
//   ALIENÍGENAS CONSUMIDOS   1 / 3   <- OBJETIVO do setor (cada alienígena conta 1)
//   DNA                         20   <- RECURSO (paga evoluções; "Devorar" vai render o dobro)
// O mesmo canto vai receber o minimapa no futuro: marque "Reserve Minimap Space"
// e os contadores descem para baixo dele.
// Também mostra o aviso "SETOR LIMPO!" quando todos os alienígenas são consumidos.
// Versão 0.3: barra de VIDA no canto superior esquerdo, flash vermelho ao levar dano
// e o aviso "VOCÊ FOI DERROTADO".
// A interface é montada por código ao dar Play; não aparece na cena fora do Play.
[DisallowMultipleComponent]
public sealed class GameHud : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private bool reserveMinimapSpace;
    [SerializeField, Min(0f)] private float minimapSize = 220f;
    [SerializeField, Min(0f)] private float margin = 24f;

    [Header("Marcadores dos últimos alienígenas")]
    [Tooltip("Quando restarem esta quantidade de alienígenas vivos (ou menos), marcadores mostram onde eles estão.")]
    [SerializeField, Min(0)] private int markerThreshold = 3;
    [SerializeField] private Color civilMarkerColor = new Color(1f, 0.82f, 0.3f, 1f);
    [SerializeField] private Color policeMarkerColor = new Color(0.4f, 0.6f, 1f, 1f);

    [Header("Cores")]
    [SerializeField] private Color alienColor = new Color(1f, 0.82f, 0.3f, 1f);
    [SerializeField] private Color dnaColor = new Color(0.35f, 1f, 0.65f, 1f);
    [SerializeField] private Color bannerColor = new Color(1f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color healthColor = new Color(0.95f, 0.3f, 0.4f, 1f);
    [SerializeField] private Color healthLowColor = new Color(1f, 0.15f, 0.15f, 1f);

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
    private PlayerHealth playerHealth;

    private RectTransform healthFill;
    private Image healthFillImage;
    private Text healthText;
    private Image damageFlash;
    private Text deathText;
    private float flashAlpha;
    private float shownHealth = 1f;
    private Text abilitiesText;
    private Text evolutionHintText;
    private DevourAbility devour;
    private ShockwaveAbility shockwave;
    private GUIStyle markerStyle;

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

        playerHealth = PlayerHealth.Instance;
        if (playerHealth != null)
        {
            devour = playerHealth.GetComponent<DevourAbility>();
            shockwave = playerHealth.GetComponent<ShockwaveAbility>();

            playerHealth.Damaged += OnPlayerDamaged;
            playerHealth.Died += OnPlayerDied;
            playerHealth.Respawned += OnPlayerRespawned;
        }
        else
        {
            healthFill.parent.gameObject.SetActive(false); // sem vida do jogador na cena: esconde a barra
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

        if (playerHealth != null)
        {
            playerHealth.Damaged -= OnPlayerDamaged;
            playerHealth.Died -= OnPlayerDied;
            playerHealth.Respawned -= OnPlayerRespawned;
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

        UpdateHealthBar(dt);
        UpdateAbilities();

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

    private void UpdateHealthBar(float dt)
    {
        if (playerHealth != null)
        {
            float fraction = playerHealth.CurrentHealth / playerHealth.MaxHealth;
            shownHealth = Mathf.MoveTowards(shownHealth, fraction, dt * 2f); // desce/sobe suave
            healthFill.anchorMax = new Vector2(Mathf.Clamp01(shownHealth), 1f);
            bool low = fraction < 0.3f;
            healthFillImage.color = low
                ? Color.Lerp(healthLowColor, Color.white, (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.15f) // pisca com vida baixa
                : healthColor;
            healthText.text = $"VIDA  {Mathf.CeilToInt(playerHealth.CurrentHealth)}";
        }

        flashAlpha = Mathf.Max(0f, flashAlpha - dt * 2.5f);
        damageFlash.color = new Color(1f, 0f, 0.05f, flashAlpha * 0.35f);
        damageFlash.enabled = flashAlpha > 0f;
    }

    // Linha de habilidades (teclas) e aviso de evolução disponível, embaixo da vida.
    private void UpdateAbilities()
    {
        if (shockwave == null && playerHealth != null)
        {
            shockwave = playerHealth.GetComponent<ShockwaveAbility>(); // pode ser criada depois do início
        }

        string line = "";
        if (devour != null && devour.Unlocked)
        {
            line += "[1] Devorar     ";
        }

        if (shockwave != null && shockwave.Unlocked)
        {
            float wait = shockwave.CooldownRemaining;
            line += wait > 0f ? $"[2] Onda {Mathf.CeilToInt(wait)}s     " : "[2] Onda pronta     ";
        }

        EvolutionSystem evolutions = EvolutionSystem.Instance;
        if (evolutions != null)
        {
            line += "[Tab] Evoluções";
        }

        abilitiesText.text = line;

        bool canEvolve = evolutions != null && evolutions.AnyAffordable();
        evolutionHintText.enabled = canEvolve;
        if (canEvolve)
        {
            Color color = dnaColor;
            color.a = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3f));
            evolutionHintText.color = color;
        }
    }

    private void OnPlayerDamaged(Vector3 source)
    {
        flashAlpha = 1f;
    }

    private void OnPlayerDied()
    {
        deathText.enabled = true;
        flashAlpha = 1.5f;
    }

    private void OnPlayerRespawned()
    {
        deathText.enabled = false;
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

    // ---------- Marcadores dos últimos alienígenas ----------

    private void OnGUI()
    {
        if (tracker == null || tracker.IsCleared || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        int alive = tracker.AliveCount;
        if (alive == 0 || alive > markerThreshold)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        markerStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 15,
            fontStyle = FontStyle.Bold,
        };

        Transform player = PlayerHealth.Instance != null ? PlayerHealth.Instance.transform : cam.transform;
        foreach (AlienHealth alien in tracker.Aliens)
        {
            if (alien == null || alien.IsDead)
            {
                continue;
            }

            bool police = alien.TryGetComponent(out AlienBrain brain) && brain.AlienRole == AlienBrain.Role.Policial;
            Color color = police ? policeMarkerColor : civilMarkerColor;
            Vector3 head = alien.transform.position + Vector3.up * 3.3f;
            int meters = Mathf.RoundToInt(Vector3.Distance(player.position, alien.transform.position));
            DrawMarker(cam, head, color, meters + " m");
        }
    }

    // Marcador sobre o alienígena; se estiver fora da tela, fica preso na borda apontando a direção.
    private void DrawMarker(Camera cam, Vector3 worldPosition, Color color, string label)
    {
        const float edge = 40f;
        Vector3 screen = cam.WorldToScreenPoint(worldPosition);
        bool behind = screen.z < 0f;
        if (behind)
        {
            screen *= -1f; // atrás da câmera: inverte para apontar para o lado certo
        }

        Vector2 point = new Vector2(screen.x, Screen.height - screen.y);
        bool onScreen = !behind && point.x > edge && point.x < Screen.width - edge && point.y > edge &&
                        point.y < Screen.height - edge;

        if (!onScreen)
        {
            // Projeta a direção a partir do centro até a borda da tela.
            Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Vector2 direction = point - center;
            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector2.down;
            }

            float scaleX = (Screen.width / 2f - edge) / Mathf.Max(Mathf.Abs(direction.x), 0.001f);
            float scaleY = (Screen.height / 2f - edge) / Mathf.Max(Mathf.Abs(direction.y), 0.001f);
            point = center + direction * Mathf.Min(scaleX, scaleY);
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.15f;
        float size = (onScreen ? 14f : 18f) * pulse;

        // Losango (quadrado girado) com contorno escuro.
        Matrix4x4 previous = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, point);
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.DrawTexture(new Rect(point.x - size / 2f - 2f, point.y - size / 2f - 2f, size + 4f, size + 4f), Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(point.x - size / 2f, point.y - size / 2f, size, size), Texture2D.whiteTexture);
        GUI.matrix = previous;

        GUI.color = Color.white;
        markerStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(point.x - 49f, point.y + 15f, 100f, 22f), label, markerStyle);
        markerStyle.normal.textColor = color;
        GUI.Label(new Rect(point.x - 50f, point.y + 14f, 100f, 22f), label, markerStyle);
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

        // Barra de vida no canto superior esquerdo.
        RectTransform healthPanel = NewRect("Vida", canvasObject.transform);
        healthPanel.anchorMin = healthPanel.anchorMax = healthPanel.pivot = new Vector2(0f, 1f);
        healthPanel.anchoredPosition = new Vector2(margin, -margin);
        healthPanel.sizeDelta = new Vector2(360f, 34f);
        healthPanel.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.04f, 0.1f, 0.6f);

        healthFill = NewRect("Vida_Preenchimento", healthPanel);
        healthFill.anchorMin = Vector2.zero;
        healthFill.anchorMax = Vector2.one;
        healthFill.offsetMin = new Vector2(3f, 3f);
        healthFill.offsetMax = new Vector2(-3f, -3f);
        healthFillImage = healthFill.gameObject.AddComponent<Image>();
        healthFillImage.color = healthColor;

        healthText = NewText("Vida_Texto", healthPanel, 20, Color.white, TextAnchor.MiddleLeft);
        RectTransform healthTextRect = healthText.rectTransform;
        healthTextRect.anchorMin = Vector2.zero;
        healthTextRect.anchorMax = Vector2.one;
        healthTextRect.offsetMin = new Vector2(12f, 0f);
        healthTextRect.offsetMax = Vector2.zero;
        healthText.fontStyle = FontStyle.Bold;

        // Teclas das habilidades e aviso de evolução, embaixo da vida.
        abilitiesText = NewText("Habilidades", canvasObject.transform, 20, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperLeft);
        RectTransform abilitiesRect = abilitiesText.rectTransform;
        abilitiesRect.anchorMin = abilitiesRect.anchorMax = abilitiesRect.pivot = new Vector2(0f, 1f);
        abilitiesRect.anchoredPosition = new Vector2(margin, -margin - 44f);
        abilitiesRect.sizeDelta = new Vector2(700f, 28f);

        evolutionHintText = NewText("Aviso_Evolucao", canvasObject.transform, 22, dnaColor, TextAnchor.UpperLeft);
        RectTransform hintRect = evolutionHintText.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(0f, 1f);
        hintRect.anchoredPosition = new Vector2(margin, -margin - 74f);
        hintRect.sizeDelta = new Vector2(700f, 30f);
        evolutionHintText.fontStyle = FontStyle.Bold;
        evolutionHintText.text = "EVOLUÇÃO DISPONÍVEL!  Aperte Tab";
        evolutionHintText.enabled = false;

        // Flash vermelho na tela inteira ao levar dano.
        RectTransform flash = NewRect("Flash_Dano", canvasObject.transform);
        flash.anchorMin = Vector2.zero;
        flash.anchorMax = Vector2.one;
        flash.offsetMin = flash.offsetMax = Vector2.zero;
        damageFlash = flash.gameObject.AddComponent<Image>();
        damageFlash.raycastTarget = false;
        damageFlash.enabled = false;
        flash.SetAsFirstSibling(); // atrás do resto da interface

        // Aviso de derrota.
        deathText = NewText("Aviso_Derrota", canvasObject.transform, 72, new Color(1f, 0.3f, 0.3f, 1f), TextAnchor.MiddleCenter);
        RectTransform death = deathText.rectTransform;
        death.anchorMin = death.anchorMax = death.pivot = new Vector2(0.5f, 0.5f);
        death.sizeDelta = new Vector2(1200f, 220f);
        deathText.fontStyle = FontStyle.Bold;
        deathText.text = "VOCÊ FOI DERROTADO\n<size=30>renascendo...</size>";
        deathText.enabled = false;

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
