using System.Collections.Generic;
using UnityEngine;

// Quebra uma malha de verdade em pedaços irregulares (Missão 20 — casas retrô).
// Em vez de blocos quadrados, cada pedaço é um caco da própria casa: parede com a faixa colorida,
// pedaço do telhado arredondado, janela com a sala, porta... com as mesmas cores (mesmo material).
// Como funciona:
// 1. Os triângulos grandes são divididos (até ~1,3 m) para a quebra não seguir as arestas da malha.
// 2. Pontos sorteados no volume da casa (mais perto de onde levou o golpe) viram "centros de caco";
//    cada triângulo vai para o centro mais próximo, com um tremido para a borda sair irregular.
// 3. Cada caco ganha espessura (face de dentro), física, uma caixa de colisão e cai.
// 4. Os cacos mais baixos ficam como escombros (não afundam); os outros afundam e somem
//    (CollapsingFragment), para não acumular.
public static class MeshShatter
{
    private const float MaxEdge = 1.3f;
    private const int MaxTriangles = 24000;
    private const float Thickness = 0.14f;

    private struct Vert
    {
        public Vector3 p;
        public Vector3 n;
        public Vector2 uv;

        public static Vert Mid(Vert a, Vert b) => new Vert { p = (a.p + b.p) * 0.5f, n = (a.n + b.n).normalized, uv = (a.uv + b.uv) * 0.5f };
    }

    // Verdadeiro se quebrou (malha legível com triângulos). "rubbleParent" recebe os cacos que ficam.
    public static bool Shatter(Renderer renderer, Vector3 hitPoint, Vector3 hitDirection, int pieces, float rubbleShare, Transform rubbleParent)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable || mesh.triangles.Length < 60)
        {
            return false;
        }

        // 1. Triângulos em coordenadas do mundo (espelhado: inverte a ordem).
        Matrix4x4 m = renderer.transform.localToWorldMatrix;
        bool flip = m.determinant < 0f;
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        int[] tris = mesh.triangles;

        List<Vert[]> work = new List<Vert[]>(tris.Length / 3 * 3);
        Stack<Vert[]> stack = new Stack<Vert[]>();
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vert a = World(m, vertices, normals, uvs, tris[i]);
            Vert b = World(m, vertices, normals, uvs, tris[i + 1]);
            Vert c = World(m, vertices, normals, uvs, tris[i + 2]);
            stack.Push(flip ? new[] { a, c, b } : new[] { a, b, c });
            while (stack.Count > 0)
            {
                Vert[] t = stack.Pop();
                float ab = (t[0].p - t[1].p).sqrMagnitude, bc = (t[1].p - t[2].p).sqrMagnitude, ca = (t[2].p - t[0].p).sqrMagnitude;
                float longest = Mathf.Max(ab, Mathf.Max(bc, ca));
                if (longest <= MaxEdge * MaxEdge || work.Count + stack.Count > MaxTriangles)
                {
                    work.Add(t);
                    continue;
                }

                // Divide a aresta mais comprida ao meio (mantém a ordem dos vértices).
                if (longest == ab)
                {
                    Vert mid = Vert.Mid(t[0], t[1]);
                    stack.Push(new[] { t[0], mid, t[2] });
                    stack.Push(new[] { mid, t[1], t[2] });
                }
                else if (longest == bc)
                {
                    Vert mid = Vert.Mid(t[1], t[2]);
                    stack.Push(new[] { t[0], t[1], mid });
                    stack.Push(new[] { t[0], mid, t[2] });
                }
                else
                {
                    Vert mid = Vert.Mid(t[2], t[0]);
                    stack.Push(new[] { t[0], t[1], mid });
                    stack.Push(new[] { mid, t[1], t[2] });
                }
            }
        }

        // 2. Centros dos cacos: metade espalhada, metade perto do golpe (cacos menores ali).
        Bounds bounds = renderer.bounds;
        Vector3[] seeds = new Vector3[Mathf.Max(4, pieces)];
        for (int s = 0; s < seeds.Length; s++)
        {
            Vector3 r = new Vector3(Random.value, Random.value, Random.value);
            Vector3 p = bounds.min + Vector3.Scale(r, bounds.size);
            if (s % 2 == 1)
            {
                p = Vector3.Lerp(p, hitPoint, 0.55f);
            }

            seeds[s] = p;
        }

        List<Vert[]>[] groups = new List<Vert[]>[seeds.Length];
        for (int g = 0; g < groups.Length; g++)
        {
            groups[g] = new List<Vert[]>();
        }

        foreach (Vert[] t in work)
        {
            Vector3 center = (t[0].p + t[1].p + t[2].p) / 3f;
            Vector3 probe = center + Jitter(center) * 0.45f; // borda irregular
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int s = 0; s < seeds.Length; s++)
            {
                Vector3 d = probe - seeds[s];
                d.y *= 1.3f; // cacos um pouco mais "deitados"
                float distance = d.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = s;
                }
            }

            groups[best].Add(t);
        }

        // 3. Um objeto por caco. Os mais baixos ficam como escombros.
        List<(GameObject go, float y)> made = new List<(GameObject, float)>();
        Material material = renderer.sharedMaterial;
        foreach (List<Vert[]> group in groups)
        {
            if (group.Count < 2)
            {
                continue;
            }

            GameObject piece = BuildPiece(group, material, bounds.center, hitDirection);
            made.Add((piece, piece.transform.position.y));
        }

        made.Sort((a, b) => a.y.CompareTo(b.y));
        int keep = Mathf.RoundToInt(made.Count * rubbleShare);
        for (int i = 0; i < made.Count; i++)
        {
            CollapsingFragment fragment = made[i].go.AddComponent<CollapsingFragment>();
            if (i < keep)
            {
                fragment.StayAsRubble(rubbleParent);
            }
        }

        return true;
    }

    private static Vert World(Matrix4x4 m, Vector3[] v, Vector3[] n, Vector2[] uv, int i) => new Vert
    {
        p = m.MultiplyPoint3x4(v[i]),
        n = n.Length > i ? m.MultiplyVector(n[i]).normalized : Vector3.up,
        uv = uv.Length > i ? uv[i] : Vector2.zero,
    };

    // Tremido fixo por posição (mesmo ponto = mesmo tremido, então os cacos vizinhos encaixam).
    private static Vector3 Jitter(Vector3 p)
    {
        float x = Mathf.PerlinNoise(p.x * 0.9f + 11.3f, p.z * 0.9f + p.y * 0.5f) - 0.5f;
        float y = Mathf.PerlinNoise(p.z * 0.9f + 47.1f, p.y * 0.9f + p.x * 0.5f) - 0.5f;
        float z = Mathf.PerlinNoise(p.y * 0.9f + 83.7f, p.x * 0.9f + p.z * 0.5f) - 0.5f;
        return new Vector3(x, y, z) * 2f;
    }

    private static GameObject BuildPiece(List<Vert[]> group, Material material, Vector3 houseCenter, Vector3 hitDirection)
    {
        Vector3 pivot = Vector3.zero;
        foreach (Vert[] t in group)
        {
            pivot += t[0].p + t[1].p + t[2].p;
        }

        pivot /= group.Count * 3;

        int count = group.Count * 6;
        Vector3[] positions = new Vector3[count];
        Vector3[] normals = new Vector3[count];
        Vector2[] uvs = new Vector2[count];
        int[] triangles = new int[count];
        int k = 0;
        foreach (Vert[] t in group)
        {
            // Face de fora.
            for (int j = 0; j < 3; j++)
            {
                positions[k] = t[j].p - pivot;
                normals[k] = t[j].n;
                uvs[k] = t[j].uv;
                triangles[k] = k;
                k++;
            }

            // Face de dentro (espessura): um pouco para trás, virada ao contrário.
            for (int j = 2; j >= 0; j--)
            {
                positions[k] = t[j].p - t[j].n * Thickness - pivot;
                normals[k] = -t[j].n;
                uvs[k] = t[j].uv;
                triangles[k] = k;
                k++;
            }
        }

        Mesh mesh = new Mesh { name = "Caco" };
        if (count > 65000)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.vertices = positions;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        GameObject go = new GameObject("Fragmento");
        go.transform.position = pivot;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<OwnedMesh>().mesh = mesh;

        BoxCollider box = go.AddComponent<BoxCollider>();
        Bounds b = mesh.bounds;
        box.center = b.center;
        box.size = Vector3.Max(b.size, Vector3.one * 0.2f);

        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = Mathf.Clamp(b.size.x * b.size.y * b.size.z * 15f, 15f, 400f);
        body.maxDepenetrationVelocity = 1.5f; // cacos nascem encostados: sem isso a física os "dispara"
        Vector3 outward = pivot - houseCenter;
        outward.y = 0f;
        body.linearVelocity = outward.normalized * Random.Range(0.6f, 2.8f) + Vector3.up * Random.Range(0f, 1f) + hitDirection * 0.15f;
        body.angularVelocity = Random.insideUnitSphere * 1.6f;
        return go;
    }
}

// Apaga a malha criada para o caco quando ele some.
public sealed class OwnedMesh : MonoBehaviour
{
    public Mesh mesh;

    private void OnDestroy()
    {
        if (mesh != null)
        {
            Destroy(mesh);
        }
    }
}
