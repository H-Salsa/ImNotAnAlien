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

    private readonly List<Renderer> partRenderers = new List<Renderer>();
    private readonly List<Color> originalColors = new List<Color>();
    private Vector3 restPosition;
    private Coroutine shakeRoutine;
    private bool collapsed;

    public float MaxHealth => maxHealth;
    public float CurrentHealth { get; private set; }
    public bool IsCollapsed => collapsed;

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
        TakeDamage((speed - minImpactSpeed) * other.mass / massDivisor, point, collision.relativeVelocity);
    }

    public void TakeDamage(float amount, Vector3 point, Vector3 direction = default)
    {
        if (collapsed || amount <= 0f)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        if (CurrentHealth <= 0f)
        {
            Collapse(direction);
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
        Material material = partRenderers.Count > 0 && partRenderers[0] != null ? partRenderers[0].sharedMaterial : null;
        GameFeel.StructureHit(point, material != null ? material.color : Color.gray);

        for (int i = 0; i < 3; i++)
        {
            Vector3 velocity = -direction.normalized * Random.Range(2f, 5f) + Random.insideUnitSphere * 3f + Vector3.up * 2f;
            SpawnFragment(material, point + Random.insideUnitSphere * 0.5f, Random.rotation,
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

        Bounds bounds = ComputeBounds();
        Material wallMaterial = null;
        Material secondMaterial = null;
        List<GameObject> originals = new List<GameObject>();

        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            Material material = part.sharedMaterial;
            if (wallMaterial == null)
            {
                wallMaterial = material;
            }
            else if (secondMaterial == null && material != wallMaterial)
            {
                secondMaterial = material;
            }

            Fragment(part, bounds.center, hitDirection);
            originals.Add(part.gameObject);
        }

        foreach (GameObject original in originals)
        {
            Destroy(original);
        }

        BuildRubble(bounds, wallMaterial, secondMaterial != null ? secondMaterial : wallMaterial);
        GameFeel.StructureCollapse(bounds.center, wallMaterial != null ? wallMaterial.color : Color.gray, bounds.size.magnitude);
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

                    SpawnFragment(part.sharedMaterial, position, rotation, chunk * Random.Range(0.88f, 0.98f), velocity);
                }
            }
        }
    }

    private int Divisions(float length)
    {
        return Mathf.Clamp(Mathf.RoundToInt(length / fragmentSize), 1, maxDivisionsPerAxis);
    }

    private static void SpawnFragment(Material material, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 velocity)
    {
        GameObject go = new GameObject("Fragmento");
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
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
        NavMeshSurface surface = GetComponentInParent<NavMeshSurface>();
        if (surface != null && surface.navMeshData != null)
        {
            surface.UpdateNavMesh(surface.navMeshData);
        }
    }
}
