using System.Collections.Generic;
using UnityEngine;

// Malhas, texturas e materiais criados só para um bloco (o chão com relevo, Missão 20).
// Quando o bloco é descarregado, eles são apagados junto — senão a memória cresceria a cada bloco.
[DisallowMultipleComponent]
public sealed class BlockAssets : MonoBehaviour
{
    private readonly List<Object> owned = new List<Object>();

    public void Add(Object asset)
    {
        if (asset != null)
        {
            owned.Add(asset);
        }
    }

    private void OnDestroy()
    {
        foreach (Object asset in owned)
        {
            if (asset != null)
            {
                Destroy(asset);
            }
        }

        owned.Clear();
    }
}
