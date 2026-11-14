using System.Collections.Generic;
using UnityEngine;

// Uma base do protagonista (Missão 19, decisão D22). Fica na raiz do bloco da base (montado pelo BlockBuilder).
// Dentro do anel brilhante (zona segura):
// - vida cheia e o nível de procurado zera;
// - a polícia não desconfia e não surge ninguém lá dentro;
// - a base vira o ponto de renascer e o jogo é salvo;
// - a base fica marcada como visitada (para a viagem rápida pelo globo).
[DisallowMultipleComponent]
public sealed class BaseSite : MonoBehaviour
{
    public const float DefaultRadius = 22f;

    private static readonly List<BaseSite> Active = new List<BaseSite>();

    // Base onde o jogador está agora (null = fora de todas).
    public static BaseSite PlayerInside { get; private set; }

    [SerializeField] private string baseId;
    [SerializeField] private Vector3 localCenter;
    [SerializeField, Min(1f)] private float radius = DefaultRadius;

    private Transform player;
    private float wantedTimer;

    public string BaseId => baseId;
    public Vector3 Center => transform.TransformPoint(localCenter);
    public float Radius => radius;

    // Lugar para nascer/chegar: no pátio, de frente para a rua.
    public Vector3 SpawnPoint => Center + new Vector3(0f, 0.3f, -8f);
    public Quaternion SpawnRotation => Quaternion.Euler(0f, 180f, 0f);

    public string DisplayName
    {
        get
        {
            foreach (Planet.BaseInfo info in Planet.Bases)
            {
                if (info.id == baseId)
                {
                    return info.name;
                }
            }

            return "Base";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active.Clear();
        PlayerInside = null;
    }

    public void Setup(string id, Vector3 center)
    {
        baseId = string.IsNullOrEmpty(id) ? "nave" : id;
        localCenter = center;
    }

    private void OnEnable()
    {
        Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
        if (PlayerInside == this)
        {
            PlayerInside = null;
        }
    }

    private void Start()
    {
        PlayerHealth health = PlayerHealth.Instance;
        player = health != null ? health.transform : null;
    }

    private void Update()
    {
        if (player == null)
        {
            return;
        }

        Vector3 offset = player.position - Center;
        offset.y = 0f;
        bool inside = offset.sqrMagnitude <= radius * radius;
        if (inside && PlayerInside != this)
        {
            Enter();
        }
        else if (!inside && PlayerInside == this)
        {
            PlayerInside = null;
        }

        // Enquanto estiver na base, o procurado continua zerado.
        if (inside)
        {
            wantedTimer -= Time.deltaTime;
            if (wantedTimer <= 0f)
            {
                wantedTimer = 0.5f;
                if (WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0)
                {
                    WantedSystem.Instance.ClearWanted();
                }
            }
        }
    }

    private void Enter()
    {
        PlayerInside = this;
        PlayerHealth health = PlayerHealth.Instance;
        if (health == null || health.IsDead || health.IsArrested)
        {
            return;
        }

        health.HealFull();
        health.SetSpawnPoint(SpawnPoint, SpawnRotation);
        if (WantedSystem.Instance != null)
        {
            WantedSystem.Instance.ClearWanted();
        }

        bool firstVisit = SaveManager.Instance != null && SaveManager.Instance.OnBaseVisited(baseId);
        GameHud hud = GameHud.Instance;
        if (hud != null)
        {
            hud.ShowBanner(firstVisit
                ? $"BASE ENCONTRADA: {DisplayName.ToUpperInvariant()}\n<size=28>Vida cheia, procurado zerado, jogo salvo. Você renasce aqui.</size>"
                : $"{DisplayName.ToUpperInvariant()}\n<size=28>Vida cheia, procurado zerado, jogo salvo.</size>", 4f);
        }
    }

    // ---------- Consultas ----------

    // O ponto (MUNDO) está dentro de alguma base carregada (com folga)?
    public static bool IsInsideAny(Vector3 world, float margin = 0f)
    {
        foreach (BaseSite site in Active)
        {
            Vector3 offset = world - site.Center;
            offset.y = 0f;
            float r = site.radius + margin;
            if (offset.sqrMagnitude <= r * r)
            {
                return true;
            }
        }

        return false;
    }
}
