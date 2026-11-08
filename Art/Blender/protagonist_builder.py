# Construtor do protagonista "Fugitivo H-19" (Missão 15, decisão D24) — roda dentro do Blender.
# Uso (duas chamadas separadas, a primeira esvazia o arquivo):
#   import bpy; bpy.ops.wm.read_homefile(use_empty=True)
#   exec(open(r"C:\WorkSpace\ImNotAnAlien\Art\Blender\protagonist_builder.py", encoding="utf-8").read())
#   build_protagonist(); preview(...); export_protagonist()
# Parte do corpo base Quaternius (o mesmo esqueleto das animações) e veste o macacão de cobaia:
# marfim com faixa coral, H-19 nas costas, gola oval larga (feita para cabeças de alien),
# manga do braço da telecinese (direito) rasgada, algema quebrada nesse pulso e botas.
# Também cria as marcas do experimento no braço (4 estágios) e a forma "Cabeca_Alien"
# (a cabeça cresce até virar um bulbo de alien no fim do jogo).
import bpy, bmesh, math, random
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

# Funções da roupa dos aliens (mesmo corpo base e mesmo esqueleto).
exec(open(r"C:\WorkSpace\ImNotAnAlien\Art\Blender\alien_builder.py", encoding="utf-8").read())

QUAT = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Quaternius"
P_EXPORT = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Protagonista\Protagonista_H19.fbx"

TK = "l"   # braço da telecinese (o que a animação levanta): manga rasgada, algema e marcas
SUIT = dict(
    offset=0.016,          # folga do tecido sobre a pele
    torn=0.42,             # manga rasgada: até onde vai no braço (0 = ombro, 1 = cotovelo)
    sleeve_end=0.93,       # manga inteira: até o pulso
    leg_end=0.92,          # perna do macacão: termina por cima do cano da bota
    stripe_half=0.075,     # meia-largura da faixa coral
)
SLIM_H19 = {"neck_01": 0.9, "spine_03": 0.93, "spine_02": 0.94, "spine_01": 0.95, "pelvis": 0.96,
            "clavicle": 0.92, "upperarm": 0.88, "lowerarm": 0.9, "thigh": 0.9, "calf": 0.92}
COLLAR = dict(cy=0.018, rx=0.205, ry=0.14, height=0.085, thick=0.022, tilt_deg=8.0, drop=0.035)


def p_materials():
    return {
        "suit": material("H19_Macacao", "#E6D9C2", 0.85),
        "stripe": material("H19_Faixa", "#C8705A", 0.8),
        "pad": material("H19_Reforco", "#D2C3A8", 0.9),
        "dark": material("H19_Escuro", "#3A3634", 0.7),
        "boot": material("H19_Bota", "#5E646C", 0.6),
        "metal": material("H19_Algema", "#7E8890", 0.35),
        "wire": material("H19_Fio", "#E39A7C", 0.6),
        "marks": material("H19_Marcas", "#5FF2D8", 0.3),
        "text": material("H19_Codigo", "#3A3634", 0.8),
        "metal_dark": material("H19_Metal_Escuro", "#4A5056", 0.4),
        "light": material("H19_Luz", "#FF4A3A", 0.3),
    }


# ---------- Base ----------

def setup_protagonist():
    """Num arquivo vazio: importa corpo, olhos, sobrancelhas, cabelo e barba no mesmo esqueleto."""
    bpy.ops.import_scene.fbx(filepath=QUAT + r"\Base\Superhero_Male_FullBody.fbx")
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["SuperHero_Male"]
    body.name = "Body"
    for hair_name in ("Hair_SimpleParted", "Hair_Beard"):
        before = set(bpy.data.objects.keys())
        bpy.ops.import_scene.fbx(filepath=QUAT + rf"\Hair\{hair_name}.fbx")
        new = [bpy.data.objects[n] for n in bpy.data.objects.keys() if n not in before]
        mesh = next(o for o in new if o.type == 'MESH')
        mw = mesh.matrix_world.copy()
        mesh.parent = arm
        mesh.matrix_world = mw
        for g in list(mesh.vertex_groups):
            mesh.vertex_groups.remove(g)
        g = mesh.vertex_groups.new(name="Head")
        g.add([v.index for v in mesh.data.vertices], 1.0, 'REPLACE')
        for m in list(mesh.modifiers):
            mesh.modifiers.remove(m)
        mesh.modifiers.new("Armature", 'ARMATURE').object = arm
        for o in new:
            if o.type == 'ARMATURE':
                bpy.data.objects.remove(o, do_unlink=True)
    # cor de pele para as prévias (no Unity a pele usa a textura da Quaternius)
    for m in body.data.materials:
        m.diffuse_color = (0.78, 0.55, 0.42, 1.0)
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.name.startswith("Hair"):
            for m in o.data.materials:
                m.diffuse_color = (0.16, 0.10, 0.07, 1.0)
    return arm, body


# ---------- Macacão ----------

def build_suit(arm, body, mats):
    rig = _Rig(arm)
    neck = bone_world(arm, "neck_01")
    collar_z = neck.z - 0.02
    tk_arm = f"upperarm_{TK}"
    tk_low = f"lowerarm_{TK}"

    def keep(p, w):
        if _sum(w, ("Head", "hand", "thumb", "index", "middle", "ring", "pinky", "foot", "ball")) > 0.3:
            return False
        if w.get("neck_01", 0) > 0.45:
            return False
        flat = Vector((p.x - neck.x, p.y - neck.y, 0))
        if p.z > collar_z - 0.03 and flat.length < 0.105:
            return False                                   # abertura do pescoço (a gola cobre)
        side = "l" if w.get("upperarm_l", 0) + w.get("lowerarm_l", 0) >= w.get("upperarm_r", 0) + w.get("lowerarm_r", 0) else "r"
        up, lo = w.get(f"upperarm_{side}", 0), w.get(f"lowerarm_{side}", 0)
        if side == TK:
            if lo > 0.3:
                return False
            if up > 0.3 and rig.t(tk_arm, p) > SUIT["torn"]:
                return False
        else:
            if lo > 0.3 and rig.t(f"lowerarm_{side}", p) > SUIT["sleeve_end"]:
                return False
        lside = "l" if w.get("thigh_l", 0) + w.get("calf_l", 0) >= w.get("thigh_r", 0) + w.get("calf_r", 0) else "r"
        if w.get(f"calf_{lside}", 0) > 0.3 and rig.t(f"calf_{lside}", p) > SUIT["leg_end"]:
            return False
        return True

    suit = make_shell(body, arm, "H19_Macacao", keep, SUIT["offset"], mats["suit"], smooth=3,
                      mats_extra=(mats["stripe"], mats["pad"], mats["dark"]), border_smooth=3)
    _tear_sleeve(suit, arm, tk_arm)
    _paint_suit(suit, arm)
    return suit


def _tear_sleeve(suit, arm, bone):
    """Borda da manga rasgada fica irregular (pontas de pano para cima e para baixo)."""
    a, b = bone_world(arm, bone), bone_world(arm, bone, tail=True)
    axis = (b - a).normalized()
    mw = suit.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(suit.data)
    rnd = random.Random(19)
    border = [v for v in bm.verts if v.is_boundary and ((mw @ v.co) - a).dot(axis) > 0.06]
    u = axis.orthogonal().normalized()
    w = axis.cross(u)

    def ang(v):
        r = (mw @ v.co) - a
        return math.atan2(r.dot(w), r.dot(u))

    border.sort(key=ang)
    for i, v in enumerate(border):
        p = mw @ v.co
        jag = (0.022 if i % 3 == 0 else -0.004) + rnd.uniform(-0.008, 0.008)
        r = p - a
        r -= axis * r.dot(axis)
        if r.length > 1e-6 and r.normalized().z < -0.2:
            jag *= 0.2      # embaixo do braço (axila) quase não rasga: evita pontas viradas
        v.co = inv @ (p + axis * jag)
    bm.to_mesh(suit.data)
    bm.free()


def _paint_suit(suit, arm):
    """Faixa coral na diagonal (frente e costas), zíper, joelheiras e punho da manga — com cortes
    na malha (bissecção), para as bordas das cores ficarem retas."""
    mw = suit.matrix_world
    inv = mw.inverted()
    # Faixa: do ombro esquerdo para o quadril direito (vista de frente), igual nas costas.
    # A faixa desce do ombro do braço COM manga até o quadril do lado da manga rasgada (como no blueprint).
    k = 1 if TK == "l" else -1
    top = Vector((0.20 * k, 0, 1.44))
    low = Vector((-0.18 * k, 0, 0.92))
    d = (low - top).normalized()
    n = d.cross(Vector((0, 1, 0))).normalized()          # normal do plano da faixa
    c = (top + low) / 2
    h = SUIT["stripe_half"]
    bm = bmesh.new()
    bm.from_mesh(suit.data)
    for off in (-h, h):
        co = inv @ (c + n * off)
        no = (inv.to_3x3() @ n).normalized()
        geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)
    # fim da faixa: embaixo, na altura do quadril; em cima, antes de descer pelo braço
    for co, no in ((Vector((0, 0, 0.93)), Vector((0, 0, 1))), (Vector((0.235 * k, 0, 0)), Vector((1, 0, 0)))):
        geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=inv @ co, plane_no=(inv.to_3x3() @ no).normalized())
    # punho da manga inteira (o outro braço)
    ob = "l" if TK == "r" else "r"
    la, lb = bone_world(arm, f"lowerarm_{ob}"), bone_world(arm, f"lowerarm_{ob}", tail=True)
    cuff_co = la + (lb - la) * 0.83
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=inv @ cuff_co, plane_no=(inv.to_3x3() @ (lb - la)).normalized())
    # zíper (frente, no meio) e fim das joelheiras
    for x in (-0.007, 0.007):
        geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=inv @ Vector((x, 0, 0)), plane_no=(inv.to_3x3() @ Vector((1, 0, 0))).normalized())
    knee_z = {s: bone_world(arm, f"calf_{s}").z for s in "lr"}
    for s in "lr":
        for z in (knee_z[s] - 0.07, knee_z[s] + 0.08):
            geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=inv @ Vector((0, 0, z)), plane_no=(inv.to_3x3() @ Vector((0, 0, 1))).normalized())
    hip_z = bone_world(arm, "pelvis").z
    for f in bm.faces:
        p = mw @ f.calc_center_median()
        f.material_index = 0
        on_torso = 0.93 < p.z < 1.50 and -0.30 < p.x * -k < 0.235
        if on_torso and abs((p - c).dot(n)) < h:
            f.material_index = 1
        elif (p - la).dot((lb - la).normalized()) > (cuff_co - la).dot((lb - la).normalized()) and p.x * (lb - la).x > 0:
            f.material_index = 2                              # punho
        elif abs(p.x) < 0.007 and p.y > 0.0 and hip_z - 0.05 < p.z < 1.47:
            f.material_index = 3                              # zíper
        else:
            for s, sx in (("l", -1), ("r", 1)):
                if p.y > 0.0 and knee_z[s] - 0.07 < p.z < knee_z[s] + 0.08 and p.x * sx > 0.02:
                    f.material_index = 2                      # joelheira
    bm.to_mesh(suit.data)
    bm.free()
    for poly in suit.data.polygons:
        poly.use_smooth = True


def build_collar(arm, mats, suit=None, body=None):
    """Gola oval, larga e um pouco alta, presa ao peito (spine_03). Por fora e por dentro ela desce
    até encostar no macacão (uma "saia" costurada no tecido), então não fica solta no ar."""
    name = "H19_Gola"
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    neck = bone_world(arm, "neck_01")
    base_z = neck.z - COLLAR["drop"]
    rx, ry, hgt, th = COLLAR["rx"], COLLAR["ry"], COLLAR["height"], COLLAR["thick"]
    seg = 96
    # Perfil (raio extra, altura): por fora sobe abrindo, a borda de cima é arredondada, por dentro desce.
    profile = [(0.0, 0.0), (0.012, hgt * 0.45), (0.02, hgt * 0.85), (0.016, hgt), (0.0, hgt * 1.04),
               (-th, hgt * 0.95), (-th - 0.004, hgt * 0.5), (-th, 0.0)]
    piv = Vector((neck.x, neck.y, base_z))
    rot = Matrix.Rotation(math.radians(-COLLAR["tilt_deg"]), 3, 'X')
    rings = []
    for (dr, dz) in profile:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            p = Vector((neck.x + math.cos(a) * (rx + dr), neck.y + COLLAR["cy"] + math.sin(a) * (ry + dr), base_z + dz))
            ring.append(piv + rot @ (p - piv))
        rings.append(ring)

    # O macacão ganha um buraco do tamanho da gola: por dentro dela só ficam o fundo escuro da gola
    # e o pescoço (antes o tecido das costas aparecia lá dentro).
    if suit is not None:
        smw = suit.matrix_world
        cbm = bmesh.new()
        cbm.from_mesh(suit.data)
        cc = Vector((neck.x, neck.y + COLLAR["cy"], 0))

        def in_oval(p, k):
            q = p - cc
            return (q.x / (rx * k)) ** 2 + (q.y / (ry * k)) ** 2 < 1.0

        kill = [f for f in cbm.faces if any(in_oval(smw @ v.co, 0.9) and (smw @ v.co).z > base_z - 0.2 for v in f.verts)]
        bmesh.ops.delete(cbm, geom=kill, context='FACES')
        bmesh.ops.delete(cbm, geom=[v for v in cbm.verts if not v.link_faces], context='VERTS')
        cbm.to_mesh(suit.data)
        cbm.free()
        suit.data.update()

    # Saia: das bordas de baixo (por fora e por dentro) até a superfície do macacão.
    tree = None
    if suit is not None:
        sbm = bmesh.new()
        sbm.from_mesh(suit.data)
        sbm.transform(suit.matrix_world)
        tree = BVHTree.FromBMesh(sbm)
        sbm.free()

    def down(p):
        if tree is not None:
            hit = tree.ray_cast(p + Vector((0, 0, 0.02)), Vector((0, 0, -1)), 0.4)
            if hit[0] is not None:
                return hit[0] - Vector((0, 0, 0.03))
        return p - Vector((0, 0, 0.06))

    # Encaixe no corpo: cada ponto procura a superfície MAIS PRÓXIMA (macacão ou pele) e fica
    # um pouco acima (lift > 0) ou enterrado (lift < 0). Com o ponto mais próximo — e não um raio
    # para baixo — a gola nunca escorrega pelas costas nem abre aba no ombro.
    body_tree_ = None
    if body is not None:
        bbm = bmesh.new()
        bbm.from_mesh(body.data)
        bbm.transform(body.matrix_world)
        body_tree_ = BVHTree.FromBMesh(bbm)
        bbm.free()

    def snap(p, lift, trees=None):
        best = None
        for tr in (trees or (tree, body_tree_)):
            if tr is None:
                continue
            loc, nrm, _, dist = tr.find_nearest(p)
            if loc is not None and (best is None or dist < best[2]):
                best = (loc, nrm, dist)
        if best is None:
            return p.copy()
        n = best[1]
        if n.z < 0.0:
            n = -n
        return best[0] + n * lift

    def smooth_ring(pts, it=3):
        m = len(pts)
        for _ in range(it):
            pts = [(pts[i - 1] + pts[i] * 2 + pts[(i + 1) % m]) / 4 for i in range(m)]
        return pts

    center = Vector((neck.x, neck.y + COLLAR["cy"], 0))
    # três anéis que vão "deitando" sobre o tecido: a gola encontra o macacão quase na tangente,
    # então a emenda praticamente some
    outer_mid, outer_mid2, outer_low = [], [], []
    for p0 in rings[0]:
        rad = Vector((p0.x - center.x, p0.y - center.y, 0)).normalized()
        outer_mid.append(snap(p0 + rad * 0.018 - Vector((0, 0, 0.012)), 0.016, (tree,)))
        outer_mid2.append(snap(p0 + rad * 0.04 - Vector((0, 0, 0.025)), 0.012, (tree,)))
        outer_low.append(snap(p0 + rad * 0.07 - Vector((0, 0, 0.035)), 0.004, (tree,)))
    outer_mid = smooth_ring(outer_mid, 4)
    outer_mid2 = smooth_ring(outer_mid2, 4)
    outer_low = smooth_ring(outer_low, 2)
    # a curva sobe sempre: o anel do meio fica abaixo da base da gola, o de baixo abaixo do meio
    for i in range(len(outer_mid)):
        outer_mid[i].z = min(outer_mid[i].z, rings[0][i].z - 0.004)
        outer_mid2[i].z = min(outer_mid2[i].z, outer_mid[i].z - 0.004)
        outer_low[i].z = min(outer_low[i].z, outer_mid2[i].z - 0.004)

    # Por dentro: a parede desce até o tecido e um "fundo" escuro fecha em volta do pescoço.
    cen = center
    inner_low, floor_mid, floor_in = [], [], []
    for p in rings[-1]:
        d = Vector((p.x - cen.x, p.y - cen.y, 0))
        inner_low.append(snap(Vector((p.x, p.y, p.z - 0.02)), 0.02, (body_tree_,)))
        floor_mid.append(snap(cen + d * 0.72 + Vector((0, 0, p.z - 0.02)), 0.016, (body_tree_,)))
        floor_in.append(snap(cen + d * 0.5 + Vector((0, 0, p.z - 0.02)), 0.012, (body_tree_,)))
    inner_low = smooth_ring(inner_low)
    floor_mid = smooth_ring(floor_mid)
    floor_in = smooth_ring(floor_in)
    for i in range(len(inner_low)):
        inner_low[i].z = min(inner_low[i].z, rings[-1][i].z - 0.004)
    inner_mid = [(p + q) / 2 for p, q in zip(rings[-1], inner_low)]
    all_rings = [outer_low, outer_mid2, outer_mid] + rings + [inner_mid, inner_low, floor_mid, floor_in]
    # 1 = segue o tecido/pele (ombros), 0 = rígida no peito; a transição é gradual
    blend = [1.0, 1.0, 0.75, 0.4, 0.15] + [0.0] * (len(rings) - 2) + [0.3, 0.8, 1.0, 1.0]
    dark_from = 3 + 5                                         # da borda de cima para dentro: forro escuro

    bm = bmesh.new()
    vrings = [[bm.verts.new(p) for p in ring] for ring in all_rings]
    for k in range(len(vrings) - 1):
        r0, r1 = vrings[k], vrings[k + 1]
        for i in range(seg):
            f = bm.faces.new((r0[i], r0[(i + 1) % seg], r1[(i + 1) % seg], r1[i]))
            # faixa de baixo = costura (reforço), por dentro = forro escuro
            f.material_index = 1 if k >= dark_from else (2 if k == 0 else 0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.append(mats["suit"])
    me.materials.append(mats["dark"])
    me.materials.append(mats["pad"])
    for poly in me.polygons:
        poly.use_smooth = True

    # Pesos: a parte de cima é rígida no peito; a saia mistura com os pesos da pele embaixo dela
    # (ombros e clavículas), para acompanhar o tecido quando os braços se mexem.
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    groups = {}

    def group(n):
        if n not in groups:
            groups[n] = obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n)
        return groups[n]

    skin = _nearest_weights(body, [v.co for v in me.vertices]) if body is not None else None
    # só ossos do tronco e pescoço (sem o braço: senão a gola é puxada para baixo junto com ele)
    if skin is not None:
        torso = ("spine", "clavicle", "neck", "pelvis")
        clean = []
        for wmap in skin:
            kept = {n: w for n, w in wmap.items() if n.startswith(torso)}
            tot = sum(kept.values()) or 1.0
            clean.append({n: w / tot for n, w in kept.items()} if kept else {"spine_03": 1.0})
        skin = clean
    for v in me.vertices:
        f = blend[v.index // seg]
        acc = {"spine_03": 1.0 - f}
        if skin is not None and f > 0:
            for n, w in skin[v.index].items():
                acc[n] = acc.get(n, 0.0) + w * f
        else:
            acc["spine_03"] = 1.0
        for n, w in acc.items():
            if w > 1e-4:
                group(n).add([v.index], w, 'REPLACE')
    obj.modifiers.new("Armature", 'ARMATURE').object = arm
    return obj


def _nearest_weights(body, points, k=4):
    """Pesos dos ossos da pele mais próxima de cada ponto (em coordenadas do mundo)."""
    from mathutils.kdtree import KDTree
    mw = body.matrix_world
    verts = body.data.vertices
    kd = KDTree(len(verts))
    for v in verts:
        kd.insert(mw @ v.co, v.index)
    kd.balance()
    names = {g.index: g.name for g in body.vertex_groups}
    out = []
    for p in points:
        acc, tot = {}, 0.0
        for co, idx, dist in kd.find_n(p, k):
            wgt = 1.0 / max(dist, 1e-4)
            tot += wgt
            for g in verts[idx].groups:
                acc[names[g.group]] = acc.get(names[g.group], 0.0) + g.weight * wgt
        s = sum(acc.values()) or 1.0
        out.append({n: w / s for n, w in acc.items()})
    return out


def _rigid(obj, arm, bone):
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    g = obj.vertex_groups.new(name=bone)
    g.add([v.index for v in obj.data.vertices], 1.0, 'REPLACE')
    obj.modifiers.new("Armature", 'ARMATURE').object = arm


# ---------- Ajudantes ----------

def body_tree(body):
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.transform(body.matrix_world)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    return tree


def new_object(name, verts, faces, mats, mat_index=None):
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for m in mats:
        me.materials.append(m)
    for i, poly in enumerate(me.polygons):
        poly.use_smooth = True
        if mat_index:
            poly.material_index = mat_index[i]
    return obj


def copy_weights(obj, body, arm, k=3):
    """Cada vértice recebe os pesos dos vértices do corpo mais próximos (dobra junto com a pele)."""
    from mathutils.kdtree import KDTree
    mw = body.matrix_world
    verts = body.data.vertices
    kd = KDTree(len(verts))
    for v in verts:
        kd.insert(mw @ v.co, v.index)
    kd.balance()
    names = {g.index: g.name for g in body.vertex_groups}
    groups = {}
    omw = obj.matrix_world
    for v in obj.data.vertices:
        acc = {}
        found = kd.find_n(omw @ v.co, k)
        tot = 0.0
        for co, idx, dist in found:
            wgt = 1.0 / max(dist, 1e-4)
            tot += wgt
            for g in verts[idx].groups:
                acc[names[g.group]] = acc.get(names[g.group], 0.0) + g.weight * wgt
        for n, wv in acc.items():
            if n not in groups:
                groups[n] = obj.vertex_groups.get(n) or obj.vertex_groups.new(name=n)
            groups[n].add([v.index], wv / tot, 'REPLACE')
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    obj.modifiers.new("Armature", 'ARMATURE').object = arm


def tube(points, radius, sides=6):
    """Tubo fino ao longo de uma linha (fios)."""
    verts, faces = [], []
    n = len(points)
    for i, p in enumerate(points):
        t = (points[min(i + 1, n - 1)] - points[max(i - 1, 0)]).normalized()
        u = t.orthogonal().normalized()
        w = t.cross(u)
        for k in range(sides):
            a = 2 * math.pi * k / sides
            verts.append(p + (u * math.cos(a) + w * math.sin(a)) * radius)
    for i in range(n - 1):
        for k in range(sides):
            a, b = i * sides + k, i * sides + (k + 1) % sides
            faces.append((a, b, b + sides, a + sides))
    verts.append(points[0])
    verts.append(points[-1])
    c0, c1 = len(verts) - 2, len(verts) - 1
    for k in range(sides):
        faces.append((c0, (k + 1) % sides, k))
        faces.append((c1, (n - 1) * sides + k, (n - 1) * sides + (k + 1) % sides))
    return verts, faces


# ---------- Botas ----------

BOOT = dict(top=0.55, offset=0.014, sole_z=0.032, sole_out=0.012, cuff=0.035, toe=0.012)


def build_boots(arm, body, mats):
    """Bota com o formato do pé de verdade: uma "casca" tirada do próprio pé do corpo (calcanhar,
    peito do pé, dedos), mais grossa embaixo (sola escura que sai um pouco para fora), bico
    levemente arredondado e um punho claro em cima. Os pesos vêm da pele, então ela dobra
    junto com o tornozelo e os dedos nas animações."""
    rig = _Rig(arm)

    def keep(p, w):
        if _sum(w, ("foot", "ball")) > 0.05:
            return True
        side = "l" if w.get("calf_l", 0) >= w.get("calf_r", 0) else "r"
        return w.get(f"calf_{side}", 0) > 0.3 and rig.t(f"calf_{side}", p) > BOOT["top"]

    boots = make_shell(body, arm, "H19_Botas", keep, BOOT["offset"], mats["boot"], smooth=4,
                       mats_extra=(mats["dark"], mats["suit"]), border_smooth=3)
    mw = boots.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(boots.data)
    for side in ("l", "r"):
        foot = bone_world(arm, f"foot_{side}")
        ball = bone_world(arm, f"ball_{side}")
        fwd = Vector((ball.x - foot.x, ball.y - foot.y, 0)).normalized()
        center = Vector((foot.x, foot.y, 0))
        # Bico arredondado: a frente do pé (onde ficavam os dedos) vira uma "caixa" de bota lisa,
        # meio elipsoide que cresce da altura da bola do pé até a ponta.
        side_dir = Vector((0, 0, 1)).cross(fwd).normalized()
        O = Vector((ball.x, ball.y, 0.0))
        mine = [v for v in bm.verts if ((mw @ v.co).x > 0) == (foot.x > 0)]
        ahead_max = max(((mw @ v.co) - O).dot(fwd) for v in mine)
        width = max(abs(((mw @ v.co) - O).dot(side_dir)) for v in mine if 0.0 < ((mw @ v.co) - O).dot(fwd) < 0.03)
        a0 = -0.03
        RA, RS, RZ = ahead_max - a0 + 0.012, width + 0.006, 0.085
        for v in mine:
            p = mw @ v.co
            q = p - O
            a = q.dot(fwd)
            if a < a0:
                continue
            loc = Vector((a - a0, q.dot(side_dir), max(p.z, 0.0)))
            k = 1.0 / max(1e-6, math.sqrt((loc.x / RA) ** 2 + (loc.y / RS) ** 2 + (loc.z / RZ) ** 2))
            surf = loc * k
            target = O + fwd * (surf.x + a0) + side_dir * surf.y + Vector((0, 0, surf.z))
            t = _smoothstep(a0, a0 + 0.06, a)
            v.co = inv @ p.lerp(target, t)
        for v in bm.verts:
            p = mw @ v.co
            if (p.x > 0) != (foot.x > 0):
                continue
            # sola: tudo que está perto do chão abre um pouco para fora e fica reto embaixo
            low = max(0.0, 1.0 - p.z / (BOOT["sole_z"] * 2.2))
            flat = Vector((p.x - center.x, p.y - center.y, 0))
            if flat.length > 1e-6:
                p += flat.normalized() * BOOT["sole_out"] * low
            if p.z < 0.006:
                p.z = -0.004
            v.co = inv @ p
    # alisa a sola (a abertura para fora deixava a borda da frente enrugada) e deixa o fundo reto
    low_verts = [v for v in bm.verts if (mw @ v.co).z < 0.07]
    for _ in range(4):
        bmesh.ops.smooth_vert(bm, verts=low_verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    for v in low_verts:
        p = mw @ v.co
        if p.z < 0.008:
            p.z = -0.004
            v.co = inv @ p
    # cortes retos para as cores: sola (escura) e punho (claro) em cima
    top_z = {}
    for side in ("l", "r"):
        a, b = bone_world(arm, f"calf_{side}"), bone_world(arm, f"calf_{side}", tail=True)
        top_z[side] = (a + (b - a) * BOOT["top"]).z
    for z in [BOOT["sole_z"]] + [top_z[s_] - BOOT["cuff"] for s_ in ("l", "r")]:
        geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=inv @ Vector((0, 0, z)), plane_no=(inv.to_3x3() @ Vector((0, 0, 1))).normalized())
    for f in bm.faces:
        c = mw @ f.calc_center_median()
        side = "l" if c.x > 0 else "r"
        side = "l" if (bone_world(arm, "foot_l").x > 0) == (c.x > 0) else "r"
        if c.z < BOOT["sole_z"]:
            f.material_index = 1
        elif c.z > top_z[side] - BOOT["cuff"]:
            f.material_index = 2
        else:
            f.material_index = 0
        f.smooth = True
    bm.to_mesh(boots.data)
    bm.free()
    return boots


def hide_feet(body):
    """Apaga os pés do corpo (ficam dentro da bota e furavam o calçado nas animações)."""
    return delete_body_part(body, ["foot_l", "foot_r", "ball_l", "ball_r"], threshold=0.3)


# ---------- Algema quebrada ----------

def build_cuff(arm, body, mats):
    a, b = bone_world(arm, f"lowerarm_{TK}"), bone_world(arm, f"lowerarm_{TK}", tail=True)
    axis = (b - a).normalized()
    c = a + (b - a) * 0.88
    u = Vector((0, 0, 1))
    u = (u - axis * u.dot(axis)).normalized()
    w = axis.cross(u)
    # tamanho do pulso: distância da pele ao osso naquela altura
    mw = body.matrix_world
    ru, rw = 0.035, 0.03
    for v in body.data.vertices:
        p = mw @ v.co
        if abs((p - c).dot(axis)) < 0.012:
            r = p - c
            r -= axis * r.dot(axis)
            if r.length < 0.08:
                ru = max(ru, abs(r.dot(u)))
                rw = max(rw, abs(r.dot(w)))
    ru += 0.012
    rw += 0.012
    half, th = 0.028, 0.013
    gap0, gap1 = math.radians(205), math.radians(250)      # pedaço que quebrou (embaixo, atrás)
    segs = 40
    angles = [gap1 + (2 * math.pi - (gap1 - gap0)) * i / segs for i in range(segs + 1)]
    prof = [(0, -half), (0, half), (th, half), (th, -half)]
    verts, faces = [], []
    for a_ in angles:
        rr = Vector((0, 0, 0))
        for dr, dx in prof:
            radial = u * math.cos(a_) * (ru + dr) + w * math.sin(a_) * (rw + dr)
            verts.append(c + radial + axis * dx)
    P = len(prof)
    for i in range(segs):
        for k in range(P):
            i0, i1 = i * P + k, i * P + (k + 1) % P
            faces.append((i0, i1, i1 + P, i0 + P))
    faces.append(tuple(range(P))[::-1])
    faces.append(tuple(segs * P + k for k in range(P)))
    mi = [0] * len(faces)
    # rebites (dobradiça quebrada) nas pontas e dois fios soltos saindo da quebra
    extra = []
    for a_ in (angles[0], angles[-1]):
        p = c + u * math.cos(a_) * (ru + th) + w * math.sin(a_) * (rw + th)
        out = (u * math.cos(a_) + w * math.sin(a_)).normalized()
        tip = p + out * 0.01
        extra.append((p, out))
    base = len(verts)
    for k, (p, out) in enumerate(extra):
        pts = [p, p + out * 0.015 + axis * 0.004, p + out * 0.03 - u * 0.02 + axis * (0.01 if k else -0.004),
               p + out * 0.035 - u * 0.045 + axis * (0.02 if k else 0.0)]
        tv, tf = tube(pts, 0.0028)
        off = len(verts)
        verts += tv
        faces += [tuple(i + off for i in f) for f in tf]
        mi += [1] * len(tf)
    obj = new_object("H19_Algema", verts, faces, [mats["metal"], mats["wire"]], mi)
    me = obj.data
    me.validate()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    _rigid(obj, arm, f"lowerarm_{TK}")
    return obj


# ---------- Marcas do experimento ----------
# 4 estágios (objetos separados, o Unity liga um por um conforme a evolução):
# 1 antebraço, 2 braço (até a manga rasgada), 3 pescoço, 4 rosto.

def _surface_path(tree, a, b, ts, angle_fn, ref_up, lift=0.0016):
    axis = (b - a)
    ax = axis.normalized()
    u = (ref_up - ax * ref_up.dot(ax)).normalized()
    w = ax.cross(u)
    pts, nrm = [], []
    for t in ts:
        c = a + axis * t
        ang = math.radians(angle_fn(t))
        d = (u * math.cos(ang) + w * math.sin(ang)).normalized()
        hit = tree.ray_cast(c + d * 0.25, -d, 0.3)
        if hit[0] is None:
            continue
        pts.append(hit[0] + hit[1] * lift)
        nrm.append(hit[1])
    return pts, nrm


def _ribbon(pts, nrm, width):
    verts, faces = [], []
    n = len(pts)
    for i in range(n):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        side = t.cross(nrm[i]).normalized()
        f = i / max(n - 1, 1)
        wdt = width * max(0.25, math.sin(math.pi * f) ** 0.6)
        verts.append(pts[i] - side * wdt / 2)
        verts.append(pts[i] + side * wdt / 2)
    for i in range(n - 1):
        faces.append((2 * i, 2 * i + 2, 2 * i + 3, 2 * i + 1))
    return verts, faces


def build_marks(arm, body, mats):
    tree = body_tree(body)
    s = TK
    sign = 1 if s == "r" else -1
    la, lb = bone_world(arm, f"lowerarm_{s}"), bone_world(arm, f"lowerarm_{s}", tail=True)
    ua, ub = bone_world(arm, f"upperarm_{s}"), bone_world(arm, f"upperarm_{s}", tail=True)
    neck = bone_world(arm, "neck_01")
    head_a, head_b = bone_world(arm, "Head"), bone_world(arm, "Head", tail=True)
    up = Vector((0, 0, 1))

    def steps(t0, t1, n=26):
        return [t0 + (t1 - t0) * i / (n - 1) for i in range(n)]

    stages = {
        1: [  # antebraço: três veias que sobem do pulso, com dois galhos
            ((la, lb), steps(0.86, 0.06), lambda t: 10 + 14 * math.sin(t * 11), 0.0065),
            ((la, lb), steps(0.80, 0.10), lambda t: -55 + 12 * math.sin(t * 9 + 1), 0.0055),
            ((la, lb), steps(0.70, 0.25), lambda t: 60 + 10 * math.sin(t * 13 + 2), 0.0045),
            ((la, lb), steps(0.55, 0.38, 10), lambda t: 10 + 14 * math.sin(t * 11) + (0.55 - t) * 160, 0.0035),
            ((la, lb), steps(0.42, 0.28, 10), lambda t: -55 - (0.42 - t) * 180, 0.003),
        ],
        2: [  # braço: continuam do cotovelo até a manga rasgada
            ((ua, ub), steps(1.0, 0.50, 20), lambda t: 5 + 14 * math.sin(t * 10), 0.0055),
            ((ua, ub), steps(0.98, 0.56, 18), lambda t: -50 + 10 * math.sin(t * 8 + 1), 0.0045),
        ],
        3: [  # pescoço: lado do braço da telecinese, de baixo da gola até atrás da orelha
            ((neck + Vector((0, 0, -0.04)), head_a + Vector((0, 0, 0.03))), steps(0.0, 1.0, 18),
             lambda t: sign * (-5 + 18 * math.sin(t * 7)), 0.005),
            ((neck + Vector((0, 0, -0.04)), head_a + Vector((0, 0, 0.0))), steps(0.1, 0.9, 14),
             lambda t: sign * (35 + 10 * math.sin(t * 9)), 0.0035),
        ],
        4: [  # rosto: da mandíbula até a têmpora
            ((head_a + Vector((0, 0, -0.03)), head_a + Vector((0, 0, 0.17))), steps(0.15, 0.85, 20),
             lambda t: sign * (25 + 22 * math.sin(t * 6)), 0.0045),
            ((head_a + Vector((0, 0, -0.03)), head_a + Vector((0, 0, 0.17))), steps(0.45, 0.95, 12),
             lambda t: sign * (5 + 10 * math.sin(t * 8)), 0.0035),
        ],
    }
    # referência "para cima" de cada eixo: braço = topo; pescoço/cabeça = lado direito (+X) ou esquerdo
    objs = []
    for stage, veins in stages.items():
        verts, faces = [], []
        for (a, b), ts, fn, width in veins:
            ref = up if stage <= 2 else Vector((sign, 0, 0))
            pts, nrm = _surface_path(tree, a, b, ts, fn, ref)
            if len(pts) < 3:
                continue
            v, f = _ribbon(pts, nrm, width)
            off = len(verts)
            verts += v
            faces += [tuple(i + off for i in fc) for fc in f]
        obj = new_object(f"H19_Marcas_{stage}", verts, faces, [mats["marks"]])
        copy_weights(obj, body, arm)
        objs.append(obj)
    return objs


# ---------- Código H-19 nas costas ----------

def build_code(arm, suit, mats, text="H-19"):
    name = "H19_Codigo"
    cu = bpy.data.curves.new(name, 'FONT')
    cu.body = text
    cu.size = 0.135
    cu.offset = 0.0018      # letras mais grossas
    cu.align_x = 'CENTER'
    cu.align_y = 'CENTER'
    cu.extrude = 0.0015
    cu.space_character = 1.05
    tmp = bpy.data.objects.new(name + "_tmp", cu)
    bpy.context.scene.collection.objects.link(tmp)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    bpy.data.objects.remove(tmp, do_unlink=True)
    bpy.data.curves.remove(cu)
    # nas costas, lido de trás: gira para olhar para -Y
    rot = Matrix.Rotation(math.radians(90), 4, 'X')   # visto de trás, a leitura fica no +X
    me.transform(rot)
    sbm = bmesh.new()
    sbm.from_mesh(suit.data)
    sbm.transform(suit.matrix_world)
    tree = BVHTree.FromBMesh(sbm)
    sbm.free()
    z0 = 1.355
    for v in me.vertices:
        x, depth, z = v.co.x, v.co.y, v.co.z + z0
        hit = tree.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)), 2.0)
        y = hit[0].y if hit[0] is not None else -0.15
        v.co = Vector((x, y - 0.0035 + depth, z))
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    me.materials.clear()
    me.materials.append(mats["text"])
    return obj


# ---------- Barba por fazer e cabelo bagunçado ----------

def trim_beard(body, amount=0.8):
    beard = bpy.data.objects.get("Hair_Beard")
    if beard is None:
        return
    tree = body_tree(body)
    mw = beard.matrix_world
    inv = mw.inverted()
    for v in beard.data.vertices:
        p = mw @ v.co
        near = tree.find_nearest(p)
        if near[0] is not None:
            v.co = inv @ (p + (near[0] + near[1] * 0.003 - p) * amount)


def mess_hair(body, strength=0.016, seed=7):
    from mathutils import noise
    hair = bpy.data.objects.get("Hair_SimpleParted")
    if hair is None:
        return
    tree = body_tree(body)
    mw = hair.matrix_world
    inv = mw.inverted()
    off = Vector((seed * 1.3, seed * 2.1, seed * 0.7))
    for v in hair.data.vertices:
        p = mw @ v.co
        near = tree.find_nearest(p)
        lift = min(1.0, (near[3] if near[0] is not None else 0.02) / 0.025)
        n = noise.noise_vector(p * 28 + off)
        up = near[1] if near[0] is not None else Vector((0, 0, 1))
        v.co = inv @ (p + (n * 0.7 + up * 0.35) * strength * lift)


# ---------- A cabeça cresce (forma "Cabeca_Alien") ----------

def _smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


GROW = dict(pivot=(0.0, -0.02, 1.645), sx=1.25, sback=1.8, sfront=0.35, sz=0.55, tilt=20.0)


def head_grow(p, eye_z):
    piv = Vector(GROW["pivot"])
    s = _smoothstep(eye_z + 0.012, eye_z + 0.095, p.z)
    if s <= 0.0:
        return p.copy()
    q = p - piv
    q.x *= 1 + GROW["sx"] * s
    q.y *= 1 + (GROW["sback"] if q.y < 0 else GROW["sfront"]) * s
    if q.z > 0:
        q.z *= 1 + GROW["sz"] * s
    rot = Matrix.Rotation(math.radians(GROW["tilt"] * s), 3, 'X')
    return piv + rot @ q


def add_head_growth(body):
    eyes = bpy.data.objects.get("Eyes")
    eye_z = sum(((eyes.matrix_world @ v.co).z for v in eyes.data.vertices), 0.0) / len(eyes.data.vertices) if eyes else 1.70
    tree = body_tree(body)
    targets = [body] + [o for o in bpy.data.objects if o.type == 'MESH' and o.name in
                        ("Eyes", "Eyebrows", "Hair_SimpleParted", "Hair_Beard", "H19_Marcas_3", "H19_Marcas_4")]
    for obj in targets:
        if obj.data.shape_keys is None:
            obj.shape_key_add(name="Basis", from_mix=False)
        key = obj.data.shape_keys.key_blocks.get("Cabeca_Alien") or obj.shape_key_add(name="Cabeca_Alien", from_mix=False)
        mw = obj.matrix_world
        inv = mw.inverted()
        hairy = obj.name.startswith("Hair_SimpleParted")
        for i, v in enumerate(obj.data.vertices):
            p = mw @ v.co
            if hairy:
                # o cabelo acompanha o couro cabeludo e vai "assentando" (no fim, quase careca)
                near = tree.find_nearest(p)
                if near[0] is not None:
                    s = _smoothstep(eye_z + 0.012, eye_z + 0.095, near[0].z)
                    base = head_grow(near[0], eye_z)
                    key.data[i].co = inv @ (base + (p - near[0]) * (1.0 - 0.9 * s))
                    continue
            key.data[i].co = inv @ head_grow(p, eye_z)
    return eye_z


# ---------- Montagem ----------

def build_protagonist():
    arm, body = setup_protagonist()
    SLIM.clear()
    SLIM.update(SLIM_H19)
    slim_body(body, arm)
    mats = p_materials()
    suit = build_suit(arm, body, mats)
    build_collar(arm, mats, suit, body)
    boots = build_boots(arm, body, mats)
    hide_feet(body)
    cuff = build_cuff_locked(arm, body, mats)
    marks = build_marks(arm, body, mats)
    code = build_code(arm, suit, mats)
    copy_weights(code, body, arm)
    trim_beard(body)
    mess_hair(body)
    apply_user_adjustments(arm)
    eye_z = add_head_growth(body)
    set_growth(0.0)
    return {"suit": len(suit.data.vertices), "marks": [len(m.data.vertices) for m in marks], "eye_z": round(eye_z, 3)}


def set_growth(value):
    for o in bpy.data.objects:
        if o.type == 'MESH' and o.data.shape_keys and "Cabeca_Alien" in o.data.shape_keys.key_blocks:
            o.data.shape_keys.key_blocks["Cabeca_Alien"].value = value


def preview(path, target=(0, 0, 1.0), offset=(0, 3.6, 0.4), size=800, lens=50):
    sc = bpy.context.scene
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    cam.data.lens = lens
    t = Vector(target)
    cam.location = t + Vector(offset)
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.light = 'STUDIO'
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def export_protagonist(path=P_EXPORT):
    import os
    os.makedirs(os.path.dirname(path), exist_ok=True)
    arm = bpy.data.objects["Armature"]
    set_growth(0.0)
    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.data.objects:
        if o.type in ('ARMATURE', 'MESH') and o.name != "PreviewCam":
            o.hide_set(False)
            o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', use_armature_deform_only=False)
    return path


# ---------- Gola contínua (parte do macacão) ----------
# A gola não é mais um anel separado: o buraco do pescoço do macacão é aberto do tamanho da gola
# e a própria borda do tecido sobe, faz a curva da gola, desce por dentro (forro escuro) e fecha
# em volta do pescoço. Uma malha só, então parece uma roupa inteira.

def build_collar_continuous(suit, arm, mats):
    old = bpy.data.objects.get("H19_Gola")
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    neck = bone_world(arm, "neck_01")
    C = Vector((neck.x, neck.y - 0.005, 0))
    rx, ry, hgt, th = COLLAR["rx"], COLLAR["ry"], COLLAR["height"], COLLAR["thick"]
    base_z = neck.z - COLLAR["drop"]
    mw = suit.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(suit.data)
    deform = bm.verts.layers.deform.active
    gi = {g.name: g.index for g in suit.vertex_groups}

    # Altura do tecido embaixo do oval da gola (raio para baixo), suavizada em volta.
    world = bm.copy()
    world.transform(mw)
    tree = BVHTree.FromBMesh(world)
    world.free()
    N = 96
    raw = []
    for i in range(N):
        t = 2 * math.pi * i / N
        q = C + Vector((math.cos(t) * rx, math.sin(t) * ry, 0))
        hit = tree.ray_cast(Vector((q.x, q.y, 2.2)), Vector((0, 0, -1)), 2.0)
        raw.append(hit[0].z if hit[0] is not None else base_z)
    feet = [sum(raw[(i + k) % N] for k in range(-4, 5)) / 9.0 for i in range(N)]

    def foot_z(t):
        f = (t % (2 * math.pi)) / (2 * math.pi) * N
        i0 = int(f) % N
        return feet[i0] + (feet[(i0 + 1) % N] - feet[i0]) * (f - int(f))

    def inside(p, k=1.0):
        q = p - C
        return (q.x / (rx * k)) ** 2 + (q.y / (ry * k)) ** 2 < 1.0

    # 1) abre o buraco do tamanho da gola (só a parte de cima do tronco)
    kill = [f for f in bm.faces if any(inside(mw @ v.co, 0.98) and (mw @ v.co).z > base_z - 0.25 for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')

    # 2) a borda do buraco, percorrida em ordem (seguindo as arestas da borda)
    cand = {v for v in bm.verts if v.is_boundary and inside(mw @ v.co, 1.6) and (mw @ v.co).z > base_z - 0.3}
    start = min(cand, key=lambda v: abs(((mw @ v.co) - C).x))
    loop, prev, cur = [start], None, start
    while True:
        nxt = None
        for e in cur.link_edges:
            if e.is_boundary:
                o = e.other_vert(cur)
                if o is not prev and o in cand:
                    nxt = o
                    break
        if nxt is None or nxt is start:
            break
        loop.append(nxt)
        prev, cur = cur, nxt
        if len(loop) > 2000:
            break

    def ang(v):
        q = (mw @ v.co) - C
        return math.atan2(q.y / ry, q.x / rx)

    # sentido anti-horário
    area = 0.0
    for i in range(len(loop)):
        p0, p1 = mw @ loop[i].co, mw @ loop[(i + 1) % len(loop)].co
        area += p0.x * p1.y - p1.x * p0.y
    if area < 0:
        loop.reverse()

    # 3) cada vértice da borda vira o pé de uma "costela" da gola, que segue a altura do tecido
    outer = [(0.006, 0.02), (0.014, hgt * 0.45), (0.02, hgt * 0.85), (0.016, hgt), (0.0, hgt * 1.04)]
    inner = [(-th, hgt * 0.95), (-th - 0.004, hgt * 0.5), (-th, 0.0)]
    floor_r = (0.088, 0.075)
    blend = [0.6, 0.25] + [0.0] * (len(outer) + len(inner) - 1)
    cols = []
    for v in loop:
        t = ang(v)
        d = Vector((math.cos(t) * rx, math.sin(t) * ry, 0))
        radial = Vector((math.cos(t) / rx, math.sin(t) / ry, 0)).normalized()
        fz = foot_z(t)
        foot = C + d
        foot.z = fz
        v.co = inv @ foot
        wsrc = dict(v[deform].items())
        col = [v]
        pts = []
        for dr, dz in outer + inner:
            q = C + d + radial * dr
            q.z = fz + dz
            pts.append(q)
        fl = C + Vector((math.cos(t) * floor_r[0], math.sin(t) * floor_r[1], 0))
        fl.z = min(fz, base_z) + 0.004
        pts.append(fl)
        for r, q in enumerate(pts):
            nv = bm.verts.new(inv @ q)
            dv = nv[deform]
            f = blend[r] if r < len(blend) else 0.0
            if f > 0:
                for k_, w in wsrc.items():
                    dv[k_] = w * f
            last = r == len(pts) - 1
            dv[gi["spine_03"]] = dv.get(gi["spine_03"], 0.0) + (1.0 - f) * (0.65 if last else 1.0)
            if last and "neck_01" in gi:
                dv[gi["neck_01"]] = 0.35
            col.append(nv)
        cols.append(col)
    dark_from = 1 + len(outer)
    n = len(cols)
    for i in range(n):
        c0, c1 = cols[i], cols[(i + 1) % n]
        for r in range(len(c0) - 1):
            try:
                f = bm.faces.new((c0[r], c1[r], c1[r + 1], c0[r + 1]))
            except ValueError:
                continue
            f.material_index = 3 if r >= dark_from else 0
            f.smooth = True
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(suit.data)
    bm.free()
    suit.data.update()
    return {"loop": n}


# ---------- Algema presa de verdade ----------
# Bracelete grosso de metal, justo no pulso e FECHADO, com a caixa da trava (luz vermelha piscando
# no Unity), parafusos dos dois lados e, embaixo, o suporte onde a corrente/cabo era preso: um elo
# quebrado e fios arrancados. O que quebrou foi a ligação com a máquina, não o bracelete.

def _oriented_box(c, axes, size, bevel=0.25):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(size) * bevel, segments=2, affect='EDGES')
    m = Matrix((axes[0], axes[1], axes[2])).transposed()
    verts = [c + m @ v.co for v in bm.verts]
    faces = [tuple(v.index for v in f.verts) for f in bm.faces]
    bm.free()
    return verts, faces


def _cylinder(c, axis, radius, length, sides=8):
    axis = axis.normalized()
    u = axis.orthogonal().normalized()
    w = axis.cross(u)
    verts, faces = [], []
    for e in (-0.5, 0.5):
        for k in range(sides):
            a = 2 * math.pi * k / sides
            verts.append(c + axis * (length * e) + (u * math.cos(a) + w * math.sin(a)) * radius)
    for k in range(sides):
        faces.append((k, (k + 1) % sides, sides + (k + 1) % sides, sides + k))
    faces.append(tuple(range(sides))[::-1])
    faces.append(tuple(sides + k for k in range(sides)))
    return verts, faces


def build_cuff_locked(arm, body, mats):
    a, b = bone_world(arm, f"lowerarm_{TK}"), bone_world(arm, f"lowerarm_{TK}", tail=True)
    axis = (b - a).normalized()
    c = a + (b - a) * 0.87
    u = Vector((0, 0, 1))
    u = (u - axis * u.dot(axis)).normalized()      # em cima do pulso (costas da mão)
    w = axis.cross(u)
    mw = body.matrix_world
    ru, rw = 0.03, 0.026
    for v in body.data.vertices:
        p = mw @ v.co
        if abs((p - c).dot(axis)) < 0.02:
            r = p - c
            r -= axis * r.dot(axis)
            if r.length < 0.08:
                ru = max(ru, abs(r.dot(u)))
                rw = max(rw, abs(r.dot(w)))
    ru += 0.005
    rw += 0.005
    half, th = 0.034, 0.017
    seg = 40
    # perfil do bracelete (radial, ao longo do braço): bordas com friso
    prof = [(0.0, -half), (0.006, -half - 0.002), (th, -half + 0.002), (th + 0.002, -half + 0.008),
            (th, -half + 0.012), (th - 0.002, 0.0), (th, half - 0.012), (th + 0.002, half - 0.008),
            (th, half - 0.002), (0.006, half + 0.002), (0.0, half)]
    P = len(prof)
    verts, faces, mi = [], [], []
    for i in range(seg):
        t = 2 * math.pi * i / seg
        for dr, dx in prof:
            verts.append(c + axis * dx + u * math.cos(t) * (ru + dr) + w * math.sin(t) * (rw + dr))
    for i in range(seg):
        for k in range(P - 1):
            i0 = i * P + k
            j0 = ((i + 1) % seg) * P + k
            faces.append((i0, j0, j0 + 1, i0 + 1))
            mi.append(0)
        faces.append((i * P + P - 1, ((i + 1) % seg) * P + P - 1, ((i + 1) % seg) * P, i * P))   # parte de dentro
        mi.append(1)

    def add(vs, fs, m):
        off = len(verts)
        verts.extend(vs)
        faces.extend(tuple(x + off for x in f) for f in fs)
        mi.extend([m] * len(fs))

    top = c + u * (ru + th)
    # caixa da trava em cima, com a luz
    vs, fs = _oriented_box(top + u * 0.009, (axis, w, u), (0.052, 0.046, 0.022))
    add(vs, fs, 2)
    vs, fs = _oriented_box(top + u * 0.021 + w * 0.012, (axis, w, u), (0.012, 0.008, 0.004), bevel=0.4)
    add(vs, fs, 3)
    vs, fs = _oriented_box(top + u * 0.0205 - w * 0.008, (axis, w, u), (0.026, 0.012, 0.003), bevel=0.3)
    add(vs, fs, 1)                                              # fenda escura (fechadura)
    # parafusos nas duas bordas (a cada 60 graus)
    for i in range(6):
        t = 2 * math.pi * (i + 0.5) / 6
        rad = u * math.cos(t) + w * math.sin(t)
        if rad.dot(u) > 0.85:
            continue
        for side in (-1, 1):
            p = c + axis * side * (half - 0.007) + u * math.cos(t) * (ru + th + 0.002) + w * math.sin(t) * (rw + th + 0.002)
            vs, fs = _cylinder(p, rad, 0.0045, 0.006, sides=6)
            add(vs, fs, 2)
    # suporte embaixo (onde o cabo da máquina era preso) + elo quebrado + fios arrancados
    bottom = c - u * (ru + th)
    vs, fs = _oriented_box(bottom - u * 0.008, (axis, w, u), (0.034, 0.03, 0.018))
    add(vs, fs, 2)
    ring_c = bottom - u * 0.026
    link = []
    for k in range(14):                                         # elo aberto (3/4 de volta) pendurado
        t = math.radians(-40 + 260 * k / 13)
        link.append(ring_c + axis * math.cos(t) * 0.016 - u * (math.sin(t) * 0.014 + 0.004))
    vs, fs = tube(link, 0.004, sides=6)
    add(vs, fs, 0)
    for k, (dx, dw) in enumerate(((-0.01, 0.009), (0.004, -0.01), (0.012, 0.004))):
        s0 = bottom - u * 0.016 + axis * dx + w * dw
        pts = [s0, s0 - u * 0.012 + w * dw * 0.6, s0 - u * 0.03 + axis * (0.006 * (k - 1)) + w * dw,
               s0 - u * 0.05 + axis * (0.012 * (k - 1)) + w * dw * 1.6]
        vs, fs = tube(pts, 0.0022, sides=5)
        add(vs, fs, 4)
    obj = new_object("H19_Algema", verts, faces,
                     [mats["metal"], mats["dark"], mats["metal_dark"], mats["light"], mats["wire"]], mi)
    me = obj.data
    me.validate()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    _rigid(obj, arm, f"lowerarm_{TK}")
    return obj


# ---------- Ajustes feitos à mão pelo autor ----------
# Barba e código H-19 ajustados no Blender pelo autor ficam guardados em
# Protagonista_H19_ajustes.blend e substituem os gerados (para sobreviver a cada reconstrução).

ADJUST_FILE = r"C:\WorkSpace\ImNotAnAlien\Art\Blender\Protagonista_H19_ajustes.blend"


def apply_user_adjustments(arm):
    import os
    if not os.path.exists(ADJUST_FILE):
        return []
    with bpy.data.libraries.load(ADJUST_FILE, link=False) as (src, dst):
        dst.objects = [n for n in src.objects if n.endswith("_Ajuste")]
    done = []
    for ob in dst.objects:
        if ob is None:
            continue
        target = ob.name[:-len("_Ajuste")]
        old = bpy.data.objects.get(target)
        mats = list(old.data.materials) if old else []
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
        ob.name = target
        ob.data.name = target
        bpy.context.scene.collection.objects.link(ob)
        for g in list(ob.vertex_groups):
            if not any(g.index in [x.group for x in v.groups] for v in ob.data.vertices):
                ob.vertex_groups.remove(g)
        if mats:
            ob.data.materials.clear()
            for m in mats:
                ob.data.materials.append(m)
        ob.parent = arm
        ob.matrix_parent_inverse = arm.matrix_world.inverted()
        ob.modifiers.clear()
        ob.modifiers.new("Armature", 'ARMATURE').object = arm
        done.append(target)
    return done


# ---------- Gola do próprio tecido (versão final) ----------
# O tecido do macacão em volta do pescoço é "levantado" e vira a gola: os vértices que estão dentro
# do oval da gola sobem pela parede de fora (quanto mais perto do pescoço, mais alto), a borda do
# buraco do pescoço vira a borda de cima da gola, e dela nascem a parede de dentro (forro escuro)
# e o fundo em volta do pescoço. É tudo UMA malha só, sem emenda: em qualquer pose o tecido
# continua inteiro até a gola.

def build_collar_lifted(suit, arm):
    neck = bone_world(arm, "neck_01")
    C = Vector((neck.x, neck.y - 0.005, 0))
    rx, ry, hgt, th = COLLAR["rx"], COLLAR["ry"], COLLAR["height"], COLLAR["thick"]
    base_z = neck.z - COLLAR["drop"]
    mw = suit.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(suit.data)
    deform = bm.verts.layers.deform.active
    gi = {g.name: g.index for g in suit.vertex_groups}
    sp = gi["spine_03"]

    def polar(p):
        q = p - C
        return math.atan2(q.y / ry, q.x / rx), math.sqrt((q.x / rx) ** 2 + (q.y / ry) ** 2)

    # tecido embaixo da borda do oval (o "pé" da gola), suavizado em volta
    world = bm.copy()
    world.transform(mw)
    tree = BVHTree.FromBMesh(world)
    world.free()
    N = 96
    raw = []
    for i in range(N):
        t = 2 * math.pi * i / N
        q = C + Vector((math.cos(t) * rx, math.sin(t) * ry, 0))
        hit = tree.ray_cast(Vector((q.x, q.y, 2.2)), Vector((0, 0, -1)), 2.0)
        raw.append(hit[0].z if hit[0] is not None else base_z - 0.05)
    feet = [sum(raw[(i + k) % N] for k in range(-3, 4)) / 7.0 for i in range(N)]

    def circ(table, t):
        f = (t % (2 * math.pi)) / (2 * math.pi) * len(table)
        i0 = int(f) % len(table)
        return table[i0] + (table[(i0 + 1) % len(table)] - table[i0]) * (f - int(f))

    # borda do buraco do pescoço (laço limpo que já existe no macacão)
    hole = [v for v in bm.verts if v.is_boundary and polar(mw @ v.co)[1] < 0.75 and (mw @ v.co).z > base_z - 0.1]
    loop, prev, cur = [hole[0]], None, hole[0]
    hs = set(hole)
    while True:
        nxt = None
        for e in cur.link_edges:
            if e.is_boundary:
                o = e.other_vert(cur)
                if o is not prev and o in hs:
                    nxt = o
                    break
        if nxt is None or nxt is loop[0]:
            break
        loop.append(nxt)
        prev, cur = cur, nxt
    # raio do buraco em cada ângulo
    M = 72
    hr = [0.0] * M
    cnt = [0] * M
    for v in loop:
        a, r = polar(mw @ v.co)
        k = int(((a % (2 * math.pi)) / (2 * math.pi)) * M) % M
        hr[k] += r
        cnt[k] += 1
    known = [(k, hr[k] / cnt[k]) for k in range(M) if cnt[k]]
    hole_r = []
    for k in range(M):
        if cnt[k]:
            hole_r.append(hr[k] / cnt[k])
            continue
        # interpola com os vizinhos conhecidos
        best = sorted(known, key=lambda kv: min(abs(kv[0] - k), M - abs(kv[0] - k)))[:2]
        hole_r.append(sum(r for _, r in best) / len(best))

    # perfil da parede de fora: s = 0 no pé (tecido), s = 1 na borda de cima
    def outer_point(t, s):
        fz = circ(feet, t)
        ctrl = [(0.0, 0.0, fz), (0.22, 0.012, max(fz + 0.02, min(fz + 0.05, base_z + 0.01))),
                (0.5, 0.02, base_z + hgt * 0.45), (0.78, 0.022, base_z + hgt * 0.85),
                (0.92, 0.016, base_z + hgt), (1.0, 0.0, base_z + hgt * 1.04)]
        for (s0, d0, z0), (s1, d1, z1) in zip(ctrl, ctrl[1:]):
            if s <= s1:
                f = (s - s0) / (s1 - s0)
                dr, z = d0 + (d1 - d0) * f, z0 + (z1 - z0) * f
                break
        else:
            dr, z = ctrl[-1][1], ctrl[-1][2]
        rad = Vector((math.cos(t) / rx, math.sin(t) / ry, 0)).normalized()
        q = C + Vector((math.cos(t) * rx, math.sin(t) * ry, 0)) + rad * dr
        q.z = max(z, fz)
        return q

    lifted = {}
    loop_set = set(loop)
    for v in bm.verts:
        p = mw @ v.co
        a, r = polar(p)
        if r >= 1.0 or p.z < base_z - 0.2:
            continue
        rh = circ(hole_r, a)
        s_ = 1.0 if v in loop_set else max(0.0, min(0.999, (1.0 - r) / max(1e-3, 1.0 - rh)))
        v.co = inv @ outer_point(a, s_)
        lifted[v] = s_
        dv = v[deform]
        for k_ in list(dv.keys()):
            dv[k_] = dv[k_] * (1.0 - s_)
        dv[sp] = dv.get(sp, 0.0) + s_
    # anéis novos a partir da borda do buraco: parede de dentro e fundo em volta do pescoço
    inner = [(-th, hgt * 0.95), (-th - 0.004, hgt * 0.5), (-th, 0.0)]
    floor_r = (0.088, 0.075)
    cols = []
    for v in loop:
        a, _ = polar(mw @ v.co)
        rad = Vector((math.cos(a) / rx, math.sin(a) / ry, 0)).normalized()
        col = [v]
        pts = []
        for dr, dz in inner:
            q = C + Vector((math.cos(a) * rx, math.sin(a) * ry, 0)) + rad * dr
            q.z = base_z + dz
            pts.append(q)
        fl = C + Vector((math.cos(a) * floor_r[0], math.sin(a) * floor_r[1], 0))
        fl.z = base_z + 0.004
        pts.append(fl)
        for i, q in enumerate(pts):
            nv = bm.verts.new(inv @ q)
            dv = nv[deform]
            last = i == len(pts) - 1
            dv[sp] = 0.65 if last else 1.0
            if last and "neck_01" in gi:
                dv[gi["neck_01"]] = 0.35
            col.append(nv)
        cols.append(col)
    n = len(cols)
    new_faces = []
    for i in range(n):
        c0, c1 = cols[i], cols[(i + 1) % n]
        for r in range(len(c0) - 1):
            try:
                f = bm.faces.new((c0[r], c1[r], c1[r + 1], c0[r + 1]))
            except ValueError:
                continue
            f.material_index = 3
            f.smooth = True
            new_faces.append(f)
    # a parte levantada fica toda no marfim (sem faixa/zíper subindo pela gola)
    for f in bm.faces:
        if f not in new_faces and all(v in lifted and lifted[v] > 0.02 for v in f.verts):
            f.material_index = 0
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # alisa a parede levantada (os vértices vieram de lugares diferentes do ombro)
    wall = [v for v, s_ in lifted.items() if 0.02 < s_ < 0.98]
    for _ in range(2):
        bmesh.ops.smooth_vert(bm, verts=wall, factor=0.35, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(suit.data)
    bm.free()
    suit.data.update()
    return {"loop": n, "lifted": len(lifted)}
