using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Natureza desenhada "em lote" (instancing, 20/11, pedido do autor).
// Antes: cada árvore, arbusto, pedra, flor e tufo de grama de modelo era um pedido de desenho separado
// (a capital chegou a 5.000 pedidos por quadro). Agora, quando o bloco termina de montar, este componente
// junta as cópias iguais (mesma malha + mesmo material) e desenha todas de uma vez com
// Graphics.RenderMeshInstanced. Os objetos continuam existindo (colisão, posição, árvore que tapa o
// caminho...), só o desenho deles é desligado.
// Distância de desenho (item 3): plantinhas pequenas somem de longe, arbustos e pedras médias um pouco
// mais longe, árvores grandes sempre.
// Só junta materiais com o shader "ImNotAnAlien/Curved Lit" (que aceita instancing); o resto fica como era.
public sealed class NatureInstancer : MonoBehaviour
{
    private const string InstancedShader = "ImNotAnAlien/Curved Lit";
    private const int MaxPerCall = 1000;

    // Distância máxima de desenho pelo tamanho do objeto (maior medida, em metros).
    private const float SmallSize = 1.2f, SmallDistance = 70f;   // flores, cogumelos, tufos, pedrinhas
    private const float MediumSize = 4f, MediumDistance = 160f;  // arbustos, pedras médias
    private const float LargeDistance = 2000f;                    // árvores e pedras grandes: sempre

    private sealed class Batch
    {
        public Mesh mesh;
        public int submesh;
        public Material material;
        public ShadowCastingMode shadows;
        public bool receiveShadows;
        public int layer;
        public float maxDistance;
        public readonly List<Matrix4x4> local = new List<Matrix4x4>(); // relativo à raiz do bloco
        public Matrix4x4[] world;
        public Bounds localBounds;
        public bool hasBounds;
    }

    private readonly List<Batch> batches = new List<Batch>();
    private Vector3 builtAt = new Vector3(float.NaN, 0f, 0f);
    private static readonly List<MeshRenderer> scratch = new List<MeshRenderer>(512);
    private static readonly Dictionary<(Mesh, int, Material, ShadowCastingMode, bool, int, float), Batch> lookup =
        new Dictionary<(Mesh, int, Material, ShadowCastingMode, bool, int, float), Batch>();

    public int InstanceCount { get; private set; }
    public int BatchCount => batches.Count;

    // Junta a natureza do bloco (os modelos colocados por BlockBuilderNature ficam dentro de "Modelo").
    public static void Setup(GameObject block)
    {
        if (block == null)
        {
            return;
        }

        NatureInstancer instancer = block.GetComponent<NatureInstancer>();
        if (instancer == null)
        {
            instancer = block.AddComponent<NatureInstancer>();
        }

        instancer.Collect();
    }

    private void Collect()
    {
        batches.Clear();
        lookup.Clear();
        InstanceCount = 0;
        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        GetComponentsInChildren(true, scratch);
        foreach (MeshRenderer renderer in scratch)
        {
            if (!renderer.enabled || !IsNature(renderer.transform) || renderer.GetComponentInParent<LODGroup>() != null)
            {
                continue;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            Material[] materials = renderer.sharedMaterials;
            if (mesh == null || materials.Length == 0 || !AllInstanceable(materials, mesh.subMeshCount))
            {
                continue;
            }

            Matrix4x4 local = toLocal * renderer.localToWorldMatrix;
            Vector3 scale = renderer.bounds.size;
            float size = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            float maxDistance = size < SmallSize ? SmallDistance : size < MediumSize ? MediumDistance : LargeDistance;
            Bounds bounds = TransformBounds(local, mesh.bounds);

            for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
            {
                Material material = materials[sub];
                material.enableInstancing = true;
                var key = (mesh, sub, material, renderer.shadowCastingMode, renderer.receiveShadows, renderer.gameObject.layer, maxDistance);
                if (!lookup.TryGetValue(key, out Batch batch))
                {
                    batch = new Batch
                    {
                        mesh = mesh,
                        submesh = sub,
                        material = material,
                        shadows = renderer.shadowCastingMode,
                        receiveShadows = renderer.receiveShadows,
                        layer = renderer.gameObject.layer,
                        maxDistance = maxDistance,
                    };
                    lookup[key] = batch;
                    batches.Add(batch);
                }

                batch.local.Add(local);
                if (batch.hasBounds)
                {
                    batch.localBounds.Encapsulate(bounds);
                }
                else
                {
                    batch.localBounds = bounds;
                    batch.hasBounds = true;
                }
            }

            renderer.enabled = false; // o desenho agora é do instancer
            InstanceCount++;
        }

        scratch.Clear();
        lookup.Clear();
        foreach (Batch batch in batches)
        {
            batch.world = new Matrix4x4[batch.local.Count];
        }

        builtAt = new Vector3(float.NaN, 0f, 0f); // calcula as posições no primeiro desenho
    }

    private static bool IsNature(Transform t)
    {
        for (int i = 0; i < 4 && t != null; i++)
        {
            if (t.name == "Modelo")
            {
                return true;
            }

            t = t.parent;
        }

        return false;
    }

    private static bool AllInstanceable(Material[] materials, int submeshes)
    {
        for (int i = 0; i < materials.Length && i < submeshes; i++)
        {
            if (materials[i] == null || materials[i].shader == null || materials[i].shader.name != InstancedShader)
            {
                return false;
            }
        }

        return true;
    }

    private static Bounds TransformBounds(Matrix4x4 m, Bounds b)
    {
        Vector3 center = m.MultiplyPoint3x4(b.center);
        Vector3 e = b.extents;
        Vector3 x = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
        Vector3 y = m.MultiplyVector(new Vector3(0f, e.y, 0f));
        Vector3 z = m.MultiplyVector(new Vector3(0f, 0f, e.z));
        Vector3 extents = new Vector3(
            Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
            Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
            Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        return new Bounds(center, extents * 2f);
    }

    private void LateUpdate()
    {
        if (batches.Count == 0)
        {
            return;
        }

        // O mundo é recentralizado de vez em quando (WorldOrigin): refaz as posições quando o bloco anda.
        Vector3 here = transform.position;
        if (here != builtAt)
        {
            builtAt = here;
            Matrix4x4 toWorld = transform.localToWorldMatrix;
            foreach (Batch batch in batches)
            {
                for (int i = 0; i < batch.local.Count; i++)
                {
                    batch.world[i] = toWorld * batch.local[i];
                }
            }
        }

        Camera cam = Camera.main;
        Vector3 eye = cam != null ? cam.transform.position : here;
        foreach (Batch batch in batches)
        {
            Bounds worldBounds = batch.localBounds;
            worldBounds.center += here;
            worldBounds.Expand(2f);
            if (worldBounds.SqrDistance(eye) > batch.maxDistance * batch.maxDistance)
            {
                continue; // longe demais para este tamanho de objeto
            }

            RenderParams rp = new RenderParams(batch.material)
            {
                worldBounds = worldBounds,
                shadowCastingMode = batch.shadows,
                receiveShadows = batch.receiveShadows,
                layer = batch.layer,
            };

            int total = batch.world.Length;
            for (int start = 0; start < total; start += MaxPerCall)
            {
                Graphics.RenderMeshInstanced(rp, batch.mesh, batch.submesh, batch.world, Mathf.Min(MaxPerCall, total - start), start);
            }
        }
    }
}
