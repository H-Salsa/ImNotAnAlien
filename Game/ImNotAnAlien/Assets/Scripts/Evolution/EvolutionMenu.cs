using UnityEngine;
using UnityEngine.InputSystem;

// Menu de evoluções. Tab (ou Start no controle) abre e fecha; Esc também fecha.
// Abrir o menu PAUSA o jogo. Cada cartão mostra a evolução, o nível atual,
// o efeito do próximo nível e o botão de compra com o custo em DNA.
// Interface provisória (IMGUI): simples de manter durante o protótipo.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)] // roda depois do controle do personagem (evita o Esc "vazar" ao fechar)
public sealed class EvolutionMenu : MonoBehaviour
{
    private const float ReferenceHeight = 1080f;
    private const float CardWidth = 400f;
    private const float CardHeight = 250f;
    private const float Gap = 24f;
    private const int Columns = 3;

    [SerializeField] private Color accent = new Color(0.55f, 0.4f, 1f, 1f);
    [SerializeField] private Color dnaColor = new Color(0.35f, 1f, 0.65f, 1f);

    private InputAction toggleAction;
    private bool isOpen;
    private EvolutionSystem system;

    private GUIStyle titleStyle;
    private GUIStyle cardTitleStyle;
    private GUIStyle textStyle;
    private GUIStyle smallStyle;
    private GUIStyle buttonStyle;
    private Texture2D buttonTexture;
    private Texture2D buttonHoverTexture;
    private float flashTime = -10f;
    private int flashIndex = -1;

    public static bool IsOpen { get; private set; }

    private void Awake()
    {
        toggleAction = new InputAction("Evolucoes", InputActionType.Button, "<Keyboard>/tab");
        toggleAction.AddBinding("<Gamepad>/start");
    }

    private void OnEnable()
    {
        toggleAction.Enable();
    }

    private void OnDisable()
    {
        toggleAction.Disable();
        if (isOpen)
        {
            Close();
        }
    }

    private void OnDestroy()
    {
        toggleAction.Dispose();
        if (buttonTexture != null) Destroy(buttonTexture);
        if (buttonHoverTexture != null) Destroy(buttonHoverTexture);
    }

    private void Start()
    {
        system = EvolutionSystem.Instance;
        if (system == null)
        {
            Debug.LogWarning("EvolutionMenu: EvolutionSystem não encontrado na cena.", this);
        }
    }

    private void Update()
    {
        if (system == null)
        {
            return;
        }

        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

        if (isOpen && (toggleAction.WasPressedThisFrame() || escape))
        {
            Close();
        }
        else if (!isOpen && toggleAction.WasPressedThisFrame() && CanOpen())
        {
            Open();
        }
    }

    private static bool CanOpen()
    {
        return PlayerHealth.Instance == null || !PlayerHealth.Instance.IsDead;
    }

    private void Open()
    {
        isOpen = true;
        IsOpen = true;
        GamePause.Pause();
    }

    private void Close()
    {
        isOpen = false;
        IsOpen = false;
        GamePause.Resume();
    }

    // ---------- Desenho ----------

    private void OnGUI()
    {
        if (!isOpen || system == null)
        {
            return;
        }

        BuildStyles();

        // Escala tudo a partir de 1080p, para ficar do mesmo tamanho em qualquer resolução.
        float scale = Screen.height / ReferenceHeight;
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;
        float height = ReferenceHeight;

        // Fundo escurecido.
        GUI.color = new Color(0.03f, 0.02f, 0.08f, 0.82f);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        int dna = DnaInventory.Instance != null ? DnaInventory.Instance.Total : 0;
        GUI.Label(new Rect(0f, 70f, width, 70f), "EVOLUÇÕES", titleStyle);
        smallStyle.normal.textColor = dnaColor;
        smallStyle.alignment = TextAnchor.MiddleCenter;
        smallStyle.fontSize = 30;
        GUI.Label(new Rect(0f, 140f, width, 40f), $"DNA disponível: {dna}", smallStyle);

        int count = system.Evolutions.Count;
        int rows = Mathf.CeilToInt(count / (float)Columns);
        float gridWidth = Columns * CardWidth + (Columns - 1) * Gap;
        float gridHeight = rows * CardHeight + (rows - 1) * Gap;
        float startX = (width - gridWidth) / 2f;
        float startY = Mathf.Max(200f, (height - gridHeight) / 2f + 30f);

        for (int i = 0; i < count; i++)
        {
            int column = i % Columns;
            int row = i / Columns;
            Rect card = new Rect(startX + column * (CardWidth + Gap), startY + row * (CardHeight + Gap), CardWidth, CardHeight);
            DrawCard(card, system.Evolutions[i], i);
        }

        smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        smallStyle.fontSize = 20;
        GUI.Label(new Rect(0f, height - 70f, width, 30f), "Tab ou Esc para voltar ao jogo", smallStyle);

        GUI.matrix = previous;
    }

    private void DrawCard(Rect card, EvolutionSystem.Evolution evolution, int index)
    {
        int level = system.GetLevel(evolution);
        int maxLevel = evolution.levelCosts.Length;
        bool maxed = system.IsMaxed(evolution);
        bool affordable = system.CanBuy(evolution);

        // Fundo do cartão (pisca ao comprar).
        float flash = index == flashIndex ? Mathf.Clamp01(1f - (Time.unscaledTime - flashTime) / 0.5f) : 0f;
        GUI.color = Color.Lerp(new Color(0.12f, 0.1f, 0.2f, 0.95f), accent, flash * 0.6f);
        GUI.DrawTexture(card, Texture2D.whiteTexture);
        GUI.color = maxed ? dnaColor : affordable ? accent : new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(new Rect(card.x, card.y, card.width, 4f), Texture2D.whiteTexture); // faixa no topo
        GUI.color = Color.white;

        float x = card.x + 20f;
        float innerWidth = card.width - 40f;

        GUI.Label(new Rect(x, card.y + 16f, innerWidth, 34f), evolution.title, cardTitleStyle);

        // Nível: quadradinhos preenchidos.
        for (int i = 0; i < maxLevel; i++)
        {
            GUI.color = i < level ? dnaColor : new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(new Rect(x + i * 26f, card.y + 56f, 20f, 10f), Texture2D.whiteTexture);
        }

        GUI.color = Color.white;

        GUI.Label(new Rect(x, card.y + 76f, innerWidth, 60f), evolution.description, textStyle);

        smallStyle.alignment = TextAnchor.UpperLeft;
        smallStyle.fontSize = 19;
        smallStyle.normal.textColor = maxed ? dnaColor : new Color(1f, 0.85f, 0.4f, 1f);
        string next = maxed ? "Nível máximo" : "Próximo: " + system.NextEffect(evolution);
        GUI.Label(new Rect(x, card.y + 140f, innerWidth, 30f), next, smallStyle);

        Rect buttonRect = new Rect(x, card.y + card.height - 66f, innerWidth, 48f);
        string label = maxed ? "MÁXIMO" : $"Evoluir  ({system.NextCost(evolution)} DNA)";

        GUI.enabled = affordable;
        if (GUI.Button(buttonRect, label, buttonStyle) && system.TryBuy(evolution))
        {
            flashIndex = index;
            flashTime = Time.unscaledTime;
        }

        GUI.enabled = true;
    }

    private void BuildStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 56,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        cardTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white },
        };
        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 19,
            wordWrap = true,
            normal = { textColor = new Color(1f, 1f, 1f, 0.85f) },
        };
        smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
        };

        buttonTexture = SolidTexture(accent);
        buttonHoverTexture = SolidTexture(Color.Lerp(accent, Color.white, 0.25f));
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { background = buttonTexture, textColor = Color.white },
            hover = { background = buttonHoverTexture, textColor = Color.white },
            active = { background = buttonTexture, textColor = Color.white },
        };
    }

    private static Texture2D SolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
