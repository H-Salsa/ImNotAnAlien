using UnityEngine;
using UnityEngine.AI;

// Trilobite: bicho-robô selvagem (Sci-Fi Kit, Missão 20). Vive na Floresta Chiclete, na Cratera
// Funda e nas Dunas Roxas (o SciFiEnemySpawner cria perto do jogador nesses biomas).
// - Passeia devagar. Viu o jogador (22 m): vira ATIRADOR — fica a 9–15 m, anda de lado e atira
//   com os canhões; se o jogador chega perto, recua. Só morde se ficar encurralado.
// - Usa o AlienHealth: apanha de objetos arremessados, a telecinese pega, dá para devorar quando
//   ferido e solta DNA ao morrer. NÃO conta para a cota do setor e não chama a polícia.
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(AlienHealth))]
public sealed class EnemyTrilobite : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 140f;
    [SerializeField, Min(0)] private int dnaValue = 30;
    [SerializeField, Min(1f)] private float noticeRange = 22f;
    [SerializeField, Min(1f)] private float giveUpRange = 35f;
    [SerializeField, Min(0.5f)] private float biteRange = 3f;
    [SerializeField, Min(0f)] private float biteDamage = 12f;
    [SerializeField, Min(0f)] private float shotDamage = 7f;
    [SerializeField, Min(1f)] private float wanderSpeed = 1.6f;
    [SerializeField, Min(1f)] private float chaseSpeed = 5.5f;
    [SerializeField, Min(1f)] private float keepMin = 9f;
    [SerializeField, Min(1f)] private float keepMax = 15f;
    [SerializeField, Min(1f)] private float shootRange = 22f;
    [SerializeField] private float modelYaw;

    private float strafeSide = 1f;

    private static TelekinesisPower telekinesis;

    private NavMeshAgent agent;
    private AlienHealth health;
    private Rigidbody body;
    private Animator animator;
    private Vector3 home;
    private float thinkTimer;
    private float attackTimer = 1f;
    private float biteTimer = -1f;
    private bool chasing;
    private bool held;
    private float landedTime;
    private string current = "";

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<AlienHealth>();
        health.Configure(maxHealth, dnaValue, 6);
        health.CountsForQuota = false;
        health.ReportsToPolice = false;
        body = GetComponent<Rigidbody>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.mass = 150f;
        body.isKinematic = true;
        animator = GetComponentInChildren<Animator>();
        if (animator != null && Mathf.Abs(modelYaw) > 0.01f)
        {
            animator.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
        }

        if (telekinesis == null)
        {
            telekinesis = FindFirstObjectByType<TelekinesisPower>();
        }
    }

    private void Start()
    {
        home = transform.position;
        if (animator != null)
        {
            StartCoroutine(ModelGrounding.Settle(transform, animator.transform)); // pés no chão
        }
    }

    // Origem flutuante: o mundo inteiro andou, o "ponto de casa" anda junto.
    private void OnEnable() => WorldOrigin.Shifted += OnShifted;

    private void OnDisable() => WorldOrigin.Shifted -= OnShifted;

    private void OnShifted(Vector3 delta) => home -= delta;

    private void Update()
    {
        if (health.IsDead)
        {
            Play("TurnOff");
            enabled = false;
            return;
        }

        // Pego pela telecinese: sem pernas no chão. Solto: espera parar e volta a andar.
        bool nowHeld = telekinesis != null && telekinesis.HeldBody == body;
        if (nowHeld)
        {
            if (!held)
            {
                held = true;
                agent.enabled = false;
                Play("Hit", true);
            }

            return;
        }

        if (held)
        {
            if (body.linearVelocity.magnitude > 0.6f || !NavMesh.SamplePosition(transform.position, out NavMeshHit ground, 1.5f, NavMesh.AllAreas))
            {
                landedTime = Time.time;
                return;
            }

            if (Time.time - landedTime < 0.8f)
            {
                return;
            }

            held = false;
            body.isKinematic = true;
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            agent.enabled = true;
            agent.Warp(ground.position);
            chasing = true;
        }

        if (!agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        PlayerHealth player = PlayerHealth.Instance;
        float distance = player != null ? Vector3.Distance(transform.position, player.transform.position) : float.MaxValue;
        if (player != null && player.IsDead)
        {
            distance = float.MaxValue;
        }

        if (!chasing && distance < noticeRange)
        {
            chasing = true;
            Play("Look", true);
            thinkTimer = 0.6f;
        }
        else if (chasing && distance > giveUpRange)
        {
            chasing = false;
        }

        if (biteTimer >= 0f)
        {
            biteTimer -= Time.deltaTime;
            if (biteTimer < 0f && player != null && distance < biteRange + 0.8f)
            {
                player.TakeDamage(biteDamage, transform.position);
            }

            return;
        }

        thinkTimer -= Time.deltaTime;
        attackTimer -= Time.deltaTime;
        if (chasing && player != null)
        {
            Combat(player.transform, distance);
            return;
        }

        agent.speed = wanderSpeed;
        agent.updateRotation = true;
        if (thinkTimer <= 0f)
        {
            thinkTimer = Random.Range(3f, 7f);
            Vector2 offset = Random.insideUnitCircle * 12f;
            if (NavMesh.SamplePosition(home + new Vector3(offset.x, 0f, offset.y), out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }

        Play(agent.velocity.sqrMagnitude > 0.2f ? "Walk" : "Idle");
    }

    // Atirador: fica a uma distância boa (9–15 m), anda de lado enquanto atira e recua se o
    // jogador chega perto. Só morde se for encurralado (bem colado e sem conseguir recuar).
    private void Combat(Transform player, float distance)
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        Vector3 away = toPlayer.sqrMagnitude > 0.01f ? -toPlayer.normalized : transform.forward;

        if (attackTimer <= 0f && distance < biteRange && agent.velocity.sqrMagnitude < 0.5f)
        {
            attackTimer = 1.6f;
            biteTimer = 0.45f;
            agent.ResetPath();
            Play("Attack", true);
            return;
        }

        if (thinkTimer <= 0f)
        {
            thinkTimer = 0.5f;
            Vector3 goal;
            if (distance < keepMin)
            {
                agent.speed = chaseSpeed; // muito perto: recua correndo, meio de lado
                goal = transform.position + (away + Vector3.Cross(Vector3.up, away) * strafeSide * 0.6f).normalized * 8f;
            }
            else if (distance > keepMax)
            {
                agent.speed = chaseSpeed; // longe: aproxima até ficar no alcance
                goal = player.position + away * (keepMin + keepMax) * 0.5f;
            }
            else
            {
                agent.speed = wanderSpeed * 1.6f; // no alcance: anda de lado (circula o jogador)
                if (Random.value < 0.15f)
                {
                    strafeSide = -strafeSide;
                }

                goal = transform.position + Vector3.Cross(Vector3.up, away) * strafeSide * 4f;
            }

            if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
            }
        }

        // Sempre virado para o jogador (os canhões ficam nos lados da frente).
        agent.updateRotation = false;
        if (toPlayer.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(toPlayer), 6f * Time.deltaTime);
        }

        if (attackTimer <= 0f && distance <= shootRange && HasLineOfSight(player))
        {
            attackTimer = Random.Range(1.4f, 2.4f);
            Shoot(player);
            Play("AttackAuto", true);
            return;
        }

        if (attackTimer > 0.9f)
        {
            return; // deixa a animação de tiro terminar
        }

        Play(agent.velocity.sqrMagnitude > 0.3f ? (agent.speed >= chaseSpeed ? "Run" : "Walk") : "Idle");
    }

    private bool HasLineOfSight(Transform player)
    {
        Vector3 from = transform.position + Vector3.up * 1.2f;
        Vector3 to = player.position + Vector3.up * 1f;
        if (!Physics.Linecast(from, to, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.collider.transform.IsChildOf(player) || hit.collider.transform.IsChildOf(transform);
    }

    private void Shoot(Transform player)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f + transform.right * side * 0.9f + transform.forward * 0.8f;
            Vector3 aim = player.position + Vector3.up * 1f;
            AlienProjectile.Spawn(AlienPoliceWeapon.SharedProjectileMaterial, origin, (aim - origin).normalized * 26f, shotDamage, transform, false);
        }

        GameFeel.PoliceShot(transform.position);
    }

    private void Play(string state, bool force = false)
    {
        if (animator == null || animator.runtimeAnimatorController == null || (!force && current == state))
        {
            return;
        }

        current = state;
        animator.CrossFadeInFixedTime(state, 0.15f);
    }
}
