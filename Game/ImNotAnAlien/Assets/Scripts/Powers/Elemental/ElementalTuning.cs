using UnityEngine;

// AJUSTES dos poderes elementais (Missão 21) num lugar só.
// O arquivo fica em Assets/Resources/Elemental/ElementalTuning.asset (criado por
// "ImNotAnAlien > Preparar Poderes Elementais"). Clique nele no Project e mude os números no Inspector:
// vale na hora, inclusive durante o Play, e fica salvo. Sem o arquivo, o jogo usa os valores daqui.
// Onde há dois números (X e Y): X = toque rápido, Y = disparo totalmente carregado.
[CreateAssetMenu(menuName = "ImNotAnAlien/Ajustes dos Poderes Elementais", fileName = "ElementalTuning")]
public sealed class ElementalTuning : ScriptableObject
{
    private static ElementalTuning current;

    public static ElementalTuning Current
    {
        get
        {
            if (current == null)
            {
                current = Resources.Load<ElementalTuning>("Elemental/ElementalTuning");
                if (current == null)
                {
                    current = CreateInstance<ElementalTuning>();
                    current.name = "ElementalTuning (padrão)";
                }
            }

            return current;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
    }

    [Header("Energia")]
    [Min(1f)] public float maxEnergy = 100f;
    [Tooltip("Energia que volta por segundo.")]
    [Min(0f)] public float regenPerSecond = 14f;
    [Tooltip("Segundos sem disparar até a energia começar a voltar.")]
    [Min(0f)] public float regenDelay = 0.6f;
    [Tooltip("Custo do toque rápido.")]
    [Min(0f)] public float tapCost = 7f;
    [Tooltip("Custo do disparo totalmente carregado.")]
    [Min(0f)] public float chargedCost = 30f;
    [Tooltip("Energia que volta para cada ponto de DNA absorvido.")]
    [Min(0f)] public float energyPerDna = 1.5f;

    [Header("Disparo")]
    [Tooltip("Segurar o botão por mais que isto (s) começa a carregar.")]
    [Min(0.05f)] public float holdToCharge = 0.22f;
    [Tooltip("Segundos carregando até a carga máxima.")]
    [Min(0.1f)] public float fullChargeTime = 1f;
    [Tooltip("Intervalo mínimo entre dois disparos (s).")]
    [Min(0.05f)] public float shotInterval = 0.3f;
    [Tooltip("Do clique até o disparo sair da mão (s): o tempo do gesto.")]
    [Min(0f)] public float castDelay = 0.11f;
    [Tooltip("Alcance da mira (m).")]
    [Min(5f)] public float range = 60f;

    [Header("Fogo")]
    [Min(1f)] public float fireSpeed = 30f;
    public Vector2 fireDamage = new Vector2(16f, 42f);
    [Tooltip("Raio da explosão (m).")]
    public Vector2 fireRadius = new Vector2(2.2f, 5f);
    [Tooltip("Dano por segundo enquanto queima.")]
    public Vector2 burnDps = new Vector2(5f, 8f);
    public Vector2 burnSeconds = new Vector2(4f, 6f);
    [Tooltip("Empurrão da explosão em objetos soltos.")]
    public Vector2 firePush = new Vector2(150f, 650f);
    [Tooltip("Dano em casas e estruturas.")]
    public Vector2 fireStructureDamage = new Vector2(8f, 70f);

    [Header("Gelo")]
    [Min(1f)] public float iceSpeed = 46f;
    public Vector2 iceDamage = new Vector2(12f, 30f);
    [Tooltip("Raio do estouro de gelo (m). O toque rápido só acerta o alvo.")]
    public Vector2 iceRadius = new Vector2(0f, 3.6f);
    [Tooltip("Quantos acertos de toque rápido congelam um alienígena.")]
    [Range(1, 6)] public int freezeStacks = 3;
    [Tooltip("Lentidão por acerto (0,3 = 30% mais lento cada).")]
    [Range(0f, 0.45f)] public float slowPerStack = 0.3f;
    [Tooltip("Segundos até o frio passar.")]
    [Min(0.5f)] public float chillSeconds = 6f;
    public Vector2 freezeSeconds = new Vector2(4f, 6.5f);
    [Tooltip("Dano extra quando um congelado leva uma pancada (o gelo estilhaça).")]
    [Min(0f)] public float shatterDamage = 45f;

    [Header("Eletricidade")]
    public Vector2 boltDamage = new Vector2(14f, 34f);
    [Tooltip("Segundos atordoado.")]
    public Vector2 stunSeconds = new Vector2(1f, 2.6f);
    [Tooltip("Para quantos alienígenas vizinhos o raio pula.")]
    public Vector2Int boltJumps = new Vector2Int(2, 5);
    [Tooltip("Distância máxima de cada pulo (m).")]
    public Vector2 jumpRadius = new Vector2(7f, 10f);
    [Tooltip("Quanto do dano sobra a cada pulo.")]
    [Range(0.1f, 1f)] public float jumpFalloff = 0.75f;
    [Tooltip("Na água o raio pula mais longe (multiplica a distância) e para mais alvos.")]
    [Min(1f)] public float waterReach = 1.8f;
    [Min(0)] public int waterExtraJumps = 3;

    public float Cost(float charge) => Mathf.Lerp(tapCost, chargedCost, Mathf.Clamp01(charge));

    public static float Pick(Vector2 range, float charge) => Mathf.Lerp(range.x, range.y, Mathf.Clamp01(charge));
}
