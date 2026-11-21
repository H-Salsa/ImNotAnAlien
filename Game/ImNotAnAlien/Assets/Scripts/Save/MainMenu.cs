using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

// Tela inicial (20/11, substitui o menu provisório da Missão 17).
// 1. Abertura: o vídeo "Abertura_H-19" em tela cheia (clique/tecla pula). Só na primeira vez que o
//    jogo abre; voltando do jogo para o menu vai direto para o passo 2.
// 2. Menu: ao fundo a mesma cena do fim do vídeo (Menu_Fundo, já com o logo), quase parada (zoom bem lento);
//    botões do lado esquerdo, embaixo do logo, cada um com a cor de uma loja:
//    Novo jogo, Carregar jogo, Multijogador, Configurações, Sair.
//    - Novo jogo / Carregar jogo: os 3 espaços de jogo (SaveSystem). Novo jogo em espaço ocupado pede confirmação.
//    - Multijogador: "em breve" (decisão do autor: 1 jogador hospeda o mundo, até 5 no total).
//    - Configurações: volume, sensibilidade do mouse, tela cheia, qualidade, VSync (GameSettings).
// Arte em Assets/Resources/Menu (Abertura_H-19.mp4, Menu_Fundo.png; Menu_Rua.png = tela "Carregando";
// Logo_ImNotAnAlien.png só se faltar o fundo) e fonte Fredoka
// em Assets/Resources/Fonts. Tudo é montado por código ao abrir a cena MenuInicial.
[DisallowMultipleComponent]
public sealed class MainMenu : MonoBehaviour
{
    [SerializeField] private string gameSceneName = PlanetScene;

    // A cena do jogo é o planeta. A cena antiga do Vilarejo (Missão 17) ficou no projeto e a ferramenta
    // antiga ainda grava o nome dela: se o planeta estiver no Build, ele sempre ganha (20/11).
    private const string PlanetScene = "Mundo_Sorteado";
    private string SceneToLoad => Application.CanStreamedLevelBeLoaded(PlanetScene) ? PlanetScene : gameSceneName;

    // Cores das lojas (as mesmas do atlas das casas) e da interface.
    private static readonly Color Coral = Hex("E2735A");
    private static readonly Color Mint = Hex("5FAE8C");
    private static readonly Color Petrol = Hex("3F6E95");
    private static readonly Color Mustard = Hex("D9A23E");
    private static readonly Color Orange = Hex("E27D4E");
    private static readonly Color Cream = Hex("F8EFDF");
    private static readonly Color Ink = Hex("22283A");
    private static readonly Color Glow = Hex("8FF0D2");

    private const float ColumnWidth = 700f;
    private const float ButtonWidth = 560f;
    private const float ButtonHeight = 84f;

    private static bool introShown;
    private float menuStart;

    private enum Panel { Main, NewGame, LoadGame, Multiplayer, Settings }

    private readonly SaveData[] slots = new SaveData[SaveSystem.SlotCount];
    private readonly Dictionary<Panel, RectTransform> panels = new Dictionary<Panel, RectTransform>();
    private readonly List<RectTransform> mainButtons = new List<RectTransform>();

    private Font fontBold;
    private Font fontSemi;
    private Sprite round;
    private Texture2D gradientTexture;

    private RectTransform canvasRect;
    private CanvasGroup menuGroup;
    private CanvasGroup videoGroup;
    private RawImage background;
    private RectTransform logo;
    private RectTransform column;
    private Text skipHint;
    private RectTransform confirmLayer;
    private Text confirmText;
    private Action confirmAction;
    private Button confirmYes;
    private Text confirmYesLabel;
    private Panel current = Panel.Main;
    private bool busy;

    private VideoPlayer video;
    private RenderTexture videoTexture;
    private bool videoEnded;
    private CanvasGroup shadeGroup;

    // ---------- Início ----------

    private void Awake()
    {
        Time.timeScale = 1f;
        fontBold = LoadFont("Fonts/Fredoka-Bold");
        fontSemi = LoadFont("Fonts/Fredoka-SemiBold");
        round = RoundedSprite();
        RefreshSlots();
        EnsureEventSystem();
        BuildUi();
    }

    private void Start()
    {
        StartCoroutine(Intro());
    }

    private void OnDestroy()
    {
        if (videoTexture != null) videoTexture.Release();
        if (gradientTexture != null) Destroy(gradientTexture);
        if (round != null) Destroy(round.texture);
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = SaveSystem.Load(i + 1);
        }
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        InputSystemUIInputModule module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions(); // setas/Enter/controle navegam pelos botões
    }

    // ---------- Abertura ----------

    private IEnumerator Intro()
    {
        VideoClip clip = Resources.Load<VideoClip>("Menu/Abertura_H-19");
        if (introShown || clip == null)
        {
            videoGroup.gameObject.SetActive(false);
            yield return ShowMenu(0.6f);
            yield break;
        }

        introShown = true;
        menuGroup.alpha = 0f;
        menuGroup.interactable = false;

        videoTexture = new RenderTexture(1280, 720, 0) { name = "Abertura" };
        video = gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.isLooping = false;
        video.clip = clip;
        video.renderMode = VideoRenderMode.RenderTexture;
        video.targetTexture = videoTexture;
        AudioSource sound = gameObject.AddComponent<AudioSource>();
        sound.playOnAwake = false;
        video.audioOutputMode = VideoAudioOutputMode.AudioSource;
        video.controlledAudioTrackCount = 1;
        video.EnableAudioTrack(0, true);
        video.SetTargetAudioSource(0, sound);
        video.loopPointReached += _ => videoEnded = true;
        videoGroup.GetComponentInChildren<RawImage>().texture = videoTexture;

        video.Prepare();
        float wait = 0f;
        while (!video.isPrepared && wait < 4f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!video.isPrepared)
        {
            videoGroup.gameObject.SetActive(false);
            yield return ShowMenu(0.6f);
            yield break;
        }

        video.Play();
        float time = 0f;
        bool skipped = false;
        while (!videoEnded)
        {
            time += Time.unscaledDeltaTime;
            skipHint.color = new Color(1f, 1f, 1f, Mathf.Clamp01((time - 0.8f) * 2f) * 0.75f);
            if (time > 0.3f && SkipPressed())
            {
                skipped = true;
                break;
            }

            yield return null;
        }

        skipHint.gameObject.SetActive(false);
        if (!skipped)
        {
            yield return new WaitForSecondsRealtime(0.5f); // segura o logo do fim do vídeo
        }

        // O fundo do menu é o último quadro do vídeo: a troca é quase invisível (só os botões entram).
        menuGroup.alpha = 1f; // só o fundo aparece (coluna e sombra ainda transparentes)
        menuStart = Time.unscaledTime;
        float fade = skipped ? 0.35f : 0.25f;
        for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
        {
            float k = Smooth(t / fade);
            videoGroup.alpha = 1f - k;
            sound.volume = 1f - k;
            yield return null;
        }

        video.Stop();
        videoGroup.gameObject.SetActive(false);
        yield return ShowMenu(0.35f);
    }

    private static bool SkipPressed()
    {
        return (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
               || (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame))
               || (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame || Gamepad.current.startButton.wasPressedThisFrame));
    }

    private IEnumerator ShowMenu(float duration)
    {
        if (videoGroup == null || !videoGroup.gameObject.activeSelf || menuStart <= 0f)
        {
            menuStart = Time.unscaledTime;
        }

        Open(Panel.Main, false);
        for (int i = 0; i < mainButtons.Count; i++)
        {
            mainButtons[i].anchoredPosition = new Vector2(-900f, mainButtons[i].anchoredPosition.y);
        }

        float startAlpha = menuGroup.alpha;
        for (float t = 0f; t < duration + 0.6f; t += Time.unscaledDeltaTime)
        {
            menuGroup.alpha = Mathf.Lerp(startAlpha, 1f, Smooth(t / duration));
            column.GetComponent<CanvasGroup>().alpha = Smooth(t / 0.3f);
            shadeGroup.alpha = Smooth(t / 0.6f);
            for (int i = 0; i < mainButtons.Count; i++)
            {
                float k = Smooth((t - 0.1f - i * 0.07f) / 0.4f);
                mainButtons[i].anchoredPosition = new Vector2(Mathf.Lerp(-900f, 0f, EaseOutBack(k)), mainButtons[i].anchoredPosition.y);
            }

            yield return null;
        }

        menuGroup.alpha = 1f;
        shadeGroup.alpha = 1f;
        foreach (RectTransform b in mainButtons)
        {
            b.anchoredPosition = new Vector2(0f, b.anchoredPosition.y);
        }

        menuGroup.interactable = true;
        SelectFirst(panels[Panel.Main]);
    }

    // ---------- A cada quadro ----------

    private void Update()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        float time = Time.unscaledTime;
        if (background != null)
        {
            // Zoom bem lento (respira), começando igual ao último quadro do vídeo.
            float since = time - menuStart;
            float zoom = 1f + 0.025f * (0.5f - 0.5f * Mathf.Cos(since * 0.12f));
            float w = 1f / zoom;
            background.uvRect = new Rect((1f - w) * 0.6f, (1f - w) * 0.5f, w, w);
        }

        if (logo != null)
        {
            logo.anchoredPosition = new Vector2(0f, -36f + Mathf.Sin(time * 1.3f) * 5f);
            logo.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 0.8f) * 1.2f);
        }

        if (!menuGroup.interactable || busy)
        {
            return;
        }

        bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
        if (back)
        {
            if (confirmLayer.gameObject.activeSelf)
            {
                CloseConfirm();
            }
            else if (current != Panel.Main)
            {
                Open(Panel.Main, true);
            }
        }

        // Ninguém selecionado (clicou no vazio): teclado/controle voltam para o primeiro botão.
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && AnyNavigation())
        {
            SelectFirst(confirmLayer.gameObject.activeSelf ? confirmLayer : panels[current]);
        }
    }

    private static bool AnyNavigation()
    {
        Keyboard k = Keyboard.current;
        Gamepad g = Gamepad.current;
        return (k != null && (k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame))
               || (g != null && g.dpad.ReadValue().sqrMagnitude > 0.5f);
    }

    // ---------- Montagem da tela ----------

    private void BuildUi()
    {
        GameObject canvasObject = new GameObject("TelaInicial", typeof(RectTransform));
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasRect = (RectTransform)canvasObject.transform;

        // Fundo preto (atrás de tudo).
        Stretch(NewImage(canvasRect, "Preto", Color.black, null).rectTransform);

        // ---- Menu ----
        RectTransform menu = Stretch(NewRect(canvasRect, "Menu"));
        menuGroup = menu.gameObject.AddComponent<CanvasGroup>();
        menuGroup.alpha = 0f;
        menuGroup.interactable = false;

        RectTransform frame = Stretch(NewRect(menu, "Fundo"));
        frame.gameObject.AddComponent<RectMask2D>();
        GameObject bg = new GameObject("Rua", typeof(RectTransform));
        bg.transform.SetParent(frame, false);
        background = bg.AddComponent<RawImage>();
        background.raycastTarget = false;
        Texture2D art = Resources.Load<Texture2D>("Menu/Menu_Fundo");
        bool artHasLogo = art != null;
        if (art == null)
        {
            art = Resources.Load<Texture2D>("Menu/Menu_Rua");
        }

        background.texture = art;
        background.color = art != null ? Color.white : Hex("2A3550");
        AspectRatioFitter fitter = bg.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = art != null ? (float)art.width / art.height : 16f / 9f;

        // Sombra azul-marinho do lado esquerdo, embaixo do logo (para os botões aparecerem sobre a explosão).
        gradientTexture = new Texture2D(128, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++)
        {
            float below = Smooth((0.72f - y / 63f) / 0.12f); // some em cima (onde fica o logo)
            for (int x = 0; x < 128; x++)
            {
                float a = Smooth(1f - x / 127f) * 0.72f * below;
                gradientTexture.SetPixel(x, y, new Color(0.11f, 0.13f, 0.2f, a));
            }
        }

        gradientTexture.Apply();
        GameObject shade = new GameObject("Sombra", typeof(RectTransform));
        shade.transform.SetParent(menu, false);
        RawImage shadeImage = shade.AddComponent<RawImage>();
        shadeImage.texture = gradientTexture;
        shadeImage.raycastTarget = false;
        shadeGroup = shade.AddComponent<CanvasGroup>();
        shadeGroup.alpha = 0f;
        RectTransform shadeRect = (RectTransform)shade.transform;
        shadeRect.anchorMin = Vector2.zero;
        shadeRect.anchorMax = new Vector2(0.6f, 1f);
        shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;

        // Coluna da esquerda (embaixo do logo da imagem): painéis.
        column = NewRect(menu, "Coluna");
        column.anchorMin = new Vector2(0f, 0f);
        column.anchorMax = new Vector2(0f, 1f);
        column.pivot = new Vector2(0f, 0.5f);
        column.sizeDelta = new Vector2(ColumnWidth, 0f);
        column.anchoredPosition = new Vector2(250f, 0f);
        CanvasGroup columnGroup = column.gameObject.AddComponent<CanvasGroup>();
        columnGroup.alpha = 0f;

        Texture2D logoTexture = artHasLogo ? null : Resources.Load<Texture2D>("Menu/Logo_ImNotAnAlien");
        if (artHasLogo)
        {
            // o logo já está na imagem de fundo
        }
        else if (logoTexture != null)
        {
            GameObject logoObject = new GameObject("Logo", typeof(RectTransform));
            logoObject.transform.SetParent(column, false);
            RawImage logoImage = logoObject.AddComponent<RawImage>();
            logoImage.texture = logoTexture;
            logoImage.raycastTarget = false;
            logo = (RectTransform)logoObject.transform;
            logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 1f);
            logo.pivot = new Vector2(0.5f, 1f);
            float width = 640f;
            logo.sizeDelta = new Vector2(width, width * logoTexture.height / logoTexture.width);
        }
        else
        {
            Text title = NewText(column, "Titulo", "I'M NOT AN ALIEN", fontBold, 72, Cream, TextAnchor.MiddleCenter); // sem arte nenhuma
            Place(title.rectTransform, 0f, -40f, ColumnWidth, 200f);
        }

        BuildMainPanel();
        panels[Panel.NewGame] = BuildSlotsPanel(true);
        panels[Panel.LoadGame] = BuildSlotsPanel(false);
        BuildMultiplayerPanel();
        BuildSettingsPanel();
        BuildConfirm(menu);

        Text version = NewText(menu, "Versao", "protótipo · v0.1", fontSemi, 20, new Color(1f, 1f, 1f, 0.55f), TextAnchor.LowerRight);
        version.rectTransform.anchorMin = version.rectTransform.anchorMax = new Vector2(1f, 0f);
        version.rectTransform.pivot = new Vector2(1f, 0f);
        version.rectTransform.sizeDelta = new Vector2(400f, 40f);
        version.rectTransform.anchoredPosition = new Vector2(-24f, 16f);

        // ---- Vídeo de abertura (por cima de tudo) ----
        RectTransform videoLayer = Stretch(NewRect(canvasRect, "Abertura"));
        videoGroup = videoLayer.gameObject.AddComponent<CanvasGroup>();
        Stretch(NewImage(videoLayer, "Preto", Color.black, null).rectTransform);
        GameObject screen = new GameObject("Video", typeof(RectTransform));
        screen.transform.SetParent(videoLayer, false);
        RawImage screenImage = screen.AddComponent<RawImage>();
        screenImage.raycastTarget = false;
        AspectRatioFitter screenFit = screen.AddComponent<AspectRatioFitter>();
        screenFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        screenFit.aspectRatio = 16f / 9f;
        skipHint = NewText(videoLayer, "Pular", "clique ou aperte qualquer tecla para pular", fontSemi, 22, new Color(1f, 1f, 1f, 0f), TextAnchor.LowerCenter);
        skipHint.rectTransform.anchorMin = new Vector2(0f, 0f);
        skipHint.rectTransform.anchorMax = new Vector2(1f, 0f);
        skipHint.rectTransform.pivot = new Vector2(0.5f, 0f);
        skipHint.rectTransform.sizeDelta = new Vector2(0f, 40f);
        skipHint.rectTransform.anchoredPosition = new Vector2(0f, 22f);
    }

    private RectTransform NewPanel(Panel id, string name)
    {
        RectTransform panel = Stretch(NewRect(column, name));
        panels[id] = panel;
        return panel;
    }

    private void BuildMainPanel()
    {
        RectTransform panel = NewPanel(Panel.Main, "Principal");
        float y = -360f;
        string[] labels = { "Novo jogo", "Carregar jogo", "Multijogador", "Configurações", "Sair" };
        Color[] colors = { Coral, Mint, Petrol, Mustard, Orange };
        Action[] actions =
        {
            () => Open(Panel.NewGame, true),
            () => Open(Panel.LoadGame, true),
            () => Open(Panel.Multiplayer, true),
            () => Open(Panel.Settings, true),
            () => Ask("Sair do jogo?", "Sair", Quit),
        };

        for (int i = 0; i < labels.Length; i++)
        {
            Button b = MakeButton(panel, labels[i], colors[i], ButtonWidth, ButtonHeight, 40, actions[i]);
            Place((RectTransform)b.transform, 0f, y, ButtonWidth, ButtonHeight);
            mainButtons.Add((RectTransform)b.transform);
            y -= ButtonHeight + 22f;
        }
    }

    private RectTransform BuildSlotsPanel(bool newGame)
    {
        RectTransform panel = Stretch(NewRect(column, newGame ? "NovoJogo" : "CarregarJogo"));
        PanelTitle(panel, newGame ? "NOVO JOGO" : "CARREGAR JOGO", newGame ? Coral : Mint);
        // Os cartões são refeitos ao abrir (os jogos salvos podem ter mudado).
        Stretch(NewRect(panel, "Cartoes"));
        BackButton(panel, -955f);
        return panel;
    }

    private void FillSlots(RectTransform panel, bool newGame)
    {
        RectTransform cards = (RectTransform)panel.Find("Cartoes");
        for (int i = cards.childCount - 1; i >= 0; i--)
        {
            GameObject old = cards.GetChild(i).gameObject;
            old.SetActive(false); // some já (o Destroy só apaga no fim do quadro)
            Destroy(old);
        }

        Color accent = newGame ? Coral : Mint;
        for (int i = 0; i < SaveSystem.SlotCount; i++)
        {
            int slot = i + 1;
            SaveData data = slots[i];
            float y = -420f - i * 172f;

            Action action;
            if (newGame)
            {
                action = data == null
                    ? () => Play(slot, true)
                    : () => Ask($"Começar um jogo novo no espaço {slot}?\n<size=24>O jogo salvo neste espaço será perdido.</size>", "Jogo novo", () => Play(slot, true));
            }
            else
            {
                action = data == null ? null : () => Play(slot, false);
            }

            Button card = MakeCard(cards, slot, data, accent, action);
            Place((RectTransform)card.transform, 0f, y, ButtonWidth, 152f);

            if (!newGame && data != null)
            {
                Button delete = MakeButton((RectTransform)card.transform, "Apagar", Coral, 130f, 46f, 22,
                    () => Ask($"Apagar o Jogo {slot}?\n<size=24>Não dá para desfazer.</size>", "Apagar", () =>
                    {
                        SaveSystem.Delete(slot);
                        RefreshSlots();
                        FillSlots(panels[Panel.LoadGame], false);
                        SelectFirst(panels[Panel.LoadGame]);
                    }));
                RectTransform r = (RectTransform)delete.transform;
                r.anchorMin = r.anchorMax = new Vector2(1f, 0f);
                r.pivot = new Vector2(1f, 0f);
                r.anchoredPosition = new Vector2(-16f, 16f);
            }
        }
    }

    private Button MakeCard(RectTransform parent, int slot, SaveData data, Color accent, Action action)
    {
        Button card = MakeButton(parent, "", Cream, ButtonWidth, 152f, 30, action, false);
        RectTransform rect = (RectTransform)card.transform;
        if (action == null)
        {
            card.interactable = false;
        }

        Image stripe = NewImage(rect, "Faixa", accent, round);
        stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
        stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
        stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
        stripe.rectTransform.sizeDelta = new Vector2(16f, -28f);
        stripe.rectTransform.anchoredPosition = new Vector2(18f, 0f);
        stripe.pixelsPerUnitMultiplier = 3f;

        Text title = NewText(rect, "Titulo", $"JOGO {slot}", fontBold, 32, Ink, TextAnchor.UpperLeft);
        title.rectTransform.anchorMin = new Vector2(0f, 1f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.pivot = new Vector2(0f, 1f);
        title.rectTransform.sizeDelta = new Vector2(-60f, 44f);
        title.rectTransform.anchoredPosition = new Vector2(50f, -14f);

        string info;
        if (data == null)
        {
            info = "Vazio";
        }
        else
        {
            int quota = data.TotalQuota();
            string progress = quota > 0 ? $"{data.TotalConsumed()} / {quota}" : "0";
            info = $"Alienígenas consumidos: {progress}   ·   DNA: {data.dna}\n" +
                   $"Tempo: {FormatTime(data.playTimeSeconds)}   ·   salvo em {FormatDate(data.savedAt)}";
        }

        Text body = NewText(rect, "Info", info, fontSemi, 21, new Color(Ink.r, Ink.g, Ink.b, data == null ? 0.5f : 0.85f), TextAnchor.UpperLeft);
        body.rectTransform.anchorMin = new Vector2(0f, 0f);
        body.rectTransform.anchorMax = new Vector2(1f, 1f);
        body.rectTransform.offsetMin = new Vector2(50f, 14f);
        body.rectTransform.offsetMax = new Vector2(-20f, -62f);
        return card;
    }

    private void BuildMultiplayerPanel()
    {
        RectTransform panel = NewPanel(Panel.Multiplayer, "Multijogador");
        PanelTitle(panel, "MULTIJOGADOR", Petrol);

        Image box = NewImage(panel, "Explicacao", new Color(Cream.r, Cream.g, Cream.b, 0.95f), round);
        Place(box.rectTransform, 0f, -420f, ButtonWidth, 230f);
        Text text = NewText(box.rectTransform, "Texto",
            "<b>Em breve!</b>\n\nUm jogador cria o mundo e, enquanto ele joga, até 4 amigos entram nele com um código " +
            "(5 jogadores no total). O mundo e o jogo salvo ficam com quem criou.",
            fontSemi, 24, Ink, TextAnchor.MiddleLeft);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(30f, 20f);
        text.rectTransform.offsetMax = new Vector2(-30f, -20f);

        Button host = MakeButton(panel, "Hospedar mundo", Petrol, ButtonWidth, ButtonHeight, 36, null);
        host.interactable = false;
        Place((RectTransform)host.transform, 0f, -680f, ButtonWidth, ButtonHeight);
        Button join = MakeButton(panel, "Entrar com código", Petrol, ButtonWidth, ButtonHeight, 36, null);
        join.interactable = false;
        Place((RectTransform)join.transform, 0f, -786f, ButtonWidth, ButtonHeight);
        BackButton(panel, -955f);
    }

    private void BuildSettingsPanel()
    {
        RectTransform panel = NewPanel(Panel.Settings, "Configuracoes");
        PanelTitle(panel, "CONFIGURAÇÕES", Mustard);
        float y = -420f;
        SettingRow(panel, "Volume", ref y,
            () => $"{Mathf.RoundToInt(GameSettings.Volume * 100f)}%",
            d => GameSettings.SetVolume(Mathf.Round((GameSettings.Volume + d * 0.1f) * 10f) / 10f));
        SettingRow(panel, "Sensibilidade do mouse", ref y,
            () => $"{GameSettings.LookSensitivity:0.00}x",
            d => GameSettings.SetLookSensitivity(GameSettings.LookSensitivity + d * 0.25f));
        SettingRow(panel, "Tela cheia", ref y,
            () => GameSettings.Fullscreen ? "Ligada" : "Desligada",
            d => GameSettings.SetFullscreen(!GameSettings.Fullscreen));
        SettingRow(panel, "Qualidade", ref y,
            () => GameSettings.QualityName,
            d =>
            {
                int count = Mathf.Max(1, QualitySettings.names.Length);
                GameSettings.SetQuality((GameSettings.Quality + d + count) % count);
            });
        SettingRow(panel, "VSync", ref y,
            () => GameSettings.VSync ? "Ligado" : "Desligado",
            d => GameSettings.SetVSync(!GameSettings.VSync));
        BackButton(panel, -955f);
    }

    private void SettingRow(RectTransform panel, string label, ref float y, Func<string> value, Action<int> change)
    {
        Image row = NewImage(panel, label, new Color(Cream.r, Cream.g, Cream.b, 0.95f), round);
        Place(row.rectTransform, 0f, y, ButtonWidth, 78f);
        Text name = NewText(row.rectTransform, "Nome", label, fontSemi, 26, Ink, TextAnchor.MiddleLeft);
        name.rectTransform.anchorMin = Vector2.zero;
        name.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        name.rectTransform.offsetMin = new Vector2(26f, 0f);
        name.rectTransform.offsetMax = Vector2.zero;

        Text shown = NewText(row.rectTransform, "Valor", value(), fontBold, 26, Ink, TextAnchor.MiddleCenter);
        shown.rectTransform.anchorMin = shown.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        shown.rectTransform.pivot = new Vector2(1f, 0.5f);
        shown.rectTransform.sizeDelta = new Vector2(150f, 60f);
        shown.rectTransform.anchoredPosition = new Vector2(-74f, 0f);

        for (int side = -1; side <= 1; side += 2)
        {
            int step = side;
            Button arrow = MakeButton(row.rectTransform, side < 0 ? "‹" : "›", Mustard, 52f, 52f, 34, () =>
            {
                change(step);
                shown.text = value();
            });
            RectTransform r = (RectTransform)arrow.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.anchoredPosition = new Vector2(side < 0 ? -230f : -16f, 0f);
        }

        y -= 88f;
    }

    private void PanelTitle(RectTransform panel, string title, Color color)
    {
        Image pill = NewImage(panel, "Titulo", color, round);
        Place(pill.rectTransform, 0f, -340f, 460f, 62f);
        pill.pixelsPerUnitMultiplier = 0.9f;
        Text text = NewText(pill.rectTransform, "Texto", title, fontBold, 34, Cream, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
    }

    private void BackButton(RectTransform panel, float y)
    {
        Button back = MakeButton(panel, "Voltar", Ink, 300f, 70f, 32, () => Open(Panel.Main, true));
        Place((RectTransform)back.transform, 0f, y, 300f, 70f);
    }

    private void BuildConfirm(RectTransform menu)
    {
        confirmLayer = Stretch(NewRect(menu, "Confirmar"));
        Image dim = NewImage(confirmLayer, "Escuro", new Color(0f, 0f, 0f, 0.6f), null);
        Stretch(dim.rectTransform);
        dim.raycastTarget = true;
        Image box = NewImage(confirmLayer, "Caixa", Cream, round);
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(760f, 320f);
        confirmText = NewText(box.rectTransform, "Pergunta", "", fontBold, 36, Ink, TextAnchor.MiddleCenter);
        confirmText.rectTransform.anchorMin = new Vector2(0f, 0.4f);
        confirmText.rectTransform.anchorMax = Vector2.one;
        confirmText.rectTransform.offsetMin = new Vector2(30f, 0f);
        confirmText.rectTransform.offsetMax = new Vector2(-30f, -20f);

        confirmYes = MakeButton(box.rectTransform, "Sim", Coral, 280f, 76f, 32, () =>
        {
            Action action = confirmAction;
            CloseConfirm();
            action?.Invoke();
        });
        confirmYesLabel = confirmYes.GetComponentInChildren<Text>();
        RectTransform yes = (RectTransform)confirmYes.transform;
        yes.anchorMin = yes.anchorMax = new Vector2(0.5f, 0f);
        yes.anchoredPosition = new Vector2(-160f, 70f);

        Button no = MakeButton(box.rectTransform, "Cancelar", Ink, 280f, 76f, 32, CloseConfirm);
        RectTransform noRect = (RectTransform)no.transform;
        noRect.anchorMin = noRect.anchorMax = new Vector2(0.5f, 0f);
        noRect.anchoredPosition = new Vector2(160f, 70f);
        confirmLayer.gameObject.SetActive(false);
    }

    // ---------- Navegação ----------

    private void Open(Panel panel, bool select)
    {
        if (panel == Panel.NewGame || panel == Panel.LoadGame)
        {
            RefreshSlots();
            FillSlots(panels[panel], panel == Panel.NewGame);
        }

        foreach (KeyValuePair<Panel, RectTransform> pair in panels)
        {
            pair.Value.gameObject.SetActive(pair.Key == panel);
        }

        Panel previous = current;
        current = panel;
        if (select)
        {
            SelectFirst(panels[panel], panel == Panel.Main ? MainButtonFor(previous) : null);
        }
    }

    private GameObject MainButtonFor(Panel from)
    {
        int index = from switch
        {
            Panel.NewGame => 0,
            Panel.LoadGame => 1,
            Panel.Multiplayer => 2,
            Panel.Settings => 3,
            _ => 0,
        };
        return index < mainButtons.Count ? mainButtons[index].gameObject : null;
    }

    private void SelectFirst(RectTransform root, GameObject preferred = null)
    {
        if (EventSystem.current == null)
        {
            return;
        }

        GameObject target = preferred;
        if (target == null)
        {
            foreach (Selectable s in root.GetComponentsInChildren<Selectable>())
            {
                if (s.interactable && s.gameObject.activeInHierarchy)
                {
                    target = s.gameObject;
                    break;
                }
            }
        }

        EventSystem.current.SetSelectedGameObject(null);
        MenuSounds.Quiet(); // o menu escolheu o botão sozinho: sem "plim"
        if (target != null)
        {
            EventSystem.current.SetSelectedGameObject(target);
        }
    }

    private void Ask(string question, string yesLabel, Action action)
    {
        confirmText.text = question;
        confirmYesLabel.text = yesLabel;
        confirmAction = action;
        confirmLayer.gameObject.SetActive(true);
        confirmLayer.SetAsLastSibling();
        SelectFirst(confirmLayer, confirmYes.gameObject);
    }

    private void CloseConfirm()
    {
        confirmLayer.gameObject.SetActive(false);
        confirmAction = null;
        SelectFirst(panels[current]);
    }

    // ---------- Ações ----------

    private void Play(int slot, bool newGame)
    {
        if (busy)
        {
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(SceneToLoad))
        {
            Debug.LogError($"A cena '{SceneToLoad}' não está no Build (File > Build Profiles > Scene List).");
            return;
        }

        StartCoroutine(LoadGame(slot, newGame));
    }

    private IEnumerator LoadGame(int slot, bool newGame)
    {
        busy = true;
        menuGroup.interactable = false;
        RectTransform cover = Stretch(NewRect(canvasRect, "Carregando"));
        CanvasGroup group = cover.gameObject.AddComponent<CanvasGroup>();
        Stretch(NewImage(cover, "Escuro", Hex("141826"), null).rectTransform);
        Texture2D street = Resources.Load<Texture2D>("Menu/Menu_Rua"); // a rua das lojas enquanto carrega
        if (street != null)
        {
            GameObject pic = new GameObject("Rua", typeof(RectTransform));
            pic.transform.SetParent(cover, false);
            RawImage picImage = pic.AddComponent<RawImage>();
            picImage.texture = street;
            picImage.raycastTarget = false;
            AspectRatioFitter picFit = pic.AddComponent<AspectRatioFitter>();
            picFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            picFit.aspectRatio = (float)street.width / street.height;
            Image band = NewImage(cover, "Faixa", new Color(0.08f, 0.09f, 0.15f, 0.7f), null);
            band.rectTransform.anchorMin = Vector2.zero;
            band.rectTransform.anchorMax = new Vector2(1f, 0f);
            band.rectTransform.pivot = new Vector2(0.5f, 0f);
            band.rectTransform.sizeDelta = new Vector2(0f, 120f);
        }

        Text text = NewText(cover, "Texto", "Carregando o planeta...", fontBold, 40, Cream, street != null ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(0f, 38f);
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / 0.35f;
            yield return null;
        }

        group.alpha = 1f;
        yield return null;
        SaveSystem.CurrentSlot = slot;
        SaveSystem.PendingNewGame = newGame;
        SceneManager.LoadScene(SceneToLoad);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- Peças de interface ----------

    private Button MakeButton(RectTransform parent, string label, Color color, float width, float height, int fontSize, Action onClick, bool withGlow = true)
    {
        RectTransform root = NewRect(parent, "Botao_" + label);
        root.sizeDelta = new Vector2(width, height);

        float radius = Mathf.Min(height / 2f, 42f);
        Image shadow = NewImage(root, "Sombra", new Color(0f, 0f, 0f, 0.28f), round);
        Stretch(shadow.rectTransform);
        shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
        shadow.pixelsPerUnitMultiplier = 28f / radius;

        Image face = NewImage(root, "Face", color, round);
        Stretch(face.rectTransform);
        face.pixelsPerUnitMultiplier = 28f / radius;
        face.raycastTarget = true;

        Image glow = null;
        if (withGlow && width > 200f)
        {
            glow = NewImage(root, "Luz", new Color(Glow.r, Glow.g, Glow.b, 0.35f), round);
            glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            glow.rectTransform.sizeDelta = new Vector2(16f, height * 0.5f);
            glow.rectTransform.anchoredPosition = new Vector2(34f, 0f);
            glow.pixelsPerUnitMultiplier = 3.5f;
        }

        if (!string.IsNullOrEmpty(label))
        {
            Text text = NewText(root, "Texto", label, fontBold, fontSize, Cream, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            Shadow textShadow = text.gameObject.AddComponent<Shadow>();
            textShadow.effectColor = new Color(0f, 0f, 0f, 0.25f);
            textShadow.effectDistance = new Vector2(0f, -2f);
        }

        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        if (onClick != null)
        {
            button.onClick.AddListener(() =>
            {
                if (!busy)
                {
                    MenuSounds.Click();
                    onClick();
                }
            });
        }

        MenuButtonFx fx = root.gameObject.AddComponent<MenuButtonFx>();
        fx.face = face;
        fx.baseColor = color;
        fx.glow = glow;
        fx.hoverScale = width > 200f ? 1.05f : 1.12f;
        return button;
    }

    private RectTransform NewRect(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private Image NewImage(Transform parent, string name, Color color, Sprite sprite)
    {
        RectTransform rect = NewRect(parent, name);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        return image;
    }

    private Text NewText(Transform parent, string name, string content, Font font, int size, Color color, TextAnchor anchor)
    {
        RectTransform rect = NewRect(parent, name);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.text = content;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static RectTransform Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    // Posição dentro da coluna: centro horizontal, "y" a partir do topo.
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    // Quadradinho branco de cantos redondos (raio 28 px), esticado em 9 partes pelos botões e painéis.
    private static Sprite RoundedSprite()
    {
        const int size = 64;
        const float r = 28f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Menu_Redondo" };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(30f, 30f, 30f, 30f));
    }

    private static Font LoadFont(string path)
    {
        Font font = Resources.Load<Font>(path);
        return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private static float EaseOutBack(float t)
    {
        t = Mathf.Clamp01(t);
        const float c1 = 1.4f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private static string FormatTime(float seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h{span.Minutes:00}" : $"{span.Minutes} min";
    }

    private static string FormatDate(string iso)
    {
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date)
            ? date.ToString("dd/MM/yyyy HH:mm")
            : "?";
    }
}
