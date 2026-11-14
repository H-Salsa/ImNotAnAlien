using UnityEngine;

// Cria e configura os orbes de DNA (decisão D10):
// 1. Ao morrer, o alienígena solta orbes que saltam do corpo.
// 2. Quando o jogador chega perto (raio de atração), os orbes voam até ele.
// 3. Rede de segurança: orbe não coletado depois de um tempo vem sozinho, de qualquer distância.
[DisallowMultipleComponent]
public sealed class DnaOrbSystem : MonoBehaviour
{
    public static DnaOrbSystem Instance { get; private set; }

    [Header("Visual")]
    [SerializeField] private Material orbMaterial;
    [SerializeField] private Material trailMaterial;
    [SerializeField] private Color orbColor = new Color(0.3f, 1f, 0.6f, 1f);
    [SerializeField, Min(0.05f)] private float orbSize = 0.32f;

    [Header("Comportamento")]
    [Tooltip("Distância em que os orbes começam a voar até o jogador. Pode crescer com evoluções.")]
    [SerializeField, Min(0f)] private float magnetRadius = 6f;
    [Tooltip("Rede de segurança: depois desse tempo (s), o orbe vem sozinho de qualquer distância.")]
    [SerializeField, Min(1f)] private float safetyNetTime = 15f;
    [Tooltip("Tempo (s) mostrando o 'salto' antes de poder ser atraído.")]
    [SerializeField, Min(0f)] private float minAgeBeforePickup = 0.6f;
    [SerializeField, Min(0.1f)] private float burstSpeed = 6f;
    [SerializeField, Min(0f)] private float hoverHeight = 0.6f;
    [SerializeField, Min(1f)] private float homingMaxSpeed = 30f;
    [SerializeField, Min(1f)] private float homingAcceleration = 45f;

    public Transform Player { get; private set; }
    public Material OrbMaterial => orbMaterial;
    public Material TrailMaterial => trailMaterial;
    public Color OrbColor => orbColor;
    public float OrbSize => orbSize;
    public float SafetyNetTime => safetyNetTime;
    public float MinAgeBeforePickup => minAgeBeforePickup;
    public float HoverHeight => hoverHeight;
    public float HomingMaxSpeed => homingMaxSpeed;
    public float HomingAcceleration => homingAcceleration;

    // Público para as evoluções futuras poderem aumentar o raio.
    public float MagnetRadius
    {
        get => magnetRadius;
        set => magnetRadius = Mathf.Max(0f, value);
    }

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
            Player = player.transform;
        }
        else
        {
            Debug.LogWarning("DnaOrbSystem: jogador não encontrado; orbes não terão para onde ir.", this);
        }
    }

    // Chamado pelo AlienHealth quando o alienígena morre.
    public static void SpawnFrom(AlienHealth source, Vector3 position, int totalValue, int orbCount)
    {
        int alienId = source.GetInstanceID();
        PlayerBreath.NoteOrbsFrom(source);

        if (Instance == null)
        {
            // Sem sistema de orbes na cena: entrega o DNA direto, para não quebrar a contagem.
            if (DnaInventory.Instance != null)
            {
                DnaInventory.Instance.Add(totalValue);
            }

            if (SectorTracker.Instance != null)
            {
                SectorTracker.Instance.OnAlienDied(source, 0);
            }

            return;
        }

        if (SectorTracker.Instance != null)
        {
            SectorTracker.Instance.OnAlienDied(source, Mathf.Max(1, orbCount));
        }

        Instance.Spawn(alienId, position, totalValue, orbCount);
    }

    private void Spawn(int alienId, Vector3 position, int totalValue, int orbCount)
    {
        int count = Mathf.Max(1, orbCount);

        for (int i = 0; i < count; i++)
        {
            // Divide o valor entre os orbes (o resto vai para os primeiros).
            int value = totalValue / count + (i < totalValue % count ? 1 : 0);

            Vector2 spread = Random.insideUnitCircle * burstSpeed * 0.5f;
            Vector3 velocity = new Vector3(spread.x, burstSpeed * Random.Range(0.8f, 1.3f), spread.y);

            DnaOrb.Create(this, alienId, value, position, velocity);
        }
    }

    // Chamado pelo orbe quando chega ao jogador.
    public void Absorb(DnaOrb orb)
    {
        if (DnaInventory.Instance != null)
        {
            DnaInventory.Instance.Add(orb.Value);
        }

        if (SectorTracker.Instance != null)
        {
            SectorTracker.Instance.OnOrbAbsorbed(orb.SourceAlienId);
        }

        PlayerBreath.OnOrbAbsorbed(orb.SourceAlienId); // Missão 20: DNA aquático recarrega o ar

        GameFeel.DnaAbsorbed(orb.transform.position, orbColor);
    }
}
