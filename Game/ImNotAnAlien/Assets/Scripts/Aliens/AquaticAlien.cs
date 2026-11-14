using UnityEngine;
using UnityEngine.AI;

// Alienígena AQUÁTICA (Missão 20). Consumir o DNA dela recarrega o ar debaixo d'água (PlayerBreath).
// Prefab: Resources/Alienigena_Aquatica (ImNotAnAlien > Preparar Alien Aquatico). O PopulationSpawner
// solta algumas na água funda; elas preferem passear dentro d'água (AlienBrain) e nadam submersas,
// perto do fundo, subindo na direção do jogador quando ele entra na água por perto (AlienAnimator).
// Este componente:
// - deixa ela do tamanho dos outros aliens (o AlienAppearance não roda nela: não tem roupas para sortear);
// - pinta a pele num tom de mar (com pequena variação) e as listras mais escuras;
// - na água ela é mais rápida (multiplica a velocidade que o AlienBrain escolheu).
[DefaultExecutionOrder(100)] // depois do AlienBrain, que troca a velocidade do agente
[DisallowMultipleComponent]
public sealed class AquaticAlien : MonoBehaviour
{
    private static readonly Color[] Skins =
    {
        new Color32(0x41, 0xA3, 0x99, 0xFF), new Color32(0x3E, 0x9F, 0xC0, 0xFF), new Color32(0x4F, 0xB8, 0xA8, 0xFF),
        new Color32(0x5B, 0x8F, 0xD6, 0xFF), new Color32(0x2F, 0x9C, 0x8A, 0xFF), new Color32(0x6F, 0xC9, 0xD9, 0xFF),
    };

    [Tooltip("Tamanho (igual aos outros aliens: 1,1).")]
    [SerializeField, Range(0.8f, 1.5f)] private float alienScale = 1.1f;
    [SerializeField, Range(0f, 0.2f)] private float sizeVariation = 0.04f;
    [Tooltip("Na água ela nada este tanto mais rápido que anda em terra.")]
    [SerializeField, Range(1f, 3f)] private float waterSpeedMultiplier = 1.5f;
    [SerializeField, Range(0.3f, 1f)] private float stripeShade = 0.72f;

    private NavMeshAgent agent;
    private AlienAnimator body;
    private float baseSpeed;
    private float lastSet = -1f;

    // Profundidade da água (m) num ponto do mundo: 0 ou menos = terra seca.
    public static float WaterDepth(Vector3 world)
    {
        if (!PlanetTerrain.Active)
        {
            return 0f;
        }

        float water = WorldOrigin.ToWorld(new Vector3(0f, PlanetTerrain.WaterLevel, 0f)).y;
        return water - PlanetTerrain.SurfaceY(world);
    }

    private void Awake()
    {
        transform.localScale = Vector3.one * alienScale * (1f + Random.Range(-sizeVariation, sizeVariation));
        agent = GetComponent<NavMeshAgent>();
        body = GetComponent<AlienAnimator>();
    }

    private void Start()
    {
        Color skin = Skins[Random.Range(0, Skins.Length)];
        Color.RGBToHSV(skin, out float h, out float s, out float v);
        skin = Color.HSVToRGB(Mathf.Repeat(h + Random.Range(-0.02f, 0.02f), 1f), Mathf.Clamp01(s + Random.Range(-0.05f, 0.05f)),
            Mathf.Clamp01(v + Random.Range(-0.05f, 0.05f)));
        Color stripe = new Color(skin.r * stripeShade, skin.g * stripeShade, skin.b * stripeShade, 1f);

        Transform corpo = transform.Find("Corpo");
        if (corpo != null)
        {
            foreach (SkinnedMeshRenderer part in corpo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] shared = part.sharedMaterials;
                Material[] own = part.materials;
                for (int i = 0; i < own.Length; i++)
                {
                    string matName = shared[i] != null ? shared[i].name : "";
                    if (matName.Contains("Listra"))
                    {
                        own[i].color = stripe;
                    }
                    else if (matName.Contains("Pele"))
                    {
                        own[i].color = skin;
                    }
                }

                part.materials = own;
            }
        }

        // A cápsula antiga (escondida) dá a cor das partículas e do DNA quando ela morre.
        foreach (string old in new[] { "Corpo/Tronco", "Corpo/Cabeca" })
        {
            Transform t = transform.Find(old);
            if (t != null && t.TryGetComponent(out Renderer renderer))
            {
                renderer.material.color = skin;
            }
        }
    }

    private void Update()
    {
        if (agent == null || !agent.enabled)
        {
            return;
        }

        // O AlienBrain troca a velocidade ao mudar de estado: guarda essa como base.
        if (!Mathf.Approximately(agent.speed, lastSet))
        {
            baseSpeed = agent.speed;
        }

        float want = baseSpeed * (body != null && body.IsSwimming ? waterSpeedMultiplier : 1f);
        if (!Mathf.Approximately(agent.speed, want))
        {
            agent.speed = want;
        }

        lastSet = agent.speed;
    }
}
