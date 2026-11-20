using UnityEngine;

// O que voa da mão do protagonista (Missão 21): a BOLA DE FOGO e a LASCA DE GELO.
// (O raio não usa projétil: é instantâneo — veja ElementalPower e ElementalBolt.)
// Anda em linha reta, sem física; a cada quadro confere se bateu em algo no caminho.
// Ao bater, chama as regras (ElementalCombat) e some. O visual é montado aqui, por código.
[DisallowMultipleComponent]
public sealed class ElementalProjectile : MonoBehaviour
{
    private const float Lifetime = 3.5f;
    private static readonly RaycastHit[] Hits = new RaycastHit[24];

    private ElementKind kind;
    private float charge;
    private Vector3 velocity;
    private float radius;
    private float age;
    private Transform owner;
    private Rigidbody ignoredBody;
    private Transform core;
    private Light glow;
    private ParticleSystem[] trails;
    private bool done;

    public static ElementalProjectile Launch(ElementKind kind, Vector3 origin, Vector3 direction, float charge, Transform owner, Rigidbody ignoredBody)
    {
        GameObject go = new GameObject(kind == ElementKind.Fire ? "Bola_De_Fogo" : "Lasca_De_Gelo");
        go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
        ElementalProjectile projectile = go.AddComponent<ElementalProjectile>();
        ElementalTuning tuning = ElementalTuning.Current;
        projectile.kind = kind;
        projectile.charge = charge;
        projectile.owner = owner;
        projectile.ignoredBody = ignoredBody;
        projectile.velocity = direction.normalized * (kind == ElementKind.Fire ? tuning.fireSpeed : tuning.iceSpeed);
        projectile.radius = Mathf.Lerp(0.18f, 0.4f, charge);
        if (kind == ElementKind.Fire)
        {
            projectile.BuildFire();
        }
        else
        {
            projectile.BuildIce();
        }

        return projectile;
    }

    // ---------- Visual ----------

    private void BuildFire()
    {
        float size = Mathf.Lerp(0.3f, 0.75f, charge);
        Color main = ElementalFx.Main(ElementKind.Fire);
        Color hot = ElementalFx.Hot(ElementKind.Fire);
        Color deep = ElementalFx.Deep(ElementKind.Fire);

        Material coreMaterial = ElementalFx.Solid("Fogo_Miolo", new Color(1.6f, 1.25f, 0.55f, 1f), new Color(2.4f, 0.7f, 0.1f, 1f), 1.6f, true);
        core = ElementalFx.MeshObject("Miolo", ElementalFx.Sphere, coreMaterial, transform).transform;
        core.localScale = Vector3.one * size;

        trails = new[]
        {
            // Halo que acompanha a bola.
            ElementalFx.Emitter("Halo", transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_glow", 1.6f), new ElementalFx.Spec
            {
                rate = 40f,
                life = new Vector2(0.08f, 0.12f),
                speed = Vector2.zero,
                size = Vector2.one * size * 3.2f,
                colorA = main,
                colorB = main,
                radius = 0.001f,
                local = true,
                loop = true,
                fade = ElementalFx.Fade(Color.white, Color.white, 0.2f, 0.6f, 0.55f),
                sizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f),
                max = 12,
            }),
            // Labaredas que ficam para trás.
            ElementalFx.Emitter("Labaredas", transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_flame", 1.5f), new ElementalFx.Spec
            {
                rate = Mathf.Lerp(55f, 90f, charge),
                life = new Vector2(0.25f, 0.5f),
                speed = new Vector2(0f, 1.2f),
                size = new Vector2(0.9f, 1.6f) * size,
                colorA = hot,
                colorB = main,
                radius = size * 0.3f,
                gravity = -0.3f,
                loop = true,
                fade = ElementalFx.Fade(Color.white, deep, 0.05f, 0.3f),
                sizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.2f),
                max = 80,
            }),
            // Brasas (cubinhos) caindo.
            ElementalFx.Emitter("Brasas", transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_glow", 2f), new ElementalFx.Spec
            {
                rate = Mathf.Lerp(14f, 30f, charge),
                life = new Vector2(0.4f, 0.9f),
                speed = new Vector2(0.5f, 2.5f),
                size = new Vector2(0.05f, 0.11f),
                colorA = hot,
                colorB = main,
                radius = size * 0.4f,
                gravity = 1f,
                mesh = ElementalFx.Cube,
                spin = 300f,
                loop = true,
                fade = ElementalFx.Fade(Color.white, deep, 0f, 0.5f),
                max = 40,
            }),
            // Fumaça fina.
            ElementalFx.Emitter("Fumaca", transform, Vector3.zero, Quaternion.identity, ElementalFx.Blended("fx_smoke"), new ElementalFx.Spec
            {
                rate = Mathf.Lerp(10f, 18f, charge),
                life = new Vector2(0.6f, 1.1f),
                speed = new Vector2(0f, 0.6f),
                size = new Vector2(0.7f, 1.2f) * size,
                colorA = new Color(0.2f, 0.17f, 0.16f),
                colorB = new Color(0.35f, 0.3f, 0.28f),
                radius = size * 0.2f,
                gravity = -0.2f,
                spin = 40f,
                loop = true,
                fade = ElementalFx.Fade(Color.white, Color.white, 0.25f, 0.5f, 0.35f),
                sizeCurve = AnimationCurve.Linear(0f, 0.6f, 1f, 1.7f),
                max = 30,
                sortFudge = 3f,
            }),
        };

        AddGlow(main, Mathf.Lerp(3.5f, 7f, charge), Mathf.Lerp(6f, 10f, charge));
    }

    private void BuildIce()
    {
        float size = Mathf.Lerp(0.55f, 1.1f, charge);
        Color main = ElementalFx.Main(ElementKind.Ice);
        Color hot = ElementalFx.Hot(ElementKind.Ice);

        // A lasca voa de ponta: o cristal (ponta em +Y) é deitado para a frente.
        core = new GameObject("Lasca").transform;
        core.SetParent(transform, false);
        Transform crystal = ElementalFx.MeshObject("Cristal", ElementalFx.Crystal, ElementalFx.IceMaterial(), core).transform;
        crystal.localRotation = Quaternion.Euler(90f, 0f, 0f);
        crystal.localPosition = new Vector3(0f, 0f, -size * 0.5f);
        crystal.localScale = new Vector3(size * 0.75f, size, size * 0.75f);

        trails = new[]
        {
            ElementalFx.Emitter("Halo", transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_glow", 1.3f), new ElementalFx.Spec
            {
                rate = 40f,
                life = new Vector2(0.08f, 0.12f),
                speed = Vector2.zero,
                size = Vector2.one * size * 1.8f,
                colorA = main,
                colorB = main,
                radius = 0.001f,
                local = true,
                loop = true,
                fade = ElementalFx.Fade(Color.white, Color.white, 0.2f, 0.6f, 0.45f),
                sizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f),
                max = 12,
            }),
            // Névoa fria no rastro.
            ElementalFx.Emitter("Nevoa", transform, Vector3.zero, Quaternion.identity, ElementalFx.Blended("fx_smoke"), new ElementalFx.Spec
            {
                rate = Mathf.Lerp(40f, 60f, charge),
                life = new Vector2(0.35f, 0.7f),
                speed = new Vector2(0f, 0.4f),
                size = new Vector2(0.45f, 0.8f) * size,
                colorA = new Color(0.85f, 0.95f, 1f),
                colorB = Color.white,
                radius = size * 0.12f,
                spin = 30f,
                loop = true,
                fade = ElementalFx.Fade(Color.white, Color.white, 0.1f, 0.3f, 0.35f),
                sizeCurve = AnimationCurve.Linear(0f, 0.7f, 1f, 1.5f),
                max = 60,
            }),
            // Flocos e brilhos que ficam caindo.
            ElementalFx.Emitter("Flocos", transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_flake", 1.4f), new ElementalFx.Spec
            {
                rate = Mathf.Lerp(26f, 44f, charge),
                life = new Vector2(0.5f, 1f),
                speed = new Vector2(0.2f, 1.2f),
                size = new Vector2(0.07f, 0.17f),
                colorA = hot,
                colorB = main,
                radius = size * 0.2f,
                gravity = 0.3f,
                spin = 160f,
                noise = 0.5f,
                loop = true,
                max = 60,
            }),
            ElementalFx.Emitter("Brilhos", transform, Vector3.zero, Quaternion.identity, ElementalFx.Additive("fx_spark", 2f), new ElementalFx.Spec
            {
                rate = 16f,
                life = new Vector2(0.15f, 0.3f),
                speed = Vector2.zero,
                size = new Vector2(0.25f, 0.55f) * size,
                colorA = hot,
                colorB = hot,
                radius = size * 0.25f,
                loop = true,
                max = 12,
            }),
        };

        AddGlow(main, Mathf.Lerp(2f, 4.5f, charge), Mathf.Lerp(5f, 8f, charge));
    }

    private void AddGlow(Color color, float intensity, float range)
    {
        glow = gameObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = color;
        glow.intensity = intensity;
        glow.range = range;
        glow.shadows = LightShadows.None;
    }

    // ---------- Voo ----------

    private void Update()
    {
        if (done)
        {
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        age += dt;
        Vector3 position = transform.position;
        Vector3 step = velocity * dt;
        float distance = step.magnitude;
        Vector3 direction = step / Mathf.Max(0.0001f, distance);

        if (FindHit(position, direction, distance, out RaycastHit hit))
        {
            transform.position = hit.distance > 0f ? position + direction * Mathf.Max(0f, hit.distance - 0.05f) : position;
            Impact(hit.distance > 0f ? hit.point : position, hit.distance > 0f ? hit.normal : -direction, hit.collider);
            return;
        }

        transform.position = position + step;

        // Animação do miolo: a bola de fogo pulsa; a lasca de gelo gira no próprio eixo.
        if (core != null)
        {
            if (kind == ElementKind.Fire)
            {
                core.localScale = Vector3.one * Mathf.Lerp(0.3f, 0.75f, charge) * (1f + 0.12f * Mathf.Sin(age * 40f));
            }
            else
            {
                core.Rotate(0f, 0f, 720f * dt, Space.Self);
            }
        }

        // Fogo que entra na água apaga com vapor (o gelo e o raio seguem).
        if (kind == ElementKind.Fire && ElementalCombat.IsWet(transform.position))
        {
            ElementalFx.Steam(transform.position, Mathf.Lerp(0.8f, 1.6f, charge));
            ElementalFxRunner.Play(ElementalFxRunner.Sound.Sizzle, transform.position, 0.8f);
            Finish();
            return;
        }

        if (age >= Lifetime)
        {
            ElementalFx.Fizzle(kind, transform.position);
            Finish();
        }
    }

    // O mais perto que não seja o próprio jogador nem o objeto que a telecinese está segurando.
    private bool FindHit(Vector3 position, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        int count = Physics.SphereCastNonAlloc(position, radius, direction, Hits, distance, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = Hits[i];
            if (owner != null && hit.collider.transform.IsChildOf(owner))
            {
                continue;
            }

            if (ignoredBody != null && hit.collider.attachedRigidbody == ignoredBody)
            {
                continue;
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

    private void Impact(Vector3 point, Vector3 normal, Collider collider)
    {
        if (kind == ElementKind.Fire)
        {
            ElementalCombat.FireImpact(point, normal, collider, charge, owner);
        }
        else
        {
            ElementalCombat.IceImpact(point, normal, collider, charge, owner);
        }

        Finish();
    }

    // Some: o rastro termina de sumir sozinho.
    private void Finish()
    {
        done = true;
        if (trails != null)
        {
            foreach (ParticleSystem trail in trails)
            {
                if (trail == null)
                {
                    continue;
                }

                // O halo anda junto com o projétil (espaço local): some na hora.
                if (trail.main.simulationSpace == ParticleSystemSimulationSpace.Local)
                {
                    ElementalFx.Recycle(trail); // 18/11: volta para a caixa de efeitos
                }
                else
                {
                    ElementalFx.Release(trail);
                }
            }
        }

        Destroy(gameObject);
    }
}
