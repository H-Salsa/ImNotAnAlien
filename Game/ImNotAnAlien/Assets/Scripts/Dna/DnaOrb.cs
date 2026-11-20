using UnityEngine;

// Um orbe de DNA. Criado pelo DnaOrbSystem; não precisa de prefab.
// Fases: Salto (sai do corpo) -> Flutuando (no chão) -> Atraído (voa até o jogador) -> absorvido.
public sealed class DnaOrb : MonoBehaviour
{
    private enum Phase
    {
        Burst,
        Idle,
        Homing,
    }

    private const float AbsorbDistance = 0.7f;
    private const float FallOutHeight = -40f;

    private DnaOrbSystem system;
    private Phase phase;
    private Vector3 velocity;
    private Vector3 restPosition;
    private float age;
    private float homingSpeed;
    private float bobOffset;

    public int Value { get; private set; }
    public int SourceAlienId { get; private set; }

    private TrailRenderer trail;
    private bool inBox;
    private static Mesh sphereMesh;

    // Pega um orbe da caixa do DnaOrbSystem (18/11: reaproveitamento) ou monta um novo se a caixa
    // estiver vazia. Tudo que mudou no uso anterior é zerado aqui.
    public static DnaOrb Create(DnaOrbSystem system, int sourceAlienId, int value, Vector3 position, Vector3 velocity)
    {
        DnaOrb orb = system.TakeOrb();
        if (orb == null)
        {
            orb = Build(system);
        }

        orb.transform.SetPositionAndRotation(position, Quaternion.identity);
        orb.transform.localScale = Vector3.one * system.OrbSize;
        orb.SourceAlienId = sourceAlienId;
        orb.Value = value;
        orb.velocity = velocity;
        orb.restPosition = position;
        orb.homingSpeed = 0f;
        orb.age = 0f;
        orb.phase = Phase.Burst;
        orb.bobOffset = Random.value * 10f;
        orb.inBox = false;
        orb.gameObject.SetActive(true);
        orb.trail.Clear(); // sem o risco do rastro do uso anterior
        return orb;
    }

    public static DnaOrb Build(DnaOrbSystem system)
    {
        // Montado à mão, sem colisor (atravessa tudo: é energia, não objeto). Conserto 18/11: antes era
        // uma esfera pronta com o colisor apagado na hora, mas a Unity não deixa apagar na hora quando o
        // orbe nasce dentro de uma batida (alien morto por pancada) e o colisor ficava.
        GameObject go = new GameObject("Orbe_DNA");
        go.transform.localScale = Vector3.one * system.OrbSize;
        if (sphereMesh == null)
        {
            sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
        }

        go.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
        MeshRenderer orbRenderer = go.AddComponent<MeshRenderer>();
        if (system.OrbMaterial != null)
        {
            orbRenderer.sharedMaterial = system.OrbMaterial;
        }

        orbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        TrailRenderer trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = system.OrbSize * 0.8f;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.05f;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.startColor = system.OrbColor;
        trail.endColor = new Color(system.OrbColor.r, system.OrbColor.g, system.OrbColor.b, 0f);
        if (system.TrailMaterial != null)
        {
            trail.sharedMaterial = system.TrailMaterial;
        }

        DnaOrb orb = go.AddComponent<DnaOrb>();
        orb.system = system;
        orb.trail = trail;
        return orb;
    }

    // Já está guardado na caixa (o sistema não guarda o mesmo orbe duas vezes).
    public bool InBox => inBox;

    public void MarkStored()
    {
        inBox = true;
    }

    private void OnEnable()
    {
        WorldOrigin.Shifted += OnOriginShifted;
    }

    private void OnDisable()
    {
        WorldOrigin.Shifted -= OnOriginShifted;
    }

    // O mundo recentralizou (Missão 16): o ponto onde o orbe flutua anda junto.
    private void OnOriginShifted(Vector3 delta)
    {
        restPosition -= delta;
    }

    private void Update()
    {
        if (system == null)
        {
            Destroy(gameObject);
            return;
        }

        float dt = Time.deltaTime;
        age += dt;

        switch (phase)
        {
            case Phase.Burst:
                UpdateBurst(dt);
                break;
            case Phase.Idle:
                UpdateIdle();
                break;
            case Phase.Homing:
                UpdateHoming(dt);
                break;
        }
    }

    private void UpdateBurst(float dt)
    {
        velocity += Physics.gravity * dt;
        Vector3 position = transform.position;
        Vector3 step = velocity * dt;

        // Pousa quando encontra o chão (ou um telhado, uma caixa...).
        if (velocity.y < 0f &&
            Physics.Raycast(position, Vector3.down, out RaycastHit hit, system.HoverHeight + Mathf.Abs(step.y),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            restPosition = hit.point + Vector3.up * system.HoverHeight;
            transform.position = restPosition;
            phase = Phase.Idle;
            return;
        }

        transform.position = position + step;

        if (transform.position.y < FallOutHeight)
        {
            StartHoming(); // caiu do mapa: vai direto para o jogador
            return;
        }

        if (ShouldHome())
        {
            StartHoming();
        }
    }

    private void UpdateIdle()
    {
        // Flutua e gira devagar.
        float bob = Mathf.Sin((Time.time + bobOffset) * 3f) * 0.12f;
        transform.position = restPosition + Vector3.up * bob;
        transform.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);

        if (ShouldHome())
        {
            StartHoming();
        }
    }

    private bool ShouldHome()
    {
        if (system.Player == null || age < system.MinAgeBeforePickup)
        {
            return false;
        }

        if (age >= system.SafetyNetTime)
        {
            return true; // rede de segurança
        }

        return (system.Player.position - transform.position).sqrMagnitude <= system.MagnetRadius * system.MagnetRadius;
    }

    private void StartHoming()
    {
        phase = Phase.Homing;
        homingSpeed = Mathf.Max(velocity.magnitude, 2f);
        velocity += Vector3.up * 3f; // um pequeno "pulo" antes de voar, fica mais vivo
    }

    private void UpdateHoming(float dt)
    {
        if (system.Player == null)
        {
            return;
        }

        Vector3 target = system.Player.position + Vector3.up * 0.8f;
        Vector3 toTarget = target - transform.position;
        float distance = toTarget.magnitude;

        if (distance <= AbsorbDistance)
        {
            system.Absorb(this);
            system.StoreOrb(this); // volta para a caixa em vez de ser destruído
            return;
        }

        homingSpeed = Mathf.MoveTowards(homingSpeed, system.HomingMaxSpeed, system.HomingAcceleration * dt);
        // Curva suave em direção ao jogador (em vez de linha reta seca).
        velocity = Vector3.Lerp(velocity, toTarget / distance * homingSpeed, 8f * dt);
        Vector3 step = velocity * dt;

        // Evita passar direto pelo jogador em alta velocidade.
        transform.position = step.magnitude >= distance ? target : transform.position + step;
    }
}
