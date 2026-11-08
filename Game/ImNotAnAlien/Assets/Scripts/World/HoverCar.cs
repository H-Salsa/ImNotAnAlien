using System.Collections.Generic;
using UnityEngine;

// Carro flutuante (Missão 15).
// - Ligado (no trânsito): os discos brilham em ciano e o carro balança de leve no ar.
// - Abandonado (levantado pela telecinese, atingido, sem motorista): os discos APAGAM e o carro
//   cai no chão, apoiado neles (o colisor do casco vai até a base dos discos).
// - Explodiu: vira sucata —
//   * os 4 discos se soltam e saem voando;
//   * a lataria amassa (teto afunda, amassados pelo corpo todo);
//   * os vidros estouram em cacos;
//   * algumas portas voam longe e as outras ficam penduradas, tortas;
//   * os retrovisores e um para-choque saem voando; o outro para-choque entorta.
// O modelo fica no filho "Visual"; as peças soltas são filhos com nomes "Disco_*", "Porta_*",
// "Espelho_*" e "Parachoque_*".
[DisallowMultipleComponent]
public sealed class HoverCar : MonoBehaviour
{
    [Tooltip("Altura do balanço no ar (m) enquanto o carro está ligado.")]
    [SerializeField, Min(0f)] private float bobHeight = 0.025f;
    [SerializeField, Min(0.1f)] private float bobSpeed = 2.2f;
    [Tooltip("Velocidade com que as luzes apagam (por segundo).")]
    [SerializeField, Min(0.1f)] private float fadeSpeed = 2.5f;

    [Header("Destruição (explosão)")]
    [Tooltip("Quanto o teto afunda (0 = nada, 0,5 = metade da altura acima das janelas).")]
    [SerializeField, Range(0f, 0.8f)] private float roofCrush = 0.4f;
    [Tooltip("Profundidade dos amassados na lataria (m).")]
    [SerializeField, Range(0f, 0.3f)] private float dentDepth = 0.12f;
    [Tooltip("Quantas portas saem voando (as outras ficam penduradas).")]
    [SerializeField, Range(0, 4)] private int doorsFlyingOff = 2;
    [SerializeField, Range(0, 40)] private int glassShards = 18;
    [Tooltip("Segundos até as peças soltas sumirem.")]
    [SerializeField, Min(1f)] private float debrisLifetime = 12f;

    private TrafficCar traffic;
    private ExplosiveVehicle explosive;
    private Rigidbody body;
    private Transform visual;
    private float phase;
    private float power = 1f;
    private bool wrecked;

    private readonly List<Transform> discs = new List<Transform>();
    private readonly HashSet<Mesh> ownMeshes = new HashSet<Mesh>(); // malhas já copiadas só para este carro
    private readonly List<(Material material, Color emission)> glows = new List<(Material, Color)>();

    private void Start()
    {
        traffic = GetComponent<TrafficCar>();
        explosive = GetComponent<ExplosiveVehicle>();
        body = GetComponent<Rigidbody>();
        visual = transform.Find("Visual");
        phase = Random.Range(0f, 10f);
        if (visual == null)
        {
            enabled = false;
            return;
        }

        discs.AddRange(FindParts("Disco_"));

        // Cópias só deste carro dos materiais que brilham (luz dos discos e faróis).
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !(mats[i].name.Contains("Disco_Luz") || mats[i].name.Contains("Farol")))
                {
                    continue;
                }

                Material own = new Material(mats[i]);
                glows.Add((own, own.IsKeywordEnabled("_EMISSION") ? own.GetColor("_EmissionColor") : own.color));
                mats[i] = own;
                changed = true;
            }

            if (changed)
            {
                r.sharedMaterials = mats;
            }
        }

        // Carro estacionado (com física) já começa desligado: discos e faróis apagados.
        if (traffic == null && body != null && !body.isKinematic)
        {
            power = 0f;
            foreach ((Material material, Color _) in glows)
            {
                material.SetColor("_EmissionColor", Color.black);
            }
        }
    }

    private void Update()
    {
        bool exploded = explosive != null && explosive.IsExploded;
        bool powered = !exploded && (traffic == null || !traffic.IsAbandoned) && (body == null || body.isKinematic);

        // Luzes acendem/apagam suave.
        float target = powered ? 1f : 0f;
        if (!Mathf.Approximately(power, target))
        {
            power = Mathf.MoveTowards(power, target, fadeSpeed * Time.deltaTime);
            foreach ((Material material, Color emission) in glows)
            {
                material.SetColor("_EmissionColor", emission * power);
            }
        }

        // Balanço no ar só enquanto ligado (desligado, o modelo assenta no lugar).
        float bob = powered ? Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight : 0f;
        Vector3 local = visual.localPosition;
        local.y = Mathf.MoveTowards(local.y, bob, 0.5f * Time.deltaTime);
        visual.localPosition = local;

        if (exploded && !wrecked)
        {
            Wreck();
        }
    }

    // ---------- Sucata ----------

    private void Wreck()
    {
        wrecked = true;
        Vector3 center = body != null ? body.worldCenterOfMass : transform.position;

        CrushBody();   // antes de soltar as peças, para portas e lataria amassarem juntas
        ShatterGlass();

        foreach (Transform disc in discs)
        {
            Launch(disc, center, 7f, 11f, 15f);
        }

        // Portas: algumas voam, as outras ficam penduradas e tortas.
        List<Transform> doors = FindParts("Porta_");
        Shuffle(doors);
        for (int i = 0; i < doors.Count; i++)
        {
            if (i < doorsFlyingOff)
            {
                Launch(doors[i], center, 6f, 10f, 20f);
            }
            else
            {
                HangDoor(doors[i]);
            }
        }

        foreach (Transform mirror in FindParts("Espelho_"))
        {
            Launch(mirror, center, 5f, 9f, 2f);
        }

        List<Transform> bumpers = FindParts("Parachoque_");
        Shuffle(bumpers);
        for (int i = 0; i < bumpers.Count; i++)
        {
            if (i == 0)
            {
                Launch(bumpers[i], center, 5f, 8f, 12f);
            }
            else
            {
                bumpers[i].Rotate(visual.forward, Random.Range(-14f, 14f), Space.World);
                bumpers[i].position += -visual.up * 0.06f;
            }
        }
    }

    // Amassa todas as malhas do carro (lataria, portas, para-choques...). O deslocamento depende só
    // da posição de cada vértice, então peças encostadas amassam juntas, sem abrir rachaduras.
    private void CrushBody()
    {
        Vector3 seed = new Vector3(Random.Range(0f, 100f), Random.Range(0f, 100f), Random.Range(0f, 100f));
        float crush = roofCrush * Random.Range(0.7f, 1.2f);
        float tilt = Random.Range(-0.12f, 0.12f); // um lado do teto afunda mais que o outro

        foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
        {
            Mesh source = filter.sharedMesh;
            if (source == null || !source.isReadable || filter.name.StartsWith("Disco_"))
            {
                continue;
            }

            Matrix4x4 toVisual = visual.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 back = toVisual.inverse;
            Mesh mesh = Instantiate(source);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = toVisual.MultiplyPoint3x4(vertices[i]);

                // Teto afunda (mais no meio do carro que nas pontas).
                if (p.y > 1.1f)
                {
                    float middle = 1f - Mathf.Clamp01(Mathf.Abs(p.z) / 1.6f);
                    p.y -= (p.y - 1.1f) * (crush + tilt * p.x) * (0.5f + middle);
                }

                // Amassados: ruído suave, empurrando a lataria para dentro.
                float n = (Mathf.PerlinNoise(p.x * 2.7f + seed.x, p.y * 2.7f + seed.y)
                         + Mathf.PerlinNoise(p.y * 2.7f + seed.z, p.z * 2.7f + seed.x)
                         + Mathf.PerlinNoise(p.z * 2.7f + seed.y, p.x * 2.7f + seed.z)) / 3f;
                float dent = Mathf.Clamp01((n - 0.42f) * 3f) * dentDepth;
                Vector3 inward = new Vector3(-p.x, -(p.y - 0.8f) * 0.6f, -p.z * 0.35f);
                if (inward.sqrMagnitude > 1e-6f)
                {
                    p += inward.normalized * dent;
                }

                vertices[i] = back.MultiplyPoint3x4(p);
            }

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            ownMeshes.Add(mesh);
        }
    }

    // Vidros somem e viram cacos que voam.
    // Os triângulos de vidro são apagados da malha (só deixar o material transparente não basta:
    // o URP continua desenhando o reflexo do vidro).
    private void ShatterGlass()
    {
        Material shardMaterial = null;
        Bounds glassArea = new Bounds(visual.position + visual.up * 1.35f, Vector3.zero);
        foreach (MeshRenderer r in visual.GetComponentsInChildren<MeshRenderer>())
        {
            Material[] mats = r.sharedMaterials;
            MeshFilter filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            bool any = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].name.Contains("Vidro"))
                {
                    continue;
                }

                if (shardMaterial == null)
                {
                    shardMaterial = new Material(mats[i]);
                    Color c = shardMaterial.color;
                    c.a = 0.7f;
                    shardMaterial.color = c;
                }

                if (!any)
                {
                    glassArea.Encapsulate(r.bounds);
                }

                any = true;
                if (mesh != null && mesh.isReadable && i < mesh.subMeshCount)
                {
                    if (!ownMeshes.Contains(mesh))
                    {
                        mesh = Instantiate(mesh); // não mexe na malha original, compartilhada
                        filter.sharedMesh = mesh;
                        ownMeshes.Add(mesh);
                    }

                    mesh.SetTriangles(System.Array.Empty<int>(), i);
                }
                else
                {
                    // Sem acesso à malha: some com o vidro pelo material.
                    Material hidden = new Material(mats[i]);
                    Color invisible = hidden.color;
                    invisible.a = 0f;
                    hidden.color = invisible;
                    hidden.SetFloat("_BlendModePreserveSpecular", 0f);
                    hidden.SetFloat("_EnvironmentReflections", 0f);
                    hidden.SetFloat("_SpecularHighlights", 0f);
                    hidden.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    hidden.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    mats[i] = hidden;
                    r.sharedMaterials = mats;
                }
            }
        }

        if (shardMaterial == null)
        {
            return;
        }

        for (int i = 0; i < glassShards; i++)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Caco_Vidro";
            Vector3 spot = new Vector3(Random.Range(-0.75f, 0.75f), Random.Range(1.15f, 1.6f), Random.Range(-1.2f, 1.0f));
            shard.transform.SetPositionAndRotation(visual.TransformPoint(spot), Random.rotation);
            shard.transform.localScale = new Vector3(Random.Range(0.06f, 0.16f), 0.012f, Random.Range(0.05f, 0.13f));
            shard.GetComponent<Renderer>().sharedMaterial = shardMaterial;
            Rigidbody piece = shard.AddComponent<Rigidbody>();
            piece.mass = 0.2f;
            Vector3 away = (shard.transform.position - glassArea.center).normalized + Vector3.up * 0.8f;
            piece.AddForce(away.normalized * Random.Range(4f, 9f), ForceMode.VelocityChange);
            piece.AddTorque(Random.insideUnitSphere * 20f, ForceMode.VelocityChange);
            Destroy(shard, Random.Range(3f, 5f));
        }
    }

    // Porta com a dobradiça quebrada: abre um pouco para fora e entorta.
    private void HangDoor(Transform door)
    {
        Renderer r = door.GetComponent<Renderer>();
        Vector3 doorCenter = r != null ? r.bounds.center : door.position;
        Vector3 side = Vector3.Project(doorCenter - visual.position, visual.right).normalized;
        float angle = Random.Range(20f, 60f);
        Vector3 pivot = door.position;
        Vector3 moved = pivot + Quaternion.AngleAxis(angle, visual.up) * (doorCenter - pivot);
        if (Vector3.Dot(moved - doorCenter, side) < 0f)
        {
            angle = -angle; // gira para o lado de fora do carro
        }

        door.Rotate(visual.up, angle, Space.World);
        door.Rotate(visual.forward, Random.Range(-10f, 10f), Space.World); // dobradiça torta
    }

    // Solta a peça do carro e a lança para longe do centro da explosão.
    private void Launch(Transform part, Vector3 center, float minSpeed, float maxSpeed, float mass)
    {
        if (part == null)
        {
            return;
        }

        part.SetParent(null, true);
        Rigidbody piece = part.gameObject.AddComponent<Rigidbody>();
        piece.mass = mass;
        BoxCollider box = part.gameObject.AddComponent<BoxCollider>();
        if (part.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
        {
            box.center = filter.sharedMesh.bounds.center;
            box.size = Vector3.Max(filter.sharedMesh.bounds.size, Vector3.one * 0.03f);
        }

        Vector3 away = (part.position - center).normalized + Vector3.up * 1.2f;
        piece.AddForce(away.normalized * Random.Range(minSpeed, maxSpeed), ForceMode.VelocityChange);
        piece.AddTorque(Random.insideUnitSphere * 12f, ForceMode.VelocityChange);
        Destroy(part.gameObject, debrisLifetime);
    }

    private List<Transform> FindParts(string prefix)
    {
        List<Transform> parts = new List<Transform>();
        if (visual == null)
        {
            return parts;
        }

        foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith(prefix) && t.parent != null && !t.parent.name.StartsWith(prefix))
            {
                parts.Add(t);
            }
        }

        return parts;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
