using System;
using System.Collections.Generic;
using UnityEngine;

// Evoluções compradas com DNA (Missão 9, decisão D16).
// Cada evolução tem níveis; cada nível custa DNA. Os custos e textos ficam editáveis
// no Inspector (lista "Evolutions"). Os EFEITOS de cada nível estão no código, em Apply().
// O nível 0 é o estado inicial: Devorar e Onda de Impacto começam bloqueados.
[DisallowMultipleComponent]
public sealed class EvolutionSystem : MonoBehaviour
{
    public enum EvolutionId
    {
        DnaMagnet,
        TelekineticForce,
        MentalReach,
        AlienSkin,
        Devour,
        Shockwave,
    }

    [Serializable]
    public sealed class Evolution
    {
        public EvolutionId id;
        public string title;
        [TextArea] public string description;
        [Tooltip("Custo de cada nível, em DNA. A quantidade de itens = número de níveis.")]
        public int[] levelCosts;
        [Tooltip("Texto curto do efeito de cada nível (mesma ordem dos custos).")]
        public string[] levelEffects;
    }

    public static EvolutionSystem Instance { get; private set; }

    [SerializeField] private List<Evolution> evolutions = new List<Evolution>
    {
        new Evolution
        {
            id = EvolutionId.DnaMagnet, title = "Ímã de DNA",
            description = "Os orbes de DNA voam até você de mais longe.",
            levelCosts = new[] { 20, 40, 70 },
            levelEffects = new[] { "Atração: 9 m", "Atração: 12 m", "Atração: 16 m" },
        },
        new Evolution
        {
            id = EvolutionId.TelekineticForce, title = "Força Telecinética",
            description = "Levanta objetos mais pesados e arremessa mais forte.",
            levelCosts = new[] { 30, 60, 100 },
            levelEffects = new[] { "Peso 2.500 kg, arremesso +15%", "Peso 4.000 kg, arremesso +30%", "Peso 6.000 kg, arremesso +50%" },
        },
        new Evolution
        {
            id = EvolutionId.MentalReach, title = "Alcance Mental",
            description = "Alcança alvos mais distantes com a telecinese.",
            levelCosts = new[] { 25, 50 },
            levelEffects = new[] { "Alcance: 30 m", "Alcance: 35 m" },
        },
        new Evolution
        {
            id = EvolutionId.AlienSkin, title = "Pele Alienígena",
            description = "Seu corpo fica mais resistente. (Ainda é humano. Juro.)",
            levelCosts = new[] { 30, 60, 90 },
            levelEffects = new[] { "Vida: 130", "Vida: 160", "Vida: 200" },
        },
        new Evolution
        {
            id = EvolutionId.Devour, title = "Devorar",
            description = "Tecla 1: puxa e devora um alienígena ferido próximo.",
            levelCosts = new[] { 50, 120 },
            levelEffects = new[] { "Desbloqueia: 2x DNA", "Mordida gulosa: 3x DNA" },
        },
        new Evolution
        {
            id = EvolutionId.Shockwave, title = "Onda de Impacto",
            description = "Tecla 2: explosão telecinética que arremessa tudo em volta.",
            levelCosts = new[] { 80, 140 },
            levelEffects = new[] { "Desbloqueia: raio 8 m", "Raio 12 m, mais força e dano" },
        },
    };

    // Valores originais (lidos dos componentes ao iniciar), base para os multiplicadores.
    private float baseMagnetRadius;
    private float baseLiftMass;
    private float baseThrowSpeed;
    private float baseTargetRange;
    private float baseMaxHealth;

    private readonly Dictionary<EvolutionId, int> levels = new Dictionary<EvolutionId, int>();

    private TelekinesisPower telekinesis;
    private DevourAbility devour;
    private ShockwaveAbility shockwave;
    private PlayerHealth playerHealth;

    public IReadOnlyList<Evolution> Evolutions => evolutions;

    // Disparado quando uma evolução é comprada.
    public event Action<Evolution> Evolved;

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
    }

    private void Start()
    {
        PrototypeThirdPersonController player = FindFirstObjectByType<PrototypeThirdPersonController>();
        if (player != null)
        {
            telekinesis = player.GetComponent<TelekinesisPower>();
            devour = player.GetComponent<DevourAbility>();
            playerHealth = player.GetComponent<PlayerHealth>();
            shockwave = player.GetComponent<ShockwaveAbility>();
            if (shockwave == null)
            {
                shockwave = player.gameObject.AddComponent<ShockwaveAbility>();
            }
        }

        if (DnaOrbSystem.Instance != null) baseMagnetRadius = DnaOrbSystem.Instance.MagnetRadius;
        if (telekinesis != null)
        {
            baseLiftMass = telekinesis.MaxLiftMass;
            baseThrowSpeed = telekinesis.ThrowSpeed;
            baseTargetRange = telekinesis.TargetRange;
        }

        if (playerHealth != null) baseMaxHealth = playerHealth.MaxHealth;

        // Aplica o nível 0 de tudo (ex.: Devorar e Onda começam bloqueados).
        foreach (Evolution evolution in evolutions)
        {
            levels[evolution.id] = 0;
            Apply(evolution.id, 0);
        }
    }

    // ---------- Consulta ----------

    public int GetLevel(Evolution evolution) => levels.TryGetValue(evolution.id, out int level) ? level : 0;

    public int GetLevel(EvolutionId id) => levels.TryGetValue(id, out int level) ? level : 0;

    public bool IsMaxed(Evolution evolution) => GetLevel(evolution) >= evolution.levelCosts.Length;

    public int NextCost(Evolution evolution) => IsMaxed(evolution) ? 0 : evolution.levelCosts[GetLevel(evolution)];

    public string NextEffect(Evolution evolution)
    {
        int level = GetLevel(evolution);
        return evolution.levelEffects != null && level < evolution.levelEffects.Length ? evolution.levelEffects[level] : "";
    }

    public bool CanBuy(Evolution evolution)
    {
        return !IsMaxed(evolution) && DnaInventory.Instance != null && DnaInventory.Instance.Total >= NextCost(evolution);
    }

    // Existe alguma evolução que dá para comprar agora? (para o aviso no HUD)
    public bool AnyAffordable()
    {
        foreach (Evolution evolution in evolutions)
        {
            if (CanBuy(evolution))
            {
                return true;
            }
        }

        return false;
    }

    // ---------- Compra ----------

    public bool TryBuy(Evolution evolution)
    {
        if (!CanBuy(evolution) || !DnaInventory.Instance.TrySpend(NextCost(evolution)))
        {
            return false;
        }

        int level = GetLevel(evolution) + 1;
        levels[evolution.id] = level;
        Apply(evolution.id, level);
        GameFeel.EvolutionBought();
        Evolved?.Invoke(evolution);
        Debug.Log($"Evolução: {evolution.title} nível {level}.");
        return true;
    }

    // ---------- Efeitos de cada nível ----------

    private void Apply(EvolutionId id, int level)
    {
        switch (id)
        {
            case EvolutionId.DnaMagnet:
                if (DnaOrbSystem.Instance != null)
                {
                    float[] multiplier = { 1f, 1.5f, 2f, 2.65f }; // 6 -> 9 -> 12 -> ~16 m
                    DnaOrbSystem.Instance.MagnetRadius = baseMagnetRadius * Pick(multiplier, level);
                }

                break;

            case EvolutionId.TelekineticForce:
                if (telekinesis != null)
                {
                    float[] mass = { 1f, 1.67f, 2.67f, 4f };     // 1.500 -> 2.500 -> 4.000 -> 6.000 kg
                    float[] speed = { 1f, 1.15f, 1.3f, 1.5f };
                    telekinesis.MaxLiftMass = baseLiftMass * Pick(mass, level);
                    telekinesis.ThrowSpeed = baseThrowSpeed * Pick(speed, level);
                }

                break;

            case EvolutionId.MentalReach:
                if (telekinesis != null)
                {
                    telekinesis.TargetRange = baseTargetRange + 5f * level; // 25 -> 30 -> 35 m
                }

                break;

            case EvolutionId.AlienSkin:
                if (playerHealth != null)
                {
                    float[] multiplier = { 1f, 1.3f, 1.6f, 2f }; // 100 -> 130 -> 160 -> 200
                    playerHealth.SetMaxHealth(baseMaxHealth * Pick(multiplier, level));
                }

                break;

            case EvolutionId.Devour:
                if (devour != null)
                {
                    devour.Unlocked = level >= 1;
                    devour.DnaMultiplier = level >= 2 ? 3 : 2;
                }

                break;

            case EvolutionId.Shockwave:
                if (shockwave != null)
                {
                    shockwave.SetLevel(level);
                }

                break;
        }
    }

    private static float Pick(float[] values, int level) => values[Mathf.Clamp(level, 0, values.Length - 1)];
}
