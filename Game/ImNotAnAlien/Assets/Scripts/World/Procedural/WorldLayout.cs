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
    Base,      // Missão 19: esconderijo do protagonista (só fixo)
    Polo,      // Missão 19: gelo dos polos, além da última linha do planeta (só montado pelo WorldStreamer)
    Lagoa,     // Missão 20: pedaço do Grande Lago (só fixo, ver GreatLake)
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

    // Planeta (Missão 19): o eixo X dá a volta (o bloco 128 é o bloco 0 de novo).
    public bool WrapsX { get; private set; }

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

    public bool Contains(Vector2Int c) => c.y >= 0 && c.y < Height && (WrapsX || c.x >= 0 && c.x < Width);

    public BlockSpec Get(Vector2Int c) => cells[WrapsX ? ((c.x % Width) + Width) % Width : c.x, c.y];

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
            case BlockType.Rio: return 0f; // Missão 20: rio agora é relevo (GreatLake); o bloco Rio só no Vilarejo
            default: return 0f; // Esquina e Base: só fixas
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
                if (northSouth || eastWest)
                {
                    return true;
                }

                // Missão 20: curva (dois lados vizinhos) ou nascente (um lado só); o resto é mato.
                // Assim o rio termina e faz curvas, em vez de dar a volta no planeta em linha reta.
                if (roads > 0)
                {
                    return false;
                }

                bool bend = rivers == 2 && s.north != s.south;
                return rivers == 1 || bend;
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
                return rivers == 0 && roads <= 1;
            case BlockType.Lago:
                return rivers == 0 && roads == 0; // Missão 20: lagoa natural no meio do mato (rua não termina na água)
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

    // ---------- Planeta (Missão 19) ----------

    // Sorteio do planeta inteiro (ex.: 128 × 128 = 16 mil blocos), rápido o bastante para o começo do jogo.
    // - X dá a volta; o norte e o sul dão nos polos: lado virado para o polo só pode ter mato ou rio.
    // - Cada bloco guarda as opções que ainda servem num conjunto de bits (128 opções no máximo).
    // - Os blocos são decididos em ordem (linha por linha), sempre tirando dos vizinhos o que não encaixa.
    //   Se algum bloco ficar sem opção, só um pedaço em volta dele é refeito (não o planeta todo).
    public static WorldLayout GeneratePlanet(int width, int height, Vector2 origin, float blockSize, int seed,
        IDictionary<Vector2Int, BlockSpec> fixedCells)
    {
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        System.Random random = new System.Random(seed);

        List<Variant> pool = AllVariants();
        // Blocos fixos que não estão no sorteio (Esquina, Base) entram como opções extras, só para eles.
        Dictionary<Vector2Int, int> fixedIndex = new Dictionary<Vector2Int, int>();
        if (fixedCells != null)
        {
            foreach (KeyValuePair<Vector2Int, BlockSpec> pair in fixedCells)
            {
                int found = pool.FindIndex(v => SameSpec(v.spec, pair.Value));
                if (found < 0)
                {
                    pool.Add(new Variant { spec = pair.Value, weight = 0f });
                    found = pool.Count - 1;
                }

                Vector2Int key = new Vector2Int(((pair.Key.x % width) + width) % width, pair.Key.y);
                fixedIndex[key] = found;
            }
        }

        if (pool.Count > 128)
        {
            Debug.LogError($"Sorteio do planeta: {pool.Count} opções de bloco; o máximo é 128.");
        }

        PlanetSolver solver = new PlanetSolver(width, height, pool, random);
        bool ok = solver.Solve(fixedIndex);

        BlockSpec[,] cells = new BlockSpec[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                int chosen = solver.Chosen(x, y);
                BlockSpec spec = chosen >= 0 ? pool[chosen].spec : BlockSpec.Make(BlockType.Floresta, Socket.Mato, Socket.Mato, Socket.Mato, Socket.Mato);
                if (fixedCells != null && fixedIndex.ContainsKey(c))
                {
                    // Mantém a receita original (variante, semente) dos blocos fixos.
                    foreach (KeyValuePair<Vector2Int, BlockSpec> pair in fixedCells)
                    {
                        if (((pair.Key.x % width) + width) % width == x && pair.Key.y == y)
                        {
                            spec = pair.Value;
                        }
                    }
                }

                if (spec.seed == 0 || !fixedIndex.ContainsKey(c))
                {
                    spec.seed = CellSeed(seed, x, y);
                }

                cells[x, y] = spec;
            }
        }

        watch.Stop();
        Debug.Log(ok
            ? $"Planeta sorteado: {width} × {height} blocos em {watch.ElapsedMilliseconds} ms ({solver.Repairs} consertos)."
            : $"Planeta sorteado com {solver.Failures} bloco(s) sem encaixe (viraram floresta), em {watch.ElapsedMilliseconds} ms.");

        return new WorldLayout(width, height, origin, blockSize, seed, cells) { WrapsX = true };
    }

    private static bool SameSpec(BlockSpec a, BlockSpec b) =>
        a.type == b.type && a.north == b.north && a.east == b.east && a.south == b.south && a.west == b.west;

    // Resolve o encaixe do planeta com conjuntos de bits.
    private sealed class PlanetSolver
    {
        private readonly int width;
        private readonly int height;
        private readonly List<Variant> pool;
        private readonly System.Random random;
        private readonly ulong[] lo;
        private readonly ulong[] hi;
        private readonly bool[] locked;
        // Opções que têm o socket "k" no lado "side": [side * 3 + k].
        private readonly ulong[] sideLo = new ulong[12];
        private readonly ulong[] sideHi = new ulong[12];
        private ulong allLo;
        private ulong allHi;
        private readonly Stack<int> queue = new Stack<int>();

        public int Repairs { get; private set; }
        public int Failures { get; private set; }

        public PlanetSolver(int width, int height, List<Variant> pool, System.Random random)
        {
            this.width = width;
            this.height = height;
            this.pool = pool;
            this.random = random;
            lo = new ulong[width * height];
            hi = new ulong[width * height];
            locked = new bool[width * height];

            for (int i = 0; i < pool.Count && i < 128; i++)
            {
                for (int side = 0; side < 4; side++)
                {
                    int k = (int)pool[i].spec.Get(side);
                    if (i < 64) sideLo[side * 3 + k] |= 1UL << i;
                    else sideHi[side * 3 + k] |= 1UL << (i - 64);
                }

                if (pool[i].weight > 0f)
                {
                    if (i < 64) allLo |= 1UL << i;
                    else allHi |= 1UL << (i - 64);
                }
            }
        }

        private int Index(int x, int y) => y * width + x;

        public int Chosen(int x, int y)
        {
            int i = Index(x, y);
            ulong a = lo[i], b = hi[i];
            if (a != 0 && (a & (a - 1)) == 0 && b == 0) return TrailingZeros(a);
            if (b != 0 && (b & (b - 1)) == 0 && a == 0) return 64 + TrailingZeros(b);
            return -1;
        }

        // Domínio inicial de um bloco livre: tudo do sorteio, menos rua virada para o polo.
        private void ResetCell(int x, int y)
        {
            int i = Index(x, y);
            ulong a = allLo, b = allHi;
            // Missão 20: só mato virado para o polo (o rio agora tem nascente e não precisa chegar lá).
            if (y == height - 1)
            {
                a &= sideLo[0 * 3 + (int)Socket.Mato];
                b &= sideHi[0 * 3 + (int)Socket.Mato];
            }

            if (y == 0)
            {
                a &= sideLo[2 * 3 + (int)Socket.Mato];
                b &= sideHi[2 * 3 + (int)Socket.Mato];
            }

            lo[i] = a;
            hi[i] = b;
        }

        public bool Solve(Dictionary<Vector2Int, int> fixedIndex)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    ResetCell(x, y);
                }
            }

            foreach (KeyValuePair<Vector2Int, int> pair in fixedIndex)
            {
                if (pair.Key.y < 0 || pair.Key.y >= height)
                {
                    continue;
                }

                int i = Index(pair.Key.x, pair.Key.y);
                lo[i] = pair.Value < 64 ? 1UL << pair.Value : 0UL;
                hi[i] = pair.Value >= 64 ? 1UL << (pair.Value - 64) : 0UL;
                locked[i] = true;
            }

            for (int i = 0; i < lo.Length; i++)
            {
                queue.Push(i);
            }

            if (Propagate(out _) == false)
            {
                Debug.LogWarning("Sorteio do planeta: os blocos fixos não encaixam entre si.");
            }

            int cursor = 0;
            int total = width * height;
            int safety = total * 4;
            while (cursor < total && safety-- > 0)
            {
                int x = cursor % width;
                int y = cursor / width;
                int i = Index(x, y);
                ulong a = lo[i], b = hi[i];
                bool single = a != 0 && (a & (a - 1)) == 0 && b == 0 || b != 0 && (b & (b - 1)) == 0 && a == 0;
                if (single)
                {
                    cursor++;
                    continue;
                }

                if (a == 0 && b == 0)
                {
                    // Sem opção (sobrou de um conserto que não deu): vira floresta.
                    Failures++;
                    cursor++;
                    continue;
                }

                int chosen = Pick(a, b, x, y);
                lo[i] = chosen < 64 ? 1UL << chosen : 0UL;
                hi[i] = chosen >= 64 ? 1UL << (chosen - 64) : 0UL;
                queue.Push(i);
                if (!Propagate(out int bad))
                {
                    Repairs++;
                    if (!Repair(bad))
                    {
                        Failures++;
                    }

                    cursor = 0; // os já decididos são pulados rapidinho
                    continue;
                }

                cursor++;
            }

            return Failures == 0;
        }

        // Missão 20: o peso de cada tipo muda com o bioma da região (Cidade Nublada = prédios;
        // Picos Gelados = mato e montanha; Baía = mato à beira-mar...). Veja PlanetTerrain.LayoutBias.
        private readonly double[] bias = new double[128];

        private int Pick(ulong a, ulong b, int x, int y)
        {
            double total = 0;
            for (int j = 0; j < pool.Count && j < 128; j++)
            {
                bias[j] = 0;
                if (Has(a, b, j))
                {
                    bias[j] = pool[j].weight * PlanetTerrain.LayoutBias(x, y, pool[j].spec.type);
                    total += bias[j];
                }
            }

            double roll = random.NextDouble() * total;
            int last = -1;
            for (int j = 0; j < pool.Count && j < 128; j++)
            {
                if (!Has(a, b, j))
                {
                    continue;
                }

                last = j;
                roll -= bias[j];
                if (roll <= 0)
                {
                    return j;
                }
            }

            return last;
        }

        private static bool Has(ulong a, ulong b, int j) => j < 64 ? (a >> j & 1UL) != 0 : (b >> (j - 64) & 1UL) != 0;

        // Tira dos vizinhos as opções que não encaixam mais. "bad" = bloco que ficou sem opção.
        private bool Propagate(out int bad)
        {
            bad = -1;
            while (queue.Count > 0)
            {
                int i = queue.Pop();
                int x = i % width;
                int y = i / width;
                ulong a = lo[i], b = hi[i];
                if (a == 0 && b == 0)
                {
                    queue.Clear();
                    bad = i;
                    return false;
                }

                for (int side = 0; side < 4; side++)
                {
                    int nx = x + Directions[side].x;
                    int ny = y + Directions[side].y;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    nx = ((nx % width) + width) % width;
                    int opposite = (side + 2) % 4;
                    ulong allowLo = 0, allowHi = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        if ((a & sideLo[side * 3 + k]) != 0 || (b & sideHi[side * 3 + k]) != 0)
                        {
                            allowLo |= sideLo[opposite * 3 + k];
                            allowHi |= sideHi[opposite * 3 + k];
                        }
                    }

                    int n = Index(nx, ny);
                    ulong newLo = lo[n] & allowLo;
                    ulong newHi = hi[n] & allowHi;
                    if (newLo == lo[n] && newHi == hi[n])
                    {
                        continue;
                    }

                    lo[n] = newLo;
                    hi[n] = newHi;
                    if (newLo == 0 && newHi == 0)
                    {
                        queue.Clear();
                        bad = n;
                        return false;
                    }

                    queue.Push(n);
                }
            }

            return true;
        }

        // Refaz um quadrado em volta do bloco sem opção (cada vez maior, se precisar).
        private bool Repair(int bad)
        {
            int bx = bad % width;
            int by = bad / width;
            for (int radius = 3; radius <= 12; radius += 2)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        int x = ((bx + dx) % width + width) % width;
                        int y = by + dy;
                        if (y >= 0 && y < height && !locked[Index(x, y)])
                        {
                            ResetCell(x, y);
                        }
                    }
                }

                for (int dx = -radius - 1; dx <= radius + 1; dx++)
                {
                    for (int dy = -radius - 1; dy <= radius + 1; dy++)
                    {
                        int x = ((bx + dx) % width + width) % width;
                        int y = by + dy;
                        if (y >= 0 && y < height)
                        {
                            queue.Push(Index(x, y));
                        }
                    }
                }

                if (Propagate(out _))
                {
                    return true;
                }
            }

            return false;
        }

        private static int TrailingZeros(ulong v)
        {
            int n = 0;
            while ((v & 1UL) == 0 && n < 64)
            {
                v >>= 1;
                n++;
            }

            return n;
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
