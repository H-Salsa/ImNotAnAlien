using System.Collections.Generic;
using UnityEngine;

// Ar debaixo d'água (Missão 20, pedido do autor).
// - Com a cabeça debaixo d'água o ar vai acabando (barra azul "AR" em cima da barra de fôlego).
//   Acabou: perde vida a cada segundo até subir. Na superfície o ar volta rápido.
// - Consumir DNA de um alien AQUÁTICO (componente AquaticAlien) recarrega o ar: cada orbe absorvido
//   devolve um pedaço; devorar devolve tudo.
// - Visual de "debaixo d'água" quando a câmera está abaixo da superfície: tela azulada e neblina curta.
// Colocado sozinho no jogador pelo PrototypeThirdPersonController.
[DisallowMultipleComponent]
public sealed class PlayerBreath : MonoBehaviour
{
    public static PlayerBreath Instance { get; private set; }

    [Tooltip("Segundos de ar debaixo d'água.")]
    [SerializeField, Min(1f)] private float maxAir = 20f;
    [Tooltip("Ar recuperado por segundo com a cabeça fora d'água.")]
    [SerializeField, Min(0f)] private float refillPerSecond = 6f;
    [Tooltip("Dano por segundo sem ar.")]
    [SerializeField, Min(0f)] private float drownDamage = 10f;
    [Tooltip("Fração do ar devolvida por orbe de DNA de alien aquático.")]
    [SerializeField, Range(0f, 1f)] private float refillPerOrb = 0.3f;

    [Header("Debaixo d'água (câmera)")]
    [SerializeField] private Color underwaterTint = new Color(0.08f, 0.38f, 0.5f, 0.28f);
    [SerializeField] private Color underwaterFog = new Color(0.12f, 0.42f, 0.52f);
    [SerializeField, Min(0f)] private float underwaterFogDensity = 0.045f;

    public float Air { get; private set; }
    public float Air01 => Air / maxAir;

    // Aliens aquáticos que soltaram orbes (id do alien -> ainda vale recarga).
    private static readonly HashSet<int> aquaticSources = new HashSet<int>();

    private PrototypeThirdPersonController mover;
    private PlayerHealth health;
    private float damageTimer;
    private float shownAlpha;
    private bool fogSwapped;
    private bool savedFog;
    private Color savedFogColor;
    private FogMode savedFogMode;
    private float savedFogDensity;

    private void Awake()
    {
        Instance = this;
        mover = GetComponent<PrototypeThirdPersonController>();
        health = GetComponent<PlayerHealth>();
        Air = maxAir;
        if (health != null)
        {
            health.Respawned += () => Air = maxAir;
        }
    }

    private void OnDisable()
    {
        RestoreFog();
    }

    private void Update()
    {
        if (GamePause.IsPaused || mover == null)
        {
            return;
        }

        bool under = mover.IsSubmerged && (health == null || !health.IsDead);
        if (under)
        {
            Air = Mathf.Max(0f, Air - Time.deltaTime);
            if (Air <= 0f && health != null)
            {
                damageTimer -= Time.deltaTime;
                if (damageTimer <= 0f)
                {
                    damageTimer = 1f;
                    health.TakeDamage(drownDamage, transform.position + Vector3.up);
                    GameFeel.Splash(transform.position + Vector3.up * 1.6f, 0.3f); // bolhas
                }
            }
        }
        else
        {
            damageTimer = 0.5f;
            Air = Mathf.Min(maxAir, Air + refillPerSecond * Time.deltaTime);
        }

        UpdateUnderwaterLook();
    }

    // ---------- Recarga por DNA aquático ----------

    public void Refill(float fraction)
    {
        Air = Mathf.Min(maxAir, Air + maxAir * Mathf.Clamp01(fraction));
    }

    // Chamado quando um alien solta os orbes (DnaOrbSystem.SpawnFrom).
    public static void NoteOrbsFrom(AlienHealth source)
    {
        if (source != null && source.GetComponent<AquaticAlien>() != null)
        {
            aquaticSources.Add(source.GetInstanceID());
        }
    }

    // Chamado a cada orbe absorvido (DnaOrbSystem.Absorb).
    public static void OnOrbAbsorbed(int sourceAlienId)
    {
        if (Instance != null && aquaticSources.Contains(sourceAlienId))
        {
            Instance.Refill(Instance.refillPerOrb);
        }
    }

    // Devorou um alien (DevourAbility).
    public static void OnDevoured(AlienHealth alien)
    {
        if (Instance != null && alien != null && alien.GetComponent<AquaticAlien>() != null)
        {
            Instance.Refill(1f);
        }
    }

    // ---------- Visual debaixo d'água ----------

    private bool CameraUnderwater()
    {
        Camera cam = Camera.main;
        return cam != null && mover.IsSwimming && cam.transform.position.y < mover.WaterSurfaceY - 0.05f;
    }

    private void UpdateUnderwaterLook()
    {
        bool under = CameraUnderwater();
        if (under && !fogSwapped)
        {
            savedFog = RenderSettings.fog;
            savedFogColor = RenderSettings.fogColor;
            savedFogMode = RenderSettings.fogMode;
            savedFogDensity = RenderSettings.fogDensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = underwaterFog;
            RenderSettings.fogDensity = underwaterFogDensity;
            fogSwapped = true;
        }
        else if (!under)
        {
            RestoreFog();
        }
    }

    private void RestoreFog()
    {
        if (!fogSwapped)
        {
            return;
        }

        RenderSettings.fog = savedFog;
        RenderSettings.fogColor = savedFogColor;
        RenderSettings.fogMode = savedFogMode;
        RenderSettings.fogDensity = savedFogDensity;
        fogSwapped = false;
    }

    private void OnGUI()
    {
        if (GamePause.IsPaused || mover == null)
        {
            return;
        }

        if (fogSwapped)
        {
            GUI.color = underwaterTint;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        // Barra de ar: aparece mergulhado ou enquanto não está cheia.
        bool show = mover.IsSubmerged || Air < maxAir - 0.01f;
        shownAlpha = Mathf.MoveTowards(shownAlpha, show ? 1f : 0f, Time.unscaledDeltaTime * 3f);
        if (shownAlpha <= 0.01f)
        {
            GUI.color = Color.white;
            return;
        }

        float scale = Screen.height / 1080f;
        float width = 260f * scale;
        float height = 12f * scale;
        Rect back = new Rect((Screen.width - width) / 2f, Screen.height - 146f * scale, width, height);
        GUI.color = new Color(0f, 0f, 0f, 0.55f * shownAlpha);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        bool low = Air01 < 0.25f;
        float blink = low ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 10f) : 1f;
        Color fill = low ? new Color(1f, 0.45f, 0.35f) : new Color(0.35f, 0.75f, 1f);
        fill.a = shownAlpha * blink;
        GUI.color = fill;
        GUI.DrawTexture(new Rect(back.x, back.y, width * Air01, height), Texture2D.whiteTexture);

        // Bolhinhas sobre a barra (uma por 20% de ar).
        GUI.color = new Color(1f, 1f, 1f, 0.85f * shownAlpha);
        GUIStyle label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18f * scale), fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(back.x - 44f * scale, back.y - 8f * scale, 44f * scale, 28f * scale), "AR", label);
        GUI.color = Color.white;
    }
}
