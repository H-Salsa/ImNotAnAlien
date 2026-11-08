using UnityEngine;

// Um bloco do mundo (Missão 16). Fica na raiz do prefab do bloco, no canto sudoeste dele.
// - "Estatico": chão, ruas, rio, casas, árvores, postes. Entra no NavMesh.
// - "Dinamico": caixas, lixeiras, contêineres, carros estacionados (tudo com física).
// Quando o bloco é descarregado, tudo dele some; ao voltar, volta novo (decisão D23).
[DisallowMultipleComponent]
public sealed class WorldBlock : MonoBehaviour
{
    [SerializeField] private Vector2Int coord;
    [SerializeField] private Transform staticRoot;
    [SerializeField] private Transform dynamicRoot;

    public Vector2Int Coord => coord;
    public Transform StaticRoot => staticRoot;
    public Transform DynamicRoot => dynamicRoot;

    public void Setup(Vector2Int blockCoord, Transform staticPart, Transform dynamicPart)
    {
        coord = blockCoord;
        staticRoot = staticPart;
        dynamicRoot = dynamicPart;
    }
}
