using System.Collections.Generic;
using UnityEngine;

// Relevo e biomas do planeta (Missão 20, decisão D29).
//
// Altura do chão = ruído de Perlin (várias oitavas) com o jeito do bioma da região:
//   Picos Gelados = montanhas pontudas de ~40 m; Planície Gorda = quase plano; Baía dos Tentáculos =
//   chão abaixo do nível do mar (vira mar raso com praia); Pântano = poças; Dunas = cristas...
// Duas camadas (Missão 20, etapa 2):
// - ONDULAÇÃO GERAL (suave, inclinação de no máximo ~10%): vale para quase todo bloco, inclusive
//   cidade, ruas e fazendas — as ruas sobem e descem os morros. O chão, as ruas e as calçadas são
//   "drapeados" no relevo; casas, postes e árvores sobem para a altura do chão (SettleObjects).
//   Fica em 0 (plano) nos rios, no Vilarejo, nas bases e nos polos (some em ~200 m), e também nos
//   lagos e parques (~120 m), porque água precisa ficar nivelada: rios e lagos ficam nos vales.
// - RELEVO FORTE (morros, picos, dunas, mar): só nos blocos selvagens (Floresta e Campo), e some a
//   ~35 m das ruas e dos blocos que não são selvagens.
// - A função é a MESMA para o planeta todo (coordenadas lógicas, dá a volta no eixo X), então dois
//   blocos vizinhos calculam a mesma altura na divisa.
// - Água: tudo abaixo de WaterLevel vira água rasa (no máximo ~1,8 m: o protagonista nada).
// - Os parâmetros dos biomas são misturados entre os centros dos blocos (com um entortado de ruído),
//   então a troca de uma região para outra é gradual, não uma linha reta.
// Cores: cada bloco ganha uma textura própria do chão (64 × 64), pintada com a cor do bioma
// (misturada com os vizinhos), areia na beira da água e neve no alto.
public static class PlanetTerrain
{
    public const float WaterLevel = -0.6f;
    public const float SeaFloor = -2.4f;
    private const float FadeDistance = 35f;   // distância até o relevo "acordar" longe do que é plano
    private const float RoadCorridor = 9f;    // meia largura de rua + calçada
    private const float BlockSize = 100f;

    public struct BiomeStyle
    {
        public float amplitude;  // altura das elevações (m)
        public float baseHeight; // desloca tudo (negativo = abaixo do mar)
        public float ridge;      // 0 = morros redondos, 1 = cristas pontudas
        public float sharpness;  // expoente: >1 deixa os vales largos e os picos finos
        public Color low;        // cor do chão baixo
        public Color high;       // cor do chão alto
        public Color foliage;    // cor das copas e arbustos
        public float treeKeep;   // fração das árvores que ficam
        public bool snowy;       // sem areia na beira da água
        public float snowLine;   // acima disto o chão fica branco (0 = nunca)

        // Pesos do sorteio dos blocos (multiplicam o peso normal do tipo).
        public float urban, wild, farm, lake, river;
    }

    private static Color Hex(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    private static BiomeStyle Style(float amp, float baseH, float ridge, float sharp, uint low, uint high, uint foliage, float keep,
        bool snowy, float snowLine, float urban, float wild, float farm, float lake, float river) => new BiomeStyle
    {
        amplitude = amp, baseHeight = baseH, ridge = ridge, sharpness = sharp,
        low = Hex(low), high = Hex(high), foliage = Hex(foliage), treeKeep = keep, snowy = snowy, snowLine = snowLine,
        urban = urban, wild = wild, farm = farm, lake = lake, river = river,
    };

    // Na ordem do Planet.Biome.
    private static readonly BiomeStyle[] Styles =
    {
        //     amp   base  crista agudo  baixo     alto      copas  árvores neve  linha   cidade mato  fazenda lago  rio
        Style(10f,  0.5f, 0.2f, 1.2f, 0xDCE8F0, 0xFFFFFF, 0xE8F2F8, 0.4f, true,  0f,    0.4f, 2.2f, 0.3f, 1.0f, 1.0f), // Gelo
        Style(9f,   0.5f, 0.7f, 1.1f, 0xA27FC9, 0xC9A9EC, 0xB58BD9, 0.15f, false, 0f,   0.5f, 2.5f, 0.2f, 0.3f, 0.5f), // Dunas
        Style(3f,  -1.3f, 0.0f, 1.0f, 0x4B6E45, 0x6B8F55, 0x3F6E45, 1.0f, false, 0f,    0.5f, 2.0f, 0.5f, 2.5f, 2.0f), // Pântano
        Style(16f,  0.0f, 0.5f, 1.3f, 0x9E6643, 0xC98E5E, 0xC27A4A, 0.6f, false, 0f,    0.6f, 2.2f, 0.4f, 0.6f, 0.6f), // Cratera
        Style(5f,   0.5f, 0.0f, 1.1f, 0x6FB04A, 0xA6D66A, 0x5DAA45, 1.0f, false, 0f,    0.8f, 1.5f, 2.5f, 1.0f, 1.0f), // Campos
        Style(20f,  0.0f, 0.1f, 1.4f, 0x56A35A, 0x8BCB6E, 0x4E9E55, 1.0f, false, 0f,    0.6f, 2.0f, 1.5f, 1.2f, 2.0f), // Vale
        Style(14f,  0.5f, 0.0f, 1.0f, 0x8DB850, 0xC2DC74, 0x8DBA4A, 0.9f, false, 0f,    0.6f, 2.0f, 1.2f, 1.0f, 1.0f), // Colinas
        Style(8f,  -4.5f, 0.0f, 1.0f, 0x7DB866, 0x9FCC72, 0x4FA86A, 0.8f, false, 0f,    0.7f, 2.2f, 0.6f, 2.0f, 1.5f), // Baía
        Style(2.5f, 0.5f, 0.0f, 1.0f, 0xCDB95E, 0xE6D788, 0xC9B04A, 0.7f, false, 0f,    0.8f, 1.2f, 3.0f, 1.0f, 1.0f), // Planície
        Style(8f,   0.5f, 0.1f, 1.1f, 0xB86A93, 0xDB8DB4, 0xE278A8, 1.0f, false, 0f,    0.5f, 2.8f, 0.5f, 1.0f, 1.0f), // Floresta
        Style(3f,   0.5f, 0.0f, 1.0f, 0x7F8BA0, 0xA0AABC, 0x6E9A6A, 0.7f, false, 0f,    3.0f, 0.4f, 0.2f, 0.6f, 0.8f), // Cidade
        Style(18f,  0.0f, 0.4f, 1.3f, 0xA8744C, 0xD39B6A, 0xB8763E, 0.6f, false, 0f,    0.8f, 2.0f, 0.4f, 0.5f, 0.5f), // Antenas
        Style(7f,   0.3f, 0.0f, 1.1f, 0x8FB6CF, 0xC4DDEC, 0x8FB8D0, 0.5f, true,  0f,    0.5f, 2.0f, 0.4f, 1.2f, 1.0f), // Tundra
        Style(38f,  0.0f, 0.85f, 1.6f, 0x6E8466, 0x9A9EA8, 0x4C7A5A, 0.8f, false, 20f,  0.3f, 3.0f, 0.2f, 0.6f, 0.8f), // Picos
        Style(6f,   0.4f, 0.0f, 1.1f, 0xBFA85E, 0xD8C47E, 0xB8A050, 0.5f, false, 0f,    0.6f, 1.8f, 1.5f, 0.5f, 0.5f), // Estepe
        Style(12f,  0.3f, 0.3f, 1.2f, 0xCFE3F0, 0xF4FAFF, 0xF0F6FA, 0.25f, true, 0f,    0.4f, 2.5f, 0.2f, 1.0f, 0.6f), // Geleira
    };

    // Altura da ondulação geral por bioma (m), na ordem do Planet.Biome.
    private static readonly float[] MacroAmplitude =
    {
        6f, 8f, 2f, 12f, 7f, 18f, 16f, 1f, 4f, 10f, 8f, 16f, 6f, 25f, 7f, 8f,
    };

    private const float MaskStep = 25f;          // grade da máscara da ondulação (m)
    private const float LongFade = 200f;         // rio, Vilarejo, bases, polos
    private const float ShortFade = 120f;        // lago, parque
    private static float[] macroMask;            // 0 = plano obrigatório, 1 = ondulação livre
    private static int maskColumns;
    private static int maskRows;
    private static byte[] cellKind;              // 0 = normal, 1 = selvagem, 2 = plano (longe), 3 = plano (perto)

    private static readonly Color Sand = Hex(0xE6D59E);
    private static readonly Color Seabed = Hex(0x4F6E66);
    private static readonly Color Snow = Hex(0xF6FAFF);

    private static WorldLayout layout;
    private static int noiseSeed;
    private static byte[] cellInfo; // por bloco do planeta: bit 7 = selvagem; bits 0..3 = lados com rua

    public static bool Active => layout != null && Planet.Active;

    public static BiomeStyle StyleOf(Planet.Biome biome) => Styles[Mathf.Clamp((int)biome, 0, Styles.Length - 1)];

    // Chamado pelo WorldStreamer logo depois de sortear o planeta.
    public static void Configure(WorldLayout planetLayout, int seed)
    {
        layout = planetLayout;
        noiseSeed = seed * 7919 + 13;
        cellInfo = new byte[Planet.Width * Planet.Height];
        ponds.Clear();
        for (int z = 0; z < Planet.Height; z++)
        {
            for (int x = 0; x < Planet.Width; x++)
            {
                BlockSpec spec = layout.Get(new Vector2Int(x, z));
                byte info = 0;
                if (IsWild(spec.type))
                {
                    info |= 0x80;
                    for (int s = 0; s < 4; s++)
                    {
                        if (spec.Get(s) == Socket.Rua)
                        {
                            info |= (byte)(1 << s);
                        }
                    }
                }

                cellInfo[z * Planet.Width + x] = info;
                if (spec.type == BlockType.Lago)
                {
                    // Lagoa: centro, raios e fase da margem sorteados pelo bloco.
                    System.Random r = new System.Random(seed * 31 + x * 977 + z * 131);
                    ponds[new Vector2Int(x, z)] = new Vector4(
                        50f + (float)(r.NextDouble() * 12.0 - 6.0), 50f + (float)(r.NextDouble() * 12.0 - 6.0),
                        24f + (float)r.NextDouble() * 6f, (float)r.NextDouble() * 6.283f);
                }
            }
        }

        BuildMacroMask();
        tintCache.Clear();
    }

    // Missão 20: lagoas naturais (blocos "Lago"): x, z = centro no bloco, w = raio médio, ww = fase.
    private static readonly Dictionary<Vector2Int, Vector4> ponds = new Dictionary<Vector2Int, Vector4>();
    private const float PondBottom = -3f;
    private const float PondShore = 14f;

    // Afunda o chão na lagoa do bloco (margem irregular; tudo dentro do próprio bloco: na borda dele
    // não muda nada, então os blocos vizinhos casam).
    private static float Pond(float mx, float mz, float h, float flat)
    {
        if (ponds.Count == 0)
        {
            return h;
        }

        int bx = Mathf.FloorToInt(mx / BlockSize), bz = Mathf.FloorToInt(mz / BlockSize);
        if (!ponds.TryGetValue(new Vector2Int(Planet.WrapX(bx), bz), out Vector4 pond))
        {
            return h;
        }

        float fx = mx - bx * BlockSize, fz = mz - bz * BlockSize;
        float edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(fx, BlockSize - fx), Mathf.Min(fz, BlockSize - fz)) / 10f); // 0 na borda do bloco
        float dx = fx - pond.x, dz = fz - pond.y;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float angle = Mathf.Atan2(dz, dx);
        float radius = pond.z * (1f + 0.14f * Mathf.Sin(2f * angle + pond.w) + 0.08f * Mathf.Sin(3f * angle + pond.w * 1.7f)
                                    + 0.05f * Mathf.Sin(5f * angle + pond.w * 2.3f));
        float k = Mathf.Clamp01((radius + PondShore * 0.3f - d) / PondShore);
        k = k * k * (3f - 2f * k);
        float floor = PondBottom + 0.8f * Mathf.Clamp01(d / Mathf.Max(radius, 1f)); // mais fundo no meio
        h *= 1f - 0.5f * edge * Mathf.Clamp01((radius + PondShore * 1.5f - d) / PondShore); // margem mais mansa
        return Mathf.Lerp(h, floor, k * flat * edge);
    }

    public static void Clear()
    {
        layout = null;
        cellInfo = null;
        ponds.Clear();
        GreatLake.Clear();
        macroMask = null;
        cellKind = null;
    }

    private static bool IsVillage(int x, int z) =>
        x >= Planet.VillageCorner.x && x < Planet.VillageCorner.x + 3 && z >= Planet.VillageCorner.y && z < Planet.VillageCorner.y + 3;

    private static byte KindOf(BlockType type, int x, int z)
    {
        if (IsVillage(x, z))
        {
            return 2;
        }

        switch (type)
        {
            case BlockType.Rio:
            case BlockType.Esquina:
            case BlockType.Base:
            case BlockType.Polo:
                return 2;
            case BlockType.Parque:
                return 3;
            case BlockType.Floresta:
            case BlockType.Campo:
            case BlockType.Lagoa:
            case BlockType.Lago: // Missão 20: lagoa natural (relevo), não mais o lago de enfeite plano
                return 1;
            default:
                return 0;
        }
    }

    private static byte KindAt(int x, int z)
    {
        if (cellKind == null || z < 0 || z >= Planet.Height)
        {
            return 2; // polos e além
        }

        return cellKind[z * Planet.Width + Planet.WrapX(x)];
    }

    // O bloco fica todo plano (altura 0)? Rio, lago, parque, Vilarejo, bases e polos.
    public static bool IsLevel(Vector2Int coord)
    {
        byte kind = KindAt(coord.x, coord.y);
        return kind >= 2;
    }

    // Máscara da ondulação geral, calculada uma vez por jogo numa grade de 25 m (dá a volta em X).
    private static void BuildMacroMask()
    {
        cellKind = new byte[Planet.Width * Planet.Height];
        for (int z = 0; z < Planet.Height; z++)
        {
            for (int x = 0; x < Planet.Width; x++)
            {
                cellKind[z * Planet.Width + x] = KindOf(layout.Get(new Vector2Int(x, z)).type, x, z);
            }
        }

        int perBlock = Mathf.RoundToInt(BlockSize / MaskStep);
        maskColumns = Planet.Width * perBlock;
        maskRows = Planet.Height * perBlock + 1;
        macroMask = new float[maskColumns * maskRows];
        int reach = Mathf.CeilToInt(LongFade / BlockSize) + 1;
        for (int j = 0; j < maskRows; j++)
        {
            float pz = j * MaskStep;
            int bz = Mathf.FloorToInt(pz / BlockSize);
            for (int i = 0; i < maskColumns; i++)
            {
                float px = i * MaskStep;
                int bx = Mathf.FloorToInt(px / BlockSize);
                float far = LongFade;
                float near = ShortFade;
                for (int cz = bz - reach; cz <= bz + reach && far > 0f; cz++)
                {
                    for (int cx = bx - reach; cx <= bx + reach; cx++)
                    {
                        byte kind = KindAt(cx, cz);
                        if (kind < 2)
                        {
                            continue;
                        }

                        float minX = cx * BlockSize;
                        float minZ = cz * BlockSize;
                        float ddx = Mathf.Max(Mathf.Max(minX - px, px - (minX + BlockSize)), 0f);
                        float ddz = Mathf.Max(Mathf.Max(minZ - pz, pz - (minZ + BlockSize)), 0f);
                        float d = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                        if (kind == 2)
                        {
                            far = Mathf.Min(far, d);
                        }
                        else
                        {
                            near = Mathf.Min(near, d);
                        }
                    }
                }

                macroMask[j * maskColumns + i] = Mathf.Min(Smooth(far / LongFade), Smooth(near / ShortFade));
            }
        }
    }

    private static float MacroMask(float mx, float mz)
    {
        if (macroMask == null)
        {
            return 0f;
        }

        float fx = mx / MaskStep;
        float fz = Mathf.Clamp(mz / MaskStep, 0f, maskRows - 1.001f);
        int i0 = Mathf.FloorToInt(fx);
        int j0 = Mathf.FloorToInt(fz);
        float tx = fx - i0;
        float tz = fz - j0;
        int a = ((i0 % maskColumns) + maskColumns) % maskColumns;
        int b = (a + 1) % maskColumns;
        float m00 = macroMask[j0 * maskColumns + a];
        float m10 = macroMask[j0 * maskColumns + b];
        float m01 = macroMask[(j0 + 1) * maskColumns + a];
        float m11 = macroMask[(j0 + 1) * maskColumns + b];
        return Mathf.Lerp(Mathf.Lerp(m00, m10, tx), Mathf.Lerp(m01, m11, tx), tz);
    }

    // Ondulação geral 0..1 (dois tamanhos de morro: 800 m e 400 m).
    private static float MacroShape(float mx, float mz)
    {
        int period = Mathf.RoundToInt(Planet.Width * BlockSize / 800f);
        float n = Perlin(mx / 800f, mz / 800f, period, 501) + 0.5f * Perlin(mx / 400f, mz / 400f, period * 2, 502);
        return Mathf.Clamp01(n / 1.5f * 1.3f + 0.5f);
    }

    public static bool IsWild(BlockType type) => type == BlockType.Floresta || type == BlockType.Campo || type == BlockType.Lagoa || type == BlockType.Lago;

    // Peso extra do sorteio de um bloco do planeta (usado pelo WorldLayout.GeneratePlanet).
    public static float LayoutBias(int x, int y, BlockType type)
    {
        if (!Planet.RegionsBuilt)
        {
            return 1f;
        }

        BiomeStyle s = StyleOf(Planet.RegionBiome(Planet.RegionAt(new Vector2Int(x, y))));
        switch (type)
        {
            // Mais construções no mapa (pedido do autor): bairros e prédios aparecem mais vezes.
            case BlockType.Bairro:
                return s.urban * 1.4f;
            case BlockType.Predios:
                return s.urban * 1.7f;
            case BlockType.Galpoes:
            case BlockType.Praca:
                return s.urban;
            case BlockType.Floresta:
            case BlockType.Campo:
                return s.wild;
            case BlockType.Fazenda:
                return s.farm;
            case BlockType.Lago:
            case BlockType.Parque:
                return s.lake;
            case BlockType.Rio:
                return s.river;
            default:
                return 1f;
        }
    }

    // ================================================================
    // Altura
    // ================================================================

    // Altura do chão num ponto LÓGICO (X pode ter dado voltas).
    public static float Height(float logicalX, float logicalZ)
    {
        if (!Active)
        {
            return 0f;
        }

        float u = (logicalX - Planet.GridOrigin.x) / BlockSize;
        float v = (logicalZ - Planet.GridOrigin.y) / BlockSize;
        float mx = Mod(logicalX - Planet.GridOrigin.x, Planet.Width * BlockSize);
        float mz = logicalZ - Planet.GridOrigin.y;
        float mask = MacroMask(mx, mz);
        float flat = Flatness(u, v);
        float h = 0f;
        if (flat > 0f || mask > 0f)
        {
            Blend(mx, mz, out float amp, out float baseH, out float ridge, out float sharp, out _, out _, out _, out float macro);
            h = macro * MacroShape(mx, mz) * mask;
            if (flat > 0f)
            {
                h += (baseH + amp * Shape(mx, mz, ridge, sharp)) * flat;
            }
        }

        // Missão 20: Grande Lago e rios (podem passar do fundo do mar raso: dá para mergulhar).
        h = Pond(mx, mz, Mathf.Max(h, SeaFloor), flat); // Missão 20: lagoas naturais
        return GreatLake.Shape(mx, mz, h, flat);
    }

    // 0 = tem que ficar plano (rua, cidade, rio...), 1 = relevo livre.
    private static float Flatness(float u, float v)
    {
        int bx = Mathf.FloorToInt(u);
        int bz = Mathf.FloorToInt(v);
        if (!IsWildCell(bx, bz, out _))
        {
            return 0f;
        }

        float px = u * BlockSize;
        float pz = v * BlockSize;
        float nearest = FadeDistance;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = bx + dx;
                int cz = bz + dz;
                float minX = cx * BlockSize;
                float minZ = cz * BlockSize;
                if (!IsWildCell(cx, cz, out byte roads))
                {
                    // O bloco inteiro é plano: distância até o retângulo dele.
                    float ddx = Mathf.Max(Mathf.Max(minX - px, px - (minX + BlockSize)), 0f);
                    float ddz = Mathf.Max(Mathf.Max(minZ - pz, pz - (minZ + BlockSize)), 0f);
                    nearest = Mathf.Min(nearest, Mathf.Sqrt(ddx * ddx + ddz * ddz));
                    continue;
                }

                if (roads == 0)
                {
                    continue;
                }

                Vector2 center = new Vector2(minX + BlockSize * 0.5f, minZ + BlockSize * 0.5f);
                for (int s = 0; s < 4; s++)
                {
                    if ((roads & (1 << s)) == 0)
                    {
                        continue;
                    }

                    Vector2 end = center + SideDir(s) * (BlockSize * 0.5f);
                    float d = SegmentDistance(new Vector2(px, pz), center, end) - RoadCorridor;
                    nearest = Mathf.Min(nearest, Mathf.Max(d, 0f));
                }
            }
        }

        float t = Mathf.Clamp01(nearest / FadeDistance);
        return t * t * (3f - 2f * t);
    }

    private static bool IsWildCell(int x, int z, out byte roads)
    {
        roads = 0;
        if (cellInfo == null || z < 0 || z >= Planet.Height)
        {
            return false; // polos e além: planos
        }

        byte info = cellInfo[z * Planet.Width + Planet.WrapX(x)];
        roads = (byte)(info & 0x0F);
        return (info & 0x80) != 0;
    }

    private static Vector2 SideDir(int side)
    {
        switch (side)
        {
            case 0: return new Vector2(0f, 1f);
            case 1: return new Vector2(1f, 0f);
            case 2: return new Vector2(0f, -1f);
            default: return new Vector2(-1f, 0f);
        }
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    // Parâmetros do bioma misturados entre os 4 centros de bloco mais próximos (com o ponto entortado).
    private static void Blend(float mx, float mz, out float amp, out float baseH, out float ridge, out float sharp,
        out Color low, out Color high, out float snowLine, out float macro)
    {
        float wx = mx + 35f * Perlin(mx / 210f, mz / 210f, 61, 101);
        float wz = mz + 35f * Perlin(mx / 210f + 17.3f, mz / 210f + 4.1f, 61, 202);
        float u = wx / BlockSize - 0.5f;
        float v = wz / BlockSize - 0.5f;
        int x0 = Mathf.FloorToInt(u);
        int z0 = Mathf.FloorToInt(v);
        float tx = Smooth(u - x0);
        float tz = Smooth(v - z0);

        amp = baseH = ridge = sharp = snowLine = macro = 0f;
        low = high = Color.clear;
        for (int k = 0; k < 4; k++)
        {
            int ox = k & 1;
            int oz = k >> 1;
            float w = (ox == 1 ? tx : 1f - tx) * (oz == 1 ? tz : 1f - tz);
            int cz = Mathf.Clamp(z0 + oz, 0, Planet.Height - 1);
            Planet.Biome biome = Planet.RegionBiome(Planet.RegionAt(new Vector2Int(Planet.WrapX(x0 + ox), cz)));
            BiomeStyle s = StyleOf(biome);
            macro += MacroAmplitude[Mathf.Clamp((int)biome, 0, MacroAmplitude.Length - 1)] * w;
            amp += s.amplitude * w;
            baseH += s.baseHeight * w;
            ridge += s.ridge * w;
            sharp += s.sharpness * w;
            snowLine += (s.snowLine > 0f ? s.snowLine : 999f) * w;
            low += s.low * w;
            high += s.high * w;
        }
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);

    // Missão 20: cor da grama misturada entre os biomas vizinhos (mesma mistura do chão), para a troca
    // de bioma ser gradual e não um "corte" na borda do bloco. Mesma fórmula do BlockBuilder.GrassBlades.
    public static void GrassColors(float logicalX, float logicalZ, out Color pe, out Color ponta)
    {
        float mx = Mod(logicalX - Planet.GridOrigin.x, Planet.Width * BlockSize);
        float mz = logicalZ - Planet.GridOrigin.y;
        float wx = mx + 35f * Perlin(mx / 210f, mz / 210f, 61, 101);
        float wz = mz + 35f * Perlin(mx / 210f + 17.3f, mz / 210f + 4.1f, 61, 202);
        float u = wx / BlockSize - 0.5f;
        float v = wz / BlockSize - 0.5f;
        int x0 = Mathf.FloorToInt(u);
        int z0 = Mathf.FloorToInt(v);
        float tx = Smooth(u - x0);
        float tz = Smooth(v - z0);
        Color low = Color.clear, foliage = Color.clear;
        for (int k = 0; k < 4; k++)
        {
            int ox = k & 1;
            int oz = k >> 1;
            float w = (ox == 1 ? tx : 1f - tx) * (oz == 1 ? tz : 1f - tz);
            int cz = Mathf.Clamp(z0 + oz, 0, Planet.Height - 1);
            BiomeStyle s = StyleOf(Planet.RegionBiome(Planet.RegionAt(new Vector2Int(Planet.WrapX(x0 + ox), cz))));
            low += s.low * w;
            foliage += s.foliage * w;
        }

        pe = Color.Lerp(low, foliage, 0.35f) * 0.62f;
        ponta = Color.Lerp(Color.Lerp(low, foliage, 0.65f), Color.white, 0.12f);
        pe.a = ponta.a = 1f;
    }

    // Forma 0..1: morros (fBm) misturados com cristas (ridged).
    private static float Shape(float mx, float mz, float ridge, float sharp)
    {
        float hills = 0f;
        float ridges = 0f;
        float weight = 1f;
        float total = 0f;
        float cell = 320f;
        int period = Mathf.RoundToInt(Planet.Width * BlockSize / cell); // 40, 80, 160, 320: o ruído dá a volta junto com o planeta
        for (int octave = 0; octave < 4; octave++)
        {
            float n = Perlin(mx / cell, mz / cell, period, octave * 31 + 7);
            hills += n * weight;
            float r = 1f - Mathf.Abs(n) * 1.4f;
            ridges += r * r * weight;
            total += weight;
            weight *= 0.5f;
            cell *= 0.5f;
            period *= 2;
        }

        hills = Mathf.Clamp01(hills / total * 1.15f + 0.5f);
        ridges = Mathf.Clamp01(ridges / total);
        float shape = Mathf.Lerp(hills, ridges, ridge);
        return Mathf.Pow(shape, Mathf.Max(0.5f, sharp));
    }

    // Perlin 2D (gradientes) que se repete a cada "period" células no eixo X. Resultado ~ -0,7..0,7.
    private static float Perlin(float x, float z, int period, int salt)
    {
        int x0 = Mathf.FloorToInt(x);
        int z0 = Mathf.FloorToInt(z);
        float fx = x - x0;
        float fz = z - z0;
        float g00 = Grad(x0, z0, period, salt, fx, fz);
        float g10 = Grad(x0 + 1, z0, period, salt, fx - 1f, fz);
        float g01 = Grad(x0, z0 + 1, period, salt, fx, fz - 1f);
        float g11 = Grad(x0 + 1, z0 + 1, period, salt, fx - 1f, fz - 1f);
        float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
        float uz = fz * fz * fz * (fz * (fz * 6f - 15f) + 10f);
        return Mathf.Lerp(Mathf.Lerp(g00, g10, ux), Mathf.Lerp(g01, g11, ux), uz);
    }

    private static float Grad(int ix, int iz, int period, int salt, float dx, float dz)
    {
        int wx = ((ix % period) + period) % period;
        uint h = (uint)(wx * 374761393 + iz * 668265263 + noiseSeed * 2147483 + salt * 1013904223);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        float angle = (h & 0xFFFF) / 65536f * Mathf.PI * 2f;
        return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dz;
    }

    private static float Mod(float a, float m)
    {
        float r = a % m;
        return r < 0f ? r + m : r;
    }

    // ================================================================
    // Cor do chão
    // ================================================================

    public static Color GroundColor(float logicalX, float logicalZ, float height)
    {
        float mx = Mod(logicalX - Planet.GridOrigin.x, Planet.Width * BlockSize);
        float mz = logicalZ - Planet.GridOrigin.y;
        Blend(mx, mz, out float amp, out _, out _, out _, out Color low, out Color high, out float snowLine, out float macro);
        amp += macro;

        // Manchas: o chão não fica de uma cor só.
        float patch = Perlin(mx / 23f, mz / 23f, Mathf.RoundToInt(Planet.Width * BlockSize / 23f), 401) * 0.6f
                    + Perlin(mx / 7f, mz / 7f, Mathf.RoundToInt(Planet.Width * BlockSize / 7f), 402) * 0.4f;
        float t = Mathf.Clamp01(height / Mathf.Max(2f, amp) * 1.2f + patch * 0.35f + 0.15f);
        Color c = Color.Lerp(low, high, t);
        c *= 1f + patch * 0.10f;

        // Neve no alto (Picos Gelados).
        if (height > snowLine - 4f)
        {
            c = Color.Lerp(c, Snow, Mathf.Clamp01((height - snowLine + 4f) / 6f + patch * 0.3f));
        }

        // Praia e fundo do mar.
        bool icy = low.r > 0.75f && low.b > 0.85f;
        if (height < WaterLevel + 0.8f && !icy)
        {
            c = Color.Lerp(c, Sand, Mathf.Clamp01((WaterLevel + 0.8f - height) / 0.6f));
        }

        if (height < WaterLevel - 0.3f)
        {
            c = Color.Lerp(c, Seabed, Mathf.Clamp01((WaterLevel - 0.3f - height) / 1.2f) * 0.7f);
        }

        c.a = 1f;
        return c;
    }

    // ================================================================
    // Montagem do chão de um bloco (chamado pelo BlockBuilder)
    // ================================================================

    // Como o chão do bloco é montado: 0 = plano (rio, lago, parque, Vilarejo, bases), 1 = só a
    // ondulação geral (cidade, fazenda...), 2 = relevo forte (floresta e campo).
    public static int GroundMode(Vector2Int coord, BlockType type)
    {
        if (!Active || type == BlockType.Polo || IsLevel(coord))
        {
            return 0;
        }

        return IsWild(type) ? 2 : 1;
    }

    // Malha do chão do bloco com relevo e textura do bioma.
    // Devolve o objeto do chão; "minHeight" = ponto mais baixo (para saber se tem água).
    public static GameObject BuildGround(Transform parent, Vector2Int coord, Material template, int mode, BlockAssets assets, out float minHeight)
    {
        float originX = Planet.GridOrigin.x + coord.x * BlockSize;
        float originZ = Planet.GridOrigin.y + coord.y * BlockSize;
        bool wild = mode >= 1; // tem altura (nome antigo: os dois modos com relevo calculam a altura)
        int cells = mode == 2 ? 50 : mode == 1 ? 25 : 20; // 2 m no relevo forte, 4 m na ondulação, 5 m no plano
        float step = BlockSize / cells;
        int row = cells + 1;

        Vector3[] vertices = new Vector3[row * row];
        Vector2[] uvs = new Vector2[row * row];
        minHeight = float.MaxValue;
        for (int j = 0; j <= cells; j++)
        {
            for (int i = 0; i <= cells; i++)
            {
                float lx = i * step;
                float lz = j * step;
                float h = wild ? Height(originX + lx, originZ + lz) : 0f;
                minHeight = Mathf.Min(minHeight, h);
                vertices[j * row + i] = new Vector3(lx, h, lz);
                uvs[j * row + i] = new Vector2(lx / BlockSize, lz / BlockSize);
            }
        }

        int[] triangles = new int[cells * cells * 6];
        int t = 0;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                int a = j * row + i;
                triangles[t++] = a;
                triangles[t++] = a + row;
                triangles[t++] = a + 1;
                triangles[t++] = a + 1;
                triangles[t++] = a + row;
                triangles[t++] = a + row + 1;
            }
        }

        Mesh mesh = new Mesh { name = $"Chao_{coord.x}_{coord.y}" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        FixBorderNormals(mesh, vertices, row, step, originX, originZ, wild);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        assets.Add(mesh);

        // Textura do chão: 64 × 64 (~1,6 m por pixel).
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            name = $"Chao_{coord.x}_{coord.y}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 2,
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float lx = (x + 0.5f) / size * BlockSize;
                float lz = (y + 0.5f) / size * BlockSize;
                float h = wild ? GridHeight(vertices, row, step, lx, lz) : 0f; // da malha (bem mais rápido)
                pixels[y * size + x] = GroundColor(originX + lx, originZ + lz, h);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(true, true); // sem cópia na CPU
        assets.Add(texture);

        Material material = new Material(template) { name = "Chao_Bioma" };
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }

        material.color = Color.white;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }

        assets.Add(material);

        GameObject ground = new GameObject("Chao");
        ground.transform.SetParent(parent, false);
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        ground.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (wild)
        {
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
        else
        {
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(BlockSize * 0.5f, -0.1f, BlockSize * 0.5f);
            box.size = new Vector3(BlockSize, 0.2f, BlockSize);
        }

        if (minHeight == float.MaxValue)
        {
            minHeight = 0f;
        }

        return ground;
    }

    private static float GridHeight(Vector3[] vertices, int row, float step, float lx, float lz)
    {
        float fx = Mathf.Clamp(lx / step, 0f, row - 1.001f);
        float fz = Mathf.Clamp(lz / step, 0f, row - 1.001f);
        int i = Mathf.FloorToInt(fx);
        int j = Mathf.FloorToInt(fz);
        float tx = fx - i;
        float tz = fz - j;
        float a = vertices[j * row + i].y;
        float b = vertices[j * row + i + 1].y;
        float c = vertices[(j + 1) * row + i].y;
        float d = vertices[(j + 1) * row + i + 1].y;
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    // As normais da borda são calculadas com a altura de FORA do bloco também: sem isso a luz faz uma
    // "costura" visível entre blocos vizinhos.
    private static void FixBorderNormals(Mesh mesh, Vector3[] vertices, int row, float step, float originX, float originZ, bool wild)
    {
        if (!wild)
        {
            return;
        }

        Vector3[] normals = mesh.normals;
        int last = row - 1;
        for (int j = 0; j <= last; j++)
        {
            for (int i = 0; i <= last; i++)
            {
                if (i != 0 && j != 0 && i != last && j != last)
                {
                    continue;
                }

                Vector3 p = vertices[j * row + i];
                float x = originX + p.x;
                float z = originZ + p.z;
                float hl = Height(x - step, z);
                float hr = Height(x + step, z);
                float hd = Height(x, z - step);
                float hu = Height(x, z + step);
                normals[j * row + i] = new Vector3(hl - hr, 2f * step, hd - hu).normalized;
            }
        }

        mesh.normals = normals;
    }

    // ================================================================
    // Objetos do bloco: sobem no relevo e ganham a cor do bioma
    // ================================================================

    private static readonly Dictionary<(int, int), Material> tintCache = new Dictionary<(int, int), Material>();

    public static Planet.Biome BiomeOfBlock(Vector2Int coord) =>
        Planet.RegionBiome(Planet.RegionAt(new Vector2Int(Planet.WrapX(coord.x), Mathf.Clamp(coord.y, 0, Planet.Height - 1))));

    // Copas e arbustos com a cor do bioma (Floresta Chiclete = rosa, Gelo = branco...).
    public static void TintFoliage(Transform root, Planet.Biome biome)
    {
        BiomeStyle style = StyleOf(biome);
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            string n = renderer.gameObject.name;
            if (n != "Copa" && n != "Arbusto")
            {
                continue;
            }

            Material original = renderer.sharedMaterial;
            if (original == null)
            {
                continue;
            }

            (int, int) key = (original.GetInstanceID(), (int)biome);
            if (!tintCache.TryGetValue(key, out Material tinted) || tinted == null)
            {
                tinted = new Material(original) { name = original.name + "_" + biome };
                Color baseColor = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.color;
                Color color = Color.Lerp(baseColor, style.foliage, 0.8f);
                color.a = baseColor.a;
                tinted.color = color;
                if (tinted.HasProperty("_BaseColor"))
                {
                    tinted.SetColor("_BaseColor", color);
                }

                tintCache[key] = tinted; // compartilhado entre blocos (não é destruído com o bloco)
            }

            renderer.sharedMaterial = tinted;
        }
    }

    // Leva cada objeto do bloco para o relevo:
    // - peças baixas e compridas feitas de cubo (rua, calçada, faixa, cruzamento, cerca, canteiro)
    //   são DRAPEADAS: viram uma malha que acompanha o chão (e ganham colisor da mesma forma);
    // - o resto (casas, prédios, postes, árvores, carros, caixas) SOBE inteiro: as pequenas pela altura
    //   do centro, as grandes pelo ponto mais baixo embaixo delas (assim nada fica flutuando).
    // Nos blocos selvagens, some com o que ficou debaixo d'água e rareia as árvores conforme o bioma.
    public static void SettleObjects(Transform blockRoot, Transform staticRoot, Transform dynamicRoot, Vector2Int coord,
        Planet.Biome biome, int seed, bool wild, BlockAssets assets)
    {
        float originX = Planet.GridOrigin.x + coord.x * BlockSize;
        float originZ = Planet.GridOrigin.y + coord.y * BlockSize;
        BiomeStyle style = StyleOf(biome);
        System.Random rng = new System.Random(seed ^ 0x5bd1e995);

        List<Transform> items = new List<Transform>();
        foreach (Transform group in staticRoot)
        {
            if (group.name == "Chao" || group.name == "Agua_Mar")
            {
                continue;
            }

            foreach (Transform child in group)
            {
                items.Add(child);
            }
        }

        foreach (Transform child in dynamicRoot)
        {
            items.Add(child);
        }

        foreach (Transform item in items)
        {
            Vector3 local = blockRoot.InverseTransformPoint(item.position);
            float h = Height(originX + local.x, originZ + local.z);
            if (wild)
            {
                bool tree = item.name == "Arvore";
                bool drop = h < WaterLevel + 0.25f;
                if (!drop && tree)
                {
                    drop = rng.NextDouble() > style.treeKeep || style.snowLine > 0f && h > style.snowLine;
                }

                if (drop)
                {
                    Object.Destroy(item.gameObject);
                    continue;
                }
            }

            if (CanDrape(item))
            {
                Drape(item, blockRoot, originX, originZ, assets);
                continue;
            }

            float lift = FootprintHeight(item, blockRoot, originX, originZ, h);
            if (Mathf.Abs(lift) > 0.01f)
            {
                item.position += Vector3.up * (lift - 0.05f); // afunda um pouquinho: no morro não fica flutuando
            }
        }
    }

    // Cubo sozinho (sem filhos e sem física), baixo e comprido, ou bem fininho (faixa da rua).
    private static bool CanDrape(Transform item)
    {
        if (item.childCount > 0 || item.GetComponent<Rigidbody>() != null)
        {
            return false;
        }

        MeshFilter filter = item.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || filter.sharedMesh.name != "Cube")
        {
            return false;
        }

        Vector3 size = item.lossyScale;
        float wide = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z));
        float tall = Mathf.Abs(size.y);
        return tall < 0.1f || wide > 3.5f && tall <= 1.5f;
    }

    // Altura para subir um objeto inteiro: o centro (pequenos) ou o ponto mais baixo da base (grandes).
    private static float FootprintHeight(Transform item, Transform blockRoot, float originX, float originZ, float centerHeight)
    {
        // Casas do Blender têm fundação (base da cor do rodapé que desce 3 m): a altura de referência é a
        // FRENTE da casa (lado da rua, pedido do autor) — a porta fica sempre no nível do chão da frente.
        // Atrás, se o terreno desce, aparece a base; se sobe, a casa fica encaixada no morro.
        if (item.GetComponent<HouseBreakable>() != null)
        {
            Transform body = item.Find("Colisor_0");
            BoxCollider box = body != null ? body.GetComponent<BoxCollider>() : null;
            if (box != null)
            {
                float front = float.MinValue;
                for (int gx = 0; gx <= 2; gx++)
                {
                    // Pontos ao longo da frente (+Z da casa); o mais alto, para nenhum canto da frente enterrar.
                    Vector3 offset = new Vector3((gx - 1) * box.size.x * 0.45f, 0f, box.size.z / 2f);
                    Vector3 world = body.TransformPoint(box.center + offset);
                    Vector3 local = blockRoot.InverseTransformPoint(world);
                    front = Mathf.Max(front, Height(originX + local.x, originZ + local.z));
                }

                return front + 0.05f; // compensa o "afunda um pouquinho" de quem chama
            }
        }

        bool any = false;
        Bounds bounds = default;
        foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>())
        {
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!any || Mathf.Max(bounds.size.x, bounds.size.z) < 2f)
        {
            return centerHeight;
        }

        float lowest = centerHeight;
        for (int k = 0; k < 4; k++)
        {
            Vector3 corner = new Vector3((k & 1) == 0 ? bounds.min.x : bounds.max.x, 0f, (k & 2) == 0 ? bounds.min.z : bounds.max.z);
            Vector3 local = blockRoot.InverseTransformPoint(corner);
            lowest = Mathf.Min(lowest, Height(originX + local.x, originZ + local.z));
        }

        return lowest;
    }

    // Troca o cubo por um cubo picado (~3 m) cujos vértices sobem com o chão.
    private static void Drape(Transform item, Transform blockRoot, float originX, float originZ, BlockAssets assets)
    {
        Vector3 size = item.lossyScale;
        int nx = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(size.x) / 3f), 1, 40);
        int nz = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(size.z) / 3f), 1, 40);

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        Vector3 h = Vector3.one * 0.5f;
        CubeFace(new Vector3(-h.x, h.y, -h.z), Vector3.right, Vector3.forward, nx, nz, Vector3.up, vertices, uvs, triangles);
        CubeFace(new Vector3(-h.x, -h.y, -h.z), Vector3.right, Vector3.forward, nx, nz, Vector3.down, vertices, uvs, triangles);
        CubeFace(new Vector3(-h.x, -h.y, -h.z), Vector3.right, Vector3.up, nx, 1, Vector3.back, vertices, uvs, triangles);
        CubeFace(new Vector3(-h.x, -h.y, h.z), Vector3.right, Vector3.up, nx, 1, Vector3.forward, vertices, uvs, triangles);
        CubeFace(new Vector3(-h.x, -h.y, -h.z), Vector3.forward, Vector3.up, nz, 1, Vector3.left, vertices, uvs, triangles);
        CubeFace(new Vector3(h.x, -h.y, -h.z), Vector3.forward, Vector3.up, nz, 1, Vector3.right, vertices, uvs, triangles);

        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 world = item.TransformPoint(vertices[i]);
            Vector3 local = blockRoot.InverseTransformPoint(world);
            world.y += Height(originX + local.x, originZ + local.z);
            vertices[i] = item.InverseTransformPoint(world);
        }

        Mesh mesh = new Mesh { name = "Drapeado_" + item.name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        assets.Add(mesh);
        item.GetComponent<MeshFilter>().sharedMesh = mesh;

        BoxCollider box = item.GetComponent<BoxCollider>();
        if (box != null)
        {
            Object.DestroyImmediate(box);
            item.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }

    // Uma face do cubo de lado 1 em grade nu × nv, com os triângulos virados para "normal".
    private static void CubeFace(Vector3 corner, Vector3 u, Vector3 v, int nu, int nv, Vector3 normal,
        List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
    {
        int start = vertices.Count;
        for (int j = 0; j <= nv; j++)
        {
            for (int i = 0; i <= nu; i++)
            {
                float a = (float)i / nu;
                float b = (float)j / nv;
                vertices.Add(corner + u * a + v * b);
                uvs.Add(new Vector2(a, b));
            }
        }

        int row = nu + 1;
        bool flip = Vector3.Dot(Vector3.Cross(v, u), normal) < 0f; // Unity: frente = Cross(b - a, c - a)
        for (int j = 0; j < nv; j++)
        {
            for (int i = 0; i < nu; i++)
            {
                int k = start + j * row + i;
                if (flip)
                {
                    triangles.Add(k); triangles.Add(k + 1); triangles.Add(k + row);
                    triangles.Add(k + 1); triangles.Add(k + row + 1); triangles.Add(k + row);
                }
                else
                {
                    triangles.Add(k); triangles.Add(k + row); triangles.Add(k + 1);
                    triangles.Add(k + 1); triangles.Add(k + row); triangles.Add(k + row + 1);
                }
            }
        }
    }

    // Altura do chão em coordenadas do MUNDO (com a origem flutuante). 0 fora do planeta.
    public static float SurfaceY(Vector3 world)
    {
        if (!Active)
        {
            return 0f;
        }

        Vector3 logical = WorldOrigin.ToLogical(world);
        float h = Height(logical.x, logical.z);
        return WorldOrigin.ToWorld(new Vector3(logical.x, h, logical.z)).y;
    }

    // Mar/lagoa do relevo: uma lâmina de água na altura WaterLevel e o gatilho de nado.
    public static void AddWater(Transform staticRoot, Transform triggerRoot, Material water, BlockAssets assets)
    {
        GameObject sheet = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(sheet.GetComponent<Collider>());
        sheet.name = "Agua_Mar";
        sheet.transform.SetParent(staticRoot, false);
        sheet.transform.localPosition = new Vector3(BlockSize * 0.5f, WaterLevel - 0.01f, BlockSize * 0.5f);
        sheet.transform.localScale = new Vector3(BlockSize, 0.02f, BlockSize);
        if (water != null)
        {
            sheet.GetComponent<Renderer>().sharedMaterial = water;
        }

        GameObject trigger = new GameObject("Mar_Gatilho");
        trigger.transform.SetParent(triggerRoot, false);
        float bottom = GreatLake.DeepBottom - 2f; // Missão 20: lago e rios fundos
        trigger.transform.localPosition = new Vector3(BlockSize * 0.5f, (bottom + WaterLevel) * 0.5f, BlockSize * 0.5f);
        BoxCollider box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(BlockSize, WaterLevel - bottom, BlockSize);
        trigger.AddComponent<WaterZone>();
    }

    // Grama do bioma para os blocos que ainda usam caixas (beira do rio, anel da esquina).
    public static Material BiomeGrass(Material grass, Planet.Biome biome)
    {
        (int, int) key = (grass.GetInstanceID(), 1000 + (int)biome);
        if (!tintCache.TryGetValue(key, out Material tinted) || tinted == null)
        {
            BiomeStyle style = StyleOf(biome);
            tinted = new Material(grass) { name = grass.name + "_" + biome };
            Color color = Color.Lerp(style.low, style.high, 0.3f);
            tinted.color = color;
            if (tinted.HasProperty("_BaseColor"))
            {
                tinted.SetColor("_BaseColor", color);
            }

            if (tinted.HasProperty("_BaseMap"))
            {
                tinted.SetTexture("_BaseMap", null);
            }

            tintCache[key] = tinted;
        }

        return tinted;
    }
}
