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
}
