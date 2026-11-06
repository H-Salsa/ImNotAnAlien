using UnityEngine;

// Fica em cada parte de uma estrutura destrutível (paredes, telhado...).
// Repassa as batidas para o DestructibleStructure da casa.
// Adicionado automaticamente; não é preciso colocar à mão.
[DisallowMultipleComponent]
public sealed class StructurePart : MonoBehaviour
{
    private DestructibleStructure structure;

    private void Awake()
    {
        structure = GetComponentInParent<DestructibleStructure>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (structure != null)
        {
            structure.OnPartHit(collision);
        }
    }
}
