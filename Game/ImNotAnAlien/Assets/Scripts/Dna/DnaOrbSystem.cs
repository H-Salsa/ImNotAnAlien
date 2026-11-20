using System.Collections.Generic;
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

    // Caixa de orbes (reaproveitamento, 18/11): orbe absorvido fica guardado, desligado, e volta na
    // próxima morte de alien ou chuva de DNA. Menos lixo de memória = menos travadinhas.
    private const int OrbPoolCap = 200;
    private const int OrbPrewarm = 96; // na briga de 18/11 chegaram a ter 64 orbes no ar ao mesmo tempo
    private readonly Stack<DnaOrb> freeOrbs = new Stack<DnaOrb>();

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

        // Enche a caixa no carregamento: a primeira chuva de DNA não trava criando orbes.
        for (int i = freeOrbs.Count; i < OrbPrewarm; i++)
        {
            StoreOrb(DnaOrb.Build(this));
        }
    }

    // Um orbe guardado, ou null se a caixa estiver vazia (aí o DnaOrb monta um novo).
    public DnaOrb TakeOrb()
    {
        while (freeOrbs.Count > 0)
        {
            DnaOrb orb = freeOrbs.Pop();
            if (orb != null)
            {
                return orb;
            }
        }

        return null;
    }

    // Guarda o orbe (desligado) para usar de novo. Caixa cheia: destrói.
    public void StoreOrb(DnaOrb orb)
    {
        if (orb == null || orb.InBox)
        {
            return;
        }

        if (freeOrbs.Count >= OrbPoolCap)
        {
            Destroy(orb.gameObject);
            return;
        }

        orb.MarkStored();
        orb.gameObject.SetActive(false);
        freeOrbs.Push(orb);
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

    // Chuva de DNA da recompensa de setor (Missão 22): os orbes caem do céu em volta do jogador
    // e voam até ele. Não vieram de alienígena nenhum (id 0), então não mexem na contagem do
    // setor nem na barra de ar.
    public void SpawnRain(Vector3 center, int totalValue, int orbCount, float radius, float height)
    {
        if (totalValue <= 0)
        {
            return;
        }

        int count = Mathf.Clamp(orbCount, 1, totalValue);
        for (int i = 0; i < count; i++)
        {
            int value = totalValue / count + (i < totalValue % count ? 1 : 0);
            Vector2 spread = Random.insideUnitCircle * radius;
            Vector3 position = center + new Vector3(spread.x, height + Random.Range(0f, 3f), spread.y);
            Vector3 velocity = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(-1f, 2f), Random.Range(-1.5f, 1.5f));
            DnaOrb.Create(this, 0, value, position, velocity);
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
