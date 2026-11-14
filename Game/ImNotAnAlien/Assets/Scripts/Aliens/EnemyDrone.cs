using UnityEngine;

// Drone de olho da polícia (Sci-Fi Kit, Missão 20). Aparece com 4 e 5 estrelas.
// - Voa em volta do jogador, a uns 9 m e 5 m acima, sempre olhando para ele.
// - De tempos em tempos "carrega" (Charging) e solta uma rajada de 3 tiros.
// - A TELECINESE PEGA o drone: enquanto segurado ele fica sem controle; ao ser solto/arremessado
//   ele cai com os motores pifados e explode ao bater em qualquer coisa.
// - Objeto arremessado que acerta o drone também o derruba.
// O SciFiEnemySpawner cria e remove os drones conforme o nível de procurado.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class EnemyDrone : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 30f;
    [SerializeField, Min(1f)] private float orbitRadius = 9f;
    [SerializeField, Min(1f)] private float hoverHeight = 5f;
    [SerializeField, Min(1f)] private float maxSpeed = 10f;
    [SerializeField] private Vector2 attackInterval = new Vector2(2.6f, 3.8f);
    [SerializeField, Min(0f)] private float chargeTime = 0.7f;
    [SerializeField, Min(1)] private int burst = 3;
    [SerializeField, Min(0f)] private float shotDamage = 6f;
    [SerializeField, Min(1f)] private float shotSpeed = 34f;
    [SerializeField, Min(1f)] private float fireRange = 28f;
    [Tooltip("Correção da frente do modelo (graus), se o olho não estiver virado para o jogador.")]
    [SerializeField] private float modelYaw;

    private static TelekinesisPower telekinesis;

    private Rigidbody body;
    private Animator animator;
    private Transform model;
    private float health;
    private float orbitAngle;
    private float attackTimer;
    private float chargeTimer = -1f;
    private int shotsLeft;
    private float shotTimer;
    private bool wasHeld;
    private bool broken;
    private float brokenTime;
    private bool leaving;

    public bool IsAlive => !broken && health > 0f;
    public bool IsLeaving => leaving;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.linearDamping = 1.5f;
        body.angularDamping = 4f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        animator = GetComponentInChildren<Animator>();
        model = animator != null ? animator.transform : null;
        health = maxHealth;
        orbitAngle = Random.Range(0f, 360f);
        attackTimer = Random.Range(attackInterval.x, attackInterval.y);
        if (telekinesis == null)
        {
            telekinesis = FindFirstObjectByType<TelekinesisPower>();
        }
    }

    // Nível de procurado caiu: vai embora voando para cima.
    public void Leave()
    {
        leaving = true;
        Destroy(gameObject, 6f);
    }

    private void FixedUpdate()
    {
        bool held = telekinesis != null && telekinesis.HeldBody == body;
        if (held)
        {
            wasHeld = true;
            return; // a telecinese manda
        }

        if (wasHeld && !broken)
        {
            Break(); // foi solto ou arremessado: motores pifam
        }

        if (broken)
        {
            if (Time.time - brokenTime > 5f)
            {
                Explode();
            }

            return;
        }

        PlayerHealth player = PlayerHealth.Instance;
        if (player == null)
        {
            return;
        }

        Vector3 target;
        if (leaving)
        {
            target = body.position + Vector3.up * 20f;
        }
        else
        {
            orbitAngle += 18f * Time.fixedDeltaTime;
            Vector3 around = Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.forward * orbitRadius;
            target = player.transform.position + around;
            float ground = PlanetTerrain.SurfaceY(target);
            target.y = Mathf.Max(player.transform.position.y, ground) + hoverHeight + Mathf.Sin(Time.time * 1.7f) * 0.4f;
        }

        Vector3 wanted = Vector3.ClampMagnitude((target - body.position) * 1.6f, maxSpeed);
        body.linearVelocity = Vector3.Lerp(body.linearVelocity, wanted, 4f * Time.fixedDeltaTime);

        Vector3 look = player.transform.position + Vector3.up * 1.2f - body.position;
        if (look.sqrMagnitude > 0.01f)
        {
            Quaternion facing = Quaternion.LookRotation(look.normalized, Vector3.up) * Quaternion.Euler(0f, modelYaw, 0f);
            body.MoveRotation(Quaternion.Slerp(body.rotation, facing, 6f * Time.fixedDeltaTime));
        }
    }

    private void Update()
    {
        if (broken || leaving || (telekinesis != null && telekinesis.HeldBody == body))
        {
            return;
        }

        PlayerHealth player = PlayerHealth.Instance;
        if (player == null || player.IsDead)
        {
            return;
        }

        Vector3 aim = player.transform.position + Vector3.up * 1.1f;
        if (shotsLeft > 0)
        {
            shotTimer -= Time.deltaTime;
            if (shotTimer <= 0f)
            {
                Fire(aim);
                shotsLeft--;
                shotTimer = 0.18f;
                if (shotsLeft == 0)
                {
                    Play("Idle");
                    attackTimer = Random.Range(attackInterval.x, attackInterval.y);
                }
            }

            return;
        }

        if (chargeTimer >= 0f)
        {
            chargeTimer -= Time.deltaTime;
            if (chargeTimer < 0f)
            {
                shotsLeft = burst;
                shotTimer = 0f;
                Play("Attack");
            }

            return;
        }

        attackTimer -= Time.deltaTime;
        if (attackTimer > 0f)
        {
            return;
        }

        if (Vector3.Distance(transform.position, aim) > fireRange || !HasLineOfSight(aim, player.transform))
        {
            attackTimer = 0.5f;
            return;
        }

        chargeTimer = chargeTime;
        Play("Charging");
        GameFeel.PoliceCharge(transform.position);
    }

    private void Fire(Vector3 aim)
    {
        Vector3 origin = transform.position + (aim - transform.position).normalized * 0.7f;
        Vector3 direction = Quaternion.Euler(Random.Range(-2f, 2f), Random.Range(-2f, 2f), 0f) * (aim - origin).normalized;
        AlienProjectile.Spawn(AlienPoliceWeapon.SharedProjectileMaterial, origin, direction * shotSpeed,
            shotDamage * WantedSystem.DamageMultiplier, transform, false);
        GameFeel.PoliceShot(origin);
    }

    private bool HasLineOfSight(Vector3 aim, Transform player)
    {
        if (!Physics.Linecast(transform.position, aim, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.collider.transform.IsChildOf(player) || hit.collider.transform.IsChildOf(transform);
    }

    private void OnCollisionEnter(Collision collision)
    {
        float speed = collision.relativeVelocity.magnitude;
        if (broken)
        {
            if (speed > 2.5f)
            {
                Explode();
            }

            return;
        }

        Rigidbody other = collision.rigidbody;
        if (other != null && !other.isKinematic && speed > 6f)
        {
            health -= (speed - 6f) * Mathf.Clamp(other.mass / 30f, 0.3f, 4f) * 2f;
            Play("Hit");
            if (health <= 0f)
            {
                Break();
            }
        }
    }

    // Explosão por perto (ExplosiveVehicle): o drone cai.
    public void OnBlast(float falloff)
    {
        if (!broken && falloff > 0.1f)
        {
            Break();
        }
    }

    private void Break()
    {
        broken = true;
        brokenTime = Time.time;
        body.useGravity = true;
        body.linearDamping = 0.1f;
        body.AddTorque(Random.insideUnitSphere * 4f, ForceMode.VelocityChange);
        Play("Hit");
        GameFeel.CreateSmoke(transform, Vector3.zero);
    }

    private void Explode()
    {
        if (this == null || !enabled)
        {
            return;
        }

        enabled = false;
        GameFeel.Explosion(transform.position, 3f);
        WantedSystem.ReportDestruction(transform.position);
        Destroy(gameObject);
    }

    private void Play(string state)
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.CrossFadeInFixedTime(state, 0.12f);
        }
    }
}
