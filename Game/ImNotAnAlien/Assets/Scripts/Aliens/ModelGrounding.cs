using System.Collections;
using UnityEngine;

// Assenta um modelo animado no chão (Missão 20): espera a animação de parado começar, acha o
// ponto mais baixo da malha JÁ ANIMADA e desce/sobe o modelo para esse ponto ficar no pé do objeto.
// Sem isso, robôs cuja pose de repouso é diferente da pose do arquivo ficam flutuando.
public static class ModelGrounding
{
    public static IEnumerator Settle(Transform root, Transform visual, float delay = 0.35f)
    {
        yield return new WaitForSeconds(delay);
        if (root == null || visual == null)
        {
            yield break;
        }

        float lowest = float.MaxValue;
        Mesh baked = new Mesh();
        foreach (SkinnedMeshRenderer skinned in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skinned.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            Transform t = skinned.transform;
            for (int i = 0; i < vertices.Length; i += 3)
            {
                Vector3 world = t.position + t.rotation * vertices[i];
                lowest = Mathf.Min(lowest, root.InverseTransformPoint(world).y);
            }
        }

        Object.Destroy(baked);
        if (lowest == float.MaxValue)
        {
            yield break;
        }

        // O objeto raiz está na altura do chão (NavMesh): o ponto mais baixo vai para lá.
        visual.localPosition -= Vector3.up * (lowest - 0.02f);
    }
}
