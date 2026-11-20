using UnityEngine;

// Um pedaço de destruição reaproveitável (etapa 3 do reaproveitamento, 18/11): caco de casa/prédio
// ou lasca que voa no golpe. Já nasce com tudo que um pedaço precisa (malha, desenho, colisão, física,
// queda e repasse de batida); quem usa só troca a malha, o tamanho e a força.
// Quando o pedaço afunda e some, volta para a caixa (DebrisPool) em vez de ser destruído.
[DisallowMultipleComponent]
public sealed class PooledDebris : MonoBehaviour
{
    public MeshFilter Filter { get; private set; }
    public MeshRenderer Renderer { get; private set; }
    public BoxCollider Box { get; private set; }
    public Rigidbody Body { get; private set; }
    public CollapsingFragment Fragment { get; private set; }
    public StructurePart Part { get; private set; }
    // 20/11: colisão com a forma exata do caco enquanto ele está de pé na casa (parado). A caixa de antes
    // cobria o espaço vazio de paredes curvas e diagonais = "parede invisível". Ao ser solto, volta para a caixa.
    public MeshCollider Shape { get; private set; }

    // Guardado na caixa (desligado).
    public bool InBox { get; set; }

    public static PooledDebris Create(Transform parent)
    {
        GameObject go = new GameObject("Caco");
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        PooledDebris debris = go.AddComponent<PooledDebris>();
        debris.Filter = go.AddComponent<MeshFilter>();
        debris.Renderer = go.AddComponent<MeshRenderer>();
        debris.Box = go.AddComponent<BoxCollider>();
        debris.Body = go.AddComponent<Rigidbody>();
        debris.Body.isKinematic = true;
        debris.Fragment = go.AddComponent<CollapsingFragment>();
        debris.Fragment.enabled = false;
        debris.Part = go.AddComponent<StructurePart>();
        debris.Shape = go.AddComponent<MeshCollider>(); // depois da caixa: GetComponent<Collider>() continua achando a caixa
        debris.Shape.enabled = false;
        return debris;
    }

    // Deixa pronto para um uso novo: tudo que o uso anterior pode ter mudado volta ao padrão.
    public void ResetForUse(string objectName, Mesh mesh, Material material, Vector3 scale)
    {
        gameObject.name = objectName;
        transform.localScale = scale;
        Filter.sharedMesh = mesh;
        Renderer.sharedMaterial = material;
        Renderer.enabled = true;
        Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        Box.enabled = true;
        Box.center = Vector3.zero;
        Box.size = Vector3.one;
        Shape.enabled = false;
        Shape.sharedMesh = null;
        Fragment.enabled = false;
        Part.Bind(null);
        Body.isKinematic = true;
        Body.interpolation = RigidbodyInterpolation.None;
        Body.maxDepenetrationVelocity = 1.5f; // pedaços nascem encostados: sem isso a física os "dispara"
    }
}
