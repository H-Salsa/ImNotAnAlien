using System.Collections.Generic;
using UnityEngine;

// O planeta (Missão 19, decisões D21 e D28).
// - Grade de 128 × 128 blocos de 100 m. Leste-oeste DÁ A VOLTA (12,8 km): andando sempre para o
//   leste você volta ao começo.
// - Norte e sul também dão a volta, como num globo: depois da última linha vêm 2 linhas de POLO
//   (neve e nevasca). Ao cruzar a linha do polo você sai do outro lado do planeta (180° de longitude
//   adiante), agora andando para o sul (veja WorldStreamer.UpdatePoleCrossing).
// - 16 regiões com FORMATO ORGÂNICO (como países num mapa), sorteado a cada jogo novo (BuildRegions):
//   cada região nasce de um centro (perto da sua posição de sempre: os nomes e o lugar aproximado não
//   mudam) e as divisas são entortadas com ruído. Tamanhos variados. Cada região tem 16 setores, também
//   orgânicos. A cota de cada setor acompanha o tamanho dele (~50 para um setor médio de 64 blocos).
//   Sem o mapa montado (fora do planeta), vale a grade antiga: regiões de 32 × 32, setores de 8 × 8.
// - O Vilarejo do Bilu (começo do jogo) fica nos blocos 59..61 × 59..61, dentro de um setor só.
// - Quatro bases, uma em cada quarto do planeta.
//
// Coordenadas:
// - "Bloco sem volta": o número do bloco contando a partir do canto da grade, podendo passar de 127
//   ou ficar negativo no eixo X depois de dar voltas (é o que fica carregado em volta do jogador).
// - "Bloco do planeta": o mesmo, com X dado a volta (0..127). É o que decide o tipo do bloco,
//   o setor e a região.
public static class Planet
{
    public const int Width = 128;
    public const int Height = 128;
    public const int PoleRows = 2; // linhas de neve além do planeta, em cada polo
    public const int RegionSize = 32;
    public const int SectorSize = 8;
    public const int RegionsX = Width / RegionSize;                  // 4
    public const int RegionsZ = Height / RegionSize;                 // 4
    public const int RegionCount = RegionsX * RegionsZ;              // 16
    public const int SectorsPerRegionSide = RegionSize / SectorSize; // 4
    public const int SectorsPerRegion = SectorsPerRegionSide * SectorsPerRegionSide; // 16
    public const int SectorCount = RegionCount * SectorsPerRegion;   // 256

    // O Vilarejo do Bilu: canto sudoeste (bloco 59, 59), 3 × 3 blocos.
    public static readonly Vector2Int VillageCorner = new Vector2Int(59, 59);

    public struct BaseInfo
    {
        public string id;
        public string name;
        public string compass;
        public Vector2Int coord; // bloco do planeta
    }

    // Uma base em cada quarto do planeta (cada uma cuida de 4 regiões).
    public static readonly BaseInfo[] Bases =
    {
        new BaseInfo { id = "nave", name = "Nave Caída", compass = "noroeste", coord = new Vector2Int(32, 96) },
        new BaseInfo { id = "torre", name = "Torre de Rádio", compass = "nordeste", coord = new Vector2Int(96, 96) },
        new BaseInfo { id = "laboratorio", name = "Laboratório Velho", compass = "sudoeste", coord = new Vector2Int(32, 32) },
        new BaseInfo { id = "estufa", name = "Estufa Abandonada", compass = "sudeste", coord = new Vector2Int(96, 32) },
    };

    private static readonly string[] RegionNames =
    {
        // Latitude 0 (sul, perto do polo) ... 3 (norte, perto do polo); longitude 0..3.
        "Gelo do Sul", "Dunas Roxas", "Pântano Zumbidor", "Cratera Funda",
        "Campos de Glub", "Vale do Bilu", "Colinas Tortas", "Baía dos Tentáculos",
        "Planície Gorda", "Floresta Chiclete", "Cidade Nublada", "Morro das Antenas",
        "Tundra Azul", "Picos Gelados", "Estepe do Norte", "Geleira Velha",
    };

    private static readonly string[] SectorColumns = { "A", "B", "C", "D" };

    // Bioma de cada região (Missão 19): cor no globo e ícone. A Missão 20 usa o mesmo para o terreno.
    public enum Biome { Gelo, Dunas, Pantano, Cratera, Campos, Vale, Colinas, Baia, Planicie, Floresta, Cidade, Antenas, Tundra, Picos, Estepe, Geleira }

    // Paleta mais viva (versão 2 do mapa): cada bioma com cor própria e saturada.
    private static readonly Color32[] RegionColors =
    {
        new Color32(0xDC, 0xEF, 0xF7, 255), new Color32(0xB4, 0x8A, 0xE0, 255), new Color32(0x5E, 0x9E, 0x6A, 255), new Color32(0xC9, 0x8E, 0x5E, 255),
        new Color32(0x9E, 0xD3, 0x6A, 255), new Color32(0x74, 0xC7, 0x7A, 255), new Color32(0xB5, 0xD8, 0x6E, 255), new Color32(0x3F, 0xA6, 0xD8, 255),
        new Color32(0xE2, 0xD2, 0x7A, 255), new Color32(0xF2, 0x8D, 0xB2, 255), new Color32(0x9A, 0xA6, 0xC4, 255), new Color32(0xD3, 0x9B, 0x6A, 255),
        new Color32(0x8E, 0xC6, 0xE6, 255), new Color32(0xE9, 0xF2, 0xF8, 255), new Color32(0xD8, 0xC4, 0x7E, 255), new Color32(0xC9, 0xE4, 0xF2, 255),
    };

    public static Color RegionColor(int region) => region >= 0 && region < RegionColors.Length ? (Color)RegionColors[region] : Color.gray;

    public static Biome RegionBiome(int region) => (Biome)Mathf.Clamp(region, 0, RegionCount - 1);

    // Ligado pelo WorldStreamer quando o mundo é o planeta.
    public static bool Active { get; private set; }
    public static Vector2 GridOrigin { get; private set; }
    public static float BlockSize { get; private set; } = 100f;

    public static float Circumference => Width * BlockSize;

    // Linha do polo (Z LÓGICO): quem passa dela aparece do outro lado do planeta.
    public static float NorthPoleZ => GridOrigin.y + (Height + PoleRows) * BlockSize;
    public static float SouthPoleZ => GridOrigin.y - PoleRows * BlockSize;

    // Quanto (0..1) a posição LÓGICA está dentro da zona do polo (0 = fora, 1 = na linha do polo).
    public static float PolarDepth(Vector3 logical)
    {
        float northStart = GridOrigin.y + Height * BlockSize;
        float southStart = GridOrigin.y;
        float span = PoleRows * BlockSize;
        if (logical.z > northStart) return Mathf.Clamp01((logical.z - northStart) / span);
        if (logical.z < southStart) return Mathf.Clamp01((southStart - logical.z) / span);
        return 0f;
    }

    // Atravessou a linha do polo? Devolve a posição LÓGICA do outro lado (meia volta em X, espelhada em Z).
    public static bool TryCrossPole(Vector3 logical, out Vector3 other)
    {
        other = logical;
        float north = NorthPoleZ;
        float south = SouthPoleZ;
        if (logical.z > north)
        {
            other = new Vector3(logical.x + Circumference / 2f, logical.y, 2f * north - logical.z - 0.5f);
            return true;
        }

        if (logical.z < south)
        {
            other = new Vector3(logical.x + Circumference / 2f, logical.y, 2f * south - logical.z + 0.5f);
            return true;
        }

        return false;
    }

    public static void Configure(Vector2 gridOrigin, float blockSize)
    {
        GridOrigin = gridOrigin;
        BlockSize = blockSize;
        Active = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = false;
        regionMap = null;
        sectorMap = null;
        sectorBlocks = null;
        regionLabels = null;
        sectorLabels = null;
        regionsSeed = int.MinValue;
    }

    // ---------- Coordenadas ----------

    public static int WrapX(int x) => ((x % Width) + Width) % Width;

    public static Vector2Int Wrap(Vector2Int coord) => new Vector2Int(WrapX(coord.x), coord.y);

    public static bool InsideLatitude(int z) => z >= 0 && z < Height;

    // Bloco (sem volta) de uma posição LÓGICA.
    public static Vector2Int BlockAt(Vector3 logical) => new Vector2Int(
        Mathf.FloorToInt((logical.x - GridOrigin.x) / BlockSize),
        Mathf.FloorToInt((logical.z - GridOrigin.y) / BlockSize));

    // Bloco do planeta (X dado a volta) de uma posição LÓGICA.
    public static Vector2Int PlanetBlockAt(Vector3 logical) => Wrap(BlockAt(logical));

    // Posição LÓGICA com X trazido para dentro da primeira volta (para salvar).
    public static Vector3 WrapLogical(Vector3 logical)
    {
        float relative = logical.x - GridOrigin.x;
        relative = Mathf.Repeat(relative, Circumference);
        return new Vector3(GridOrigin.x + relative, logical.y, logical.z);
    }

    // Centro LÓGICO de um bloco do planeta, na volta mais perto de "near" (posição LÓGICA).
    public static Vector3 CenterNear(Vector2Int planetCoord, Vector3 near)
    {
        float x = GridOrigin.x + (planetCoord.x + 0.5f) * BlockSize;
        float z = GridOrigin.y + (planetCoord.y + 0.5f) * BlockSize;
        float dx = Mathf.Repeat(x - near.x + Circumference / 2f, Circumference) - Circumference / 2f;
        return new Vector3(near.x + dx, 0f, z);
    }

    // Onde o jogador chega numa base (no pátio, de frente para a rua), em posição LÓGICA,
    // na volta mais perto de "near". Igual ao BaseSite.SpawnPoint.
    public static Vector3 BaseSpawnLogical(BaseInfo info, Vector3 near)
    {
        Vector3 center = CenterNear(info.coord, near);
        return center + new Vector3(0f, 0.3f, 4f);
    }

    public static bool TryGetBase(string id, out BaseInfo info)
    {
        foreach (BaseInfo candidate in Bases)
        {
            if (candidate.id == id)
            {
                info = candidate;
                return true;
            }
        }

        info = default;
        return false;
    }

    // ---------- Regiões e setores ----------

    public static int RegionAt(Vector2Int planetCoord)
    {
        if (!InsideLatitude(planetCoord.y))
        {
            return -1;
        }

        Vector2Int c = Wrap(planetCoord);
        if (regionMap != null)
        {
            return regionMap[c.y * Width + c.x];
        }

        return c.y / RegionSize * RegionsX + c.x / RegionSize;
    }

    // Índice do setor no planeta inteiro (0..255): região × 16 + setor dentro da região.
    public static int SectorAt(Vector2Int planetCoord)
    {
        int region = RegionAt(planetCoord);
        if (region < 0)
        {
            return -1;
        }

        Vector2Int c = Wrap(planetCoord);
        if (sectorMap != null)
        {
            return region * SectorsPerRegion + sectorMap[c.y * Width + c.x];
        }

        int sx = c.x % RegionSize / SectorSize;
        int sz = c.y % RegionSize / SectorSize;
        return region * SectorsPerRegion + sz * SectorsPerRegionSide + sx;
    }

    public static int RegionOfSector(int sector) => sector / SectorsPerRegion;

    public static string RegionName(int region) =>
        region >= 0 && region < RegionNames.Length ? RegionNames[region] : $"Região {region + 1}";

    // Identificador fixo da região (para o jogo salvo).
    public static string RegionId(int region) => $"R{region:00}";

    public static string SectorId(int sector) => $"R{RegionOfSector(sector):00}S{sector % SectorsPerRegion:00}";

    // Nome do setor no HUD: "A1".."D4" (coluna de oeste para leste, linha de sul para norte).
    // O HUD já escreve "SETOR" antes.
    public static string SectorName(int sector)
    {
        if (sector == VillageSector)
        {
            return "Vilarejo do Bilu";
        }

        int local = sector % SectorsPerRegion;
        return $"{SectorColumns[local % SectorsPerRegionSide]}{local / SectorsPerRegionSide + 1}";
    }

    public static int VillageSector => SectorAt(VillageCorner + Vector2Int.one);

    public static int RegionOfBlock(Vector2Int planetCoord) => RegionAt(planetCoord);

    // Latitude e longitude (graus) do centro de um bloco do planeta, para o globo.
    public static Vector2 LatLong(Vector2 planetPosition)
    {
        float longitude = planetPosition.x / Width * 360f - 180f;
        // A linha do polo (2 linhas de neve além do planeta) é exatamente ±90°.
        float latitude = ((planetPosition.y + PoleRows) / (Height + 2f * PoleRows) - 0.5f) * 180f;
        return new Vector2(latitude, longitude);
    }

    // ---------- Regiões orgânicas (Missão 19) ----------

    private static byte[] regionMap;   // região de cada bloco (z * Width + x)
    private static byte[] sectorMap;   // setor dentro da região (0..15)
    private static int[] sectorBlocks; // blocos de cada setor (0..255)
    private static Vector2[] regionLabels; // meio de cada região (posição no planeta), para o globo
    private static Vector2[] sectorLabels; // meio de cada setor (0..255)
    private static int regionsSeed = int.MinValue;

    public static bool RegionsBuilt => regionMap != null;

    // Quantos blocos o setor tem (a cota acompanha). Sem mapa: 64 (8 × 8).
    public static int SectorBlockCount(int sector) =>
        sectorBlocks != null && sector >= 0 && sector < sectorBlocks.Length ? sectorBlocks[sector] : SectorSize * SectorSize;

    // Meio do setor (posição no planeta, em blocos), para o selo do globo.
    public static Vector2 SectorLabelPosition(int sector)
    {
        if (sectorLabels != null && sector >= 0 && sector < sectorLabels.Length)
        {
            return sectorLabels[sector];
        }

        int region = sector / SectorsPerRegion;
        int local = sector % SectorsPerRegion;
        return new Vector2((region % RegionsX) * RegionSize + (local % SectorsPerRegionSide + 0.5f) * SectorSize,
            (region / RegionsX) * RegionSize + (local / SectorsPerRegionSide + 0.5f) * SectorSize);
    }

    // Onde escrever o nome da região no globo (posição no planeta, em blocos).
    public static Vector2 RegionLabelPosition(int region) =>
        regionLabels != null ? regionLabels[region] : new Vector2((region % RegionsX + 0.5f) * RegionSize, (region / RegionsX + 0.5f) * RegionSize);

    // Monta as regiões e os setores para a semente do jogo. Sempre igual para a mesma semente.
    public static void BuildRegions(int seed)
    {
        if (regionMap != null && regionsSeed == seed)
        {
            return;
        }

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        System.Random random = new System.Random(seed ^ 0x5EED);
        Vector2[] centers = new Vector2[RegionCount];
        float[] weights = new float[RegionCount];
        for (int i = 0; i < RegionCount; i++)
        {
            int rx = i % RegionsX;
            int rz = i / RegionsX;
            centers[i] = new Vector2(rx * RegionSize + 16f + Range(random, -9f, 9f), rz * RegionSize + 16f + Range(random, -7f, 7f));
            weights[i] = Range(random, -100f, 140f); // tamanhos variados
        }

        // O Vale do Bilu fica sempre em volta do Vilarejo.
        int valley = 1 * RegionsX + 1;
        centers[valley] = new Vector2(56f + Range(random, -3f, 3f), 54f + Range(random, -3f, 3f));

        byte[] regions = new byte[Width * Height];
        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                Vector2 p = Warp(seed, x + 0.5f, z + 0.5f, 1f);
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < RegionCount; i++)
                {
                    float dx = WrapDx(p.x, centers[i].x);
                    float dz = p.y - centers[i].y;
                    float d = dx * dx + dz * dz - weights[i];
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }

                regions[z * Width + x] = (byte)best;
            }
        }

        ForceVillage(regions, (byte)valley);
        MergeLoosePieces(regions, null);
        ForceVillage(regions, (byte)valley);

        // Setores: 16 por região, pelo "centro de massa" (Lloyd), com divisas levemente tortas.
        byte[] sectors = new byte[Width * Height];
        for (int region = 0; region < RegionCount; region++)
        {
            BuildSectors(seed, region, regions, sectors, centers[region]);
        }

        // O setor do Vilarejo cobre o Vilarejo inteiro.
        byte villageSector = sectors[(VillageCorner.y + 1) * Width + VillageCorner.x + 1];
        for (int dz = 0; dz < 3; dz++)
        {
            for (int dx = 0; dx < 3; dx++)
            {
                sectors[(VillageCorner.y + dz) * Width + VillageCorner.x + dx] = villageSector;
            }
        }

        MergeLoosePieces(sectors, regions);

        // Tamanhos e meio de cada região (para os nomes no globo).
        int[] counts = new int[SectorCount];
        Vector2[] sectorSums = new Vector2[SectorCount];
        Vector2[] sums = new Vector2[RegionCount];
        int[] regionCounts = new int[RegionCount];
        for (int z = 0; z < Height; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = z * Width + x;
                int region = regions[i];
                counts[region * SectorsPerRegion + sectors[i]]++;
                sectorSums[region * SectorsPerRegion + sectors[i]] += new Vector2(WrapDx(x + 0.5f, centers[region].x), z + 0.5f - centers[region].y);
                sums[region] += new Vector2(WrapDx(x + 0.5f, centers[region].x), z + 0.5f - centers[region].y);
                regionCounts[region]++;
            }
        }

        Vector2[] labels = new Vector2[RegionCount];
        for (int region = 0; region < RegionCount; region++)
        {
            Vector2 mean = regionCounts[region] > 0 ? sums[region] / regionCounts[region] : Vector2.zero;
            labels[region] = new Vector2(Mathf.Repeat(centers[region].x + mean.x, Width), centers[region].y + mean.y);
        }

        Vector2[] sectorMiddles = new Vector2[SectorCount];
        for (int sector = 0; sector < SectorCount; sector++)
        {
            int region = sector / SectorsPerRegion;
            Vector2 mean = counts[sector] > 0 ? sectorSums[sector] / counts[sector] : Vector2.zero;
            sectorMiddles[sector] = new Vector2(Mathf.Repeat(centers[region].x + mean.x, Width), centers[region].y + mean.y);
        }

        sectorLabels = sectorMiddles;
        regionMap = regions;
        sectorMap = sectors;
        sectorBlocks = counts;
        regionLabels = labels;
        regionsSeed = seed;
        watch.Stop();
        Debug.Log($"Regiões orgânicas montadas em {watch.ElapsedMilliseconds} ms (semente {seed}).");
    }

    private static void ForceVillage(byte[] regions, byte valley)
    {
        for (int dz = -1; dz <= 3; dz++)
        {
            for (int dx = -1; dx <= 3; dx++)
            {
                regions[(VillageCorner.y + dz) * Width + VillageCorner.x + dx] = valley;
            }
        }
    }

    private static void BuildSectors(int seed, int region, byte[] regions, byte[] sectors, Vector2 regionCenter)
    {
        List<int> cells = new List<int>();
        for (int i = 0; i < regions.Length; i++)
        {
            if (regions[i] == region)
            {
                cells.Add(i);
            }
        }

        if (cells.Count == 0)
        {
            return;
        }

        System.Random random = new System.Random(seed * 31 + region);
        Vector2[] centers = new Vector2[SectorsPerRegion];
        HashSet<int> used = new HashSet<int>();
        for (int k = 0; k < SectorsPerRegion; k++)
        {
            int cell = cells[random.Next(cells.Count)];
            for (int tries = 0; tries < 50 && used.Contains(cell); tries++)
            {
                cell = cells[random.Next(cells.Count)]; // centros diferentes
            }

            used.Add(cell);
            centers[k] = new Vector2(cell % Width + 0.5f, cell / Width + 0.5f);
        }

        // Lloyd: cada centro vai para o meio dos blocos que pegou (setores de tamanho parecido).
        Vector2[] sums = new Vector2[SectorsPerRegion];
        int[] counts = new int[SectorsPerRegion];
        for (int iteration = 0; iteration < 6; iteration++)
        {
            System.Array.Clear(sums, 0, sums.Length);
            System.Array.Clear(counts, 0, counts.Length);
            foreach (int cell in cells)
            {
                float x = cell % Width + 0.5f;
                float z = cell / Width + 0.5f;
                int k = Nearest(centers, x, z);
                sums[k] += new Vector2(WrapDx(x, centers[k].x), z - centers[k].y);
                counts[k]++;
            }

            for (int k = 0; k < SectorsPerRegion; k++)
            {
                if (counts[k] > 0)
                {
                    Vector2 move = sums[k] / counts[k];
                    centers[k] = new Vector2(Mathf.Repeat(centers[k].x + move.x, Width), centers[k].y + move.y);
                }
            }
        }

        // Nomes A1..D4: 4 fileiras de sul para norte, cada uma de oeste para leste.
        int[] byRow = new int[SectorsPerRegion];
        for (int k = 0; k < SectorsPerRegion; k++) byRow[k] = k;
        System.Array.Sort(byRow, (a, b) => centers[a].y.CompareTo(centers[b].y));
        int[] rename = new int[SectorsPerRegion];
        for (int row = 0; row < SectorsPerRegionSide; row++)
        {
            int[] group = new int[SectorsPerRegionSide];
            System.Array.Copy(byRow, row * SectorsPerRegionSide, group, 0, SectorsPerRegionSide);
            System.Array.Sort(group, (a, b) => WrapDx(centers[a].x, regionCenter.x).CompareTo(WrapDx(centers[b].x, regionCenter.x)));
            for (int col = 0; col < SectorsPerRegionSide; col++)
            {
                rename[group[col]] = row * SectorsPerRegionSide + col;
            }
        }

        foreach (int cell in cells)
        {
            Vector2 p = Warp(seed + 5, cell % Width + 0.5f, cell / Width + 0.5f, 0.35f);
            sectors[cell] = (byte)rename[Nearest(centers, p.x, p.y)];
        }
    }

    private static int Nearest(Vector2[] centers, float x, float z)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int k = 0; k < centers.Length; k++)
        {
            float dx = WrapDx(x, centers[k].x);
            float dz = z - centers[k].y;
            float d = dx * dx + dz * dz;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = k;
            }
        }

        return best;
    }

    // Pedaços soltos (que não são o maior pedaço da sua cor) passam para o vizinho mais comum.
    // "within": se vier, só junta com vizinhos da mesma região (para os setores).
    private static void MergeLoosePieces(byte[] grid, byte[] within)
    {
        bool[] seen = new bool[grid.Length];
        Dictionary<int, List<List<int>>> pieces = new Dictionary<int, List<List<int>>>();
        Stack<int> stack = new Stack<int>();
        for (int start = 0; start < grid.Length; start++)
        {
            if (seen[start])
            {
                continue;
            }

            int key = Key(grid, within, start);
            List<int> piece = new List<int>();
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int cell = stack.Pop();
                piece.Add(cell);
                foreach (int next in Neighbors(cell))
                {
                    if (!seen[next] && Key(grid, within, next) == key)
                    {
                        seen[next] = true;
                        stack.Push(next);
                    }
                }
            }

            if (!pieces.TryGetValue(key, out List<List<int>> list))
            {
                pieces[key] = list = new List<List<int>>();
            }

            list.Add(piece);
        }

        foreach (List<List<int>> list in pieces.Values)
        {
            list.Sort((a, b) => b.Count.CompareTo(a.Count));
            for (int p = 1; p < list.Count; p++)
            {
                HashSet<int> mine = new HashSet<int>(list[p]);
                Dictionary<byte, int> votes = new Dictionary<byte, int>();
                foreach (int cell in list[p])
                {
                    foreach (int next in Neighbors(cell))
                    {
                        if (mine.Contains(next) || within != null && within[next] != within[cell])
                        {
                            continue;
                        }

                        votes.TryGetValue(grid[next], out int count);
                        votes[grid[next]] = count + 1;
                    }
                }

                byte winner = 0;
                int most = -1;
                foreach (KeyValuePair<byte, int> vote in votes)
                {
                    if (vote.Value > most)
                    {
                        most = vote.Value;
                        winner = vote.Key;
                    }
                }

                if (most > 0)
                {
                    foreach (int cell in list[p])
                    {
                        grid[cell] = winner;
                    }
                }
            }
        }
    }

    private static int Key(byte[] grid, byte[] within, int cell) => within != null ? within[cell] * 256 + grid[cell] : grid[cell];

    private static IEnumerable<int> Neighbors(int cell)
    {
        int x = cell % Width;
        int z = cell / Width;
        yield return z * Width + (x + 1) % Width;
        yield return z * Width + (x + Width - 1) % Width;
        if (z + 1 < Height) yield return (z + 1) * Width + x;
        if (z > 0) yield return (z - 1) * Width + x;
    }

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    // Diferença em X pelo caminho mais curto (o planeta dá a volta).
    private static float WrapDx(float a, float b)
    {
        float d = a - b;
        return Mathf.Repeat(d + Width / 2f, Width) - Width / 2f;
    }

    // Entorta um ponto com ruído (duas camadas), para as divisas virarem curvas.
    private static Vector2 Warp(int seed, float x, float z, float strength)
    {
        float dx = 0f, dz = 0f;
        // Escalas que cabem certinho na volta do planeta (5 e 12 células), para não ter emenda.
        float big = 5f / Width;
        float small = 12f / Width;
        dx += (ValueNoise(seed + 11, x * big, z * big, 5) - 0.5f) * 24f;
        dz += (ValueNoise(seed + 29, x * big, z * big, 5) - 0.5f) * 24f;
        dx += (ValueNoise(seed + 18, x * small, z * small, 12) - 0.5f) * 9f;
        dz += (ValueNoise(seed + 36, x * small, z * small, 12) - 0.5f) * 9f;
        return new Vector2(x + dx * strength, z + dz * strength);
    }

    // Ruído suave que dá a volta em X (período em células).
    private static float ValueNoise(int seed, float x, float z, int period)
    {
        int xi = Mathf.FloorToInt(x);
        int zi = Mathf.FloorToInt(z);
        float fx = x - xi;
        float fz = z - zi;
        float u = fx * fx * (3f - 2f * fx);
        float w = fz * fz * (3f - 2f * fz);
        float a = Hash01(seed, Mod(xi, period), zi);
        float b = Hash01(seed, Mod(xi + 1, period), zi);
        float c = Hash01(seed, Mod(xi, period), zi + 1);
        float d = Hash01(seed, Mod(xi + 1, period), zi + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), w);
    }

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    private static float Hash01(int seed, int x, int z)
    {
        unchecked
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(x * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    // Posição contínua no planeta (em blocos, X dado a volta) de uma posição LÓGICA.
    public static Vector2 PlanetPosition(Vector3 logical)
    {
        float x = Mathf.Repeat((logical.x - GridOrigin.x) / BlockSize, Width);
        float z = (logical.z - GridOrigin.y) / BlockSize;
        return new Vector2(x, z);
    }
}
