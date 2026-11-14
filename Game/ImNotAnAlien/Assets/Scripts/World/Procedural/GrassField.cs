using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Grama de triângulos ao vento (Missão 20), um por bloco.
// O BlockBuilder guarda aqui o que precisa (onde não pode ter grama, a altura do chão, a cor do bioma)
// e a grama só é montada quando a câmera chega perto (~110 m) e é jogada fora quando se afasta (~160 m):
// assim só os blocos em volta do jogador gastam memória. O bloco é dividido em 4 × 4 pedaços de
// 25 m (um por quadro, para não travar), e cada pedaço é uma malha só (a placa de vídeo desenha
// milhares de folhas de uma vez). O balanço, o empurrão e o sumiço ao longe são do shader
// "ImNotAnAlien/Grass Wind".
public sealed class GrassField : MonoBehaviour
{
    private const float BlockSize = 100f;
    private const int Chunks = 4;
    private const float ChunkSize = BlockSize / Chunks;
    private const float BuildRadius = 110f; // até a borda do bloco (a grama some aos ~105 m)
    private const float KeepRadius = 160f;

    private static readonly int PusherId = Shader.PropertyToID("_GrassPusher");
    private static readonly int OffsetId = Shader.PropertyToID("_GrassWorldOffset");
    private static int globalsFrame = -1;

    private Material material;
    private bool[] blocked;      // 100 × 100 células de 1 m (ruas, casas, árvores, lagos)
    private Vector3[] grid;      // vértices da malha do chão (null = chão plano em 0)
    private int row;
    private float step;
    private float density;       // folhas por m²
    private float heightScale;
    private float minHeight;     // sem grama debaixo d'água
    private Color baseColor;
    private Color tipColor;
    private int seed;

    private Transform holder;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private int next = -1;       // próximo pedaço a montar (-1 = desmontado)

    public void Setup(Material grassMaterial, bool[] blockedCells, Mesh ground, float bladesPerSquareMeter, float bladeHeight,
        float lowestHeight, Color pe, Color ponta, int randomSeed)
    {
        material = grassMaterial;
        blocked = blockedCells;
        density = bladesPerSquareMeter;
        heightScale = bladeHeight;
        minHeight = lowestHeight;
        baseColor = pe;
        tipColor = ponta;
        seed = randomSeed;

        // Malha do chão do relevo (PlanetTerrain.BuildGround): grade quadrada de (n + 1)² vértices.
        if (ground != null && ground.name.StartsWith("Chao_"))
        {
            Vector3[] vertices = ground.vertices;
            int n = Mathf.RoundToInt(Mathf.Sqrt(vertices.Length));
            if (n * n == vertices.Length && n > 1)
            {
                grid = vertices;
                row = n;
                step = BlockSize / (n - 1);
            }
        }
    }

    private void Update()
    {
        UpdateGlobals();
        Camera cam = Camera.main;
        if (cam == null || material == null)
        {
            return;
        }

        Vector3 center = transform.TransformPoint(new Vector3(BlockSize / 2f, 0f, BlockSize / 2f));
        Vector3 eye = cam.transform.position;
        float distance = new Vector2(center.x - eye.x, center.z - eye.z).magnitude - BlockSize * 0.5f;

        if (next < 0 && distance < BuildRadius)
        {
            holder = new GameObject("Grama").transform;
            holder.SetParent(transform, false);
            next = 0;
        }

        if (next >= 0 && next < Chunks * Chunks)
        {
            BuildChunk(next++);
        }

        if (next >= 0 && distance > KeepRadius)
        {
            Clear();
        }
    }

    // Posição dos pés do protagonista e origem flutuante para o shader (uma vez por quadro).
    private static void UpdateGlobals()
    {
        if (globalsFrame == Time.frameCount)
        {
            return;
        }

        globalsFrame = Time.frameCount;
        PlayerHealth player = PlayerHealth.Instance;
        Shader.SetGlobalVector(PusherId, player != null ? (Vector4)player.transform.position + new Vector4(0f, 0f, 0f, 1f) : Vector4.zero);
        Shader.SetGlobalVector(OffsetId, WorldOrigin.Offset);
    }

    private void BuildChunk(int k)
    {
        int cx = k % Chunks;
        int cz = k / Chunks;
        System.Random rng = new System.Random(seed + k * 7919);
        int attempts = Mathf.RoundToInt(density * ChunkSize * ChunkSize);

        List<Vector3> vertices = new List<Vector3>(attempts * 3);
        List<Color32> colors = new List<Color32>(attempts * 3);
        List<Vector2> uvs = new List<Vector2>(attempts * 3);
        for (int i = 0; i < attempts; i++)
        {
            float x = (cx + (float)rng.NextDouble()) * ChunkSize;
            float z = (cz + (float)rng.NextDouble()) * ChunkSize;
            int cell = Mathf.Min((int)x, 99) + Mathf.Min((int)z, 99) * 100;
            if (blocked != null && blocked[cell])
            {
                continue;
            }

            float h = HeightAt(x, z);
            if (h < minHeight)
            {
                continue;
            }

            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float width = 0.08f + (float)rng.NextDouble() * 0.07f;
            float tall = (0.13f + (float)rng.NextDouble() * 0.18f) * heightScale; // 13–31 cm (metade da 1ª versão)
            Vector3 side = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (width * 0.5f);
            Vector3 lean = new Vector3(-side.z, 0f, side.x).normalized * ((float)rng.NextDouble() - 0.5f) * tall * 0.5f;
            Vector3 root = new Vector3(x, h - 0.03f, z);

            float shade = 0.82f + (float)rng.NextDouble() * 0.3f;
            Color32 pe = baseColor * shade;
            Color32 ponta = tipColor * shade;
            float phase = (float)rng.NextDouble();

            vertices.Add(root - side);
            vertices.Add(root + side);
            vertices.Add(root + lean + Vector3.up * tall);
            colors.Add(pe);
            colors.Add(pe);
            colors.Add(ponta);
            uvs.Add(new Vector2(phase, 0f));
            uvs.Add(new Vector2(phase, 0f));
            uvs.Add(new Vector2(phase, tall)); // altura da folha: o shader balança só a ponta
        }

        if (vertices.Count == 0)
        {
            return;
        }

        int[] triangles = new int[vertices.Count];
        for (int t = 0; t < triangles.Length; t++)
        {
            triangles[t] = t;
        }

        Mesh mesh = new Mesh { name = "Grama" };
        mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, false);
        mesh.RecalculateBounds();
        Bounds bounds = mesh.bounds;
        bounds.Expand(new Vector3(2f, 1f, 2f)); // a ponta sai um pouco do lugar com o vento
        mesh.bounds = bounds;
        mesh.UploadMeshData(true); // sem cópia na CPU
        meshes.Add(mesh);

        GameObject chunk = new GameObject("Grama_" + k);
        chunk.transform.SetParent(holder, false);
        chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;
    }

    // Mesma altura da malha do chão (mesmos triângulos), para a folha não flutuar nem afundar.
    private float HeightAt(float x, float z)
    {
        if (grid == null)
        {
            return 0f;
        }

        float fx = Mathf.Clamp(x / step, 0f, row - 1.001f);
        float fz = Mathf.Clamp(z / step, 0f, row - 1.001f);
        int i = (int)fx;
        int j = (int)fz;
        float tx = fx - i;
        float tz = fz - j;
        float a = grid[j * row + i].y;
        float b = grid[j * row + i + 1].y;
        float c = grid[(j + 1) * row + i].y;
        float d = grid[(j + 1) * row + i + 1].y;
        return tx + tz <= 1f
            ? a + (b - a) * tx + (c - a) * tz
            : d + (c - d) * (1f - tx) + (b - d) * (1f - tz);
    }

    private void Clear()
    {
        foreach (Mesh mesh in meshes)
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        meshes.Clear();
        if (holder != null)
        {
            Destroy(holder.gameObject);
            holder = null;
        }

        next = -1;
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in meshes)
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        meshes.Clear();
    }
}
