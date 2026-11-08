using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menu inicial provisório (Missão 17). Três espaços de jogo:
// - Continuar: carrega o jogo salvo naquele espaço.
// - Novo jogo: sorteia uma semente nova (mundo diferente). Se o espaço já tem jogo, pede confirmação.
// - Apagar: apaga o jogo do espaço (com confirmação).
// Interface IMGUI, no mesmo estilo do menu de evoluções, até existir a arte do menu.
[DisallowMultipleComponent]
public sealed class MainMenu : MonoBehaviour
{
    private const float ReferenceHeight = 1080f;
    private const float CardWidth = 440f;
    private const float CardHeight = 360f;
    private const float Gap = 32f;

    private enum Confirm
    {
        None,
        Overwrite,
        Delete,
    }

    [SerializeField] private string gameSceneName = "Regiao1_VilarejoDoBilu_Blocos";
    [SerializeField] private Color accent = new Color(0.55f, 0.4f, 1f, 1f);
    [SerializeField] private Color dnaColor = new Color(0.35f, 1f, 0.65f, 1f);

    private readonly SaveData[] slots = new SaveData[SaveSystem.SlotCount];
    private Confirm confirm;
    private int confirmSlot;

    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle cardTitleStyle;
    private GUIStyle textStyle;
    private GUIStyle buttonStyle;
    private Texture2D buttonTexture;
    private Texture2D buttonHoverTexture;

    private void Awake()
    {
        Time.timeScale = 1f;
        Refresh();
    }

    private void OnDestroy()
    {
        if (buttonTexture != null) Destroy(buttonTexture);
        if (buttonHoverTexture != null) Destroy(buttonHoverTexture);
    }

    private void Update()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Refresh()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = SaveSystem.Load(i + 1);
        }
    }

    // ---------- Ações ----------

    private void Play(int slot, bool newGame)
    {
        if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            Debug.LogError($"A cena '{gameSceneName}' não está no Build. Rode 'Preparar Salvamento (Missao 17)'.");
            return;
        }

        SaveSystem.CurrentSlot = slot;
        SaveSystem.PendingNewGame = newGame;
        SceneManager.LoadScene(gameSceneName);
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------- Desenho ----------

    private void OnGUI()
    {
        BuildStyles();

        float scale = Screen.height / ReferenceHeight;
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;
        float height = ReferenceHeight;

        GUI.color = new Color(0.05f, 0.03f, 0.12f, 1f);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(0f, 120f, width, 110f), "I'M NOT AN ALIEN", titleStyle);
        GUI.Label(new Rect(0f, 225f, width, 40f), "(Eu não sou um alienígena. Juro.)", subtitleStyle);

        float total = SaveSystem.SlotCount * CardWidth + (SaveSystem.SlotCount - 1) * Gap;
        float startX = (width - total) / 2f;
        for (int i = 0; i < SaveSystem.SlotCount; i++)
        {
            DrawSlot(new Rect(startX + i * (CardWidth + Gap), 340f, CardWidth, CardHeight), i + 1);
        }

        if (GUI.Button(new Rect((width - 240f) / 2f, 780f, 240f, 56f), "Sair", buttonStyle) && confirm == Confirm.None)
        {
            Quit();
        }

        if (confirm != Confirm.None)
        {
            DrawConfirm(width, height);
        }

        GUI.matrix = previous;
    }

    private void DrawSlot(Rect card, int slot)
    {
        SaveData data = slots[slot - 1];

        GUI.color = new Color(0.12f, 0.1f, 0.2f, 0.95f);
        GUI.DrawTexture(card, Texture2D.whiteTexture);
        GUI.color = data != null ? accent : new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(new Rect(card.x, card.y, card.width, 6f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = card.x + 28f;
        float w = card.width - 56f;
        GUI.Label(new Rect(x, card.y + 24f, w, 44f), $"JOGO {slot}", cardTitleStyle);

        bool enabledBefore = GUI.enabled;
        GUI.enabled = confirm == Confirm.None;

        if (data == null)
        {
            textStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(x, card.y + 90f, w, 30f), "Vazio", textStyle);
            if (GUI.Button(new Rect(x, card.y + card.height - 84f, w, 56f), "Novo jogo", buttonStyle))
            {
                Play(slot, true);
            }
        }
        else
        {
            float y = card.y + 82f;
            textStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            int quota = data.TotalQuota();
            string progress = quota > 0 ? $"{data.TotalConsumed()} / {quota}" : "0";
            GUI.Label(new Rect(x, y, w, 28f), $"Alienígenas consumidos: {progress}", textStyle);
            textStyle.normal.textColor = dnaColor;
            GUI.Label(new Rect(x, y + 32f, w, 28f), $"DNA: {data.dna}", textStyle);
            textStyle.normal.textColor = new Color(1f, 1f, 1f, 0.65f);
            GUI.Label(new Rect(x, y + 64f, w, 28f), $"Tempo de jogo: {FormatTime(data.playTimeSeconds)}", textStyle);
            GUI.Label(new Rect(x, y + 96f, w, 28f), $"Salvo em {FormatDate(data.savedAt)}", textStyle);
            GUI.Label(new Rect(x, y + 128f, w, 28f), $"Semente {data.worldSeed}", textStyle);

            float buttonY = card.y + card.height - 84f;
            float third = (w - 16f) / 3f;
            if (GUI.Button(new Rect(x, buttonY, third, 56f), "Continuar", buttonStyle))
            {
                Play(slot, false);
            }

            if (GUI.Button(new Rect(x + third + 8f, buttonY, third, 56f), "Novo", buttonStyle))
            {
                confirm = Confirm.Overwrite;
                confirmSlot = slot;
            }

            if (GUI.Button(new Rect(x + 2f * (third + 8f), buttonY, third, 56f), "Apagar", buttonStyle))
            {
                confirm = Confirm.Delete;
                confirmSlot = slot;
            }
        }

        GUI.enabled = enabledBefore;
    }

    private void DrawConfirm(float width, float height)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
        Rect box = new Rect((width - 640f) / 2f, (height - 260f) / 2f, 640f, 260f);
        GUI.color = new Color(0.12f, 0.1f, 0.2f, 1f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        string question = confirm == Confirm.Delete
            ? $"Apagar o Jogo {confirmSlot}?"
            : $"Começar um jogo novo no espaço {confirmSlot}?";
        GUI.Label(new Rect(box.x, box.y + 36f, box.width, 44f), question, cardTitleStyle);
        textStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
        textStyle.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect(box.x, box.y + 92f, box.width, 30f), "O jogo salvo neste espaço será perdido.", textStyle);
        textStyle.alignment = TextAnchor.MiddleLeft;

        float buttonY = box.y + box.height - 90f;
        if (GUI.Button(new Rect(box.x + 60f, buttonY, 240f, 56f), confirm == Confirm.Delete ? "Apagar" : "Jogo novo", buttonStyle))
        {
            int slot = confirmSlot;
            Confirm action = confirm;
            confirm = Confirm.None;
            if (action == Confirm.Delete)
            {
                SaveSystem.Delete(slot);
                Refresh();
            }
            else
            {
                Play(slot, true);
            }
        }

        if (GUI.Button(new Rect(box.xMax - 300f, buttonY, 240f, 56f), "Cancelar", buttonStyle))
        {
            confirm = Confirm.None;
        }
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

    private void BuildStyles()
    {
        if (titleStyle != null)
        {
            return;
        }

        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 84, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = Color.white;
        subtitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter };
        subtitleStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
        cardTitleStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        cardTitleStyle.normal.textColor = Color.white;
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft };

        buttonTexture = SolidTexture(accent * 0.8f);
        buttonHoverTexture = SolidTexture(accent);
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 24, fontStyle = FontStyle.Bold };
        buttonStyle.normal.background = buttonTexture;
        buttonStyle.hover.background = buttonHoverTexture;
        buttonStyle.active.background = buttonHoverTexture;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.textColor = Color.white;
        buttonStyle.active.textColor = Color.white;
    }

    private static Texture2D SolidTexture(Color color)
    {
        color.a = 1f;
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
