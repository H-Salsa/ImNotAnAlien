using UnityEngine;
using UnityEngine.AI;

// Robô de 4 patas do exército (Sci-Fi Kit, Missão 20). Um por vez, só com 5 estrelas.
// - Anda atrás do jogador; de longe "carrega" (Charge) e solta uma rajada de 5 tiros;
//   de perto dá uma patada (Attack) que machuca e empurra.
// - MUITO resistente: objetos leves não fazem nada. Só cai com coisas pesadas arremessadas
//   (carros, contêineres: 300 kg ou mais) e com explosões (carros, barris explosivos).
//   O mesmo objeto só machuca de novo depois de 1,5 s (um carro não "conta" várias vezes).
// - Com metade da vida entra em FÚRIA: corre mais, atira mais rápido e a patada vira onda de choque.
// - Barra de vida no alto da tela quando está perto (é um chefe).
// - Destruído: desliga (TurnOff), explode e fica a carcaça escura por um tempo.
[DisallowMultipleComponent]
[RequireComponent(typeof(NavMeshAgent))]
public sealed class EnemyQuadShell : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHealth = 1500f;
    [SerializeField, Min(0f)] private float minImpactMass = 300f;
    [SerializeField, Min(0f)] private float minImpactSpeed = 10f;
    [Tooltip("Dano = (velocidade - mínima) × peso / este valor. Carro (900 kg) a 28 m/s = ~110.")]
    [SerializeField, Min(1f)] private float impactDivisor = 150f;
    [SerializeField, Min(0f)] private float blastDamage = 160f;
    [SerializeField, Min(0f)] private float hitCooldown = 1.5f;
    [SerializeField, Min(1f)] private float walkSpeed = 3f;
    [SerializeField, Min(1f)] private float runSpeed = 6f;
    [SerializeField, Min(1f)] private float shootRange = 26f;
    [SerializeField, Min(1f)] private float stompRange = 4.5f;
    [SerializeField, Min(0f)] private float shotDamage = 12f;
    [SerializeField, Min(0f)] private float stompDamage = 28f;
    [SerializeField] private float modelYaw;

    private NavMeshAgent agent;
    private Animator animator;
    private float health;
    private float actionTimer = 2f;
    private float chargeTimer = -1f;
    private int shotsLeft;
    private float shotTimer;
    private float stompTimer = -1f;
    private float repathTimer;
    private string current = "";
    private bool dead;
    private bool leaving;
    private bool enraged;
    private readonly System.Collections.Generic.Dictionary<Rigidbody, float> lastHit =
        new System.Collections.Generic.Dictionary<Rigidbody, float>();
    private static GUIStyle barStyle;

    public bool IsAlive => !dead;
    public float HealthFraction => health / maxHealth;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponentInChildren<Animator>();
        health = maxHealth;
        if (animator != null && Mathf.Abs(modelYaw) > 0.01f)
        {
            animator.transform.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
        }
    }

    private void Start()
    {
        if (animator != null)
        {
            StartCoroutine(ModelGrounding.Settle(transform, animator.transform)); // patas no chão
        }
    }

    public void Leave()
    {
        leaving = true;
        Destroy(gameObject, 1f);
    }

    private void Update()
    {
        if (dead || leaving)
        {
            return;
        }

        PlayerHealth player = PlayerHealth.Instance;
        if (player == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return;
        }

        Vector3 target = player.transform.position;
        float distance = Vector3.Distance(transform.position, target);

        if (shotsLeft > 0 || chargeTimer >= 0f || stompTimer >= 0f)
        {
            agent.isStopped = true;
            Face(target);
            TickAttack(player, distance);
            return;
        }

        agent.isStopped = false;
        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = 0.5f;
            agent.SetDestination(target);
        }

        bool far = distance > shootRange * 0.8f;
        agent.speed = (far || enraged ? runSpeed : walkSpeed) * (enraged ? 1.3f : 1f);
        Play(agent.velocity.sqrMagnitude > 0.2f ? (far ? "Run" : "Walk") : "Idle");

        actionTimer -= Time.deltaTime;
        if (actionTimer > 0f)
        {
            return;
        }

        if (distance < stompRange)
        {
            stompTimer = 0.55f;
            Play("Attack");
        }
        else if (distance < shootRange)
        {
            chargeTimer = enraged ? 0.6f : 1f;
            Play("Charge");
            GameFeel.PoliceCharge(transform.position + Vector3.up * 2f);
        }
        else
        {
            actionTimer = 0.5f;
        }
    }

    private void TickAttack(PlayerHealth player, float distance)
    {
        if (stompTimer >= 0f)
        {
            stompTimer -= Time.deltaTime;
            if (stompTimer < 0f)
            {
                float reach = enraged ? stompRange * 2f : stompRange + 0.8f;
                if (distance < reach)
                {
                    player.TakeDamage(stompDamage * WantedSystem.DamageMultiplier, transform.position);
                }

                // A patada empurra tudo em volta (e, na fúria, vai bem mais longe).
                foreach (Collider hit in Physics.OverlapSphere(transform.position, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    Rigidbody other = hit.attachedRigidbody;
                    if (other != null && !other.isKinematic && other != GetComponent<Rigidbody>())
                    {
                        other.AddExplosionForce(900f, transform.position, reach, 1f, ForceMode.Impulse);
                    }
                }

                GameFeel.Shockwave(transform.position, reach, new Color(1f, 0.6f, 0.3f));
                actionTimer = enraged ? Random.Range(0.6f, 1.1f) : Random.Range(1.2f, 2f);
            }

            return;
        }

        if (chargeTimer >= 0f)
        {
            chargeTimer -= Time.deltaTime;
            if (chargeTimer < 0f)
            {
                shotsLeft = enraged ? 9 : 6;
                shotTimer = 0f;
                Play("Attack");
            }

            return;
        }

        shotTimer -= Time.deltaTime;
        if (shotTimer > 0f)
        {
            return;
        }

        Vector3 origin = transform.position + Vector3.up * 2.2f + transform.forward * 1.5f;
        Vector3 aim = player.transform.position + Vector3.up * 1f;
        Vector3 direction = Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(-3f, 3f), 0f) * (aim - origin).normalized;
        AlienProjectile.Spawn(AlienPoliceWeapon.SharedProjectileMaterial, origin, direction * 30f,
            shotDamage * WantedSystem.DamageMultiplier, transform, false);
        GameFeel.PoliceShot(origin);
        shotsLeft--;
        shotTimer = enraged ? 0.14f : 0.2f;
        if (shotsLeft == 0)
        {
            actionTimer = enraged ? Random.Range(1.2f, 2f) : Random.Range(2.2f, 3.4f);
        }
    }

    private void Face(Vector3 target)
    {
        Vector3 flat = target - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 4f * Time.deltaTime);
        }
    }

    // Só coisa pesada machuca.
    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody other = collision.rigidbody;
        float speed = collision.relativeVelocity.magnitude;
        if (dead || other == null || other.isKinematic || other.mass < minImpactMass || speed < minImpactSpeed)
        {
            if (other != null && !other.isKinematic && speed > 6f)
            {
                GameFeel.Impact(collision.GetContact(0).point, speed, new Color(0.7f, 0.75f, 0.8f)); // "tim": nem arranhou
            }

            return;
        }

        // O mesmo objeto (o carro quica, encosta de novo...) só machuca outra vez depois de um tempo.
        if (lastHit.TryGetValue(other, out float when) && Time.time - when < hitCooldown)
        {
            return;
        }

        lastHit[other] = Time.time;
        TakeDamage((speed - minImpactSpeed) * other.mass / impactDivisor);
    }

    // Chamado pela explosão de carros e barris (ExplosiveVehicle, por SendMessage).
    public void OnBlast(float falloff)
    {
        TakeDamage(blastDamage * falloff);
    }

    public void TakeDamage(float amount)
    {
        if (dead || amount <= 0f)
        {
            return;
        }

        health -= amount;
        Play("Hit", true);
        if (!enraged && health <= maxHealth * 0.5f)
        {
            enraged = true;
            GameFeel.Shockwave(transform.position, 8f, new Color(1f, 0.25f, 0.15f));
            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                foreach (Material material in part.materials)
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", new Color(1.2f, 0.15f, 0.05f)); // olhos e luzes ficam vermelhos
                }
            }
        }

        if (health <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        dead = true;
        agent.isStopped = true;
        agent.enabled = false;
        Play("TurnOff", true);
        GameFeel.Explosion(transform.position + Vector3.up * 1.5f, 6f);
        WantedSystem.ReportDestruction(transform.position);
        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            foreach (Material material in part.materials)
            {
                material.color = Color.Lerp(material.color, new Color(0.1f, 0.09f, 0.09f), 0.75f);
            }
        }

        GameFeel.CreateSmoke(transform, Vector3.up * 2f);
        Destroy(gameObject, 25f);
    }

    // Barra de vida de chefe (só quando está perto).
    private void OnGUI()
    {
        PlayerHealth player = PlayerHealth.Instance;
        if (dead || leaving || player == null || Vector3.Distance(player.transform.position, transform.position) > 45f)
        {
            return;
        }

        if (barStyle == null)
        {
            barStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 18 };
            barStyle.normal.textColor = Color.white;
        }

        float scale = Screen.height / 1080f;
        float width = 620f * scale;
        float height = 18f * scale;
        Rect back = new Rect((Screen.width - width) / 2f, 70f * scale, width, height);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(back.x - 3f, back.y - 3f, back.width + 6f, back.height + 6f), Texture2D.whiteTexture);
        GUI.color = enraged ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.65f, 0.2f);
        GUI.DrawTexture(new Rect(back.x, back.y, back.width * Mathf.Clamp01(health / maxHealth), back.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        barStyle.fontSize = Mathf.RoundToInt(18f * scale);
        GUI.Label(new Rect(back.x, back.y - 30f * scale, back.width, 26f * scale), enraged ? "ROBÔ DO EXÉRCITO — FÚRIA" : "ROBÔ DO EXÉRCITO", barStyle);
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
