using System.Collections.Generic;
using UnityEngine;

// Monta um bloco de 100 × 100 m a partir da receita (BlockSpec) — Missão 18.
// Funciona no jogo (blocos sorteados, montados ao carregar) e no editor (blocos fixos do Vilarejo,
// salvos como prefabs para poderem ser editados à mão).
// Mesma receita (tipo + lados + semente) = mesmo bloco, sempre.
//
// Padrão das bordas: rua de 10 m no meio do lado (calçadas de 3 m dos dois lados);
// rio de 25 m no meio do lado (margens em rampa, fundo raso).
// Coordenadas: a raiz do bloco fica no canto sudoeste; o centro do bloco é (50, 50).
public static partial class BlockBuilder
{
    public const float Size = 100f;
    private const float Half = Size / 2f;
    private const float RoadHalf = 5f;
    private const float WalkWidth = 3f;
    private const float WalkHeight = 0.15f;
    private const float Corridor = RoadHalf + WalkWidth + 1f; // faixa reservada para rua + calçada

    // Rio (mesmas medidas do Vilarejo original)
    private const float RiverHalf = 12.5f;
    private const float BankLength = 5f;
    private const float RiverBedY = -1.2f;
    private const float WaterY = -0.35f;

    private static readonly Vector2[] Dir = { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f) };
    private static readonly Vector2 Center = new Vector2(Half, Half);

    private sealed class Ctx
    {
        public BlockKit kit;
        public BlockSpec spec;
        public System.Random rng;
        public Transform root;
        public Transform est;
        public Transform dyn;
        public Transform trig;
        public readonly List<Rect> reserved = new List<Rect>();
        public readonly bool[] arm = new bool[4];
        public Vector2Int coord;
        public bool terrain;          // Missão 20: planeta com relevo e biomas
        public Planet.Biome biome;
        public Material grass;        // grama das caixas (com a cor do bioma no planeta)
        public BlockAssets assets;
        public bool[] blocked;        // Missão 20: onde não nasce planta (BlockedMap), células de 1 m
        public bool AnyRoad => arm[0] || arm[1] || arm[2] || arm[3];
    }

    // ---------- Entrada ----------

    // "position" = canto sudoeste do bloco no MUNDO. O bloco já nasce no lugar: se fosse montado na
    // origem e movido depois, os objetos com física (carros) podiam ficar presos lá e ser arremessados.
    public static GameObject Build(BlockSpec spec, BlockKit kit, Vector2Int coord, Vector3 position, string name = null)
    {
        GameObject rootObject = new GameObject(name ?? $"Bloco_{coord.x}_{coord.y}_{spec.type}");
        rootObject.transform.position = position;
        Ctx c = new Ctx
        {
            kit = kit,
            spec = spec,
            rng = new System.Random(spec.seed),
            root = rootObject.transform,
            coord = coord,
        };

        c.terrain = PlanetTerrain.Active && spec.type != BlockType.Polo;
        c.grass = c.kit.grass;
        if (c.terrain)
        {
            c.biome = PlanetTerrain.BiomeOfBlock(coord);
            c.assets = rootObject.AddComponent<BlockAssets>();
            if (c.kit.grass != null)
            {
                c.grass = PlanetTerrain.BiomeGrass(c.kit.grass, c.biome);
            }
        }

        c.est = Child(c.root, "Estatico");
        c.dyn = Child(c.root, "Dinamico");
        c.trig = Child(c.root, "Gatilhos"); // fora do NavMesh
        for (int s = 0; s < 4; s++)
        {
            c.arm[s] = spec.Get(s) == Socket.Rua;
        }

        switch (spec.type)
        {
            case BlockType.Rio:
                BuildRiverBlock(c);
                break;
            case BlockType.Esquina:
                BuildEsquinaRing(c);
                break;
            case BlockType.Polo:
                BuildPole(c);
                break;
            default:
                Ground(c);
                if (c.AnyRoad)
                {
                    Roads(c, RoadHalf);
                }

                Fill(c);
                GroundCover(c); // Missão 20: grama, flores, pedrinhas (Kit de Natureza)
                break;
        }

        if (c.terrain)
        {
            // Missão 20: ruas e calçadas acompanham o relevo, o resto sobe para a altura do chão;
            // copas e arbustos com a cor do bioma.
            int mode = PlanetTerrain.GroundMode(coord, spec.type);
            if (mode > 0)
            {
                PlanetTerrain.SettleObjects(c.root, c.est, c.dyn, coord, c.biome, spec.seed, mode == 2, c.assets);
            }

            PlanetTerrain.TintFoliage(c.root, c.biome);
        }

        WaterLook.Apply(rootObject.transform, kit.water); // Missão 20: água transparente

        WorldBlock block = rootObject.AddComponent<WorldBlock>();
        block.Setup(coord, c.est, c.dyn);
        block.SetSpec(spec);
        Physics.SyncTransforms();
        return rootObject;
    }

    private static void Fill(Ctx c)
    {
        switch (c.spec.type)
        {
            case BlockType.Bairro: Bairro(c); break;
            case BlockType.Parque: Parque(c); break;
            case BlockType.Galpoes: Galpoes(c); break;
            case BlockType.Floresta: Floresta(c); break;
            case BlockType.Fazenda: Fazenda(c); break;
            case BlockType.Praca: Praca(c); break;
            case BlockType.Predios: Predios(c); break;
            case BlockType.Lago: if (c.terrain) Lagoa(c); else Lago(c); break; // Missão 20: no planeta é lagoa natural (relevo)
            case BlockType.Campo: Campo(c); break;
            case BlockType.Lagoa: Lagoa(c); break;
            case BlockType.Base: BaseBlock(c); break;
        }

        StreetProps(c);
        if (c.terrain && c.biome == Planet.Biome.Antenas && c.kit.satelliteDish != null &&
            (c.spec.type == BlockType.Floresta || c.spec.type == BlockType.Campo || c.spec.type == BlockType.Fazenda))
        {
            AntennaField(c);
        }
    }

    // Morro das Antenas: parabólicas espalhadas (umas gigantes), viradas para lados diferentes.
    private static void AntennaField(Ctx c)
    {
        Transform group = Child(c.est, "Antenas");
        if (c.rng.NextDouble() < 0.35)
        {
            Scatter(c, 1, 12f, p => Antenna(c, group, p, Range(c, 2.2f, 2.8f)));
        }

        Scatter(c, c.rng.Next(2, 5), 5f, p => Antenna(c, group, p, Range(c, 1f, 1.6f)));
    }

    // Uma antena parabólica destrutível (base de ~1 m), com luz vermelha piscando no topo.
    private static Transform Antenna(Ctx c, Transform parent, Vector2 p, float scale)
    {
        Transform root = Child(parent, "Antena");
        root.localPosition = new Vector3(p.x, 0f, p.y);
        root.localRotation = Quaternion.Euler(0f, Range(c, 0f, 360f), 0f);

        GameObject dish = KitModel(c.kit.satelliteDish, "Parabolica", root, Vector3.zero, 0f, scale, 0f);
        Object.DestroyImmediate(dish.GetComponent<BoxCollider>());
        Bounds bounds = LocalBounds(dish);
        float top = (bounds.max.y - bounds.min.y) * scale;

        // Colisor só no poste (o prato fica no alto e não atrapalha quem passa embaixo).
        Transform pole = Child(root, "Poste");
        BoxCollider box = pole.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, top * 0.4f, 0f);
        box.size = new Vector3(1.1f * scale, top * 0.8f, 1.1f * scale);

        GameObject lamp = NoCollider(Prim(c, PrimitiveType.Sphere, "Luz_Piscando", root, new Vector3(0f, top + 0.15f * scale, 0f),
            Vector3.one * 0.35f * scale, Mat(c, "Antena_Luz", new Color(1f, 0.15f, 0.1f), 0.3f, 5f)));
        lamp.AddComponent<BlinkLight>();

        root.gameObject.AddComponent<DestructibleStructure>();
        return root;
    }

    // ---------- Chão e ruas ----------

    private static void Ground(Ctx c)
    {
        if (c.terrain && c.kit.grass != null)
        {
            // Missão 20: chão com relevo e textura do bioma.
            int mode = PlanetTerrain.GroundMode(c.coord, c.spec.type);
            PlanetTerrain.BuildGround(c.est, c.coord, c.kit.grass, mode, c.assets, out float lowest);
            if (mode == 2 && lowest < PlanetTerrain.WaterLevel)
            {
                PlanetTerrain.AddWater(c.est, c.trig, c.kit.water, c.assets);
            }

            return;
        }

        Box(c, c.est, "Chao", new Vector3(Half, -0.1f, Half), new Vector3(Size, 0.2f, Size), c.kit.grass);
    }

    // Ruas do centro até cada lado com rua. "inner" = onde a rua começa (5 = no centro; 40 = depois da esquina feita à mão).
    private static void Roads(Ctx c, float inner)
    {
        Transform roads = Child(c.est, "Ruas");
        bool fromCenter = inner <= RoadHalf + 0.01f;

        if (fromCenter)
        {
            Box(c, roads, "Cruzamento", new Vector3(Half, 0.01f, Half), new Vector3(RoadHalf * 2f, 0.02f, RoadHalf * 2f), c.kit.asphalt);
            Reserve(c, RectAround(Center, Corridor * 2f, Corridor * 2f));

            // Cantos das calçadas e "tampas" onde não há rua.
            float corner = RoadHalf + WalkWidth / 2f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Box(c, roads, "Calcada", new Vector3(Half + sx * corner, WalkHeight / 2f, Half + sz * corner),
                        new Vector3(WalkWidth, WalkHeight, WalkWidth), c.kit.sidewalk);
                }
            }

            for (int s = 0; s < 4; s++)
            {
                if (c.arm[s])
                {
                    continue;
                }

                Vector2 p = Center + Dir[s] * corner;
                Vector3 size = Dir[s].x != 0f ? new Vector3(WalkWidth, WalkHeight, RoadHalf * 2f) : new Vector3(RoadHalf * 2f, WalkHeight, WalkWidth);
                Box(c, roads, "Calcada", new Vector3(p.x, WalkHeight / 2f, p.y), size, c.kit.sidewalk);
            }
        }

        for (int s = 0; s < 4; s++)
        {
            if (c.arm[s])
            {
                RoadArm(c, roads, s, inner, Half);
            }
        }
    }

    // Um braço de rua (com faixas, calçadas e postes) ao longo do lado "s", de "from" até "to" (distância do centro).
    private static void RoadArm(Ctx c, Transform parent, int s, float from, float to)
    {
        Vector2 d = Dir[s];
        Vector2 perp = new Vector2(d.y, d.x); // perpendicular (eixo do outro lado)
        bool alongX = d.x != 0f;
        float length = to - from;
        Vector2 mid = Center + d * (from + length / 2f);

        Box(c, parent, "Rua", new Vector3(mid.x, 0.01f, mid.y),
            alongX ? new Vector3(length, 0.02f, RoadHalf * 2f) : new Vector3(RoadHalf * 2f, 0.02f, length), c.kit.asphalt);

        for (float t = from + 2f; t < to - 1f; t += 4f)
        {
            Vector2 p = Center + d * t;
            NoCollider(Box(c, parent, "Faixa", new Vector3(p.x, 0.025f, p.y),
                alongX ? new Vector3(2f, 0.01f, 0.25f) : new Vector3(0.25f, 0.01f, 2f), c.kit.laneMark));
        }

        float walkFrom = Mathf.Max(from, RoadHalf + WalkWidth);
        float walkLength = to - walkFrom;
        if (walkLength > 0.1f)
        {
            Vector2 walkMid = Center + d * (walkFrom + walkLength / 2f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 p = walkMid + perp * side * (RoadHalf + WalkWidth / 2f);
                Box(c, parent, "Calcada", new Vector3(p.x, WalkHeight / 2f, p.y),
                    alongX ? new Vector3(walkLength, WalkHeight, WalkWidth) : new Vector3(WalkWidth, WalkHeight, walkLength), c.kit.sidewalk);
            }
        }

        // Postes alternando os lados.
        int lampSide = c.rng.Next(2) == 0 ? -1 : 1;
        for (float t = Mathf.Max(from + 6f, 18f); t < to - 3f; t += 22f)
        {
            Vector2 p = Center + d * t + perp * lampSide * (RoadHalf + 2.45f); // beira de fora da calçada
            Lamp(c, parent, p);
            lampSide = -lampSide;
        }

        Vector2 rectCenter = Center + d * (from + length / 2f);
        Reserve(c, alongX ? RectAround(rectCenter, length, Corridor * 2f) : RectAround(rectCenter, Corridor * 2f, length));
    }

    // Objetos soltos na calçada e carros estacionados.
    private static void StreetProps(Ctx c)
    {
        if (!c.AnyRoad || c.spec.type == BlockType.Esquina)
        {
            return;
        }

        for (int s = 0; s < 4; s++)
        {
            if (!c.arm[s])
            {
                continue;
            }

            Vector2 d = Dir[s];
            Vector2 perp = new Vector2(d.y, d.x);
            float carChance = c.spec.type == BlockType.Floresta || c.spec.type == BlockType.Campo ? 0.2f : 0.55f;
            if (c.rng.NextDouble() < carChance && c.kit.carModel != null)
            {
                int side = c.rng.Next(2) == 0 ? -1 : 1;
                Vector2 p = Center + d * Range(c, 16f, 44f) + perp * side * 6.2f; // longe dos postes
                ParkedCar(c, new Vector3(p.x, 0.05f, p.y), d.x != 0f ? 0f : 90f);
            }

            int bins = c.rng.Next(0, 3);
            for (int i = 0; i < bins; i++)
            {
                int side = c.rng.Next(2) == 0 ? -1 : 1;
                Vector2 p = Center + d * Range(c, 17f, 47f) + perp * side * 7.6f;
                if (c.rng.NextDouble() < 0.5)
                {
                    Bin(c, new Vector3(p.x, WalkHeight + 0.55f, p.y));
                }
                else
                {
                    Crate(c, new Vector3(p.x, WalkHeight + 0.5f, p.y));
                }
            }
        }
    }

    // ---------- Esquina (feita à mão no Vilarejo) ----------

    // A esquina original ocupa 80 × 80 m no meio do bloco; aqui só o anel de 10 m em volta e as pontas das ruas.
    private static void BuildEsquinaRing(Ctx c)
    {
        Transform ground = Child(c.est, "Chao_Anel");
        Box(c, ground, "Chao_N", new Vector3(Half, -0.1f, 95f), new Vector3(Size, 0.2f, 10f), c.grass);
        Box(c, ground, "Chao_S", new Vector3(Half, -0.1f, 5f), new Vector3(Size, 0.2f, 10f), c.grass);
        Box(c, ground, "Chao_O", new Vector3(5f, -0.1f, Half), new Vector3(10f, 0.2f, 80f), c.grass);
        Box(c, ground, "Chao_L", new Vector3(95f, -0.1f, Half), new Vector3(10f, 0.2f, 80f), c.grass);

        Transform roads = Child(c.est, "Ruas");
        for (int s = 0; s < 4; s++)
        {
            if (c.arm[s])
            {
                RoadArm(c, roads, s, 40f, Half);
            }
        }

        Reserve(c, RectAround(Center, 80f, 80f));
        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 10, 2.5f, p => Tree(c, trees, p, 1f));
    }

    // ---------- Rio ----------

    // Montado com o rio no eixo X (leste-oeste) dentro de um "pivô" no centro, girado 90° se o rio for norte-sul.
    private static void BuildRiverBlock(Ctx c)
    {
        bool straight = c.spec.Count(Socket.Rio) == 2 && c.spec.north == c.spec.south;
        if (!straight)
        {
            BuildRiverPieces(c); // Missão 20: curva ou nascente
            return;
        }

        bool northSouth = c.spec.north == Socket.Rio;
        bool bridge = northSouth ? c.arm[1] : c.arm[0];

        Transform pivot = Child(c.est, "Rio");
        pivot.localPosition = new Vector3(Half, 0f, Half);
        pivot.localRotation = Quaternion.Euler(0f, northSouth ? 90f : 0f, 0f);

        // Chão das duas margens.
        float groundDepth = Half - RiverHalf;
        Local(c, pivot, "Chao_Norte", new Vector3(0f, -0.1f, RiverHalf + groundDepth / 2f), new Vector3(Size, 0.2f, groundDepth), c.grass);
        Local(c, pivot, "Chao_Sul", new Vector3(0f, -0.1f, -(RiverHalf + groundDepth / 2f)), new Vector3(Size, 0.2f, groundDepth), c.grass);

        // Fundo, margens em rampa e água.
        float bedHalf = RiverHalf - BankLength;
        Local(c, pivot, "Fundo", new Vector3(0f, RiverBedY - 0.1f, 0f), new Vector3(Size, 0.2f, bedHalf * 2f), c.kit.riverBed);
        float drop = -0.02f - RiverBedY;
        float slope = Mathf.Sqrt(BankLength * BankLength + drop * drop);
        float angle = Mathf.Atan2(drop, BankLength) * Mathf.Rad2Deg;
        float rampY = (-0.02f + RiverBedY) / 2f - 0.1f;
        Local(c, pivot, "Margem_Norte", new Vector3(0f, rampY, RiverHalf - BankLength / 2f), new Vector3(Size, 0.2f, slope), c.kit.riverBed, new Vector3(-angle, 0f, 0f));
        Local(c, pivot, "Margem_Sul", new Vector3(0f, rampY, -(RiverHalf - BankLength / 2f)), new Vector3(Size, 0.2f, slope), c.kit.riverBed, new Vector3(angle, 0f, 0f));
        NoCollider(Local(c, pivot, "Agua", new Vector3(0f, WaterY, 0f), new Vector3(Size, 0.02f, RiverHalf * 2f), c.kit.water));

        // Gatilho da água (anda mais devagar, respingos).
        Transform trigger = Child(c.trig, "Agua_Gatilho");
        trigger.localPosition = new Vector3(Half, (RiverBedY + WaterY) / 2f, Half);
        trigger.localRotation = pivot.localRotation;
        BoxCollider box = trigger.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(Size, WaterY - RiverBedY + 0.2f, RiverHalf * 2f);
        trigger.gameObject.AddComponent<WaterZone>();

        // Ponte e ruas nas margens.
        if (bridge)
        {
            float deck = RiverHalf * 2f + 6f;
            Transform ponte = Child(pivot, "Ponte");
            Local(c, ponte, "Tabuleiro", new Vector3(0f, -0.18f, 0f), new Vector3(RoadHalf * 2f + 2f, 0.4f, deck), c.kit.asphalt);
            Local(c, ponte, "Mureta_O", new Vector3(-(RoadHalf + 0.85f), 0.5f, 0f), new Vector3(0.3f, 1f, deck), c.kit.lampPole);
            Local(c, ponte, "Mureta_L", new Vector3(RoadHalf + 0.85f, 0.5f, 0f), new Vector3(0.3f, 1f, deck), c.kit.lampPole);
            for (int side = -1; side <= 1; side += 2)
            {
                Transform lamp = Child(ponte, "Poste");
                lamp.localPosition = new Vector3(side * (RoadHalf + 0.85f), 0f, side * 8f);
                Prim(c, PrimitiveType.Cylinder, "Haste", lamp, new Vector3(0f, 3f, 0f), new Vector3(0.2f, 3f, 0.2f), c.kit.lampPole);
                NoCollider(Prim(c, PrimitiveType.Sphere, "Luz", lamp, new Vector3(0f, 6.3f, 0f), Vector3.one * 0.7f, c.kit.lampLight));
            }

            // Ruas das duas margens até a ponte (em coordenadas do bloco).
            int sideA = northSouth ? 1 : 0;
            int sideB = northSouth ? 3 : 2;
            Transform roads = Child(c.est, "Ruas");
            RoadArm(c, roads, sideA, RiverHalf + 3f, Half);
            RoadArm(c, roads, sideB, RiverHalf + 3f, Half);
        }

        // Reserva a faixa do rio (em coordenadas do bloco).
        float band = (RiverHalf + 2f) * 2f;
        Reserve(c, northSouth ? RectAround(Center, band, Size) : RectAround(Center, Size, band));

        // Píeres na margem sul (cais do Vilarejo).
        if (c.spec.variant == "cais")
        {
            Transform piers = Child(pivot, "Pieres");
            foreach (float u in new[] { -32f, -12f, 18f, 36f })
            {
                if (bridge && Mathf.Abs(u) < 10f)
                {
                    continue;
                }

                Local(c, piers, "Pier", new Vector3(u, -0.18f, -RiverHalf + 5f), new Vector3(6f, 0.4f, 12f), c.kit.wood);
            }
        }

        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 14, 2.5f, p => Tree(c, trees, p, Range(c, 0.9f, 1.2f)));
        Transform bushes = Child(c.est, "Arbustos");
        Scatter(c, 8, 1.5f, p => Bush(c, bushes, p));
        StreetProps(c);
    }

    // Curva ou nascente (Missão 20). O bloco é dividido em 3 × 3: o quadrado do meio (25 m), um "braço"
    // para cada lado e os 4 cantos. Os braços com rio e o meio viram canal (fundo, margens em rampa,
    // água e gatilho), o resto é chão. Na nascente, o canal acaba no meio do bloco com pedras em volta.
    private static void BuildRiverPieces(Ctx c)
    {
        Transform rio = Child(c.est, "Rio");
        float w = RiverHalf;
        float arm = Half - w;
        bool[] river = new bool[4];
        for (int s = 0; s < 4; s++)
        {
            river[s] = c.spec.Get(s) == Socket.Rio;
        }

        // Cantos: sempre chão.
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Box(c, rio, "Chao_Canto", new Vector3(Half + sx * (w + arm / 2f), -0.1f, Half + sz * (w + arm / 2f)), new Vector3(arm, 0.2f, arm), c.grass);
            }
        }

        // Meio: margem nos lados sem rio.
        bool[] centerWalls = new bool[4];
        for (int s = 0; s < 4; s++)
        {
            centerWalls[s] = !river[s];
        }

        RiverChannel(c, rio, Center, new Vector2(w * 2f, w * 2f), centerWalls);

        // Braços.
        for (int s = 0; s < 4; s++)
        {
            Vector2 at = Center + Dir[s] * (w + arm / 2f);
            bool alongZ = s == 0 || s == 2;
            Vector2 size = alongZ ? new Vector2(w * 2f, arm) : new Vector2(arm, w * 2f);
            if (river[s])
            {
                RiverChannel(c, rio, at, size, alongZ ? new[] { false, true, false, true } : new[] { true, false, true, false });
            }
            else
            {
                Box(c, rio, "Chao", new Vector3(at.x, -0.1f, at.y), new Vector3(size.x, 0.2f, size.y), c.grass);
            }
        }

        // Curva: arredonda o canto de dentro (senão fica um degrau entre as duas margens).
        for (int s = 0; s < 4; s++)
        {
            int next = (s + 1) % 4;
            if (river[s] && river[next])
            {
                Vector2 corner = Center + (Dir[s] + Dir[next]) * w;
                float height = -0.02f - RiverBedY;
                Prim(c, PrimitiveType.Cylinder, "Margem_Curva", rio, new Vector3(corner.x, RiverBedY + height / 2f, corner.y),
                    new Vector3(BankLength * 2f, height / 2f, BankLength * 2f), c.kit.riverBed);
            }
        }

        // Nascente: pedras no fim do canal.
        if (c.spec.Count(Socket.Rio) == 1)
        {
            int inlet = System.Array.IndexOf(river, true);
            Vector2 back = -Dir[inlet];
            Vector2 side = new Vector2(back.y, back.x);
            Transform rocks = Child(c.est, "Nascente");
            Rock(c, rocks, Center + back * (w + 2f), Range(c, 1.6f, 2.2f));
            Rock(c, rocks, Center + back * (w + 1f) + side * 6f, Range(c, 0.9f, 1.4f));
            Rock(c, rocks, Center + back * (w + 1.5f) - side * 7f, Range(c, 1f, 1.5f));
            Reserve(c, RectAround(Center + back * (w + 2f), 18f, 18f));
        }

        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 14, 2.5f, p => Tree(c, trees, p, Range(c, 0.9f, 1.2f)));
        Transform bushes = Child(c.est, "Arbustos");
        Scatter(c, 10, 1.5f, p => Bush(c, bushes, p));
        StreetProps(c);
    }

    // Um trecho de canal (retângulo em coordenadas do bloco). "walls" = lados com margem em rampa.
    private static void RiverChannel(Ctx c, Transform parent, Vector2 center, Vector2 size, bool[] walls)
    {
        float minX = center.x - size.x / 2f, maxX = center.x + size.x / 2f;
        float minZ = center.y - size.y / 2f, maxZ = center.y + size.y / 2f;
        NoCollider(Box(c, parent, "Agua", new Vector3(center.x, WaterY, center.y), new Vector3(size.x, 0.02f, size.y), c.kit.water));

        // Fundo: o retângulo sem as rampas.
        float bx0 = minX + (walls[3] ? BankLength : 0f), bx1 = maxX - (walls[1] ? BankLength : 0f);
        float bz0 = minZ + (walls[2] ? BankLength : 0f), bz1 = maxZ - (walls[0] ? BankLength : 0f);
        Box(c, parent, "Fundo", new Vector3((bx0 + bx1) / 2f, RiverBedY - 0.1f, (bz0 + bz1) / 2f), new Vector3(bx1 - bx0, 0.2f, bz1 - bz0), c.kit.riverBed);

        float drop = -0.02f - RiverBedY;
        float slope = Mathf.Sqrt(BankLength * BankLength + drop * drop);
        float angle = Mathf.Atan2(drop, BankLength) * Mathf.Rad2Deg;
        float rampY = (-0.02f + RiverBedY) / 2f - 0.1f;
        if (walls[0]) Local(c, parent, "Margem", new Vector3(center.x, rampY, maxZ - BankLength / 2f), new Vector3(size.x, 0.2f, slope), c.kit.riverBed, new Vector3(-angle, 0f, 0f));
        if (walls[2]) Local(c, parent, "Margem", new Vector3(center.x, rampY, minZ + BankLength / 2f), new Vector3(size.x, 0.2f, slope), c.kit.riverBed, new Vector3(angle, 0f, 0f));
        if (walls[1]) Local(c, parent, "Margem", new Vector3(maxX - BankLength / 2f, rampY, center.y), new Vector3(slope, 0.2f, size.y), c.kit.riverBed, new Vector3(0f, 0f, angle));
        if (walls[3]) Local(c, parent, "Margem", new Vector3(minX + BankLength / 2f, rampY, center.y), new Vector3(slope, 0.2f, size.y), c.kit.riverBed, new Vector3(0f, 0f, -angle));

        Transform trigger = Child(c.trig, "Agua_Gatilho");
        trigger.localPosition = new Vector3(center.x, (RiverBedY + WaterY) / 2f, center.y);
        BoxCollider box = trigger.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(size.x, WaterY - RiverBedY + 0.2f, size.y);
        trigger.gameObject.AddComponent<WaterZone>();

        Reserve(c, RectAround(center, size.x + 4f, size.y + 4f));
    }

    // ---------- Tipos ----------

    private static void Bairro(Ctx c)
    {
        Transform houses = Child(c.est, "Casas");
        if (c.kit.retroHouses)
        {
            // Bairro cheio (pedido do autor): 4 casas por lado de rua, quase sem vão, e uma 2ª fileira atrás.
            AlongRoads(c, new[] { 13.5f, 24f, 34.5f, 45f }, 0.95f, (p, yaw, faceOffset) => House(c, houses, p, yaw), 9.5f, 9.5f, 2.5f);
            AlongRoads(c, new[] { 13.5f, 24f, 34.5f, 45f }, 0.6f, (p, yaw, faceOffset) => House(c, houses, p, yaw), 9.5f, 9.5f, 14f);
        }
        else
        {
            AlongRoads(c, new[] { 17f, 30f, 43f }, 0.85f, (p, yaw, faceOffset) => House(c, houses, p, yaw), 8.5f, 9.5f, 2.5f);
        }

        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 10, 2.2f, p => Tree(c, trees, p, Range(c, 0.8f, 1.1f)));
        Transform bushes = Child(c.est, "Arbustos");
        Scatter(c, 8, 1.2f, p => Bush(c, bushes, p));
    }

    private static void Predios(Ctx c)
    {
        Transform buildings = Child(c.est, "Predios");
        AlongRoads(c, new[] { 19f, 38f }, 1f, (p, yaw, f) => Building(c, buildings, p, yaw), 15f, 15f, 2f);
        // 2ª fileira de prédios atrás da primeira (cidade mais densa, pedido do autor).
        AlongRoads(c, new[] { 19f, 38f }, 0.75f, (p, yaw, f) => Building(c, buildings, p, yaw), 15f, 13f, 18f);
        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 6, 2.2f, p => Tree(c, trees, p, 0.9f));
        Transform benches = Child(c.est, "Bancos");
        Scatter(c, 4, 1.5f, p => Bench(c, benches, p));
    }

    private static void Galpoes(Ctx c)
    {
        Transform sheds = Child(c.est, "Galpoes");
        AlongRoads(c, new[] { 22f, 40f }, 0.9f, (p, yaw, f) => Warehouse(c, sheds, p, yaw), 22f, 14f, 3f);

        // Pilhas de contêineres (precisam da Força Telecinética: 2.200 kg).
        int stacks = c.rng.Next(1, 4);
        for (int i = 0; i < stacks; i++)
        {
            Vector2 p = RandomPoint(c, 8f);
            Rect area = RectAround(p, 12f, 8f);
            if (!Free(c, area))
            {
                continue;
            }

            Reserve(c, area);
            for (int k = 0; k < 4; k++)
            {
                Vector3 pos = new Vector3(p.x - 4.5f + k * 3f, 1.3f, p.y);
                Container(c, pos);
                if (k % 2 == 0 && c.rng.NextDouble() < 0.6)
                {
                    Container(c, pos + Vector3.up * 2.65f);
                }
            }
        }

        Scatter(c, 6, 1.5f, p =>
        {
            Crate(c, new Vector3(p.x, 0.5f, p.y));
            if (c.rng.NextDouble() < 0.5) Crate(c, new Vector3(p.x + 0.2f, 1.5f, p.y));
        });

        // Sci-Fi Kit: caixas grandes e grupinhos de barris (alguns explodem).
        if (c.kit.largeCrates != null && c.kit.largeCrates.Length > 0)
        {
            Scatter(c, c.rng.Next(1, 3), 4.5f, p => LargeCrate(c, p));
        }

        // Depósito de barris: fileiras arrumadas (como num pátio de oficina), todos do mesmo tipo.
        // Um depósito por galpão; metade dos depósitos é de barris explosivos (avermelhados).
        if (c.kit.barrels != null && c.kit.barrels.Length > 0)
        {
            Scatter(c, 1, 6f, p =>
            {
                bool explosive = c.rng.NextDouble() < 0.5;
                GameObject model = Pick(c, c.kit.barrels);
                int rows = c.rng.Next(2, 4);
                int columns = c.rng.Next(3, 5);
                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        Vector2 spot = p + new Vector2((column - (columns - 1) * 0.5f) * 0.95f, (row - (rows - 1) * 0.5f) * 0.95f);
                        Barrel(c, spot, 0f, explosive, model);
                    }
                }
            });
        }
    }

    private static void Parque(Ctx c)
    {
        // Laguinho.
        Vector2 lake = RandomPoint(c, 10f);
        Vector2 lakeSize = new Vector2(Range(c, 16f, 24f), Range(c, 11f, 16f));
        Rect lakeRect = RectAround(lake, lakeSize.x + 4f, lakeSize.y + 4f);
        if (Free(c, lakeRect))
        {
            NoCollider(Prim(c, PrimitiveType.Cylinder, "Lago", c.est, new Vector3(lake.x, 0.02f, lake.y), new Vector3(lakeSize.x, 0.02f, lakeSize.y), c.kit.water));
            Reserve(c, lakeRect);
        }

        // Caminhos em cruz (ou ligando as ruas ao centro).
        if (!c.AnyRoad)
        {
            Transform paths = Child(c.est, "Caminhos");
            NoCollider(Box(c, paths, "Caminho", new Vector3(Half, 0.02f, Half), new Vector3(2.5f, 0.02f, Size * 1.3f), c.kit.path, 45f));
            NoCollider(Box(c, paths, "Caminho", new Vector3(Half, 0.02f, Half), new Vector3(2.5f, 0.02f, Size * 1.3f), c.kit.path, -45f));
        }

        // Parquinho: caixa de areia, balanço e escorregador.
        Vector2 play = RandomPoint(c, 6f);
        Rect playRect = RectAround(play, 12f, 12f);
        if (Free(c, playRect))
        {
            Reserve(c, playRect);
            Transform playground = Child(c.est, "Parquinho");
            playground.localPosition = new Vector3(play.x, 0f, play.y);
            Local(c, playground, "Areia", new Vector3(-2.5f, 0.1f, 0f), new Vector3(5f, 0.2f, 5f), c.kit.sand);
            Local(c, playground, "Balanco_Poste_A", new Vector3(2f, 1.25f, -2f), new Vector3(0.2f, 2.5f, 0.2f), c.kit.lampPole);
            Local(c, playground, "Balanco_Poste_B", new Vector3(2f, 1.25f, 2f), new Vector3(0.2f, 2.5f, 0.2f), c.kit.lampPole);
            Local(c, playground, "Balanco_Barra", new Vector3(2f, 2.5f, 0f), new Vector3(0.2f, 0.2f, 4.2f), c.kit.lampPole);
            NoCollider(Local(c, playground, "Balanco_Assento", new Vector3(2f, 0.6f, 0f), new Vector3(0.5f, 0.08f, 1f), c.kit.wood));
            Local(c, playground, "Escorregador", new Vector3(4.5f, 0.9f, 0f), new Vector3(1f, 0.15f, 4f), c.kit.awningColors != null && c.kit.awningColors.Length > 0 ? c.kit.awningColors[0] : c.kit.bench, new Vector3(-25f, 0f, 0f));
        }

        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 22, 2.5f, p => Tree(c, trees, p, Range(c, 0.9f, 1.3f)));
        Transform benches = Child(c.est, "Bancos");
        Scatter(c, 7, 1.5f, p => Bench(c, benches, p));
        Transform lamps = Child(c.est, "Postes");
        Scatter(c, 3, 1f, p => Lamp(c, lamps, p, 0f));
    }

    private static void Floresta(Ctx c)
    {
        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 65, 2f, p => Tree(c, trees, p, Range(c, 0.8f, 1.6f)));
        Transform rocks = Child(c.est, "Pedras");
        Scatter(c, 9, 2f, p => Rock(c, rocks, p, Range(c, 0.8f, 2.4f)));
        Transform bushes = Child(c.est, "Arbustos");
        Scatter(c, 18, 1.2f, p => Bush(c, bushes, p));
    }

    // Pedaço do Grande Lago (Missão 20): o relevo faz a água (PlanetTerrain + GreatLake); aqui só a
    // margem: pedras, arbustos e algumas árvores. O que cair dentro d'água some no SettleObjects.
    private static void Lagoa(Ctx c)
    {
        Transform rocks = Child(c.est, "Pedras");
        Scatter(c, 6, 1.5f, p => Rock(c, rocks, p, Range(c, 0.7f, 1.8f)));
        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 5, 2.5f, p => Tree(c, trees, p, Range(c, 1f, 1.3f)));
        Transform bushes = Child(c.est, "Arbustos");
        Scatter(c, 18, 1.2f, p => Bush(c, bushes, p));
    }

    private static void Campo(Ctx c)
    {
        Transform rocks = Child(c.est, "Pedras");
        Vector2 big = RandomPoint(c, 6f);
        if (Free(c, RectAround(big, 10f, 10f)))
        {
            Reserve(c, RectAround(big, 10f, 10f));
            Rock(c, rocks, big, 3.5f);
            Rock(c, rocks, big + new Vector2(2.8f, 1.5f), 2.2f);
            Rock(c, rocks, big + new Vector2(-2f, 2.4f), 1.6f);
        }

        Scatter(c, 7, 1.5f, p => Rock(c, rocks, p, Range(c, 0.6f, 1.6f)));
        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 7, 2.5f, p => Tree(c, trees, p, Range(c, 1f, 1.4f)));
        Transform bushes = Child(c.est, "Arbustos");
        Scatter(c, 22, 1.2f, p => Bush(c, bushes, p));
        if (Nature(c) != null && Has(Nature(c).flowers))
        {
            return; // flores de verdade vêm na vegetação rasteira (GroundCover)
        }

        Transform flowers = Child(c.est, "Flores");
        for (int i = 0; i < 40; i++)
        {
            Vector2 p = RandomPoint(c, 0.5f);
            if (Free(c, RectAround(p, 0.5f, 0.5f)))
            {
                NoCollider(Box(c, flowers, "Flor", new Vector3(p.x, 0.2f, p.y), new Vector3(0.3f, 0.4f, 0.3f), c.kit.flower));
            }
        }
    }

    private static void Fazenda(Ctx c)
    {
        // Celeiro (destrutível) e silo perto do centro.
        Transform farm = Child(c.est, "Fazenda");
        Vector2 barnPos = NearCenter(c, 16f, 20f);
        if (barnPos.x >= 0f)
        {
            float yaw = c.rng.Next(4) * 90f;
            Barn(c, farm, barnPos, yaw);
            Vector2 silo = barnPos + Rotate(new Vector2(11f, 4f), yaw);
            if (Free(c, RectAround(silo, 7f, 7f)))
            {
                Reserve(c, RectAround(silo, 7f, 7f));
                Prim(c, PrimitiveType.Cylinder, "Silo", farm, new Vector3(silo.x, 6f, silo.y), new Vector3(5f, 6f, 5f), c.kit.warehouse);
                Prim(c, PrimitiveType.Sphere, "Silo_Topo", farm, new Vector3(silo.x, 12f, silo.y), new Vector3(5f, 3f, 5f), c.kit.warehouseRoof);
            }
        }

        // Plantações.
        Transform fields = Child(c.est, "Plantacoes");
        for (int i = 0; i < 4; i++)
        {
            Vector2 size = new Vector2(Range(c, 18f, 30f), Range(c, 14f, 24f));
            Vector2 p = RandomPoint(c, Mathf.Max(size.x, size.y) / 2f);
            Rect area = RectAround(p, size.x + 2f, size.y + 2f);
            if (!Free(c, area))
            {
                continue;
            }

            Reserve(c, area);
            Box(c, fields, "Terra", new Vector3(p.x, 0.03f, p.y), new Vector3(size.x, 0.06f, size.y), c.kit.soil);
            for (float z = p.y - size.y / 2f + 1.5f; z < p.y + size.y / 2f - 1f; z += 2.5f)
            {
                NoCollider(Box(c, fields, "Fileira", new Vector3(p.x, 0.3f, z), new Vector3(size.x - 2f, 0.5f, 0.6f), c.kit.crop));
            }

            // Cerca baixa (só visual).
            float fx = size.x / 2f + 0.8f;
            float fz = size.y / 2f + 0.8f;
            NoCollider(Box(c, fields, "Cerca", new Vector3(p.x, 0.5f, p.y + fz), new Vector3(size.x + 1.6f, 0.15f, 0.12f), c.kit.fence));
            NoCollider(Box(c, fields, "Cerca", new Vector3(p.x, 0.5f, p.y - fz), new Vector3(size.x + 1.6f, 0.15f, 0.12f), c.kit.fence));
            NoCollider(Box(c, fields, "Cerca", new Vector3(p.x + fx, 0.5f, p.y), new Vector3(0.12f, 0.15f, size.y + 1.6f), c.kit.fence));
            NoCollider(Box(c, fields, "Cerca", new Vector3(p.x - fx, 0.5f, p.y), new Vector3(0.12f, 0.15f, size.y + 1.6f), c.kit.fence));
        }

        // Fardos de feno (300 kg, dá para arremessar).
        Scatter(c, 6, 1.8f, p =>
        {
            GameObject bale = Prim(c, PrimitiveType.Cylinder, "Fardo", c.dyn, new Vector3(p.x, 0.8f, p.y), new Vector3(1.6f, 0.7f, 1.6f), c.kit.hay,
                new Vector3(0f, Range(c, 0f, 360f), 90f));
            Throwable(bale, 300f);
        });

        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 6, 2.5f, p => Tree(c, trees, p, 1.1f));
    }

    private static void Praca(Ctx c)
    {
        // Um dos quatro cantos vira praça; os outros têm lojas.
        int qx = c.rng.Next(2) == 0 ? -1 : 1;
        int qz = c.rng.Next(2) == 0 ? -1 : 1;
        float inner = Corridor;
        float plazaSize = Half - inner - 1f;
        Vector2 plaza = Center + new Vector2(qx, qz) * (inner + plazaSize / 2f);
        Reserve(c, RectAround(plaza, plazaSize, plazaSize));

        Transform square = Child(c.est, "Praca");
        square.localPosition = new Vector3(plaza.x, 0f, plaza.y);
        Local(c, square, "Piso", new Vector3(0f, WalkHeight / 2f, 0f), new Vector3(plazaSize, WalkHeight, plazaSize), c.kit.sidewalk);
        Prim(c, PrimitiveType.Cylinder, "Chafariz", square, new Vector3(0f, 0.5f, 0f), new Vector3(8f, 0.35f, 8f), c.kit.lampPole);
        NoCollider(Prim(c, PrimitiveType.Cylinder, "Chafariz_Agua", square, new Vector3(0f, 0.87f, 0f), new Vector3(7.2f, 0.02f, 7.2f), c.kit.water));
        Prim(c, PrimitiveType.Cylinder, "Chafariz_Coluna", square, new Vector3(0f, 1.6f, 0f), new Vector3(0.8f, 1.1f, 0.8f), c.kit.lampPole);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Chafariz_Jato", square, new Vector3(0f, 2.9f, 0f), new Vector3(1.6f, 0.9f, 1.6f), c.kit.water));

        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + 45f;
            Vector2 offset = Rotate(new Vector2(0f, 9f), a);
            GameObject seat = Box(c, square, "Banco", new Vector3(offset.x, WalkHeight + 0.25f, offset.y), new Vector3(2f, 0.5f, 0.6f), c.kit.bench, a);
            seat.transform.localRotation = Quaternion.Euler(0f, a, 0f);
            Vector2 tree = Rotate(new Vector2(0f, plazaSize / 2f - 3f), a);
            TreeLocal(c, square, new Vector3(tree.x, WalkHeight, tree.y), 0.9f);
        }

        // Banca de jornal.
        Local(c, square, "Banca", new Vector3(-qx * (plazaSize / 2f - 4f), WalkHeight + 1.3f, -qz * (plazaSize / 2f - 4f)), new Vector3(3f, 2.6f, 2.2f), Pick(c, c.kit.awningColors) ?? c.kit.door);

        Transform shops = Child(c.est, "Lojas");
        AlongRoads(c, new[] { 18f, 33f }, 1f, (p, yaw, f) => Shop(c, shops, p, yaw), 12f, 9f, 1.5f);
    }

    private static void Lago(Ctx c)
    {
        Vector2 center = Center + new Vector2(Range(c, -6f, 6f), Range(c, -6f, 6f));
        Vector2 size = new Vector2(Range(c, 46f, 60f), Range(c, 36f, 48f));
        for (int s = 0; s < 4; s++)
        {
            if (c.arm[s])
            {
                center -= Dir[s] * 14f;
                size *= 0.75f;
            }
        }

        Transform lake = Child(c.est, "Lago");
        NoCollider(Prim(c, PrimitiveType.Cylinder, "Areia", lake, new Vector3(center.x, 0.015f, center.y), new Vector3(size.x + 6f, 0.015f, size.y + 6f), c.kit.sand));
        NoCollider(Prim(c, PrimitiveType.Cylinder, "Agua", lake, new Vector3(center.x, 0.04f, center.y), new Vector3(size.x, 0.02f, size.y), c.kit.water));
        Reserve(c, RectAround(center, size.x + 6f, size.y + 6f));

        // Gatilho da água (no meio do lago).
        Transform trigger = Child(c.trig, "Lago_Gatilho");
        trigger.localPosition = new Vector3(center.x, 0.5f, center.y);
        BoxCollider box = trigger.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(size.x * 0.65f, 1f, size.y * 0.65f);
        trigger.gameObject.AddComponent<WaterZone>();

        // Píer e barquinho (150 kg).
        float pierLength = size.y * 0.3f;
        Vector2 pierPos = center + new Vector2(0f, -size.y / 2f + pierLength / 2f - 1f);
        Box(c, lake, "Pier", new Vector3(pierPos.x, 0.25f, pierPos.y), new Vector3(3f, 0.3f, pierLength + 4f), c.kit.wood);
        GameObject boat = Box(c, c.dyn, "Barco", new Vector3(pierPos.x + 3.5f, 0.35f, pierPos.y + pierLength / 2f), new Vector3(1.5f, 0.6f, 3.6f), c.kit.wood);
        Throwable(boat, 150f);

        // Juncos na beira.
        for (int i = 0; i < 24; i++)
        {
            float a = (float)(c.rng.NextDouble() * Mathf.PI * 2f);
            Vector2 p = center + new Vector2(Mathf.Cos(a) * size.x / 2f, Mathf.Sin(a) * size.y / 2f) * Range(c, 0.92f, 1.02f);
            NoCollider(Prim(c, PrimitiveType.Cylinder, "Junco", lake, new Vector3(p.x, 0.6f, p.y), new Vector3(0.12f, 0.6f, 0.12f), c.kit.crop));
        }

        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 16, 2.5f, p => Tree(c, trees, p, Range(c, 0.9f, 1.3f)));
        Transform benches = Child(c.est, "Bancos");
        Scatter(c, 3, 1.5f, p => Bench(c, benches, p));
    }

    // ---------- Construções ----------

    // Coloca construções viradas para as ruas, dos dois lados de cada braço.
    // depthOffset: recuo além da calçada (jardim da frente).
    private static void AlongRoads(Ctx c, float[] slots, float chance, System.Action<Vector2, float, float> place,
        float width, float depth, float setback)
    {
        for (int s = 0; s < 4; s++)
        {
            if (!c.arm[s])
            {
                continue;
            }

            Vector2 d = Dir[s];
            Vector2 perp = new Vector2(d.y, d.x);
            foreach (float slot in slots)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    if (c.rng.NextDouble() > chance)
                    {
                        continue;
                    }

                    float offset = RoadHalf + WalkWidth + setback + depth / 2f;
                    Vector2 p = Center + d * slot + perp * side * offset;
                    Vector2 face = -perp * side; // frente para a rua
                    float yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;
                    bool alongX = Mathf.Abs(face.x) > 0.5f;
                    Rect area = alongX ? RectAround(p, depth + 1f, width + 1f) : RectAround(p, width + 1f, depth + 1f);
                    if (!Free(c, area))
                    {
                        continue;
                    }

                    Reserve(c, area);
                    place(p, yaw, offset);
                }
            }
        }
    }

    // Casa destrutível (paredes, telhado de duas águas, porta, janelas; às vezes sobrado e chaminé).
    private static void House(Ctx c, Transform parent, Vector2 p, float yaw)
    {
        if (c.kit.retroHouses)
        {
            RetroHouse.Build(parent, p, yaw, c.rng, c.kit.houseWalls != null && c.kit.houseWalls.Length > 0 ? c.kit.houseWalls[0] : null, c.kit.houseModels);
            return;
        }

        float w = Range(c, 8f, 9.5f);
        float d = Range(c, 8f, 9.5f);
        bool twoStory = c.rng.NextDouble() < 0.25;
        float wallH = twoStory ? 7.2f : Range(c, 4.2f, 4.8f);

        Transform house = Child(parent, "Casa");
        house.localPosition = new Vector3(p.x, 0f, p.y);
        house.localRotation = Quaternion.Euler(0f, yaw, 0f);

        Local(c, house, "Paredes", new Vector3(0f, wallH / 2f, 0f), new Vector3(w, wallH, d), Pick(c, c.kit.houseWalls));
        float roofSide = w / Mathf.Sqrt(2f);
        Local(c, house, "Telhado", new Vector3(0f, wallH, 0f), new Vector3(roofSide, roofSide, d + 0.6f), Pick(c, c.kit.houseRoofs), new Vector3(0f, 0f, 45f));
        float front = d / 2f + 0.1f;
        Local(c, house, "Porta", new Vector3(0f, 1.3f, front), new Vector3(1.6f, 2.6f, 0.2f), c.kit.door);
        Local(c, house, "Janela_E", new Vector3(-w * 0.31f, 2.6f, front), new Vector3(1.6f, 1.4f, 0.15f), c.kit.window);
        Local(c, house, "Janela_D", new Vector3(w * 0.31f, 2.6f, front), new Vector3(1.6f, 1.4f, 0.15f), c.kit.window);
        if (twoStory)
        {
            Local(c, house, "Janela_Cima_E", new Vector3(-w * 0.25f, 5.6f, front), new Vector3(1.4f, 1.2f, 0.15f), c.kit.window);
            Local(c, house, "Janela_Cima_D", new Vector3(w * 0.25f, 5.6f, front), new Vector3(1.4f, 1.2f, 0.15f), c.kit.window);
        }

        if (c.rng.NextDouble() < 0.4)
        {
            Local(c, house, "Chamine", new Vector3(w * 0.25f, wallH + w * 0.35f, -d * 0.2f), new Vector3(0.8f, 1.8f, 0.8f), c.kit.warehouseRoof);
        }

        house.gameObject.AddComponent<DestructibleStructure>();
    }

    private static void Shop(Ctx c, Transform parent, Vector2 p, float yaw)
    {
        float w = Range(c, 10f, 12f);
        float d = 8.5f;
        float h = Range(c, 5f, 6.5f);
        Transform shop = Child(parent, "Casa_Loja");
        shop.localPosition = new Vector3(p.x, 0f, p.y);
        shop.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Local(c, shop, "Paredes", new Vector3(0f, h / 2f, 0f), new Vector3(w, h, d), Pick(c, c.kit.houseWalls));
        Local(c, shop, "Telhado", new Vector3(0f, h + 0.2f, 0f), new Vector3(w + 0.4f, 0.4f, d + 0.4f), Pick(c, c.kit.houseRoofs));
        float front = d / 2f + 0.1f;
        Local(c, shop, "Vitrine", new Vector3(0f, 1.8f, front), new Vector3(w * 0.7f, 2.2f, 0.15f), c.kit.window);
        Local(c, shop, "Porta", new Vector3(w * 0.38f, 1.3f, front), new Vector3(1.4f, 2.6f, 0.2f), c.kit.door);
        Material awning = Pick(c, c.kit.awningColors) ?? c.kit.door;
        NoCollider(Local(c, shop, "Toldo", new Vector3(0f, 3.4f, front + 0.9f), new Vector3(w * 0.9f, 0.15f, 1.8f), awning, new Vector3(15f, 0f, 0f)));
        Local(c, shop, "Letreiro", new Vector3(0f, h - 0.9f, front), new Vector3(w * 0.6f, 0.9f, 0.2f), awning);
        shop.gameObject.AddComponent<DestructibleStructure>();
    }

    // Prédio alto (não desmorona: fica para a Superforça, decisão D15).
    private static void Building(Ctx c, Transform parent, Vector2 p, float yaw)
    {
        if (c.kit.buildingModels != null && c.kit.buildingModels.Length > 0)
        {
            RetroHouse.BuildBuilding(parent, p, yaw, c.rng, c.kit.houseWalls != null && c.kit.houseWalls.Length > 0 ? c.kit.houseWalls[0] : null, c.kit.buildingModels);
            return;
        }

        float w = Range(c, 12f, 15f);
        float h = Range(c, 12f, 34f);
        Transform building = Child(parent, "Predio");
        building.localPosition = new Vector3(p.x, 0f, p.y);
        building.localRotation = Quaternion.Euler(0f, yaw, 0f);
        Material body = Pick(c, c.kit.buildingColors) ?? c.kit.warehouse;
        Local(c, building, "Corpo", new Vector3(0f, h / 2f, 0f), new Vector3(w, h, w), body);
        float front = w / 2f + 0.1f;
        for (float y = 4f; y < h - 2f; y += 3.6f)
        {
            NoCollider(Local(c, building, "Janelas", new Vector3(0f, y, front), new Vector3(w * 0.8f, 1.3f, 0.12f), c.kit.window));
        }

        Local(c, building, "Porta", new Vector3(0f, 1.5f, front), new Vector3(3f, 3f, 0.2f), c.kit.door);
        Local(c, building, "Topo", new Vector3(0f, h + 0.3f, 0f), new Vector3(w + 0.4f, 0.6f, w + 0.4f), c.kit.warehouseRoof);
        if (c.rng.NextDouble() < 0.5)
        {
            Prim(c, PrimitiveType.Cylinder, "Antena", building, new Vector3(w * 0.25f, h + 3f, w * 0.25f), new Vector3(0.3f, 3f, 0.3f), c.kit.lampPole);
            NoCollider(Prim(c, PrimitiveType.Sphere, "Antena_Luz", building, new Vector3(w * 0.25f, h + 6.2f, w * 0.25f), Vector3.one * 0.6f, c.kit.lampLight));
        }
    }

    private static void Warehouse(Ctx c, Transform parent, Vector2 p, float yaw)
    {
        Transform shed = Child(parent, "Galpao");
        shed.localPosition = new Vector3(p.x, 0f, p.y);
        shed.localRotation = Quaternion.Euler(0f, yaw, 0f);
        float h = Range(c, 7.5f, 9.5f);
        Local(c, shed, "Paredes", new Vector3(0f, h / 2f, 0f), new Vector3(22f, h, 14f), c.kit.warehouse);
        Local(c, shed, "Teto", new Vector3(0f, h + 0.2f, 0f), new Vector3(23f, 0.4f, 15f), c.kit.warehouseRoof);
        Local(c, shed, "Portao", new Vector3(0f, 2.8f, 7.05f), new Vector3(7f, 5.6f, 0.2f), c.kit.door);
    }

    private static void Barn(Ctx c, Transform parent, Vector2 p, float yaw)
    {
        Transform barn = Child(parent, "Casa_Celeiro");
        barn.localPosition = new Vector3(p.x, 0f, p.y);
        barn.localRotation = Quaternion.Euler(0f, yaw, 0f);
        const float w = 11f, d = 15f, h = 6f;
        Local(c, barn, "Paredes", new Vector3(0f, h / 2f, 0f), new Vector3(w, h, d), c.kit.barn);
        float roofSide = w / Mathf.Sqrt(2f);
        Local(c, barn, "Telhado", new Vector3(0f, h, 0f), new Vector3(roofSide, roofSide, d + 0.8f), c.kit.barnRoof, new Vector3(0f, 0f, 45f));
        Local(c, barn, "Porta", new Vector3(0f, 2.2f, d / 2f + 0.1f), new Vector3(4f, 4.4f, 0.2f), c.kit.door);
        Local(c, barn, "X_Porta", new Vector3(0f, 2.2f, d / 2f + 0.22f), new Vector3(0.3f, 5.6f, 0.08f), c.kit.fence, new Vector3(0f, 0f, 42f));
        barn.gameObject.AddComponent<DestructibleStructure>();
    }

    // ---------- Natureza e objetos ----------

    private static void Tree(Ctx c, Transform parent, Vector2 p, float scale)
    {
        Transform tree = Child(parent, "Arvore");
        tree.localPosition = new Vector3(p.x, 0f, p.y);
        BuildTree(c, tree, scale);
    }

    private static void TreeLocal(Ctx c, Transform parent, Vector3 localPosition, float scale)
    {
        Transform tree = Child(parent, "Arvore");
        tree.localPosition = localPosition;
        BuildTree(c, tree, scale);
    }

    private static void BuildTree(Ctx c, Transform tree, float scale)
    {
        if (NatureTree(c, tree, scale))
        {
            return;
        }

        float canopy = (3.2f + (float)c.rng.NextDouble() * 1.6f) * scale;
        float trunkHeight = 2f * scale;
        Prim(c, PrimitiveType.Cylinder, "Tronco", tree, new Vector3(0f, trunkHeight, 0f), new Vector3(0.5f * scale, trunkHeight, 0.5f * scale), c.kit.trunk);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Copa", tree, new Vector3(0f, trunkHeight * 2f + canopy * 0.35f, 0f), Vector3.one * canopy, Pick(c, c.kit.canopies)));
    }

    private static void Bush(Ctx c, Transform parent, Vector2 p)
    {
        if (NatureBush(c, parent, p) != null)
        {
            return;
        }

        float s = Range(c, 1f, 2f);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Arbusto", parent, new Vector3(p.x, s * 0.35f, p.y), new Vector3(s, s * 0.8f, s), c.kit.bush));
    }

    private static void Rock(Ctx c, Transform parent, Vector2 p, float s)
    {
        if (NatureRock(c, parent, p, s) != null)
        {
            return;
        }

        Prim(c, PrimitiveType.Sphere, "Pedra", parent, new Vector3(p.x, s * 0.25f, p.y), new Vector3(s, s * 0.7f, s * Range(c, 0.8f, 1.2f)), c.kit.rock,
            new Vector3(0f, Range(c, 0f, 360f), 0f));
    }

    private static void Bench(Ctx c, Transform parent, Vector2 p)
    {
        Box(c, parent, "Banco", new Vector3(p.x, 0.25f, p.y), new Vector3(2f, 0.5f, 0.6f), c.kit.bench, Range(c, 0f, 360f));
    }

    private static void Lamp(Ctx c, Transform parent, Vector2 p, float baseY = WalkHeight)
    {
        Transform lamp = Child(parent, "Poste");
        lamp.localPosition = new Vector3(p.x, baseY, p.y);
        Prim(c, PrimitiveType.Cylinder, "Haste", lamp, new Vector3(0f, 3f, 0f), new Vector3(0.2f, 3f, 0.2f), c.kit.lampPole);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Luz", lamp, new Vector3(0f, 6.3f, 0f), Vector3.one * 0.7f, c.kit.lampLight));
    }

    private static void Crate(Ctx c, Vector3 p)
    {
        GameObject model = Pick(c, c.kit.crates);
        if (model != null)
        {
            // Sci-Fi Kit: caixa de 1 m de altura (mesmo lugar e peso da caixa antiga). "p" = centro.
            GameObject crate = KitModel(model, "Caixa", c.dyn, new Vector3(p.x, p.y - 0.5f, p.z), Range(c, 0f, 360f), 0f, 1f);
            Throwable(crate, 40f);
            return;
        }

        Throwable(Prim(c, PrimitiveType.Cube, "Caixa", c.dyn, p, Vector3.one, c.kit.crate, new Vector3(0f, Range(c, 0f, 90f), 0f)), 40f);
    }

    // Caixa grande do Sci-Fi Kit (galpões): ~3,5 × 1,5 × 1,5 m, 500 kg.
    private static void LargeCrate(Ctx c, Vector2 p)
    {
        GameObject model = Pick(c, c.kit.largeCrates);
        if (model != null)
        {
            Throwable(KitModel(model, "Caixa_Grande", c.dyn, new Vector3(p.x, 0f, p.y), Range(c, 0f, 360f), 1f, 0f), 500f);
        }
    }

    // Barril (Sci-Fi Kit). Os explosivos são avermelhados: arremessar ou bater forte = BUM.
    private static void Barrel(Ctx c, Vector2 p, float baseY, bool explosive, GameObject model = null)
    {
        model = model != null ? model : Pick(c, c.kit.barrels);
        if (model == null)
        {
            return;
        }

        GameObject barrel = KitModel(model, explosive ? "Barril_Explosivo" : "Barril", c.dyn, new Vector3(p.x, baseY, p.y), Range(c, 0f, 360f), 1f, 0f);
        Throwable(barrel, 60f);
        if (explosive)
        {
            foreach (Renderer part in barrel.GetComponentsInChildren<Renderer>())
            {
                part.sharedMaterials = System.Array.ConvertAll(part.sharedMaterials, m => Tinted(m, new Color(1f, 0.42f, 0.32f)));
            }

            barrel.AddComponent<ExplosiveVehicle>().ConfigureAsBarrel();
        }
    }

    // Instancia um modelo do kit com um colisor de caixa do tamanho dele. "baseLocal" = ponto no chão.
    // scale > 0 = escala fixa; height > 0 = escala para ficar com essa altura.
    private static GameObject KitModel(GameObject model, string name, Transform parent, Vector3 baseLocal, float yaw, float scale, float height)
    {
        GameObject go = Object.Instantiate(model, parent);
        go.name = name;
        go.transform.localPosition = baseLocal;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        foreach (Collider extra in go.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(extra);
        }

        Bounds bounds = LocalBounds(go);
        float s = height > 0f && bounds.size.y > 0.01f ? height / bounds.size.y : Mathf.Max(0.01f, scale);
        go.transform.localScale = Vector3.one * s;
        // Pivô na base: se o modelo começa um pouco acima ou abaixo de 0, corrige.
        go.transform.localPosition = baseLocal - Vector3.up * (bounds.min.y * s);

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return go;
    }

    // Caixa que envolve as malhas do objeto, no espaço do próprio objeto.
    private static Bounds LocalBounds(GameObject go)
    {
        bool any = false;
        Bounds result = default;
        foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            Bounds b = filter.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                Vector3 local = go.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!any)
                {
                    result = new Bounds(local, Vector3.zero);
                    any = true;
                }
                else
                {
                    result.Encapsulate(local);
                }
            }
        }

        return any ? result : new Bounds(Vector3.up * 0.5f, Vector3.one);
    }

    private static readonly Dictionary<(int, Color), Material> TintCache = new Dictionary<(int, Color), Material>();

    // Cópia do material com outra cor (compartilhada entre todos os objetos iguais).
    private static Material Tinted(Material source, Color color)
    {
        if (source == null)
        {
            return null;
        }

        (int, Color) key = (source.GetInstanceID(), color);
        if (!TintCache.TryGetValue(key, out Material tinted) || tinted == null)
        {
            tinted = new Material(source) { name = source.name + "_Tinta" };
            tinted.color = color;
            if (tinted.HasProperty("_BaseColor"))
            {
                tinted.SetColor("_BaseColor", color);
            }

            TintCache[key] = tinted;
        }

        return tinted;
    }

    private static void Bin(Ctx c, Vector3 p)
    {
        Throwable(Prim(c, PrimitiveType.Cylinder, "Lixeira", c.dyn, p, new Vector3(0.7f, 0.55f, 0.7f), c.kit.bin), 25f);
    }

    private static void Container(Ctx c, Vector3 p)
    {
        Throwable(Prim(c, PrimitiveType.Cube, "Conteiner", c.dyn, p, new Vector3(2.4f, 2.6f, 6f), Pick(c, c.kit.containerColors) ?? c.kit.warehouse), 2200f);
    }

    // Carro estacionado (comprido no eixo X do objeto, como os do Vilarejo): desligado, cores sorteadas, explode.
    private static void ParkedCar(Ctx c, Vector3 p, float yaw)
    {
        GameObject car = new GameObject("Carro");
        car.transform.SetParent(c.dyn, false);
        car.transform.localPosition = p;
        car.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        GameObject visual = Object.Instantiate(c.kit.carModel, car.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);
        visual.transform.localScale = Vector3.one;
        foreach (Collider extra in visual.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(extra);
        }

        BoxCollider hull = car.AddComponent<BoxCollider>();
        hull.center = new Vector3(0f, 0.615f, 0f);
        hull.size = new Vector3(3.2f, 0.89f, 1.76f);
        BoxCollider cabin = car.AddComponent<BoxCollider>();
        cabin.center = new Vector3(-0.2f, 1.38f, 0f);
        cabin.size = new Vector3(2.35f, 0.72f, 1.56f);

        Throwable(car, 900f);
        car.AddComponent<HoverCar>();
        car.AddComponent<CarPaint>();
        car.AddComponent<ExplosiveVehicle>();
    }

    private static void Throwable(GameObject go, float mass)
    {
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<TelekinesisTarget>();
    }

    // ---------- Espaço livre ----------

    // Espalha até "count" objetos em pontos livres; cada um reserva um quadrado de "footprint" metros.
    private static void Scatter(Ctx c, int count, float footprint, System.Action<Vector2> place)
    {
        int placed = 0;
        for (int attempt = 0; attempt < count * 6 && placed < count; attempt++)
        {
            Vector2 p = new Vector2(Range(c, footprint / 2f + 1f, Size - footprint / 2f - 1f), Range(c, footprint / 2f + 1f, Size - footprint / 2f - 1f));
            Rect area = RectAround(p, footprint, footprint);
            if (!Free(c, area))
            {
                continue;
            }

            Reserve(c, area);
            place(p);
            placed++;
        }
    }

    // Ponto ao acaso dentro do bloco (com margem). Quem usa confere com Free() antes de construir.
    private static Vector2 RandomPoint(Ctx c, float margin)
    {
        return new Vector2(Range(c, margin + 1f, Size - margin - 1f), Range(c, margin + 1f, Size - margin - 1f));
    }

    // Ponto livre perto do centro (para o celeiro).
    private static Vector2 NearCenter(Ctx c, float w, float d)
    {
        for (int i = 0; i < 40; i++)
        {
            Vector2 p = Center + new Vector2(Range(c, -30f, 30f), Range(c, -30f, 30f));
            Rect area = RectAround(p, Mathf.Max(w, d) + 2f, Mathf.Max(w, d) + 2f);
            if (p.x - w > 1f && p.x + w < Size - 1f && p.y - d > 1f && p.y + d < Size - 1f && Free(c, area))
            {
                Reserve(c, area);
                return p;
            }
        }

        return new Vector2(-1f, -1f);
    }

    private static bool Free(Ctx c, Rect area)
    {
        if (area.xMin < 0.5f || area.yMin < 0.5f || area.xMax > Size - 0.5f || area.yMax > Size - 0.5f)
        {
            return false;
        }

        foreach (Rect r in c.reserved)
        {
            if (r.Overlaps(area))
            {
                return false;
            }
        }

        return true;
    }

    private static void Reserve(Ctx c, Rect area) => c.reserved.Add(area);

    private static Rect RectAround(Vector2 center, float width, float depth) =>
        new Rect(center.x - width / 2f, center.y - depth / 2f, width, depth);

    // ---------- Planeta (Missão 19): bases e polos ----------

    private static readonly Dictionary<string, Material> RuntimeMaterials = new Dictionary<string, Material>();

    // Material criado no jogo (cópia do material da grama, com outra cor). Um por nome, reaproveitado.
    private static Material Mat(Ctx c, string name, Color color, float smoothness = 0.2f, float glow = 0f)
    {
        if (RuntimeMaterials.TryGetValue(name, out Material cached) && cached != null)
        {
            return cached;
        }

        Material material = c.kit.grass != null ? new Material(c.kit.grass) : new Material(CurvedHorizon.LitShader());
        material.name = name;
        material.color = color;
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        if (glow > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", color * glow);
        }

        RuntimeMaterials[name] = material;
        return material;
    }

    // Base: esconderijo improvisado num lugar alienígena abandonado, decorado com coisas "humanas"
    // para provar que ele não é alien (decisão D22). Rua chegando pelo sul até o pátio.
    private static void BaseBlock(Ctx c)
    {
        Transform site = Child(c.est, "Base");
        Material metal = Mat(c, "Base_Metal", new Color(0.55f, 0.58f, 0.62f), 0.55f);
        Material rust = Mat(c, "Base_Ferrugem", new Color(0.55f, 0.32f, 0.2f), 0.15f);
        Material glow = Mat(c, "Base_Luz", new Color(0.35f, 1f, 0.75f), 0.4f, 3f);
        Material concrete = Mat(c, "Base_Concreto", new Color(0.62f, 0.62f, 0.6f), 0.1f);
        Material glass = Mat(c, "Base_Vidro", new Color(0.65f, 0.88f, 0.92f), 0.9f);
        Material cloth = Mat(c, "Base_Lencol", new Color(0.92f, 0.85f, 0.6f), 0.1f);

        // Pátio de terra batida em volta do centro.
        NoCollider(Box(c, site, "Patio", new Vector3(Half, 0.02f, Half + 12f), new Vector3(46f, 0.03f, 40f), c.kit.path));
        Reserve(c, new Rect(Half - 24f, Half - 10f, 48f, 44f));

        switch (c.spec.variant)
        {
            case "torre": RadioTower(c, site, metal, rust, glow); break;
            case "laboratorio": OldLab(c, site, concrete, metal, glow); break;
            case "estufa": Greenhouse(c, site, metal, glass); break;
            default: CrashedShip(c, site, metal, rust, glow); break;
        }

        // Coisas "humanas": placa, cama, foto da Terra, fogueira, varal.
        Transform home = Child(site, "LarDoceLar");
        Box(c, home, "Placa_Poste_E", new Vector3(Half - 4f, 1.4f, Half + 2f), new Vector3(0.25f, 2.8f, 0.25f), c.kit.wood);
        Box(c, home, "Placa_Poste_D", new Vector3(Half + 4f, 1.4f, Half + 2f), new Vector3(0.25f, 2.8f, 0.25f), c.kit.wood);
        Box(c, home, "Placa", new Vector3(Half, 2.6f, Half + 2f), new Vector3(9f, 1.4f, 0.2f), c.kit.wood);
        // Lido de quem chega pela rua (do sul) e de quem está no pátio (do norte).
        Label(home, "LAR DOCE LAR", new Vector3(Half, 2.6f, Half + 1.85f), 0.14f, new Color(0.15f, 0.08f, 0.03f), 0f);
        Label(home, "LAR DOCE LAR", new Vector3(Half, 2.6f, Half + 2.15f), 0.14f, new Color(0.15f, 0.08f, 0.03f), 180f);

        Box(c, home, "Cama", new Vector3(Half + 12f, 0.35f, Half + 22f), new Vector3(2.2f, 0.7f, 3.4f), cloth);
        Box(c, home, "Travesseiro", new Vector3(Half + 12f, 0.8f, Half + 23.2f), new Vector3(1.4f, 0.3f, 0.7f), Mat(c, "Base_Branco", Color.white));
        Box(c, home, "Quadro", new Vector3(Half + 12f, 1.8f, Half + 24.4f), new Vector3(1.4f, 1.1f, 0.12f), c.kit.wood);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Foto_Terra", home, new Vector3(Half + 12f, 1.8f, Half + 24.32f), new Vector3(0.75f, 0.75f, 0.05f),
            Mat(c, "Base_Terra", new Color(0.2f, 0.45f, 0.95f), 0.6f)));
        NoCollider(Prim(c, PrimitiveType.Sphere, "Continente", home, new Vector3(Half + 11.9f, 1.85f, Half + 24.27f), new Vector3(0.35f, 0.3f, 0.04f),
            Mat(c, "Base_Continente", new Color(0.3f, 0.7f, 0.3f))));

        BaseFurniture(c, home);

        Transform fire = Child(home, "Fogueira");
        fire.localPosition = new Vector3(Half - 10f, 0f, Half + 14f);
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f;
            Prim(c, PrimitiveType.Sphere, "Pedra", fire, new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * 1.1f, 0.15f, Mathf.Sin(a * Mathf.Deg2Rad) * 1.1f),
                new Vector3(0.6f, 0.4f, 0.6f), c.kit.rock);
        }

        NoCollider(Prim(c, PrimitiveType.Sphere, "Fogo", fire, new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 1.1f, 0.9f),
            Mat(c, "Base_Fogo", new Color(1f, 0.55f, 0.15f), 0.2f, 4f)));
        for (int i = 0; i < 3; i++)
        {
            Box(c, fire, "Tronco", new Vector3(Mathf.Cos(i * 2.1f) * 2.4f, 0.25f, Mathf.Sin(i * 2.1f) * 2.4f), new Vector3(1.8f, 0.5f, 0.5f), c.kit.trunk, i * 60f);
        }

        // Farol: um feixe alto, para achar a base de longe.
        NoCollider(Prim(c, PrimitiveType.Cylinder, "Farol", site, new Vector3(Half - 16f, 30f, Half + 28f), new Vector3(0.6f, 30f, 0.6f), glow));

        // Anel no chão marcando a zona segura (raio da BaseSite).
        Transform ring = Child(site, "Zona_Segura");
        const int pieces = 40;
        for (int i = 0; i < pieces; i++)
        {
            float a = i * Mathf.PI * 2f / pieces;
            Vector3 p = new Vector3(Half + Mathf.Cos(a) * BaseSite.DefaultRadius, 0.04f, Half + 12f + Mathf.Sin(a) * BaseSite.DefaultRadius);
            NoCollider(Box(c, ring, "Marca", p, new Vector3(1.6f, 0.04f, 0.35f), glow, -a * Mathf.Rad2Deg + 90f));
        }

        BaseSite baseSite = c.root.gameObject.AddComponent<BaseSite>();
        baseSite.Setup(c.spec.variant, new Vector3(Half, 0f, Half + 12f));
    }

    // Sci-Fi Kit: o "quarto" do protagonista ao lado da cama — mesa com caneca, cadeira, armário e estante.
    private static void BaseFurniture(Ctx c, Transform home)
    {
        if (c.kit.desks == null || c.kit.desks.Length == 0)
        {
            return;
        }

        Transform room = Child(home, "Moveis");
        KitModel(c.kit.desks[1 % c.kit.desks.Length], "Mesa", room, new Vector3(Half + 16.5f, 0f, Half + 23.5f), 180f, 1f, 0f);
        if (c.kit.mug != null)
        {
            GameObject mug = KitModel(c.kit.mug, "Caneca", room, new Vector3(Half + 16f, 0.95f, Half + 23.4f), Range(c, 0f, 360f), 1f, 0f);
            Throwable(mug, 0.4f);
        }

        if (c.kit.chair != null)
        {
            KitModel(c.kit.chair, "Cadeira", room, new Vector3(Half + 16.5f, 0f, Half + 21.8f), Range(c, -20f, 20f), 1f, 0f);
        }

        if (c.kit.locker != null)
        {
            KitModel(c.kit.locker, "Armario", room, new Vector3(Half + 9.2f, 0f, Half + 24.6f), 180f, 1f, 0f);
        }

        if (c.kit.shelves != null && c.kit.shelves.Length > 0)
        {
            KitModel(c.kit.shelves[c.kit.shelves.Length - 1], "Estante", room, new Vector3(Half + 19.8f, 0f, Half + 24.6f), 180f, 1f, 0f);
        }
    }

    private static void CrashedShip(Ctx c, Transform site, Material metal, Material rust, Material glow)
    {
        Transform ship = Child(site, "Nave");
        ship.localPosition = new Vector3(Half + 2f, 1.6f, Half + 22f);
        ship.localRotation = Quaternion.Euler(10f, 25f, -8f);
        Prim(c, PrimitiveType.Cylinder, "Disco", ship, Vector3.zero, new Vector3(18f, 1.2f, 18f), metal);
        Prim(c, PrimitiveType.Cylinder, "Aro", ship, new Vector3(0f, -0.3f, 0f), new Vector3(20f, 0.4f, 20f), rust);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Cupula", ship, new Vector3(0f, 1.5f, 0f), new Vector3(8f, 4.5f, 8f),
            Mat(c, "Base_Cupula", new Color(0.55f, 0.9f, 0.85f), 0.9f)));
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            NoCollider(Prim(c, PrimitiveType.Sphere, "Luz", ship, new Vector3(Mathf.Cos(a) * 9.2f, 0.1f, Mathf.Sin(a) * 9.2f), Vector3.one * 0.7f, glow));
        }

        // Pedaços espalhados e um sulco no chão por onde ela caiu.
        Transform debris = Child(site, "Destrocos");
        NoCollider(Box(c, debris, "Sulco", new Vector3(Half - 10f, 0.03f, Half + 30f), new Vector3(6f, 0.04f, 26f), c.kit.soil, 40f));
        for (int i = 0; i < 6; i++)
        {
            Vector3 p = new Vector3(Half + Range(c, -20f, 20f), 0.4f, Half + Range(c, 26f, 38f));
            Box(c, debris, "Pedaco", p, new Vector3(Range(c, 1f, 3f), Range(c, 0.3f, 0.8f), Range(c, 1f, 2.5f)), i % 2 == 0 ? metal : rust, Range(c, 0f, 360f));
        }
    }

    private static void RadioTower(Ctx c, Transform site, Material metal, Material rust, Material glow)
    {
        Transform tower = Child(site, "Torre");
        tower.localPosition = new Vector3(Half + 8f, 0f, Half + 26f);
        const float height = 34f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Local(c, tower, "Perna", new Vector3(sx * 2f, height / 2f, sz * 2f), new Vector3(0.4f, height, 0.4f), rust);
            }
        }

        for (float y = 4f; y < height; y += 4f)
        {
            Local(c, tower, "Travessa", new Vector3(0f, y, 2f), new Vector3(4.4f, 0.25f, 0.25f), metal);
            Local(c, tower, "Travessa", new Vector3(0f, y, -2f), new Vector3(4.4f, 0.25f, 0.25f), metal);
            Local(c, tower, "Travessa", new Vector3(2f, y, 0f), new Vector3(0.25f, 0.25f, 4.4f), metal);
            Local(c, tower, "Travessa", new Vector3(-2f, y, 0f), new Vector3(0.25f, 0.25f, 4.4f), metal);
        }

        if (c.kit.satelliteDish != null)
        {
            // Sci-Fi Kit: parabólica no topo e uma gigante ao lado da torre.
            GameObject top = KitModel(c.kit.satelliteDish, "Antena", tower, new Vector3(0f, height, 0f), 200f, 1.3f, 0f);
            Object.DestroyImmediate(top.GetComponent<BoxCollider>());
            Antenna(c, tower, new Vector2(10f, 6f), 3f);
        }
        else
        {
            NoCollider(Prim(c, PrimitiveType.Sphere, "Antena", tower, new Vector3(0f, height + 2f, 2.5f), new Vector3(6f, 6f, 1.2f), metal, new Vector3(-20f, 0f, 0f)));
        }
        NoCollider(Prim(c, PrimitiveType.Sphere, "Luz_Topo", tower, new Vector3(0f, height + 0.6f, 0f), Vector3.one * 1.2f, glow));

        // Casinha de ferro ao pé da torre.
        Local(c, tower, "Casinha", new Vector3(-9f, 1.6f, -2f), new Vector3(7f, 3.2f, 5f), metal);
        Local(c, tower, "Telhado", new Vector3(-9f, 3.3f, -2f), new Vector3(7.6f, 0.3f, 5.6f), rust);
        Local(c, tower, "Porta", new Vector3(-9f, 1.1f, -4.52f), new Vector3(1.2f, 2.2f, 0.05f), c.kit.door);
    }

    private static void OldLab(Ctx c, Transform site, Material concrete, Material metal, Material glow)
    {
        Transform lab = Child(site, "Laboratorio");
        lab.localPosition = new Vector3(Half, 0f, Half + 26f);
        Local(c, lab, "Predio", new Vector3(0f, 3f, 0f), new Vector3(18f, 6f, 10f), concrete);
        Local(c, lab, "Telhado", new Vector3(0f, 6.15f, 0f), new Vector3(18.6f, 0.3f, 10.6f), metal);
        Local(c, lab, "Porta", new Vector3(0f, 1.2f, -5.02f), new Vector3(2.4f, 2.4f, 0.05f), c.kit.door);
        for (int i = -1; i <= 1; i += 2)
        {
            Local(c, lab, "Janela", new Vector3(i * 5.5f, 3.2f, -5.02f), new Vector3(3f, 1.6f, 0.05f), c.kit.window);
        }

        Local(c, lab, "Rachadura", new Vector3(4f, 4f, -5.03f), new Vector3(0.15f, 3f, 0.02f), c.kit.rock, new Vector3(0f, 0f, 25f));
        Prim(c, PrimitiveType.Cylinder, "Chamine", lab, new Vector3(-6f, 8f, 2f), new Vector3(1.2f, 2.5f, 1.2f), metal);

        // Sci-Fi Kit: mesas, estantes e armário de laboratório largados na frente do prédio.
        if (c.kit.desks != null && c.kit.desks.Length > 0)
        {
            KitModel(c.kit.desks[c.kit.desks.Length - 1], "Mesa_Lab", lab, new Vector3(-6f, 0f, -9f), 180f, 1f, 0f);
            if (c.kit.chair != null) KitModel(c.kit.chair, "Cadeira_Lab", lab, new Vector3(-6.5f, 0f, -10.6f), Range(c, 150f, 210f), 1f, 0f);
        }

        if (c.kit.shelves != null && c.kit.shelves.Length > 0)
        {
            KitModel(c.kit.shelves[c.kit.shelves.Length - 1], "Estante_Lab", lab, new Vector3(-9.5f, 0f, -6f), 180f, 1f, 0f);
            KitModel(c.kit.shelves[1 % c.kit.shelves.Length], "Estante_Lab", lab, new Vector3(-12f, 0f, -6f), 180f, 1f, 0f);
        }

        if (c.kit.locker != null)
        {
            KitModel(c.kit.locker, "Armario_Lab", lab, new Vector3(6.5f, 0f, -6f), 180f, 1f, 0f);
            KitModel(c.kit.locker, "Armario_Lab", lab, new Vector3(7.6f, 0f, -6f), 180f, 1f, 0f);
        }

        // Tanques com líquido brilhante (DNA alienígena velho).
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = new Vector3(12f, 0f, -3f + i * 4f);
            Prim(c, PrimitiveType.Cylinder, "Tanque", lab, p + new Vector3(0f, 2f, 0f), new Vector3(2.6f, 2f, 2.6f), metal);
            NoCollider(Prim(c, PrimitiveType.Cylinder, "Liquido", lab, p + new Vector3(0f, 2.2f, 0f), new Vector3(2.3f, 1.6f, 2.3f), glow));
        }
    }

    private static void Greenhouse(Ctx c, Transform site, Material metal, Material glass)
    {
        Transform house = Child(site, "Estufa");
        house.localPosition = new Vector3(Half, 0f, Half + 26f);
        const float w = 16f, d = 9f, h = 5f;
        // Vidro (meio quebrado) e armação.
        Local(c, house, "Vidro_Fundo", new Vector3(0f, h / 2f, d / 2f), new Vector3(w, h, 0.1f), glass);
        Local(c, house, "Vidro_Esq", new Vector3(-w / 2f, h / 2f, 0f), new Vector3(0.1f, h, d), glass);
        Local(c, house, "Vidro_Dir", new Vector3(w / 2f, h / 2f, 0f), new Vector3(0.1f, h, d), glass);
        Local(c, house, "Vidro_Frente_E", new Vector3(-w / 4f - 1.5f, h / 2f, -d / 2f), new Vector3(w / 2f - 3f, h, 0.1f), glass);
        Local(c, house, "Vidro_Frente_D", new Vector3(w / 4f + 1.5f, h / 2f, -d / 2f), new Vector3(w / 2f - 3f, h, 0.1f), glass);
        NoCollider(Local(c, house, "Teto", new Vector3(0f, h + 0.6f, 0f), new Vector3(w, 0.1f, d + 0.5f), glass, new Vector3(0f, 0f, 0f)));
        for (float x = -w / 2f; x <= w / 2f + 0.1f; x += 4f)
        {
            Local(c, house, "Armacao", new Vector3(x, h / 2f + 0.3f, -d / 2f), new Vector3(0.25f, h + 0.6f, 0.25f), metal);
            Local(c, house, "Armacao", new Vector3(x, h / 2f + 0.3f, d / 2f), new Vector3(0.25f, h + 0.6f, 0.25f), metal);
            Local(c, house, "Arco", new Vector3(x, h + 0.55f, 0f), new Vector3(0.25f, 0.25f, d), metal);
        }

        // Plantas alienígenas que cresceram demais.
        for (int i = 0; i < 7; i++)
        {
            Vector3 p = new Vector3(Range(c, -w / 2f + 1.5f, w / 2f - 1.5f), 0f, Range(c, -d / 2f + 1.5f, d / 2f - 1.5f));
            NoCollider(Prim(c, PrimitiveType.Sphere, "Planta", house, p + new Vector3(0f, 1.2f, 0f), new Vector3(1.6f, 2.6f, 1.6f), Pick(c, c.kit.canopies)));
        }

        Local(c, house, "Canteiro", new Vector3(0f, 0.25f, 0f), new Vector3(w - 2f, 0.5f, 2f), c.kit.soil);
    }

    // Placa de texto (usa a fonte embutida do Unity).
    private static void Label(Transform parent, string text, Vector3 localPosition, float size, Color color, float yaw)
    {
        GameObject go = new GameObject("Texto");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        TextMesh mesh = go.AddComponent<TextMesh>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.font = font;
        mesh.text = text;
        mesh.fontSize = 64;
        mesh.characterSize = size;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = color;
        if (font != null)
        {
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }

    // Polo: neve, blocos de gelo e montes de neve. "variant": "norte" ou "sul", e "_fora" na linha de fora
    // (a que encosta na linha do polo). Perto da linha do polo fica uma faixa livre, igual dos dois lados,
    // para a passagem para o outro lado do planeta não aparecer (a nevasca esconde o resto).
    private static void BuildPole(Ctx c)
    {
        Material snow = Mat(c, "Polo_Neve", new Color(0.93f, 0.96f, 1f), 0.35f);
        Material ice = Mat(c, "Polo_Gelo", new Color(0.62f, 0.83f, 0.95f), 0.85f);
        Box(c, c.est, "Chao_Neve", new Vector3(Half, -0.1f, Half), new Vector3(Size, 0.2f, Size), snow);

        string variant = c.spec.variant ?? "";
        bool north = variant.StartsWith("norte");
        bool outer = variant.EndsWith("_fora");
        Transform field = Child(c.est, "Gelo");
        if (outer)
        {
            // Faixa livre de 45 m junto da linha do polo.
            Reserve(c, new Rect(0f, north ? Size - 45f : 0f, Size, 45f));

            // O mastro do polo, listrado, no meio da linha (tem um igual do outro lado).
            float z = north ? Size - 3f : 3f;
            Material red = Mat(c, "Polo_Vermelho", new Color(0.85f, 0.15f, 0.15f), 0.4f);
            for (int i = 0; i < 8; i++)
            {
                Prim(c, PrimitiveType.Cylinder, "Mastro", field, new Vector3(Half, 0.4f + i * 0.8f, z), new Vector3(0.35f, 0.4f, 0.35f), i % 2 == 0 ? red : snow);
            }

            NoCollider(Prim(c, PrimitiveType.Sphere, "Bola", field, new Vector3(Half, 6.8f, z), Vector3.one * 0.8f, Mat(c, "Polo_Dourado", new Color(1f, 0.8f, 0.3f), 0.7f, 1.5f)));
            Box(c, field, "Placa", new Vector3(Half, 4.6f, z), new Vector3(3.2f, 0.8f, 0.12f), c.kit.wood);
            string title = north ? "POLO NORTE" : "POLO SUL";
            Label(field, title, new Vector3(Half, 4.6f, z - 0.08f), 0.09f, Color.white, 0f);
            Label(field, title, new Vector3(Half, 4.6f, z + 0.08f), 0.09f, Color.white, 180f);
        }

        Scatter(c, outer ? 8 : 14, 4f, p => Box(c, field, "Bloco_Gelo", new Vector3(p.x, Range(c, 0.5f, 1.8f), p.y),
            new Vector3(Range(c, 2f, 4f), Range(c, 1.5f, 4f), Range(c, 2f, 4f)), ice, Range(c, 0f, 360f)));
        Scatter(c, outer ? 10 : 18, 3f, p => NoCollider(Prim(c, PrimitiveType.Sphere, "Monte_Neve", field, new Vector3(p.x, 0.2f, p.y),
            new Vector3(Range(c, 3f, 6f), Range(c, 0.8f, 1.6f), Range(c, 3f, 6f)), snow)));
    }

    // ---------- Primitivas ----------

    private static Transform Child(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    // Caixa com posição em coordenadas do bloco (filhos diretos de grupos na origem do bloco).
    private static GameObject Box(Ctx c, Transform parent, string name, Vector3 position, Vector3 size, Material material, float yaw = 0f)
    {
        GameObject go = Prim(c, PrimitiveType.Cube, name, parent, position, size, material);
        if (Mathf.Abs(yaw) > 0.01f)
        {
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        return go;
    }

    // Caixa com posição local ao pai (casas, pivô do rio).
    private static GameObject Local(Ctx c, Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, Vector3? euler = null)
    {
        return Prim(c, PrimitiveType.Cube, name, parent, localPosition, size, material, euler);
    }

    private static GameObject Prim(Ctx c, PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 scale, Material material,
        Vector3? euler = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        if (euler.HasValue)
        {
            go.transform.localRotation = Quaternion.Euler(euler.Value);
        }

        if (material != null)
        {
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        return go;
    }

    private static GameObject NoCollider(GameObject go)
    {
        if (go.TryGetComponent(out Collider collider))
        {
            Object.DestroyImmediate(collider);
        }

        return go;
    }

    private static float Range(Ctx c, float min, float max) => min + (float)c.rng.NextDouble() * (max - min);

    private static T Pick<T>(Ctx c, T[] options) where T : class =>
        options != null && options.Length > 0 ? options[c.rng.Next(options.Length)] : null;

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        float r = -degrees * Mathf.Deg2Rad; // mesmo sentido do giro em Y da Unity (horário visto de cima)
        float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }
}
