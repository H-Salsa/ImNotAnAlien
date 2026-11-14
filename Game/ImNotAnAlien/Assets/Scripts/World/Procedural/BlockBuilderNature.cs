using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Missão 20 — natureza de verdade: árvores, arbustos, pedras e vegetação rasteira do
// Stylized Nature MegaKit + Textured Stylized Trees (Kit de Natureza, ligado ao Kit de Blocos).
// Sem o kit, tudo continua com as árvores de blockout (cilindro + esfera).
//
// Cada objeto fica num "invólucro" com o nome antigo (Arvore, Arbusto, Pedra) para o relevo
// continuar funcionando (sobe com o chão, some debaixo d'água, menos árvores em certos biomas).
// Dentro dele, "Modelo" é o modelo escalado para a altura desejada (os pacotes vêm em cm ou em m).
// Folhas e grama recebem a cor do bioma (Floresta Chiclete rosa, Tundra azulada...).
public static partial class BlockBuilder
{
    private static readonly Dictionary<GameObject, Bounds> natureBounds = new Dictionary<GameObject, Bounds>();
    private static readonly Dictionary<(int, int, int), Material> natureTint = new Dictionary<(int, int, int), Material>();
    private static readonly float[] TintShade = { 0.86f, 1f, 1.12f };

    private static NatureKit Nature(Ctx c) => c.kit != null ? c.kit.nature : null;

    // Missão 20 — troca de bioma gradual: cada árvore/planta/pedra usa o bioma de um ponto sorteado
    // até 45 m em volta dela. Longe da divisa dá sempre o próprio bioma; perto dela, às vezes o vizinho
    // (metade das vezes bem na divisa) — as duas vegetações se misturam numa faixa, sem corte reto.
    private static Planet.Biome LocalBiome(Ctx c, Vector3 localPosition)
    {
        if (!c.terrain)
        {
            return NatureBiome(c);
        }

        float x = c.coord.x * 100f + localPosition.x + Range(c, -45f, 45f);
        float z = c.coord.y * 100f + localPosition.z + Range(c, -45f, 45f);
        Vector2Int cell = new Vector2Int(Planet.WrapX(Mathf.FloorToInt(x / 100f)), Mathf.Clamp(Mathf.FloorToInt(z / 100f), 0, Planet.Height - 1));
        return Planet.RegionBiome(Planet.RegionAt(cell));
    }

    // Fora do planeta (Vilarejo original) o mundo é "Vale": verde comum, sem pintar.
    private static Planet.Biome NatureBiome(Ctx c) => c.terrain ? c.biome : Planet.Biome.Vale;

    private static bool Has(GameObject[] pool) => pool != null && pool.Length > 0;

    // Escolhe um conjunto ao acaso entre os que existem, pelo peso.
    private static GameObject[] Weighted(Ctx c, params (GameObject[] pool, float weight)[] options)
    {
        float total = 0f;
        foreach ((GameObject[] pool, float weight) in options)
        {
            if (Has(pool)) total += weight;
        }

        if (total <= 0f)
        {
            return null;
        }

        float roll = Range(c, 0f, total);
        foreach ((GameObject[] pool, float weight) in options)
        {
            if (!Has(pool)) continue;
            roll -= weight;
            if (roll <= 0f) return pool;
        }

        return null;
    }

    // ---------- Árvores ----------

    // Árvore do kit no lugar da de blockout. "scale" é o mesmo fator de antes (≈ 0,8–1,6).
    private static bool NatureTree(Ctx c, Transform tree, float scale)
    {
        Planet.Biome own = c.biome;
        c.biome = LocalBiome(c, tree.localPosition + (tree.parent != null ? tree.parent.localPosition : Vector3.zero));
        try
        {
            return NatureTreeIn(c, tree, scale);
        }
        finally
        {
            c.biome = own;
        }
    }

    private static bool NatureTreeIn(Ctx c, Transform tree, float scale)
    {
        NatureKit k = Nature(c);
        if (k == null)
        {
            return false;
        }

        // Missão 20: cada bioma mistura árvores "da Terra" (Nature Kit) com árvores alienígenas
        // (Ultimate Space Kit) — sem o Space Kit preparado, as alienígenas ficam de fora sozinhas.
        GameObject[] pool;
        switch (NatureBiome(c))
        {
            case Planet.Biome.Gelo:
            case Planet.Biome.Geleira:
                pool = Weighted(c, (k.pines, 0.45f), (k.deadBirches, 0.15f), (k.alienLight, 0.4f));
                break;
            case Planet.Biome.Dunas:
                pool = Weighted(c, (k.deadTrees, 0.35f), (k.alienLava, 0.4f), (k.alienSpikes, 0.25f));
                break;
            case Planet.Biome.Pantano:
                pool = Weighted(c, (k.twistedTrees, 0.35f), (k.deadBirches, 0.2f), (k.alienSpiral, 0.45f));
                break;
            case Planet.Biome.Cratera:
            case Planet.Biome.Antenas:
                pool = Weighted(c, (k.deadTrees, 0.3f), (k.alienLava, 0.35f), (k.alienSpikes, 0.35f));
                break;
            case Planet.Biome.Campos:
            case Planet.Biome.Colinas:
                pool = Weighted(c, (k.commonTrees, 0.35f), (k.roundTrees, 0.35f), (k.alienFloating, 0.3f));
                break;
            case Planet.Biome.Baia:
                pool = Weighted(c, (k.roundTrees, 0.3f), (k.birches, 0.25f), (k.alienFloating, 0.3f), (k.alienSwirl, 0.15f));
                break;
            case Planet.Biome.Planicie:
            case Planet.Biome.Cidade:
                pool = Weighted(c, (k.roundTrees, 0.55f), (k.commonTrees, 0.15f), (k.alienFloating, 0.3f));
                break;
            case Planet.Biome.Floresta:
                pool = Weighted(c, (k.twistedTrees, 0.35f), (k.commonTrees, 0.2f), (k.alienSwirl, 0.45f));
                break;
            case Planet.Biome.Tundra:
                pool = Weighted(c, (k.pines, 0.45f), (k.birches, 0.2f), (k.alienLight, 0.35f));
                break;
            case Planet.Biome.Picos:
                pool = Weighted(c, (k.pines, 0.65f), (k.alienSpikes, 0.35f));
                break;
            case Planet.Biome.Estepe:
                pool = Weighted(c, (k.deadBirches, 0.25f), (k.roundTrees, 0.2f), (k.deadTrees, 0.2f), (k.alienSpikes, 0.35f));
                break;
            default: // Vale
                pool = Weighted(c, (k.commonTrees, 0.3f), (k.birches, 0.2f), (k.pines, 0.15f), (k.alienFloating, 0.2f), (k.alienSwirl, 0.15f));
                break;
        }

        GameObject model = Pick(c, pool);
        if (model == null)
        {
            return false;
        }

        // Altura de cada família (m), multiplicada pelo fator do bloco.
        Vector2 height = pool == k.alienFloating ? new Vector2(7f, 10f)
            : pool == k.alienLava ? new Vector2(4.5f, 6.5f)
            : pool == k.alienLight ? new Vector2(5f, 7.5f)
            : pool == k.alienSpikes ? new Vector2(6f, 9f)
            : pool == k.alienSpiral ? new Vector2(6.5f, 9.5f)
            : pool == k.alienSwirl ? new Vector2(8f, 11f)
            : pool == k.pines ? new Vector2(8f, 11f)
            : pool == k.twistedTrees ? new Vector2(7f, 10f)
            : pool == k.deadTrees || pool == k.deadBirches ? new Vector2(5.5f, 8f)
            : pool == k.birches ? new Vector2(7f, 10f)
            : new Vector2(6.5f, 9f);
        float h = Mathf.Min(Range(c, height.x, height.y) * scale, 14f);

        tree.localRotation = Quaternion.Euler(0f, Range(c, 0f, 360f), 0f);
        NatureModel(c, model, tree, h, true, ShadowCastingMode.On);

        // Colisor só no tronco (dá para passar por baixo da copa).
        CapsuleCollider trunk = tree.gameObject.AddComponent<CapsuleCollider>();
        trunk.radius = Mathf.Clamp(0.3f * h / 8f, 0.2f, 0.6f);
        trunk.height = Mathf.Min(h * 0.5f, 5f);
        trunk.center = new Vector3(0f, trunk.height / 2f, 0f);
        return true;
    }

    // ---------- Arbustos e pedras ----------

    private static Transform NatureBush(Ctx c, Transform parent, Vector2 p)
    {
        Planet.Biome own = c.biome;
        c.biome = LocalBiome(c, new Vector3(p.x, 0f, p.y));
        try
        {
            return NatureBushIn(c, parent, p);
        }
        finally
        {
            c.biome = own;
        }
    }

    private static Transform NatureBushIn(Ctx c, Transform parent, Vector2 p)
    {
        NatureKit k = Nature(c);
        if (k == null || !Has(k.bushes) && !Has(k.flowerBushes))
        {
            return null;
        }

        Planet.Biome b = NatureBiome(c);
        bool flowery = b == Planet.Biome.Campos || b == Planet.Biome.Vale || b == Planet.Biome.Baia || b == Planet.Biome.Floresta
            || b == Planet.Biome.Colinas || b == Planet.Biome.Planicie;
        GameObject[] pool = Weighted(c, (k.bushes, 1f), (k.flowerBushes, flowery ? 0.6f : 0f), (k.alienPlants, 0.7f));
        GameObject model = Pick(c, pool);
        if (model == null)
        {
            return null;
        }

        Transform bush = Child(parent, "Arbusto");
        bush.localPosition = new Vector3(p.x, 0f, p.y);
        bush.localRotation = Quaternion.Euler(0f, Range(c, 0f, 360f), 0f);
        NatureModel(c, model, bush, Range(c, 0.9f, 1.5f), true, ShadowCastingMode.On);
        return bush;
    }

    private static Transform NatureRock(Ctx c, Transform parent, Vector2 p, float s)
    {
        Planet.Biome own = c.biome;
        c.biome = LocalBiome(c, new Vector3(p.x, 0f, p.y));
        try
        {
            return NatureRockIn(c, parent, p, s);
        }
        finally
        {
            c.biome = own;
        }
    }

    private static Transform NatureRockIn(Ctx c, Transform parent, Vector2 p, float s)
    {
        NatureKit k = Nature(c);
        Planet.Biome rb = NatureBiome(c);
        bool alienGround = rb == Planet.Biome.Cratera || rb == Planet.Biome.Dunas || rb == Planet.Biome.Antenas
            || rb == Planet.Biome.Picos || rb == Planet.Biome.Estepe || rb == Planet.Biome.Gelo || rb == Planet.Biome.Geleira;
        GameObject model = k != null ? Pick(c, Weighted(c, (k.rocks, 1f), (k.alienRocks, alienGround ? 0.8f : 0.3f))) : null;
        if (model == null)
        {
            return null;
        }

        Transform rock = Child(parent, "Pedra");
        rock.localPosition = new Vector3(p.x, 0f, p.y);
        rock.localRotation = Quaternion.Euler(0f, Range(c, 0f, 360f), 0f);
        // "s" era o diâmetro da esfera de antes: a pedra do kit fica com mais ou menos o mesmo tamanho.
        Transform holder = NatureModel(c, model, rock, s * Range(c, 0.9f, 1.2f), false, ShadowCastingMode.On);
        Bounds bounds = natureBounds[model];
        BoxCollider box = holder.gameObject.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
        return rock;
    }

    // ---------- Vegetação rasteira ----------

    // Grama, flores, samambaias, cogumelos e pedrinhas espalhados pelo bloco (sem colisor e sem
    // reservar espaço: só evitam ruas, casas, lagos, troncos e pedras — veja BlockedMap). Mais densa
    // no mato, rala na cidade.
    private static void GroundCover(Ctx c)
    {
        NatureKit k = Nature(c);
        if (k == null)
        {
            return;
        }

        float density;
        switch (c.spec.type)
        {
            case BlockType.Floresta:
            case BlockType.Campo:
            case BlockType.Lagoa:
                density = 1f;
                break;
            case BlockType.Fazenda:
                density = 0.6f;
                break;
            case BlockType.Parque:
            case BlockType.Lago:
                density = 0.5f;
                break;
            case BlockType.Rio:
                density = 0.4f; // só nas margens planas (blocos do Vilarejo)
                break;
            case BlockType.Bairro:
            case BlockType.Praca:
            case BlockType.Esquina:
                density = 0.22f;
                break;
            case BlockType.Predios:
            case BlockType.Galpoes:
                density = 0.1f;
                break;
            default:
                return; // Base, Polo
        }

        // Quantidades por bioma num bloco de mato (densidade 1).
        int grass, flowers, ferns, mushrooms, pebbles;
        switch (NatureBiome(c))
        {
            case Planet.Biome.Gelo:
            case Planet.Biome.Geleira:
            case Planet.Biome.Picos:
                grass = 8; flowers = 0; ferns = 0; mushrooms = 0; pebbles = 16;
                break;
            case Planet.Biome.Tundra:
                grass = 45; flowers = 0; ferns = 4; mushrooms = 0; pebbles = 12;
                break;
            case Planet.Biome.Dunas:
            case Planet.Biome.Cratera:
            case Planet.Biome.Antenas:
                grass = 12; flowers = 0; ferns = 0; mushrooms = 0; pebbles = 22;
                break;
            case Planet.Biome.Pantano:
                grass = 55; flowers = 4; ferns = 30; mushrooms = 12; pebbles = 4;
                break;
            case Planet.Biome.Estepe:
                grass = 80; flowers = 6; ferns = 0; mushrooms = 0; pebbles = 10;
                break;
            case Planet.Biome.Floresta:
                grass = 60; flowers = 14; ferns = 16; mushrooms = 10; pebbles = 6;
                break;
            default: // Campos, Vale, Colinas, Baía, Planície, Cidade
                grass = 75; flowers = 16; ferns = 10; mushrooms = 3; pebbles = 6;
                break;
        }

        if (c.spec.type == BlockType.Campo)
        {
            flowers = Mathf.Max(flowers * 2, 30); // o campo é florido (substitui as caixinhas "Flor")
        }

        // Alienígenas: mais nos biomas "estranhos", menos nos verdes comuns.
        int alienPlants = 5, alienTufts = 8;
        switch (NatureBiome(c))
        {
            case Planet.Biome.Floresta:
            case Planet.Biome.Pantano:
            case Planet.Biome.Cratera:
            case Planet.Biome.Antenas:
            case Planet.Biome.Dunas:
                alienPlants = 10; alienTufts = 14; break;
            case Planet.Biome.Gelo:
            case Planet.Biome.Geleira:
            case Planet.Biome.Picos:
                alienPlants = 2; alienTufts = 3; break;
        }

        c.blocked = BlockedMap(c);
        if (GrassBlades(c))
        {
            grass = 0; // a grama de triângulos ao vento substitui os modelos de grama
        }

        Transform cover = Child(c.est, "Vegetacao");
        Cover(c, cover, k.grasses, Mathf.RoundToInt(grass * density), 0.35f, 0.65f, true);
        Cover(c, cover, k.flowers, Mathf.RoundToInt(flowers * density), 0.35f, 0.65f, true);
        Cover(c, cover, k.ferns, Mathf.RoundToInt(ferns * density), 0.5f, 1f, true);
        Cover(c, cover, k.mushrooms, Mathf.RoundToInt(mushrooms * density), 0.25f, 0.45f, true);
        Cover(c, cover, k.pebbles, Mathf.RoundToInt(pebbles * density), 0.35f, 0.9f, false);
        // Missão 20: plantas e tufos alienígenas no meio da vegetação.
        Cover(c, cover, k.alienPlants, Mathf.RoundToInt(alienPlants * density), 0.7f, 1.5f, true);
        Cover(c, cover, k.alienGrass, Mathf.RoundToInt(alienTufts * density), 0.4f, 0.8f, true);
    }

    // Grama de triângulos ao vento (GrassField + shader "ImNotAnAlien/Grass Wind"). Falso = sem grama
    // aqui (sem material ou bioma de areia/gelo): fica a grama de modelos.
    private static bool GrassBlades(Ctx c)
    {
        NatureKit k = Nature(c);
        if (k == null || k.grassBlades == null)
        {
            return false;
        }

        float density;
        switch (c.spec.type)
        {
            case BlockType.Campo:
            case BlockType.Lagoa: density = 3.6f; break;
            case BlockType.Floresta: density = 3.2f; break;
            case BlockType.Parque: density = 2.6f; break;
            case BlockType.Lago: density = 2.4f; break;
            case BlockType.Rio: density = 2f; break;
            case BlockType.Fazenda: density = 2f; break;
            case BlockType.Bairro:
            case BlockType.Praca:
            case BlockType.Esquina: density = 1.2f; break;
            case BlockType.Predios:
            case BlockType.Galpoes: density = 0.6f; break;
            default: return false;
        }

        density *= 6f; // folhas baixinhas: precisa de bem mais para fechar o gramado (pedido do autor; teste com o dobro)
        Planet.Biome biome = NatureBiome(c);
        float tall = 1f;
        switch (biome)
        {
            case Planet.Biome.Gelo:
            case Planet.Biome.Geleira:
            case Planet.Biome.Picos:
                return false;
            case Planet.Biome.Dunas:
            case Planet.Biome.Cratera:
            case Planet.Biome.Antenas:
                density *= 0.15f; tall = 0.8f; break;
            case Planet.Biome.Tundra:
                density *= 0.6f; tall = 0.7f; break;
            case Planet.Biome.Pantano:
                density *= 1.1f; tall = 1.3f; break;
            case Planet.Biome.Estepe:
                tall = 1.3f; break;
            case Planet.Biome.Planicie:
            case Planet.Biome.Campos:
                tall = 1.15f; break;
            case Planet.Biome.Cidade:
                tall = 0.7f; break;
        }

        Color pe, ponta;
        if (c.terrain)
        {
            PlanetTerrain.BiomeStyle style = PlanetTerrain.StyleOf(biome);
            pe = Color.Lerp(style.low, style.foliage, 0.35f) * 0.62f;
            ponta = Color.Lerp(Color.Lerp(style.low, style.foliage, 0.65f), Color.white, 0.12f);
        }
        else
        {
            pe = new Color(0.20f, 0.40f, 0.15f);
            ponta = new Color(0.56f, 0.80f, 0.36f);
        }

        if (QualitySettings.activeColorSpace == ColorSpace.Linear)
        {
            pe = pe.linear; // a cor vai direto no vértice: o shader espera cor linear
            ponta = ponta.linear;
        }

        Transform ground = c.est.Find("Chao");
        MeshFilter groundFilter = ground != null ? ground.GetComponent<MeshFilter>() : null;
        GrassField field = c.root.gameObject.AddComponent<GrassField>();
        field.Setup(k.grassBlades, c.blocked, groundFilter != null ? groundFilter.sharedMesh : null, density, tall,
            c.terrain ? PlanetTerrain.WaterLevel + 0.1f : -999f, pe, ponta, c.spec.seed ^ 0x3c6ef372);
        if (c.terrain)
        {
            // Missão 20: cor da grama misturada com o bioma vizinho perto da divisa.
            field.BlendBiomes(new Vector2(Planet.GridOrigin.x + c.coord.x * 100f, Planet.GridOrigin.y + c.coord.y * 100f),
                QualitySettings.activeColorSpace == ColorSpace.Linear);
        }
        return true;
    }

    private static void Cover(Ctx c, Transform parent, GameObject[] pool, int count, float min, float max, bool byHeight)
    {
        if (!Has(pool))
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Vector2 p = RandomPoint(c, 0.5f);
            if (Blocked(c, p))
            {
                continue;
            }

            Transform item = Child(parent, "Planta");
            item.localPosition = new Vector3(p.x, 0f, p.y);
            item.localRotation = Quaternion.Euler(0f, Range(c, 0f, 360f), 0f);
            Planet.Biome own = c.biome;
            c.biome = LocalBiome(c, item.localPosition); // cor do bioma vizinho perto da divisa
            NatureModel(c, Pick(c, pool), item, Range(c, min, max), byHeight, ShadowCastingMode.Off);
            c.biome = own;
        }
    }

    // ---------- Onde não nasce planta ----------

    private static bool Blocked(Ctx c, Vector2 p)
    {
        if (c.blocked == null)
        {
            return !Free(c, RectAround(p, 0.6f, 0.6f));
        }

        int x = Mathf.Clamp((int)p.x, 0, 99), z = Mathf.Clamp((int)p.y, 0, 99);
        return c.blocked[z * 100 + x];
    }

    // Mapa de 100 × 100 células de 1 m pelo FORMATO real do que foi construído (e não pelos espaços
    // reservados, que são quadrados com folga: deixavam um buraco sem grama em volta das pedras e dos lagos).
    // - árvore: só um círculo no pé do tronco;
    // - arbustos, flores e plantas: grama em volta normalmente;
    // - pedra, casa, rua, calçada, cerca, lago: a forma da peça (caixa girada, ou elipse para cilindro e esfera);
    // - chão (placa no nível 0) e coisas no alto (copas, luzes, placas): não contam.
    // Funciona também nos blocos do Vilarejo (prefabs), que não têm a lista de espaços reservados.
    private static bool[] BlockedMap(Ctx c)
    {
        bool[] map = new bool[100 * 100];
        Vector3 origin = c.root.position;
        HashSet<Transform> trees = new HashSet<Transform>();
        foreach (Renderer renderer in c.est.GetComponentsInChildren<Renderer>(true))
        {
            Transform t = renderer.transform;
            if (t.name == "Chao" || t.name.StartsWith("Agua_Mar") || renderer.GetComponentInParent<LODGroup>() != null)
            {
                continue; // casas retrô: contam pelas caixas de colisão (abaixo), não pelo lote inteiro
            }

            // Dentro de qual "invólucro" está (Arvore, Arbusto, Pedra, Planta...)?
            Transform tree = null;
            bool soft = false, rock = false;
            for (Transform a = t; a != null && a != c.est; a = a.parent)
            {
                string n = a.name;
                if (n == "Arvore") { tree = a; break; }
                if (n == "Pedra") rock = true; // a grama encosta na pedra
                if (n == "Arbusto" || n == "Planta" || n == "Flor" || n == "Vegetacao" || n == "Copa") { soft = true; break; }
            }

            if (tree != null)
            {
                if (trees.Add(tree))
                {
                    Vector3 local = tree.position - origin;
                    Disc(map, local.x, local.z, 0.7f);
                }

                continue;
            }

            if (soft)
            {
                continue;
            }

            Bounds b = renderer.bounds;
            float bottom = b.min.y - origin.y;
            float top = b.max.y - origin.y;
            string name = t.name;
            bool water = name.Contains("Agua") || name.Contains("Lago");
            if (!water && (bottom > 1.2f || top <= 0.01f && bottom >= -0.5f))
            {
                continue; // no alto, ou placa de chão no nível 0
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            float margin = rock ? -0.1f : 0.25f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(b.min.x - origin.x - 1f), 0, 99), x1 = Mathf.Clamp(Mathf.CeilToInt(b.max.x - origin.x + 1f), 0, 99);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(b.min.z - origin.z - 1f), 0, 99), z1 = Mathf.Clamp(Mathf.CeilToInt(b.max.z - origin.z + 1f), 0, 99);
            if (mesh == null)
            {
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        map[z * 100 + x] = true;
                continue;
            }

            Bounds lb = mesh.bounds;
            Vector3 scale = t.lossyScale;
            float mx = margin / Mathf.Max(Mathf.Abs(scale.x), 0.001f);
            float mz = margin / Mathf.Max(Mathf.Abs(scale.z), 0.001f);
            bool round = mesh.name == "Cylinder" || mesh.name == "Sphere";
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector3 l = t.InverseTransformPoint(new Vector3(origin.x + x + 0.5f, b.center.y, origin.z + z + 0.5f));
                    float hx = lb.extents.x + mx, hz = lb.extents.z + mz;
                    float dx = l.x - lb.center.x, dz = l.z - lb.center.z;
                    bool inside = round
                        ? (dx * dx) / (hx * hx) + (dz * dz) / (hz * hz) <= 1f
                        : Mathf.Abs(dx) <= hx && Mathf.Abs(dz) <= hz;
                    if (inside)
                    {
                        map[z * 100 + x] = true;
                    }
                }
            }
        }

        // Marcas "SemGrama" (entrada da garagem das casas retrô): caixa de 1 × 1 escalada.
        foreach (Transform t in c.est.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "SemGrama")
            {
                continue;
            }

            for (int z = 0; z < 100; z++)
            {
                for (int x = 0; x < 100; x++)
                {
                    Vector3 l = t.InverseTransformPoint(new Vector3(origin.x + x + 0.5f, t.position.y, origin.z + z + 0.5f));
                    if (Mathf.Abs(l.x) <= 0.5f && Mathf.Abs(l.z) <= 0.5f)
                    {
                        map[z * 100 + x] = true;
                    }
                }
            }
        }

        // Casas retrô (RetroHouse): só o corpo da casa; o jardim da frente fica com grama.
        foreach (BoxCollider box in c.est.GetComponentsInChildren<BoxCollider>(true))
        {
            if (!box.name.StartsWith("Colisor_"))
            {
                continue;
            }

            // (box.bounds ainda não vale: o colisor acabou de ser criado) — calcula pelos cantos.
            Transform t = box.transform;
            Bounds b = new Bounds(t.TransformPoint(box.center), Vector3.zero);
            for (int k = 0; k < 8; k++)
            {
                Vector3 corner = box.center + Vector3.Scale(box.size / 2f, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                b.Encapsulate(t.TransformPoint(corner));
            }

            int x0 = Mathf.Clamp(Mathf.FloorToInt(b.min.x - origin.x), 0, 99), x1 = Mathf.Clamp(Mathf.CeilToInt(b.max.x - origin.x), 0, 99);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(b.min.z - origin.z), 0, 99), z1 = Mathf.Clamp(Mathf.CeilToInt(b.max.z - origin.z), 0, 99);
            Vector3 half = box.size / 2f;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector3 l = t.InverseTransformPoint(new Vector3(origin.x + x + 0.5f, b.center.y, origin.z + z + 0.5f)) - box.center;
                    if (Mathf.Abs(l.x) <= half.x + 0.2f && Mathf.Abs(l.z) <= half.z + 0.2f)
                    {
                        map[z * 100 + x] = true;
                    }
                }
            }
        }

        return map;
    }

    private static void Disc(bool[] map, float cx, float cz, float radius)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(cx - radius), 0, 99), x1 = Mathf.Clamp(Mathf.FloorToInt(cx + radius), 0, 99);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(cz - radius), 0, 99), z1 = Mathf.Clamp(Mathf.FloorToInt(cz + radius), 0, 99);
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x + 0.5f - cx, dz = z + 0.5f - cz;
                if (dx * dx + dz * dz <= radius * radius)
                {
                    map[z * 100 + x] = true;
                }
            }
        }
    }

    // ---------- Vilarejo (blocos feitos à mão) ----------

    // Os blocos do Vilarejo do Bilu são prefabs salvos antes da natureza: troca as árvores, arbustos,
    // pedras e flores de blockout pelos modelos do kit e põe a grama ao vento e a vegetação rasteira.
    // Chamado pelo WorldStreamer ao carregar um bloco fixo. Sem o Kit de Natureza, não faz nada.
    public static void Naturalize(GameObject rootObject, BlockKit kit, Vector2Int coord)
    {
        WorldBlock block = rootObject != null ? rootObject.GetComponent<WorldBlock>() : null;
        if (kit == null || kit.nature == null || block == null || block.StaticRoot == null)
        {
            return;
        }

        BlockSpec spec = block.HasSpec ? block.Spec : BlockSpec.Make(BlockType.Bairro, Socket.Mato, Socket.Mato, Socket.Mato, Socket.Mato);
        if (spec.seed == 0)
        {
            spec.seed = coord.x * 73856093 ^ coord.y * 19349663;
        }

        Ctx c = new Ctx
        {
            kit = kit,
            spec = spec,
            rng = new System.Random(spec.seed ^ 0x2545f491),
            root = rootObject.transform,
            est = block.StaticRoot,
            dyn = block.DynamicRoot,
            coord = coord,
            terrain = PlanetTerrain.Active,
        };
        if (c.terrain)
        {
            c.biome = PlanetTerrain.BiomeOfBlock(coord);
            c.assets = rootObject.GetComponent<BlockAssets>() ?? rootObject.AddComponent<BlockAssets>();
        }

        for (int s = 0; s < 4; s++)
        {
            c.arm[s] = spec.Get(s) == Socket.Rua;
        }

        ReplaceVillageHouses(c);
        ConvertVillageRiver(c);

        List<Transform> trees = new List<Transform>();
        List<Transform> bushes = new List<Transform>();
        List<Transform> rocks = new List<Transform>();
        List<Transform> flowers = new List<Transform>();
        foreach (Transform t in c.est.GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "Arvore": if (t.Find("Tronco") != null) trees.Add(t); break;
                case "Arbusto": if (t.childCount == 0) bushes.Add(t); break;
                case "Pedra": if (t.childCount == 0) rocks.Add(t); break;
                case "Flor": flowers.Add(t); break;
            }
        }

        foreach (Transform tree in trees)
        {
            Transform trunk = tree.Find("Tronco");
            float scale = Mathf.Clamp(trunk.localScale.x / 0.5f, 0.6f, 1.8f);
            List<GameObject> old = new List<GameObject>();
            foreach (Transform child in tree)
            {
                old.Add(child.gameObject);
            }

            if (NatureTree(c, tree, scale))
            {
                Remove(old);
            }
        }

        foreach (Transform bush in bushes)
        {
            Vector3 p = bush.localPosition;
            Transform made = NatureBush(c, bush.parent, new Vector2(p.x, p.z));
            if (made != null)
            {
                made.localPosition = new Vector3(p.x, Mathf.Max(0f, p.y - bush.localScale.y * 0.45f), p.z);
                Remove(bush.gameObject);
            }
        }

        foreach (Transform rock in rocks)
        {
            Vector3 p = rock.localPosition;
            Transform made = NatureRock(c, rock.parent, new Vector2(p.x, p.z), rock.localScale.x);
            if (made != null)
            {
                made.localPosition = new Vector3(p.x, Mathf.Max(0f, p.y - rock.localScale.y * 0.4f), p.z);
                Remove(rock.gameObject);
            }
        }

        if (Has(kit.nature.flowers))
        {
            foreach (Transform flower in flowers)
            {
                Remove(flower.gameObject);
            }
        }

        Physics.SyncTransforms();
        GroundCover(c);
    }

    // Rio do Vilarejo (Missão 20): o canal reto de concreto vira rio natural, igual ao que continua
    // fora do Vilarejo. Tira o chão em caixas, o fundo, as rampas e a água do canal; põe o chão com
    // relevo (o GreatLake cava o leito reto embaixo das pontes) e a água transparente do terreno.
    // Pontes, píeres e ruas ficam.
    private static void ConvertVillageRiver(Ctx c)
    {
        if (!c.terrain || !GreatLake.Active || c.spec.type != BlockType.Rio)
        {
            return;
        }

        Transform pivot = c.est.Find("Rio");
        if (pivot == null || pivot.Find("Agua") == null)
        {
            return;
        }

        string[] parts = { "Chao_Norte", "Chao_Sul", "Fundo", "Margem_Norte", "Margem_Sul", "Agua" };
        foreach (string part in parts)
        {
            Transform t = pivot.Find(part);
            if (t != null)
            {
                Object.DestroyImmediate(t.gameObject);
            }
        }

        Transform triggers = null;
        foreach (Transform t in c.root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Agua_Gatilho")
            {
                triggers = t.parent;
                Object.DestroyImmediate(t.gameObject);
                break;
            }
        }

        if (triggers == null)
        {
            triggers = new GameObject("Gatilhos").transform;
            triggers.SetParent(c.root, false);
        }

        PlanetTerrain.BuildGround(c.est, c.coord, c.kit.grass, 2, c.assets, out float lowest);
        if (lowest < PlanetTerrain.WaterLevel)
        {
            PlanetTerrain.AddWater(c.est, triggers, c.kit.water, c.assets);
        }

        Physics.SyncTransforms();
    }

    // Casas e prédios de blockout do Vilarejo viram as casas/prédios retrô do Blender (mesma frente,
    // mesmo lugar). Galpões e o resto ficam como estão (ainda não têm modelo novo).
    private static void ReplaceVillageHouses(Ctx c)
    {
        bool houses = c.kit.retroHouses && c.kit.houseModels != null && c.kit.houseModels.Length > 0;
        bool buildings = c.kit.buildingModels != null && c.kit.buildingModels.Length > 0;
        if (!houses && !buildings)
        {
            return;
        }

        Material template = c.kit.houseWalls != null && c.kit.houseWalls.Length > 0 ? c.kit.houseWalls[0] : null;
        List<Transform> found = new List<Transform>();
        foreach (Transform t in c.est.GetComponentsInChildren<Transform>(true))
        {
            bool house = t.name == "Casa" || t.name.StartsWith("Casa_");
            bool building = t.name == "Predio" || t.name.StartsWith("Predio_");
            if ((house || building) && t.GetComponent<HouseBreakable>() == null && t.GetComponentInParent<HouseBreakable>() == null)
            {
                found.Add(t);
            }
        }

        foreach (Transform old in found)
        {
            if (old == null)
            {
                continue;
            }

            bool building = old.name.StartsWith("Predio");
            if (building ? !buildings : !houses)
            {
                continue;
            }

            Renderer[] renderers = old.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                continue;
            }

            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            if (Mathf.Min(b.size.x, b.size.z) < (building ? 8f : 4f))
            {
                continue; // pequeno demais para o modelo novo (deixa como está)
            }

            // Frente = lado da porta (ou o +Z da peça antiga).
            Transform door = FindDeep(old, "Porta");
            Vector3 forward = door != null ? door.position - b.center : old.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = old.forward;
            forward.Normalize();

            Transform parent = old.parent;
            float yaw = Quaternion.LookRotation(forward).eulerAngles.y - parent.eulerAngles.y;
            Vector3 root = b.center + forward * (building ? 0f : 1f); // a casa retrô fica 1 m para trás do ponto
            Vector3 local = parent.InverseTransformPoint(new Vector3(root.x, b.min.y, root.z));
            Remove(old.gameObject);

            Transform made = building
                ? RetroHouse.BuildBuilding(parent, new Vector2(local.x, local.z), yaw, c.rng, template, c.kit.buildingModels)
                : RetroHouse.Build(parent, new Vector2(local.x, local.z), yaw, c.rng, template, c.kit.houseModels);
            made.localPosition = new Vector3(local.x, local.y, local.z);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }

        return null;
    }

    // Some na hora (sem render nem colisão) e é destruído no fim do quadro.
    private static void Remove(GameObject go)
    {
        go.SetActive(false);
        go.transform.SetParent(null, false);
        Object.Destroy(go);
    }

    private static void Remove(List<GameObject> list)
    {
        foreach (GameObject go in list)
        {
            Remove(go);
        }
    }

    // ---------- Modelo ----------

    // Coloca o modelo dentro de "wrapper" com o tamanho pedido (altura, ou maior medida),
    // base no chão, cor do bioma nas folhas e pedra de deserto nos biomas secos.
    private static Transform NatureModel(Ctx c, GameObject model, Transform wrapper, float size, bool byHeight, ShadowCastingMode shadows)
    {
        Transform holder = Child(wrapper, "Modelo");
        GameObject instance = Object.Instantiate(model, holder, false);
        foreach (Collider extra in instance.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(extra);
        }

        if (!natureBounds.TryGetValue(model, out Bounds bounds))
        {
            bounds = LocalBounds(holder.gameObject);
            natureBounds[model] = bounds;
        }

        float measure = byHeight ? bounds.size.y : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float s = measure > 0.001f ? size / measure : 1f;
        holder.localScale = Vector3.one * s;
        holder.localPosition = new Vector3(0f, -bounds.min.y * s, 0f);

        NatureKit k = Nature(c);
        Planet.Biome biome = NatureBiome(c);
        bool dry = biome == Planet.Biome.Dunas || biome == Planet.Biome.Cratera || biome == Planet.Biome.Antenas || biome == Planet.Biome.Estepe;
        int shade = c.rng.Next(TintShade.Length);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = shadows;
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int m = 0; m < materials.Length; m++)
            {
                Material original = materials[m];
                if (original == null) continue;
                if (dry && k.desertRock != null && original == k.normalRock)
                {
                    materials[m] = k.desertRock;
                    changed = true;
                }
                else if (c.terrain && k.alienMaterial != null && original == k.alienMaterial)
                {
                    materials[m] = AlienTinted(original, biome); // Missão 20: cores do bioma no Space Kit
                    changed = true;
                }
                else if (c.terrain && IsTintable(k, original))
                {
                    materials[m] = Tinted(original, biome, shade);
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }

        return holder;
    }

    // Árvores, plantas e pedras do Ultimate Space Kit (paleta de cores numa textura só) recoloridas
    // para combinar com o bioma: as cores escuras (troncos) puxam para o chão escuro do bioma e as
    // claras (copas, bulbos) para a cor das copas do bioma, guardando 30% da cor original (continuam
    // "estranhas", mas no tom da região). Uma cópia da paleta por bioma, feita uma vez.
    private static readonly Dictionary<int, Material> alienTint = new Dictionary<int, Material>();

    private static Material AlienTinted(Material source, Planet.Biome biome)
    {
        if (alienTint.TryGetValue((int)biome, out Material made) && made != null)
        {
            return made;
        }

        Texture2D palette = source.mainTexture as Texture2D;
        if (palette == null || !palette.isReadable)
        {
            return source; // rode de novo "Preparar Space Kit": a paleta precisa ficar legível
        }

        PlanetTerrain.BiomeStyle style = PlanetTerrain.StyleOf(biome);
        Color dark = style.low * 0.42f;
        Color mid = Color.Lerp(style.low, style.foliage, 0.55f);
        Color bright = Color.Lerp(style.foliage, Color.white, 0.12f);
        Color[] pixels = palette.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            float lum = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            float t = Mathf.InverseLerp(0.06f, 0.62f, lum);
            Color target = t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, bright, (t - 0.5f) * 2f);
            Color result = Color.Lerp(target, c, 0.3f);
            result.a = c.a;
            pixels[i] = result;
        }

        Texture2D texture = new Texture2D(palette.width, palette.height, TextureFormat.RGBA32, false)
        {
            name = palette.name + "_" + biome,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.SetPixels(pixels);
        texture.Apply(false, true);

        made = new Material(source) { name = source.name + "_" + biome };
        made.mainTexture = texture;
        if (made.HasProperty("_BaseMap"))
        {
            made.SetTexture("_BaseMap", texture);
        }

        alienTint[(int)biome] = made;
        return made;
    }

    private static bool IsTintable(NatureKit k, Material material)
    {
        if (k.tintable == null) return false;
        foreach (Material t in k.tintable)
        {
            if (t == material) return true;
        }

        return false;
    }

    // Cópia pintada com a cor do bioma (3 tons). Compartilhada entre os blocos, como as copas de blockout.
    private static Material Tinted(Material original, Planet.Biome biome, int shade)
    {
        (int, int, int) key = (original.GetInstanceID(), (int)biome, shade);
        if (natureTint.TryGetValue(key, out Material tinted) && tinted != null)
        {
            return tinted;
        }

        PlanetTerrain.BiomeStyle style = PlanetTerrain.StyleOf(biome);
        // A grama puxa para a cor do chão, para não ficar um tapete verde em cima da areia.
        Color color = original.name.Contains("Grass") ? Color.Lerp(style.low, style.foliage, 0.45f) : style.foliage;
        color *= TintShade[shade];
        color.a = 1f;
        tinted = new Material(original) { name = original.name + "_" + biome + "_" + shade };
        tinted.color = color;
        if (tinted.HasProperty("_BaseColor"))
        {
            tinted.SetColor("_BaseColor", color);
        }

        natureTint[key] = tinted;
        return tinted;
    }
}
