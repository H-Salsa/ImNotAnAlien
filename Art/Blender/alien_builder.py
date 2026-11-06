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

# Inclinação da cabeça para trás (o bulbo "cai" para trás, como na arte conceitual).
TILT = dict(degrees=20.0, pivot=(0.0, -0.035, 1.600), start=1.600, full=1.740)


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
    for z, hx, yf, yb in _sample(profile, 4):
        rings.append([bm.verts.new(tilt_point(head_point(z, hx, yf, yb, th))) for th in thetas])
    for a, b in zip(rings, rings[1:]):
        for j in range(seg):
            bm.faces.new((a[j], a[(j + 1) % seg], b[(j + 1) % seg], b[j]))

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
        w = _smooth(1.585, 1.650, v.co.z)
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
    seam = [v for v in bm.verts if 1.45 < (mw @ v.co).z < 1.6 and abs((mw @ v.co).x) < 0.15]
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

    verts, radii, edges = [], [], []

    def add(p, r):
        verts.append(Vector(p))
        radii.append(r)
        return len(verts) - 1

    if wrist_info:
        w = add(wrist_info[0], wrist_info[1])
    else:
        w = add(wrist - hand_dir * 0.045, HAND["wrist"])
    bases = [bone_world(arm, f"{f}_01_{side}") for f in ("index", "ring")]
    palm_center = (wrist + (bases[0] + bases[1]) * 0.5) * 0.5
    p = add(palm_center, HAND["palm"])
    edges.append((w, p))

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
            v = add(c, r)
            edges.append((prev, v))
            prev = v

    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], edges, [])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    skin = obj.modifiers.new("Skin", 'SKIN')
    skin.use_smooth_shade = True
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = (radii[i], radii[i] * (0.85 if i == 0 else 0.6 if i == 1 else 1.0))
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


def rebuild_alien():
    """Refaz o alien inteiro a partir do corpo original (SuperHero_Male_Original)."""
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["SuperHero_Male"]
    orig = bpy.data.objects["SuperHero_Male_Original"]
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
    _remove_small_islands(body)
    slim_body(body, arm)

    head = build_head(body)
    head.data.materials.append(mats["skin"])
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


def fill_small_holes(body, max_sides=8):
    """Tapa buraquinhos que sobram nas costuras."""
    bm = bmesh.new()
    bm.from_mesh(body.data)
    edges = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.holes_fill(bm, edges=edges, sides=max_sides)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()


# ---------- Exportar para o Unity ----------

EXPORT_PATH = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Aliens\Alien_Civil.fbx"


def export_alien(path=EXPORT_PATH):
    """Une olhos, pálpebras, nariz e boca no corpo e exporta Armature + corpo em FBX."""
    import os
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["SuperHero_Male"]
    parts = []
    for n in ("AlienEye_R", "AlienEye_L", "AlienLid_R", "AlienLid_L", "AlienNose", "AlienMouth"):
        o = bpy.data.objects.get(n)
        if o is None:
            continue
        for m in list(o.modifiers):
            with bpy.context.temp_override(object=o, active_object=o):
                bpy.ops.object.modifier_apply(modifier=m.name)
        parts.append(o)
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
    arm.hide_set(False)
    body.hide_set(False)
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', use_armature_deform_only=False)
    arm.hide_set(True)
    return path
