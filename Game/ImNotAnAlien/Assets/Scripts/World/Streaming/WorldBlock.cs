using UnityEngine;

// Um bloco do mundo (Missão 16). Fica na raiz do bloco, no canto sudoeste dele.
// - "Estatico": chão, ruas, rio, casas, árvores, postes. Entra no NavMesh.
// - "Dinamico": caixas, lixeiras, contêineres, carros estacionados (tudo com física).
// - "Gatilhos" (blocos montados por código): água do rio/lago. Fica fora do NavMesh.
// Quando o bloco é descarregado, tudo dele some; ao voltar, volta novo (decisão D23).
// Missão 18: guarda também a receita (tipo e lados). Nos blocos fixos (prefabs do Vilarejo) é ela
// que diz ao sorteio o que há em cada lado.
[DisallowMultipleComponent]
public sealed class WorldBlock : MonoBehaviour
{
    [SerializeField] private Vector2Int coord;
    [SerializeField] private Transform staticRoot;
    [SerializeField] private Transform dynamicRoot;
    [SerializeField] private bool hasSpec;
    [SerializeField] private BlockSpec spec;

    public Vector2Int Coord => coord;
    public Transform StaticRoot => staticRoot;
    public Transform DynamicRoot => dynamicRoot;
    public bool HasSpec => hasSpec;
    public BlockSpec Spec => spec;

    public void Setup(Vector2Int blockCoord, Transform staticPart, Transform dynamicPart)
    {
        coord = blockCoord;
        staticRoot = staticPart;
        dynamicRoot = dynamicPart;
    }

    public void SetCoord(Vector2Int blockCoord)
    {
        coord = blockCoord;
    }

    public void SetSpec(BlockSpec blockSpec)
    {
        spec = blockSpec;
        hasSpec = true;
    }
}
