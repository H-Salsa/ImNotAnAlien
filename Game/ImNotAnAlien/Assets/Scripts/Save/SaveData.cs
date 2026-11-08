using System;
using System.Collections.Generic;
using UnityEngine;

// O que fica guardado num jogo salvo (Missão 17). Vira um arquivo JSON por espaço de jogo.
// Não guarda casas, carros nem alienígenas soltos (decisão D23): só o progresso.
[Serializable]
public sealed class SaveData
{
    public const int CurrentVersion = 1;

    [Serializable]
    public sealed class EvolutionSave
    {
        public string id;
        public int level;
    }

    [Serializable]
    public sealed class SectorSave
    {
        public string name;
        public int consumed;
        public bool cleared;
    }

    [Serializable]
    public sealed class RegionSave
    {
        public string name;
        public int seed;
        public int quota;
        public int consumed;
        public bool cleared;
        public List<SectorSave> sectors = new List<SectorSave>();
    }

    public int version = CurrentVersion;
    public string savedAt;            // data e hora (ISO 8601)
    public float playTimeSeconds;

    // Semente do mundo: sorteada no Novo jogo. Vai decidir o encaixe dos blocos (Missão 18).
    public int worldSeed;

    public int dna;
    public List<EvolutionSave> evolutions = new List<EvolutionSave>();

    // Onde o jogador estava (posição LÓGICA, fixa no mapa).
    public bool hasPlayerPosition;
    public Vector3 playerPosition;

    public List<RegionSave> regions = new List<RegionSave>();

    public static SaveData NewGame()
    {
        return new SaveData { worldSeed = UnityEngine.Random.Range(1, int.MaxValue) };
    }

    public RegionSave FindRegion(string name)
    {
        return regions.Find(r => r.name == name);
    }

    public RegionSave GetOrAddRegion(string name)
    {
        RegionSave region = FindRegion(name);
        if (region == null)
        {
            // Semente da região: derivada da semente do mundo (sempre a mesma neste jogo).
            region = new RegionSave { name = name, seed = DeriveSeed(worldSeed, name) };
            regions.Add(region);
        }

        return region;
    }

    public int TotalConsumed()
    {
        int total = 0;
        foreach (RegionSave region in regions) total += region.consumed;
        return total;
    }

    public int TotalQuota()
    {
        int total = 0;
        foreach (RegionSave region in regions) total += region.quota;
        return total;
    }

    private static int DeriveSeed(int seed, string name)
    {
        unchecked
        {
            int hash = seed * 31 + 17;
            foreach (char c in name)
            {
                hash = hash * 31 + c;
            }

            return hash & int.MaxValue;
        }
    }
}
