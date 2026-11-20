using System.Collections.Generic;
using UnityEngine;

// Cidades do planeta (20/11, decisões em claude/cidades.md).
// Cada cidade é um quadrado de blocos FIXOS no sorteio do planeta (como o Vilarejo e as bases):
// o lugar é sorteado uma vez por jogo dentro da região dela (o mais perto do meio da região que caiba
// inteiro, longe do Vilarejo e das bases), e os blocos são montados em anéis:
//   centro  = praça e prédios altos
//   meio    = prédios, depois prédios e bairros misturados
//   borda   = bairros, alguns galpões e parques
// Todas as ruas de dentro se ligam (grade) e algumas saem para as estradas de fora.
// O chão da cidade fica plano (PlanetTerrain.KindOf) e os rios desviam dela (entram como fixos antes
// do GreatLake.Plan).
//
// Capital (8 × 8), 2 cidades grandes (5 × 5) e 3 vilarejos médios (3 × 3). Nomes provisórios.
// Vilarejo: praça no meio e bairros em volta (sem prédios altos).
public static class Cities
{
    public sealed class City
    {
        public string name;
        public string title;      // "Capital do planeta", "Cidade grande"...
        public int region;
        public int size;          // lado em blocos
        public Vector2Int corner; // bloco sudoeste (do planeta)

        public Vector2Int Center => new Vector2Int(corner.x + size / 2, corner.y + size / 2);
        public Vector2 CenterPosition => new Vector2(corner.x + size * 0.5f, corner.y + size * 0.5f); // em blocos
    }

    private struct Recipe
    {
        public string name;
        public string title;
        public Planet.Biome biome;
        public int size;
        public bool village;   // vilarejo: só praça, bairros, galpões e parques
        public float sheds;    // vilarejo: chance de galpão (celeiros, depósitos)
    }

    private static readonly Recipe[] Recipes =
    {
        new Recipe { name = "Nublópolis", title = "Capital do planeta", biome = Planet.Biome.Cidade, size = 8 },
        new Recipe { name = "Porto Tentáculo", title = "Cidade grande", biome = Planet.Biome.Baia, size = 5 },
        new Recipe { name = "Vegas Lilás", title = "Cidade grande", biome = Planet.Biome.Dunas, size = 5 },
        new Recipe { name = "Palafitas do Zumbido", title = "Vilarejo", biome = Planet.Biome.Pantano, size = 3, village = true, sheds = 0.1f },
        new Recipe { name = "Vila Gorda", title = "Vilarejo", biome = Planet.Biome.Planicie, size = 3, village = true, sheds = 0.3f },
        new Recipe { name = "Vila Gelada", title = "Vilarejo", biome = Planet.Biome.Tundra, size = 3, village = true, sheds = 0.15f },
    };

    private const int Margin = 3; // blocos de folga até o Vilarejo, as bases e outras cidades

    private static readonly List<City> all = new List<City>();
    private static City[] map; // cidade de cada bloco do planeta (z * Width + x), ou null

    public static IReadOnlyList<City> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all.Clear();
        map = null;
    }

    // Escolhe o lugar de cada cidade e põe os blocos dela em "fixedSpecs". Chamar depois do
    // Planet.BuildRegions e ANTES do GreatLake.Plan e do sorteio.
    public static void Plan(int seed, Dictionary<Vector2Int, BlockSpec> fixedSpecs)
    {
        all.Clear();
        map = new City[Planet.Width * Planet.Height];
        foreach (Recipe recipe in Recipes)
        {
            int region = (int)recipe.biome; // a região N tem o bioma N
            if (!FindPlace(region, recipe.size, fixedSpecs, out Vector2Int corner))
            {
                Debug.LogWarning($"Cidades: não coube {recipe.name} ({recipe.size} × {recipe.size}) na região {Planet.RegionName(region)}.");
                continue;
            }

            City city = new City { name = recipe.name, title = recipe.title, region = region, size = recipe.size, corner = corner };
            all.Add(city);
            System.Random rng = new System.Random(seed * 31 + region * 7919 + 17);
            for (int j = 0; j < city.size; j++)
            {
                for (int i = 0; i < city.size; i++)
                {
                    Vector2Int coord = Planet.Wrap(new Vector2Int(corner.x + i, corner.y + j));
                    fixedSpecs[coord] = SpecFor(recipe, i, j, rng);
                    map[coord.y * Planet.Width + coord.x] = city;
                }
            }

            Debug.Log($"Cidades: {city.name} ({city.size} × {city.size} blocos) em {corner} — {Planet.RegionName(region)}.");
        }
    }

    // A cidade em que o bloco está (null = nenhuma).
    public static City At(Vector2Int planetCoord)
    {
        if (map == null || !Planet.InsideLatitude(planetCoord.y))
        {
            return null;
        }

        Vector2Int c = Planet.Wrap(planetCoord);
        return map[c.y * Planet.Width + c.x];
    }

    public static bool IsCity(int x, int z) => At(new Vector2Int(x, z)) != null;

    // Onde o jogador chega na cidade (teste F10): no cruzamento de um bloco do centro, em posição LÓGICA.
    public static Vector3 SpawnLogical(City city, Vector3 near)
    {
        Vector2Int block = new Vector2Int(city.corner.x + city.size / 2, city.corner.y + city.size / 2 - 1);
        return Planet.CenterNear(Planet.Wrap(block), near) + new Vector3(0f, 0.3f, 0f);
    }

    // ---------- Lugar ----------

    // O canto que deixa o meio da cidade mais perto do meio da região, com a cidade inteira dentro
    // da região e longe dos blocos fixos (Vilarejo, bases, outras cidades).
    private static bool FindPlace(int region, int size, Dictionary<Vector2Int, BlockSpec> fixedSpecs, out Vector2Int best)
    {
        best = default;
        Vector2 target = Planet.RegionLabelPosition(region);
        float bestScore = float.MaxValue;
        int reach = Planet.RegionSize; // procura até uma região de distância do meio
        int cx = Mathf.RoundToInt(target.x), cz = Mathf.RoundToInt(target.y);
        for (int z0 = cz - reach; z0 <= cz + reach; z0++)
        {
            if (z0 < 2 || z0 + size > Planet.Height - 2)
            {
                continue; // longe dos polos
            }

            for (int x0 = cx - reach; x0 <= cx + reach; x0++)
            {
                float dx = x0 + size * 0.5f - target.x;
                float dz = z0 + size * 0.5f - target.y;
                float score = dx * dx + dz * dz;
                if (score >= bestScore || !Fits(region, x0, z0, size, fixedSpecs))
                {
                    continue;
                }

                bestScore = score;
                best = Planet.Wrap(new Vector2Int(x0, z0));
            }
        }

        return bestScore < float.MaxValue;
    }

    private static bool Fits(int region, int x0, int z0, int size, Dictionary<Vector2Int, BlockSpec> fixedSpecs)
    {
        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size; i++)
            {
                Vector2Int c = Planet.Wrap(new Vector2Int(x0 + i, z0 + j));
                if (Planet.RegionAt(c) != region)
                {
                    return false;
                }
            }
        }

        for (int j = -Margin; j < size + Margin; j++)
        {
            for (int i = -Margin; i < size + Margin; i++)
            {
                Vector2Int c = Planet.Wrap(new Vector2Int(x0 + i, z0 + j));
                if (c.y < 0 || c.y >= Planet.Height)
                {
                    continue; // folga passando do polo: não tem bloco ali
                }

                if (fixedSpecs.ContainsKey(c) || map[c.y * Planet.Width + c.x] != null)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ---------- Blocos ----------

    // Lados: dentro da cidade sempre rua (grade). Na borda, rua só nas saídas (2 por lado), o resto mato.
    private static BlockSpec SpecFor(Recipe recipe, int i, int j, System.Random rng)
    {
        int size = recipe.size;
        Socket north = j == size - 1 ? Exit(size, i) : Socket.Rua;
        Socket south = j == 0 ? Exit(size, i) : Socket.Rua;
        Socket east = i == size - 1 ? Exit(size, j) : Socket.Rua;
        Socket west = i == 0 ? Exit(size, j) : Socket.Rua;

        // Anel: 0 = centro ... size/2 - 1 = borda.
        float half = (size - 1) * 0.5f;
        int ring = Mathf.FloorToInt(Mathf.Max(Mathf.Abs(i - half), Mathf.Abs(j - half)));
        int last = size / 2 - 1;
        double roll = rng.NextDouble();
        BlockType type;
        string variant = null;
        if (recipe.village)
        {
            // Vilarejo: praça no meio (precisa das 4 ruas), o resto bairros, alguns galpões e parques.
            bool center = i == size / 2 && j == size / 2;
            type = center ? BlockType.Praca
                : roll < recipe.sheds ? BlockType.Galpoes
                : roll < recipe.sheds + 0.15 ? BlockType.Parque
                : BlockType.Bairro;
        }
        else if (ring == 0)
        {
            // Praça central (duas, na diagonal) e prédios altos.
            bool plaza = (i + j) % 2 == 0;
            type = plaza ? BlockType.Praca : BlockType.Predios;
            variant = plaza ? null : "alto";
        }
        else if (ring == 1)
        {
            type = BlockType.Predios;
            variant = "alto";
        }
        else if (ring < last)
        {
            type = roll < 0.6 ? BlockType.Predios : roll < 0.9 ? BlockType.Bairro : BlockType.Parque;
        }
        else
        {
            type = roll < 0.72 ? BlockType.Bairro : roll < 0.86 ? BlockType.Galpoes : BlockType.Parque;
        }

        BlockSpec spec = BlockSpec.Make(type, north, east, south, west, variant);
        if (type == BlockType.Praca && spec.Count(Socket.Rua) != 4)
        {
            spec.type = BlockType.Predios; // praça precisa das 4 ruas
        }

        return spec; // semente 0: o sorteio dá a semente do bloco
    }

    // Saídas da cidade: 2 por lado, no 2º bloco de cada ponta (8 × 8: blocos 2 e 5).
    private static Socket Exit(int size, int k)
    {
        int a = Mathf.Max(1, size / 4);
        return k == a || k == size - 1 - a ? Socket.Rua : Socket.Mato;
    }
}
