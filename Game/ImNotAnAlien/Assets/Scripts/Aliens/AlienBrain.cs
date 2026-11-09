using UnityEngine;
using UnityEngine.AI;

// Comportamento dos alienígenas.
// - Passeio: andam devagar até pontos aleatórios perto de "casa" e param um pouco.
// - Ao ver o jogador (susto com pulinho + "!"):
//     Civil    -> foge para longe.
//     Policial -> depende do nível de procurado (Missão 19, D27):
//                 0 estrelas: só observa (a suspeita é do WantedSystem);
//                 1 estrela: corre até você, bate com cassetete/socos e tenta algemar (PoliceArrest);
//                 2+ estrelas: mantém a distância de tiro e atira (AlienPoliceWeapon).
// - Quando o jogador fica longe o bastante, se acalmam e voltam a passear.
// - Telecinese: enquanto levantados ou voando, viram corpos físicos. Quando param
//   no chão (e ainda estão vivos), se levantam e voltam a reagir ao jogador.
// Precisa de um NavMesh "assado" (Bake) na cena para conseguir andar.
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class AlienBrain : MonoBehaviour
{
    public enum Role
    {
        Civil,
        Policial,
    }

    private enum State
    {
        Wandering,
        Fleeing,
        Attacking,
        Hunting,
        Tumbling,
        Devoured,
    }

    [Header("Papel")]
    [SerializeField] private Role role = Role.Civil;

    [Header("Passeio")]
    [Tooltip("Distância máxima, a partir do ponto inicial, dos pontos aleatórios de passeio.")]
    [SerializeField, Min(1f)] private float wanderRadius = 12f;
    [SerializeField, Min(0.1f)] private float wanderSpeed = 1.8f;
    [Tooltip("Tempo parado (mínimo e máximo, em segundos) entre um passeio e outro.")]
    [SerializeField] private Vector2 idleTimeRange = new Vector2(1.5f, 4f);

    [Header("Percepção")]
    [Tooltip("Se o jogador chegar mais perto que isso, o alienígena reage (foge ou ataca).")]
    [SerializeField, Min(0.5f)] private float detectionRadius = 9f;
    [Tooltip("Quando o jogador estiver mais longe que isso, o alienígena se acalma.")]
    [SerializeField, Min(1f)] private float calmDownDistance = 20f;

    [Header("Fuga (civil)")]
    [SerializeField, Min(0.1f)] private float fleeSpeed = 6.5f;
    [Tooltip("Até onde ele tenta correr a cada nova escolha de rota.")]
    [SerializeField, Min(1f)] private float fleeDistance = 10f;
    [SerializeField, Min(0.1f)] private float fleeRepathInterval = 0.4f;

    [Header("Ataque (policial)")]
    [SerializeField, Min(0.1f)] private float attackMoveSpeed = 4.5f;
    [Tooltip("Distância mínima e máxima que o policial tenta manter do jogador.")]
    [SerializeField] private Vector2 attackDistanceRange = new Vector2(7f, 13f);
    [SerializeField, Min(0.1f)] private float attackRepathInterval = 0.5f;
    [SerializeField, Min(1f)] private float turnSpeed = 8f;

    [Header("Corpo a corpo (policial, nível 1)")]
    [SerializeField, Min(0f)] private float meleeDamage = 6f;
    [SerializeField, Min(0.1f)] private float meleeInterval = 1.1f;
    [SerializeField, Min(0.5f)] private float meleeReach = 1.7f;
    [Tooltip("Chance de o policial ser 'de cassetete' (sorteado ao nascer); o resto é 'de soco'.")]
    [SerializeField, Range(0f, 1f)] private float batonChance = 0.5f;
    [Tooltip("Segundos do início do golpe até o impacto (o dano sai aqui).")]
    [SerializeField, Min(0f)] private float punchImpactDelay = 0.22f;
    [SerializeField, Min(0f)] private float batonImpactDelay = 0.38f;

    [Header("Reação (visual)")]
    [Tooltip("Parte visual que dá o pulinho de susto.")]
    [SerializeField] private Transform body;
    [Tooltip("Objeto '!' mostrado enquanto foge ou ataca.")]
    [SerializeField] private GameObject alertIcon;
    [SerializeField, Min(0f)] private float scareHopHeight = 0.9f;
    [SerializeField, Min(0.05f)] private float scareHopDuration = 0.35f;

    [Header("Telecinese")]
    [Tooltip("Tempo parado no chão, depois de arremessado, até se levantar.")]
    [SerializeField, Min(0.1f)] private float recoverDelay = 1.2f;
    [Tooltip("Se cair abaixo desta altura (fora do mapa), o alienígena é removido.")]
    [SerializeField] private float fallOutHeight = -30f;

    private static readonly float[] FleeAngles = { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 130f, -130f };

    private NavMeshAgent agent;
    private Rigidbody physicsBody;
    private AlienPoliceWeapon weapon;
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

    // Visão do policial (com linha de visão), atualizada algumas vezes por segundo.
    private bool seesPlayer;
    private float sightTimer;
    private float lostSightTime;
    private float searchTimer;
    private float meleeTimer;
    private bool meleeMode;
    private float pendingHit = -1f;

    private static int WantedStars => WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 0;

    public Role AlienRole => role;
    public Transform Body => body;

    // Verdadeiro enquanto a telecinese está segurando este alienígena.
    public bool IsHeld => isHeld && enabled;

    // Só passeando, sem reagir ao jogador (pode sumir com segurança quando longe).
    public bool IsCalm => enabled && state == State.Wandering;

    // Usados pelas animações do modelo 3D (AlienAnimator).
    public bool IsTumbling => state == State.Tumbling;
    public bool IsAttacking => enabled && state == State.Attacking && !meleeMode; // mirando a arma
    public bool IsMelee => enabled && state == State.Attacking && meleeMode;

    // Estilo de briga, sorteado por policial: cassetete ou soco (AlienAnimator toca a animação).
    public bool UsesBaton { get; private set; }

    // Sobe a cada golpe iniciado; o AlienAnimator dispara a animação quando muda.
    public int SwingCount { get; private set; }
    public bool IsFleeing => enabled && state == State.Fleeing;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        weapon = GetComponent<AlienPoliceWeapon>();
        home = transform.position;

        // Corpo físico usado pela telecinese. Fica "cinemático" (sem física) enquanto anda.
        if (!TryGetComponent(out physicsBody))
        {
            physicsBody = gameObject.AddComponent<Rigidbody>();
        }

        physicsBody.isKinematic = true;
        physicsBody.mass = 60f;
        // Interpolação só quando a física move o corpo (voando). Andando pelo NavMesh (cinemático),
        // a interpolação puxava o alien de volta e ele andava a menos da metade da velocidade.
        physicsBody.interpolation = RigidbodyInterpolation.None;
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

    private void OnEnable()
    {
        WorldOrigin.Shifted -= OnOriginShifted; // (evita assinar duas vezes)
        WorldOrigin.Shifted += OnOriginShifted;
    }

    private void OnDestroy()
    {
        WorldOrigin.Shifted -= OnOriginShifted;
    }

    // O mundo recentralizou (Missão 16): o "bairro" onde ele passeia anda junto.
    // (O destino do NavMeshAgent é ajustado pelo WorldOrigin.)
    private void OnOriginShifted(Vector3 delta)
    {
        home -= delta;
    }

    private void Start()
    {
        UsesBaton = role == Role.Policial && Random.value < batonChance;

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

        RigidbodyInterpolation wanted = physicsBody != null && !physicsBody.isKinematic
            ? RigidbodyInterpolation.Interpolate
            : RigidbodyInterpolation.None;
        if (physicsBody != null && physicsBody.interpolation != wanted)
        {
            physicsBody.interpolation = wanted;
        }

        if (state == State.Devoured)
        {
            return;
        }

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

        float distanceToPlayer = PlayerIsAvailable()
            ? Vector3.Distance(transform.position, player.position)
            : float.MaxValue;

        switch (state)
        {
            case State.Wandering:
                if (role == Role.Policial && WantedStars == 0)
                {
                    // Sem estrela: o policial só fica de olho em você quando chega perto.
                    if (distanceToPlayer < 7f)
                    {
                        FacePlayer();
                        if (agent.hasPath)
                        {
                            agent.ResetPath();
                        }
                    }
                    else
                    {
                        UpdateWander();
                    }
                }
                else if (distanceToPlayer < detectionRadius)
                {
                    EnterAlert();
                }
                else if (PoliceShouldHunt())
                {
                    EnterHunt();
                }
                else
                {
                    UpdateWander();
                }

                break;

            case State.Hunting:
                UpdateSight(distanceToPlayer);
                if (!PoliceShouldHunt())
                {
                    EnterWander();
                }
                else if (seesPlayer && distanceToPlayer < detectionRadius)
                {
                    EnterAttack();
                }
                else
                {
                    UpdateHunt();
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

            case State.Attacking:
                if (role == Role.Policial && WantedStars == 0)
                {
                    EnterWander(); // o procurado acabou
                    break;
                }

                UpdateSight(distanceToPlayer);
                lostSightTime = seesPlayer ? 0f : lostSightTime + Time.deltaTime;

                // Procurado e te perdeu de vista por 4 s: volta a procurar pelo último ponto conhecido.
                if (PoliceShouldHunt() && lostSightTime > 4f)
                {
                    EnterHunt();
                }
                else if (distanceToPlayer > calmDownDistance)
                {
                    if (PoliceShouldHunt())
                    {
                        EnterHunt(); // procurado: não desiste, vai atrás
                    }
                    else
                    {
                        EnterWander();
                    }
                }
                else
                {
                    UpdateAttack(distanceToPlayer);
                }

                break;
        }
    }

    // O jogador existe e está vivo? (policiais param de atirar num jogador derrotado)
    private bool PlayerIsAvailable()
    {
        if (player == null)
        {
            return false;
        }

        PlayerHealth health = PlayerHealth.Instance;
        return health == null || !health.IsDead;
    }

    // Reação ao ver o jogador, conforme o papel.
    private void EnterAlert()
    {
        if (role == Role.Policial)
        {
            if (WantedStars == 0)
            {
                EnterWander();
            }
            else
            {
                EnterAttack();
            }
        }
        else
        {
            EnterFlee();
        }
    }

    private void StartScare()
    {
        GameFeel.AlienScream(transform.position + Vector3.up * 1.8f);
        hopTime = 0f; // começa o pulinho de susto
        if (alertIcon != null)
        {
            alertIcon.SetActive(true);
        }
    }

    // ---------- Passeio ----------

    private void EnterWander()
    {
        state = State.Wandering;
        agent.speed = wanderSpeed;
        agent.autoBraking = true;
        agent.updateRotation = true;
        idleTimer = Random.Range(idleTimeRange.x, idleTimeRange.y);
        if (alertIcon != null)
        {
            alertIcon.SetActive(false);
        }

        if (weapon != null)
        {
            weapon.CancelCharge();
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

    // ---------- Fuga (civil) ----------

    private void EnterFlee()
    {
        state = State.Fleeing;
        agent.speed = fleeSpeed;
        agent.acceleration = Mathf.Max(agent.acceleration, 20f);
        agent.autoBraking = false; // não freia perto do destino (o destino é trocado o tempo todo)
        lastFleeDirection = Vector3.zero;
        agent.updateRotation = true;
        repathTimer = 0f;
        StartScare();
    }

    private Vector3 lastFleeDirection;

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

        // Escolhe a direção com caminho LIVRE em linha reta (sem atravessar casas), preferindo
        // "para longe do jogador". Antes, um ponto atrás de uma casa fazia o alien dar a volta
        // (às vezes na direção do jogador) e ele era alcançado fácil.
        Vector3 origin = transform.position;
        float bestScore = float.MinValue;
        Vector3 bestTarget = Vector3.zero;
        bool found = false;
        foreach (float angle in FleeAngles)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * away;
            float lookAhead = Mathf.Max(fleeDistance, 16f);
            Vector3 wanted = origin + direction * lookAhead;
            float clear = lookAhead;
            Vector3 end = wanted;
            if (NavMesh.Raycast(origin, wanted, out NavMeshHit wall, NavMesh.AllAreas))
            {
                clear = wall.distance;
                end = wall.position;
            }

            float awayness = Vector3.Dot(direction, away);
            if (clear < 2f || awayness < -0.1f)
            {
                continue;
            }

            // Vale o quanto ele se AFASTA de verdade (distância livre x quanto aponta para longe),
            // com um bônus para manter a direção anterior (não fica ziguezagueando).
            float keep = lastFleeDirection == Vector3.zero ? 0f : Vector3.Dot(direction, lastFleeDirection);
            float score = clear * (awayness + 0.35f * keep);
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = end;
                found = true;
            }
        }

        if (found)
        {
            Vector3 chosen = bestTarget - origin;
            chosen.y = 0f;
            lastFleeDirection = chosen.normalized;
            agent.SetDestination(bestTarget);
            return;
        }

        // Encurralado: tenta o caminho mais longe possível, mesmo dando a volta.
        if (NavMesh.SamplePosition(origin + away * fleeDistance, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    // ---------- Ataque (policial) ----------

    private void EnterAttack()
    {
        state = State.Attacking;
        meleeMode = WantedStars == 1;
        meleeTimer = 0.4f;
        pendingHit = -1f;
        agent.speed = meleeMode ? attackMoveSpeed * 1.25f : attackMoveSpeed;
        agent.updateRotation = false; // ele mesmo gira para encarar o jogador
        repathTimer = 0f;
        StartScare();

        if (weapon != null)
        {
            weapon.ResetCooldown();
        }
    }

    private void FacePlayer()
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(toPlayer, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
    }

    // Nível 1: corre até você, tenta algemar; se não der, cassetete e socos.
    private void UpdateMelee(float distanceToPlayer)
    {
        if (weapon != null)
        {
            weapon.CancelCharge();
        }

        FacePlayer();
        PoliceArrest arrest = PoliceArrest.Instance;
        bool cuffed = arrest != null && arrest.IsCuffed;
        bool iAmHolding = cuffed && arrest.Officer == this;

        if (iAmHolding)
        {
            if (agent.hasPath)
            {
                agent.ResetPath();
            }

            return;
        }

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = 0.3f;
            Vector3 target = cuffed
                ? player.position - (player.position - transform.position).normalized * 2.5f // espera em volta
                : player.position;
            if (NavMesh.SamplePosition(target, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }

        // Golpe em andamento: o dano sai no impacto, se você ainda estiver ao alcance.
        if (pendingHit >= 0f)
        {
            pendingHit -= Time.deltaTime;
            if (pendingHit < 0f && !cuffed && distanceToPlayer <= meleeReach + 0.5f)
            {
                // A surra é para prender, não para matar: não passa de 10% da vida.
                PlayerHealth health = PlayerHealth.Instance;
                if (health != null)
                {
                    float damage = Mathf.Min(meleeDamage, health.CurrentHealth - health.MaxHealth * 0.1f);
                    if (damage > 0f)
                    {
                        health.TakeDamage(damage, transform.position);
                    }
                }
            }
        }

        meleeTimer -= Time.deltaTime;
        if (cuffed || distanceToPlayer > meleeReach || meleeTimer > 0f)
        {
            return;
        }

        meleeTimer = meleeInterval;
        if (arrest != null && arrest.TryCuff(this))
        {
            return;
        }

        // Golpe de cassetete / soco: começa a animação; o dano sai no impacto.
        SwingCount++;
        pendingHit = UsesBaton ? batonImpactDelay : punchImpactDelay;
    }

    private void UpdateAttack(float distanceToPlayer)
    {
        meleeMode = WantedStars == 1;
        if (meleeMode)
        {
            UpdateMelee(distanceToPlayer);
            return;
        }

        // Sempre de frente para o jogador.
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(toPlayer, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        // Mantém a distância de tiro: aproxima se longe, recua se perto demais.
        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = attackRepathInterval;
            float wanted = Mathf.Clamp(distanceToPlayer, attackDistanceRange.x, attackDistanceRange.y);
            if (!Mathf.Approximately(wanted, distanceToPlayer))
            {
                Vector3 target = player.position - toPlayer.normalized * wanted;
                if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    agent.SetDestination(hit.position);
                }
            }
            else if (agent.hasPath)
            {
                agent.ResetPath();
            }
        }

        if (weapon != null)
        {
            weapon.Tick(player);
        }
    }

    // ---------- Caçada (policial com o jogador procurado) ----------

    private bool PoliceShouldHunt()
    {
        return role == Role.Policial && PlayerIsAvailable() && WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
    }

    private void EnterHunt()
    {
        state = State.Hunting;
        searchTimer = 0f;
        lostSightTime = 0f;
        agent.speed = attackMoveSpeed * 1.2f;
        agent.updateRotation = true;
        repathTimer = 0f;
        if (alertIcon != null)
        {
            alertIcon.SetActive(true);
        }
    }

    // Vai até onde o jogador foi visto por último; chegando lá, procura em volta.
    // Só segue o jogador "de verdade" se estiver vendo ele.
    private void UpdateHunt()
    {
        repathTimer -= Time.deltaTime;
        if (repathTimer > 0f)
        {
            return;
        }

        repathTimer = 1f;
        Vector3 lastKnown = WantedSystem.Instance.LastKnownPosition;
        Vector3 target = seesPlayer ? player.position : lastKnown;

        bool arrived = !seesPlayer && (transform.position - lastKnown).sqrMagnitude < 4f * 4f;
        if (arrived)
        {
            // Procurando: anda para pontos aleatórios perto do último lugar conhecido.
            searchTimer -= 1f;
            if (searchTimer > 0f && agent.hasPath)
            {
                return;
            }

            searchTimer = Random.Range(3f, 5f);
            Vector2 offset = Random.insideUnitCircle * 14f;
            target = lastKnown + new Vector3(offset.x, 0f, offset.y);
        }

        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    // O policial vê o jogador? (perto o bastante e sem paredes no caminho)
    private void UpdateSight(float distanceToPlayer)
    {
        sightTimer -= Time.deltaTime;
        if (sightTimer > 0f)
        {
            return;
        }

        sightTimer = 0.25f;
        seesPlayer = false;
        if (player == null || distanceToPlayer > calmDownDistance)
        {
            return;
        }

        if (distanceToPlayer < 4f)
        {
            seesPlayer = true; // colado: percebe mesmo sem ver
            return;
        }

        Vector3 eyes = transform.position + Vector3.up * 1.8f;
        Vector3 target = player.position + Vector3.up * 0.5f;
        Vector3 direction = target - eyes;
        float distance = direction.magnitude;
        RaycastHit[] hits = Physics.RaycastAll(eyes, direction / distance, distance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            Transform hitTransform = hit.collider.transform;
            if (hitTransform.IsChildOf(transform) || hitTransform.IsChildOf(player) || hit.collider.attachedRigidbody != null)
            {
                continue; // ele mesmo, o jogador e objetos soltos não bloqueiam
            }

            return; // parede, casa, árvore...
        }

        seesPlayer = true;
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

        if (weapon != null)
        {
            weapon.CancelCharge();
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

    // Chamado pelo Devorar: para tudo; o DevourAbility cuida da animação.
    public void OnDevoured()
    {
        state = State.Devoured;
        isHeld = false;
        if (agent.enabled)
        {
            agent.enabled = false;
        }

        physicsBody.isKinematic = true;
        if (weapon != null)
        {
            weapon.CancelCharge();
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
            // Flutuando: balança devagar, como quem perdeu o chão (braços e pernas vêm da animação).
            if (body != null)
            {
                float t = Time.time;
                body.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.7f) * 7f, Mathf.Sin(t * 1.1f) * 12f, Mathf.Cos(t * 1.4f) * 6f);
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
        EnterAlert(); // levanta e reage: civil foge, policial volta a atacar
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
