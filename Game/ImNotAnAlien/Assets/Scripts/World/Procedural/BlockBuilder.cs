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
public static class BlockBuilder
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
        };

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
            default:
                Ground(c);
                if (c.AnyRoad)
                {
                    Roads(c, RoadHalf);
                }

                Fill(c);
                break;
        }

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
            case BlockType.Lago: Lago(c); break;
            case BlockType.Campo: Campo(c); break;
        }

        StreetProps(c);
    }

    // ---------- Chão e ruas ----------

    private static void Ground(Ctx c)
    {
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
        Box(c, ground, "Chao_N", new Vector3(Half, -0.1f, 95f), new Vector3(Size, 0.2f, 10f), c.kit.grass);
        Box(c, ground, "Chao_S", new Vector3(Half, -0.1f, 5f), new Vector3(Size, 0.2f, 10f), c.kit.grass);
        Box(c, ground, "Chao_O", new Vector3(5f, -0.1f, Half), new Vector3(10f, 0.2f, 80f), c.kit.grass);
        Box(c, ground, "Chao_L", new Vector3(95f, -0.1f, Half), new Vector3(10f, 0.2f, 80f), c.kit.grass);

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
        bool northSouth = c.spec.north == Socket.Rio;
        bool bridge = northSouth ? c.arm[1] : c.arm[0];

        Transform pivot = Child(c.est, "Rio");
        pivot.localPosition = new Vector3(Half, 0f, Half);
        pivot.localRotation = Quaternion.Euler(0f, northSouth ? 90f : 0f, 0f);

        // Chão das duas margens.
        float groundDepth = Half - RiverHalf;
        Local(c, pivot, "Chao_Norte", new Vector3(0f, -0.1f, RiverHalf + groundDepth / 2f), new Vector3(Size, 0.2f, groundDepth), c.kit.grass);
        Local(c, pivot, "Chao_Sul", new Vector3(0f, -0.1f, -(RiverHalf + groundDepth / 2f)), new Vector3(Size, 0.2f, groundDepth), c.kit.grass);

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

    // ---------- Tipos ----------

    private static void Bairro(Ctx c)
    {
        Transform houses = Child(c.est, "Casas");
        AlongRoads(c, new[] { 17f, 30f, 43f }, 0.85f, (p, yaw, faceOffset) => House(c, houses, p, yaw), 8.5f, 9.5f, 2.5f);

        Transform trees = Child(c.est, "Arvores");
        Scatter(c, 10, 2.2f, p => Tree(c, trees, p, Range(c, 0.8f, 1.1f)));
        Transform bushes = Child(c.est, "Arbustos");
        Scatter(c, 8, 1.2f, p => Bush(c, bushes, p));
    }

    private static void Predios(Ctx c)
    {
        Transform buildings = Child(c.est, "Predios");
        AlongRoads(c, new[] { 19f, 38f }, 1f, (p, yaw, f) => Building(c, buildings, p, yaw), 15f, 15f, 2f);
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
        float canopy = (3.2f + (float)c.rng.NextDouble() * 1.6f) * scale;
        float trunkHeight = 2f * scale;
        Prim(c, PrimitiveType.Cylinder, "Tronco", tree, new Vector3(0f, trunkHeight, 0f), new Vector3(0.5f * scale, trunkHeight, 0.5f * scale), c.kit.trunk);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Copa", tree, new Vector3(0f, trunkHeight * 2f + canopy * 0.35f, 0f), Vector3.one * canopy, Pick(c, c.kit.canopies)));
    }

    private static void Bush(Ctx c, Transform parent, Vector2 p)
    {
        float s = Range(c, 1f, 2f);
        NoCollider(Prim(c, PrimitiveType.Sphere, "Arbusto", parent, new Vector3(p.x, s * 0.35f, p.y), new Vector3(s, s * 0.8f, s), c.kit.bush));
    }

    private static void Rock(Ctx c, Transform parent, Vector2 p, float s)
    {
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
        Throwable(Prim(c, PrimitiveType.Cube, "Caixa", c.dyn, p, Vector3.one, c.kit.crate, new Vector3(0f, Range(c, 0f, 90f), 0f)), 40f);
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
