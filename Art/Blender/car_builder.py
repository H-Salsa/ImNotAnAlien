# Construtor procedural do carro flutuante (compacto civil e viatura) — roda dentro do Blender.
# Uso (aba Scripting ou via conector), num arquivo vazio:
#   exec(open(r"C:\WorkSpace\ImNotAnAlien\Art\Blender\car_builder.py", encoding="utf-8").read())
#   build_car(police=False)   # ou police=True
#   export_car()
# Medidas do blueprint "Frota urbana" (mm -> m): 3,2 m de comprimento, 1,8 m de largura,
# 1,75 m de altura (sem a bolha), carroceria a 30 cm do chão, 4 discos de 47 cm.
# Frente do carro = +Y no Blender (vira +Z, a frente, no Unity). Direita = +X.
# Cada peça é um objeto separado (o Unity solta discos, giroflex etc. nas batidas).
import bpy, bmesh, math
from mathutils import Vector, Matrix, Euler

EXPORT_DIR = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Vehicles"

# ---------- Medidas ----------
# Carroceria (parte de baixo), de trás para a frente: (y, meia-largura, z de baixo, z de cima)
BODY = [
    (-1.62, 0.50, 0.52, 0.80),
    (-1.58, 0.68, 0.40, 0.93),
    (-1.46, 0.80, 0.33, 1.00),
    (-1.20, 0.86, 0.30, 1.04),
    (-0.60, 0.88, 0.30, 1.06),
    (0.40, 0.88, 0.30, 1.06),
    (1.00, 0.86, 0.30, 1.02),
    (1.30, 0.83, 0.32, 0.96),
    (1.50, 0.75, 0.36, 0.88),
    (1.60, 0.62, 0.44, 0.78),
    (1.63, 0.45, 0.52, 0.70),
]
# Cabine (vidros e teto): (y, meia-largura, z do topo); a base fica dentro da carroceria.
CABIN = [
    (-1.47, 0.66, 1.00),
    (-1.40, 0.72, 1.28),
    (-1.18, 0.77, 1.58),
    (-0.85, 0.79, 1.72),
    (0.00, 0.79, 1.76),
    (0.45, 0.79, 1.72),
    (0.72, 0.77, 1.56),
    (0.92, 0.73, 1.30),
    (1.00, 0.68, 1.06),
]
CABIN_BASE = 0.98
# Janelas: soleira, topo, colunas (pares y início/fim) e cantos do para-brisa/vidro traseiro.
WIN = dict(sill=1.12, top=1.60, pillars=(-1.02, -0.92, -0.08, 0.02, 0.62, 0.72), corner=0.58)
DRIVER = Vector((-0.34, 0.10))            # posição (x, y) do motorista (lado esquerdo)
# Bolha de vidro comprida (motorista e carona): cápsula de -span a +span em x, na altura dos bancos.
BUBBLE = dict(radius=0.32, z=1.64, span=0.30, height=0.95)
# A bolha fica um pouco atrás do banco: o motorista dirige reclinado e a cabeça vai para trás.
BUBBLE_Y = DRIVER.y - 0.26
# Portas (pares y de trás/frente, alinhados com as colunas da cabine) e altura da parte de baixo.
DOORS = {"F": (0.02, 0.62), "T": (-0.92, -0.08)}
DOOR_BOTTOM = 0.40
DOOR_GAP = 0.008
FLOOR_Z = 0.48
DISCS = dict(x=0.58, y=1.03, radius=0.235, top=0.33, bottom=0.17)


# ---------- Materiais ----------

def material(name, hex_color, roughness=0.5, alpha=1.0, emission=None):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    h = hex_color.lstrip("#")
    rgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    mat.diffuse_color = (*lin, alpha)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*lin, 1)
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Alpha"].default_value = alpha
        if emission:
            bsdf.inputs["Emission Color"].default_value = (*lin, 1)
            bsdf.inputs["Emission Strength"].default_value = emission
    if alpha < 1.0:
        mat.surface_render_method = 'BLENDED'
    return mat


def car_materials(police):
    return {
        "paint": material("Carro_Pintura", "#1F4E63" if police else "#F1DFC0", 0.45),
        "accent": material("Carro_Detalhe", "#1F4E63" if police else "#E2735A", 0.45),
        "bumper": material("Carro_Parachoque", "#3A3D44" if police else "#F1DFC0", 0.5),
        "chrome": material("Carro_Cromado", "#9AA3AD", 0.25),
        "glass": material("Carro_Vidro", "#8FD6E8", 0.05, alpha=0.35),
        "interior": material("Carro_Interior", "#D9CBB4", 0.8),
        "interior_dark": material("Carro_Interior_Escuro", "#5A4E44", 0.9),
        "dark": material("Carro_Escuro", "#2A2D33", 0.6),
        "headlight": material("Carro_Farol", "#FFF1C9", 0.2, emission=2.0),
        "taillight": material("Carro_Lanterna", "#E0473F", 0.3, emission=1.0),
        "disc": material("Carro_Disco", "#3A3D44", 0.4),
        "hover": material("Carro_Disco_Luz", "#5FF2D8", 0.3, emission=3.0),
        "siren_blue": material("Policia_Giroflex_Azul", "#2F6BFF", 0.2, emission=3.0),
        "siren_red": material("Policia_Giroflex_Vermelho", "#FF3B3B", 0.2, emission=3.0),
        "siren_white": material("Policia_Giroflex_Branco", "#F2F2F2", 0.2, emission=1.0),
        "badge": material("Policia_Distintivo", "#E8D8B0", 0.4),
    }


# ---------- Ajudantes de malha ----------

def _catmull(p0, p1, p2, p3, t):
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                  + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t)


def _sample(profile, steps):
    out, n = [], len(profile)
    k = len(profile[0])
    for i in range(n - 1):
        for s in range(steps):
            t = s / steps
            out.append([_catmull(profile[max(i - 1, 0)][j], profile[i][j], profile[i + 1][j],
                                 profile[min(i + 2, n - 1)][j], t) for j in range(k)])
    out.append(list(profile[-1]))
    return out


def _superellipse(a, b, theta, n):
    c, s = math.cos(theta), math.sin(theta)
    return (a * math.copysign(abs(c) ** (2 / n), c), b * math.copysign(abs(s) ** (2 / n), s))


def _new_object(name, bm, mats, smooth=True):
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = smooth
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def _loft(bm, rings, close_ends=True):
    seg = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for j in range(seg):
            bm.faces.new((a[j], a[(j + 1) % seg], b[(j + 1) % seg], b[j]))
    if close_ends:
        for ring, flip in ((rings[0], True), (rings[-1], False)):
            c = sum((v.co for v in ring), Vector()) / seg
            cv = bm.verts.new(c)
            for j in range(seg):
                f = (ring[j], ring[(j + 1) % seg], cv)
                bm.faces.new(tuple(reversed(f)) if flip else f)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def _apply_modifiers(obj):
    for m in list(obj.modifiers):
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.object.modifier_apply(modifier=m.name)


def _skin_tube(name, points, radii, mats, flat=1.0, subdiv=2, closed=False):
    """Tubo arredondado passando pelos pontos (para-choques, hastes)."""
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    me = bpy.data.meshes.new(name)
    edges = [(i, i + 1) for i in range(len(points) - 1)]
    if closed:
        edges.append((len(points) - 1, 0))
    me.from_pydata([tuple(p) for p in points], edges, [])
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    skin = obj.modifiers.new("Skin", 'SKIN')
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = (radii[i], radii[i] * flat)
        sv.use_root = i == 0
    sub = obj.modifiers.new("Sub", 'SUBSURF')
    sub.levels = subdiv
    _apply_modifiers(obj)
    for m in mats:
        obj.data.materials.append(m)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def _ellipsoid(name, center, radii, mat, rot=(0, 0, 0), segs=(24, 14)):
    bm = bmesh.new()
    m = Matrix.Translation(Vector(center)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*radii, 1))
    bmesh.ops.create_uvsphere(bm, u_segments=segs[0], v_segments=segs[1], radius=1.0, matrix=m)
    return _new_object(name, bm, [mat])


def _rounded_box(name, center, size, mat, bevel=0.3):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(center)
    bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(size) * bevel, segments=3, affect='EDGES')
    return _new_object(name, bm, [mat])


def _cylinder(name, center, radius, depth, mat, segs=32, axis='Z'):
    bm = bmesh.new()
    rot = {'Z': Matrix.Identity(4), 'X': Matrix.Rotation(math.radians(90), 4, 'Y'),
           'Y': Matrix.Rotation(math.radians(90), 4, 'X')}[axis]
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=radius, radius2=radius, depth=depth,
                          matrix=Matrix.Translation(Vector(center)) @ rot)
    return _new_object(name, bm, [mat], smooth=False)


# ---------- Peças ----------

def _body_z(y, top=True):
    """Altura de cima (ou de baixo) da carroceria no ponto y."""
    rows = _sample(BODY, 8)
    best = min(rows, key=lambda r: abs(r[0] - y))
    return best[3] if top else best[2]


def build_body(mats, police):
    rows = _sample(BODY, 10)
    seg = 56
    bm = bmesh.new()
    rings = []
    for y, hw, zb, zt in rows:
        cz, rz = (zb + zt) / 2, (zt - zb) / 2
        ring = []
        for j in range(seg):
            th = 2 * math.pi * j / seg
            x, z = _superellipse(hw, rz, th, 3.6)
            ring.append(bm.verts.new((x, y, cz + z)))
        rings.append(ring)
    _loft(bm, rings)
    # Corta a malha exatamente na linha dos dois tons (borda reta, sem "escadinha").
    _cut(bm, (0, 0, 0.86), (0, 0, 1), lambda c: c.y < 0.60)
    _cut(bm, (0, 0.60, 0.86), (0, 0.20 / 0.60, 1.0), lambda c: 0.50 < c.y < 1.30)
    _cut(bm, (0, 0, 0.66), (0, 0, 1), lambda c: 1.10 < c.y < 1.45)
    _cut(bm, (0, 1.42, 0), (0, 1, 0), lambda c: c.z < 0.80)
    # Linhas das portas (laterais).
    side = lambda c: abs(c.x) > 0.40 and c.z > 0.30
    for back, front in DOORS.values():
        _cut(bm, (0, back, 0), (0, 1, 0), side)
        _cut(bm, (0, front, 0), (0, 1, 0), side)
    _cut(bm, (0, 0, DOOR_BOTTOM), (0, 0, 1), lambda c: abs(c.x) > 0.40 and -1.0 < c.y < 0.7)

    # Abre o "teto" da carroceria dentro da cabine: vira o habitáculo, com paredes e assoalho.
    _open_cabin_floor(bm)
    obj = _new_object("Carroceria", bm, [mats["paint"], mats["accent"], mats["interior_dark"]])
    # Pintura em dois tons: faixa de baixo com a "onda" que sobe na frente (como na arte).
    for p in obj.data.polygons:
        c = p.center
        if p.material_index == 2:
            continue
        if c.z < _two_tone_line(c.y) and c.y < 1.42 and (not police or abs(p.normal.x) > 0.3):
            p.material_index = 1
    return obj


def _cabin_half_width(y):
    rows = _sample(CABIN, 6)
    best = min(rows, key=lambda r: abs(r[0] - y))
    return best[1]


def _open_cabin_floor(bm):
    """Tira a parte de cima da carroceria dentro da cabine e cria paredes internas e assoalho."""
    def inside(c):
        return -1.40 < c.y < 0.95 and abs(c.x) < _cabin_half_width(c.y) - 0.07

    top = [f for f in bm.faces if f.normal.z > 0.25 and f.calc_center_median().z > 0.85 and inside(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=top, context='FACES')
    rim = [e for e in bm.edges if e.is_boundary]
    if not rim:
        return
    ext = bmesh.ops.extrude_edge_only(bm, edges=rim)
    new_verts = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]
    new_faces = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMFace)]
    center = sum((v.co for v in new_verts), Vector()) / len(new_verts)
    for v in new_verts:
        d = Vector((v.co.x - center.x, v.co.y - center.y, 0))
        v.co = Vector((v.co.x, v.co.y, FLOOR_Z)) - d.normalized() * 0.02
    bottom = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMEdge) and g.is_boundary]
    filled = bmesh.ops.triangle_fill(bm, edges=bottom, use_beauty=True, use_dissolve=False)
    for f in new_faces + [g for g in filled["geom"] if isinstance(g, bmesh.types.BMFace)]:
        f.material_index = 2
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def _two_tone_line(y):
    if y < 0.60:
        return 0.86
    if y < 1.20:
        return 0.86 - (y - 0.60) * (0.20 / 0.60)
    return 0.66


def _cut(bm, co, no, where):
    """Corta (bisect) só as faces escolhidas por um plano."""
    geom = [f for f in bm.faces if where(f.calc_center_median())]
    if not geom:
        return
    edges = list({e for f in geom for e in f.edges})
    verts = list({v for f in geom for v in f.verts})
    bmesh.ops.bisect_plane(bm, geom=verts + edges + geom, plane_co=Vector(co), plane_no=Vector(no).normalized())


def _smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def build_cabin(mats):
    rows = _sample(CABIN, 6)
    seg = 40
    bm = bmesh.new()
    rings = []
    for y, hw, zt in rows:
        ring = []
        for j in range(seg + 1):
            th = math.pi * j / seg                      # só a metade de cima (0..180°)
            x, z = _superellipse(hw, zt - CABIN_BASE, th, 4.0)
            ring.append(bm.verts.new((x, y, CABIN_BASE + z)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for j in range(seg):
            bm.faces.new((a[j], a[j + 1], b[j + 1], b[j]))
    # tampa da frente e de trás (para-brisa e vidro traseiro)
    for ring, flip in ((rings[0], True), (rings[-1], False)):
        f = bm.faces.new(ring if not flip else list(reversed(ring)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    # Cortes retos para as bordas das janelas (soleira, topo, colunas, cantos do para-brisa).
    every = lambda c: True
    _cut(bm, (0, 0, WIN["sill"]), (0, 0, 1), every)
    _cut(bm, (0, 0, WIN["top"]), (0, 0, 1), every)
    for y in WIN["pillars"]:
        _cut(bm, (0, y, 0), (0, 1, 0), every)
    for x in (-WIN["corner"], WIN["corner"]):
        _cut(bm, (x, 0, 0), (1, 0, 0), lambda c: abs(c.y) > 0.80)

    # Furo no teto para a bolha do motorista.
    hole = [f for f in bm.faces if _bubble_dist(f.calc_center_median()) < BUBBLE["radius"] * 0.85
            and f.calc_center_median().z > 1.55]
    bmesh.ops.delete(bm, geom=hole, context='FACES')
    obj = _new_object("Cabine", bm, [mats["glass"], mats["paint"]])

    # Teto, colunas e soleira na cor do carro; o resto é vidro.
    pl = WIN["pillars"]
    bands = [(pl[i], pl[i + 1]) for i in range(0, len(pl), 2)]
    for p in obj.data.polygons:
        c = p.center
        glass = WIN["sill"] < c.z < WIN["top"]
        if abs(c.y) <= 0.80:
            glass = glass and not any(lo < c.y < hi for lo, hi in bands)
        else:
            glass = glass and abs(c.x) < WIN["corner"]
        p.material_index = 0 if glass else 1
    return obj


def _bubble_dist(p):
    """Distância (no plano) até a linha central da bolha comprida."""
    span = BUBBLE["span"]
    x = max(-span, min(span, p.x))
    return (Vector((p.x, p.y)) - Vector((x, BUBBLE_Y))).length


def build_bubble(mats):
    from mathutils.bvhtree import BVHTree
    r, span, z0 = BUBBLE["radius"], BUBBLE["span"], BUBBLE["z"]

    # Teto da cabine (para assentar a bolha e o aro na curva dele).
    cabin = bpy.data.objects.get("Cabine")
    tree = None
    if cabin is not None:
        cbm = bmesh.new()
        cbm.from_mesh(cabin.data)
        cbm.transform(cabin.matrix_world)
        tree = BVHTree.FromBMesh(cbm)
        cbm.free()

    def rim_point(x, y):
        """Ponto do aro na direção de (x, y), assentado no teto."""
        cx = max(-span, min(span, x))
        d = Vector((x - cx, y - BUBBLE_Y))
        if d.length < 1e-6:
            d = Vector((0.0, 1.0))
        d.normalize()
        px, py = cx + d.x * r * 0.98, BUBBLE_Y + d.y * r * 0.98
        z = z0 + 0.12
        if tree is not None:
            hit = tree.ray_cast(Vector((px, py, 2.5)), Vector((0, 0, -1)), 2.0)
            if hit[0] is not None:
                z = hit[0].z + 0.015
        return Vector((px, py, z))

    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=36, radius=r)
    for v in bm.verts:
        v.co.z *= BUBBLE["height"]
        v.co.x += span if v.co.x > 1e-6 else (-span if v.co.x < -1e-6 else 0.0)
        v.co += Vector((0.0, BUBBLE_Y, z0))
    # Só a cúpula fica: tudo o que desce abaixo do aro (para dentro do carro) é cortado.
    kill = [v for v in bm.verts if v.co.z < rim_point(v.co.x, v.co.y).z - 0.01]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    obj = _new_object("Bolha", bm, [mats["glass"]])

    # Aro na cor do carro, assentado na curva do teto (esconde o corte da cúpula).
    ring = []
    steps = 64
    for i in range(steps + 1):
        a = 2 * math.pi * i / steps
        x = math.cos(a) * r + (span if math.cos(a) > 0 else -span)
        y = BUBBLE_Y + math.sin(a) * r
        ring.append(rim_point(x, y))
    rim = _skin_tube("Bolha_Aro", ring, [0.035] * len(ring), [mats["paint"]], subdiv=1)
    return [obj, rim]


def _seat(name, x, mats):
    """Banco com assento, encosto inclinado e apoio de cabeça (e um pé escuro embaixo)."""
    y0 = DRIVER.y
    parts = [
        _rounded_box(name + "_Pe", (x, y0 + 0.02, FLOOR_Z + 0.05), (0.36, 0.36, 0.10), mats["dark"], bevel=0.2),
        _rounded_box(name, (x, y0 + 0.02, 0.60), (0.48, 0.50, 0.11), mats["interior"], bevel=0.45),
    ]
    back = _rounded_box(name + "_Encosto", (0, 0, 0), (0.46, 0.11, 0.58), mats["interior"], bevel=0.45)
    head = _rounded_box(name + "_Cabeca", (0, 0, 0), (0.26, 0.09, 0.15), mats["interior"], bevel=0.45)
    tilt = Matrix.Rotation(math.radians(-14), 4, 'X')
    for obj, offset in ((back, Vector((0, 0, 0.29))), (head, Vector((0, 0.01, 0.66)))):
        obj.data.transform(Matrix.Translation(Vector((x, y0 - 0.25, 0.64))) @ tilt @ Matrix.Translation(offset))
        parts.append(obj)
    return parts


def build_interior(mats):
    parts = []
    for side, name in ((-1, "Banco_Motorista"), (1, "Banco_Passageiro")):
        parts += _seat(name, DRIVER.x if side < 0 else -DRIVER.x, mats)
    # banco de trás (inteiriço)
    parts.append(_rounded_box("Banco_Tras", (0.0, -1.05, 0.60), (1.20, 0.42, 0.11), mats["interior"], bevel=0.45))
    rear_back = _rounded_box("Banco_Tras_Encosto", (0, 0, 0), (1.20, 0.10, 0.50), mats["interior"], bevel=0.45)
    rear_back.data.transform(Matrix.Translation(Vector((0.0, -1.27, 0.64))) @ Matrix.Rotation(math.radians(-12), 4, 'X')
                             @ Matrix.Translation(Vector((0, 0, 0.25))))
    parts.append(rear_back)
    parts.append(_rounded_box("Painel", (0.0, 0.78, 1.02), (1.40, 0.30, 0.14), mats["dark"]))
    # volante
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=False, radius=0.17, segments=24)
    me = bpy.data.meshes.new("Volante")
    bm.to_mesh(me)
    bm.free()
    wheel = bpy.data.objects.new("Volante", me)
    bpy.context.scene.collection.objects.link(wheel)
    wheel.location = (DRIVER.x, 0.55, 1.10)
    wheel.rotation_euler = (math.radians(-60), 0, 0)
    skin = wheel.modifiers.new("Skin", 'SKIN')
    for sv in me.skin_vertices[0].data:
        sv.radius = (0.018, 0.018)
    _apply_modifiers(wheel)
    wheel.data.materials.append(mats["dark"])
    parts.append(wheel)
    return parts


def build_front_and_back(mats, police):
    parts = []
    # Para-choques: barra arredondada que acompanha a curva da carroceria.
    for name, y0, sign in (("Parachoque_Frente", 1.56, 1), ("Parachoque_Tras", -1.56, -1)):
        pts, rad = [], []
        for i in range(13):
            u = -1 + 2 * i / 12
            x = u * 0.84
            y = y0 - sign * 0.16 * (u ** 4)
            pts.append(Vector((x, y, 0.47)))
            rad.append(0.075)
        parts.append(_skin_tube(name, pts, rad, [mats["bumper"]], flat=0.85))
    # Guardas verticais do para-choque da frente (coral no civil).
    for x in (-0.34, 0.34):
        parts.append(_skin_tube(f"Guarda_{'E' if x < 0 else 'D'}", [Vector((x, 1.63, 0.38)), Vector((x, 1.62, 0.66))],
                                [0.05, 0.05], [mats["accent"] if not police else mats["bumper"]]))
    # Faróis com aro, grade, emblema em "V".
    # O farol fica "pregado" na frente da carroceria: acha o ponto da superfície e a direção em que
    # ela olha, e monta o farol e o aro virados para lá (assim aparece o círculo inteiro).
    from mathutils.bvhtree import BVHTree
    body = bpy.data.objects["Carroceria"]
    bbm = bmesh.new()
    bbm.from_mesh(body.data)
    tree = BVHTree.FromBMesh(bbm)
    bbm.free()
    for x in (-0.50, 0.50):
        tag = "E" if x < 0 else "D"
        hit = tree.ray_cast(Vector((x, 3.0, 0.74)), Vector((0, -1, 0)), 5.0)
        surf = hit[0] if hit[0] is not None else Vector((x, 1.5, 0.74))
        normal = hit[1].normalized() if hit[0] is not None else Vector((0, 1, 0))
        normal = (normal + Vector((0, 0.6, 0))).normalized()       # puxa um pouco para a frente
        rot = normal.to_track_quat('Z', 'Y').to_matrix()           # eixo Z local = para fora
        lamp_r = 0.125
        center = surf + normal * 0.02
        bm = bmesh.new()
        m = Matrix.Translation(center) @ rot.to_4x4() @ Matrix.Diagonal((lamp_r, lamp_r, 0.06, 1))
        bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=1.0, matrix=m)
        parts.append(_new_object(f"Farol_{tag}", bm, [mats["headlight"]]))
        ring = [center + rot @ Vector((math.cos(a) * (lamp_r + 0.02), math.sin(a) * (lamp_r + 0.02), 0.0))
                for a in [2 * math.pi * i / 32 for i in range(32)]]
        parts.append(_skin_tube(f"Farol_Aro_{tag}", ring, [0.022] * len(ring),
                                [mats["accent"] if not police else mats["chrome"]], subdiv=1, closed=True))
        parts.append(_ellipsoid(f"Lanterna_{tag}", (x * 1.42, -1.55, 0.85), (0.06, 0.05, 0.11), mats["taillight"]))
    parts.append(_rounded_box("Grade", (0.0, 1.60, 0.62), (0.46, 0.08, 0.15), mats["chrome"], bevel=0.35))
    for i, z in enumerate((0.58, 0.62, 0.66)):
        parts.append(_rounded_box(f"Grade_Fresta_{i}", (0.0, 1.645, z), (0.38, 0.02, 0.012), mats["dark"], bevel=0.4))
    v = [Vector((-0.16, 1.40, 0.985)), Vector((0.0, 1.47, 0.955)), Vector((0.16, 1.40, 0.985))]
    parts.append(_skin_tube("Emblema", v, [0.022, 0.03, 0.022], [mats["accent"] if not police else mats["badge"]], flat=0.5))
    # Placa traseira e maçanetas.
    parts.append(_rounded_box("Placa", (0.0, -1.62, 0.66), (0.34, 0.03, 0.12), mats["dark"], bevel=0.3))
    for y in (0.35, -0.45):
        for x in (-0.885, 0.885):
            parts.append(_rounded_box(f"Macaneta_{'E' if x < 0 else 'D'}_{'F' if y > 0 else 'T'}",
                                      (x, y, 0.96), (0.03, 0.16, 0.03), mats["chrome"], bevel=0.4))
    return parts


def _split_faces(obj, pick):
    """Separa as faces escolhidas num objeto novo (as coordenadas são do mundo: os objetos estão na origem)."""
    bm_keep = bmesh.new()
    bm_keep.from_mesh(obj.data)
    bm_part = bm_keep.copy()
    sel = [f for f in bm_keep.faces if pick(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm_keep, geom=sel, context='FACES')
    unsel = [f for f in bm_part.faces if not pick(f.calc_center_median(), f.normal)]
    bmesh.ops.delete(bm_part, geom=unsel, context='FACES')
    bm_keep.to_mesh(obj.data)
    bm_keep.free()
    if not bm_part.faces:
        bm_part.free()
        return None
    part = _new_object(obj.name + "_parte", bm_part, list(obj.data.materials))
    return part


def build_doors(mats):
    """4 portas separadas (as da frente vão abrir para o alien entrar e sair), com a dobradiça na frente."""
    body = bpy.data.objects["Carroceria"]
    cabin = bpy.data.objects["Cabine"]
    doors = []
    for sx, side in ((-1, "E"), (1, "D")):
        for key, (back, front) in DOORS.items():
            def body_pick(c, n, back=back, front=front, sx=sx):
                return (c.x * sx > 0.40 and back < c.y < front and DOOR_BOTTOM < c.z < 1.10
                        and abs(n.x) > 0.3)

            def cabin_pick(c, n, back=back, front=front, sx=sx):
                return c.x * sx > 0.40 and back < c.y < front and c.z < WIN["top"] and abs(n.x) > 0.4

            low = _split_faces(body, body_pick)
            high = _split_faces(cabin, cabin_pick)
            pieces = [p for p in (low, high) if p is not None]
            if not pieces:
                continue
            # a maçaneta vai junto com a porta
            handle = bpy.data.objects.get(f"Macaneta_{side}_{key}")
            if handle is not None:
                pieces.append(handle)
            door = pieces[0]
            if len(pieces) > 1:
                _join(door, pieces[1:])
            door.name = f"Porta_{side}{key}"
            me = door.data
            # folga em volta da porta (aparece a linha da porta)
            ys = [v.co.y for v in me.vertices]
            zs = [v.co.z for v in me.vertices]
            cy, cz = (min(ys) + max(ys)) / 2, (min(zs) + max(zs)) / 2
            hy, hz = (max(ys) - min(ys)) / 2, (max(zs) - min(zs)) / 2
            for v in me.vertices:
                v.co.y = cy + (v.co.y - cy) * (1 - DOOR_GAP / max(hy, 0.01))
                v.co.z = cz + (v.co.z - cz) * (1 - DOOR_GAP / max(hz, 0.01))
            # dobradiça: na borda da frente, no meio da altura
            front_verts = [v.co for v in me.vertices if v.co.y > max(ys) - 0.03]
            hinge = sum(front_verts, Vector()) / len(front_verts) if front_verts else Vector((sx * 0.88, max(ys), cz))
            me.transform(Matrix.Translation(-hinge))
            door.location = hinge
            for p in me.polygons:
                p.use_smooth = True
            doors.append(door)
    return doors


def build_mirrors(mats):
    parts = []
    for x in (-1, 1):
        tag = "E" if x < 0 else "D"
        base = Vector((x * 0.80, 0.80, 1.10))
        tip = Vector((x * 0.98, 0.84, 1.20))
        stalk = _skin_tube(f"Espelho_Haste_{tag}", [base, tip], [0.018, 0.018], [mats["bumper"]], subdiv=1)
        ball = _ellipsoid(f"Espelho_{tag}", tip + Vector((x * 0.04, 0, 0.02)), (0.07, 0.05, 0.065), mats["accent"])
        _join(ball, [stalk])
        ball.name = f"Espelho_{tag}"
        parts.append(ball)
    return parts


def build_discs(mats):
    parts = []
    d = DISCS
    h = d["top"] - d["bottom"]
    for sx in (-1, 1):
        for sy in (-1, 1):
            tag = ("E" if sx < 0 else "D") + ("F" if sy > 0 else "T")
            center = Vector((sx * d["x"], sy * d["y"], d["bottom"] + h * 0.6))
            disc = _cylinder(f"Disco_{tag}", center, d["radius"], h * 0.8, mats["disc"])
            bev = disc.modifiers.new("Bevel", 'BEVEL')
            bev.width = 0.03
            bev.segments = 3
            _apply_modifiers(disc)
            glow = _cylinder(f"Disco_{tag}_Luz", Vector((center.x, center.y, d["bottom"] + 0.015)),
                             d["radius"] * 0.92, 0.03, mats["hover"])
            disc.data.materials.append(mats["hover"])
            _join(disc, [glow])
            disc.name = f"Disco_{tag}"
            for p in disc.data.polygons:
                p.use_smooth = True
            parts.append(disc)
    return parts


def build_police_extras(mats):
    parts = []
    base = _rounded_box("Giroflex", (0.0, 0.42, 1.76), (0.92, 0.26, 0.06), mats["dark"], bevel=0.4)
    lamps = []
    for i, (x, m) in enumerate(((-0.30, "siren_blue"), (0.0, "siren_white"), (0.30, "siren_red"))):
        lamps.append(_rounded_box(f"Giroflex_{i}", (x, 0.42, 1.83), (0.28, 0.22, 0.10), mats[m], bevel=0.35))
    parts.append(base)
    parts += lamps
    # Distintivo (escudo) nas portas da frente.
    for x in (-1, 1):
        bm = bmesh.new()
        outline = [(-0.11, 0.10), (0.11, 0.10), (0.11, 0.0), (0.0, -0.13), (-0.11, 0.0)]
        verts = [bm.verts.new((x * 0.89, 0.30 + px * -x, 0.82 + pz)) for px, pz in outline]
        bm.faces.new(verts if x > 0 else list(reversed(verts)))
        obj = _new_object(f"Distintivo_{'E' if x < 0 else 'D'}", bm, [mats["badge"]], smooth=False)
        sol = obj.modifiers.new("Solid", 'SOLIDIFY')
        sol.thickness = 0.01
        _apply_modifiers(obj)
        parts.append(obj)
    return parts


def _alive(obj):
    """O objeto ainda existe? (os que foram unidos a outros somem)"""
    try:
        return obj.name in bpy.data.objects
    except ReferenceError:
        return False


def _join(target, parts):
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    with bpy.context.temp_override(active_object=target, selected_editable_objects=[target] + parts):
        bpy.ops.object.join()


# ---------- Montagem ----------

def build_car(police=False):
    """Num arquivo VAZIO do Blender: monta o carro inteiro dentro de um objeto vazio 'Carro'."""
    mats = car_materials(police)
    objs = [build_body(mats, police), build_cabin(mats)]
    objs += build_bubble(mats)
    objs += build_interior(mats)
    objs += build_front_and_back(mats, police)
    objs += build_doors(mats)            # depois das maçanetas (elas vão junto com as portas)
    objs = [o for o in objs if _alive(o)]
    objs += build_mirrors(mats)
    objs += build_discs(mats)
    if police:
        objs += build_police_extras(mats)

    root_name = "Viatura" if police else "Carro_Civil"
    old = bpy.data.objects.get(root_name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    root = bpy.data.objects.new(root_name, None)
    bpy.context.scene.collection.objects.link(root)

    # Peças que ficam juntas na carroceria (não se soltam no jogo).
    loose_prefix = ("Disco_", "Giroflex", "Espelho_", "Parachoque_", "Porta_")
    loose = [o.name for o in objs if o.name.startswith(loose_prefix)]
    keep = [o for o in objs if not o.name.startswith(loose_prefix) and o.name != "Carroceria"]
    body = bpy.data.objects["Carroceria"]
    _join(body, keep)
    body.name = "Carroceria"
    body.parent = root
    for name in loose:
        bpy.data.objects[name].parent = root
    bpy.context.scene["car_police"] = police
    return root


def preview(path, target=(0, 0, 0.9), offset=(3.2, 4.2, 1.6), size=800):
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


def export_car(path=None):
    import os
    police = bool(bpy.context.scene.get("car_police", False))
    root = bpy.data.objects["Viatura" if police else "Carro_Civil"]
    if path is None:
        path = os.path.join(EXPORT_DIR, "Viatura.fbx" if police else "Carro_Civil.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for c in root.children:
        c.hide_set(False)
        c.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'},
                             mesh_smooth_type='FACE', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True)
    return path
