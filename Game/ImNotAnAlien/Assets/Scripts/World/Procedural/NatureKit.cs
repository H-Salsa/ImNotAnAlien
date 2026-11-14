using UnityEngine;

// Natureza do planeta (Missão 20): Stylized Nature MegaKit + Textured Stylized Trees (Quaternius, CC0).
// Criado pela ferramenta "Preparar Natureza (Missao 20)" em Assets/Art/Nature/NatureKit.asset e
// ligado ao Kit de Blocos (campo "nature"). O BlockBuilder escolhe os modelos pelo bioma.
// As folhas e a grama usam texturas claras: a cor vem do bioma (Floresta Chiclete = rosa,
// Tundra = azul...). Os materiais "pintáveis" estão em "tintable".
[CreateAssetMenu(menuName = "ImNotAnAlien/Kit de Natureza", fileName = "NatureKit")]
public sealed class NatureKit : ScriptableObject
{
    [Header("Árvores")]
    public GameObject[] commonTrees;   // MegaKit CommonTree (copa redonda grande)
    public GameObject[] roundTrees;    // Trees Tree_ (copa fofa)
    public GameObject[] birches;       // Trees Birch_ (tronco branco)
    public GameObject[] pines;         // MegaKit Pine_ + Trees Pine_
    public GameObject[] twistedTrees;  // MegaKit TwistedTree (retorcida)
    public GameObject[] deadTrees;     // MegaKit DeadTree + Trees DeadTree
    public GameObject[] deadBirches;   // Trees DeadBirch_

    [Header("Plantas baixas")]
    public GameObject[] bushes;        // Bush_Common
    public GameObject[] flowerBushes;  // Bush_Common_Flowers
    public GameObject[] grasses;       // Grass_*
    public GameObject[] flowers;       // Flower_* e Petal_*
    public GameObject[] ferns;         // Fern, Plant, Clover
    public GameObject[] mushrooms;

    [Header("Pedras")]
    public GameObject[] rocks;         // Rock_Medium
    public GameObject[] pebbles;       // Pebble_*
    public GameObject[] rockPaths;     // RockPath_* (pedras de caminho)
    [Tooltip("Material das pedras de deserto (Dunas, Cratera, Estepe, Antenas).")]
    public Material desertRock;
    [Tooltip("Material normal das pedras (trocado pelo de deserto nos biomas secos).")]
    public Material normalRock;

    [Header("Grama ao vento")]
    [Tooltip("Material da grama de triângulos (shader ImNotAnAlien/Grass Wind). Vazio = grama de modelos.")]
    public Material grassBlades;

    [Header("Cor do bioma")]
    [Tooltip("Materiais de folha e grama que recebem a cor do bioma.")]
    public Material[] tintable;
}
