# Alien aquático (Missão 20) — roda dentro do Blender, em cima do alien feminino.
# Uso (arquivo Alien_Aquatico.blend, cópia do Alien_Feminino.blend):
#   exec(open(r"C:\WorkSpace\ImNotAnAlien\Art\Blender\alien_builder.py", encoding="utf-8").read())
#   exec(open(r"C:\WorkSpace\ImNotAnAlien\Art\Blender\aquatic_builder.py", encoding="utf-8").read())
#   build_aquatic()
#   export_aquatic()
# Peças (todas presas aos ossos do mesmo esqueleto: usa as mesmas animações):
# - maiô retrô azul-marinho com bordas e cinto coral e fivela creme;
# - barbatanas na cabeça, nos antebraços e nas panturrilhas (sólidas, borda brilhante);
# - pés-nadadeira (médios, para não atravessar o chão ao andar) e membrana entre os dedos;
# - guelras no pescoço; manchas que brilham (bochecha, ombro, antebraço, panturrilha);
# - costas e parte de fora dos braços/pernas mais escuras ("escamas" pintadas).
import bpy, bmesh, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

AQ_EXPORT = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Aliens\Alien_Aquatico.fbx"
AQ_PARTS = ("Aq_Maio", "Aq_Debrum", "Aq_Cinto", "Aq_Fivela", "Aq_Nadadeiras", "Aq_Barbatanas", "Aq_Membranas", "Aq_Guelras", "Aq_Manchas")


def _deselect_all():
    for o in bpy.context.view_layer.objects:
        o.select_set(False)


def _export_fbx(objs, **kw):
    """O export de FBX precisa de 'selected_objects' no contexto (o MCP não tem janela): passa à mão."""
    ctx = {"selected_objects": list(objs), "active_object": objs[0], "object": objs[0]}
    win = bpy.context.window_manager.windows[0] if bpy.context.window_manager.windows else None
    if win is not None:
        ctx["window"] = win
        for area in win.screen.areas:
            if area.type == 'VIEW_3D':
                ctx["area"] = area
                ctx["region"] = next((r for r in area.regions if r.type == 'WINDOW'), None)
                break
    with bpy.context.temp_override(**ctx):
        bpy.ops.export_scene.fbx(**kw)


def aq_materials():
    return {
        "suit": material("Aq_Maio", "#1F3B66", 0.7),
        "trim": material("Aq_Maio_Borda", "#F06A4E", 0.6),
        "buckle": material("Aq_Fivela", "#F2E4C4", 0.4),
        "fin": material("Aq_Barbatana", "#8FE6D6", 0.35),
        "glow": material("Aq_Brilho", "#62FFD9", 0.2),
        "gill": material("Aq_Guelra", "#1D6B66", 0.5),
    }


def _body_tree(body):
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.transform(body.matrix_world)
    tree = BVHTree.FromBMesh(bm)
    bm.free()
    return tree


def _surface(tree, origin, direction, fallback):
    """Ponto da pele achado com um raio vindo de fora."""
    hit = tree.ray_cast(origin, direction.normalized(), 1.0)
    return (hit[0], hit[1]) if hit[0] is not None else (fallback, -direction.normalized())


def _new_object(name, me, arm, mats):
    old = bpy.data.objects.get(name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    for m in mats:
        me.materials.append(m)
    return obj


def _finish(obj, arm, bones, blend=2, thickness=0.0):
    if thickness > 0:
        sol = obj.modifiers.new("Solidify", 'SOLIDIFY')
        sol.thickness = thickness
        sol.offset = 0.0
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.object.modifier_apply(modifier="Solidify")
    for poly in obj.data.polygons:
        poly.use_smooth = True
    weight_by_bones(obj, arm, bones, blend)
    obj.modifiers.new("Armature", 'ARMATURE').object = arm
    return obj


# ---------- Barbatanas ----------

def _fin_mesh(bm, base, outward, lengths, rows=4, scallops=3, bend=None):
    """Leque: linha de base (na pele) e borda recortada em ondas. Devolve as faces da última fileira."""
    n = len(base)
    grid = []
    for i in range(n):
        u = i / (n - 1)
        wave = 0.62 + 0.38 * abs(math.sin(math.pi * scallops * u))   # borda em "dedinhos"
        col = []
        for j in range(rows + 1):
            t = j / rows
            p = base[i] + outward[i] * lengths[i] * t * wave
            if bend is not None:
                p = p + bend * (t * t) * lengths[i]
            col.append(bm.verts.new(p))
        grid.append(col)
    rim = []
    for i in range(n - 1):
        for j in range(rows):
            f = bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
            if j == rows - 1:
                rim.append(f)
    return rim


def build_fins(body, arm, m):
    tree = _body_tree(body)
    k = OFFSET["head_scale"]
    bm = bmesh.new()
    rim_all = []
    bones = []
    for side, sx in (("l", -1.0), ("r", 1.0)):
        # Cabeça: dos lados, atrás dos olhos, varrendo para trás e um pouco para cima.
        eye_z = EYE["z"]
        base, out, lens = [], [], []
        for i in range(13):
            u = i / 12
            z = eye_z - 0.03 + 0.13 * u
            y = -0.005 - 0.06 * u
            p, _ = _surface(tree, Vector((sx * 0.4, y, z)), Vector((-sx, 0, 0)), Vector((sx * 0.09, y, z)))
            base.append(p - Vector((sx * 0.004, 0, 0)))
            out.append(Vector((sx * 0.5, -0.72, 0.48)).normalized())
            lens.append((0.08 + 0.12 * math.sin(math.pi * (0.1 + 0.8 * u))) * k)
        rim_all += _fin_mesh(bm, base, out, lens, rows=5, scallops=3, bend=Vector((sx * 0.3, -0.15, 0.1)))
        bones += ["Head"]

        # Antebraço: na parte de trás (lado do dedo anelar), saindo para trás.
        a, b = bone_world(arm, f"lowerarm_{side}"), bone_world(arm, f"lowerarm_{side}", tail=True)
        base, out, lens = [], [], []
        for i in range(8):
            u = 0.12 + 0.8 * i / 7
            c = a + (b - a) * u
            p, _ = _surface(tree, c + Vector((0, -0.3, 0)), Vector((0, 1, 0)), c + Vector((0, -0.03, 0)))
            base.append(p + Vector((0, 0.004, 0)))
            out.append(Vector((sx * 0.25, -0.9, -0.25)).normalized())
            lens.append(0.04 + 0.09 * math.sin(math.pi * (0.2 + 0.75 * i / 7)))
        rim_all += _fin_mesh(bm, base, out, lens, rows=4, scallops=3, bend=Vector((sx * 0.2, -0.1, -0.2)))
        bones += [f"lowerarm_{side}"]

        # Panturrilha: atrás, saindo para trás e para baixo (como na arte).
        a, b = bone_world(arm, f"calf_{side}"), bone_world(arm, f"calf_{side}", tail=True)
        base, out, lens = [], [], []
        for i in range(8):
            u = 0.18 + 0.62 * i / 7
            c = a + (b - a) * u
            p, _ = _surface(tree, c + Vector((0, -0.3, 0)), Vector((0, 1, 0)), c + Vector((0, -0.045, 0)))
            base.append(p + Vector((0, 0.004, 0)))
            out.append(Vector((sx * 0.3, -0.85, -0.42)).normalized())
            lens.append(0.04 + 0.09 * math.sin(math.pi * (0.15 + 0.8 * i / 7)))
        rim_all += _fin_mesh(bm, base, out, lens, rows=4, scallops=3, bend=Vector((sx * 0.15, -0.05, -0.25)))
        bones += [f"calf_{side}"]

    rim_set = set(rim_all)
    me = bpy.data.meshes.new("Aq_Barbatanas")
    for f in bm.faces:
        f.material_index = 1 if f in rim_set else 0
    bm.to_mesh(me)
    bm.free()
    obj = _new_object("Aq_Barbatanas", me, arm, [m["fin"], m["glow"]])
    return _finish(obj, arm, sorted(set(bones)), blend=1, thickness=0.007)


# ---------- Pés-nadadeira ----------

def build_flippers(body, arm, m):
    k = OFFSET["head_scale"]
    verts, radii, edges = [], [], []
    for side in ("l", "r"):
        calf_a, calf_b = bone_world(arm, f"calf_{side}"), bone_world(arm, f"calf_{side}", tail=True)
        foot = bone_world(arm, f"foot_{side}")
        ball = bone_world(arm, f"ball_{side}")
        toe = bone_world(arm, f"ball_{side}", tail=True)
        fwd = (toe - ball).normalized()
        chain = [
            (calf_a + (calf_b - calf_a) * 0.82, (0.042, 0.042)),
            (foot + Vector((0, 0, 0.005)), (0.045, 0.045)),
            (Vector((ball.x, ball.y, 0.032)), (0.052, 0.026)),
            (Vector((toe.x, toe.y, 0.022)) + fwd * 0.02, (0.07, 0.014)),
            (Vector((toe.x, toe.y, 0.018)) + fwd * 0.09, (0.085, 0.01)),
        ]
        prev = None
        for p, r in chain:
            verts.append(p)
            radii.append((r[0] * k, r[1] * k))
            i = len(verts) - 1
            if prev is not None:
                edges.append((prev, i))
            prev = i
    me = bpy.data.meshes.new("Aq_Nadadeiras")
    me.from_pydata([tuple(v) for v in verts], edges, [])
    obj = _new_object("Aq_Nadadeiras", me, arm, [m["fin"], bpy.data.materials["Alien_Pele"]])
    skin = obj.modifiers.new("Skin", 'SKIN')
    half = len(verts) // 2
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = radii[i]
        sv.use_root = i in (0, half)
    sub = obj.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = 2
    for mod in list(obj.modifiers):
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    lowest = min(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:
        v.co.z -= lowest + 0.002
    # Tornozelo e peito do pé na cor da pele; a parte larga (nadadeira) na cor da barbatana.
    for poly in obj.data.polygons:
        c = poly.center
        poly.material_index = 1 if c.z > 0.045 else 0
    bones = []
    for side in ("l", "r"):
        bones += [f"calf_{side}", f"foot_{side}", f"ball_{side}"]
    return _finish(obj, arm, bones, blend=2)


# ---------- Membrana entre os dedos ----------

def build_webs(arm, m):
    bm = bmesh.new()
    bones = []
    for side in ("l", "r"):
        pairs = [
            ([f"thumb_01_{side}", f"thumb_02_{side}", f"thumb_03_{side}"], [f"index_01_{side}", f"index_02_{side}", f"index_03_{side}"]),
            ([f"index_01_{side}", f"index_02_{side}", f"index_03_{side}"], [f"ring_01_{side}", f"ring_02_{side}", f"ring_03_{side}"]),
        ]
        for left, right in pairs:
            la = [bone_world(arm, b) for b in left]
            ra = [bone_world(arm, b) for b in right]
            # base no começo dos dedos (fundo da mão), indo até ~2/3 do dedo, com a borda em "U".
            row = []
            for t in (0.0, 0.5, 1.0):
                pl = la[0] + (la[2] - la[0]) * t
                pr = ra[0] + (ra[2] - ra[0]) * t
                dip = 0.0 if t == 0.0 else 0.35 * t
                mid = pl.lerp(pr, 0.5)
                row.append((bm.verts.new(pl), bm.verts.new(mid.lerp((la[0] + ra[0]) * 0.5, dip)), bm.verts.new(pr)))
            for j in range(2):
                a, b = row[j], row[j + 1]
                bm.faces.new((a[0], a[1], b[1], b[0]))
                bm.faces.new((a[1], a[2], b[2], b[1]))
            bones += left + right
    me = bpy.data.meshes.new("Aq_Membranas")
    bm.to_mesh(me)
    bm.free()
    obj = _new_object("Aq_Membranas", me, arm, [m["fin"]])
    bones += [f"hand_{s}" for s in "lr"]
    return _finish(obj, arm, sorted(set(bones)), blend=2, thickness=0.004)


# ---------- Guelras e manchas ----------

def build_gills(body, arm, m):
    tree = _body_tree(body)
    verts, edges = [], []
    neck = bone_world(arm, "neck_01")
    for sx in (-1.0, 1.0):
        for g in range(3):
            pts = []
            for i in range(4):
                z = neck.z + 0.005 + g * 0.017 + i * 0.003
                y = neck.y + 0.022 - i * 0.014
                hit = tree.ray_cast(Vector((sx * 0.3, y, z)), Vector((-sx, 0, 0)), 1.0)
                if hit[0] is not None:
                    pts.append(hit[0] + hit[1] * 0.0015)
            if len(pts) < 2:
                continue
            base = len(verts)
            verts += pts
            edges += [(base + i, base + i + 1) for i in range(len(pts) - 1)]
    me = bpy.data.meshes.new("Aq_Guelras")
    me.from_pydata([tuple(v) for v in verts], edges, [])
    obj = _new_object("Aq_Guelras", me, arm, [m["gill"]])
    skin = obj.modifiers.new("Skin", 'SKIN')
    for i, sv in enumerate(me.skin_vertices[0].data):
        sv.radius = (0.004, 0.004)
        sv.use_root = False
    if me.skin_vertices[0].data:
        roots = {e[0] for e in edges} - {e[1] for e in edges}
        for i in roots:
            me.skin_vertices[0].data[i].use_root = True
    sub = obj.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = 1
    for mod in list(obj.modifiers):
        with bpy.context.temp_override(object=obj, active_object=obj):
            bpy.ops.object.modifier_apply(modifier=mod.name)
    return _finish(obj, arm, ["neck_01", "Head"], blend=2)


def build_spots(body, arm, m):
    tree = _body_tree(body)
    k = OFFSET["head_scale"]
    spots = []   # (origem do raio, direção, raio da mancha)
    for sx in (-1.0, 1.0):
        # bochecha / têmpora (fileira de pontinhos acima do olho, como na arte)
        ez = EYE["z"]
        for (y, dz, r) in ((0.03, 0.045, 0.009), (0.012, 0.06, 0.011), (-0.008, 0.072, 0.009), (0.045, 0.03, 0.007)):
            spots.append((Vector((sx * 0.4, y, ez + dz)), Vector((-sx, 0, 0)), r))
        # ombro
        for (y, dz, r) in ((0.0, 0.0, 0.013), (0.022, -0.02, 0.01), (-0.022, -0.015, 0.009), (0.005, 0.024, 0.009)):
            c = bone_world(arm, f"upperarm_{'l' if sx < 0 else 'r'}")
            spots.append((Vector((c.x + sx * 0.04, y, c.z + 0.4 + dz)), Vector((0, 0, -1)), r))
        # antebraço e panturrilha (de lado)
        side = 'l' if sx < 0 else 'r'
        a, b = bone_world(arm, f"lowerarm_{side}"), bone_world(arm, f"lowerarm_{side}", tail=True)
        for u, r in ((0.35, 0.009), (0.5, 0.011), (0.65, 0.008)):
            c = a + (b - a) * u
            spots.append((c + Vector((0, 0, 0.3)), Vector((0, 0, -1)), r))
        a, b = bone_world(arm, f"calf_{side}"), bone_world(arm, f"calf_{side}", tail=True)
        for u, r in ((0.3, 0.012), (0.42, 0.009), (0.55, 0.011)):
            c = a + (b - a) * u
            spots.append((c + Vector((sx * 0.3, 0, 0)), Vector((-sx, 0, 0)), r))
    bm = bmesh.new()
    for origin, direction, r in spots:
        hit = tree.ray_cast(origin, direction.normalized(), 1.0)
        if hit[0] is None:
            continue
        p, n = hit[0], hit[1]
        geo = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r * k)
        vs = geo["verts"]
        # achata na direção da pele e encosta nela
        for v in vs:
            d = v.co.dot(n)
            v.co = v.co - n * d * 0.7 + p + n * 0.001
    me = bpy.data.meshes.new("Aq_Manchas")
    bm.to_mesh(me)
    bm.free()
    obj = _new_object("Aq_Manchas", me, arm, [m["glow"]])
    bones = ["Head", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "calf_l", "calf_r", "clavicle_l", "clavicle_r"]
    return _finish(obj, arm, bones, blend=1)


# ---------- Maiô ----------

def build_swimsuit(body, arm, m):
    rig = _Rig(arm)
    k = OFFSET["head_scale"]
    armpit = rig.chest + 0.11

    def keep(p, w):
        if _sum(w, ("Head", "hand", "thumb", "index", "middle", "ring", "pinky", "lowerarm", "calf", "foot", "ball")) >= 0.3:
            return False
        if w.get("neck_01", 0) >= 0.5 or rig.in_neck(p, 0.068):
            return False
        if _sum(w, ("thigh",)) > 0.3:
            side = "l" if w.get("thigh_l", 0) >= w.get("thigh_r", 0) else "r"
            return rig.t(f"thigh_{side}", p) < 0.3
        if p.z > armpit:   # alças largas sobre os ombros
            return abs(p.x - rig.neck.x) < 0.15 * k and _sum(w, ("upperarm",)) < 0.7
        return _sum(w, ("upperarm",)) <= 0.4

    suit = make_shell(body, arm, "Aq_Maio", keep, 0.012, m["suit"], smooth=4, mats_extra=(m["trim"],), border_smooth=2)
    # Bordas coral: um "debrum" (tubinho) seguindo cada beirada do maiô (decote, cavas e pernas).
    piping = build_piping(suit, arm, m["trim"])

    belt = build_belt(body, arm, "Aq_Cinto", rig.waist - 0.015, m["trim"], height=0.032, offset=0.02)
    # Fivela creme na frente da cintura.
    tree = _body_tree(body)
    c = Vector((0.0, 0.4, rig.waist - 0.015))
    hit = tree.ray_cast(c, Vector((0, -1, 0)), 1.0)
    front = (hit[0] if hit[0] is not None else Vector((0, 0.09, c.z))) + Vector((0, 0.034, 0))
    buckle = make_box(arm, "Aq_Fivela", "pelvis", front, (0.024, 0.008, 0.016), m["buckle"], body)
    return [suit, piping, belt, buckle]


def build_piping(suit, arm, mat, radius=0.0065, sides=6):
    """Debrum: um tubinho em volta de cada beirada (anel fechado) do maiô."""
    bm = bmesh.new()
    bm.from_mesh(suit.data)
    bm.transform(suit.matrix_world)
    bm.normal_update()
    border = {e for e in bm.edges if e.is_boundary}
    loops = []
    while border:
        e = border.pop()
        chain = [e.verts[0], e.verts[1]]
        while True:
            last = chain[-1]
            nxt = next((x for x in last.link_edges if x in border), None)
            if nxt is None:
                break
            border.discard(nxt)
            v = nxt.other_vert(last)
            if v == chain[0]:
                break
            chain.append(v)
        if len(chain) >= 4:
            loops.append([(v.co.copy(), v.normal.copy()) for v in chain])
    bm.free()

    out = bmesh.new()
    for loop in loops:
        n = len(loop)
        rings = []
        for i, (co, nor) in enumerate(loop):
            prev_co = loop[i - 1][0]
            next_co = loop[(i + 1) % n][0]
            tangent = (next_co - prev_co).normalized()
            up = (nor - tangent * nor.dot(tangent)).normalized()
            side = tangent.cross(up)
            center = co + up * 0.003
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides
                ring.append(out.verts.new(center + (up * math.cos(a) + side * math.sin(a)) * radius))
            rings.append(ring)
        for i in range(n):
            r0, r1 = rings[i], rings[(i + 1) % n]
            for k in range(sides):
                out.faces.new((r0[k], r0[(k + 1) % sides], r1[(k + 1) % sides], r1[k]))
    me = bpy.data.meshes.new("Aq_Debrum")
    out.to_mesh(me)
    out.free()
    obj = _new_object("Aq_Debrum", me, arm, [mat])
    bones = ["pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "clavicle_l", "clavicle_r",
             "upperarm_l", "upperarm_r", "thigh_l", "thigh_r"]
    return _finish(obj, arm, bones, blend=2)


# ---------- "Escamas" pintadas ----------

def paint_back(body, arm):
    """Costas, parte de fora dos braços e de trás das pernas com a cor mais escura (Alien_Pele_Listra)."""
    mw = body.matrix_world
    rot = mw.to_3x3()
    stripe = body.data.materials.find("Alien_Pele_Listra")
    if stripe < 0:
        return
    for poly in body.data.polygons:
        c = mw @ poly.center
        n = (rot @ poly.normal).normalized()
        if c.z > 1.47:            # cabeça e pescoço: ficam com as listras de sempre
            continue
        poly.material_index = 0
        if n.y < -0.45:  # costas e parte de trás das pernas
            poly.material_index = stripe


# ---------- Tudo ----------

def build_aquatic():
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["Body"]
    apply_body_offset(arm, bpy.context.scene.get("alien_gender", "f"))
    for n in GARMENTS_CIVIL + GARMENTS_POLICE:
        o = bpy.data.objects.get(n)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)
    m = aq_materials()
    made = build_swimsuit(body, arm, m)
    made.append(build_flippers(body, arm, m))
    made.append(build_fins(body, arm, m))
    made.append(build_webs(arm, m))
    made.append(build_gills(body, arm, m))
    made.append(build_spots(body, arm, m))
    paint_back(body, arm)
    return [o.name for o in made]


def export_aquatic(path=AQ_EXPORT):
    """Igual ao export_alien, mas com as peças aquáticas UNIDAS ao corpo (um só modelo, sem roupas para trocar)."""
    import os
    arm = bpy.data.objects["Armature"]
    body = bpy.data.objects["Body"]
    parts = []
    for n in ("AlienEye_R", "AlienEye_L", "AlienLid_R", "AlienLid_L", "AlienNose", "AlienMouth"):
        o = bpy.data.objects.get(n)
        if o is None:
            continue
        for mod in list(o.modifiers):
            with bpy.context.temp_override(object=o, active_object=o):
                bpy.ops.object.modifier_apply(modifier=mod.name)
        parts.append(o)
    add_expressions(arm, body, parts)
    aq = [bpy.data.objects[n] for n in AQ_PARTS if n in bpy.data.objects]
    for o in aq:
        for mod in list(o.modifiers):
            with bpy.context.temp_override(object=o, active_object=o):
                bpy.ops.object.modifier_apply(modifier=mod.name)
    join = parts + aq
    _deselect_all()
    for p in join:
        p.hide_set(False)
        p.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    with bpy.context.temp_override(active_object=body, selected_editable_objects=join + [body]):
        bpy.ops.object.join()
    if not any(mod.type == 'ARMATURE' for mod in body.modifiers):
        body.modifiers.new("Armature", 'ARMATURE').object = arm
    os.makedirs(os.path.dirname(path), exist_ok=True)
    arm.hide_set(False)
    _deselect_all()
    arm.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm
    _export_fbx([arm, body],
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE',
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', use_armature_deform_only=False)
    arm.hide_set(True)
    return path


# ---------- Nado próprio (Missão 20) ----------
# Dois clipes feitos aqui, no mesmo esqueleto (entram no Unity no lugar do nado comum, só para ela):
# - Aq_Swim_Fwd_Loop: nado de "golfinho/sereia": pernas juntas batendo como cauda, onda passando
#   pelo corpo, braços para trás colados ao corpo (balançando de leve);
# - Aq_Swim_Idle_Loop: parada flutuando deitada, braços abertos "remando" devagar, cauda mexendo pouco.
# A base (corpo deitado e altura do quadril) vem do Swim_Fwd_Loop da Quaternius (UAL1), que já
# funciona com a nossa altura na água.
UAL1 = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Quaternius\Animations\UAL1_Standard.fbx"
AQ_ANIM_EXPORT = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Aliens\Alien_Aquatico_Nado.fbx"


def _load_ual_action(name="Swim_Fwd_Loop"):
    win = bpy.context.window
    old = win.scene
    before_a = set(bpy.data.actions)
    tmp = bpy.data.scenes.new("TmpUAL")
    win.scene = tmp
    try:
        bpy.ops.import_scene.fbx(filepath=UAL1)
        keep = None
        for a in bpy.data.actions:
            if a not in before_a and a.name.split("|")[-1] == name:
                keep = a
        for a in list(bpy.data.actions):
            if a not in before_a and a is not keep:
                bpy.data.actions.remove(a)
        keep.name = "UAL_" + name
        keep.use_fake_user = False
        for o in list(tmp.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    finally:
        win.scene = old
        bpy.data.scenes.remove(tmp)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    for a in list(bpy.data.armatures):
        if a.users == 0:
            bpy.data.armatures.remove(a)
    return keep


def _rotate(pb, q):
    """Gira o osso em torno da própria cabeça. q no espaço da armadura."""
    from mathutils import Matrix
    m = pb.matrix.copy()
    t = m.translation.copy()
    pb.matrix = Matrix.Translation(t) @ q.to_matrix().to_4x4() @ Matrix.Translation(-t) @ m
    bpy.context.view_layer.update()


class _Space:
    """Converte direções/rotações do MUNDO (personagem olhando para +Y, Z para cima) para a armadura."""

    def __init__(self, arm):
        self.r = arm.matrix_world.to_quaternion()
        self.ri = self.r.inverted()
        self.arm = arm

    def vec(self, v):
        return self.ri @ v

    def quat(self, axis_world, degrees):
        from mathutils import Quaternion
        return Quaternion(self.ri @ axis_world.normalized(), math.radians(degrees))

    def diff(self, a_world, b_world):
        q = a_world.normalized().rotation_difference(b_world.normalized())
        axis, angle = q.to_axis_angle()
        from mathutils import Quaternion
        return Quaternion(self.ri @ axis, angle)

    def head(self, pb):
        return self.arm.matrix_world @ pb.head

    def dir(self, pb):
        return (self.arm.matrix_world.to_3x3() @ (pb.tail - pb.head)).normalized()


def _aim(sp, pb, direction_world, blend=1.0):
    from mathutils import Quaternion
    q = sp.diff(sp.dir(pb), direction_world)
    if blend < 1.0:
        q = Quaternion().slerp(q, blend)
    _rotate(pb, q)


_FC_CACHE = {}


def _fcurves(action, arm):
    """F-curves da ação (Blender 4.4+/5: as curvas ficam num "slot" da ação)."""
    if hasattr(action, "fcurves"):
        return action.fcurves
    key = action.name
    if key not in _FC_CACHE:
        from bpy_extras import anim_utils
        slot = action.slots[0] if len(action.slots) else action.slots.new(id_type='OBJECT', name=arm.name)
        _FC_CACHE[key] = anim_utils.action_ensure_channelbag_for_slot(action, slot).fcurves
    return _FC_CACHE[key]


def _key(action, arm, pb, f, location=False):
    fcs = _fcurves(action, arm)

    def put(path, values):
        for i, v in enumerate(values):
            fc = fcs.find(path, index=i) or fcs.new(path, index=i)
            fc.keyframe_points.insert(f, v, options={'FAST'})
    put(f'pose.bones["{pb.name}"].rotation_quaternion', pb.rotation_quaternion)
    if location:
        put(f'pose.bones["{pb.name}"].location', pb.location)


def build_swim_actions():
    """Monta a pose de nado do zero (deitada de barriga para baixo, olhando para a frente) e anima."""
    arm = bpy.data.objects["Armature"]
    pose = arm.pose.bones
    sp = _Space(arm)
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = None
    _FC_CACHE.clear()
    for n in ("Aq_Swim_Fwd_Loop", "Aq_Swim_Idle_Loop"):
        old = bpy.data.actions.get(n)
        if old:
            bpy.data.actions.remove(old)

    X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, -1)) * -1
    lat = X

    def rest():
        for pb in pose:
            pb.matrix_basis.identity()
        bpy.context.view_layer.update()

    def bake(name, length, beats, kick, arms_mode, bob):
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        for f in range(length + 1):
            ph = 2 * math.pi * f / length
            w = ph * beats
            rest()
            # 1. Deita: o corpo gira 90° no quadril (cabeça para +Y, barriga para baixo) e desce o
            #    quadril até a altura da raiz (igual ao nado da Quaternius: o corpo fica na linha da água).
            _rotate(pose["pelvis"], sp.quat(lat, -90))
            pel = pose["pelvis"]
            from mathutils import Matrix
            target = Vector((sp.head(pel).x, sp.head(pel).y, -0.03 + bob * math.sin(w + 0.5)))
            delta = sp.vec(target - sp.head(pel))
            pel.matrix = Matrix.Translation(delta) @ pel.matrix
            bpy.context.view_layer.update()
            # 2. Onda no corpo (quadril -> peito).
            for b, amp, lag in (("pelvis", 6, 1.4), ("spine_01", 5, 0.9), ("spine_02", 4, 0.4), ("spine_03", 3, 0.0)):
                _rotate(pose[b], sp.quat(lat, amp * kick * math.sin(w + lag)))
            # 3. Cabeça levantada, olhando para a frente (compensa a onda).
            _rotate(pose["neck_01"], sp.quat(lat, 18))
            _rotate(pose["Head"], sp.quat(lat, 22 - 5 * kick * math.sin(w + 0.2)))
            # 4. Pernas juntas, esticadas para trás, batendo como cauda; pés de bailarina (nadadeira).
            for s in ("l", "r"):
                _aim(sp, pose[f"thigh_{s}"], Vector((0, -1, -0.04)))
                _aim(sp, pose[f"calf_{s}"], Vector((0, -1, -0.02)))
                _aim(sp, pose[f"foot_{s}"], Vector((0, -1, -0.25)), 0.9)
                _rotate(pose[f"thigh_{s}"], sp.quat(lat, 20 * kick * math.sin(w)))
                _rotate(pose[f"calf_{s}"], sp.quat(lat, 18 * kick * math.sin(w - 0.9)))
                _rotate(pose[f"foot_{s}"], sp.quat(lat, 25 * kick * math.sin(w - 1.7)))
            # 5. Braços.
            for s, side in (("l", -1.0), ("r", 1.0)):
                sx = 1.0 if sp.head(pose[f"upperarm_{s}"]).x > 0 else -1.0
                if arms_mode == "fwd":   # colados ao corpo, para trás, balançando de leve
                    d = Vector((sx * 0.3, -0.9, -0.12))
                    sway = 9 * math.sin(w + 0.6)
                else:                    # abertos, "remando" devagar
                    d = Vector((sx * 0.85, -0.25, -0.3))
                    sway = 18 * math.sin(ph + (0.0 if s == "l" else 0.5))
                from mathutils import Quaternion
                d = Quaternion(Z, math.radians(sway * sx)) @ d
                _aim(sp, pose[f"upperarm_{s}"], d)
                _aim(sp, pose[f"lowerarm_{s}"], d)
                _aim(sp, pose[f"hand_{s}"], d, 0.8)
            for pb in pose:
                _key(action, arm, pb, f, location=pb.name in ("root", "pelvis"))
        return action

    fwd = bake("Aq_Swim_Fwd_Loop", 40, 2, 1.0, "fwd", 0.02)
    idle = bake("Aq_Swim_Idle_Loop", 72, 1, 0.35, "idle", 0.04)
    rest()
    bpy.context.scene.frame_set(0)
    return [fwd.name, idle.name]


def export_swim_actions(path=AQ_ANIM_EXPORT):
    """Exporta só o esqueleto com os dois clipes de nado (o Unity tira os clipes deste arquivo)."""
    arm = bpy.data.objects["Armature"]
    keep = [bpy.data.actions[n] for n in ("Aq_Swim_Fwd_Loop", "Aq_Swim_Idle_Loop")]
    for a in list(bpy.data.actions):
        if a not in keep:
            bpy.data.actions.remove(a)
    arm.hide_set(False)
    _deselect_all()
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    arm.animation_data.action = keep[0]
    if hasattr(arm.animation_data, "action_slot") and len(keep[0].slots):
        arm.animation_data.action_slot = keep[0].slots[0]
    _export_fbx([arm],
        filepath=path, use_selection=True, object_types={'ARMATURE'},
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', use_armature_deform_only=False)
    arm.animation_data.action = None
    arm.hide_set(True)
    return path
