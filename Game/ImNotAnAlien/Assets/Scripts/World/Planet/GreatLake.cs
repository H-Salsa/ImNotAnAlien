using System.Collections.Generic;
using UnityEngine;

// Grande Lago e rios naturais do planeta (Missão 20, pedidos do autor).
// Os rios antigos eram blocos retos (uma linha azul cortando o mapa). Agora rio é RELEVO, igual à água
// da Baía dos Tentáculos: o chão é cavado em curva abaixo do nível da água e a lâmina de água do bloco
// (PlanetTerrain.AddWater) aparece sozinha — com margem de areia, curvas e largura variando.
//
// Antes do sorteio do planeta (WorldStreamer.PreparePlanet), Plan():
// - escolhe o lugar do lago (raio de ~5 blocos, de preferência no meio do Vale, longe do Vilarejo
//   e das bases) e marca esses blocos como "Lagoa" (selvagens, com relevo);
// - continua o rio do Vilarejo pelos dois lados: um lado vai serpenteando até o lago (se ele estiver
//   perto), o outro termina numa nascente;
// - solta mais 2 ou 3 rios do lago, em zigue-zague, até uma nascente.
// Os blocos por onde o rio passa também viram "Lagoa" (sem rua). A forma do rio é uma curva suave
// (Catmull-Rom) pelos blocos, com desvios sorteados; PlanetTerrain.Height chama Shape() para cavar.
public static class GreatLake
{
    public const int RadiusBlocks = 5;
    private const float CellReach = RadiusBlocks + 0.7f;
    private const float WaterRadius = (RadiusBlocks - 0.6f) * 100f;
    private const float Shore = 70f;
    private const float Bottom = -2.2f;           // fundo perto da margem
    public const float DeepBottom = -10f;         // meio do lago (dá para mergulhar)

    // Rio
    private const float RiverBed = -3.6f;     // fundo no meio (água a -0,6: ~3 m, dá para mergulhar)
    private const float RiverBank = 8f;       // rampa da margem depois da largura
    private const float VillageBed = -1.9f;   // fundo do rio dentro do Vilarejo (reto, largura fixa)
    private const float VillageHalf = 12.5f;  // meia largura do rio dentro do Vilarejo (cabe embaixo das pontes)
    private const float Valley = 45f;         // até onde os morros ficam mais baixos perto do rio
    private const float Reach = 75f;          // alcance de cada trecho na grade de busca

    public static bool Active { get; private set; }
    public static Vector2Int Center { get; private set; }

    private static Vector2 centerMeters;
    private static bool hasLake;
    private static float phase1, phase2, phase3;
    private static readonly HashSet<Vector2Int> lakeCells = new HashSet<Vector2Int>();
    private static readonly HashSet<Vector2Int> waterCells = new HashSet<Vector2Int>(); // lago + rios

    // Trechos do rio (em metros a partir do GridOrigin) e a meia largura em cada ponta.
    private static readonly List<Vector2> segA = new List<Vector2>();
    private static readonly List<Vector2> segB = new List<Vector2>();
    private static readonly List<float> segWa = new List<float>();
    private static readonly List<float> segWb = new List<float>();
    private static readonly List<float> segDa = new List<float>(); // fundo em cada ponta
    private static readonly List<float> segDb = new List<float>();
    private static readonly List<float> segNa = new List<float>(); // 0 = margem reta (Vilarejo), 1 = margem irregular
    private static readonly List<float> segNb = new List<float>();
    private static readonly HashSet<(Vector2Int, int)> openEdges = new HashSet<(Vector2Int, int)>(); // lados que encostam no rio do Vilarejo
    private static readonly Dictionary<int, List<int>> grid = new Dictionary<int, List<int>>();

    private static int[] occ; // 0 livre, 1 fixo, 2 lago, 3+ rio n
    private const int Hard = 1, Lake = 2;

    public static bool IsLake(Vector2Int coord) => Active && lakeCells.Contains(Planet.Wrap(coord));

    public static void Clear()
    {
        Active = false;
        hasLake = false;
        lakeCells.Clear();
        waterCells.Clear();
        segA.Clear();
        segB.Clear();
        segWa.Clear();
        segWb.Clear();
        segDa.Clear();
        segDb.Clear();
        segNa.Clear();
        segNb.Clear();
        openEdges.Clear();
        grid.Clear();
        occ = null;
    }

    public static void Plan(int seed, Dictionary<Vector2Int, BlockSpec> fixedSpecs)
    {
        Clear();
        System.Random rng = new System.Random(seed * 31 + 4049);
        int w = Planet.Width;
        occ = new int[w * Planet.Height];
        foreach (Vector2Int f in fixedSpecs.Keys)
        {
            Vector2Int c = Planet.Wrap(f);
            if (c.y >= 0 && c.y < Planet.Height)
            {
                occ[c.y * w + c.x] = Hard;
            }
        }

        // ----- Lago -----
        if (PickCenter(rng, fixedSpecs, out Vector2Int center))
        {
            hasLake = true;
            Center = center;
            centerMeters = new Vector2((center.x + 0.5f) * 100f, (center.y + 0.5f) * 100f);
            phase1 = (float)rng.NextDouble() * 6.283f;
            phase2 = (float)rng.NextDouble() * 6.283f;
            phase3 = (float)rng.NextDouble() * 6.283f;
            int reach = RadiusBlocks + 1;
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (dx * dx + dz * dz <= CellReach * CellReach)
                    {
                        Vector2Int cell = Planet.Wrap(new Vector2Int(center.x + dx, center.y + dz));
                        lakeCells.Add(cell);
                        occ[cell.y * w + cell.x] = Lake;
                    }
                }
            }
        }

        Dictionary<Vector2Int, BlockSpec> added = new Dictionary<Vector2Int, BlockSpec>();
        int riverId = 3;

        // ----- Rio do Vilarejo: continua pelos lados com rio que dão para fora -----
        List<(Vector2Int cell, int side)> exits = new List<(Vector2Int, int)>();
        foreach (KeyValuePair<Vector2Int, BlockSpec> pair in fixedSpecs)
        {
            if (pair.Value.type != BlockType.Rio)
            {
                continue;
            }

            for (int s = 0; s < 4; s++)
            {
                if (pair.Value.Get(s) == Socket.Rio && !fixedSpecs.ContainsKey(pair.Key + Step(s)))
                {
                    exits.Add((pair.Key, s));
                }
            }
        }

        // O rio DENTRO do Vilarejo também vira relevo (os blocos do Vilarejo trocam o canal de concreto
        // pelo chão cavado ao carregar; ver BlockBuilder.ConvertVillageRiver): trecho reto, largura fixa.
        foreach (KeyValuePair<Vector2Int, BlockSpec> pair in fixedSpecs)
        {
            if (pair.Value.type != BlockType.Rio)
            {
                continue;
            }

            bool eastWest = pair.Value.east == Socket.Rio || pair.Value.west == Socket.Rio;
            Vector2 middle = CellCenter(Planet.Wrap(pair.Key));
            Vector2 axis = eastWest ? Vector2.right : Vector2.up;
            AddSegment(middle - axis * 50f, middle + axis * 50f, VillageHalf, VillageHalf, VillageBed, VillageBed, 0f, 0f);
            waterCells.Add(Planet.Wrap(pair.Key));
        }

        bool reachedLake = false;
        foreach ((Vector2Int cell, int side) in exits)
        {
            Vector2Int start = Planet.Wrap(cell + Step(side));
            if (!InPlanet(start) || occ[Index(start)] != 0)
            {
                continue;
            }

            List<Vector2Int> path = null;
            bool toLake = false;
            if (hasLake && !reachedLake)
            {
                path = PathToLake(rng, start, side, riverId);
                toLake = path != null;
            }

            if (path == null)
            {
                path = Walk(rng, start, side, riverId, 6 + rng.Next(6), true);
            }

            if (path == null || path.Count < 1) // 1 bloco já serve: vira uma nascente colada no Vilarejo
            {
                continue;
            }

            reachedLake |= toLake;
            Commit(path, riverId, added);
            BlockSpec first = added[path[0]];
            first.Set((side + 2) % 4, Socket.Rio); // encaixa no rio do Vilarejo
            added[path[0]] = first;
            openEdges.Add((path[0], (side + 2) % 4));

            // Curva: começa reta na borda do bloco do Vilarejo (casa com o canal dele).
            Vector2 edge = CellCenter(path[0]) - (Vector2)Step(side) * 50f;
            BuildCurve(rng, path, edge, (Vector2)Step(side), toLake, true);
            riverId++;
        }

        // ----- Rios do lago -----
        if (hasLake)
        {
            int more = 2 + rng.Next(2);
            int[] order = { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            foreach (int side in order)
            {
                if (more <= 0)
                {
                    break;
                }

                // Bloco de lago da borda nessa direção.
                Vector2Int dir = Step(side);
                Vector2Int lateral = new Vector2Int(dir.y, dir.x);
                Vector2Int inlet = Planet.Wrap(Center + lateral * rng.Next(-1, 2));
                while (lakeCells.Contains(Planet.Wrap(inlet + dir)))
                {
                    inlet = Planet.Wrap(inlet + dir);
                }

                Vector2Int start = Planet.Wrap(inlet + dir);
                if (!InPlanet(start) || !Free(start, inlet, riverId, null, false))
                {
                    continue;
                }

                List<Vector2Int> path = Walk(rng, start, side, riverId, 7 + rng.Next(7), false);
                if (path == null || path.Count < 3)
                {
                    continue;
                }

                Commit(path, riverId, added);
                List<Vector2Int> withLake = new List<Vector2Int> { inlet };
                withLake.AddRange(path);
                BuildCurve(rng, withLake, CellCenter(inlet) - (Vector2)Step(side) * 120f, (Vector2)Step(side), false, false);
                riverId++;
                more--;
            }
        }

        foreach (Vector2Int cell in lakeCells)
        {
            BlockSpec spec = BlockSpec.Make(BlockType.Lagoa, Socket.Mato, Socket.Mato, Socket.Mato, Socket.Mato);
            spec.seed = 8000 + cell.x * 137 + cell.y * 7;
            fixedSpecs[cell] = spec;
            waterCells.Add(cell);
        }

        foreach (KeyValuePair<Vector2Int, BlockSpec> pair in added)
        {
            fixedSpecs[pair.Key] = pair.Value;
            waterCells.Add(pair.Key);
        }

        Active = true;
        Debug.Log($"Grande Lago {(hasLake ? "em " + Center : "(sem lugar)")}, {riverId - 3} rio(s), {segA.Count} trechos.");
    }

    // ---------- Grade ----------

    private static int Index(Vector2Int c) => c.y * Planet.Width + Planet.WrapX(c.x);

    private static bool InPlanet(Vector2Int c) => c.y >= 3 && c.y < Planet.Height - 3;

    private static Vector2 CellCenter(Vector2Int c) => new Vector2((c.x + 0.5f) * 100f, (c.y + 0.5f) * 100f);

    private static Vector2Int Step(int side)
    {
        switch (side)
        {
            case 0: return Vector2Int.up;
            case 1: return Vector2Int.right;
            case 2: return Vector2Int.down;
            default: return Vector2Int.left;
        }
    }

    // Bloco livre para o rio "id": nada fixo nem outro rio nos 8 vizinhos; do próprio rio só os
    // dois últimos blocos (de onde veio). "exempt" = o primeiro bloco, colado no Vilarejo.
    private static bool Free(Vector2Int c, Vector2Int from, int id, List<Vector2Int> own, bool exempt)
    {
        if (!InPlanet(c) || occ[Index(c)] != 0)
        {
            return false;
        }

        if (exempt)
        {
            return true;
        }

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                Vector2Int n = Planet.Wrap(new Vector2Int(c.x + dx, c.y + dz));
                if (n.y < 0 || n.y >= Planet.Height)
                {
                    continue;
                }

                int o = occ[Index(n)];
                if (o == Hard || (o >= 3 && o != id))
                {
                    return false;
                }

                if (o == id && own != null)
                {
                    int k = own.IndexOf(n);
                    if (k >= 0 && k < own.Count - 2)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static void Commit(List<Vector2Int> path, int id, Dictionary<Vector2Int, BlockSpec> added)
    {
        foreach (Vector2Int c in path)
        {
            occ[Index(c)] = id;
            BlockSpec spec = BlockSpec.Make(BlockType.Lagoa, Socket.Mato, Socket.Mato, Socket.Mato, Socket.Mato);
            spec.seed = 8500 + c.x * 131 + c.y * 3;
            added[c] = spec;
        }
    }

    // Caminhada sorteada: começa reto, depois vira às vezes (nunca para trás). Termina na nascente.
    private static List<Vector2Int> Walk(System.Random rng, Vector2Int start, int side, int id, int length, bool exemptFirst)
    {
        List<Vector2Int> path = new List<Vector2Int> { start };
        occ[Index(start)] = id;
        int current = side, back = (side + 2) % 4;
        Vector2Int at = start;
        for (int i = 1; i < length; i++)
        {
            List<int> options = new List<int>();
            if (i < 2 || rng.NextDouble() < 0.55)
            {
                options.Add(current);
            }

            int left = (current + 3) % 4, right = (current + 1) % 4;
            if (i >= 2)
            {
                if (rng.NextDouble() < 0.5) { if (left != back) options.Add(left); if (right != back) options.Add(right); }
                else { if (right != back) options.Add(right); if (left != back) options.Add(left); }
            }

            if (!options.Contains(current)) options.Add(current);

            bool moved = false;
            foreach (int o in options)
            {
                Vector2Int next = Planet.Wrap(at + Step(o));
                if (Free(next, at, id, path, false))
                {
                    current = o;
                    path.Add(next);
                    occ[Index(next)] = id;
                    at = next;
                    moved = true;
                    break;
                }
            }

            if (!moved)
            {
                break;
            }
        }

        foreach (Vector2Int c in path)
        {
            occ[Index(c)] = 0; // o Commit marca de novo
        }

        return path;
    }

    // Caminho até o lago com custos sorteados (serpenteia). O segundo bloco é sempre reto, para longe
    // do Vilarejo. Desiste se o lago estiver longe demais.
    private static List<Vector2Int> PathToLake(System.Random rng, Vector2Int start, int side, int id)
    {
        Vector2Int second = Planet.Wrap(start + Step(side));
        if (!Free(second, start, id, null, false))
        {
            return null;
        }

        int w = Planet.Width;
        int total = w * Planet.Height;
        float[] cost = new float[total];
        int[] parent = new int[total];
        for (int i = 0; i < total; i++) { cost[i] = float.MaxValue; parent[i] = -1; }

        int salt = rng.Next();
        float Noise(int i) => 1f + 2.2f * (((i * 73856093) ^ salt) & 1023) / 1023f;

        int s0 = Index(second);
        cost[s0] = 0f;
        SortedSet<(float, int)> open = new SortedSet<(float, int)> { (0f, s0) };
        int goal = -1;
        const float MaxCost = 110f;
        while (open.Count > 0)
        {
            (float curCost, int cur) = open.Min;
            open.Remove(open.Min);
            if (curCost > MaxCost)
            {
                break;
            }

            Vector2Int cc = new Vector2Int(cur % w, cur / w);
            bool done = false;
            for (int s = 0; s < 4; s++)
            {
                Vector2Int n = Planet.Wrap(cc + Step(s));
                if (n.y < 0 || n.y >= Planet.Height)
                {
                    continue;
                }

                int ni = Index(n);
                if (occ[ni] == Lake)
                {
                    goal = cur;
                    done = true;
                    break;
                }

                if (ni == Index(start) || !Free(n, cc, id, null, false))
                {
                    continue;
                }

                float nc = cost[cur] + Noise(ni);
                if (nc < cost[ni])
                {
                    if (cost[ni] != float.MaxValue) open.Remove((cost[ni], ni));
                    open.Add((nc, ni));
                    cost[ni] = nc;
                    parent[ni] = cur;
                }
            }

            if (done)
            {
                break;
            }
        }

        if (goal < 0)
        {
            return null;
        }

        List<Vector2Int> path = new List<Vector2Int>();
        for (int i = goal; i >= 0; i = parent[i])
        {
            path.Add(new Vector2Int(i % w, i / w));
        }

        path.Add(start);
        path.Reverse();
        return path;
    }

    // ---------- Curva ----------

    // Pontos de controle: entrada (borda do Vilarejo ou dentro do lago), centros dos blocos com
    // desvios sorteados e, no fim, o lago ou a nascente. Amostra a cada ~7 m.
    private static void BuildCurve(System.Random rng, List<Vector2Int> cells, Vector2 entry, Vector2 entryDir, bool endsInLake, bool fromVillage)
    {
        List<Vector2> points = new List<Vector2>();
        Vector2 previous = entry;
        points.Add(entry);
        if (fromVillage)
        {
            points.Add(entry + entryDir * 22f); // sai reto do canal do Vilarejo
        }

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2 p = Unwrap(CellCenter(cells[i]), previous);
            if (i > 0) // o primeiro fica no eixo (casa com o canal do Vilarejo / sai do lago)
            {
                p += new Vector2(Range(rng, -8f, 8f), Range(rng, -8f, 8f));
            }

            points.Add(p);
            previous = p;
        }

        if (endsInLake)
        {
            // Entra no lago: mais dois pontos rumo ao centro.
            Vector2 lake = Unwrap(centerMeters, previous);
            Vector2 toward = (lake - previous).normalized;
            points.Add(previous + toward * 100f);
            points.Add(previous + toward * 200f);
        }

        // Largura bem irregular: ondas de vários tamanhos + "poços" largos sorteados ao longo do rio.
        float baseWidth = Range(rng, 11f, 17f);
        float phase = Range(rng, 0f, 6.28f);
        float travelled = 0f;
        float length = 0f;
        for (int i = 1; i < points.Count; i++) length += Vector2.Distance(points[i - 1], points[i]);

        int poolCount = 1 + rng.Next(3);
        float[] poolAt = new float[poolCount];
        float[] poolSize = new float[poolCount];
        for (int i = 0; i < poolCount; i++)
        {
            poolAt[i] = Range(rng, 0.2f, 0.85f) * length;
            if (fromVillage)
            {
                poolAt[i] = Mathf.Max(poolAt[i], 100f); // nada de poço colado no Vilarejo
            }
            poolSize[i] = Range(rng, 0.5f, 0.9f);
        }

        Vector2 last = points[0];
        float lastW = Width(0f);
        float lastD = Bed(0f);
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 p0 = i > 0 ? points[i - 1] : points[0] - (points[1] - points[0]);
            Vector2 p1 = points[i], p2 = points[i + 1];
            Vector2 p3 = i + 2 < points.Count ? points[i + 2] : p2 + (p2 - p1);
            int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 6f));
            for (int k = 1; k <= steps; k++)
            {
                Vector2 q = CatmullRom(p0, p1, p2, p3, k / (float)steps);
                travelled += Vector2.Distance(last, q);
                float wq = Width(travelled);
                float dq = Bed(travelled);
                AddSegment(last, q, lastW, wq, lastD, dq, Noise(travelled - Vector2.Distance(last, q)), Noise(travelled));
                last = q;
                lastW = wq;
                lastD = dq;
            }
        }

        // Margem irregular só depois de sair do Vilarejo (lá dentro é reta, como o trecho do Vilarejo).
        float Noise(float s) => fromVillage ? Mathf.Clamp01((s - 15f) / 40f) : 1f;

        float Width(float s)
        {
            float wave = 0.32f * Mathf.Sin(s / 60f + phase) + 0.18f * Mathf.Sin(s / 21f + phase * 2.3f) + 0.08f * Mathf.Sin(s / 8.5f + phase * 3.7f);
            float wv = baseWidth * (1f + wave);
            for (int i = 0; i < poolCount; i++)
            {
                float t = (s - poolAt[i]) / 35f;
                wv += baseWidth * poolSize[i] * Mathf.Exp(-t * t);
            }

            if (fromVillage)
            {
                wv = Mathf.Lerp(VillageHalf, wv, Mathf.Clamp01((s - 15f) / 40f)); // mesma largura do trecho do Vilarejo
            }

            if (!endsInLake)
            {
                wv = Mathf.Lerp(7f, wv, Mathf.Clamp01((length - s) / 70f)); // afina até a nascente
            }

            return Mathf.Clamp(wv, 6f, 34f);
        }

        // Fundo: ~3 m de água; mais fundo nos poços; raso saindo do Vilarejo e na nascente.
        float Bed(float s)
        {
            float d = RiverBed;
            for (int i = 0; i < poolCount; i++)
            {
                float t = (s - poolAt[i]) / 35f;
                d -= 1.8f * poolSize[i] * Mathf.Exp(-t * t);
            }

            if (fromVillage)
            {
                d = Mathf.Lerp(VillageBed, d, Mathf.Clamp01(s / 50f));
            }

            if (!endsInLake)
            {
                d = Mathf.Lerp(-1.5f, d, Mathf.Clamp01((length - s) / 60f));
            }

            return d;
        }
    }

    private static Vector2 Unwrap(Vector2 p, Vector2 near)
    {
        float circ = Planet.Width * 100f;
        p.x -= Mathf.Round((p.x - near.x) / circ) * circ;
        return p;
    }

    private static float Range(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static void AddSegment(Vector2 a, Vector2 b, float wa, float wb, float da, float db, float na, float nb)
    {
        int index = segA.Count;
        segA.Add(a);
        segB.Add(b);
        segWa.Add(wa);
        segWb.Add(wb);
        segDa.Add(da);
        segDb.Add(db);
        segNa.Add(na);
        segNb.Add(nb);
        int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - Reach) / 100f), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + Reach) / 100f);
        int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - Reach) / 100f), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + Reach) / 100f);
        for (int z = z0; z <= z1; z++)
        {
            if (z < 0 || z >= Planet.Height)
            {
                continue;
            }

            for (int x = x0; x <= x1; x++)
            {
                int key = z * Planet.Width + Planet.WrapX(x);
                if (!grid.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>();
                    grid[key] = list;
                }

                if (list.Count == 0 || list[list.Count - 1] != index)
                {
                    list.Add(index);
                }
            }
        }
    }

    // Distância até o rio mais perto e a meia largura dele ali. false = nenhum rio por perto.
    public static bool NearestRiver(float mx, float mz, out float distance, out float halfWidth)
        => NearestRiver(mx, mz, out distance, out halfWidth, out _);

    public static bool NearestRiver(float mx, float mz, out float distance, out float halfWidth, out float bed)
        => NearestRiver(mx, mz, out distance, out halfWidth, out bed, out _);

    public static bool NearestRiver(float mx, float mz, out float distance, out float halfWidth, out float bed, out float noise)
    {
        noise = 1f;
        distance = float.MaxValue;
        halfWidth = 0f;
        bed = RiverBed;
        if (!Active || mz < 0f)
        {
            return false;
        }

        int cz = Mathf.FloorToInt(mz / 100f);
        if (cz >= Planet.Height)
        {
            return false;
        }

        int key = cz * Planet.Width + Planet.WrapX(Mathf.FloorToInt(mx / 100f));
        if (!grid.TryGetValue(key, out List<int> list))
        {
            return false;
        }

        float circ = Planet.Width * 100f;
        foreach (int i in list)
        {
            Vector2 a = segA[i];
            Vector2 ab = segB[i] - a;
            Vector2 ap = new Vector2(mx - a.x, mz - a.y);
            ap.x -= Mathf.Round(ap.x / circ) * circ;
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
            float d = (ap - ab * t).magnitude;
            if (d < distance)
            {
                distance = d;
                halfWidth = Mathf.Lerp(segWa[i], segWb[i], t);
                bed = Mathf.Lerp(segDa[i], segDb[i], t);
                noise = Mathf.Lerp(segNa[i], segNb[i], t);
            }
        }

        return distance < float.MaxValue;
    }

    // Quanto o leito do rio pode cavar aqui (0..1): 1 dentro de um bloco de lago/rio, caindo a 0 nos
    // últimos 10 m antes de um bloco que não é de água (assim nunca sobra um "degrau" na divisa).
    // O lado que encosta no rio do Vilarejo fica aberto (o canal continua até a borda).
    private static float WaterFactor(float mx, float mz)
    {
        int bx = Mathf.FloorToInt(mx / 100f), bz = Mathf.FloorToInt(mz / 100f);
        Vector2Int c = Planet.Wrap(new Vector2Int(bx, bz));
        if (!waterCells.Contains(c))
        {
            return 0f;
        }

        float fx = mx - bx * 100f, fz = mz - bz * 100f; // 0..100 dentro do bloco
        float edge = float.MaxValue;
        for (int side = 0; side < 4; side++)
        {
            if (!waterCells.Contains(Planet.Wrap(c + Step(side))) && !openEdges.Contains((c, side)))
            {
                float toSide = side == 0 ? 100f - fz : side == 1 ? 100f - fx : side == 2 ? fz : fx; // norte, leste, sul, oeste
                edge = Mathf.Min(edge, toSide);
            }
        }

        for (int dz = -1; dz <= 1; dz += 2)
        {
            for (int dx = -1; dx <= 1; dx += 2)
            {
                if (!waterCells.Contains(Planet.Wrap(new Vector2Int(c.x + dx, c.y + dz))))
                {
                    float cx = dx > 0 ? 100f - fx : fx, cz = dz > 0 ? 100f - fz : fz;
                    edge = Mathf.Min(edge, Mathf.Sqrt(cx * cx + cz * cz));
                }
            }
        }

        return Smooth(edge / 10f);
    }

    // ---------- Relevo ----------

    // Altura do chão com o lago e os rios: "h" = relevo normal, "flat" = 0 perto de blocos planos.
    // mx/mz em metros a partir do GridOrigin (mx já dá a volta no planeta).
    public static float Shape(float mx, float mz, float h, float flat)
    {
        if (!Active)
        {
            return h;
        }

        // Lago.
        if (hasLake)
        {
            float circumference = Planet.Width * 100f;
            float dx = mx - centerMeters.x;
            dx -= Mathf.Round(dx / circumference) * circumference;
            float dz = mz - centerMeters.y;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < (RadiusBlocks + 1.5f) * 100f)
            {
                float angle = Mathf.Atan2(dz, dx);
                float radius = WaterRadius * (1f + 0.06f * Mathf.Sin(2f * angle + phase1) + 0.045f * Mathf.Sin(3f * angle + phase2)
                                                 + 0.025f * Mathf.Sin(7f * angle + phase3));
                float k = Smooth((radius + Shore * 0.35f - d) / Shore);
                float calm = Mathf.Clamp01((radius + Shore * 1.6f - d) / Shore);
                h *= 1f - 0.6f * calm;
                // Margem rasa (~1,6 m) e depois descendo até ~9 m no meio, com ondulações no fundo.
                float deep = Smooth((radius - 40f - d) / 200f);
                float floor = Mathf.Lerp(Bottom, DeepBottom, deep) + deep * 1.2f * (Mathf.PerlinNoise(mx * 0.012f + 31f, mz * 0.012f + 7f) - 0.5f);
                h = Mathf.Lerp(h, floor, k * flat);
            }
        }

        // Rios.
        if (NearestRiver(mx, mz, out float dist, out float half, out float bed, out float noise))
        {
            // Margem irregular: cada lado do rio com sua própria variação (ruído pela posição).
            half *= Mathf.Lerp(1f, 0.78f + 0.44f * Mathf.PerlinNoise(mx * 0.028f + 13f, mz * 0.028f + 5f), noise);

            // Vale: os morros baixam perto do rio (contínuo, vale para qualquer bloco).
            if (h > 0f)
            {
                h = Mathf.Lerp(h * 0.2f, h, Smooth((dist - half) / Valley));
            }

            // Leito: só nos blocos do rio/lago (os outros ficam longe da curva de qualquer jeito).
            if (dist < half + RiverBank)
            {
                float factor = WaterFactor(mx, mz);
                if (factor > 0f)
                {
                    float carve = dist < half
                        ? Mathf.Lerp(bed, -0.1f, Smooth((dist - half * 0.35f) / (half * 0.65f)))
                        : Mathf.Lerp(-0.1f, h, Smooth((dist - half) / RiverBank));
                    h = Mathf.Lerp(h, Mathf.Min(h, carve), factor);
                }
            }
        }

        return h;
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    // ---------- Lugar do lago ----------

    private static bool PickCenter(System.Random rng, Dictionary<Vector2Int, BlockSpec> fixedSpecs, out Vector2Int best)
    {
        int w = Planet.Width, h = Planet.Height;
        int prefer = (int)Planet.Biome.Vale;
        int[] dist = new int[w * h];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                bool inside = Planet.RegionAt(new Vector2Int(x, z)) == prefer;
                dist[z * w + x] = inside ? int.MaxValue : 0;
                if (!inside)
                {
                    queue.Enqueue(new Vector2Int(x, z));
                }
            }
        }

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int d = dist[c.y * w + c.x];
            for (int s = 0; s < 4; s++)
            {
                Vector2Int n = Planet.Wrap(c + Step(s));
                if (n.y < 0 || n.y >= h || dist[n.y * w + n.x] <= d + 1)
                {
                    continue;
                }

                dist[n.y * w + n.x] = d + 1;
                queue.Enqueue(n);
            }
        }

        best = default;
        float bestScore = float.MinValue;
        int margin = RadiusBlocks + 4;
        for (int z = margin; z < h - margin; z++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector2Int c = new Vector2Int(x, z);
                if (!FarFromFixed(c, fixedSpecs, RadiusBlocks + 5))
                {
                    continue;
                }

                int region = Planet.RegionAt(c);
                if (region == (int)Planet.Biome.Baia || region == (int)Planet.Biome.Gelo || region == (int)Planet.Biome.Geleira)
                {
                    continue;
                }

                float d = Mathf.Min(dist[z * w + x], 20);
                float score = region == prefer ? 100f + d : -Mathf.Abs(z - h * 0.5f);
                score += (float)rng.NextDouble() * 0.9f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
        }

        return bestScore > float.MinValue;
    }

    private static bool FarFromFixed(Vector2Int c, Dictionary<Vector2Int, BlockSpec> fixedSpecs, int distance)
    {
        foreach (Vector2Int f in fixedSpecs.Keys)
        {
            int dx = Mathf.Abs(Planet.WrapX(f.x) - c.x);
            dx = Mathf.Min(dx, Planet.Width - dx);
            if (dx <= distance && Mathf.Abs(f.y - c.y) <= distance)
            {
                return false;
            }
        }

        return true;
    }
}
