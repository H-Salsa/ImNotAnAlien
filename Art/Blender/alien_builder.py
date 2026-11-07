# Construtor procedural do alienígena (Missão 15) — roda dentro do Blender.
# Uso no Blender (aba Scripting ou via conector):
#   exec(open(r"C:\WorkSpace\ImNotAnAlien\Art\Blender\alien_builder.py", encoding="utf-8").read())
#   rebuild_alien()
# Tudo é gerado a partir do corpo base da Quaternius (mesmo esqueleto do protagonista),
# então os aliens usam as mesmas animações no Unity.
import bpy, bmesh, math
from mathutils import Vector, Euler

# Perfil da cabeça, de baixo (pescoço) para cima: (altura z, meia-largura x, frente y, trás y).
# O personagem olha para +Y. O primeiro anel da cabeça é a própria borda do pescoço do corpo
# (a cabeça fica COSTURADA no corpo); estes anéis vêm logo acima dela.
HEAD_PROFILE = [
    (1.575, 0.040, 0.004, -0.072),   # pescoço fino
    (1.605, 0.050, 0.064, -0.064),   # embaixo do queixo
    (1.635, 0.066, 0.090, -0.062),   # mandíbula
    (1.675, 0.082, 0.100, -0.075),   # bochechas
    (1.715, 0.098, 0.104, -0.098),   # olhos
    (1.765, 0.130, 0.104, -0.140),
    (1.825, 0.154, 0.096, -0.178),
    (1.885, 0.150, 0.074, -0.192),
    (1.935, 0.126, 0.040, -0.175),
    (1.965, 0.098, 0.012, -0.140),
    (1.985, 0.066, -0.015, -0.110),
    (1.996, 0.034, -0.038, -0.086),
]

# Listras do crânio: uma coluna a cada "every" (48 colunas no total).
STRIPES = dict(every=6, above_eyes=0.045, below_top=0.03)

# Inclinação da cabeça para trás (o bulbo "cai" para trás, como na arte conceitual).
TILT = dict(degrees=20.0, pivot=(0.0, -0.035, 1.600), start=1.600, full=1.740)


# Valores acima são do corpo masculino (osso Head em z = 1.5998). Para outros corpos (feminino),
# tudo da cabeça é deslocado pela diferença de altura do osso Head (veja apply_body_offset).
BASE_HEAD_Z = 1.5998
OFFSET = dict(dz=0.0, head_scale=1.0)


def _smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def tilt_point(p, factor_z=None):
    """Gira o ponto para trás em torno do pivô (mais forte quanto mais alto)."""
    f = _smooth(TILT["start"], TILT["full"], p.z if factor_z is None else factor_z)
    if f <= 0:
        return Vector(p)
    a = math.radians(TILT["degrees"]) * f
    pv = Vector(TILT["pivot"])
    d = p - pv
    y = d.y * math.cos(a) - d.z * math.sin(a)
    z = d.y * math.sin(a) + d.z * math.cos(a)
    return pv + Vector((d.x, y, z))


def untilt_point(p):
    """Desfaz a inclinação total (vale para pontos do crânio, acima de TILT['full'])."""
    a = -math.radians(TILT["degrees"])
    pv = Vector(TILT["pivot"])
    d = p - pv
    y = d.y * math.cos(a) - d.z * math.sin(a)
    z = d.y * math.sin(a) + d.z * math.cos(a)
    return pv + Vector((d.x, y, z))


def tilt_dir(v, factor_z):
    f = _smooth(TILT["start"], TILT["full"], factor_z)
    a = math.radians(TILT["degrees"]) * f
    return Vector((v.x, v.y * math.cos(a) - v.z * math.sin(a), v.y * math.sin(a) + v.z * math.cos(a)))


def _catmull(p0, p1, p2, p3, t):
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                  + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)


def _sample(profile, steps=4):
    out, n = [], len(profile)
    for i in range(n - 1):
        for s in range(steps):
            t = s / steps
            out.append([_catmull(profile[max(i - 1, 0)][k], profile[i][k], profile[i + 1][k],
                                 profile[min(i + 2, n - 1)][k], t) for k in range(4)])
    out.append(list(profile[-1]))
    return out


def head_point(z, hx, yf, yb, theta):
    s, c = math.sin(theta), math.cos(theta)
    cy = (yf + yb) * 0.5  # centro do anel (os lados ficam no meio entre frente e trás)
    y = cy + (abs(c) ** 0.85) * ((yf if c >= 0 else yb) - cy)
    x = hx * math.copysign(abs(s) ** 0.9, s)
    return Vector((x, y, z))


def neck_boundary(body):
    """Borda aberta do pescoço do corpo (depois de apagar cabeça e pescoço), em ordem de ângulo."""
    mw = body.matrix_world
    bm = bmesh.new()
    bm.from_mesh(body.data)
    verts = {v for e in bm.edges if e.is_boundary for v in e.verts}
    pts = [(v.index, mw @ v.co) for v in verts if (mw @ v.co).z > 1.3 and abs((mw @ v.co).x) < 0.25]
    bm.free()
    center = sum((p for _, p in pts), Vector()) / len(pts)
    pts.sort(key=lambda ip: math.atan2(ip[1].x - center.x, ip[1].y - center.y))
    return pts, center


def build_head(body, name="AlienHead", profile=None):
    """Cabeça costurada na borda do pescoço do corpo. Use join_head() depois para unir."""
    profile = profile or HEAD_PROFILE
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)

    boundary, center = neck_boundary(body)
    seg = 48
    thetas = [-math.pi + 2 * math.pi * j / seg for j in range(seg)]

    bm = bmesh.new()
    ring0 = [bm.verts.new(p) for _, p in boundary]
    rings = []
    samples = _sample(profile, 4)
    for z, hx, yf, yb in samples:
        rings.append([bm.verts.new(tilt_point(head_point(z, hx, yf, yb, th))) for th in thetas])
    # Listras: faixas "de melão" (colunas) no crânio, da testa até perto do topo.
    stripe_from = EYE["z"] + STRIPES["above_eyes"]
    stripe_to = profile[-1][0] - STRIPES["below_top"]
    for k, (a, b) in enumerate(zip(rings, rings[1:])):
        z_mid = (samples[k][0] + samples[k + 1][0]) * 0.5
        for j in range(seg):
            f = bm.faces.new((a[j], a[(j + 1) % seg], b[(j + 1) % seg], b[j]))
            if stripe_from < z_mid < stripe_to and (j - seg // 2) % STRIPES["every"] == 0:
                f.material_index = 1

    # Costura: liga a borda do pescoço (vértices em ângulos irregulares) ao primeiro anel
    # (ângulos regulares) com triângulos, andando pelos dois em ordem de ângulo ("zíper").
    def ang(v):
        return math.atan2(v.co.x - center.x, v.co.y - center.y)
    lower = sorted(ring0, key=ang)
    upper = rings[0]
    la = [ang(v) for v in lower]
    ua = list(thetas)
    i = j = 0
    n_l, n_u = len(lower), len(upper)
    while i < n_l or j < n_u:
        a0, b0 = lower[i % n_l], upper[j % n_u]
        next_l = la[(i + 1) % n_l] + (2 * math.pi if i + 1 >= n_l else 0)
        next_u = ua[(j + 1) % n_u] + (2 * math.pi if j + 1 >= n_u else 0)
        if j >= n_u or (i < n_l and next_l <= next_u):
            bm.faces.new((a0, lower[(i + 1) % n_l], b0))
            i += 1
        else:
            bm.faces.new((a0, upper[(j + 1) % n_u], b0))
            j += 1
    rings.insert(0, ring0)
    z, hx, yf, yb = profile[-1]
    top = bm.verts.new(tilt_point(Vector((0, (yf + yb) / 2, z + 0.003))))
    for j in range(seg):
        bm.faces.new((rings[-1][j], rings[-1][(j + 1) % seg], top))
    bm.verts.index_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # deixa as normais para fora
    bm.normal_update()
    probe = max(bm.faces, key=lambda f: f.calc_center_median().z)
    if probe.normal.z < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for poly in me.polygons:
        poly.use_smooth = True
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)

    # Pesos: o anel de baixo copia os do corpo; daí para cima vai do pescoço para a cabeça.
    body_groups = {g.index: g.name for g in body.vertex_groups}
    for i, (vi, _) in enumerate(boundary):
        for g in body.data.vertices[vi].groups:
            name_g = body_groups[g.group]
            vg = obj.vertex_groups.get(name_g) or obj.vertex_groups.new(name=name_g)
            vg.add([i], g.weight, 'REPLACE')
    neck = obj.vertex_groups.get("neck_01") or obj.vertex_groups.new(name="neck_01")
    head = obj.vertex_groups.get("Head") or obj.vertex_groups.new(name="Head")
    for v in me.vertices[len(boundary):]:
        w = _smooth(1.585 + OFFSET["dz"], 1.650 + OFFSET["dz"], v.co.z)
        head.add([v.index], w, 'REPLACE')
        if w < 1:
            neck.add([v.index], 1 - w, 'REPLACE')
    return obj


def join_into(body, parts):
    """Une as peças no corpo e solda a costura do pescoço."""
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    with bpy.context.temp_override(active_object=body, selected_editable_objects=parts + [body]):
        bpy.ops.object.join()
    bm = bmesh.new()
    bm.from_mesh(body.data)
    mw = body.matrix_world
    dz = OFFSET["dz"]
    seam = [v for v in bm.verts if 1.45 + dz < (mw @ v.co).z < 1.6 + dz and abs((mw @ v.co).x) < 0.15]
    bmesh.ops.remove_doubles(bm, verts=seam, dist=0.0003)
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()


def set_view(rx, rz, loc=(0, 0, 1.75), dist=0.9):
    for area in bpy.context.screen.areas:
        if area.type == 'VIEW_3D':
            r3d = area.spaces.active.region_3d
            r3d.view_location = Vector(loc)
            r3d.view_distance = dist
            r3d.view_rotation = Euler((math.radians(rx), 0, math.radians(rz))).to_quaternion()


# ---------- Materiais ----------

def material(name, hex_color, roughness=0.6):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    h = hex_color.lstrip("#")
    rgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    mat.diffuse_color = (*lin, 1)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*lin, 1)
        bsdf.inputs["Roughness"].default_value = roughness
    return mat


def alien_materials():
    return {
        "skin": material("Alien_Pele", "#3FA59B"),
        "stripe": material("Alien_Pele_Listra", "#2C7F7A"),
        "white": material("Alien_Olho_Branco", "#F2EEE3", 0.25),
        "iris": material("Alien_Olho_Iris", "#E8922B", 0.25),
        "pupil": material("Alien_Olho_Pupila", "#141018", 0.15),
        "mouth": material("Alien_Boca", "#16262A", 0.5),
    }


# ---------- Olhos ----------
# Olho grande com pálpebra de cima (a inclinação da pálpebra dá a expressão).

EYE = dict(x=0.052, y=0.072, z=1.708, radius=0.036, outward_deg=14.0, lid_tilt_deg=-12.0, lid_cover=0.35)


def _tilt_rigid(p, ref_z):
    """Inclina como um bloco só (mesmo ângulo para todos os pontos), usando a altura de referência."""
    return tilt_point(p, factor_z=ref_z)


def _eye_dir(side):
    a = math.radians(EYE["outward_deg"]) * side
    return Vector((math.sin(a), math.cos(a), 0.0))


def build_eyes(mats, lid_tilt_deg=None):
    lid_tilt = EYE["lid_tilt_deg"] if lid_tilt_deg is None else lid_tilt_deg
    objs = []
    for side, tag in ((1, "R"), (-1, "L")):
        for n in (f"AlienEye_{tag}", f"AlienLid_{tag}"):
            old = bpy.data.objects.get(n)
            if old:
                bpy.data.objects.remove(old, do_unlink=True)
        center = Vector((EYE["x"] * side, EYE["y"], EYE["z"]))
        fwd = _eye_dir(side)

        # Globo ocular: branco, íris laranja e pupila, por ângulo a partir da frente.
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=48, v_segments=32, radius=EYE["radius"])
        me = bpy.data.meshes.new(f"AlienEye_{tag}")
        bm.to_mesh(me)
        bm.free()
        eye = bpy.data.objects.new(f"AlienEye_{tag}", me)
        bpy.context.scene.collection.objects.link(eye)
        for m in (mats["white"], mats["iris"], mats["pupil"]):
            me.materials.append(m)
        for poly in me.polygons:
            ang = math.degrees(poly.normal.angle(fwd))
            poly.material_index = 2 if ang < 16 else 1 if ang < 38 else 0
            poly.use_smooth = True
        for v in me.vertices:
            v.co = _tilt_rigid(v.co + center, EYE["z"])
        objs.append(eye)

        # Pálpebra: casca um pouco maior que o olho, só a parte de cima de um plano inclinado.
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=EYE["radius"] * 1.12)
        tilt = math.radians(lid_tilt) * side
        # normal do plano de corte: para cima, girada em torno do eixo da frente
        normal = Vector((math.sin(tilt), 0.0, math.cos(tilt)))
        cut = EYE["radius"] * 1.12 * (1.0 - 2.0 * EYE["lid_cover"])
        kill = [v for v in bm.verts if v.co.dot(normal) < cut]
        bmesh.ops.delete(bm, geom=kill, context='VERTS')
        me = bpy.data.meshes.new(f"AlienLid_{tag}")
        bm.to_mesh(me)
        bm.free()
        lid = bpy.data.objects.new(f"AlienLid_{tag}", me)
        bpy.context.scene.collection.objects.link(lid)
        me.materials.append(mats["skin"])
        for poly in me.polygons:
            poly.use_smooth = True
        for v in me.vertices:
            v.co = _tilt_rigid(v.co + center, EYE["z"])
        sol = lid.modifiers.new("Solidify", 'SOLIDIFY')
        sol.thickness = 0.003
        objs.append(lid)
    return objs


# ---------- Mãos de 3 dedos ----------
# Polegar + dois dedos grandes com ponta "gordinha". Usa os ossos do polegar, do indicador e do
# anelar (mais afastados = mão mais característica); médio e mindinho ficam sem malha.

FINGERS = ("thumb", "index", "ring")
HAND = dict(wrist=0.034, palm=0.036, finger_base=0.0165, finger_mid=0.0145, finger_tip=0.0215, tip_extra=0.024)


def bone_world(arm, name, tail=False):
    b = arm.data.bones[name]
    return arm.matrix_world @ (b.tail_local if tail else b.head_local)


def delete_body_part(body, groups, threshold=0.5):
    idx = {body.vertex_groups[g].index for g in groups if g in body.vertex_groups}
    bm = bmesh.new()
    bm.from_mesh(body.data)
    deform = bm.verts.layers.deform.active
    kill = [v for v in bm.verts if sum(w for gi, w in v[deform].items() if gi in idx) > threshold]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    return len(kill)


def hand_groups(side):
    names = [f"hand_{side}"]
    for f in ("thumb", "index", "middle", "ring", "pinky"):
        names += [f"{f}_0{i}_{side}" for i in (1, 2, 3)] + [f"{f}_04_leaf_{side}"]
    return names


def build_hand(arm, side, mats, wrist_info=None):
    """wrist_info = (centro, raio médio) da borda do antebraço: a mão começa ali e é costurada nela."""
    name = f"AlienHand_{side}"
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)

    wrist = bone_world(arm, f"hand_{side}")
    hand_tail = bone_world(arm, f"hand_{side}", tail=True)
    hand_dir = (hand_tail - wrist).normalized()

    verts, radii, flat, edges = [], [], [], []
    k = OFFSET["head_scale"]  # mão um pouco menor no corpo feminino

    def add(p, r, f=1.0):
        verts.append(Vector(p))
        radii.append(r)
        flat.append(f)
        return len(verts) - 1

    if wrist_info:
        # Um pedacinho de "pulso" redondo antes da palma: a costura fica num lugar redondo,
        # longe da palma achatada (evita dobras no pulso).
        w = add(wrist_info[0], wrist_info[1], 0.85)
        w2 = add(wrist_info[0] + hand_dir * 0.024, wrist_info[1] * 0.95, 0.8)
        edges.append((w, w2))
    else:
        w2 = add(wrist - hand_dir * 0.045, HAND["wrist"] * k, 0.85)
    bases = [bone_world(arm, f"{f}_01_{side}") for f in ("index", "ring")]
    palm_center = (wrist + (bases[0] + bases[1]) * 0.5) * 0.5
    if wrist_info and (palm_center - wrist_info[0]).dot(hand_dir) < 0.045:
        palm_center = wrist_info[0] + hand_dir * 0.045 + (palm_center - wrist_info[0] - hand_dir * (palm_center - wrist_info[0]).dot(hand_dir))
    p = add(palm_center, HAND["palm"] * k, 0.6)
    edges.append((w2, p))

    for f in FINGERS:
        chain = [bone_world(arm, f"{f}_0{i}_{side}") for i in (1, 2, 3)]
        chain.append(bone_world(arm, f"{f}_04_leaf_{side}"))
        tip_dir = (chain[-1] - chain[-2]).normalized()
        chain.append(chain[-1] + tip_dir * HAND["tip_extra"])
        prev = p
        n = len(chain)
        for i, c in enumerate(chain):
            t = i / (n - 1)
            if i == n - 1:
                r = HAND["finger_tip"] * 0.7
            elif i == n - 2:
                r = HAND["finger_tip"]
            else:
                r = HAND["finger_base"] + (HAND["finger_mid"] - HAND["finger_base"]) * t
            if f == "thumb":
                r *= 1.05
            v = add(c, r * k)
            edges.append((prev, v))
            prev = v

    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], edges, [])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    skin = obj.modifiers.new("Skin", 'SKIN')
    skin.use_smooth_shade = True
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = (radii[i], radii[i] * flat[i])
        sv.use_root = (i == 0)
    sub = obj.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = sub.render_levels = 2
    bpy.context.view_layer.objects.active = obj
    for m in list(obj.modifiers):
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.object.modifier_apply(modifier=m.name)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    obj.data.materials.append(mats["skin"])
    if wrist_info:
        _cut_wrist_cap(obj, wrist_info[0], hand_dir, 0.012)
    weight_by_bones(obj, arm, hand_bone_list(side))
    return obj


def _cut_wrist_cap(obj, center, axis, offset):
    """Apaga a 'tampa' arredondada do pulso, deixando a mão aberta para ser costurada."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    kill = [v for v in bm.verts if (v.co - center).dot(axis) < offset]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()


def _boundary_loops(bm):
    """Grupos de vértices de borda ligados entre si (cada buraco aberto é um grupo)."""
    edges = [e for e in bm.edges if e.is_boundary]
    verts = {v for e in edges for v in e.verts}
    loops, seen = [], set()
    for v in verts:
        if v in seen:
            continue
        stack, loop = [v], []
        seen.add(v)
        while stack:
            x = stack.pop()
            loop.append(x)
            for e in x.link_edges:
                if e.is_boundary:
                    o = e.other_vert(x)
                    if o not in seen:
                        seen.add(o)
                        stack.append(o)
        loops.append(loop)
    return loops


def wrist_boundary(body, arm, side):
    """Borda aberta do antebraço (onde a mão original foi apagada): centro e raio médio, em coordenadas do mundo."""
    mw = body.matrix_world
    wrist = bone_world(arm, f"hand_{side}")
    bm = bmesh.new()
    bm.from_mesh(body.data)
    best = None
    for loop in _boundary_loops(bm):
        pts = [mw @ v.co for v in loop]
        c = sum(pts, Vector()) / len(pts)
        d = (c - wrist).length
        if best is None or d < best[0]:
            best = (d, c, sum((p - c).length for p in pts) / len(pts))
    bm.free()
    return best[1], best[2]


def _zipper(bm, lower, upper, center, axis):
    """Liga dois anéis (com números diferentes de vértices) com triângulos, em ordem de ângulo."""
    u = axis.orthogonal().normalized()
    w = axis.cross(u).normalized()

    def ang(v):
        d = v.co - center
        return math.atan2(d.dot(w), d.dot(u))
    lower = sorted(lower, key=ang)
    upper = sorted(upper, key=ang)
    la, ua = [ang(v) for v in lower], [ang(v) for v in upper]
    i = j = 0
    nl, nu = len(lower), len(upper)
    while i < nl or j < nu:
        a0, b0 = lower[i % nl], upper[j % nu]
        next_l = la[(i + 1) % nl] + (2 * math.pi if i + 1 >= nl else 0)
        next_u = ua[(j + 1) % nu] + (2 * math.pi if j + 1 >= nu else 0)
        try:
            if j >= nu or (i < nl and next_l <= next_u):
                bm.faces.new((a0, lower[(i + 1) % nl], b0))
                i += 1
            else:
                bm.faces.new((a0, upper[(j + 1) % nu], b0))
                j += 1
        except ValueError:
            if j >= nu or (i < nl and next_l <= next_u):
                i += 1
            else:
                j += 1


def stitch_hands(body, arm):
    """Depois de unir tudo: costura cada mão na borda do antebraço."""
    mw = body.matrix_world
    inv = mw.inverted()
    bm = bmesh.new()
    bm.from_mesh(body.data)
    loops = _boundary_loops(bm)
    for side in ("l", "r"):
        wrist = inv @ bone_world(arm, f"hand_{side}")
        axis = ((inv @ bone_world(arm, f"hand_{side}", tail=True)) - wrist).normalized()
        near = []
        for loop in loops:
            c = sum((v.co for v in loop), Vector()) / len(loop)
            near.append(((c - wrist).length, c, loop))
        near.sort(key=lambda t: t[0])
        if len(near) < 2:
            continue
        (_, c1, l1), (_, c2, l2) = near[0], near[1]
        arm_loop, hand_loop = (l1, l2) if (c1 - wrist).dot(axis) < (c2 - wrist).dot(axis) else (l2, l1)
        _zipper(bm, arm_loop, hand_loop, (c1 + c2) * 0.5, axis)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()


def hand_bone_list(side):
    bones = [f"lowerarm_{side}", f"hand_{side}"]
    for f in FINGERS:
        bones += [f"{f}_0{i}_{side}" for i in (1, 2, 3)]
    return bones


def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


def weight_by_bones(obj, arm, bones, blend=2):
    """Pesos pelo osso mais próximo (mistura suave entre os dois mais próximos)."""
    segs = []
    for b in bones:
        bone = arm.data.bones[b]
        a = arm.matrix_world @ bone.head_local
        t = arm.matrix_world @ bone.tail_local
        if b.endswith(tuple(f"_03_{s}" for s in "lr")):
            t = t + (t - a).normalized() * 0.04  # ponta do dedo segue a última falange
        segs.append((b, a, t))
    groups = {b: (obj.vertex_groups.get(b) or obj.vertex_groups.new(name=b)) for b in bones}
    mw = obj.matrix_world
    for v in obj.data.vertices:
        p = mw @ v.co
        d = sorted(((_seg_dist(p, a, t), b) for b, a, t in segs))[:blend]
        inv = [1.0 / max(x, 1e-4) ** 2 for x, _ in d]
        total = sum(inv)
        for (dist, b), wgt in zip(d, inv):
            groups[b].add([v.index], wgt / total, 'REPLACE')


# ---------- Corpo mais magro ----------
# O corpo base é musculoso ("super-herói"). Os aliens são magrelos: cada vértice é puxado
# na direção do osso que o controla, com um fator por região (1 = igual, 0.8 = 20% mais fino).

SLIM = {
    "neck_01": 0.62, "spine_03": 0.84, "spine_02": 0.86, "spine_01": 0.88, "pelvis": 0.9,
    "clavicle": 0.82, "upperarm": 0.74, "lowerarm": 0.78,
    "thigh": 0.8, "calf": 0.82, "foot": 1.0, "ball": 1.0,
}


def _slim_factor(bone_name):
    for key, k in SLIM.items():
        if bone_name == key or bone_name.startswith(key + "_"):
            return k
    return 1.0


def slim_body(body, arm):
    segs = {}
    for b in arm.data.bones:
        segs[b.name] = (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
    names = {g.index: g.name for g in body.vertex_groups}
    mw = body.matrix_world
    inv = mw.inverted()
    for v in body.data.vertices:
        p = mw @ v.co
        total, acc = 0.0, Vector((0, 0, 0))
        for g in v.groups:
            n = names.get(g.group)
            if n not in segs or g.weight <= 0:
                continue
            a, t = segs[n]
            ab = t - a
            u = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
            c = a + ab * u
            acc += (c + (p - c) * _slim_factor(n)) * g.weight
            total += g.weight
        if total > 0:
            v.co = inv @ (acc / total)
    body.data.update()


# ---------- Nariz e boca ----------
# Desenhados "por cima" da pele: posição achada lançando um raio contra o rosto.

FACE = dict(nose_z=1.664, nose_x=0.010, mouth_z=1.628, mouth_half=0.024, mouth_curve=-0.003)


def _hit_face(head, x, z):
    origin = tilt_point(Vector((x, 0.4, z)), factor_z=z)
    direction = tilt_dir(Vector((0, -1, 0)), z)
    ok, loc, normal, _ = head.ray_cast(origin, direction)
    return (loc, normal) if ok else (None, None)


def build_face_details(head, mats):
    for n in ("AlienNose", "AlienMouth"):
        old = bpy.data.objects.get(n)
        if old:
            bpy.data.objects.remove(old, do_unlink=True)
    dark = mats["mouth"]

    # Nariz: duas fendinhas escuras, um pouco abertas para fora.
    bm = bmesh.new()
    for side in (1, -1):
        loc, normal = _hit_face(head, FACE["nose_x"] * side, FACE["nose_z"])
        if loc is None:
            continue
        geom = bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=8, radius=1.0)
        vs = geom["verts"]
        rot = normal.to_track_quat('Y', 'Z').to_matrix().to_4x4()
        spin = Euler((0, math.radians(18 * side), 0)).to_matrix().to_4x4()
        scale = Euler((0, 0, 0)).to_matrix().to_4x4()
        for v in vs:
            local = Vector((v.co.x * 0.0026, v.co.y * 0.0018, v.co.z * 0.0068))
            v.co = loc + (rot @ spin @ local.to_4d()).to_3d() - normal * 0.0006
    me = bpy.data.meshes.new("AlienNose")
    bm.to_mesh(me)
    bm.free()
    nose = bpy.data.objects.new("AlienNose", me)
    bpy.context.scene.collection.objects.link(nose)
    me.materials.append(dark)

    # Boca: uma linha curva e fina que acompanha a curvatura do rosto.
    pts, radii = [], []
    steps = 12
    for i in range(steps + 1):
        u = -1 + 2 * i / steps
        x = FACE["mouth_half"] * u
        z = FACE["mouth_z"] + FACE["mouth_curve"] * u * u
        loc, normal = _hit_face(head, x, z)
        if loc is None:
            continue
        pts.append(loc + normal * 0.0004)
        radii.append(0.0024 * (1 - 0.55 * abs(u) ** 2))
    me = bpy.data.meshes.new("AlienMouth")
    me.from_pydata([tuple(p) for p in pts], [(i, i + 1) for i in range(len(pts) - 1)], [])
    mouth = bpy.data.objects.new("AlienMouth", me)
    bpy.context.scene.collection.objects.link(mouth)
    skin = mouth.modifiers.new("Skin", 'SKIN')
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = (radii[i], radii[i] * 0.8)
        sv.use_root = (i == 0)
    sub = mouth.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = 1
    for m in list(mouth.modifiers):
        with bpy.context.temp_override(object=mouth, active_object=mouth):
            bpy.ops.object.modifier_apply(modifier=m.name)
    for poly in mouth.data.polygons:
        poly.use_smooth = True
    mouth.data.materials.append(dark)

    for o in (nose, mouth):
        g = o.vertex_groups.new(name="Head")
        g.add([v.index for v in o.data.vertices], 1.0, 'REPLACE')
    return nose, mouth


def _weight_all(obj, group):
    g = obj.vertex_groups.get(group) or obj.vertex_groups.new(name=group)
    g.add([v.index for v in obj.data.vertices], 1.0, 'REPLACE')


def _remove_small_islands(obj, min_verts=60):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    seen, small = set(), []
    for v in bm.verts:
        if v.index in seen:
            continue
        stack, island = [v], []
        seen.add(v.index)
        while stack:
            x = stack.pop()
            island.append(x)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen:
                    seen.add(o.index)
                    stack.append(o)
        if len(island) < min_verts:
            small += island
    bmesh.ops.delete(bm, geom=small, context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()


QUATERNIUS = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Quaternius\Base"
BASES = {"m": QUATERNIUS + r"\Superhero_Male_FullBody.fbx", "f": QUATERNIUS + r"\Superhero_Female_FullBody.fbx"}
# Ajustes por corpo: cabeça um pouco menor na feminina e corpo menos afinado (ela já é mais magra).
GENDER = {
    "m": dict(head_scale=1.0, slim=1.0),
    "f": dict(head_scale=0.95, slim=0.55),
}

_BASE_PROFILE = [tuple(r) for r in HEAD_PROFILE]
_BASE_TILT = dict(TILT)
_BASE_EYE = dict(EYE)
_BASE_FACE = dict(FACE)
_BASE_SLIM = dict(SLIM)


def setup_base(gender):
    """Num arquivo VAZIO do Blender: importa o corpo base (m/f) e prepara os nomes usados aqui.
    Rode bpy.ops.wm.read_homefile(use_empty=True) numa chamada separada antes."""
    bpy.ops.import_scene.fbx(filepath=BASES[gender])
    for n in ("Eyes", "Eyebrows"):
        o = bpy.data.objects.get(n)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)
    body = next(o for o in bpy.data.objects if o.type == 'MESH')
    body.name = "Body"
    orig = body.copy()
    orig.data = body.data.copy()
    orig.name = "Body_Original"
    bpy.context.scene.collection.objects.link(orig)
    orig.hide_set(True)
    orig.hide_render = True
    bpy.context.scene["alien_gender"] = gender
    return body


def apply_body_offset(arm, gender):
    """Ajusta a cabeça, olhos, rosto e afinamento para o corpo carregado."""
    dz = bone_world(arm, "Head").z - BASE_HEAD_Z
    g = GENDER[gender]
    k = g["head_scale"]
    OFFSET["dz"] = dz
    OFFSET["head_scale"] = k
    base_z = _BASE_PROFILE[0][0]
    HEAD_PROFILE[:] = [(base_z + (z - base_z) * k + dz, hx * k, yf * k, yb * k) for z, hx, yf, yb in _BASE_PROFILE]
    TILT.update(_BASE_TILT)
    TILT["pivot"] = (_BASE_TILT["pivot"][0], _BASE_TILT["pivot"][1], _BASE_TILT["pivot"][2] + dz)
    TILT["start"] = _BASE_TILT["start"] + dz
    TILT["full"] = _BASE_TILT["full"] + dz
    EYE.update(_BASE_EYE)
    EYE["z"] = base_z + (_BASE_EYE["z"] - base_z) * k + dz
    EYE["y"] = _BASE_EYE["y"] * k
    EYE["x"] = _BASE_EYE["x"] * k
    FACE.update(_BASE_FACE)
    FACE["nose_z"] = base_z + (_BASE_FACE["nose_z"] - base_z) * k + dz
    FACE["mouth_z"] = base_z + (_BASE_FACE["mouth_z"] - base_z) * k + dz
    for key, v in _BASE_SLIM.items():
        SLIM[key] = 1.0 - (1.0 - v) * g["slim"]


def rebuild_alien():
    """Refaz o alien inteiro a partir do corpo original (Body_Original)."""
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["Body"]
    orig = bpy.data.objects["Body_Original"]
    apply_body_offset(arm, bpy.context.scene.get("alien_gender", "m"))
    mats = alien_materials()
    for n in ("AlienHead", "AlienEye_R", "AlienEye_L", "AlienLid_R", "AlienLid_L", "AlienHand_l", "AlienHand_r",
              "AlienNose", "AlienMouth"):
        o = bpy.data.objects.get(n)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)

    body.data = orig.data.copy()
    body.data.name = "AlienBody"
    body.data.materials.clear()
    body.data.materials.append(mats["skin"])
    delete_body_part(body, ["Head", "neck_01"], 0.5)
    for s in ("l", "r"):
        delete_body_part(body, hand_groups(s), 0.25)
        # Os pés ficam sempre dentro do tênis/bota: apagados para não atravessarem o calçado.
        delete_body_part(body, [f"foot_{s}", f"ball_{s}", f"ball_leaf_{s}"], 0.5)
    _remove_small_islands(body)
    slim_body(body, arm)

    head = build_head(body)
    head.data.materials.append(mats["skin"])
    head.data.materials.append(mats["stripe"])
    hands = [build_hand(arm, s, mats, wrist_boundary(body, arm, s)) for s in ("l", "r")]
    eyes = build_eyes(mats)
    for o in eyes:
        _weight_all(o, "Head")
    nose, mouth = build_face_details(head, mats)
    join_into(body, [head] + hands)
    stitch_hands(body, arm)
    fill_small_holes(body)
    smooth_seams(body, arm)
    return body


def smooth_seams(body, arm, radius=0.03, repeat=4):
    """Suaviza as costuras do pulso e do pescoço para não aparecer o 'vinco'."""
    inv = body.matrix_world.inverted()
    centers = [inv @ bone_world(arm, f"hand_{s}") for s in ("l", "r")]
    centers.append(inv @ bone_world(arm, "neck_01"))
    bm = bmesh.new()
    bm.from_mesh(body.data)
    verts = [v for v in bm.verts if any((v.co - c).length < radius * (1.6 if i == 2 else 1.0)
                                        for i, c in enumerate(centers))]
    for _ in range(repeat):
        bmesh.ops.smooth_vert(bm, verts=verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()


def fill_small_holes(body, max_sides=16):
    """Tapa buraquinhos que sobram nas costuras."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    # Arestas com 3 faces (sobra da costura): apaga o triângulo menor e tapa o buraco depois.
    extra = set()
    for e in bm.edges:
        if len(e.link_faces) > 2:
            extra.add(min(e.link_faces, key=lambda f: f.calc_area()))
    if extra:
        bmesh.ops.delete(bm, geom=list(extra), context='FACES_ONLY')
    edges = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.holes_fill(bm, edges=edges, sides=max_sides)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()


# ---------- Exportar para o Unity ----------

# ---------- Expressões (shape keys / blend shapes no Unity) ----------
# "Apavorado": olhos crescem, pálpebras sobem e a boca ABRE num grito (oval escuro, a linha vira o lábio de cima).
# "Bravo": pálpebras descem e inclinam para dentro; cantos da boca bem para baixo.
EXPRESSIONS = {
    "Apavorado": dict(lid_lift=42.0, lid_tilt=0.0, mouth_center=0.0, mouth_corners=0.0, mouth_width=1.0,
                      mouth_wave=0.0, eye_scale=1.2, scream=dict(half_w=0.024, half_h=0.024, drop=0.012)),
    "Bravo": dict(lid_lift=-22.0, lid_tilt=-32.0, mouth_center=0.002, mouth_corners=-0.014, mouth_width=0.85,
                  mouth_wave=0.0, eye_scale=0.95),
}


def _rotate_about(p, pivot, axis, degrees):
    from mathutils import Matrix
    m = Matrix.Rotation(math.radians(degrees), 3, axis.normalized())
    return pivot + m @ (p - pivot)


def add_expressions(arm, body, parts):
    """Cria as shape keys nas pálpebras e na boca (e chaves vazias no corpo) antes de unir tudo."""
    apply_body_offset(arm, bpy.context.scene.get("alien_gender", "m"))
    _FACE_TREE.clear()
    mouth_line = next((o for o in parts if o.name == "AlienMouth"), None)
    scream_obj = None
    if mouth_line is not None:
        scream_obj = _make_scream_disk(mouth_line)
        parts.append(scream_obj)
    names = ["Basis"] + list(EXPRESSIONS)
    for obj in [body] + parts:
        if obj.data.shape_keys is None:
            obj.shape_key_add(name="Basis", from_mix=False)
        for n in names[1:]:
            if obj.data.shape_keys.key_blocks.get(n) is None:
                obj.shape_key_add(name=n, from_mix=False)

    right = Vector((1, 0, 0))
    up = tilt_dir(Vector((0, 0, 1)), EYE["z"]).normalized()
    fwd = tilt_dir(Vector((0, 1, 0)), EYE["z"]).normalized()
    for obj in parts:
        keys = obj.data.shape_keys.key_blocks
        mw = obj.matrix_world
        inv = mw.inverted()
        base = [mw @ v.co for v in obj.data.vertices]
        if obj.name.startswith("AlienEye_"):
            side = 1 if obj.name.endswith("_R") else -1
            center = _tilt_rigid(Vector((EYE["x"] * side, EYE["y"], EYE["z"])), EYE["z"])
            for n, e in EXPRESSIONS.items():
                kb = keys[n]
                for i, p in enumerate(base):
                    kb.data[i].co = inv @ (center + (p - center) * e["eye_scale"])
        elif obj.name.startswith("AlienLid_"):
            side = 1 if obj.name.endswith("_R") else -1
            center = _tilt_rigid(Vector((EYE["x"] * side, EYE["y"], EYE["z"])), EYE["z"])
            for n, e in EXPRESSIONS.items():
                kb = keys[n]
                for i, p in enumerate(base):
                    p = center + (p - center) * e["eye_scale"]  # a pálpebra acompanha o olho
                    q = _rotate_about(p, center, right, e["lid_lift"])
                    q = _rotate_about(q, center, fwd, -side * e["lid_tilt"])
                    kb.data[i].co = inv @ q
        elif obj.name == "AlienMouthOpen":
            # Base e "Bravo": encolhido num ponto (invisível). "Apavorado": oval aberto no rosto.
            c = obj["mouth_center"]
            c = Vector(c)
            half_n = len(base) // 2
            ring = half_n - 1
            for n, e in EXPRESSIONS.items():
                kb = keys[n]
                sc = e.get("scream")
                for i in range(len(base)):
                    j = i % half_n
                    if sc is None:
                        # fechado: um ovalzinho escondido dentro da cabeça (pontos diferentes, para o
                        # Unity não "soldar" os vértices e estragar o oval)
                        a = 2 * math.pi * (j - 1) / ring if j else 0.0
                        q = c + right * (math.cos(a) * 0.004 * (j > 0)) + up * (math.sin(a) * 0.003 * (j > 0)) \
                            - Vector((0, 0.012 + 0.001 * (i >= half_n), 0))
                        kb.data[i].co = inv @ q
                        continue
                    if j == 0:
                        q = _scream_point(body, c, 0.0, 0.0, sc, right, up, fwd, depth=0.002)
                    else:
                        a = 2 * math.pi * (j - 1) / ring
                        q = _scream_point(body, c, math.cos(a), math.sin(a), sc, right, up, fwd)
                    kb.data[i].co = inv @ q
        elif obj.name == "AlienMouth":
            c = sum(base, Vector()) / len(base)
            half = max(abs((p - c).dot(right)) for p in base) or 0.02
            for n, e in EXPRESSIONS.items():
                kb = keys[n]
                sc = e.get("scream")
                if sc is not None:
                    # a linha da boca vira o lábio de cima, contornando a metade de cima do oval
                    for i, p in enumerate(base):
                        d = p - c
                        u = max(-1.0, min(1.0, d.dot(right) / half))
                        ang = math.pi * (1 - (u + 1) / 2)          # u=-1 -> esquerda, u=1 -> direita
                        spine = c + right * d.dot(right)
                        rest = p - spine                             # grossura do tubo
                        q = _scream_point(body, c, math.cos(ang), math.sin(ang), sc, right, up, fwd) + rest
                        kb.data[i].co = inv @ q
                    continue
                for i, p in enumerate(base):
                    d = p - c
                    u = d.dot(right) / half
                    q = c + (d - right * d.dot(right) * (1 - e["mouth_width"]))
                    q += up * (e["mouth_center"] * (1 - u * u) + e["mouth_corners"] * u * u
                               + e["mouth_wave"] * math.sin(u * math.pi * 2.5))  # boca tremida (medo)
                    kb.data[i].co = inv @ q


def _make_scream_disk(mouth_line, segments=28):
    """Oval escuro da boca aberta (começa encolhido num ponto; a shape key abre)."""
    old = bpy.data.objects.get("AlienMouthOpen")
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    mw = mouth_line.matrix_world
    pts = [mw @ v.co for v in mouth_line.data.vertices]
    c = sum(pts, Vector()) / len(pts)
    # Dois "lados" com vértices próprios (no Unity só a frente de cada face aparece; assim o oval
    # aparece de qualquer jeito).
    n = segments + 1
    up_m = tilt_dir(Vector((0, 0, 1)), c.z).normalized()
    verts = []
    for side in range(2):
        verts.append(tuple(c - Vector((0, 0.012 + 0.001 * side, 0))))
        for i in range(segments):
            a = 2 * math.pi * i / segments
            verts.append(tuple(c + Vector((math.cos(a) * 0.004, 0, 0)) + up_m * (math.sin(a) * 0.003)
                               - Vector((0, 0.012 + 0.001 * side, 0))))
    faces = [(0, 1 + i, 1 + (i + 1) % segments) for i in range(segments)]
    faces += [(n, n + 1 + (i + 1) % segments, n + 1 + i) for i in range(segments)]
    me = bpy.data.meshes.new("AlienMouthOpen")
    me.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new("AlienMouthOpen", me)
    bpy.context.scene.collection.objects.link(obj)
    obj["mouth_center"] = list(c)
    me.materials.append(alien_materials()["mouth"])
    g = obj.vertex_groups.new(name="Head")
    g.add([v.index for v in me.vertices], 1.0, 'REPLACE')
    return obj


_FACE_TREE = {}


def _face_tree(body):
    from mathutils.bvhtree import BVHTree
    key = body.name
    if key not in _FACE_TREE:
        bm = bmesh.new()
        bm.from_mesh(body.data)
        bm.transform(body.matrix_world)
        _FACE_TREE[key] = BVHTree.FromBMesh(bm)
        bm.free()
    return _FACE_TREE[key]


def _scream_point(body, c, cx, cy, sc, right, up, fwd, depth=0.0):
    """Ponto do oval do grito projetado na superfície do rosto (um pouco para fora)."""
    # Na altura da boca o rosto quase não é inclinado: usa a vertical e um raio horizontal.
    up_m = tilt_dir(Vector((0, 0, 1)), c.z).normalized()
    target = c + right * (cx * sc["half_w"]) + up_m * (cy * sc["half_h"] - sc["drop"])
    hit = _face_tree(body).ray_cast(target + Vector((0, 0.08, 0)), Vector((0, -1, 0)), 0.2)
    if hit[0] is None:
        return target
    # empurra para a frente (o normal da cabeça costurada pode vir invertido)
    return hit[0] + Vector((0, 0.003 - depth, 0))


EXPORT_DIR = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Aliens"
EXPORT_NAMES = {"m": "Alien_Masculino.fbx", "f": "Alien_Feminino.fbx"}


def export_alien(path=None):
    """Une olhos, pálpebras, nariz e boca no corpo e exporta Armature + corpo em FBX."""
    import os
    if path is None:
        path = os.path.join(EXPORT_DIR, EXPORT_NAMES[bpy.context.scene.get("alien_gender", "m")])
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["Body"]
    parts = []
    for n in ("AlienEye_R", "AlienEye_L", "AlienLid_R", "AlienLid_L", "AlienNose", "AlienMouth"):
        o = bpy.data.objects.get(n)
        if o is None:
            continue
        for m in list(o.modifiers):
            with bpy.context.temp_override(object=o, active_object=o):
                bpy.ops.object.modifier_apply(modifier=m.name)
        parts.append(o)
    add_expressions(arm, body, parts)
    if parts:
        bpy.ops.object.select_all(action='DESELECT')
        for p in parts:
            p.hide_set(False)
            p.select_set(True)
        body.select_set(True)
        bpy.context.view_layer.objects.active = body
        with bpy.context.temp_override(active_object=body, selected_editable_objects=parts + [body]):
            bpy.ops.object.join()

    os.makedirs(os.path.dirname(path), exist_ok=True)
    garments = [bpy.data.objects[n] for n in GARMENTS_CIVIL + GARMENTS_POLICE if n in bpy.data.objects]
    arm.hide_set(False)
    body.hide_set(False)
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    body.select_set(True)
    for g in garments:
        g.hide_set(False)
        g.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', use_armature_deform_only=False)
    arm.hide_set(True)
    for g in garments:
        g.hide_set(True)
    return path


# ---------- Roupas ----------
# Cada peça é uma "casca" tirada do próprio corpo: as faces da região escolhida são copiadas
# (com os mesmos pesos dos ossos), afastadas um pouco da pele e suavizadas. Assim a roupa dobra
# junto com as animações. Cada peça é um objeto separado (o Unity liga/desliga cada uma).
# Civis: material "Roupa_Civil" (branco, o Unity pinta cada peça de uma cor).
# Polícia: azul-marinho, equipamento preto e detalhes ciano brilhantes (D20).

TORSO = ("spine_01", "spine_02", "spine_03", "clavicle_l", "clavicle_r", "pelvis")
GARMENTS_CIVIL = ("Camiseta", "Regata", "MangaLonga", "Calca", "Bermuda", "Tenis")
GARMENTS_POLICE = ("Pol_Macacao", "Pol_Colete", "Pol_Cinto", "Pol_Coldre", "Pol_Botas", "Pol_Bone", "Pol_Capacete")


def clothing_materials():
    return {
        "civil": material("Roupa_Civil", "#F2F2F2", 0.8),
        "uniform": material("Policia_Uniforme", "#1E2A4A", 0.75),
        "gear": material("Policia_Equipamento", "#1A1A1F", 0.6),
        "light": material("Policia_Luz", "#3FF2FF", 0.3),
    }


class _Rig:
    """Medidas do corpo atual tiradas dos ossos (funciona para o masculino e o feminino)."""

    def __init__(self, arm):
        self.arm = arm
        self.waist = bone_world(arm, "spine_01").z + 0.02
        self.collar = bone_world(arm, "neck_01").z - 0.015
        self.chest = (bone_world(arm, "spine_02").z + bone_world(arm, "spine_03").z) * 0.5 + 0.03
        self.segs = {b.name: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local) for b in arm.data.bones}
        self.neck = bone_world(arm, "neck_01")

    def in_neck(self, p, radius=0.075):
        """Gola redonda: só tira a região em volta do pescoço (os ombros continuam cobertos)."""
        flat = Vector((p.x - self.neck.x, p.y - self.neck.y, 0.0))
        return p.z > self.collar - 0.03 and flat.length < radius * OFFSET["head_scale"]

    def t(self, bone, p):
        """0 no começo do osso, 1 no fim."""
        a, b = self.segs[bone]
        ab = b - a
        return (p - a).dot(ab) / max(ab.length_squared, 1e-12)


def _weights(dv, names):
    return {names[i]: w for i, w in dv.items() if i in names}


def _sum(w, prefixes):
    return sum(v for k, v in w.items() if k in prefixes or any(k.startswith(p + "_") for p in prefixes))


def _dominant(w):
    return max(w.items(), key=lambda kv: kv[1])[0] if w else ""


def make_shell(body, arm, name, keep, offset, mat, smooth=2, flat_sole=False, face_mat=None, mats_extra=(),
               border_smooth=6):
    """keep(p_mundo, pesos) -> bool para cada vértice. Faces com todos os vértices aceitos viram a peça."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    mw = body.matrix_world
    names = {g.index: g.name for g in body.vertex_groups}
    bm = bmesh.new()
    bm.from_mesh(body.data)
    deform = bm.verts.layers.deform.active
    keep_set = {v for v in bm.verts if keep(mw @ v.co, _weights(v[deform], names))}
    drop = [f for f in bm.faces if not all(v in keep_set for v in f.verts)]
    bmesh.ops.delete(bm, geom=drop, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    # Pontas soltas na borda (faces presas por um lado só) viram "espinhos" depois de afastar
    # e suavizar: apaga essas faces algumas vezes.
    for _ in range(3):
        tips = [f for f in bm.faces if sum(1 for e in f.edges if e.is_boundary) >= 3]
        if not tips:
            break
        bmesh.ops.delete(bm, geom=tips, context='FACES')
        loose = [v for v in bm.verts if not v.link_faces]
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
    # tira pedacinhos soltos
    seen, small = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        stack, island = [v], []
        seen.add(v)
        while stack:
            x = stack.pop()
            island.append(x)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o not in seen:
                    seen.add(o)
                    stack.append(o)
        if len(island) < 25:
            small += island
    if small:
        bmesh.ops.delete(bm, geom=small, context='VERTS')
    bm.normal_update()
    origin = {v: (v.co.copy(), v.normal.copy()) for v in bm.verts}
    for v in bm.verts:
        v.co += v.normal * offset
    # suaviza (some com os músculos e deixa cara de tecido) e arruma a barra (bordas)
    for _ in range(smooth):
        bmesh.ops.smooth_vert(bm, verts=list(bm.verts), factor=0.5,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    border = [v for v in bm.verts if v.is_boundary]
    for _ in range(border_smooth if border else 0):  # alças finas: pouca suavização (senão viram um fio)
        bmesh.ops.smooth_vert(bm, verts=border, factor=0.5,
                              use_axis_x=True, use_axis_y=True, use_axis_z=True)
    # A suavização "encolhe" a roupa nas partes salientes (peito, ombro) e a pele furava o tecido.
    # Aqui cada ponto volta a ficar pelo menos "offset" para fora da pele de onde ele saiu.
    for v in bm.verts:
        o, n = origin[v]
        d = (v.co - o).dot(n)
        if d < offset:
            v.co += n * (offset - d)
    if flat_sole:
        inv = mw.inverted()
        for v in bm.verts:
            w = mw @ v.co
            if w.z < 0.012:
                w.z = -0.004
                v.co = inv @ w
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = body.parent
    obj.matrix_world = mw.copy()
    for g in sorted(body.vertex_groups, key=lambda g: g.index):
        obj.vertex_groups.new(name=g.name)
    mod = obj.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    me.materials.clear()
    me.materials.append(mat)
    for m in mats_extra:
        me.materials.append(m)
    for poly in me.polygons:
        poly.material_index = 0
        poly.use_smooth = True
    if face_mat:
        for poly in me.polygons:
            idx = face_mat(mw @ poly.center, poly)
            if idx:
                poly.material_index = idx
    return obj


def make_box(arm, name, bone, center, size, mat, body):
    """Peça rígida simples (ex.: coldre), presa a um osso só."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(size) * 0.2, segments=2, affect='EDGES')
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for v in me.vertices:
        v.co += center
    obj.parent = body.parent
    obj.matrix_parent_inverse = body.parent.matrix_world.inverted()
    g = obj.vertex_groups.new(name=bone)
    g.add([v.index for v in me.vertices], 1.0, 'REPLACE')
    mod = obj.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    me.materials.append(mat)
    for poly in me.polygons:
        poly.use_smooth = True
    return obj


def build_clothes(gender_tag=None):
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["Body"]
    apply_body_offset(arm, bpy.context.scene.get("alien_gender", "m"))
    cm = clothing_materials()
    rig = _Rig(arm)
    made = []

    def no_hands_head(w):
        return _sum(w, ("Head", "neck_01", "hand", "thumb", "index", "middle", "ring", "pinky")) < 0.3

    def arm_ok(w, p, max_upper, max_lower):
        side = "l" if w.get("upperarm_l", 0) + w.get("lowerarm_l", 0) >= w.get("upperarm_r", 0) + w.get("lowerarm_r", 0) else "r"
        up, lo = w.get(f"upperarm_{side}", 0), w.get(f"lowerarm_{side}", 0)
        if lo > 0.3:
            return max_lower is not None and rig.t(f"lowerarm_{side}", p) < max_lower
        if up > 0.3:
            return rig.t(f"upperarm_{side}", p) < max_upper
        return True

    def leg_ok(w, p, max_thigh, max_calf, min_calf=None):
        side = "l" if w.get("thigh_l", 0) + w.get("calf_l", 0) >= w.get("thigh_r", 0) + w.get("calf_r", 0) else "r"
        th, ca = w.get(f"thigh_{side}", 0), w.get(f"calf_{side}", 0)
        if _sum(w, ("foot", "ball")) > 0.3:
            return False
        if ca > 0.3:
            t = rig.t(f"calf_{side}", p)
            return max_calf is not None and t < max_calf and (min_calf is None or t >= min_calf)
        if th > 0.3:
            return rig.t(f"thigh_{side}", p) < max_thigh
        return True

    def top(max_upper, max_lower, shoulders=True):
        def keep(p, w):
            if not no_hands_head(w) or rig.in_neck(p) or p.z < rig.waist - 0.09:
                return False
            if _sum(w, ("thigh", "calf", "foot", "ball")) > 0.3:
                return False
            if not shoulders and _sum(w, ("upperarm", "clavicle")) > 0.35:
                return False
            return arm_ok(w, p, max_upper, max_lower)
        return keep

    def bottom(max_thigh, max_calf):
        def keep(p, w):
            if p.z > rig.waist + 0.02 or not no_hands_head(w):
                return False
            if _sum(w, ("upperarm", "lowerarm")) > 0.2:
                return False
            return leg_ok(w, p, max_thigh, max_calf)
        return keep

    def feet(min_calf, offset, name, mat):
        return build_shoes(arm, body, name, mat, min_calf, boot=min_calf < 0.8)

    # --- Civis ---
    made.append(make_shell(body, arm, "Camiseta", top(0.5, None), 0.02, cm["civil"], smooth=5))
    # Regata: corpo coberto até a axila; acima dela só as alças sobre os ombros.
    armpit = rig.chest + 0.11

    def tank(p, w):
        # gola mais fechada que a camiseta, para as alças ficarem largas sobre o ombro
        if _sum(w, ("Head", "hand", "thumb", "index", "middle", "ring", "pinky")) >= 0.3 or w.get("neck_01", 0) >= 0.6:
            return False
        if rig.in_neck(p, 0.062) or p.z < rig.waist - 0.09:
            return False
        if _sum(w, ("thigh", "calf", "foot", "ball", "lowerarm")) > 0.3:
            return False
        if p.z > armpit:  # alças: decididas pela posição (o ombro tem muito peso do braço)
            return abs(p.x - rig.neck.x) < 0.165 * OFFSET["head_scale"] and _sum(w, ("upperarm",)) < 0.8
        return _sum(w, ("upperarm",)) <= 0.45
    made.append(make_shell(body, arm, "Regata", tank, 0.018, cm["civil"], smooth=5, border_smooth=1))
    made.append(make_shell(body, arm, "MangaLonga", top(1.01, 0.88), 0.02, cm["civil"], smooth=5))
    made.append(make_shell(body, arm, "Calca", bottom(1.01, 0.97), 0.015, cm["civil"], smooth=4))
    made.append(make_shell(body, arm, "Bermuda", bottom(0.72, None), 0.016, cm["civil"], smooth=4))
    made.append(feet(0.8, 0.03, "Tenis", cm["civil"]))

    # --- Polícia ---
    def suit(p, w):
        if not no_hands_head(w) or rig.in_neck(p):
            return False
        if _sum(w, ("foot", "ball")) > 0.3:
            return False
        if _sum(w, ("upperarm", "lowerarm")) > 0.3:
            return arm_ok(w, p, 1.01, 0.9)
        if _sum(w, ("thigh", "calf")) > 0.3:
            return leg_ok(w, p, 1.01, 0.8)
        return True
    made.append(make_shell(body, arm, "Pol_Macacao", suit, 0.016, cm["uniform"], smooth=4))

    def vest(p, w):
        # Colete: tronco até o ombro, com abertura grande no pescoço e cava nos braços.
        if not no_hands_head(w) or rig.in_neck(p, 0.09) or p.z < rig.waist + 0.04:
            return False
        if _sum(w, ("lowerarm", "thigh")) > 0.25:
            return False
        if p.z > rig.chest + 0.11:
            return abs(p.x - rig.neck.x) < 0.14 * OFFSET["head_scale"] and _sum(w, ("upperarm",)) < 0.8
        return _sum(w, ("upperarm",)) <= 0.25

    def vest_light(c, poly):
        return 1 if abs(c.z - (rig.chest - 0.03)) < 0.022 else 0
    made.append(make_shell(body, arm, "Pol_Colete", vest, 0.034, cm["gear"], smooth=6,
                           face_mat=vest_light, mats_extra=(cm["light"],), border_smooth=1))

    made.append(build_belt(body, arm, "Pol_Cinto", rig.waist - 0.03, cm["gear"]))
    made.append(feet(0.6, 0.034, "Pol_Botas", cm["gear"]))

    # Coldre na coxa direita (lado de fora).
    thigh_a, thigh_b = rig.segs["thigh_r"]
    out = Vector((1 if thigh_a.x > 0 else -1, 0, 0))
    holster_c = thigh_a + (thigh_b - thigh_a) * 0.25 + out * 0.085 * OFFSET["head_scale"]
    made.append(make_box(arm, "Pol_Coldre", "thigh_r", holster_c, (0.022, 0.045, 0.075), cm["gear"], body))

    made += build_headwear(body, arm, cm)
    for o in made:
        o.hide_set(True)
    return made


def build_belt(body, arm, name, z, mat, height=0.045, offset=0.03, segments=48):
    """Cinto: um anel em volta da cintura, achado lançando raios de dentro para fora no corpo."""
    from mathutils.bvhtree import BVHTree
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    mw = body.matrix_world
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.transform(mw)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    center = Vector((bone_world(arm, "spine_01").x, bone_world(arm, "spine_01").y, z))
    rings = []
    for dz in (-height / 2, height / 2):
        ring = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            d = Vector((math.sin(a), math.cos(a), 0.0))
            origin = center + Vector((0, 0, dz)) + d * 0.4
            hit = tree.ray_cast(origin, -d, 0.5)
            p = hit[0] if hit[0] is not None else center + d * 0.12
            ring.append(p + d * offset)
        rings.append(ring)
    verts = rings[0] + rings[1]
    faces = [(i, (i + 1) % segments, segments + (i + 1) % segments, segments + i) for i in range(segments)]
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    sol = obj.modifiers.new("Solidify", 'SOLIDIFY')
    sol.thickness = 0.012
    sol.offset = 1.0
    with bpy.context.temp_override(object=obj, active_object=obj):
        bpy.ops.object.modifier_apply(modifier="Solidify")
    for poly in obj.data.polygons:
        poly.use_smooth = True
    obj.data.materials.append(mat)
    weight_by_bones(obj, arm, ["pelvis", "spine_01"])
    obj.parent = body.parent
    obj.matrix_parent_inverse = body.parent.matrix_world.inverted()
    obj.modifiers.new("Armature", 'ARMATURE').object = arm
    return obj


def build_shoes(arm, body, name, mat, start_t, boot=False):
    """Calçado arredondado (tênis/bota) feito com 'pele' (skin) ao longo dos ossos do pé."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    k = OFFSET["head_scale"]
    verts, radii, edges, flat = [], [], [], []

    def add(p, r, f):
        verts.append(Vector(p))
        radii.append(r * k)
        flat.append(f)
        return len(verts) - 1

    for side in ("l", "r"):
        calf_a, calf_b = bone_world(arm, f"calf_{side}"), bone_world(arm, f"calf_{side}", tail=True)
        foot = bone_world(arm, f"foot_{side}")
        ball = bone_world(arm, f"ball_{side}")
        toe = bone_world(arm, f"ball_{side}", tail=True)
        toe = toe + (toe - ball).normalized() * 0.03
        heel = foot - (ball - foot).normalized() * 0.035
        top = calf_a + (calf_b - calf_a) * start_t
        chain = [(top, 0.06 if boot else 0.052, 1.0)]
        if boot:
            chain.append((calf_a + (calf_b - calf_a) * ((start_t + 1) / 2), 0.06, 1.0))
        chain += [
            (Vector((foot.x, foot.y, foot.z)), 0.066, 1.0),
            (Vector((ball.x, ball.y, 0.05)), 0.07, 0.62),
            (Vector((toe.x, toe.y, 0.045)), 0.058, 0.6),
        ]
        prev = None
        for p, r, f in chain:
            i = add(p, r, f)
            if prev is not None:
                edges.append((prev, i))
            prev = i
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], edges, [])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    skin = obj.modifiers.new("Skin", 'SKIN')
    roots = {0, len(verts) // 2}
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = (radii[i], radii[i] * flat[i])
        sv.use_root = i in roots
    sub = obj.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = 2
    for m in list(obj.modifiers):
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.object.modifier_apply(modifier=m.name)
    lowest = min(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:
        v.co.z -= lowest + 0.004   # encosta no chão
        if v.co.z < 0.012:
            v.co.z = -0.004        # sola reta
    for poly in obj.data.polygons:
        poly.use_smooth = True
    obj.data.materials.append(mat)
    bones = []
    for side in ("l", "r"):
        bones += [f"calf_{side}", f"foot_{side}", f"ball_{side}"]
    weight_by_bones(obj, arm, bones)
    obj.parent = body.parent
    obj.matrix_parent_inverse = body.parent.matrix_world.inverted()
    obj.modifiers.new("Armature", 'ARMATURE').object = arm
    return obj


CAP = dict(front_above_eyes=0.075, slope=0.43)  # altura do boné na testa e inclinação do corte


def _head_frame():
    """Eixo 'para cima' da cabeça inclinada e um ponto na altura da testa."""
    up = tilt_dir(Vector((0, 0, 1)), TILT["full"]).normalized()
    pivot = Vector(TILT["pivot"])
    return up, pivot


def build_headwear(body, arm, cm):
    up, pivot = _head_frame()
    brow = tilt_point(Vector((0, 0.1, EYE["z"] + 0.045)), factor_z=EYE["z"] + 0.045)
    eye_line = tilt_point(Vector((0, 0.1, EYE["z"] - 0.005)), factor_z=EYE["z"])
    top = tilt_point(Vector((0, (HEAD_PROFILE[-1][2] + HEAD_PROFILE[-1][3]) / 2, HEAD_PROFILE[-1][0])))
    fwd = tilt_dir(Vector((0, 1, 0)), TILT["full"]).normalized()

    def h(p):
        return (p - pivot).dot(up)

    def is_head(w):
        return w.get("Head", 0) > 0.9

    # Boné: cobre o alto do crânio, cortado por um plano inclinado (baixo na testa, mais alto
    # atrás), medido na cabeça "desinclinada". A aba sai do ponto mais à frente dessa borda.
    mw = body.matrix_world
    head_idx = body.vertex_groups["Head"].index
    pts = [mw @ v.co for v in body.data.vertices
           if any(g.group == head_idx and g.weight > 0.9 for g in v.groups)]
    k = OFFSET["head_scale"]
    z_front = EYE["z"] + CAP["front_above_eyes"] * k
    y_front = 0.10 * k
    slope = CAP["slope"]

    def cap_value(p):
        q = untilt_point(p)
        return q.z - (z_front + slope * (y_front - q.y))

    def cap(p, w):
        return is_head(w) and cap_value(p) > 0
    cap_obj = make_shell(body, arm, "Pol_Bone", cap, 0.013, cm["uniform"], smooth=3)
    # base da aba: a borda do próprio boné, no trecho mais à frente
    bm_cap = bmesh.new()
    bm_cap.from_mesh(cap_obj.data)
    cmw = cap_obj.matrix_world
    border = [cmw @ v.co for v in bm_cap.verts if v.is_boundary]
    bm_cap.free()
    border.sort(key=lambda p: -p.dot(fwd))
    front = border[:max(3, len(border) // 8)]
    visor_base = sum(front, Vector()) / len(front) if front else brow
    visor = build_visor(body, arm, cm, visor_base - fwd * 0.012, fwd, up)

    # Capacete: cobre o crânio até a linha dos olhos, com abertura para o rosto.
    def helmet(p, w):
        if not is_head(w) or h(p) < h(eye_line):
            return False
        front = (p - pivot).dot(fwd)
        if front > 0.03 and h(p) < h(brow) + 0.02:
            return False  # abertura do rosto
        return True

    def helmet_light(c, poly):
        side = abs(c.x)
        return 1 if side < 0.012 and h(c) > h(brow) else 0
    helmet_obj = make_shell(body, arm, "Pol_Capacete", helmet, 0.028, cm["uniform"], smooth=4,
                            face_mat=helmet_light, mats_extra=(cm["light"],))
    # a viseira vai junto com o boné (mesmo objeto)
    _merge_into(cap_obj, visor)
    return [cap_obj, helmet_obj]


def build_visor(body, arm, cm, brow, fwd, up):
    name = "Pol_Bone_Aba"
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    k = OFFSET["head_scale"]
    right = fwd.cross(up).normalized()
    # ponto da testa na superfície (lançando um raio na cabeça)
    base = brow + fwd * 0.004
    down = (fwd * math.cos(math.radians(14)) - up * math.sin(math.radians(14))).normalized()
    fwd = down
    bm = bmesh.new()
    rx, ry = 0.09 * k, 0.07 * k
    ring = []
    steps = 16
    for i in range(steps + 1):
        a = math.pi * i / steps
        x = math.cos(a) * rx
        y = math.sin(a) * ry
        ring.append(bm.verts.new(base + right * x + fwd * (y + 0.01) - up * (y * 0.25)))
    center = bm.verts.new(base + fwd * 0.005)
    for i in range(steps):
        bm.faces.new((center, ring[i], ring[i + 1]))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    sol = obj.modifiers.new("Solidify", 'SOLIDIFY')
    sol.thickness = 0.008
    with bpy.context.temp_override(object=obj, active_object=obj):
        bpy.ops.object.modifier_apply(modifier="Solidify")
    obj.parent = body.parent
    obj.matrix_parent_inverse = body.parent.matrix_world.inverted()
    g = obj.vertex_groups.new(name="Head")
    g.add([v.index for v in obj.data.vertices], 1.0, 'REPLACE')
    obj.modifiers.new("Armature", 'ARMATURE').object = arm
    obj.data.materials.append(cm["uniform"])
    return obj


def _merge_into(target, part):
    bpy.ops.object.select_all(action='DESELECT')
    part.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    with bpy.context.temp_override(active_object=target, selected_editable_objects=[target, part]):
        bpy.ops.object.join()


def show_outfit(names):
    """Mostra só as peças pedidas (para conferir no Blender)."""
    for n in GARMENTS_CIVIL + GARMENTS_POLICE:
        o = bpy.data.objects.get(n)
        if o:
            o.hide_set(n not in names)
            o.hide_render = n not in names


def preview(path=r"C:\WorkSpace\ImNotAnAlien\Art\Blender\_preview.png", target=(0, 0, 1.2), offset=(0.6, 1.1, 0.15), size=700):
    """Renderiza uma imagem rápida (Workbench) para conferir o resultado."""
    sc = bpy.context.scene
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    t = Vector(target)
    cam.location = t + Vector(offset)
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.color_type = 'MATERIAL'
    sc.render.resolution_x = sc.render.resolution_y = size
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path
