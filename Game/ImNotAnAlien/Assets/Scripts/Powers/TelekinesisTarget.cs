using UnityEngine;

// Marca um objeto como "pode ser levantado pela telecinese".
// Precisa de um Rigidbody (física). Alienígenas não precisam deste componente:
// a telecinese já reconhece qualquer objeto com AlienHealth.
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class TelekinesisTarget : MonoBehaviour
{
}
