using UnityEngine;

// Fica em cada parte de uma estrutura destrutível (paredes, telhado...).
// Repassa as batidas para o DestructibleStructure da casa.
// Adicionado automaticamente; não é preciso colocar à mão.
// 18/11: os pedaços reaproveitados (PooledDebris) dizem para quem repassar com Bind (null = ninguém).
[DisallowMultipleComponent]
public sealed class StructurePart : MonoBehaviour
{
    private DestructibleStructure structure;
    private bool bound;

    private void Awake()
    {
        if (!bound)
        {
            structure = GetComponentInParent<DestructibleStructure>();
        }
    }

    public void Bind(DestructibleStructure owner)
    {
        bound = true;
        structure = owner;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (structure != null)
        {
            structure.OnPartHit(collision);
        }
    }
}
