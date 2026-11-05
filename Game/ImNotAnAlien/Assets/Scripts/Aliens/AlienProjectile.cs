using UnityEngine;

// Projétil dos policiais: uma bola de energia que voa em linha reta.
// É lenta o bastante para ser vista e desviada. Objetos no caminho bloqueiam o tiro.
public sealed class AlienProjectile : MonoBehaviour
{
    private const float Radius = 0.18f;
    private const float Lifetime = 4f;

    private static readonly RaycastHit[] Hits = new RaycastHit[8];
    private static readonly Color ProjectileColor = new Color(1f, 0.25f, 0.55f, 1f);

    private Vector3 velocity;
    private float damage;
    private Transform owner;
    private bool friendlyFire;
    private float age;

    public static void Spawn(Material material, Vector3 position, Vector3 velocity, float damage, Transform owner,
        bool friendlyFire)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Tiro_Policial";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = position;
        go.transform.localScale = Vector3.one * Radius * 2f;

        Renderer projectileRenderer = go.GetComponent<Renderer>();
        projectileRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (material != null)
        {
            projectileRenderer.sharedMaterial = material;
        }

        TrailRenderer trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.07f; // rastro curto: tiro rápido parece um traço
        trail.startWidth = Radius * 1.6f;
        trail.endWidth = 0f;
        trail.startColor = ProjectileColor;
        trail.endColor = new Color(ProjectileColor.r, ProjectileColor.g, ProjectileColor.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (material != null)
        {
            trail.sharedMaterial = material;
        }

        AlienProjectile projectile = go.AddComponent<AlienProjectile>();
        projectile.velocity = velocity;
        projectile.damage = damage;
        projectile.owner = owner;
        projectile.friendlyFire = friendlyFire;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age > Lifetime)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 position = transform.position;
        Vector3 step = velocity * Time.deltaTime;
        float distance = step.magnitude;

        if (distance > 0f && TryHit(position, step / distance, distance, out RaycastHit hit))
        {
            OnHit(hit);
            return;
        }

        transform.position = position + step;
    }

    private bool TryHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.SphereCastNonAlloc(origin, Radius, direction, Hits, distance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = Hits[i];
            if (owner != null && hit.collider.transform.IsChildOf(owner))
            {
                continue; // não acerta quem atirou
            }

            if (hit.distance < best)
            {
                best = hit.distance;
                nearest = hit;
                found = true;
            }
        }

        return found;
    }

    private void OnHit(RaycastHit hit)
    {
        Vector3 point = hit.point == Vector3.zero ? transform.position : hit.point;

        PlayerHealth playerHealth = hit.collider.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(damage, point);
        }
        else if (friendlyFire)
        {
            AlienHealth alien = hit.collider.GetComponentInParent<AlienHealth>();
            if (alien != null)
            {
                alien.TakeDamage(damage);
            }
        }

        GameFeel.Impact(point, 10f, ProjectileColor);
        Destroy(gameObject);
    }
}
