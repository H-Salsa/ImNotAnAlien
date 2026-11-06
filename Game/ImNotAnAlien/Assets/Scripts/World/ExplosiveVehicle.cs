using System.Collections.Generic;
using UnityEngine;

// Carro que pode pegar fogo e explodir (tem gasolina!).
// - Perde vida com batidas fortes (arremessado contra algo, ou caindo de muito alto).
//   Esbarrões enquanto a telecinese segura o carro não contam.
// - Danificado: solta fumaça. Sem vida: PEGA FOGO e explode alguns segundos depois
//   (ou na hora, se levar outra pancada forte enquanto queima).
// - Explosão: empurra tudo em volta, fere alienígenas e o jogador, danifica casas
//   e outros carros (reação em cadeia). Sobra uma carcaça queimada.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class ExplosiveVehicle : MonoBehaviour
{
    [Header("Vida")]
    [Tooltip("Com 60, um arremesso forte (~28 m/s) já faz o carro pegar fogo.")]
    [SerializeField, Min(1f)] private float maxHealth = 60f;
    [Tooltip("Batidas mais lentas que isso (m/s) não danificam o carro.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 12f;
    [SerializeField, Min(0f)] private float impactDamagePerSpeed = 4f;
    [Tooltip("Segundos pegando fogo até explodir.")]
    [SerializeField, Min(0.1f)] private float burnTime = 2.5f;

    [Header("Explosão")]
    [SerializeField, Min(1f)] private float radius = 8f;
    [Tooltip("Impulso da explosão. Objetos pesados voam menos.")]
    [SerializeField, Min(0f)] private float power = 1600f;
    [SerializeField, Min(0f)] private float alienDamage = 120f;
    [SerializeField, Min(0f)] private float playerDamage = 40f;
    [SerializeField, Min(0f)] private float structureDamage = 300f;
    [SerializeField, Min(0f)] private float vehicleDamage = 60f;

    private static readonly Color Charcoal = new Color(0.1f, 0.09f, 0.09f, 1f);
    private static TelekinesisPower telekinesis;

    private Rigidbody body;
    private float health;
    private bool burning;
    private bool exploded;
    private float burnTimer;
    private ParticleSystem smoke;
    private ParticleSystem fire;

    public bool IsExploded => exploded;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        health = maxHealth;
        if (telekinesis == null)
        {
            telekinesis = FindFirstObjectByType<TelekinesisPower>();
        }
    }

    private void Update()
    {
        if (!burning || exploded)
        {
            return;
        }

        burnTimer -= Time.deltaTime;
        if (burnTimer <= 0f)
        {
            Explode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (exploded)
        {
            return;
        }

        float speed = collision.relativeVelocity.magnitude;
        if (speed <= minImpactSpeed)
        {
            return;
        }

        if (body.isKinematic)
        {
            // Carro do trânsito atingido por algo arremessado: o motorista sai voando.
            if (!TryGetComponent(out TrafficCar traffic))
            {
                return;
            }

            traffic.Abandon();
        }
        else if (telekinesis != null && telekinesis.HeldBody == body)
        {
            return; // sendo segurado: esbarrões no caminho não contam
        }

        TakeDamage((speed - minImpactSpeed) * impactDamagePerSpeed);
    }

    public void TakeDamage(float amount)
    {
        if (exploded || amount <= 0f)
        {
            return;
        }

        if (TryGetComponent(out TrafficCar traffic) && !traffic.IsAbandoned)
        {
            traffic.Abandon();
        }

        health -= amount;

        if (health <= maxHealth * 0.5f && smoke == null)
        {
            smoke = GameFeel.CreateSmoke(transform, Vector3.up * 1.3f);
        }

        if (health > 0f)
        {
            return;
        }

        if (burning)
        {
            if (amount >= 15f)
            {
                Explode(); // pancada forte enquanto queima: explode na hora
            }

            return;
        }

        burning = true;
        burnTimer = burnTime;
        fire = GameFeel.CreateFire(transform, Vector3.up * 1.2f);
    }

    [ContextMenu("Teste: explodir agora")]
    private void DebugExplode()
    {
        if (Application.isPlaying)
        {
            Explode();
        }
    }

    // ---------- Explosão ----------

    private void Explode()
    {
        if (exploded)
        {
            return;
        }

        exploded = true;
        burning = false;
        Vector3 center = body.worldCenterOfMass;

        GameFeel.Explosion(center, radius);
        ApplyBlast(center);
        WantedSystem.ReportDestruction(center);
        MakeWreck();

        // O próprio carro dá um pulo com a explosão.
        if (!body.isKinematic)
        {
            body.AddForce(Vector3.up * 7f + Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * 3f, ForceMode.VelocityChange);
        }
    }

    private void ApplyBlast(Vector3 center)
    {
        HashSet<Rigidbody> bodies = new HashSet<Rigidbody>();
        HashSet<DestructibleStructure> structures = new HashSet<DestructibleStructure>();
        bool playerHit = false;

        foreach (Collider hit in Physics.OverlapSphere(center, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform))
            {
                continue;
            }

            Vector3 closest = hit.ClosestPoint(center);
            float falloff = Mathf.Clamp01(1f - Vector3.Distance(center, closest) / radius);

            DestructibleStructure structure = hit.GetComponentInParent<DestructibleStructure>();
            if (structure != null && structures.Add(structure))
            {
                structure.TakeDamage(structureDamage * falloff, closest, closest - center);
            }

            PlayerHealth player = hit.GetComponentInParent<PlayerHealth>();
            if (player != null && !playerHit)
            {
                playerHit = true;
                player.TakeDamage(playerDamage * falloff, center);
            }

            Rigidbody other = hit.attachedRigidbody;
            if (other == null || !bodies.Add(other))
            {
                continue;
            }

            if (other.TryGetComponent(out AlienHealth alien) && !alien.IsDead)
            {
                if (other.TryGetComponent(out AlienBrain brain) && brain.enabled)
                {
                    brain.OnTelekinesisGrab();    // vira corpo físico para voar com a explosão
                    brain.OnTelekinesisRelease();
                }

                alien.TakeDamage(alienDamage * falloff);
            }

            if (other.TryGetComponent(out ExplosiveVehicle vehicle))
            {
                vehicle.TakeDamage(vehicleDamage * falloff); // reação em cadeia
            }
            else if (other.isKinematic && other.TryGetComponent(out TrafficCar traffic))
            {
                traffic.Abandon();
            }

            if (!other.isKinematic)
            {
                if (!other.TryGetComponent(out ImpactFeedback _))
                {
                    other.gameObject.AddComponent<ImpactFeedback>();
                }

                other.AddExplosionForce(power, center, radius, 1.5f, ForceMode.Impulse);
            }
        }
    }

    // Carcaça queimada: tudo escuro, o fogo diminui e fica só a fumaça.
    private void MakeWreck()
    {
        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            if (part is ParticleSystemRenderer)
            {
                continue;
            }

            Material material = part.material;
            material.color = Color.Lerp(material.color, Charcoal, 0.85f);
        }

        if (fire != null)
        {
            ParticleSystem.EmissionModule emission = fire.emission;
            emission.rateOverTime = 8f;
            Destroy(fire.gameObject, 6f);
        }

        if (smoke == null)
        {
            smoke = GameFeel.CreateSmoke(transform, Vector3.up * 1.3f);
        }
    }
}
