using System.Collections.Generic;
using UnityEngine;

// Casas retrofuturistas do bairro (Missão 20): subúrbio anos 50 "do futuro", cantos arredondados,
// telhado em laje que sobra para fora, porta em arco com escotilha, janelão com sala dentro,
// faixa colorida, garagem, caixa de correio e lixeira. Feitas pensando no desempenho:
// 1. Casa inteira numa peça só: a casa intacta é UMA malha por nível de detalhe (sem peças soltas).
//    Os pedaços só aparecem quando ela desmorona (DestructibleStructure quebra em blocos na hora).
//    Colisão: uma caixa por corpo da casa (sem renderer).
// 2. LOD: de perto a casa completa; a ~60 m sem escotilha, molduras, pedras e caixa de correio e com
//    menos arredondamento; longe (~170 m) quase uma caixa colorida. O Unity troca sozinho (LODGroup).
// 3. Um material para todas: as cores vêm de uma textura-paleta (8 paletas × 8 cores) e a malha só
//    aponta para a célula certa. Variação = modelo (3) × paleta (8) × espelhada (2) = 48 casas
//    diferentes com as mesmas malhas reaproveitadas (no máximo 72 malhas no jogo inteiro).
// 4. Interior falso: a sala (sofá, abajur, cortinas...) é um desenho na mesma textura, um pouco
//    para dentro da moldura da janela. Nada modelado por dentro.
// A textura é desenhada por código ao começar o jogo (não precisa de arquivo).
public static class RetroHouse
{
    private const int Wall = 0, Roof = 1, Stripe = 2, Door = 3, Trim = 4, Plinth = 5, Glass = 6, Garage = 7;
    private const int PaletteCount = 8;
    private const int AtlasSize = 512;

    // parede, telhado, faixa, porta, moldura, rodapé/concreto, vidro, garagem (escuro)
    private static readonly string[][] PaletteHex =
    {
        new[] { "F2E3C9", "FAF2E2", "E2735A", "D9604A", "F8EFDF", "D6C3A2", "2F5862", "4A403B" }, // coral
        new[] { "EFE2C6", "F8F1E1", "8FB79A", "6F9C7C", "F6EEDD", "D3C4A4", "2F5862", "45403A" }, // sálvia
        new[] { "F3E7D0", "FBF4E6", "5B8FB0", "3F6E95", "F8F0E2", "D7C7A9", "2B4F63", "3F3D40" }, // azul
        new[] { "F1DDB8", "F9EFD9", "E6B04A", "C98A2E", "F7ECD6", "D2BC94", "33555C", "4A4036" }, // mostarda
        new[] { "EEDFD4", "F8F0EA", "B48CC4", "9467A8", "F6EEE8", "D3C1B6", "34505E", "463D45" }, // lilás
        new[] { "E8EFE6", "F5F8F2", "4DB3A6", "2F8F84", "F2F6EF", "C7CFC2", "2A5560", "3A4240" }, // turquesa
        new[] { "F5E2D3", "FBF1E8", "E58A8A", "C95F6E", "F9EEE5", "D9C1AF", "315560", "4A3D3D" }, // pêssego
        new[] { "E9E2D4", "F6F2EA", "7A8FA6", "E27D4E", "F4EFE6", "CFC6B5", "2E5260", "403E3C" }, // porta laranja
    };

    // ---------- Modelos ----------

    private sealed class Body
    {
        public float x, z, w, d, h, r;
        public Body(float x, float z, float w, float d, float h, float r) { this.x = x; this.z = z; this.w = w; this.d = d; this.h = h; this.r = r; }
        public float Front => z + d / 2f;
    }

    private sealed class Opening
    {
        public int kind; // 0 porta, 1 janela, 2 garagem
        public int body;
        public float x, y, w, h, r;
    }

    private sealed class Template
    {
        public Body[] bodies;
        public Opening[] openings;
        public float mailboxX;
        public float drivewayX, drivewayW; // 0 = sem garagem
    }

    private static Opening DoorAt(int body, float x, float w, float h) => new Opening { kind = 0, body = body, x = x, w = w, h = h };
    private static Opening WindowAt(int body, float x, float y, float w, float h, float r) => new Opening { kind = 1, body = body, x = x, y = y, w = w, h = h, r = r };
    private static Opening GarageAt(int body, float x, float w, float h) => new Opening { kind = 2, body = body, x = x, w = w, h = h };

    // Frente da casa = +Z (para a rua). Cabe no lote de ~10 × 9,5 m.
    private static readonly Template[] Templates =
    {
        new Template // "Bolha": casinha com porta e janela
        {
            bodies = new[] { new Body(0f, -0.8f, 8.2f, 7f, 3.3f, 1.8f) },
            openings = new[] { DoorAt(0, 1.7f, 1.1f, 2.35f), WindowAt(0, -0.9f, 1.7f, 2.4f, 1.3f, 0.38f) },
            mailboxX = -3.1f,
        },
        new Template // "Panorâmica": maior, janelão
        {
            bodies = new[] { new Body(0f, -0.8f, 9.2f, 7.4f, 3.6f, 1.8f) },
            openings = new[] { DoorAt(0, -2.2f, 1.1f, 2.4f), WindowAt(0, 0.8f, 1.75f, 3.6f, 1.5f, 0.45f) },
            mailboxX = 3.7f,
        },
        new Template // "Garagem": casa + garagem com carro voador
        {
            bodies = new[] { new Body(-1.9f, -0.8f, 5.6f, 7f, 3.4f, 1.0f), new Body(2.6f, -0.8f, 4.0f, 7f, 3.0f, 0.8f) },
            openings = new[] { WindowAt(0, -2.7f, 1.7f, 1.7f, 1.25f, 0.35f), DoorAt(0, -0.9f, 1.05f, 2.3f), GarageAt(1, 2.6f, 2.1f, 2.35f) },
            mailboxX = -4.1f,
            drivewayX = 2.6f,
            drivewayW = 3.0f,
        },
    };

    private static Material material;
    private static Texture2D atlas;
    private static Texture2D emissionAtlas;
    private static readonly Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
    private static readonly Color[][] palettes = BuildPalettes();

    // ---------- Montagem ----------

    // Cria a casa (raiz "Casa" com LODGroup, colisores e DestructibleStructure) no lugar pedido.
    public static Transform Build(Transform parent, Vector2 position, float yaw, System.Random rng, Material template, GameObject[] models = null)
    {
        if (models != null && models.Length > 0)
        {
            return BuildFromModel(parent, position, yaw, rng, template, models);
        }

        int model = rng.Next(Templates.Length);
        int palette = rng.Next(PaletteCount);
        bool mirror = rng.NextDouble() < 0.5;
        Template t = Templates[model];

        Transform house = new GameObject("Casa").transform;
        house.SetParent(parent, false);
        house.localPosition = new Vector3(position.x, 0f, position.y);
        house.localRotation = Quaternion.Euler(0f, yaw, 0f);

        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(house, false);
        visual.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);

        Material shared = SharedMaterial(template);
        Renderer[] lods = new Renderer[3];
        for (int lod = 0; lod < 3; lod++)
        {
            GameObject go = new GameObject("LOD" + lod);
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshFor(model, palette, lod);
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = shared;
            renderer.shadowCastingMode = lod == 2 ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            lods[lod] = renderer;
        }

        LODGroup group = house.gameObject.AddComponent<LODGroup>();
        group.SetLODs(new[]
        {
            new LOD(0.11f, new[] { lods[0] }),  // ~60 m
            new LOD(0.04f, new[] { lods[1] }),  // ~170 m
            new LOD(0.008f, new[] { lods[2] }), // some (a névoa já escondeu)
        });
        group.RecalculateBounds();

        // Entrada da garagem: marca para a grama não nascer em cima (BlockedMap).
        if (t.drivewayW > 0f)
        {
            float z0 = t.bodies[t.bodies.Length - 1].Front;
            Transform clear = new GameObject("SemGrama").transform;
            clear.SetParent(visual, false);
            clear.localPosition = new Vector3(t.drivewayX, 0f, (z0 + 4.75f) / 2f);
            clear.localScale = new Vector3(t.drivewayW, 1f, 4.75f - z0);
        }

        // Colisão: uma caixa por corpo (telhado incluído).
        for (int i = 0; i < t.bodies.Length; i++)
        {
            Body b = t.bodies[i];
            BoxCollider box = new GameObject("Colisor_" + i).AddComponent<BoxCollider>();
            box.transform.SetParent(house, false);
            box.transform.localPosition = new Vector3(mirror ? -b.x : b.x, (b.h + 0.35f) / 2f, b.z);
            box.size = new Vector3(b.w + 0.3f, b.h + 0.35f, b.d + 0.3f);
        }

        DestructibleStructure structure = house.gameObject.AddComponent<DestructibleStructure>();
        structure.SetDebrisColors(palettes[palette][Wall], palettes[palette][Stripe]);
        return house;
    }

    // ---------- Material e textura ----------

    private static Material SharedMaterial(Material template)
    {
        if (material != null)
        {
            return material;
        }

        atlas = BuildAtlas();
        material = template != null ? new Material(template) : new Material(Shader.Find(CurvedHorizon.ShaderName));
        material.name = "CasaRetro (paleta)";
        material.mainTexture = atlas;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", atlas);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        material.color = Color.white;
        if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
        material.DisableKeyword("_NORMALMAP");
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.22f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_EmissionMap"))
        {
            material.SetTexture("_EmissionMap", emissionAtlas);
            material.SetColor("_EmissionColor", Color.white);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        material.enableInstancing = true;
        return material;
    }

    private static Color[][] BuildPalettes()
    {
        Color[][] result = new Color[PaletteHex.Length][];
        for (int p = 0; p < PaletteHex.Length; p++)
        {
            result[p] = new Color[8];
            for (int k = 0; k < 8; k++)
            {
                ColorUtility.TryParseHtmlString("#" + PaletteHex[p][k], out result[p][k]);
            }
        }

        return result;
    }

    // Metade de cima: células de cor 32 × 32 (paleta p = linha p % 8, colunas (p / 8) * 8 + cor).
    // Metade de baixo: 4 salas de 256 × 128.
    private static Vector2 CellUV(int palette, int cell)
    {
        int column = (palette / 8) * 8 + cell;
        int row = palette % 8;
        return new Vector2((column + 0.5f) / 16f, (8 + row + 0.5f) / 16f);
    }

    private static Rect InteriorUV(int index)
    {
        float pad = 2f / AtlasSize;
        return new Rect((index % 2) * 0.5f + pad, (index / 2) * 0.25f + pad, 0.5f - 2f * pad, 0.25f - 2f * pad);
    }

    private static Texture2D BuildAtlas()
    {
        Painter paint = new Painter(AtlasSize);
        for (int p = 0; p < PaletteCount; p++)
        {
            for (int k = 0; k < 8; k++)
            {
                int column = (p / 8) * 8 + k;
                int row = 8 + p % 8;
                paint.Rect(column * 32, row * 32, column * 32 + 32, row * 32 + 32, palettes[p][k]);
            }
        }

        for (int i = 0; i < 4; i++)
        {
            DrawInterior(paint, (i % 2) * 256, (i / 2) * 128, i);
        }

        // Cores comuns a todas as paletas (casas do Blender): luz, concreto, carro, metal, reboco.
        for (int k = 0; k < SharedHex.Length; k++)
        {
            paint.Rect((8 + k) * 32, 8 * 32, (9 + k) * 32, 9 * 32, Hex(SharedHex[k]));
        }

        // Brilho: as luzes acesas e um pouco da sala atrás do vidro (o resto preto).
        Painter glow = new Painter(AtlasSize);
        glow.Rect(0, 0, AtlasSize, AtlasSize, Color.black);
        glow.Rect((8 + Light - 8) * 32, 8 * 32, (9 + Light - 8) * 32, 9 * 32, Hex("8FF0D2"));
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < AtlasSize; x++)
            {
                glow.Set(x, y, paint.Get(x, y) * 0.28f);
            }
        }

        emissionAtlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, true) { name = "CasaRetro_Brilho" };
        emissionAtlas.SetPixels32(glow.pixels);
        emissionAtlas.wrapMode = TextureWrapMode.Clamp;
        emissionAtlas.Apply(true, true);

        Texture2D texture = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, true) { name = "CasaRetro_Atlas" };
        texture.SetPixels32(paint.pixels);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 2;
        texture.Apply(true, true);
        return texture;
    }

    // Salas vistas pela janela (estilo anos 50): parede, piso, cortinas, sofá/mesa, abajur, quadro, planta.
    private static void DrawInterior(Painter p, int ox, int oy, int index)
    {
        Color[] walls = { Hex("E9D9B6"), Hex("DCE6DD"), Hex("F0DCC4"), Hex("D9E1E6") };
        Color[] floors = { Hex("9C6B45"), Hex("8A6A50"), Hex("A0754E"), Hex("7E6A5C") };
        Color[] sofas = { Hex("D9773F"), Hex("C9603F"), Hex("4F7FA8"), Hex("D88A3A") };
        Color curtain = index % 2 == 0 ? Hex("4E8C8F") : Hex("6E9D8A");
        Color wall = walls[index];

        p.Rect(ox, oy, ox + 256, oy + 128, wall);
        p.Rect(ox, oy, ox + 256, oy + 30, floors[index]);                  // piso
        p.Rect(ox, oy + 30, ox + 256, oy + 33, Darker(wall, 0.8f));       // rodapé

        // Cortinas com dobras.
        for (int side = 0; side < 2; side++)
        {
            int x0 = side == 0 ? ox : ox + 226;
            p.Rect(x0, oy + 18, x0 + 30, oy + 128, curtain);
            for (int f = 4; f < 30; f += 8)
            {
                p.Rect(x0 + f, oy + 18, x0 + f + 2, oy + 128, Darker(curtain, 0.82f));
            }
        }

        Color warm = Hex("F7DE8E");
        if (index == 1)
        {
            // Sala de jantar: mesa redonda, cadeiras, luminária pendente.
            p.Ellipse(ox + 128, oy + 52, 46, 7, Hex("C8A47A"));
            p.Rect(ox + 125, oy + 30, ox + 131, oy + 50, Hex("8A6A50"));
            p.Rect(ox + 70, oy + 30, ox + 92, oy + 62, sofas[0]);
            p.Rect(ox + 164, oy + 30, ox + 186, oy + 62, sofas[0]);
            p.Rect(ox + 127, oy + 92, ox + 129, oy + 128, Hex("5A4A3A"));
            p.Ellipse(ox + 128, oy + 90, 18, 9, Hex("7CC4B4"));
            p.Glow(ox + 128, oy + 80, 34, warm, 0.35f);
        }
        else
        {
            // Sala de estar: sofá, abajur, mesinha.
            Color sofa = sofas[index];
            p.Rect(ox + 66, oy + 52, ox + 170, oy + 80, Darker(sofa, 0.9f)); // encosto
            p.Rect(ox + 60, oy + 32, ox + 176, oy + 56, sofa);                // assento
            p.Circle(ox + 62, oy + 52, 12, sofa);
            p.Circle(ox + 174, oy + 52, 12, sofa);
            p.Rect(ox + 116, oy + 38, ox + 118, oy + 54, Darker(sofa, 0.75f));
            p.Rect(ox + 196, oy + 30, ox + 199, oy + 84, Hex("5A4A3A"));      // abajur
            p.Rect(ox + 186, oy + 84, ox + 210, oy + 100, warm);
            p.Glow(ox + 198, oy + 92, 30, warm, 0.45f);
            p.Ellipse(ox + 40 + 4, oy + 40, 10, 10, Hex("5F8F4E"));           // planta
            p.Rect(ox + 40, oy + 30, ox + 50, oy + 36, Hex("C9603F"));
        }

        // Quadro / relógio "sunburst" na parede.
        p.Circle(ox + 120, oy + 104, 9, Hex("C9A65A"));
        for (int ray = 0; ray < 12; ray++)
        {
            float a = ray * Mathf.PI / 6f;
            for (int s = 9; s < 17; s++)
            {
                p.Dot(ox + 120 + Mathf.RoundToInt(Mathf.Cos(a) * s), oy + 104 + Mathf.RoundToInt(Mathf.Sin(a) * s), Hex("8C6A3A"));
            }
        }

        // Reflexo no vidro (faixas diagonais claras) e leve tom frio.
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                Color c = p.Get(ox + x, oy + y);
                c = Color.Lerp(c, Hex("9FC3CC"), 0.12f);
                int band = (x + y * 2) % 140;
                if (band < 14 || band > 30 && band < 36)
                {
                    c = Color.Lerp(c, Color.white, 0.18f);
                }

                p.Set(ox + x, oy + y, c);
            }
        }
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    private static Color Darker(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

    private sealed class Painter
    {
        public readonly Color32[] pixels;
        private readonly int size;

        public Painter(int size)
        {
            this.size = size;
            pixels = new Color32[size * size];
        }

        public Color Get(int x, int y) => pixels[y * size + x];
        public void Set(int x, int y, Color c) => pixels[y * size + x] = c;

        public void Dot(int x, int y, Color c)
        {
            if (x >= 0 && y >= 0 && x < size && y < size) Set(x, y, c);
        }

        public void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(size, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(size, x1); x++)
                    Set(x, y, c);
        }

        public void Circle(int cx, int cy, int r, Color c) => Ellipse(cx, cy, r, r, c);

        public void Ellipse(int cx, int cy, int rx, int ry, Color c)
        {
            for (int y = -ry; y <= ry; y++)
                for (int x = -rx; x <= rx; x++)
                    if ((float)(x * x) / (rx * rx) + (float)(y * y) / (ry * ry) <= 1f)
                        Dot(cx + x, cy + y, c);
        }

        public void Glow(int cx, int cy, int r, Color c, float strength)
        {
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    int px = cx + x, py = cy + y;
                    if (px < 0 || py < 0 || px >= size || py >= size) continue;
                    float d = Mathf.Sqrt(x * x + y * y) / r;
                    if (d < 1f) Set(px, py, Color.Lerp(Get(px, py), c, (1f - d) * (1f - d) * strength));
                }
            }
        }
    }

    // ---------- Malhas ----------

    private static Mesh MeshFor(int model, int palette, int lod)
    {
        int key = model * 100 + palette * 10 + lod;
        if (meshes.TryGetValue(key, out Mesh mesh) && mesh != null)
        {
            return mesh;
        }

        mesh = BuildMesh(Templates[model], palette, lod);
        mesh.name = $"CasaRetro_{model}_{palette}_LOD{lod}";
        meshes[key] = mesh;
        return mesh;
    }

    private static Mesh BuildMesh(Template t, int palette, int lod)
    {
        Builder b = new Builder();
        int corner = lod == 0 ? 6 : lod == 1 ? 3 : 1;
        int arc = lod == 0 ? 6 : lod == 1 ? 3 : 2;
        Vector2 C(int cell) => CellUV(palette, cell);

        foreach (Body body in t.bodies)
        {
            Outline plan = Outline.RoundRect(body.x, body.z, body.w, body.d, body.r, corner);
            b.Ring(plan, 0f, 0f, body.h, C(Wall));
            if (lod < 2)
            {
                b.Ring(plan, 0.04f, 0f, 0.32f, C(Plinth));
            }

            b.Ring(plan, 0.025f, 1.12f, 1.36f, C(Stripe));

            // Telhado em laje com beiral; de perto, levemente abaulado.
            Outline roof = Outline.RoundRect(body.x, body.z, body.w + 0.9f, body.d + 0.9f, body.r + 0.45f, corner);
            b.Ring(roof, 0f, body.h, body.h + 0.34f, C(Roof));
            b.Cap(roof, body.h, 0f, false, C(Roof));
            b.Cap(roof, body.h + 0.34f, lod == 0 ? 0.22f : 0f, true, C(Roof));
        }

        int interior = palette % 4;
        foreach (Opening o in t.openings)
        {
            float front = t.bodies[o.body].Front;
            switch (o.kind)
            {
                case 0: // porta em arco
                {
                    Outline arch = Outline.Arch(o.x, o.w, o.h, arc);
                    b.Extrude(arch, front, front + (lod == 2 ? 0.02f : 0.07f), C(Door), lod < 2);
                    if (lod < 2)
                    {
                        Outline frame = Outline.Arch(o.x, o.w + 0.24f, o.h + 0.12f, arc);
                        b.Frame(frame, arch, front, front + 0.05f, C(Trim), false);
                    }

                    if (lod == 0)
                    {
                        float cy = o.h - o.w / 2f - 0.05f;
                        Outline outer = Outline.RoundRect2D(o.x, cy, 0.42f, 0.42f, 0.21f, 4);
                        Outline glass = Outline.RoundRect2D(o.x, cy, 0.3f, 0.3f, 0.15f, 4);
                        b.Frame(outer, glass, front + 0.07f, front + 0.1f, C(Trim), true);
                        b.Fill(glass, front + 0.08f, C(Glass), null);
                    }

                    if (lod == 0)
                    {
                        // Caminho de pedras até a calçada.
                        float z = front + 0.55f;
                        for (int s = 0; z < 4.7f; s++, z += 0.7f)
                        {
                            float sx = o.x + (s % 2 == 0 ? -0.12f : 0.12f);
                            Outline stone = Outline.RoundRect(sx, z, 0.95f, 0.5f, 0.24f, 3);
                            b.Ring(stone, 0f, -0.02f, 0.04f, C(Plinth));
                            b.Cap(stone, 0.04f, 0f, true, C(Plinth));
                        }
                    }

                    break;
                }
                case 1: // janela com sala dentro
                {
                    Outline inner = Outline.RoundRect2D(o.x, o.y, o.w, o.h, o.r, lod == 2 ? 1 : arc);
                    if (lod < 2)
                    {
                        Outline outer = Outline.RoundRect2D(o.x, o.y, o.w + 0.26f, o.h + 0.26f, o.r + 0.13f, arc);
                        b.Frame(outer, inner, front, front + 0.18f, C(Trim), lod == 0);
                    }

                    Rect uv = InteriorUV(interior);
                    interior = (interior + 1) % 4;
                    float x0 = o.x - o.w / 2f, y0 = o.y - o.h / 2f;
                    b.Fill(inner, front + 0.05f, default, // na frente da faixa colorida, atrás da moldura
                        q => new Vector2(uv.x + (q.x - x0) / o.w * uv.width, uv.y + (q.y - y0) / o.h * uv.height));
                    break;
                }
                case 2: // garagem (abertura escura de mentira) com moldura
                {
                    Outline arch = Outline.Arch(o.x, o.w, o.h, arc);
                    b.Fill(arch, front + 0.04f, C(Garage), null);
                    if (lod < 2)
                    {
                        Outline frame = Outline.Arch(o.x, o.w + 0.36f, o.h + 0.18f, arc);
                        b.Frame(frame, arch, front, front + 0.08f, C(Trim), lod == 0);
                    }

                    break;
                }
            }
        }

        if (lod == 0)
        {
            float mz = 4.25f;
            // Caixa de correio: poste + caixa arredondada.
            b.Box(new Vector3(t.mailboxX, 0.5f, mz), new Vector3(0.12f, 1f, 0.12f), C(Trim));
            Outline head = Outline.RoundRect(t.mailboxX, mz, 0.36f, 0.55f, 0.12f, 3);
            b.Ring(head, 0f, 1f, 1.32f, C(Door));
            b.Cap(head, 1.32f, 0.04f, true, C(Door));
            b.Cap(head, 1f, 0f, false, C(Door));

            // Lixeira redonda com faixa.
            float bx = t.mailboxX + (t.mailboxX < 0f ? 0.75f : -0.75f);
            Outline bin = Outline.RoundRect(bx, mz, 0.5f, 0.5f, 0.25f, 4);
            b.Ring(bin, 0f, 0f, 0.8f, C(Roof));
            b.Ring(bin, 0.01f, 0.1f, 0.22f, C(Stripe));
            b.Cap(bin, 0.8f, 0.03f, true, C(Roof));
        }

        if (t.drivewayW > 0f && lod < 2)
        {
            float z0 = t.bodies[t.bodies.Length - 1].Front;
            b.Box(new Vector3(t.drivewayX, 0.02f, (z0 + 4.75f) / 2f), new Vector3(t.drivewayW, 0.04f, 4.75f - z0), C(Plinth));
        }

        return b.ToMesh();
    }

    // Contorno fechado (no chão: x, z; na fachada: x, y) com normais para fora.
    private sealed class Outline
    {
        public readonly List<Vector2> points = new List<Vector2>();
        public readonly List<Vector2> normals = new List<Vector2>();

        public Vector2 Center
        {
            get
            {
                Vector2 sum = Vector2.zero;
                foreach (Vector2 p in points) sum += p;
                return sum / points.Count;
            }
        }

        // Retângulo de cantos arredondados (no chão ou na fachada — mesmo formato).
        public static Outline RoundRect(float cx, float cy, float w, float h, float r, int segments)
        {
            Outline o = new Outline();
            r = Mathf.Clamp(r, 0.01f, Mathf.Min(w, h) / 2f - 0.001f);
            Vector2[] centers =
            {
                new Vector2(cx + w / 2f - r, cy + h / 2f - r),
                new Vector2(cx - w / 2f + r, cy + h / 2f - r),
                new Vector2(cx - w / 2f + r, cy - h / 2f + r),
                new Vector2(cx + w / 2f - r, cy - h / 2f + r),
            };
            for (int c = 0; c < 4; c++)
            {
                for (int k = 0; k <= segments; k++)
                {
                    float a = (c * 90f + k * 90f / segments) * Mathf.Deg2Rad;
                    Vector2 n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    o.points.Add(centers[c] + n * r);
                    o.normals.Add(n);
                }
            }

            return o;
        }

        public static Outline RoundRect2D(float cx, float cy, float w, float h, float r, int segments) => RoundRect(cx, cy, w, h, r, segments);

        // Arco de porta: retângulo com meio círculo em cima, base em y = 0.
        public static Outline Arch(float cx, float w, float h, int segments)
        {
            Outline o = new Outline();
            float r = w / 2f;
            float spring = Mathf.Max(0.05f, h - r);
            o.points.Add(new Vector2(cx + r, 0f));
            o.normals.Add(Vector2.right);
            for (int k = 0; k <= segments * 2; k++)
            {
                float a = k * Mathf.PI / (segments * 2);
                Vector2 n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                o.points.Add(new Vector2(cx, spring) + n * r);
                o.normals.Add(n);
            }

            o.points.Add(new Vector2(cx - r, 0f));
            o.normals.Add(Vector2.left);
            return o;
        }
    }

    private sealed class Builder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        private int Add(Vector3 p, Vector3 n, Vector2 uv)
        {
            vertices.Add(p);
            normals.Add(n);
            uvs.Add(uv);
            return vertices.Count - 1;
        }

        // Triângulo virado para "facing" (confere a ordem dos vértices sozinho).
        private void Tri(int a, int b, int c, Vector3 facing)
        {
            Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            triangles.Add(a);
            if (Vector3.Dot(cross, facing) >= 0f)
            {
                triangles.Add(b);
                triangles.Add(c);
            }
            else
            {
                triangles.Add(c);
                triangles.Add(b);
            }
        }

        private void Quad(int a, int b, int c, int d, Vector3 facing)
        {
            Tri(a, b, c, facing);
            Tri(a, c, d, facing);
        }

        // Parede em volta de um contorno no chão (x, z), de y0 a y1, afastada "offset" para fora.
        public void Ring(Outline o, float offset, float y0, float y1, Vector2 uv)
        {
            int start = vertices.Count;
            int count = o.points.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = o.points[i] + o.normals[i] * offset;
                Vector3 n = new Vector3(o.normals[i].x, 0f, o.normals[i].y);
                Add(new Vector3(p.x, y0, p.y), n, uv);
                Add(new Vector3(p.x, y1, p.y), n, uv);
            }

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 facing = normals[start + 2 * i] + normals[start + 2 * j];
                Quad(start + 2 * i, start + 2 * j, start + 2 * j + 1, start + 2 * i + 1, facing);
            }
        }

        // Tampa (em cima ou embaixo) de um contorno no chão; "dome" levanta o meio (telhado abaulado).
        public void Cap(Outline o, float y, float dome, bool up, Vector2 uv)
        {
            Vector3 facing = up ? Vector3.up : Vector3.down;
            Vector2 c = o.Center;
            int center = Add(new Vector3(c.x, y + dome, c.y), facing, uv);
            int start = vertices.Count;
            foreach (Vector2 n2 in o.normals)
            {
                int i = vertices.Count - start;
                Vector2 p = o.points[i];
                Vector3 n = dome > 0f ? new Vector3(n2.x * 0.3f, 1f, n2.y * 0.3f).normalized : facing;
                Add(new Vector3(p.x, y, p.y), n, uv);
            }

            int count = o.points.Count;
            for (int i = 0; i < count; i++)
            {
                Tri(center, start + i, start + (i + 1) % count, facing);
            }
        }

        // Peça plana da fachada (x, y) com espessura, de z0 (parede) a z1 (frente).
        public void Extrude(Outline o, float z0, float z1, Vector2 uv, bool sides)
        {
            Fill(o, z1, uv, null);
            if (!sides)
            {
                return;
            }

            int count = o.points.Count;
            int start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 n = new Vector3(o.normals[i].x, o.normals[i].y, 0f);
                Add(new Vector3(o.points[i].x, o.points[i].y, z0), n, uv);
                Add(new Vector3(o.points[i].x, o.points[i].y, z1), n, uv);
            }

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Quad(start + 2 * i, start + 2 * j, start + 2 * j + 1, start + 2 * i + 1, normals[start + 2 * i] + normals[start + 2 * j]);
            }
        }

        // Preenche um contorno da fachada no plano z (de frente para +Z). "map" = uv por ponto (sala da janela).
        public void Fill(Outline o, float z, Vector2 uv, System.Func<Vector2, Vector2> map)
        {
            Vector2 c = o.Center;
            int center = Add(new Vector3(c.x, c.y, z), Vector3.forward, map != null ? map(c) : uv);
            int start = vertices.Count;
            foreach (Vector2 p in o.points)
            {
                Add(new Vector3(p.x, p.y, z), Vector3.forward, map != null ? map(p) : uv);
            }

            int count = o.points.Count;
            for (int i = 0; i < count; i++)
            {
                Tri(center, start + i, start + (i + 1) % count, Vector3.forward);
            }
        }

        // Moldura entre dois contornos com o mesmo número de pontos (janela, porta, garagem).
        public void Frame(Outline outer, Outline inner, float z0, float z1, Vector2 uv, bool sides)
        {
            int count = outer.points.Count;
            int start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Add(new Vector3(outer.points[i].x, outer.points[i].y, z1), Vector3.forward, uv);
                Add(new Vector3(inner.points[i].x, inner.points[i].y, z1), Vector3.forward, uv);
            }

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Quad(start + 2 * i, start + 2 * j, start + 2 * j + 1, start + 2 * i + 1, Vector3.forward);
            }

            if (!sides)
            {
                return;
            }

            SideBand(outer, z0, z1, uv, 1f);
            SideBand(inner, z0, z1, uv, -1f);
        }

        private void SideBand(Outline o, float z0, float z1, Vector2 uv, float sign)
        {
            int count = o.points.Count;
            int start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 n = new Vector3(o.normals[i].x, o.normals[i].y, 0f) * sign;
                Add(new Vector3(o.points[i].x, o.points[i].y, z0), n, uv);
                Add(new Vector3(o.points[i].x, o.points[i].y, z1), n, uv);
            }

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Quad(start + 2 * i, start + 2 * j, start + 2 * j + 1, start + 2 * i + 1, normals[start + 2 * i] + normals[start + 2 * j]);
            }
        }

        // Caixa simples (poste, calçada da garagem).
        public void Box(Vector3 center, Vector3 size, Vector2 uv)
        {
            Vector3 h = size / 2f;
            Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 n in axes)
            {
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                Vector3 c = center + Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);
                int a = Add(c - du - dv, n, uv);
                int b = Add(c + du - dv, n, uv);
                int d = Add(c + du + dv, n, uv);
                int e = Add(c - du + dv, n, uv);
                Quad(a, b, d, e, n);
            }
        }

        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh();
            if (vertices.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false); // continua legível (desmoronar e assentar no relevo leem a malha)
            return mesh;
        }
    }

    // ======================================================================
    // Casas modeladas no Blender (Assets/Art/Houses, ferramenta "Preparar Casas (Missao 20)").
    // O FBX traz: LOD0_* (partes da casa inteira), LOD1, LOD2 e Caco_* (versão pré-fraturada).
    // Cada material do Blender (Parede, Faixa, Porta...) vira uma célula da textura-paleta: a casa
    // continua com UM material e as 8 paletas/espelhada saem da mesma malha (variações em cache).
    // ======================================================================

    // Células comuns (coluna 8 em diante da 1ª linha da metade de cima).
    private const int Light = 8, Concrete = 9, CarBody = 10, Metal = 11, Plaster = 12;
    private const int Plant = 13;
    private static readonly string[] SharedHex = { "BFF7E6", "D3CCC0", "F1DCC4", "B9BDC1", "CFC6B6", "6F9F57" };
    private const float LotBack = -1.0f; // a casa fica 1 m para trás: caminho e caixa de correio cabem no lote

    public sealed class Piece
    {
        public Mesh mesh;
        public Vector3 center;   // no espaço do "Visual"
        public bool roof;
        public bool prop;        // carro, caixa de correio, lixeira (inteiros)
    }

    private sealed class Variant
    {
        public Mesh lod0, lod1, lod2, ground;
        public Bounds body;
        public List<Piece> pieces;
    }

    private static readonly Dictionary<(GameObject, int, bool), Variant> variants = new Dictionary<(GameObject, int, bool), Variant>();

    private static Vector2 SharedUV(int cell) => new Vector2((cell + 0.5f) / 16f, (8 + 0.5f) / 16f);

    // Prédios retrô (Assets/Art/Buildings): mesma receita das casas, sem jardim na frente (calçada),
    // mais vida conforme a altura e desabamento andar por andar.
    public static Transform BuildBuilding(Transform parent, Vector2 position, float yaw, System.Random rng, Material template, GameObject[] models)
    {
        Transform building = BuildFromModel(parent, position, yaw, rng, template, models, 0f, "Predio");
        HouseBreakable h = building.GetComponent<HouseBreakable>();
        float height = BodyFor(h).max.y;
        building.GetComponent<DestructibleStructure>().SetMaxHealth(500f + Mathf.Round(height / 3f) * 420f);
        return building;
    }

    private static Transform BuildFromModel(Transform parent, Vector2 position, float yaw, System.Random rng, Material template, GameObject[] models,
        float lotBack = LotBack, string name = "Casa")
    {
        GameObject model = models[rng.Next(models.Length)];
        int palette = rng.Next(PaletteCount);
        bool mirror = rng.NextDouble() < 0.5;
        Variant v = VariantFor(model, palette, mirror);

        Transform house = new GameObject(name).transform;
        house.SetParent(parent, false);
        house.localPosition = new Vector3(position.x, 0f, position.y);
        house.localRotation = Quaternion.Euler(0f, yaw, 0f);

        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(house, false);
        visual.localPosition = new Vector3(0f, 0f, lotBack);

        Material shared = SharedMaterial(template);
        Mesh[] meshesByLod = { v.lod0, v.lod1, v.lod2 };
        Renderer[] lods = new Renderer[3];
        for (int lod = 0; lod < 3; lod++)
        {
            GameObject go = new GameObject("LOD" + lod);
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = meshesByLod[lod] != null ? meshesByLod[lod] : v.lod0;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = shared;
            renderer.shadowCastingMode = lod == 2 ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            lods[lod] = renderer;
        }

        LODGroup group = house.gameObject.AddComponent<LODGroup>();
        group.SetLODs(new[]
        {
            new LOD(0.11f, new[] { lods[0] }),
            new LOD(0.04f, new[] { lods[1] }),
            new LOD(0.008f, new[] { lods[2] }),
        });
        group.RecalculateBounds();

        // Colisão: o corpo da casa (sem o beiral do telhado).
        Bounds b = v.body;
        BoxCollider box = new GameObject("Colisor_0").AddComponent<BoxCollider>();
        box.transform.SetParent(house, false);
        box.transform.localPosition = new Vector3(b.center.x, b.max.y / 2f, b.center.z + lotBack);
        box.size = new Vector3(Mathf.Max(1f, b.size.x - 0.9f), b.max.y, Mathf.Max(1f, b.size.z - 0.9f));

        HouseBreakable breakable = house.gameObject.AddComponent<HouseBreakable>();
        breakable.source = model;
        breakable.palette = palette;
        breakable.mirror = mirror;
        breakable.visual = visual;

        DestructibleStructure structure = house.gameObject.AddComponent<DestructibleStructure>();
        structure.SetDebrisColors(palettes[palette][Wall], palettes[palette][Stripe]);
        return house;
    }

    private static Variant VariantFor(GameObject model, int palette, bool mirror)
    {
        if (variants.TryGetValue((model, palette, mirror), out Variant v) && v.lod0 != null)
        {
            return v;
        }

        Transform root = model.transform;
        v = new Variant();
        Accumulator lod0 = new Accumulator();
        Accumulator body = new Accumulator();
        Accumulator ground = new Accumulator();
        Transform lod1 = null, lod2 = null;
        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
        {
            string name = Clean(part.name);
            if (name == "LOD1") lod1 = part;
            if (name == "LOD2") lod2 = part;
            if (!name.StartsWith("LOD0_"))
            {
                continue;
            }

            lod0.Append(part, root, palette, mirror);
            if (name == "LOD0_Predio") body.Append(part, root, palette, mirror);
            if (name == "LOD0_Chao" || name == "LOD0_Base") ground.Append(part, root, palette, mirror);
        }

        v.lod0 = lod0.ToMesh("Casa_LOD0", false, out _);
        v.body = body.Bounds;
        v.ground = ground.Count > 0 ? ground.ToMesh("Casa_Chao", false, out _) : null;
        if (lod1 != null) { Accumulator a = new Accumulator(); a.Append(lod1, root, palette, mirror); v.lod1 = a.ToMesh("Casa_LOD1", false, out _); }
        if (lod2 != null) { Accumulator a = new Accumulator(); a.Append(lod2, root, palette, mirror); v.lod2 = a.ToMesh("Casa_LOD2", false, out _); }
        variants[(model, palette, mirror)] = v;
        return v;
    }

    // Cacos da versão pré-fraturada (feitos só quando a primeira casa daquela cor desmorona).
    public static List<Piece> PiecesFor(HouseBreakable h)
    {
        Variant v = VariantFor(h.source, h.palette, h.mirror);
        if (v.pieces != null)
        {
            return v.pieces;
        }

        v.pieces = new List<Piece>();
        Transform root = h.source.transform;
        float roofLine = v.body.max.y - 0.6f;
        foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
        {
            string name = Clean(part.name);
            bool prop = name == "LOD0_Carro" || name == "LOD0_Correio" || name == "LOD0_Lixeira";
            if (!name.StartsWith("Caco_") && !prop)
            {
                continue;
            }

            Accumulator a = new Accumulator();
            a.Append(part, root, h.palette, h.mirror);
            if (a.Count == 0)
            {
                continue;
            }

            Mesh mesh = a.ToMesh(part.name, true, out Vector3 center);
            v.pieces.Add(new Piece { mesh = mesh, center = center, prop = prop, roof = !prop && center.y > roofLine });
        }

        return v.pieces;
    }

    // O Blender põe ".001", ".002"... quando o nome já existe no arquivo (várias casas no mesmo .blend).
    private static string Clean(string name)
    {
        int dot = name.IndexOf('.');
        return dot > 0 ? name.Substring(0, dot) : name;
    }

    public static Mesh GroundFor(HouseBreakable h) => VariantFor(h.source, h.palette, h.mirror).ground;
    public static Bounds BodyFor(HouseBreakable h) => VariantFor(h.source, h.palette, h.mirror).body;
    public static Material HouseMaterial => material;

    // Junta partes do modelo numa malha só, com UV apontando para a célula da paleta de cada material.
    private sealed class Accumulator
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        public int Count => triangles.Count;

        public Bounds Bounds
        {
            get
            {
                if (vertices.Count == 0) return new Bounds(Vector3.zero, Vector3.one);
                Bounds b = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 p in vertices) b.Encapsulate(p);
                return b;
            }
        }

        public void Append(Transform part, Transform root, int palette, bool mirror)
        {
            MeshFilter filter = part.GetComponent<MeshFilter>();
            Renderer renderer = part.GetComponent<Renderer>();
            Mesh src = filter != null ? filter.sharedMesh : null;
            if (src == null || !src.isReadable)
            {
                if (src != null) Debug.LogWarning($"Casa: a malha {src.name} não está legível. Rode 'Preparar Casas (Missao 20)'.");
                return;
            }

            Matrix4x4 m = root.worldToLocalMatrix * part.localToWorldMatrix;
            if (mirror) m = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * m;
            Matrix4x4 nm = m.inverse.transpose;
            Vector3[] v = src.vertices;
            Vector3[] n = src.normals;
            Vector2[] uv = src.uv;
            Material[] mats = renderer != null ? renderer.sharedMaterials : new Material[0];
            Rect room = InteriorUV(palette % 4);
            for (int sub = 0; sub < src.subMeshCount; sub++)
            {
                string name = sub < mats.Length && mats[sub] != null ? mats[sub].name : "Parede";
                int dot = name.IndexOf('.');
                if (dot > 0) name = name.Substring(0, dot);
                name = name.Replace(" (Instance)", "");
                bool interior = name == "Interior";
                Vector2 cell = CellFor(name, palette);
                int[] t = src.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int idx = t[i + (mirror ? (3 - k) % 3 : k)];
                        vertices.Add(m.MultiplyPoint3x4(v[idx]));
                        normals.Add(n.Length > idx ? nm.MultiplyVector(n[idx]).normalized : Vector3.up);
                        uvs.Add(interior && uv.Length > idx
                            ? new Vector2(room.x + Mathf.Clamp01(uv[idx].x) * room.width, room.y + Mathf.Clamp01(uv[idx].y) * room.height)
                            : cell);
                        triangles.Add(vertices.Count - 1);
                    }
                }
            }
        }

        // recenter: o pivô vai para o centro (cacos); "center" devolve onde ele estava.
        public Mesh ToMesh(string name, bool recenter, out Vector3 center)
        {
            center = Vector3.zero;
            if (recenter && vertices.Count > 0)
            {
                center = Bounds.center;
                for (int i = 0; i < vertices.Count; i++) vertices[i] -= center;
            }

            Mesh mesh = new Mesh { name = name };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }
    }

    private static Vector2 CellFor(string name, int palette)
    {
        switch (name)
        {
            case "Parede": return CellUV(palette, Wall);
            case "Telhado": return CellUV(palette, Roof);
            case "Faixa":
            case "CarroFaixa": return CellUV(palette, Stripe);
            case "Porta": return CellUV(palette, Door);
            case "Moldura": return CellUV(palette, Trim);
            case "Rodape": return CellUV(palette, Plinth);
            case "Vidro": return CellUV(palette, Glass);
            case "Garagem": return CellUV(palette, Garage);
            case "Luz": return SharedUV(Light);
            case "Concreto": return SharedUV(Concrete);
            case "Carro": return SharedUV(CarBody);
            case "Metal": return SharedUV(Metal);
            case "Reboco": return SharedUV(Plaster);
            case "Planta": return SharedUV(Plant);
            default: return CellUV(palette, Wall);
        }
    }
}
