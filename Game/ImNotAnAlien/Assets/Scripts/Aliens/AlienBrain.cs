using UnityEngine;
using UnityEngine.AI;

// Comportamento dos alienígenas da Versão 0.1:
// - Passeio: andam devagar até pontos aleatórios perto de "casa" e param um pouco.
// - Fuga: quando o jogador chega perto, levam um susto (pulinho + "!") e correm para longe.
// - Quando o jogador fica longe o bastante, se acalmam e voltam a passear.
// - Telecinese: enquanto levantados ou voando, viram corpos físicos. Quando param
//   no chão (e ainda estão vivos), se levantam tontos e fogem.
// Precisa de um NavMesh "assado" (Bake) na cena para conseguir andar.
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class AlienBrain : MonoBehaviour
{
    private enum State
    {
        Wandering,
        Fleeing,
        Tumbling,
    }

    [Header("Passeio")]
    [Tooltip("Distância máxima, a partir do ponto inicial, dos pontos aleatórios de passeio.")]
    [SerializeField, Min(1f)] private float wanderRadius = 12f;
    [SerializeField, Min(0.1f)] private float wanderSpeed = 1.8f;
    [Tooltip("Tempo parado (mínimo e máximo, em segundos) entre um passeio e outro.")]
    [SerializeField] private Vector2 idleTimeRange = new Vector2(1.5f, 4f);

    [Header("Fuga")]
    [Tooltip("Se o jogador chegar mais perto que isso, o alienígena foge.")]
    [SerializeField, Min(0.5f)] private float detectionRadius = 9f;
    [Tooltip("Quando o jogador estiver mais longe que isso, o alienígena se acalma.")]
    [SerializeField, Min(1f)] private float calmDownDistance = 20f;
    [SerializeField, Min(0.1f)] private float fleeSpeed = 6.5f;
    [Tooltip("Até onde ele tenta correr a cada nova escolha de rota.")]
    [SerializeField, Min(1f)] private float fleeDistance = 10f;
    [SerializeField, Min(0.1f)] private float fleeRepathInterval = 0.4f;

    [Header("Reação (visual)")]
    [Tooltip("Parte visual que dá o pulinho de susto.")]
    [SerializeField] private Transform body;
    [Tooltip("Objeto '!' mostrado enquanto foge.")]
    [SerializeField] private GameObject alertIcon;
    [SerializeField, Min(0f)] private float scareHopHeight = 0.9f;
    [SerializeField, Min(0.05f)] private float scareHopDuration = 0.35f;

    [Header("Telecinese")]
    [Tooltip("Tempo parado no chão, depois de arremessado, até se levantar.")]
    [SerializeField, Min(0.1f)] private float recoverDelay = 1.2f;
    [Tooltip("Se cair abaixo desta altura (fora do mapa), o alienígena é removido.")]
    [SerializeField] private float fallOutHeight = -30f;

    private static readonly float[] FleeAngles = { 0f, 40f, -40f, 80f, -80f, 120f, -120f };

    private NavMeshAgent agent;
    private Rigidbody physicsBody;
    private bool isHeld;
    private float settleTimer;
    private Transform player;
    private Vector3 home;
    private State state;
    private float idleTimer;
    private float repathTimer;
    private float hopTime = -1f;
    private Vector3 bodyRestPosition;
    private bool warnedNoNavMesh;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        home = transform.position;

        // Corpo físico usado pela telecinese. Fica "cinemático" (sem física) enquanto anda.
        if (!TryGetComponent(out physicsBody))
        {
            physicsBody = gameObject.AddComponent<Rigidbody>();
        }

        physicsBody.isKinematic = true;
        physicsBody.mass = 60f;
        physicsBody.interpolation = RigidbodyInterpolation.Interpolate;
        physicsBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        if (!TryGetComponent(out ImpactFeedback _))
        {
            gameObject.AddComponent<ImpactFeedback>();
        }

        if (body == null)
        {
            body = transform.Find("Corpo");
        }

        if (body != null)
        {
            bodyRestPosition = body.localPosition;
        }

        if (alertIcon != null)
        {
            alertIcon.SetActive(false);
        }
    }

    private void Start()
    {
        PrototypeThirdPersonController controller = FindFirstObjectByType<PrototypeThirdPersonController>();
        if (controller != null)
        {
            player = controller.transform;
        }
        else
        {
            Debug.LogWarning($"{name}: jogador não encontrado; o alienígena só vai passear.", this);
        }

        EnterWander();
    }

    private void OnDisable()
    {
        // Ao ser derrotado (ou desativado), some o "!" e o corpo volta ao lugar.
        if (alertIcon != null)
        {
            alertIcon.SetActive(false);
        }

        if (body != null)
        {
            body.localPosition = bodyRestPosition;
            body.localRotation = Quaternion.identity;
        }

        hopTime = -1f;
    }

    private void Update()
    {
        UpdateScareHop();

        if (state == State.Tumbling)
        {
            UpdateTumble();
            return;
        }

        if (!agent.isOnNavMesh)
        {
            if (!warnedNoNavMesh)
            {
                Debug.LogWarning(
                    $"{name} não está sobre um NavMesh. Selecione 'Esquina_Blockout' e clique em Bake no componente NavMeshSurface.",
                    this);
                warnedNoNavMesh = true;
            }

            return;
        }

        float distanceToPlayer = player != null
            ? Vector3.Distance(transform.position, player.position)
            : float.MaxValue;

        switch (state)
        {
            case State.Wandering:
                if (distanceToPlayer < detectionRadius)
                {
                    EnterFlee();
                }
                else
                {
                    UpdateWander();
                }

                break;

            case State.Fleeing:
                if (distanceToPlayer > calmDownDistance)
                {
                    EnterWander();
                }
                else
                {
                    UpdateFlee();
                }

                break;
        }
    }

    // ---------- Passeio ----------

    private void EnterWander()
    {
        state = State.Wandering;
        agent.speed = wanderSpeed;
        idleTimer = Random.Range(idleTimeRange.x, idleTimeRange.y);
        if (alertIcon != null)
        {
            alertIcon.SetActive(false);
        }
    }

    private void UpdateWander()
    {
        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.1f;
        if (!arrived)
        {
            return;
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer > 0f)
        {
            return;
        }

        Vector2 offset = Random.insideUnitCircle * wanderRadius; // só no plano, para não escolher telhados
        Vector3 randomPoint = home + new Vector3(offset.x, 0f, offset.y);
        if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }

        idleTimer = Random.Range(idleTimeRange.x, idleTimeRange.y);
    }

    // ---------- Fuga ----------

    private void EnterFlee()
    {
        state = State.Fleeing;
        GameFeel.AlienScream(transform.position + Vector3.up * 1.8f);
        agent.speed = fleeSpeed;
        repathTimer = 0f;
        hopTime = 0f; // começa o pulinho de susto
        if (alertIcon != null)
        {
            alertIcon.SetActive(true);
        }
    }

    private void UpdateFlee()
    {
        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f || player == null)
        {
            return;
        }

        repathTimer = fleeRepathInterval;

        Vector3 away = transform.position - player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f)
        {
            away = transform.forward;
        }

        away.Normalize();

        // Tenta fugir em linha reta; se houver parede, tenta ângulos cada vez mais abertos.
        foreach (float angle in FleeAngles)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
            Vector3 target = transform.position + direction * fleeDistance;
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }
    }

    // ---------- Telecinese ----------

    public void OnTelekinesisGrab()
    {
        state = State.Tumbling;
        GameFeel.AlienScream(transform.position + Vector3.up * 1.8f);
        isHeld = true;
        settleTimer = 0f;
        agent.enabled = false;
        physicsBody.isKinematic = false;
        hopTime = -1f;
        if (body != null)
        {
            body.localPosition = bodyRestPosition;
        }

        if (alertIcon != null)
        {
            alertIcon.SetActive(true);
        }
    }

    public void OnTelekinesisRelease()
    {
        isHeld = false;
        settleTimer = 0f;
        if (body != null)
        {
            body.localRotation = Quaternion.identity;
        }
    }

    private void UpdateTumble()
    {
        if (transform.position.y < fallOutHeight)
        {
            Debug.Log($"{name} caiu para fora do mapa.", this);
            // Conta como derrotado: o DNA é liberado e a rede de segurança traz os orbes de volta.
            if (TryGetComponent(out AlienHealth health) && !health.IsDead)
            {
                health.TakeDamage(health.CurrentHealth);
            }

            Destroy(gameObject);
            return;
        }

        if (isHeld)
        {
            // Se debate no ar, desesperado.
            if (body != null)
            {
                float t = Time.time;
                body.localRotation = Quaternion.Euler(Mathf.Sin(t * 23f) * 18f, Mathf.Sin(t * 11f) * 25f, Mathf.Cos(t * 19f) * 18f);
            }

            return;
        }

        bool resting = physicsBody.linearVelocity.sqrMagnitude < 0.09f && physicsBody.angularVelocity.sqrMagnitude < 0.25f;
        settleTimer = resting ? settleTimer + Time.deltaTime : 0f;
        if (settleTimer < recoverDelay)
        {
            return;
        }

        // Procura um ponto do NavMesh perto de onde caiu para se levantar.
        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
        {
            settleTimer = 0f; // caiu num lugar sem NavMesh (ex.: telhado); continua caído
            return;
        }

        physicsBody.linearVelocity = Vector3.zero;
        physicsBody.angularVelocity = Vector3.zero;
        physicsBody.isKinematic = true;
        transform.SetPositionAndRotation(hit.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        agent.enabled = true;
        agent.Warp(hit.position);
        EnterFlee(); // levanta assustado e sai correndo
    }

    // ---------- Visual ----------

    private void UpdateScareHop()
    {
        if (body == null || hopTime < 0f)
        {
            return;
        }

        hopTime += Time.deltaTime;
        float t = hopTime / scareHopDuration;
        if (t >= 1f)
        {
            body.localPosition = bodyRestPosition;
            hopTime = -1f;
            return;
        }

        body.localPosition = bodyRestPosition + Vector3.up * (Mathf.Sin(t * Mathf.PI) * scareHopHeight);
    }

    private void OnDrawGizmosSelected()
    {
        // Mostra na janela Scene os raios de detecção (amarelo) e de calma (verde).
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, calmDownDistance);
    }
}
