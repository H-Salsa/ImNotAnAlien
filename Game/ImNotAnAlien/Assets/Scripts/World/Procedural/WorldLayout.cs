using System;
using System.Collections.Generic;
using UnityEngine;

// O que pode haver em cada lado de um bloco (Missão 18). Ruas e rio passam SEMPRE no meio do lado:
// rua com 10 m de largura, rio com 25 m. Assim qualquer peça encaixa em qualquer outra com o mesmo lado.
public enum Socket
{
    Mato,
    Rua,
    Rio,
}

// Tipos de bloco. Os do Vilarejo (Esquina, Bairro, Parque, Rio, Galpoes) e os 6 novos.
public enum BlockType
{
    Esquina,   // a esquina feita à mão (só no Vilarejo)
    Bairro,    // casas na beira das ruas
    Parque,    // gramado, lago pequeno, caminhos, bancos, parquinho
    Rio,       // rio atravessando (com ponte se houver rua dos lados)
    Galpoes,   // galpões, contêineres e caixas
    Floresta,  // novo: mata fechada
    Fazenda,   // novo: plantação, celeiro, silo, fardos de feno
    Praca,     // novo: cruzamento com praça, chafariz e lojas
    Predios,   // novo: prédios altos
    Lago,      // novo: lago grande com píer
    Campo,     // novo: campo aberto com arbustos e pedras
}

// A "receita" de um bloco: tipo, o que há em cada lado e a semente própria dele.
// Lados: 0 = norte (+Z), 1 = leste (+X), 2 = sul (-Z), 3 = oeste (-X).
[Serializable]
public struct BlockSpec
{
    public BlockType type;
    public Socket north;
    public Socket east;
    public Socket south;
    public Socket west;
    public int seed;
    [Tooltip("Detalhe opcional (ex.: \"cais\" no rio do Vilarejo).")]
    public string variant;

    public Socket Get(int side)
    {
        switch (side)
        {
            case 0: return north;
            case 1: return east;
            case 2: return south;
            default: return west;
        }
    }

    public void Set(int side, Socket socket)
    {
        switch (side)
        {
            case 0: north = socket; break;
            case 1: east = socket; break;
            case 2: south = socket; break;
            default: west = socket; break;
        }
    }

    public int Count(Socket socket)
    {
        int n = 0;
        for (int s = 0; s < 4; s++)
        {
            if (Get(s) == socket) n++;
        }

        return n;
    }

    public static BlockSpec Make(BlockType type, Socket n, Socket e, Socket s, Socket w, string variant = null)
    {
        return new BlockSpec { type = type, north = n, east = e, south = s, west = w, variant = variant };
    }

    public override string ToString() => $"{type} [N {north}, L {east}, S {south}, O {west}]";
}

// Sorteio do mapa (Missão 18, decisão D21).
// Com a mesma semente sai sempre o mesmo mapa. Blocos fixos (o Vilarejo) entram prontos e o resto
// é sorteado em volta, um bloco por vez, sempre respeitando os lados dos vizinhos ("colapso de
// função de onda"). Lados virados para fora do mapa só podem ter mato ou rio (nada de rua batendo
// na parede).
public sealed class WorldLayout
{
    private static readonly Vector2Int[] Directions = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

    private sealed class Variant
    {
        public BlockSpec spec;
        public float weight;
    }

    public int Width { get; }
    public int Height { get; }
    public Vector2 Origin { get; }      // canto sudoeste do bloco (0, 0), coordenadas LÓGICAS X/Z
    public float BlockSize { get; }
    public int Seed { get; }

    private readonly BlockSpec[,] cells;

    public WorldLayout(int width, int height, Vector2 origin, float blockSize, int seed, BlockSpec[,] cells)
    {
        Width = width;
        Height = height;
        Origin = origin;
        BlockSize = blockSize;
        Seed = seed;
        this.cells = cells;
    }

    public bool Contains(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

    public BlockSpec Get(Vector2Int c) => cells[c.x, c.y];

    public Vector3 CenterLogical(Vector2Int c) =>
        new Vector3(Origin.x + (c.x + 0.5f) * BlockSize, 0.05f, Origin.y + (c.y + 0.5f) * BlockSize);

    // ---------- Pesos e regras de cada tipo ----------

    private static float TypeWeight(BlockType type)
    {
        switch (type)
        {
            case BlockType.Bairro: return 4f;
            case BlockType.Floresta: return 3f;
            case BlockType.Fazenda: return 2f;
            case BlockType.Campo: return 2f;
            case BlockType.Parque: return 1.5f;
            case BlockType.Predios: return 1.5f;
            case BlockType.Galpoes: return 1f;
            case BlockType.Praca: return 1f;
            case BlockType.Lago: return 1f;
            case BlockType.Rio: return 0.6f;
            default: return 0f; // Esquina: só fixa
        }
    }

    // O tipo aceita esses quatro lados?
    public static bool Accepts(BlockType type, BlockSpec s)
    {
        int roads = s.Count(Socket.Rua);
        int rivers = s.Count(Socket.Rio);
        switch (type)
        {
            case BlockType.Rio:
                // Rio atravessando de um lado ao oposto; os outros dois lados iguais (mato, ou rua = ponte).
                bool northSouth = s.north == Socket.Rio && s.south == Socket.Rio && s.east == s.west && s.east != Socket.Rio;
                bool eastWest = s.east == Socket.Rio && s.west == Socket.Rio && s.north == s.south && s.north != Socket.Rio;
                return northSouth || eastWest;
            case BlockType.Floresta:
                return roads == 0 && rivers == 0;
            case BlockType.Bairro:
            case BlockType.Galpoes:
                return rivers == 0 && roads >= 1;
            case BlockType.Predios:
                return rivers == 0 && roads >= 2;
            case BlockType.Praca:
                return roads == 4;
            case BlockType.Fazenda:
            case BlockType.Lago:
                return rivers == 0 && roads <= 1;
            case BlockType.Campo:
                return rivers == 0 && roads <= 2;
            case BlockType.Parque:
                return rivers == 0;
            default:
                return false;
        }
    }

    // Preferência pelo desenho das ruas: retas e cruzamentos mais que ruas sem saída.
    private static float PatternBias(BlockSpec s)
    {
        int roads = s.Count(Socket.Rua);
        bool straight = roads == 2 && (s.north == s.south || s.east == s.west);
        switch (roads)
        {
            case 0: return 1f;
            case 1: return 0.5f;
            case 2: return straight ? 2f : 1f;
            case 3: return 1.3f;
            default: return 1f;
        }
    }

    private static List<Variant> AllVariants()
    {
        List<Variant> variants = new List<Variant>();
        foreach (BlockType type in (BlockType[])Enum.GetValues(typeof(BlockType)))
        {
            float weight = TypeWeight(type);
            if (weight <= 0f)
            {
                continue;
            }

            List<BlockSpec> accepted = new List<BlockSpec>();
            for (int code = 0; code < 81; code++)
            {
                int c = code;
                BlockSpec spec = new BlockSpec { type = type };
                for (int side = 0; side < 4; side++)
                {
                    spec.Set(side, (Socket)(c % 3));
                    c /= 3;
                }

                if (Accepts(type, spec))
                {
                    accepted.Add(spec);
                }
            }

            float total = 0f;
            foreach (BlockSpec spec in accepted) total += PatternBias(spec);
            foreach (BlockSpec spec in accepted)
            {
                variants.Add(new Variant { spec = spec, weight = weight * PatternBias(spec) / Mathf.Max(0.001f, total) });
            }
        }

        return variants;
    }

    // ---------- Sorteio ----------

    public static WorldLayout Generate(int width, int height, Vector2 origin, float blockSize, int seed,
        IDictionary<Vector2Int, BlockSpec> fixedCells)
    {
        System.Random random = new System.Random(seed);
        List<Variant> pool = AllVariants();

        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (TrySolve(width, height, pool, fixedCells, random, out BlockSpec[,] cells))
            {
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        cells[x, y].seed = CellSeed(seed, x, y);
                    }
                }

                if (attempt > 0)
                {
                    Debug.Log($"Sorteio do mapa: resolvido na tentativa {attempt + 1}.");
                }

                return new WorldLayout(width, height, origin, blockSize, seed, cells);
            }
        }

        Debug.LogWarning("Sorteio do mapa: não encontrei um encaixe; usando floresta nos blocos que faltaram.");
        BlockSpec[,] fallback = new BlockSpec[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                fallback[x, y] = fixedCells != null && fixedCells.TryGetValue(c, out BlockSpec f) ? f : BlockSpec.Make(BlockType.Floresta, Socket.Mato, Socket.Mato, Socket.Mato, Socket.Mato);
                fallback[x, y].seed = CellSeed(seed, x, y);
            }
        }

        return new WorldLayout(width, height, origin, blockSize, seed, fallback);
    }

    private static bool TrySolve(int width, int height, List<Variant> pool, IDictionary<Vector2Int, BlockSpec> fixedCells,
        System.Random random, out BlockSpec[,] result)
    {
        result = null;
        List<Variant>[,] domains = new List<Variant>[width, height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                List<Variant> domain;
                if (fixedCells != null && fixedCells.TryGetValue(c, out BlockSpec fixedSpec))
                {
                    domain = new List<Variant> { new Variant { spec = fixedSpec, weight = 1f } };
                }
                else
                {
                    domain = new List<Variant>();
                    foreach (Variant v in pool)
                    {
                        if (FitsBorder(v.spec, c, width, height))
                        {
                            domain.Add(v);
                        }
                    }
                }

                domains[x, y] = domain;
                queue.Enqueue(c);
            }
        }

        if (!Propagate(domains, queue, width, height))
        {
            return false;
        }

        while (true)
        {
            // Bloco ainda indefinido com menos opções (empate: sorteado).
            Vector2Int pick = new Vector2Int(-1, -1);
            int best = int.MaxValue;
            int ties = 0;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int count = domains[x, y].Count;
                    if (count <= 1)
                    {
                        continue;
                    }

                    if (count < best)
                    {
                        best = count;
                        pick = new Vector2Int(x, y);
                        ties = 1;
                    }
                    else if (count == best && random.Next(++ties) == 0)
                    {
                        pick = new Vector2Int(x, y);
                    }
                }
            }

            if (pick.x < 0)
            {
                break; // todos definidos
            }

            List<Variant> domain = domains[pick.x, pick.y];
            float total = 0f;
            foreach (Variant v in domain) total += v.weight;
            double roll = random.NextDouble() * total;
            Variant chosen = domain[domain.Count - 1];
            foreach (Variant v in domain)
            {
                roll -= v.weight;
                if (roll <= 0)
                {
                    chosen = v;
                    break;
                }
            }

            domains[pick.x, pick.y] = new List<Variant> { chosen };
            queue.Enqueue(pick);
            if (!Propagate(domains, queue, width, height))
            {
                return false;
            }
        }

        result = new BlockSpec[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                result[x, y] = domains[x, y][0].spec;
            }
        }

        return true;
    }

    private static bool FitsBorder(BlockSpec spec, Vector2Int c, int width, int height)
    {
        for (int side = 0; side < 4; side++)
        {
            Vector2Int n = c + Directions[side];
            bool outside = n.x < 0 || n.y < 0 || n.x >= width || n.y >= height;
            if (outside && spec.Get(side) == Socket.Rua)
            {
                return false;
            }
        }

        return true;
    }

    // Tira dos vizinhos as opções que não encaixam mais.
    private static bool Propagate(List<Variant>[,] domains, Queue<Vector2Int> queue, int width, int height)
    {
        bool[] allowed = new bool[3];
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            List<Variant> domain = domains[c.x, c.y];
            if (domain.Count == 0)
            {
                return false;
            }

            for (int side = 0; side < 4; side++)
            {
                Vector2Int n = c + Directions[side];
                if (n.x < 0 || n.y < 0 || n.x >= width || n.y >= height)
                {
                    continue;
                }

                allowed[0] = allowed[1] = allowed[2] = false;
                foreach (Variant v in domain)
                {
                    allowed[(int)v.spec.Get(side)] = true;
                }

                int opposite = (side + 2) % 4;
                List<Variant> other = domains[n.x, n.y];
                int removed = other.RemoveAll(v => !allowed[(int)v.spec.Get(opposite)]);
                if (other.Count == 0)
                {
                    return false;
                }

                if (removed > 0)
                {
                    queue.Enqueue(n);
                }
            }
        }

        return true;
    }

    private static int CellSeed(int seed, int x, int y)
    {
        unchecked
        {
            int h = seed;
            h = h * 73856093 ^ x * 19349663;
            h = h * 83492791 ^ y * 2654435;
            return h & int.MaxValue;
        }
    }

    // ---------- Rede de ruas (para o trânsito) ----------

    // Nós no centro de cada bloco com rua; ligações entre centros vizinhos ligados por rua.
    // Posições LÓGICAS.
    public void BuildRoadNetwork(List<Vector3> nodes, List<(int a, int b)> edges)
    {
        nodes.Clear();
        edges.Clear();
        Dictionary<Vector2Int, int> index = new Dictionary<Vector2Int, int>();
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                if (cells[x, y].Count(Socket.Rua) > 0)
                {
                    index[c] = nodes.Count;
                    nodes.Add(CenterLogical(c));
                }
            }
        }

        foreach (KeyValuePair<Vector2Int, int> pair in index)
        {
            BlockSpec spec = cells[pair.Key.x, pair.Key.y];
            for (int side = 0; side < 2; side++) // norte e leste (cada ligação uma vez)
            {
                Vector2Int n = pair.Key + Directions[side];
                if (spec.Get(side) == Socket.Rua && index.TryGetValue(n, out int other))
                {
                    edges.Add((pair.Value, other));
                }
            }
        }
    }
}
