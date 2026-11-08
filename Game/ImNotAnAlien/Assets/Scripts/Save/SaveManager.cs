using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Salvamento dentro do jogo (Missão 17). Fica no objeto "Sistemas" da cena do jogo.
// Ao começar: carrega o espaço escolhido no menu (ou começa um jogo novo) e devolve
//   DNA, evoluções, progresso dos setores e o lugar onde o jogador estava.
// Salva sozinho: ao limpar um setor ou a região, a cada poucos minutos e ao fechar o jogo.
// Salva na mão: F5. No menu de evoluções (Tab) há "Salvar e voltar ao menu".
// Não guarda nível de procurado, casas, carros nem alienígenas soltos (decisão D23).
//
// Play direto nesta cena pelo editor (sem passar pelo menu): usa o Jogo 1 (continua se existir).
[DefaultExecutionOrder(-950)]
[DisallowMultipleComponent]
public sealed class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    // Abaixo disso o jogador caiu para fora do mundo: essa posição não é salva nem carregada.
    private const float MinValidHeight = -10f;

    [SerializeField] private string menuSceneName = "MenuInicial";
    [Tooltip("Salvamento automático a cada X segundos de jogo (0 = desligado).")]
    [SerializeField, Min(0f)] private float autosaveInterval = 180f;
    [SerializeField] private bool manualSaveKey = true;
    [Tooltip("Só no editor: Play direto nesta cena sempre começa um jogo novo no Jogo 1.")]
    [SerializeField] private bool editorAlwaysNewGame;

    private SaveData data;
    private int slot;
    private bool isNewGame;
    private Transform player;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private SectorTracker tracker;
    private float autosaveTimer;
    private string toast;
    private float toastUntil;
    private GUIStyle toastStyle;

    public SaveData Data => data;
    public int Slot => slot;

    private void Awake()
    {
        Instance = this;

        slot = SaveSystem.CurrentSlot;
        bool fromMenu = slot >= 1;
        if (!fromMenu)
        {
            slot = 1;
        }

        isNewGame = fromMenu ? SaveSystem.PendingNewGame : Application.isEditor && editorAlwaysNewGame;
        SaveSystem.PendingNewGame = false;
        SaveSystem.CurrentSlot = slot;

        data = isNewGame ? null : SaveSystem.Load(slot);
        if (data == null)
        {
            isNewGame = true;
            data = SaveData.NewGame();
        }

        Debug.Log(isNewGame
            ? $"Jogo {slot}: NOVO JOGO (semente {data.worldSeed})."
            : $"Jogo {slot}: continuando (semente {data.worldSeed}, DNA {data.dna}, salvo em {data.savedAt}).");

        // O jogador volta para onde estava, antes de o mundo carregar os blocos em volta dele.
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        player = controller != null ? controller.transform : null;
        if (player != null)
        {
            startPosition = player.position;
            startRotation = player.rotation;
            // Posição salva fora do mundo (ex.: caiu no vazio): ignora e começa no ponto inicial.
            if (data.hasPlayerPosition && data.playerPosition.y > MinValidHeight)
            {
                player.position = WorldOrigin.ToWorld(data.playerPosition) + Vector3.up * 0.3f;
                Physics.SyncTransforms();
            }
        }

        // Progresso da região (setores limpos e consumidos), antes de a população começar a surgir.
        tracker = FindFirstObjectByType<SectorTracker>();
        if (tracker != null)
        {
            SaveData.RegionSave region = data.FindRegion(tracker.RegionName);
            if (region != null)
            {
                tracker.RestoreProgress(region.sectors);
            }
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (tracker != null)
        {
            tracker.SectorCleared -= OnSectorCleared;
            tracker.RegionCleared -= OnRegionCleared;
        }
    }

    private void Start()
    {
        // Renascer continua sendo no ponto inicial da região (as bases vêm na Missão 19).
        if (player != null && player.TryGetComponent(out PlayerHealth health))
        {
            health.SetSpawnPoint(startPosition, startRotation);
        }

        if (tracker != null)
        {
            tracker.SectorCleared += OnSectorCleared;
            tracker.RegionCleared += OnRegionCleared;
        }

        StartCoroutine(ApplyAfterStart());
    }

    // DNA e evoluções só depois que o EvolutionSystem preparou o nível 0 (no Start dele).
    private IEnumerator ApplyAfterStart()
    {
        yield return null;

        if (DnaInventory.Instance != null)
        {
            DnaInventory.Instance.SetTotal(data.dna);
        }

        EvolutionSystem evolutions = EvolutionSystem.Instance;
        if (evolutions != null)
        {
            foreach (SaveData.EvolutionSave saved in data.evolutions)
            {
                if (Enum.TryParse(saved.id, out EvolutionSystem.EvolutionId id))
                {
                    evolutions.SetLevel(id, saved.level);
                }
            }
        }

        if (isNewGame)
        {
            Save(false); // cria o arquivo já no começo, para o "Continuar" funcionar
        }
        else
        {
            ShowToast($"Jogo {slot} carregado");
        }
    }

    private void Update()
    {
        if (!GamePause.IsPaused)
        {
            data.playTimeSeconds += Time.unscaledDeltaTime;
        }

        if (manualSaveKey && Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
        {
            Save(true);
        }

        if (autosaveInterval > 0f && !GamePause.IsPaused)
        {
            autosaveTimer += Time.unscaledDeltaTime;
            if (autosaveTimer >= autosaveInterval)
            {
                Save(true);
            }
        }
    }

    private void OnApplicationQuit()
    {
        Save(false);
    }

    private void OnSectorCleared(int index) => Save(true);

    private void OnRegionCleared() => Save(true);

    // ---------- Salvar ----------

    public bool Save(bool notify)
    {
        autosaveTimer = 0f;
        Collect();
        bool ok = SaveSystem.Write(slot, data);
        if (notify)
        {
            ShowToast(ok ? "Jogo salvo" : "Não consegui salvar (veja o Console)");
        }

        return ok;
    }

    public void SaveAndQuitToMenu()
    {
        Save(false);
        GamePause.Resume();
        if (Application.CanStreamedLevelBeLoaded(menuSceneName))
        {
            SceneManager.LoadScene(menuSceneName);
        }
        else
        {
            Debug.LogWarning($"A cena '{menuSceneName}' não está no Build. Rode 'Preparar Salvamento (Missao 17)'.");
        }
    }

    private void Collect()
    {
        data.version = SaveData.CurrentVersion;
        data.savedAt = DateTime.Now.ToString("o");

        if (DnaInventory.Instance != null)
        {
            data.dna = DnaInventory.Instance.Total;
        }

        EvolutionSystem evolutions = EvolutionSystem.Instance;
        if (evolutions != null)
        {
            data.evolutions.Clear();
            foreach (EvolutionSystem.Evolution evolution in evolutions.Evolutions)
            {
                data.evolutions.Add(new SaveData.EvolutionSave { id = evolution.id.ToString(), level = evolutions.GetLevel(evolution) });
            }
        }

        // Morto ou caindo no vazio: guarda o ponto de renascer.
        bool dead = PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead ||
                    player != null && player.position.y < MinValidHeight;
        if (player != null)
        {
            data.hasPlayerPosition = true;
            data.playerPosition = WorldOrigin.ToLogical(dead ? startPosition : player.position);
        }

        if (tracker != null && !tracker.SingleSectorMode)
        {
            SaveData.RegionSave region = data.GetOrAddRegion(tracker.RegionName);
            region.quota = tracker.RegionQuota;
            region.consumed = tracker.RegionConsumed;
            region.cleared = tracker.IsCleared;
            region.sectors.Clear();
            foreach (SectorTracker.SectorInfo sector in tracker.Sectors)
            {
                region.sectors.Add(new SaveData.SectorSave { name = sector.name, consumed = sector.consumed, cleared = sector.cleared });
            }
        }
    }

    // ---------- Aviso na tela ----------

    private void ShowToast(string text)
    {
        toast = text;
        toastUntil = Time.unscaledTime + 2f;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(toast) || Time.unscaledTime > toastUntil)
        {
            return;
        }

        if (toastStyle == null)
        {
            toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
        }

        float scale = Screen.height / 1080f;
        toastStyle.fontSize = Mathf.RoundToInt(22 * scale);
        float alpha = Mathf.Clamp01((toastUntil - Time.unscaledTime) / 0.5f);
        toastStyle.normal.textColor = new Color(0.35f, 1f, 0.65f, alpha);
        GUI.Label(new Rect(0f, 0f, Screen.width - 24f * scale, Screen.height - 20f * scale), toast, toastStyle);
    }
}
