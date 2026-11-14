using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Horizonte curvo do planeta (Missão 19).
// Os materiais do jogo usam o shader "ImNotAnAlien/Curved Lit" (cópia do URP Lit): cada vértice desce
// conforme a distância do jogador (queda = distância² / (2 × raio)), e o chão parece cair ao longe,
// como num planetinha. Só muda o desenho: a física e o NavMesh continuam planos.
// - Este componente passa a posição do jogador e a força da curva para os shaders a cada quadro.
// - Peças compridas feitas de cubo (chão de 100 m, ruas, calçadas, rio) têm só 8 vértices e não
//   entortariam direito: ao carregar um bloco, elas trocam o cubo por um cubo "picado" em pedaços de ~5 m.
// - F7 (com o modo teste F8 ligado) liga e desliga a curva, para comparar.
// O WorldStreamer coloca este componente sozinho no modo planeta. Os materiais são trocados pela
// ferramenta ImNotAnAlien > Horizonte Curvo (Missao 19).
[DisallowMultipleComponent]
public sealed class CurvedHorizon : MonoBehaviour
{
    public const string ShaderName = "ImNotAnAlien/Curved Lit";
    private static readonly int OriginId = Shader.PropertyToID("_CurveOrigin");
    private static readonly int StrengthId = Shader.PropertyToID("_CurveStrength");

    [Tooltip("Raio do 'planetinha' que o olho vê (m). Menor = curva mais forte. O planeta de verdade teria ~2000 m. " +
             "O autor escolheu 100000 (bem sutil) em 10/11/2026.")]
    [SerializeField, Min(100f)] private float visualRadius = 100000f;
    [SerializeField] private bool curveOn = true;
    [Tooltip("Peças maiores que isto (m) são picadas para entortar direito.")]
    [SerializeField, Min(1f)] private float pieceSize = 5f;

    private static readonly Dictionary<(int, int, int), Mesh> SubdividedCubes = new Dictionary<(int, int, int), Mesh>();
    private static float cellSize = 5f;

    private Transform player;

    public static CurvedHorizon Instance { get; private set; }

    // Shader para materiais criados no jogo (cai no URP Lit se a cópia curva não existir).
    public static Shader LitShader()
    {
        Shader curved = Shader.Find(ShaderName);
        return curved != null ? curved : Shader.Find("Universal Render Pipeline/Lit");
    }

    private void Awake()
    {
        Instance = this;
        cellSize = pieceSize;
    }

    private void Start()
    {
        PlayerHealth health = PlayerHealth.Instance;
        player = health != null ? health.transform : null;
        if (Shader.Find(ShaderName) == null)
        {
            Debug.LogWarning("Horizonte curvo: shader 'ImNotAnAlien/Curved Lit' não encontrado (falta Assets/Shaders/CurvedLit.shader).");
        }
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(StrengthId, 0f); // o editor e as outras cenas voltam a ver tudo plano
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void LateUpdate()
    {
        WorldStreamer streamer = WorldStreamer.Instance;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f7Key.wasPressedThisFrame && streamer != null && streamer.TestMode)
        {
            curveOn = !curveOn;
            Debug.Log(curveOn ? "Horizonte curvo LIGADO." : "Horizonte curvo desligado (teste).");
        }

        if (player != null)
        {
            Shader.SetGlobalVector(OriginId, player.position);
        }

        Shader.SetGlobalFloat(StrengthId, curveOn ? 1f / (2f * visualRadius) : 0f);
    }

    // ---------- Peças compridas ----------

    // Troca o cubo das peças compridas do bloco por um cubo picado (mesma forma, mais vértices).
    public static void PrepareBlock(GameObject root)
    {
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || mesh.name != "Cube" || mesh.vertexCount != 24)
            {
                continue; // só o cubo embutido do Unity
            }

            Vector3 size = filter.transform.lossyScale;
            int nx = Segments(Mathf.Abs(size.x));
            int ny = Segments(Mathf.Abs(size.y));
            int nz = Segments(Mathf.Abs(size.z));
            if (nx == 1 && ny == 1 && nz == 1)
            {
                continue;
            }

            filter.sharedMesh = SubdividedCube(nx, ny, nz);
        }
    }

    private static int Segments(float length) => Mathf.Clamp(Mathf.CeilToInt(length / cellSize), 1, 40);

    private static Mesh SubdividedCube(int nx, int ny, int nz)
    {
        if (SubdividedCubes.TryGetValue((nx, ny, nz), out Mesh cached) && cached != null)
        {
            return cached;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        Vector3 h = Vector3.one * 0.5f;

        Face(new Vector3(-h.x, h.y, -h.z), Vector3.right, Vector3.forward, nx, nz, Vector3.up, vertices, normals, uvs, triangles);
        Face(new Vector3(-h.x, -h.y, -h.z), Vector3.right, Vector3.forward, nx, nz, Vector3.down, vertices, normals, uvs, triangles);
        Face(new Vector3(-h.x, -h.y, -h.z), Vector3.right, Vector3.up, nx, ny, Vector3.back, vertices, normals, uvs, triangles);
        Face(new Vector3(-h.x, -h.y, h.z), Vector3.right, Vector3.up, nx, ny, Vector3.forward, vertices, normals, uvs, triangles);
        Face(new Vector3(-h.x, -h.y, -h.z), Vector3.forward, Vector3.up, nz, ny, Vector3.left, vertices, normals, uvs, triangles);
        Face(new Vector3(h.x, -h.y, -h.z), Vector3.forward, Vector3.up, nz, ny, Vector3.right, vertices, normals, uvs, triangles);

        Mesh mesh = new Mesh { name = $"Cubo_Picado_{nx}x{ny}x{nz}" };
        if (vertices.Count > 65000)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        SubdividedCubes[(nx, ny, nz)] = mesh;
        return mesh;
    }

    // Uma face do cubo (lado de 1), em grade nu × nv, virada para "normal".
    private static void Face(Vector3 corner, Vector3 u, Vector3 v, int nu, int nv, Vector3 normal,
        List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
    {
        int start = vertices.Count;
        for (int j = 0; j <= nv; j++)
        {
            for (int i = 0; i <= nu; i++)
            {
                float a = (float)i / nu;
                float b = (float)j / nv;
                vertices.Add(corner + u * a + v * b);
                normals.Add(normal);
                uvs.Add(new Vector2(a, b));
            }
        }

        int row = nu + 1;
        for (int j = 0; j < nv; j++)
        {
            for (int i = 0; i < nu; i++)
            {
                int p0 = start + j * row + i;
                int p1 = p0 + 1;
                int p2 = p0 + row;
                int p3 = p2 + 1;
                AddTriangle(vertices, triangles, normal, p0, p2, p3);
                AddTriangle(vertices, triangles, normal, p0, p3, p1);
            }
        }
    }

    // Triângulo virado para fora (a ordem dos vértices decide qual lado aparece).
    private static void AddTriangle(List<Vector3> vertices, List<int> triangles, Vector3 normal, int a, int b, int c)
    {
        Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
        if (Vector3.Dot(cross, normal) < 0f)
        {
            (b, c) = (c, b);
        }

        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }
}
