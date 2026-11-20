using UnityEngine;

// Poste de rua (20/11). Dois estágios de estrago:
// 1. ARRANCADO (inteiro). Fica de pé, preso ao chão (corpo físico parado, custa quase nada), até:
//    - levar uma pancada forte: carro, caco de prédio, caixa ou alien arremessado (massa × velocidade);
//      pancadas menores vão somando até ele ceder;
//    - explosão (carro/barril), onda de choque, fogo/gelo/raio;
//    - ser puxado pela telecinese (TelekinesisPower chama Uproot ao pegar).
//    Sobra um toco na calçada, a luz apaga, faíscas e poeira; vira objeto com física (120 kg).
// 2. QUEBRADO (em 2 partes). Com dano suficiente o poste parte num ponto de quebra (perto de onde
//    apanhou): explosão perto, pancada muito forte (ex.: arremessado contra uma parede) ou dano somado.
//    Cada pedaço com 2,4 m ou mais ainda pode quebrar de novo (2, 3 ou 4 partes no total).
//    Os pedaços também são objetos: voam, machucam e dá para pegar com a telecinese.
//
// Preparado para a Superforça (depois): Uproot() arranca na mão; GripPoint é a ponta de segurar
// (a base) e TipPoint a ponta de bater (a luminária) — para usar o poste como taco.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class StreetLamp : MonoBehaviour
{
    public const float LampMass = 120f;

    // Estágio 1 (arrancar). Pancadas em kg·m/s (massa × velocidade).
    private const float Bump = 150f;           // abaixo disso é só esbarrão
    private const float UprootHit = 900f;      // arranca na hora (caixa de 40 kg a ~23 m/s, carro a ~1 m/s)
    private const float UprootToughness = 2000f; // pancadas menores somam até isso

    // Estágio 2 (quebrar em partes).
    private const float BreakHit = 3000f;      // parte na hora (carro a ~3 m/s; poste arremessado contra parede)
    private const float BreakToughness = 3000f; // dano somado (pancadas, explosões, poderes)
    private const float BlastBreak = 0.3f;     // explosão de carro/barril até ~70% do raio parte o poste
    private const float MinSplitLength = 2.4f; // pedaço menor que isso não quebra mais (pedaços de 1,2 m+)

    private const float StumpHeight = 0.3f;
    private static readonly Color SparkColor = new Color(1f, 0.85f, 0.45f);

    [SerializeField] private float height = 6.65f;  // comprimento total (com a luminária)
    [SerializeField] private float shaft = 6f;      // comprimento da haste
    [SerializeField] private Transform haste;
    [SerializeField] private Renderer bulb;
    [SerializeField] private Material poleMaterial;
    [SerializeField] private Material offMaterial;
    [SerializeField] private Mesh cylinderMesh;
    [SerializeField] private bool anchored = true;

    private Rigidbody body;
    private float uprootDamage;
    private float breakDamage;
    private float nextSplitTime;

    public bool IsAnchored => anchored;
    public bool IsPiece { get; private set; }
    public float Height => height;
    public Vector3 GripPoint => transform.TransformPoint(new Vector3(0f, 0.5f, 0f));
    public Vector3 TipPoint => transform.TransformPoint(new Vector3(0f, height, 0f));

    // ---------- Montagem (na hora de montar o bloco) ----------

    // Transforma um "Poste" (Haste + Luz) em poste arrancável. Só na montagem do bloco
    // (apaga colisores com DestroyImmediate, que é proibido dentro de batidas de física).
    public static StreetLamp Setup(Transform lamp)
    {
        if (lamp == null || lamp.GetComponent<StreetLamp>() != null)
        {
            return null;
        }

        Renderer hasteRenderer = null;
        Renderer bulbRenderer = null;
        float top = 0f;
        foreach (Renderer r in lamp.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "Haste") hasteRenderer = r;
            if (r.name == "Luz") bulbRenderer = r;
            top = Mathf.Max(top, lamp.InverseTransformPoint(r.bounds.max).y);
        }

        if (hasteRenderer == null)
        {
            return null; // não é um poste de rua (ex.: o "Poste" da antena parabólica)
        }

        foreach (Collider old in lamp.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(old);
        }

        top = Mathf.Max(1f, top);
        AddCapsule(lamp.gameObject, top, 0.18f);

        Rigidbody rb = lamp.gameObject.AddComponent<Rigidbody>();
        rb.mass = LampMass;
        rb.isKinematic = true; // de pé: preso ao chão
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        lamp.gameObject.AddComponent<TelekinesisTarget>();

        StreetLamp street = lamp.gameObject.AddComponent<StreetLamp>();
        street.height = top;
        street.haste = hasteRenderer.transform;
        street.shaft = Mathf.Abs(hasteRenderer.transform.localScale.y) * 2f; // cilindro do Unity: 2 m
        street.bulb = bulbRenderer;
        street.poleMaterial = hasteRenderer.sharedMaterial;
        street.offMaterial = hasteRenderer.sharedMaterial;
        street.cylinderMesh = hasteRenderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        return street;
    }

    private static void AddCapsule(GameObject go, float length, float radius)
    {
        CapsuleCollider capsule = go.GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            capsule = go.AddComponent<CapsuleCollider>();
        }

        capsule.direction = 1;
        capsule.radius = radius;
        capsule.height = Mathf.Max(length, radius * 2f + 0.01f);
        capsule.center = new Vector3(0f, length / 2f, 0f);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    // ---------- Pancadas ----------

    // Chamado também quando o poste está de pé (corpo parado recebe batidas de corpos em movimento).
    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody other = collision.rigidbody;
        float speed = collision.relativeVelocity.magnitude;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position + transform.up * (shaft / 2f);

        if (anchored)
        {
            if (other == null || other.isKinematic)
            {
                return;
            }

            float hit = other.mass * speed;
            if (hit < Bump)
            {
                return;
            }

            uprootDamage += hit;
            breakDamage += hit;
            if (hit < UprootHit && uprootDamage < UprootToughness)
            {
                return;
            }

            Vector3 push = point - other.worldCenterOfMass;
            push.y = 0f;
            if (push.sqrMagnitude < 0.01f)
            {
                push = transform.position - other.position;
                push.y = 0f;
            }

            // Empurra no ponto atingido (gira e tomba para o lado da pancada); muito forte = parte também.
            Knock(push.normalized * Mathf.Clamp(hit * 0.35f, 150f, 900f), point);
            if (hit >= BreakHit || breakDamage >= BreakToughness)
            {
                Split(point, push.normalized * 4f);
            }

            return;
        }

        // Segurado pela telecinese (ela desliga a gravidade): esbarrões no caminho não quebram.
        if (!body.useGravity)
        {
            return;
        }

        // Solto (voando): a pancada vale pelo menor dos dois pesos (bater num alien leve quebra menos
        // que bater numa parede ou no chão).
        float mass = other != null && !other.isKinematic ? Mathf.Min(body.mass, other.mass) : body.mass;
        float impact = mass * speed;
        if (impact < 600f)
        {
            return;
        }

        breakDamage += impact;
        if (impact >= BreakHit || breakDamage >= BreakToughness)
        {
            Split(point, collision.contactCount > 0 ? collision.GetContact(0).normal * 2f : Vector3.up * 2f);
        }
    }

    // Explosão de carro/barril (ExplosiveVehicle manda "OnBlast" para todos os corpos). Depois disto quem
    // chamou empurra o poste (ele já está solto). Perto da explosão parte em dois.
    public void OnBlast(float falloff)
    {
        if (falloff < 0.1f)
        {
            return;
        }

        Uproot();
        breakDamage += falloff * BreakToughness;
        if (falloff >= BlastBreak || breakDamage >= BreakToughness)
        {
            Split(RandomBreakPoint(), Random.onUnitSphere * 3f);
        }
    }

    // Poderes (fogo/gelo/raio, onda de choque). strength 0..1: perto/forte arranca; dano somado parte.
    public void Hit(float strength)
    {
        if (strength <= 0f)
        {
            return;
        }

        uprootDamage += strength * UprootToughness;
        if (anchored && strength < 0.2f && uprootDamage < UprootToughness)
        {
            return;
        }

        Uproot();
        breakDamage += strength * BreakToughness * 0.6f;
        if (breakDamage >= BreakToughness)
        {
            Split(RandomBreakPoint(), Random.onUnitSphere * 2f);
        }
    }

    // Arranca e empurra (impulso em N·s no ponto).
    public void Knock(Vector3 impulse, Vector3 point)
    {
        Uproot();
        body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
    }

    // ---------- Estágio 1: arrancar ----------

    // Solta o poste do chão. Usado pelas pancadas, pela telecinese e (depois) pela Superforça.
    public void Uproot()
    {
        if (!anchored)
        {
            return;
        }

        anchored = false;
        Vector3 foot = transform.position;
        LeaveStump(foot);

        // Sai da parte "estática" do bloco (a que entra no NavMesh) para a dinâmica (some com o bloco).
        WorldBlock block = GetComponentInParent<WorldBlock>();
        if (block != null && block.DynamicRoot != null)
        {
            transform.SetParent(block.DynamicRoot, true);
        }

        transform.position = foot + Vector3.up * StumpHeight; // quebra acima do toco
        MakeLoose(body);

        // Luz apaga, faíscas no pé e poeirinha.
        if (bulb != null && offMaterial != null)
        {
            bulb.sharedMaterial = offMaterial;
        }

        GameFeel.Impact(foot + Vector3.up * StumpHeight, 14f, SparkColor);
        DestructionDust.Impact(foot, 1.2f);
    }

    private void MakeLoose(Rigidbody rb)
    {
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.position = rb.transform.position;
        if (!rb.TryGetComponent(out ImpactFeedback _))
        {
            rb.gameObject.AddComponent<ImpactFeedback>();
        }
    }

    // Toco na calçada: só visual (sem colisor), no mesmo lugar do bloco.
    private void LeaveStump(Vector3 foot)
    {
        if (cylinderMesh == null || offMaterial == null)
        {
            return;
        }

        GameObject stump = new GameObject("Poste_Toco");
        stump.transform.SetParent(transform.parent, false);
        stump.transform.position = foot + Vector3.up * (StumpHeight / 2f);
        stump.transform.rotation = transform.rotation;
        stump.transform.localScale = new Vector3(0.26f, StumpHeight / 2f, 0.26f);
        stump.AddComponent<MeshFilter>().sharedMesh = cylinderMesh;
        stump.AddComponent<MeshRenderer>().sharedMaterial = offMaterial;
    }

    // ---------- Estágio 2: quebrar em partes ----------

    private Vector3 RandomBreakPoint() => transform.TransformPoint(new Vector3(0f, shaft * Random.Range(0.4f, 0.6f), 0f));

    // Parte o poste em dois no ponto da haste mais perto de "point". Este objeto vira o pedaço de baixo
    // (base); o de cima (com a luminária) nasce como objeto novo. "kick" = velocidade extra para separar.
    public void Split(Vector3 point, Vector3 kick)
    {
        if (shaft < MinSplitLength || haste == null || cylinderMesh == null || Time.time < nextSplitTime)
        {
            return;
        }

        Uproot();
        nextSplitTime = Time.time + 0.15f; // uma quebra por pancada

        float at = Mathf.Clamp(transform.InverseTransformPoint(point).y, shaft * 0.3f, shaft * 0.7f);
        at = Mathf.Clamp(at, 1.2f, shaft - 1.2f);
        float topShaft = shaft - at;
        float massPerMeter = body.mass / Mathf.Max(0.5f, height);
        Vector3 breakPoint = transform.TransformPoint(new Vector3(0f, at, 0f));
        Vector3 velocity = body.GetPointVelocity(breakPoint);
        Vector3 spin = body.angularVelocity;

        // Pedaço de cima (novo).
        GameObject top = new GameObject(IsPiece ? name : "Poste_Pedaco");
        top.transform.SetParent(transform.parent, false);
        top.transform.SetPositionAndRotation(breakPoint, transform.rotation);

        Transform topHaste = new GameObject("Haste").transform;
        topHaste.SetParent(top.transform, false);
        topHaste.localPosition = new Vector3(0f, topShaft / 2f, 0f);
        topHaste.localScale = new Vector3(haste.localScale.x, topShaft / 2f, haste.localScale.z);
        topHaste.gameObject.AddComponent<MeshFilter>().sharedMesh = cylinderMesh;
        topHaste.gameObject.AddComponent<MeshRenderer>().sharedMaterial = poleMaterial;

        float topLength = height - at; // inclui a luminária, se ela estiver neste pedaço
        Renderer topBulb = bulb;
        if (bulb != null)
        {
            bulb.transform.SetParent(top.transform, true);
        }
        else
        {
            topLength = topShaft;
        }

        AddCapsule(top, topLength, topBulb != null ? 0.18f : 0.12f);
        Rigidbody topBody = top.AddComponent<Rigidbody>();
        topBody.mass = Mathf.Max(10f, massPerMeter * topLength);
        MakeLoose(topBody);
        topBody.linearVelocity = velocity + kick;
        topBody.angularVelocity = spin + Random.insideUnitSphere * 2f;
        top.AddComponent<TelekinesisTarget>();

        StreetLamp piece = top.AddComponent<StreetLamp>();
        piece.anchored = false;
        piece.IsPiece = true;
        piece.height = topLength;
        piece.shaft = topShaft;
        piece.haste = topHaste;
        piece.bulb = topBulb;
        piece.poleMaterial = poleMaterial;
        piece.offMaterial = offMaterial;
        piece.cylinderMesh = cylinderMesh;
        piece.nextSplitTime = nextSplitTime;

        // Este objeto vira o pedaço de baixo.
        haste.localPosition = new Vector3(haste.localPosition.x, at / 2f, haste.localPosition.z);
        haste.localScale = new Vector3(haste.localScale.x, at / 2f, haste.localScale.z);
        AddCapsule(gameObject, at, 0.12f);
        body.mass = Mathf.Max(10f, massPerMeter * at);
        body.linearVelocity -= kick * 0.5f;
        bulb = null;
        height = at;
        shaft = at;
        IsPiece = true;
        breakDamage = 0f;

        GameFeel.Impact(breakPoint, 18f, SparkColor);
        DestructionDust.Impact(breakPoint, 0.8f);
    }
}
