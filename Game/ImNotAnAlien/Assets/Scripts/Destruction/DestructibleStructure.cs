using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

// Estrutura destrutível (decisão D15). Fica no objeto "raiz" de uma casa
// (ex.: Casa_A), e as partes (Paredes, Telhado, Porta...) ficam como filhas.
// - Só objetos PESADOS causam dano ao bater (carro sim; caixas, lixeiras e alienígenas não).
// - Cada batida: a casa treme, solta poeira e pedacinhos e fica mais "encardida".
// - Ao chegar a zero: desmorona em blocos que caem, afundam no chão e são apagados;
//   no lugar fica um pequeno monte de escombros. Destruir não dá recompensa (só visual).
// - O NavMesh é atualizado para os alienígenas andarem por onde a casa estava.
// Outras fontes de dano (Superforça, arma futura) chamam TakeDamage().
[DisallowMultipleComponent]
public sealed class DestructibleStructure : MonoBehaviour
{
    [Header("Vida")]
    [SerializeField, Min(1f)] private float maxHealth = 500f;

    [Header("Dano por impacto")]
    [Tooltip("Objetos mais leves que isso (kg) não causam dano. O carro pesa 900 kg; caixas, 40.")]
    [SerializeField, Min(0f)] private float minImpactMass = 300f;
    [Tooltip("Batidas mais lentas que isso (m/s) não causam dano.")]
    [SerializeField, Min(0f)] private float minImpactSpeed = 8f;
    [Tooltip("Dano = (velocidade - mínima) x massa / este valor. Com 100, um carro a 28 m/s tira 180.")]
    [SerializeField, Min(1f)] private float massDivisor = 100f;

    [Header("Desmoronamento")]
    [Tooltip("Tamanho aproximado de cada bloco (m). Menor = mais blocos.")]
    [SerializeField, Min(0.5f)] private float fragmentSize = 2.2f;
    [SerializeField, Min(1)] private int maxDivisionsPerAxis = 3;
    [Tooltip("Quantos pedaços formam o monte de escombros.")]
    [SerializeField, Min(1)] private int rubblePieces = 7;
    [SerializeField, Range(0f, 1f)] private float rubbleDarkness = 0.45f;

    private static Mesh cubeMesh;
    private static readonly Dictionary<Color, Material> debrisMaterials = new Dictionary<Color, Material>();

    // Casas com material de textura (casas retrô, Missão 20): os blocos do desmoronamento usam cor lisa.
    private bool hasDebrisColors;
    private Vector3 lastHitPoint;
    private Rigidbody lastAttacker;

    // Poeira, polícia e NavMesh quando a casa do Blender desaba (uma vez só).
    private bool houseCollapseDone;

    private void FinishHouseCollapse()
    {
        collapsed = true;
        if (houseCollapseDone)
        {
            return;
        }

        houseCollapseDone = true;
        Vector3 center = transform.position + Vector3.up * 1.5f;
        GameFeel.StructureCollapse(center, hasDebrisColors ? debrisMain : Color.gray, 10f);
        WantedSystem.ReportDestruction(center);
        StartCoroutine(UpdateNavMeshNextFrame());
    }

    [Tooltip("Casas de malha (retrô): em quantos cacos quebra.")]
    [SerializeField, Min(4)] private int shatterPieces = 22;
    private Color debrisMain;
    private Color debrisSecond;

    private readonly List<Renderer> partRenderers = new List<Renderer>();
    private readonly List<Color> originalColors = new List<Color>();
    private Vector3 restPosition;
    private Coroutine shakeRoutine;
    private bool collapsed;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsCollapsed => collapsed;

    // Missão 20: a casa pode subir no relevo depois de criada (o bloco "assenta" tudo no chão),
    // então a posição de descanso é pega de novo no Start.
    private void Start()
    {
        restPosition = transform.localPosition;
    }

    private void Awake()
    {
        CurrentHealth = maxHealth;
        restPosition = transform.localPosition;
        if (cubeMesh == null)
        {
            cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        }

        // Cada parte com colisor avisa esta estrutura quando é atingida.
        foreach (Collider part in GetComponentsInChildren<Collider>())
        {
            if (!part.TryGetComponent(out StructurePart _))
            {
                part.gameObject.AddComponent<StructurePart>();
            }
        }

        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            partRenderers.Add(part);
            originalColors.Add(part.sharedMaterial != null ? part.sharedMaterial.color : Color.white);
        }
    }

    // Prédios: mais vida conforme a altura.
    public void SetMaxHealth(float health)
    {
        maxHealth = Mathf.Max(1f, health);
        CurrentHealth = maxHealth;
    }

    // Casas que pintam as cores com uma textura (paleta): os pedaços ficam com estas cores lisas.
    public void SetDebrisColors(Color main, Color second)
    {
        hasDebrisColors = true;
        debrisMain = main;
        debrisSecond = second;
    }

    private Material DebrisMaterial(Material source, bool second)
    {
        if (!hasDebrisColors || source == null)
        {
            return source;
        }

        Color color = second ? debrisSecond : debrisMain;
        if (!debrisMaterials.TryGetValue(color, out Material material) || material == null)
        {
            material = new Material(source) { name = "Entulho" };
            material.mainTexture = null;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            debrisMaterials[color] = material;
        }

        return material;
    }

    // ---------- Dano ----------

    // Chamado pelas partes (StructurePart) quando algo bate nelas.
    public void OnPartHit(Collision collision)
    {
        if (collapsed)
        {
            return;
        }

        Rigidbody other = collision.rigidbody;
        if (other == null || other.isKinematic || other.mass < minImpactMass)
        {
            return; // leve demais (ou parado): não machuca a casa
        }

        float speed = collision.relativeVelocity.magnitude;
        if (speed <= minImpactSpeed)
        {
            return;
        }

        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : other.position;
        // Direção do golpe: de quem bateu para o ponto atingido, com a velocidade da batida.
        Vector3 into = point - other.worldCenterOfMass;
        Vector3 impact = (into.sqrMagnitude > 0.01f ? into.normalized : collision.relativeVelocity.normalized) * speed;
        lastAttacker = other;
        TakeDamage((speed - minImpactSpeed) * other.mass / massDivisor, point, impact);
        lastAttacker = null;
    }

    public void TakeDamage(float amount, Vector3 point, Vector3 direction = default)
    {
        if (collapsed || amount <= 0f)
        {
            return;
        }

        lastHitPoint = point;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        if (CurrentHealth <= 0f)
        {
            Collapse(direction);
            return;
        }

        // Casa do Blender: abre um buraco onde bateu (pode desabar se faltar parede demais).
        if (TryGetComponent(out HouseBreakable house))
        {
            house.Hit(point, direction, amount / maxHealth, lastAttacker);
            SpawnChips(point, direction);
            if (house.Collapsed)
            {
                FinishHouseCollapse();
            }

            return;
        }

        // Feedback de dano: tremor, pedacinhos e "encardido" progressivo.
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
        }

        shakeRoutine = StartCoroutine(Shake(Mathf.Clamp01(amount / maxHealth)));
        SpawnChips(point, direction);
        ApplyDamageTint();
    }

    // Explosão (carro, barril): a casa do Blender é arrancada em pedaços para longe do centro da explosão.
    // Outras estruturas recebem dano normal.
    public void TakeBlast(float amount, Vector3 point, Vector3 center, float radius)
    {
        if (collapsed || amount <= 0f)
        {
            return;
        }

        if (!TryGetComponent(out HouseBreakable house))
        {
            TakeDamage(amount, point, point - center);
            return;
        }

        lastHitPoint = point;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        house.Blast(center, radius, amount / maxHealth, CurrentHealth <= 0f);
        if (house.Collapsed)
        {
            FinishHouseCollapse();
        }
    }

    [ContextMenu("Teste: desmoronar agora")]
    private void DebugCollapse()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Este teste só funciona durante o Play.");
            return;
        }

        TakeDamage(CurrentHealth, transform.position, Vector3.zero);
    }

    private IEnumerator Shake(float strength)
    {
        float duration = 0.3f;
        float magnitude = Mathf.Lerp(0.05f, 0.18f, strength);
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float fade = 1f - t / duration;
            transform.localPosition = restPosition + Random.insideUnitSphere * magnitude * fade;
            yield return null;
        }

        transform.localPosition = restPosition;
        shakeRoutine = null;
    }

    private void ApplyDamageTint()
    {
        float damage = 1f - CurrentHealth / maxHealth;
        Color grime = new Color(0.25f, 0.22f, 0.2f, 1f);
        for (int i = 0; i < partRenderers.Count; i++)
        {
            if (partRenderers[i] != null)
            {
                partRenderers[i].material.color = Color.Lerp(originalColors[i], grime, damage * 0.45f);
            }
        }
    }

    private void SpawnChips(Vector3 point, Vector3 direction)
    {
        Material material = DebrisMaterial(partRenderers.Count > 0 && partRenderers[0] != null ? partRenderers[0].sharedMaterial : null, false);
        GameFeel.StructureHit(point, material != null ? material.color : Color.gray);

        for (int i = 0; i < 3; i++)
        {
            Vector3 velocity = -direction.normalized * Random.Range(2f, 5f) + Random.insideUnitSphere * 3f + Vector3.up * 2f;
            SpawnFragment(material, point + Random.insideUnitSphere * 0.5f, Random.rotation, hasDebrisColors ? ShardMesh() : cubeMesh,
                Vector3.one * Random.Range(0.3f, 0.6f), velocity);
        }
    }

    // ---------- Desmoronamento ----------

    private void Collapse(Vector3 hitDirection)
    {
        collapsed = true;
        if (shakeRoutine != null)
        {
            StopCoroutine(shakeRoutine);
        }

        transform.localPosition = restPosition;

        // Casa do Blender: desaba a versão pré-fraturada (paredes, depois o telhado).
        if (TryGetComponent(out HouseBreakable house))
        {
            house.Collapse(lastHitPoint != Vector3.zero ? lastHitPoint : transform.position, hitDirection, lastAttacker);
            FinishHouseCollapse();
            return;
        }

        // Os colisores somem na hora (as casas retrô têm caixas de colisão sem desenho, que ficariam
        // como paredes invisíveis; e os cacos não podem nascer empurrados por elas).
        foreach (Collider part in GetComponentsInChildren<Collider>())
        {
            part.enabled = false;
            if (part.GetComponent<Renderer>() == null)
            {
                Destroy(part.gameObject);
            }
        }

        Bounds bounds = ComputeBounds();
        Vector3 hitPoint = lastHitPoint != Vector3.zero ? lastHitPoint : bounds.center;
        bool shattered = false;

        bool prefractured = false;
        Material wallMaterial = null;
        Material secondMaterial = null;
        List<GameObject> originals = new List<GameObject>();

        // Casa com níveis de detalhe (LODGroup): só a versão de perto vira blocos; as outras somem.
        HashSet<Renderer> fragmentOnly = null;
        if (TryGetComponent(out LODGroup lodGroup))
        {
            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length > 0)
            {
                fragmentOnly = new HashSet<Renderer>(lods[0].renderers);
            }

            Destroy(lodGroup);
        }

        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            Material material = DebrisMaterial(part.sharedMaterial, false);
            if (wallMaterial == null)
            {
                wallMaterial = material;
            }
            else if (secondMaterial == null && material != wallMaterial)
            {
                secondMaterial = material;
            }

            if (!prefractured && (fragmentOnly == null || fragmentOnly.Contains(part)))
            {
                // Malha de verdade (casas retrô): quebra em cacos da própria casa. Senão, blocos.
                if (MeshShatter.Shatter(part, hitPoint, hitDirection, shatterPieces, 0.35f, transform))
                {
                    shattered = true;
                }
                else
                {
                    Fragment(part, bounds.center, hitDirection);
                }
            }

            originals.Add(part.gameObject);
        }

        foreach (GameObject original in originals)
        {
            Destroy(original);
        }


        if (!shattered)
        {
            BuildRubble(bounds, wallMaterial, secondMaterial != null ? secondMaterial : wallMaterial);
        }

        GameFeel.StructureCollapse(bounds.center, wallMaterial != null ? wallMaterial.color : Color.gray, bounds.size.magnitude);
        WantedSystem.ReportDestruction(bounds.center);
        StartCoroutine(UpdateNavMeshNextFrame());
    }

    // Quebra uma parte (caixa) em blocos menores, alinhados com ela.
    private void Fragment(Renderer part, Vector3 center, Vector3 hitDirection)
    {
        Transform piece = part.transform;
        bool isBox = piece.TryGetComponent(out BoxCollider _) || piece.TryGetComponent(out MeshFilter filter) && filter.sharedMesh == cubeMesh;
        Vector3 size = isBox ? piece.lossyScale : part.bounds.size;
        Quaternion rotation = isBox ? piece.rotation : Quaternion.identity;
        Vector3 origin = isBox ? piece.position : part.bounds.center;

        int nx = Divisions(size.x);
        int ny = Divisions(size.y);
        int nz = Divisions(size.z);
        Vector3 chunk = new Vector3(size.x / nx, size.y / ny, size.z / nz);

        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                for (int k = 0; k < nz; k++)
                {
                    Vector3 local = new Vector3(
                        ((i + 0.5f) / nx - 0.5f) * size.x,
                        ((j + 0.5f) / ny - 0.5f) * size.y,
                        ((k + 0.5f) / nz - 0.5f) * size.z);
                    Vector3 position = origin + rotation * local;

                    // Explode um pouco para fora do centro da casa, mais o empurrão do golpe.
                    Vector3 outward = position - center;
                    outward.y = 0f;
                    Vector3 velocity = outward.normalized * Random.Range(0.5f, 2.5f) + Vector3.up * Random.Range(0f, 0.6f) +
                                       hitDirection * 0.15f;

                    SpawnFragment(DebrisMaterial(part.sharedMaterial, Random.value < 0.25f), position, rotation, chunk * Random.Range(0.88f, 0.98f), velocity);
                }
            }
        }
    }

    private int Divisions(float length)
    {
        return Mathf.Clamp(Mathf.RoundToInt(length / fragmentSize), 1, maxDivisionsPerAxis);
    }

    private static void SpawnFragment(Material material, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 velocity) =>
        SpawnFragment(material, position, rotation, cubeMesh, scale, velocity);

    // Lasca irregular (casas retrô soltam lascas pontudas no golpe, não cubinhos).
    private static Mesh shardMesh;

    private static Mesh ShardMesh()
    {
        if (shardMesh != null)
        {
            return shardMesh;
        }

        Vector3[] c =
        {
            new Vector3(-0.5f, -0.3f, -0.4f), new Vector3(0.5f, -0.25f, -0.2f), new Vector3(0.05f, -0.3f, 0.5f),
            new Vector3(0.1f, 0.45f, 0.05f), new Vector3(-0.2f, 0.1f, -0.5f),
        };
        int[][] faces = { new[] { 0, 2, 1 }, new[] { 0, 1, 4 }, new[] { 1, 3, 4 }, new[] { 1, 2, 3 }, new[] { 2, 0, 3 }, new[] { 0, 4, 3 } };
        List<Vector3> v = new List<Vector3>();
        List<int> t = new List<int>();
        Vector3 middle = Vector3.zero;
        foreach (Vector3 p in c) middle += p;
        middle /= c.Length;
        foreach (int[] f in faces)
        {
            Vector3 a = c[f[0]], b = c[f[1]], d = c[f[2]];
            bool outward = Vector3.Dot(Vector3.Cross(b - a, d - a), (a + b + d) / 3f - middle) >= 0f;
            v.Add(a);
            v.Add(outward ? b : d);
            v.Add(outward ? d : b);
            t.Add(v.Count - 3);
            t.Add(v.Count - 2);
            t.Add(v.Count - 1);
        }

        shardMesh = new Mesh { name = "Lasca" };
        shardMesh.SetVertices(v);
        shardMesh.SetTriangles(t, 0);
        shardMesh.RecalculateNormals();
        shardMesh.RecalculateBounds();
        return shardMesh;
    }

    private static void SpawnFragment(Material material, Vector3 position, Quaternion rotation, Mesh shape, Vector3 scale, Vector3 velocity)
    {
        GameObject go = new GameObject("Fragmento");
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = shape;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        go.AddComponent<BoxCollider>();

        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = Mathf.Clamp(scale.x * scale.y * scale.z * 25f, 5f, 200f);
        // Blocos nascem encostados/sobrepostos; sem este limite a física os "dispara" para cima ao separá-los.
        body.maxDepenetrationVelocity = 1.5f;
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 3f;

        go.AddComponent<CollapsingFragment>();
    }

    // Monte baixo de pedaços escurecidos no lugar da casa.
    private void BuildRubble(Bounds bounds, Material mainMaterial, Material secondMaterial)
    {
        Transform rubble = new GameObject("Escombros").transform;
        rubble.SetParent(transform, true);

        Material darkMain = Darkened(mainMaterial);
        Material darkSecond = Darkened(secondMaterial);
        float ground = bounds.min.y;

        for (int i = 0; i < rubblePieces; i++)
        {
            Vector3 size = new Vector3(
                bounds.size.x * Random.Range(0.25f, 0.5f),
                Random.Range(0.3f, 1.1f),
                bounds.size.z * Random.Range(0.25f, 0.5f));
            Vector3 position = new Vector3(
                bounds.center.x + Random.Range(-0.3f, 0.3f) * bounds.size.x,
                ground + size.y * 0.5f - 0.15f,
                bounds.center.z + Random.Range(-0.3f, 0.3f) * bounds.size.z);
            Quaternion rotation = Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f));

            GameObject piece = new GameObject("Escombro");
            piece.transform.SetParent(rubble, true);
            piece.transform.SetPositionAndRotation(position, rotation);
            piece.transform.localScale = size;
            piece.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
            piece.AddComponent<MeshRenderer>().sharedMaterial = i % 3 == 0 ? darkSecond : darkMain;
            piece.AddComponent<BoxCollider>();
        }
    }

    private Material Darkened(Material source)
    {
        if (source == null)
        {
            return null;
        }

        Material copy = new Material(source);
        copy.color = Color.Lerp(source.color, new Color(0.2f, 0.18f, 0.17f, 1f), rubbleDarkness);
        return copy;
    }

    private Bounds ComputeBounds()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            if (first)
            {
                bounds = part.bounds;
                first = false;
            }
            else
            {
                bounds.Encapsulate(part.bounds);
            }
        }

        return bounds;
    }

    // Atualiza o NavMesh depois que as partes originais sumirem (elas somem no fim do quadro).
    private IEnumerator UpdateNavMeshNextFrame()
    {
        yield return null;

        // Mundo em blocos (Missão 16): um NavMesh só para os blocos carregados.
        if (WorldNavMesh.Instance != null)
        {
            WorldNavMesh.Instance.RequestRebuild();
            yield break;
        }

        NavMeshSurface surface = GetComponentInParent<NavMeshSurface>();
        if (surface != null && surface.navMeshData != null)
        {
            surface.UpdateNavMesh(surface.navMeshData);
        }
    }
}
