# Lojas retrô de "I'm Not an Alien" (cidades): Lanchonete, Mercadinho, Roupas e Chapéus, Eletrônicos, Oficina.
# Mesmo padrão das casas (CasaRetro_Casas.blend, textos casa_helpers/casa_lib):
#   LOD0_Predio (corpo, quebra em Caco_*), LOD0_Chao, LOD0_Base, LOD1, LOD2,
#   LOD0_Obj_* = objetos soltos (toldo, banquinhos, caixotes...) que caem inteiros quando a loja desaba.
# Materiais = células da paleta do jogo (Parede, Telhado, Faixa, Porta, Moldura, Rodape, Vidro, Garagem,
#   Luz, Concreto, Metal, Planta, Reboco). Frente da loja = -Y (vira +Z no Unity, para a rua).
# Uso no Blender (com CasaRetro_Casas.blend aberto):
#   exec(open(r"C:\WorkSpace\ImNotAnAlien\Art\Blender\loja_builder.py", encoding="utf-8").read())
#   build_shop("Lanchonete"); export_shop("Lanchonete")      # ou build_all()
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

exec(bpy.data.texts["casa_helpers"].as_string(), globals())

OUT = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Shops"
W, D, H = 10.4, 8.0, 4.3          # corpo da loja (cabe no lote de 12 x 9 da cidade)
YF = -D / 2                        # fachada

# Paleta de prévia (mesmas cores do RetroHouse.cs): parede, telhado, faixa, porta, moldura, rodapé, vidro, garagem
PALETTES = {
    "coral":    ["F2E3C9", "FAF2E2", "E2735A", "D9604A", "F8EFDF", "D6C3A2", "2F5862", "4A403B"],
    "salvia":   ["EFE2C6", "F8F1E1", "8FB79A", "6F9C7C", "F6EEDD", "D3C4A4", "2F5862", "45403A"],
    "azul":     ["F3E7D0", "FBF4E6", "5B8FB0", "3F6E95", "F8F0E2", "D7C7A9", "2B4F63", "3F3D40"],
    "mostarda": ["F1DDB8", "F9EFD9", "E6B04A", "C98A2E", "F7ECD6", "D2BC94", "33555C", "4A4036"],
    "turquesa": ["E8EFE6", "F5F8F2", "4DB3A6", "2F8F84", "F2F6EF", "C7CFC2", "2A5560", "3A4240"],
    "laranja":  ["E9E2D4", "F6F2EA", "7A8FA6", "E27D4E", "F4EFE6", "CFC6B5", "2E5260", "403E3C"],
}
CELLS = ["Parede", "Telhado", "Faixa", "Porta", "Moldura", "Rodape", "Vidro", "Garagem"]

def preview_palette(name):
    for cell, h in zip(CELLS, PALETTES[name]):
        m = bpy.data.materials[cell]
        c = hexc(h)
        m.diffuse_color = c
        if m.use_nodes:
            for n in m.node_tree.nodes:
                if n.type == 'BSDF_PRINCIPLED':
                    n.inputs["Base Color"].default_value = c

# ---------------- utilidades ----------------

def bake(ob):
    ob.data.transform(Matrix.Translation(ob.location)); ob.location = (0, 0, 0)
    return ob

def bake_rot(ob):
    ob.data.transform(Matrix.Translation(ob.location) @ ob.rotation_euler.to_matrix().to_4x4())
    ob.location = (0, 0, 0); ob.rotation_euler = (0, 0, 0)
    return ob

def get_col(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name); bpy.context.scene.collection.children.link(c)
    for o in list(c.objects):
        me = o.data; bpy.data.objects.remove(o, do_unlink=True)
        if me and me.users == 0: bpy.data.meshes.remove(me)
    return c

def bisect_z(ob, heights):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    for h in heights:
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, h), plane_no=(0, 0, 1))
    bm.to_mesh(ob.data); bm.free()

def join(objs, name):
    objs = [o for o in objs if o is not None]
    bpy.context.view_layer.update()  # posições novas (location) ainda não valem para o join sem isto
    if len(objs) > 1:
        with bpy.context.temp_override(active_object=objs[0], selected_editable_objects=objs, selected_objects=objs, object=objs[0]):
            bpy.ops.object.join()
    j = objs[0]; j.name = name; j.data.name = name
    return j

def box(name, size, loc, mat, r=0.0, axis='Z', seg=4, edge=0.0, top_only=False):
    o = bake(rbox(name, size, loc, r=r, axis=axis, seg=seg, edge=edge, eseg=2, mat=mat, top_only=top_only))
    if r > 0 or edge > 0: smooth(o, 40)
    return o

def pill(name, x, z, length=0.75, y=None, mat="Luz", horizontal=False):
    """Pílula de luz (cápsula achatada) presa na fachada."""
    y = YF - 0.06 if y is None else y
    if horizontal:
        return box(name, (length, 0.1, 0.2), (x, y, z), mat, r=0.098, axis='Y', seg=6)
    return box(name, (0.2, 0.1, length), (x, y, z), mat, r=0.098, axis='Y', seg=6)

def disc_front(name, r, x, z, mat, y=None, depth=0.08, verts=28):
    y = YF - 0.1 if y is None else y
    return bake_rot(cyl(name, r, depth, (x, y, z), mat, verts=verts, rot=(math.radians(90), 0, 0)))

# ---------------- casca comum ----------------

class Shop:
    def __init__(self, key):
        self.K = key
        self.col = None
        self.building, self.ground, self.base = [], [], []
        self.props = {}      # nome -> [objetos]  (vira LOD0_Obj_<nome>)
        self.keep_lod1 = []  # props que também aparecem de longe (toldo)

    def prop(self, name, *objs):
        self.props.setdefault(name, []).extend(objs)

def make_body(S, openings, stripe=(3.05, 3.35), r=1.0, h=H, facade=None):
    """Corpo com aberturas. openings: dicts kind=vitrine|door|capsule|garage, x, z(centro), w, h, r, depth, back."""
    K = S.K
    ob = bake(rbox(f"{K}_Corpo", (W, D, h), (0, 0, h / 2), r=r, axis='Z', seg=8, edge=0.08, eseg=2, mat="Parede"))
    cuts = [0.3, stripe[0], stripe[1]] + ([1.0] if facade else [])
    bisect_z(ob, cuts)
    for o in openings:
        dep = o.get("depth", 0.5)
        if o["kind"] == "door":
            c = bake(rbox("cut", (o["w"], 1.0, o["h"]), (o["x"], YF - 0.5 + dep, o["h"] / 2), r=o["w"] / 2 * 0.998, axis='Y', seg=10, mat=None, top_only=True))
        elif o["kind"] == "garage":
            c = bake(rbox("cut", (o["w"], dep + 1.0, o["h"] + 0.4), (o["x"], YF + dep - (dep + 1.0) / 2, (o["h"] + 0.4) / 2 - 0.4), r=o.get("r", 0.6), axis='Y', seg=8, mat=None, top_only=True))
        else:
            rr = o["h"] / 2 * 0.998 if o["kind"] == "capsule" else o.get("r", 0.45)
            c = bake(rbox("cut", (o["w"], dep + 1.0, o["h"]), (o["x"], YF + dep - (dep + 1.0) / 2, o["z"]), r=rr, axis='Y', seg=10, mat=None))
        cut(ob, c); remove(c)
    assign_faces(ob, "Rodape", lambda c, n: c.z < 0.3)
    assign_faces(ob, "Faixa", lambda c, n: stripe[0] < c.z < stripe[1] and abs(n.z) < 0.5)
    if facade:
        # fachada colorida da cintura para baixo (só na frente)
        assign_faces(ob, facade, lambda c, n: 0.3 < c.z < 1.0 and n.y < -0.7 and c.y < YF + 0.02)
    for o in openings:
        dep = o.get("depth", 0.5)
        if o["kind"] == "door":
            lo, hi = 0.0, o["h"]
        elif o["kind"] == "garage":
            lo, hi = 0.0, o["h"]
        else:
            lo, hi = o["z"] - o["h"] / 2, o["z"] + o["h"] / 2
        inside = lambda c, n, o=o, lo=lo, hi=hi, dep=dep: YF + 0.01 < c.y < YF + dep + 0.05 and abs(c.x - o["x"]) < o["w"] / 2 + 0.01 and lo - 0.01 < c.z < hi + 0.01
        assign_faces(ob, o.get("side", "Moldura"), inside)
        back = lambda c, n, o=o, dep=dep: abs(c.y - (YF + dep)) < 0.01 and n.y < -0.9 and abs(c.x - o["x"]) < o["w"] / 2
        assign_faces(ob, o.get("back", "Porta" if o["kind"] == "door" else "Garagem"), back)
        if o["kind"] == "garage":
            floor = lambda c, n, o=o, dep=dep: n.z > 0.9 and c.z < 0.05 and abs(c.x - o["x"]) < o["w"] / 2 and c.y < YF + dep
            assign_faces(ob, "Concreto", floor)
    smooth(ob, 35)
    S.building.append(ob)

    # molduras salientes das aberturas
    for i, o in enumerate(openings):
        if o["kind"] in ("vitrine", "capsule"):
            rr = o["h"] / 2 if o["kind"] == "capsule" else o.get("r", 0.45)
            t = o.get("frame", 0.16)
            ot = bake(rbox("tmp", (o["w"] + 2 * t, 0.16, o["h"] + 2 * t), (o["x"], YF - 0.06, o["z"]), r=rr + t, axis='Y', seg=10, edge=0.03, eseg=2, mat=o.get("frame_mat", "Moldura")))
            it = bake(rbox("tmpc", (o["w"], 1.0, o["h"]), (o["x"], YF, o["z"]), r=rr, axis='Y', seg=10, mat=None))
            cut(ot, it); remove(it); ot.name = f"{S.K}_Moldura{i}"; smooth(ot, 40); S.building.append(ot)
            if o.get("sill", True):
                S.building.append(box(f"{S.K}_Peitoril{i}", (o["w"] + 0.3, 0.3, 0.09), (o["x"], YF - 0.12, o["z"] - o["h"] / 2 - t - 0.02), "Moldura", r=0.04, axis='Y', seg=3, edge=0.03))
            # faixa de luz no alto da vitrine (dentro)
            if o.get("glow", True):
                dep = o.get("depth", 0.5)
                S.building.append(box(f"{S.K}_LuzVitrine{i}", (o["w"] - 2 * rr if o["kind"] == "capsule" else o["w"] - 0.6, 0.08, 0.06), (o["x"], YF + dep - 0.06, o["z"] + o["h"] / 2 - 0.12), "Luz", r=0.03, axis='X', seg=2))
        elif o["kind"] == "door":
            ot = bake(rbox("tmp", (o["w"] + 0.3, 0.12, o["h"] + 0.15), (o["x"], YF - 0.04, (o["h"] + 0.15) / 2), r=(o["w"] + 0.3) / 2 * 0.998, axis='Y', seg=10, mat=o.get("frame_mat", "Moldura"), top_only=True))
            it = bake(rbox("tmpc", (o["w"], 1.0, o["h"] + 0.02), (o["x"], YF, o["h"] / 2 - 0.01), r=o["w"] / 2 * 0.998, axis='Y', seg=10, mat=None, top_only=True))
            cut(ot, it); remove(it); ot.name = f"{S.K}_MolduraP{i}"; smooth(ot, 40); S.building.append(ot)
            if o.get("porthole", True):
                py = YF + o.get("depth", 0.16); pz = o["h"] - o["w"] / 2 - 0.1
                S.building.append(bake_rot(torus(f"{S.K}_EscAro{i}", 0.17, 0.035, (o["x"], py - 0.03, pz), "Moldura", rot=(math.radians(90), 0, 0))))
                S.building.append(disc_front(f"{S.K}_EscVidro{i}", 0.15, o["x"], pz, "Vidro", y=py - 0.015, depth=0.03, verts=20))
                S.building.append(sphere(f"{S.K}_Macaneta{i}", (0.05, 0.05, 0.05), (o["x"] + o["w"] / 2 - 0.17, py - 0.06, 1.05), "Metal", seg=10, rings=6))
            S.ground.append(box(f"{S.K}_Degrau{i}", (o["w"] + 0.6, 0.6, 0.14), (o["x"], YF - 0.3, 0.07), "Concreto", r=0.25, axis='Z', seg=5, edge=0.04))

    # telhado em laje que sobra
    S.building.append(box(f"{S.K}_Telhado", (W + 0.8, D + 0.8, 0.32), (0, 0, h + 0.16), "Telhado", r=r + 0.4, axis='Z', seg=8, edge=0.12))
    S.base.append(bake(rbox(f"{S.K}_Base", (W + 0.08, D + 0.08, 3.0), (0, 0, -1.5), r=r + 0.04, axis='Z', seg=8, mat="Rodape")))
    return ob

def frontao(S, w, hgt, mat="Parede", z0=None, y=None):
    """Platibanda arredondada que sobe acima do telhado (onde vai o letreiro)."""
    z0 = H + 0.1 if z0 is None else z0
    y = YF + 0.25 if y is None else y
    o = box(f"{S.K}_Frontao", (w, 0.5, hgt), (0, y, z0 + hgt / 2), mat, r=min(hgt * 0.48, 0.9), axis='Y', seg=10, edge=0.06, top_only=True)
    S.building.append(o)
    return z0 + hgt / 2

# ---------------- letreiro: ícone + escrita alienígena ----------------

def glyphs(S, x0, x1, z, rows=2, seed=1, size=0.13, mat="Garagem", y=None):
    """'Escrita alienígena': traços, pontos, arcos e ganchos em 1-2 linhas."""
    rnd = random.Random(seed)
    y = YF - 0.18 if y is None else y
    parts = []
    gap = size * 2.4
    for row in range(rows):
        zz = z + (rows - 1) * gap / 2 - row * gap
        x = x0
        k = 0
        while x < x1 - size:
            t = rnd.random()
            if t < 0.3:      # traço
                L = rnd.uniform(0.2, 0.42)
                if x + L > x1: break
                parts.append(box(f"{S.K}_Glifo", (L, 0.04, size * 0.55), (x + L / 2, y, zz), mat, r=size * 0.27, axis='Y', seg=3))
                x += L + size * 0.9
            elif t < 0.5:    # ponto (às vezes dois)
                parts.append(disc_front(f"{S.K}_Glifo", size * 0.42, x + size * 0.4, zz + (size * 0.5 if rnd.random() < 0.5 else 0), mat, y=y, depth=0.04, verts=12))
                if rnd.random() < 0.5:
                    parts.append(disc_front(f"{S.K}_Glifo", size * 0.42, x + size * 0.4, zz - size * 0.5, mat, y=y, depth=0.04, verts=12))
                x += size * 1.6
            elif t < 0.75:   # arco (anel aberto)
                ring = bake_rot(torus(f"{S.K}_Glifo", size * 0.7, size * 0.17, (x + size * 0.7, y, zz), mat, rot=(math.radians(90), 0, 0), seg=16, mseg=5))
                bm = bmesh.new(); bm.from_mesh(ring.data)
                cutside = rnd.choice([(0, 0, 1), (0, 0, -1), (1, 0, 0)])
                bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(x + size * 0.7, y, zz), plane_no=cutside, clear_outer=True)
                bm.to_mesh(ring.data); bm.free(); parts.append(ring)
                x += size * 1.9
            else:            # gancho: haste vertical + bolinha
                parts.append(box(f"{S.K}_Glifo", (size * 0.5, 0.04, size * 1.5), (x + size * 0.25, y, zz), mat, r=size * 0.24, axis='Y', seg=3))
                parts.append(disc_front(f"{S.K}_Glifo", size * 0.36, x + size * 0.85, zz + size * 0.55, mat, y=y, depth=0.04, verts=12))
                x += size * 1.7
            k += 1
    if parts:
        S.building.append(join(parts, f"{S.K}_Escrita"))

def sign(S, cx, cz, w, h, icon, plate="Moldura", disc="Faixa", seed=1, pills=True):
    """Placa arredondada: ícone num disco à esquerda + escrita alienígena à direita."""
    y = YF - 0.08
    S.building.append(box(f"{S.K}_Placa", (w, 0.14, h), (cx, y, cz), plate, r=h * 0.48, axis='Y', seg=10, edge=0.03))
    S.building.append(box(f"{S.K}_PlacaBorda", (w + 0.16, 0.1, h + 0.16), (cx, y + 0.04, cz), "Garagem", r=h * 0.48 + 0.08, axis='Y', seg=10))
    rdisc = h * 0.42
    ix = cx - w / 2 + h * 0.5
    S.building.append(disc_front(f"{S.K}_IconeDisco", rdisc, ix, cz, disc, y=y - 0.09, depth=0.06))
    icon(S, ix, cz, rdisc, y - 0.16)
    glyphs(S, ix + rdisc + 0.25, cx + w / 2 - h * 0.35, cz, rows=2, seed=seed, size=h * 0.13, y=y - 0.09)
    if pills:
        for sx in (-1, 1):
            S.building.append(pill(f"{S.K}_PilulaPlaca", cx + sx * (w / 2 + 0.3), cz, length=h * 0.8, y=y))

# ícones (2,5D, ~0,6 do diâmetro do disco). (x, z) = centro do disco, y = frente do disco.
def icon_burger(S, x, z, r, y):
    s = r * 0.85
    p = [sphere(f"{S.K}_IcPao", (s * 0.75, 0.07, s * 0.36), (x, y, z + s * 0.2), "Moldura", seg=16, rings=8)]
    bm = bmesh.new(); bm.from_mesh(p[0].data)  # meia-esfera (pão de cima)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(0, 0, -1), clear_outer=True)
    bm.to_mesh(p[0].data); bm.free()
    p.append(box(f"{S.K}_IcAlface", (s * 1.5, 0.08, s * 0.14), (x, y + 0.01, z + s * 0.12), "Planta", r=s * 0.07, axis='Y', seg=2))
    p.append(box(f"{S.K}_IcCarne", (s * 1.45, 0.1, s * 0.24), (x, y + 0.005, z - s * 0.08), "Garagem", r=s * 0.11, axis='Y', seg=3))
    p.append(box(f"{S.K}_IcPaoB", (s * 1.4, 0.08, s * 0.22), (x, y + 0.01, z - s * 0.36), "Moldura", r=s * 0.1, axis='Y', seg=3))
    S.building.append(join(p, f"{S.K}_Icone"))

def icon_fruit(S, x, z, r, y):
    s = r * 0.62
    p = [sphere(f"{S.K}_IcFruta", (s * 0.95, 0.08, s * 0.85), (x - s * 0.05, y, z - s * 0.12), "Moldura", seg=18, rings=10)]
    p.append(box(f"{S.K}_IcCabo", (s * 0.12, 0.06, s * 0.45), (x - s * 0.05, y - 0.02, z + s * 0.8), "Garagem", r=s * 0.05, axis='Y', seg=2))
    p.append(sphere(f"{S.K}_IcFolha", (s * 0.42, 0.06, s * 0.18), (x + s * 0.35, y - 0.02, z + s * 0.85), "Planta", seg=12, rings=6))
    p[-1].rotation_euler = (0, math.radians(-25), 0); bake_rot(p[-1])
    S.building.append(join(p, f"{S.K}_Icone"))

def icon_hat(S, x, z, r, y):
    s = r * 0.7
    p = [box(f"{S.K}_IcCopa", (s * 1.0, 0.08, s * 0.85), (x, y, z + s * 0.2), "Moldura", r=s * 0.3, axis='Y', seg=5, top_only=True)]
    p.append(box(f"{S.K}_IcAba", (s * 1.85, 0.09, s * 0.2), (x, y - 0.005, z - s * 0.3), "Moldura", r=s * 0.1, axis='Y', seg=3))
    p.append(box(f"{S.K}_IcFita", (s * 1.02, 0.1, s * 0.16), (x, y - 0.01, z - s * 0.08), "Garagem", r=0.0))
    S.building.append(join(p, f"{S.K}_Icone"))

def icon_tv(S, x, z, r, y):
    s = r * 0.7
    p = [box(f"{S.K}_IcTV", (s * 1.5, 0.08, s * 1.1), (x, y, z - s * 0.15), "Moldura", r=s * 0.3, axis='Y', seg=5)]
    p.append(box(f"{S.K}_IcTela", (s * 1.05, 0.08, s * 0.75), (x - s * 0.12, y - 0.02, z - s * 0.15), "Vidro", r=s * 0.22, axis='Y', seg=5))
    p.append(disc_front(f"{S.K}_IcBotao", s * 0.09, x + s * 0.55, z - s * 0.05, "Luz", y=y - 0.03, depth=0.04, verts=10))
    for sx in (-1, 1):
        a = cyl(f"{S.K}_IcAntena", s * 0.04, s * 0.6, (x + sx * s * 0.22, y, z + s * 0.62), "Garagem", verts=6, rot=(0, sx * math.radians(30), 0))
        p.append(bake_rot(a))
    S.building.append(join(p, f"{S.K}_Icone"))

def icon_saucer(S, x, z, r, y):
    s = r * 0.75
    p = [sphere(f"{S.K}_IcDisco", (s * 1.0, 0.07, s * 0.26), (x, y, z - s * 0.1), "Moldura", seg=20, rings=8)]
    dome = sphere(f"{S.K}_IcCupula", (s * 0.42, 0.07, s * 0.42), (x, y - 0.01, z + s * 0.05), "Vidro", seg=16, rings=8)
    bm = bmesh.new(); bm.from_mesh(dome.data)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(0, 0, -1), clear_outer=True)
    bm.to_mesh(dome.data); bm.free(); p.append(dome)
    # chave inglesa cruzando por baixo
    wr = box(f"{S.K}_IcChave", (s * 1.6, 0.05, s * 0.14), (x, y + 0.02, z - s * 0.55), "Garagem", r=s * 0.06, axis='Y', seg=2)
    p.append(wr)
    for sx in (-1, 1):
        p.append(disc_front(f"{S.K}_IcChaveCab", s * 0.17, x + sx * s * 0.8, z - s * 0.55, "Garagem", y=y + 0.02, depth=0.05, verts=12))
    p.append(disc_front(f"{S.K}_IcLuz", s * 0.07, x - s * 0.55, z - s * 0.1, "Luz", y=y - 0.04, depth=0.03, verts=8))
    p.append(disc_front(f"{S.K}_IcLuz", s * 0.07, x + s * 0.55, z - s * 0.1, "Luz", y=y - 0.04, depth=0.03, verts=8))
    S.building.append(join(p, f"{S.K}_Icone"))

# ---------------- peças reaproveitadas ----------------

def roof_saucer(S, x, y, scale=1.0, z=None):
    """Torrezinha com disco voador no telhado (como na imagem)."""
    z = H + 0.32 if z is None else z
    s = scale
    p = [cyl(f"{S.K}_TorrePe", 0.32 * s, 0.25 * s, (x, y, z + 0.12 * s), "Moldura", verts=20, edge=0.04),
         cyl(f"{S.K}_TorreHaste", 0.08 * s, 1.3 * s, (x, y, z + 0.85 * s), "Metal", verts=12),
         sphere(f"{S.K}_TorreDisco", (0.95 * s, 0.95 * s, 0.22 * s), (x, y, z + 1.6 * s), "Moldura", seg=24, rings=10),
         torus(f"{S.K}_TorreAnel", 0.9 * s, 0.06 * s, (x, y, z + 1.6 * s), "Faixa", seg=28, mseg=6),
         sphere(f"{S.K}_TorreCupula", (0.42 * s, 0.42 * s, 0.32 * s), (x, y, z + 1.75 * s), "Luz", seg=18, rings=8)]
    for o in p: smooth(o, 40)
    S.building.append(join(p, f"{S.K}_Torre"))

def awning_curved(S, x, z, w, out=1.3, drop=0.75, stripes=7, mat_a="Faixa", mat_b="Moldura", name="Toldo"):
    """Toldo curvo listrado (quarto de cilindro), preso na fachada em z, avançando 'out' para a rua."""
    parts = []
    sw = w / stripes
    for i in range(stripes):
        bm = bmesh.new()
        segs = 8
        x0 = x - w / 2 + i * sw; x1 = x0 + sw
        rows = []
        for k in range(segs + 1):
            a = math.radians(90) * k / segs
            yy = YF - math.sin(a) * out
            zz = z - (1 - math.cos(a)) * drop
            rows.append((yy, zz))
        th = 0.05
        vs = []
        for (yy, zz) in rows:
            vs.append([bm.verts.new((x0, yy, zz)), bm.verts.new((x1, yy, zz)), bm.verts.new((x1, yy, zz - th)), bm.verts.new((x0, yy, zz - th))])
        for k in range(segs):
            a, b = vs[k], vs[k + 1]
            bm.faces.new((a[0], b[0], b[1], a[1])); bm.faces.new((a[3], a[2], b[2], b[3]))
            bm.faces.new((a[0], a[3], b[3], b[0])); bm.faces.new((a[1], b[1], b[2], a[2]))
        bm.faces.new(vs[0][:]); bm.faces.new(vs[-1][::-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        o = new_obj(f"{S.K}_{name}{i}", bm, mat_a if i % 2 == 0 else mat_b)
        parts.append(o)
    # barra na ponta (bainha arredondada)
    yy = YF - out; zz = z - drop
    hem = bake_rot(cyl(f"{S.K}_{name}Bainha", 0.06, w + 0.04, (x, yy, zz - 0.03), mat_a, verts=10, rot=(0, math.radians(90), 0)))
    parts.append(hem)
    j = join(parts, f"{S.K}_{name}")
    S.prop(name, j); S.keep_lod1.append(j)
    return j

def stool(S, x, y, i, seat="Faixa", back=True):
    p = [cyl(f"{S.K}_BancoPe{i}", 0.2, 0.05, (x, y, 0.025), "Metal", verts=16),
         cyl(f"{S.K}_BancoHaste{i}", 0.04, 0.68, (x, y, 0.37), "Metal", verts=8),
         cyl(f"{S.K}_BancoAssento{i}", 0.24, 0.12, (x, y, 0.75), seat, verts=20, edge=0.04)]
    if back:
        # encosto alto e oval: espaço para o crânio alongado dos aliens
        p.append(cyl(f"{S.K}_BancoEncHaste{i}", 0.025, 0.55, (x, y + 0.2, 1.05), "Metal", verts=6))
        e = sphere(f"{S.K}_BancoEncosto{i}", (0.2, 0.05, 0.34), (x, y + 0.22, 1.42), seat, seg=14, rings=8)
        p.append(e)
    for o in p: smooth(o, 40)
    S.prop(f"Banco{i}", join(p, f"{S.K}_Banco{i}"))

# ---------------- as 5 lojas ----------------

def lanchonete(S):
    # vitrine grande à esquerda, porta à direita, fachada coral embaixo
    ops = [dict(kind="vitrine", x=-1.2, z=1.95, w=5.0, h=1.75, r=0.55, depth=0.5, sill=False),
           dict(kind="door", x=3.3, w=1.25, h=2.45, depth=0.16)]
    make_body(S, ops, facade="Faixa")
    frontao(S, 8.6, 1.35)
    sign(S, -0.3, H + 0.78, 6.0, 0.95, icon_burger, seed=3)
    awning_curved(S, -1.2, 3.05, 5.6, out=1.25, drop=0.55, stripes=9)
    # balcão do lado de fora, junto da vitrine
    S.building.append(box(f"{S.K}_Balcao", (5.0, 0.42, 0.09), (-1.2, YF - 0.2, 1.02), "Moldura", r=0.12, axis='Z', seg=4, edge=0.03))
    S.building.append(box(f"{S.K}_BalcaoFrente", (4.8, 0.1, 0.62), (-1.2, YF - 0.05, 0.66), "Faixa", r=0.05, axis='Y', seg=2))
    for i, x in enumerate((-3.0, -1.2, 0.6)):
        stool(S, x, YF - 0.75, i)
    # dentro da vitrine: copos e uma placa de cardápio (silhuetas simples)
    for i, x in enumerate((-3.3, -2.9, 0.5, 0.9)):
        S.building.append(cyl(f"{S.K}_Copo{i}", 0.09, 0.26, (x, YF + 0.32, 1.21), "Faixa" if i % 2 else "Moldura", verts=12, radius2=0.12))
    S.building.append(box(f"{S.K}_Cardapio", (1.6, 0.05, 0.75), (-1.2, YF + 0.45, 2.1), "Garagem", r=0.12, axis='Y', seg=3))
    glyphs(S, -1.85, -0.6, 2.1, rows=3, seed=31, size=0.07, mat="Moldura", y=YF + 0.42)
    S.building.append(pill(f"{S.K}_PilulaPorta", 3.3, 2.95, length=0.9, horizontal=True))
    roof_saucer(S, 3.4, 1.8, 0.9)

def mercadinho(S):
    ops = [dict(kind="vitrine", x=-2.75, z=1.75, w=2.3, h=1.9, r=0.5, depth=0.45, frame_mat="Faixa", frame=0.2),
           dict(kind="vitrine", x=2.75, z=1.75, w=2.3, h=1.9, r=0.5, depth=0.45, frame_mat="Faixa", frame=0.2),
           dict(kind="garage", x=0.0, w=2.3, h=2.6, r=0.5, depth=0.3, back="Vidro")]
    make_body(S, ops)
    # porta automática: duas folhas de vidro com moldura menta e linha no meio
    S.building.append(box(f"{S.K}_PortaAuto", (2.7, 0.18, 2.95), (0, YF - 0.06, 1.47), "Faixa", r=0.5, axis='Y', seg=8, top_only=True))
    cutter = bake(rbox("cut", (2.3, 1.0, 2.62), (0, YF, 1.3), r=0.5, axis='Y', seg=8, mat=None, top_only=True))
    cut(S.building[-1], cutter); remove(cutter)
    S.building.append(box(f"{S.K}_PortaMeio", (0.06, 0.06, 2.55), (0, YF + 0.26, 1.28), "Moldura"))
    for sx in (-1, 1):
        S.building.append(box(f"{S.K}_PortaPuxador", (0.05, 0.05, 0.5), (sx * 0.18, YF + 0.22, 1.15), "Metal", r=0.02, axis='Y', seg=2))
    S.building.append(box(f"{S.K}_SensorPorta", (0.5, 0.12, 0.12), (0, YF - 0.15, 3.05), "Luz", r=0.05, axis='Y', seg=3))
    S.ground.append(box(f"{S.K}_Tapete", (2.4, 1.0, 0.04), (0, YF - 0.55, 0.02), "Garagem", r=0.2, axis='Z', seg=3))
    frontao(S, 9.0, 1.4)
    sign(S, 0.0, H + 0.8, 6.4, 1.0, icon_fruit, seed=7)
    # caixotes de frutas na entrada (soltos)
    rnd = random.Random(5)
    fruit_mats = ["Faixa", "Porta", "Planta", "Luz", "Moldura"]
    for i, (x, tier) in enumerate(((-4.0, 0), (-2.9, 0), (-3.45, 1), (2.9, 0), (4.0, 0))):
        z = 0.0 if tier == 0 else 0.5
        y = YF - 0.65 - (0.0 if tier == 0 else -0.1)
        crate = box(f"{S.K}_Caixote{i}", (0.95, 0.65, 0.48), (x, y, z + 0.24), "Rodape", r=0.04, axis='Z', seg=2, edge=0.02)
        slat = box(f"{S.K}_CaixoteRipa{i}", (0.97, 0.67, 0.07), (x, y, z + 0.3), "Garagem")
        fr = []
        fm = fruit_mats[i % len(fruit_mats)]
        for k in range(6):
            fx = x - 0.3 + (k % 3) * 0.3 + rnd.uniform(-0.04, 0.04)
            fy = y - 0.14 + (k // 3) * 0.28
            if fm == "Planta":   # fruta comprida esquisita
                f = sphere(f"{S.K}_Fruta", (0.09, 0.09, 0.2), (fx, fy, z + 0.6), fm, seg=10, rings=6)
            elif fm == "Luz":    # fruta que brilha
                f = sphere(f"{S.K}_Fruta", (0.1, 0.1, 0.1), (fx, fy, z + 0.55), fm, seg=10, rings=6)
            else:
                f = sphere(f"{S.K}_Fruta", (0.13, 0.13, 0.11), (fx, fy, z + 0.55), fm, seg=10, rings=6)
            fr.append(f)
        S.prop(f"Caixote{i}", join([crate, slat] + fr, f"{S.K}_Caixote{i}"))
    # cartazes na vitrine: embalagens com alien satisfeito (rosto simples)
    for i, x in enumerate((-2.75, 2.75)):
        for k, dx in enumerate((-0.72, 0.0, 0.72)):
            px = x + dx
            S.building.append(box(f"{S.K}_Pacote{i}{k}", (0.48, 0.25, 0.7), (px, YF + 0.3, 1.18), "Faixa" if k != 1 else "Porta", r=0.08, axis='Y', seg=3))
            S.building.append(sphere(f"{S.K}_PacoteCara{i}{k}", (0.13, 0.03, 0.18), (px, YF + 0.165, 1.28), "Moldura", seg=12, rings=6))
            S.building.append(box(f"{S.K}_PacoteSorriso{i}{k}", (0.1, 0.03, 0.025), (px, YF + 0.15, 1.2), "Garagem"))
    for sx in (-1, 1):
        S.building.append(pill(f"{S.K}_PilulaVit", sx * 2.75, 2.98, length=1.2, horizontal=True))
    roof_saucer(S, -3.2, 1.6, 0.8)

def roupas(S):
    ops = [dict(kind="vitrine", x=-0.9, z=1.9, w=5.6, h=2.3, r=0.7, depth=0.95, frame_mat="Faixa", frame=0.28, back="Porta"),
           dict(kind="door", x=3.4, w=1.15, h=2.45, depth=0.16)]
    make_body(S, ops, stripe=(3.45, 3.7))
    frontao(S, 8.4, 1.3)
    sign(S, -0.4, H + 0.75, 5.8, 0.92, icon_hat, seed=11)
    # manequins de cabeça grande com chapéus enormes
    hats = [("Faixa", 0.75), ("Porta", 0.85), ("Telhado", 0.7)]
    for i, x in enumerate((-2.8, -0.9, 1.0)):
        y = YF + 0.5
        p = [cyl(f"{S.K}_ManBase{i}", 0.22, 0.06, (x, y, 0.78), "Metal", verts=14),
             cyl(f"{S.K}_ManHaste{i}", 0.03, 0.3, (x, y, 0.95), "Metal", verts=6),
             sphere(f"{S.K}_ManCorpo{i}", (0.24, 0.16, 0.42), (x, y, 1.42), "Moldura", seg=14, rings=8),
             sphere(f"{S.K}_ManCabeca{i}", (0.22, 0.24, 0.36), (x, y, 2.05), "Moldura", seg=14, rings=8)]
        m, rad = hats[i]
        aba = cyl(f"{S.K}_ChapAba{i}", rad, 0.05, (x, y - 0.05, 2.32), m, verts=24, edge=0.02)
        aba.data.transform(Matrix.Diagonal((1.0, 0.72, 1.0, 1.0))); p.append(aba)
        p.append(cyl(f"{S.K}_ChapCopa{i}", 0.3, 0.32, (x, y - 0.02, 2.5), m, verts=18, edge=0.08, radius2=0.25))
        p.append(cyl(f"{S.K}_ChapFita{i}", 0.305, 0.07, (x, y - 0.02, 2.39), "Garagem", verts=18))
        if i == 1:  # peninha
            pen = sphere(f"{S.K}_ChapPena{i}", (0.06, 0.05, 0.28), (x + 0.28, y - 0.02, 2.62), "Luz", seg=8, rings=6)
            pen.rotation_euler = (0, math.radians(-30), 0); bake_rot(pen); p.append(pen)
        for o in p: smooth(o, 40)
        S.building.append(join(p, f"{S.K}_Manequim{i}"))
    # cabide de chapéus ao lado da porta (solto)
    p = [cyl(f"{S.K}_CabidePe", 0.25, 0.05, (4.6, YF - 0.6, 0.025), "Metal", verts=14),
         cyl(f"{S.K}_CabideHaste", 0.035, 1.7, (4.6, YF - 0.6, 0.88), "Metal", verts=8),
         cyl(f"{S.K}_CabideChapA", 0.55, 0.04, (4.6, YF - 0.6, 1.75), "Faixa", verts=20),
         cyl(f"{S.K}_CabideChapC", 0.2, 0.22, (4.6, YF - 0.6, 1.88), "Faixa", verts=14, radius2=0.17)]
    S.prop("Cabide", join(p, f"{S.K}_Cabide"))
    awning_curved(S, -0.9, 3.4, 6.3, out=0.9, drop=0.4, stripes=1, mat_a="Faixa", name="Marquise")
    S.building.append(pill(f"{S.K}_PilulaPorta", 3.4, 2.95, length=0.9, horizontal=True))
    roof_saucer(S, 3.3, 1.6, 1.0)

def eletronicos(S):
    ops = [dict(kind="capsule", x=-2.6, z=1.75, w=2.2, h=1.5, depth=0.7, back="Garagem"),
           dict(kind="capsule", x=0.0, z=1.75, w=2.2, h=1.5, depth=0.7, back="Garagem"),
           dict(kind="door", x=3.1, w=1.2, h=2.45, depth=0.16)]
    make_body(S, ops, facade="Faixa")
    frontao(S, 8.8, 1.35)
    sign(S, -0.2, H + 0.78, 6.0, 0.95, icon_tv, plate="Moldura", disc="Faixa", seed=17)
    y = YF + 0.42
    # pedestais
    for i, x in enumerate((-3.05, -2.0, -0.55, 0.55)):
        S.building.append(cyl(f"{S.K}_Pedestal{i}", 0.26, 0.24, (x, y, 1.12), "Moldura", verts=18, edge=0.03))
    # TV arredondada
    S.building.append(box(f"{S.K}_TV", (0.75, 0.45, 0.58), (-3.05, y, 1.55), "Telhado", r=0.2, axis='Y', seg=6, edge=0.03))
    S.building.append(box(f"{S.K}_TVTela", (0.52, 0.06, 0.4), (-3.12, y - 0.22, 1.55), "Vidro", r=0.15, axis='Y', seg=6))
    S.building.append(disc_front(f"{S.K}_TVBotao", 0.04, -2.8, 1.6, "Luz", y=y - 0.24, depth=0.03, verts=8))
    for sx in (-1, 1):
        a = cyl(f"{S.K}_TVAntena", 0.012, 0.45, (-3.05 + sx * 0.12, y, 2.0), "Metal", verts=6, rot=(0, sx * math.radians(25), 0)); bake_rot(a)
        S.building.append(a)
    # rádio
    S.building.append(box(f"{S.K}_Radio", (0.7, 0.35, 0.4), (-2.0, y, 1.45), "Faixa", r=0.12, axis='Y', seg=5, edge=0.02))
    S.building.append(disc_front(f"{S.K}_RadioAuto", 0.12, -2.15, 1.45, "Garagem", y=y - 0.18, depth=0.03, verts=14))
    S.building.append(disc_front(f"{S.K}_RadioDial", 0.06, -1.82, 1.48, "Luz", y=y - 0.18, depth=0.04, verts=10))
    # globo de navegação
    S.building.append(cyl(f"{S.K}_GloboPe", 0.03, 0.2, (-0.55, y, 1.34), "Metal", verts=6))
    S.building.append(sphere(f"{S.K}_Globo", (0.27, 0.27, 0.27), (-0.55, y, 1.65), "Vidro", seg=18, rings=10))
    g = torus(f"{S.K}_GloboAnel", 0.32, 0.018, (-0.55, y, 1.65), "Metal", rot=(math.radians(70), math.radians(20), 0), seg=24, mseg=4); bake_rot(g)
    S.building.append(g)
    for k in range(3):
        S.building.append(sphere(f"{S.K}_GloboPonto{k}", (0.035, 0.035, 0.035), (-0.55 + (k - 1) * 0.12, y - 0.25, 1.62 + (k % 2) * 0.1), "Luz", seg=6, rings=4))
    # robozinho-aspirador / comunicador
    S.building.append(sphere(f"{S.K}_Comunicador", (0.3, 0.22, 0.2), (0.55, y, 1.42), "Telhado", seg=16, rings=8))
    S.building.append(box(f"{S.K}_ComTela", (0.3, 0.04, 0.1), (0.55, y - 0.2, 1.45), "Luz", r=0.04, axis='Y', seg=2))
    # antenas grandes no telhado
    S.building.append(cyl(f"{S.K}_AntenaHaste", 0.04, 1.8, (-3.6, 2.2, H + 1.2), "Metal", verts=8))
    S.building.append(sphere(f"{S.K}_AntenaBola", (0.17, 0.17, 0.17), (-3.6, 2.2, H + 2.15), "Luz", seg=12, rings=6))
    for k, zz in enumerate((H + 1.0, H + 1.5)):
        S.building.append(bake_rot(torus(f"{S.K}_AntenaAnel{k}", 0.35 - k * 0.1, 0.025, (-3.6, 2.2, zz), "Metal", seg=20, mseg=4)))
    for i, x in enumerate((-2.6, 0.0)):
        S.building.append(pill(f"{S.K}_PilulaVit{i}", x, 2.85, length=1.0, horizontal=True))
    S.building.append(pill(f"{S.K}_PilulaPorta", 3.1, 2.95, length=0.9, horizontal=True))
    awning_curved(S, 3.1, 3.05, 1.9, out=0.7, drop=0.35, stripes=3, name="ToldoPorta")
    roof_saucer(S, 2.6, 1.4, 0.85)

def oficina(S):
    ops = [dict(kind="garage", x=-1.2, w=5.6, h=3.0, r=0.8, depth=2.2, back="Garagem", side="Concreto"),
           dict(kind="capsule", x=3.2, z=1.85, w=1.6, h=1.1, depth=0.4)]
    make_body(S, ops, stripe=(3.55, 3.8), r=0.8)
    frontao(S, 9.2, 1.3, mat="Porta")
    sign(S, -0.2, H + 0.76, 6.2, 0.95, icon_saucer, plate="Moldura", disc="Porta", seed=23)
    # cobertura baixa na frente da garagem (solta; apoiada em 2 colunas)
    p = [box(f"{S.K}_Cobertura", (6.6, 2.0, 0.18), (-1.2, YF - 0.95, 3.25), "Telhado", r=0.6, axis='Z', seg=6, edge=0.05),
         box(f"{S.K}_CoberturaFaixa", (6.62, 2.02, 0.07), (-1.2, YF - 0.95, 3.17), "Porta", r=0.6, axis='Z', seg=6)]
    for sx in (-1, 1):
        p.append(cyl(f"{S.K}_Coluna", 0.09, 3.2, (-1.2 + sx * 3.0, YF - 1.75, 1.6), "Metal", verts=10))
    for k in range(4):
        p.append(box(f"{S.K}_CoberturaLuz", (0.6, 0.1, 0.05), (-3.3 + k * 1.4, YF - 1.2, 3.13), "Luz", r=0.04, axis='Y', seg=2))
    S.prop("Cobertura", join(p, f"{S.K}_Cobertura")); S.keep_lod1.append(S.props["Cobertura"][0])
    # chão de concreto com faixa amarela (sinal de entrada)
    S.ground.append(box(f"{S.K}_Patio", (6.4, 2.1, 0.05), (-1.2, YF - 1.0, 0.025), "Concreto", r=0.3, axis='Z', seg=4))
    for k in range(5):
        S.ground.append(box(f"{S.K}_PatioFaixa{k}", (0.45, 0.25, 0.06), (-3.6 + k * 1.2, YF - 1.8, 0.03), "Porta"))
    S.ground.append(box(f"{S.K}_PisoGaragem", (5.6, 2.25, 0.04), (-1.2, YF + 1.1, 0.02), "Concreto"))
    # peças penduradas na parede do fundo: discos de levitação
    for k, (x, z, rr) in enumerate(((-3.0, 2.2, 0.42), (-1.6, 2.35, 0.32), (-0.3, 2.15, 0.5), (0.9, 2.3, 0.3))):
        yb = YF + 2.2 - 0.08
        S.building.append(bake_rot(torus(f"{S.K}_DiscoParede{k}", rr, 0.07, (x, yb, z), "Metal", rot=(math.radians(90), 0, 0), seg=22, mseg=6)))
        S.building.append(disc_front(f"{S.K}_DiscoCentro{k}", rr * 0.45, x, z, "Luz", y=yb + 0.02, depth=0.04, verts=14))
        S.building.append(box(f"{S.K}_Gancho{k}", (0.05, 0.1, 0.25), (x, yb + 0.03, z + rr + 0.1), "Metal"))
    # bancada com ferramentas no fundo
    S.building.append(box(f"{S.K}_Bancada", (3.4, 0.7, 0.12), (-1.2, YF + 1.8, 0.95), "Rodape", r=0.05, axis='Z', seg=2))
    for sx in (-1, 1):
        S.building.append(box(f"{S.K}_BancadaPe", (0.1, 0.6, 0.9), (-1.2 + sx * 1.55, YF + 1.8, 0.45), "Metal"))
    # disco de levitação desmontado no chão (solto) + cúpulas de vidro de vários tamanhos (soltas)
    p = [cyl(f"{S.K}_DiscoChao", 1.1, 0.22, (-1.4, YF + 0.3, 0.32), "Moldura", verts=28, edge=0.08),
         torus(f"{S.K}_DiscoChaoAnel", 1.1, 0.06, (-1.4, YF + 0.3, 0.32), "Porta", seg=28, mseg=6),
         cyl(f"{S.K}_DiscoChaoNucleo", 0.35, 0.12, (-1.4, YF + 0.3, 0.48), "Luz", verts=18)]
    for k in range(3):
        a = math.radians(120 * k + 30)
        p.append(cyl(f"{S.K}_DiscoCalco{k}", 0.08, 0.22, (-1.4 + math.cos(a) * 0.8, YF + 0.3 + math.sin(a) * 0.8, 0.11), "Garagem", verts=8))
    for o in p: smooth(o, 40)
    S.prop("DiscoDesmontado", join(p, f"{S.K}_DiscoDesmontado"))
    for k, (x, rr) in enumerate(((3.0, 0.55), (4.1, 0.38), (3.55, 0.28))):
        y = YF - 0.75 - (0.45 if k == 2 else 0)
        base = cyl(f"{S.K}_CupulaBase{k}", rr + 0.05, 0.08, (x, y, 0.04), "Metal", verts=20)
        d = sphere(f"{S.K}_Cupula{k}", (rr, rr, rr * 0.9), (x, y, 0.08), "Vidro", seg=18, rings=10)
        bm = bmesh.new(); bm.from_mesh(d.data)
        bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(0, 0, -1), clear_outer=True)
        bm.to_mesh(d.data); bm.free(); smooth(d, 40)
        S.prop(f"Cupula{k}", join([base, d], f"{S.K}_Cupula{k}"))
    # pneu-disco encostado na parede lateral (solto)
    t = torus(f"{S.K}_DiscoEncostado", 0.45, 0.13, (5.4, YF + 1.3, 0.58), "Garagem", rot=(math.radians(90), 0, math.radians(90)), seg=20, mseg=8)
    bake_rot(t); smooth(t, 40)
    S.prop("DiscoEncostado", t)
    S.building.append(pill(f"{S.K}_PilulaVit", 3.2, 2.75, length=0.8, horizontal=True))
    roof_saucer(S, 3.2, 1.5, 1.15)

SHOPS = {"Lanchonete": (lanchonete, "coral"), "Mercadinho": (mercadinho, "salvia"), "Roupas": (roupas, "azul"),
         "Eletronicos": (eletronicos, "mostarda"), "Oficina": (oficina, "laranja")}

LAST = {}

def build_shop(key, x_offset=0.0):
    global col
    S = Shop(key)
    col = get_col("Loja_" + key)
    S.col = col
    for lc in bpy.context.view_layer.layer_collection.children:
        if lc.collection == col:
            lc.exclude = False  # coleção escondida = peças fora da cena e o join erra as posições
    SHOPS[key][0](S)
    # chão: as peças de chão descem até -1,2 (no morro não flutuam)
    for g in S.ground:
        for v in g.data.vertices:
            if v.co.z < 0.005: v.co.z = -1.2
    if x_offset:
        for o in col.objects:
            o.location.x += x_offset
    LAST[key] = S
    return S

def export_shop(key):
    S = LAST[key]
    exp = get_col("Export_Loja_" + key)
    for lc in bpy.context.view_layer.layer_collection.children:
        if lc.collection in (exp, S.col):
            lc.exclude = False
    def dup_join(objs, name):
        copies = []
        for o in objs:
            c = o.copy(); c.data = o.data.copy(); exp.objects.link(c); copies.append(c)
        return join(copies, name)
    def decimated(objs, name, ratio):
        j = dup_join(objs, name)
        m = j.modifiers.new("dec", 'DECIMATE'); m.ratio = ratio
        with bpy.context.temp_override(object=j, active_object=j, selected_objects=[j]):
            bpy.ops.object.modifier_apply(modifier=m.name)
        return j
    predio = dup_join(S.building, "LOD0_Predio")
    chao = dup_join(S.ground, "LOD0_Chao")
    base = dup_join(S.base, "LOD0_Base")
    props = []
    for name, objs in S.props.items():
        props.append(dup_join(objs, "LOD0_Obj_" + name))
    keep = [p for p in props if any(p.name == "LOD0_Obj_" + n for n, objs in S.props.items() if any(o in S.keep_lod1 for o in objs))]
    decimated([predio, chao, base] + keep, "LOD1", 0.3)
    decimated([predio, base] + keep, "LOD2", 0.07)
    # versão pré-fraturada (Voronoi), igual às casas
    basem = predio.data
    names = [m.name if m else "" for m in basem.materials]
    if "Reboco" not in names:
        basem.materials.append(bpy.data.materials["Reboco"]); names.append("Reboco")
    reb = names.index("Reboco")
    xs = [v.co.x for v in basem.vertices]; ys = [v.co.y for v in basem.vertices]; top = max(v.co.z for v in basem.vertices)
    levels = [z for z in (0.6, 1.6, 2.6, 3.5, 4.5, 5.4) if z < top]
    random.seed(13)
    seeds = [Vector((random.uniform(min(xs) + 0.4, max(xs) - 0.4), random.uniform(min(ys) + 0.4, max(ys) - 0.4), levels[k % len(levels)] + random.uniform(-0.3, 0.3))) for k in range(4 * len(levels))]
    count = 0
    for i, s in enumerate(seeds):
        bm = bmesh.new(); bm.from_mesh(basem)
        for j, o in enumerate(seeds):
            if i == j: continue
            res = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(o + s) * 0.5, plane_no=(o - s).normalized(), clear_outer=True)
            cutE = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge) and e.is_valid and e.is_boundary]
            if cutE:
                for f in bmesh.ops.holes_fill(bm, edges=cutE, sides=0)["faces"]:
                    f.material_index = reb
            if not bm.faces: break
        if len(bm.faces) < 4:
            bm.free(); continue
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        me = bpy.data.meshes.new("Caco_%02d" % count); bm.to_mesh(me); bm.free()
        for m in basem.materials: me.materials.append(m)
        c = sum((v.co for v in me.vertices), Vector()) / len(me.vertices)
        me.transform(Matrix.Translation(-c))
        ob = bpy.data.objects.new("Caco_%02d" % count, me); ob.location = c; exp.objects.link(ob)
        for p_ in me.polygons: p_.use_smooth = p_.material_index != reb
        count += 1
    os.makedirs(OUT, exist_ok=True)
    for o in bpy.context.view_layer.objects: o.select_set(False)
    objs = list(exp.objects)
    for o in objs:
        o.hide_set(False); o.hide_viewport = False; o.hide_render = False; o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Loja_" + key + ".fbx"), use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True, mesh_smooth_type='FACE',
        use_mesh_modifiers=True, use_triangles=True, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
    for o in objs:
        o.select_set(False); o.hide_set(True); o.hide_render = True
    tris = sum(len(o.data.polygons) for o in objs if o.name.startswith("LOD0_"))
    return {"cacos": count, "props": len(props), "faces_lod0": tris}

def build_all():
    out = {}
    for k in SHOPS:
        build_shop(k)
        out[k] = export_shop(k)
    return out
