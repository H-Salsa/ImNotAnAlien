using UnityEngine;

// Materiais e modelos usados para montar os blocos (Missão 18).
// Criado pela ferramenta "Gerar Mundo Sorteado (Missao 18)" em Assets/Art/BlockKit.asset.
// Mudar uma cor aqui (ou no material) muda todos os blocos novos.
[CreateAssetMenu(menuName = "ImNotAnAlien/Kit de Blocos", fileName = "BlockKit")]
public sealed class BlockKit : ScriptableObject
{
    [Header("Chão e ruas")]
    public Material grass;
    public Material asphalt;
    public Material laneMark;
    public Material sidewalk;
    public Material path;
    public Material sand;
    public Material soil;

    [Header("Água")]
    public Material water;
    public Material riverBed;

    [Header("Casas e prédios")]
    public Material[] houseWalls;
    public Material[] houseRoofs;
    public Material door;
    public Material window;
    public Material[] buildingColors;
    public Material[] awningColors;
    public Material warehouse;
    public Material warehouseRoof;
    public Material barn;
    public Material barnRoof;

    [Header("Natureza")]
    public Material trunk;
    public Material[] canopies;
    public Material bush;
    public Material rock;
    public Material crop;
    public Material flower;

    [Header("Objetos")]
    public Material wood;
    public Material bench;
    public Material fence;
    public Material lampPole;
    public Material lampLight;
    public Material crate;
    public Material bin;
    public Material hay;
    public Material[] containerColors;

    [Header("Carro estacionado")]
    [Tooltip("Carro_Civil.fbx (o mesmo do trânsito).")]
    public GameObject carModel;

    // Preenchidos pela ferramenta "Preparar Sci-Fi Kit" (Quaternius, CC0). Vazios = peças de blockout.
    [Header("Sci-Fi Kit (Quaternius)")]
    [Tooltip("Prop_SatelliteDish: antenas do Morro das Antenas e da Torre de Rádio.")]
    public GameObject satelliteDish;
    [Tooltip("Caixas pequenas (Prop_Crate, Prop_Crate_Tarp): calçadas e galpões.")]
    public GameObject[] crates;
    [Tooltip("Caixas grandes (Prop_Crate_Large, Prop_Crate_Tarp_Large): galpões.")]
    public GameObject[] largeCrates;
    [Tooltip("Barris (Prop_Barrel1, Prop_Barrel2_Closed, Prop_Barrel2_Open).")]
    public GameObject[] barrels;
    [Tooltip("Móveis das bases: mesas.")]
    public GameObject[] desks;
    [Tooltip("Móveis das bases: estantes.")]
    public GameObject[] shelves;
    public GameObject locker;
    public GameObject chair;
    public GameObject mug;

    [Header("Casas retrô (Missão 20)")]
    [Tooltip("Casas do bairro no estilo retrofuturista (RetroHouse). Desligado = casas de blockout.")]
    public bool retroHouses = true;
    [Tooltip("Casas modeladas no Blender (Assets/Art/Houses), preenchido pela ferramenta 'Preparar Casas'. Vazio = casas feitas por código.")]
    public GameObject[] houseModels;
    [Tooltip("Prédios modelados no Blender (Assets/Art/Buildings), preenchido pela ferramenta 'Preparar Casas'. Vazio = prédios de blockout.")]
    public GameObject[] buildingModels;

    [Header("Natureza (Missão 20)")]
    [Tooltip("Árvores, plantas e pedras do MegaKit + Trees (ferramenta 'Preparar Natureza'). Vazio = árvores de blockout.")]
    public NatureKit nature;
}
