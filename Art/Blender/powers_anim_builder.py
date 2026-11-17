"""Animações dos PODERES ELEMENTAIS do protagonista (fogo, gelo, eletricidade).

Como usar (Blender 5.1, com o Protagonista_H19.blend aberto):
    exec(open(r"C:\\WorkSpace\\ImNotAnAlien\\Art\\Blender\\powers_anim_builder.py", encoding="utf-8").read())
    build_power_actions()      # cria as 6 ações na armadura
    export_power_actions()     # exporta Protagonista_Poderes.fbx (só esqueleto + clipes) para o Unity

As poses são montadas por código a partir do T-pose (sem depender de outra animação):
    El_Fogo_Carga_Loop   mão direita ao lado do corpo, palma para cima, segurando a bola de fogo
    El_Fogo_Disparo      puxa o braço para trás e arremessa para a frente
    El_Gelo_Carga_Loop   as duas mãos na frente do peito, palmas uma para a outra
    El_Gelo_Disparo      recolhe e empurra as duas palmas para a frente
    El_Raio_Carga_Loop   braço direito erguido para o céu, mão em garra, tremendo
    El_Raio_Disparo      o braço desce apontando o dedo para a frente, com um coice
No Unity elas tocam só na camada dos BRAÇOS (cabeça, braços e dedos): pernas e tronco seguem a
animação de andar/correr. Mundo: personagem olha para +Y, direita = +X, cima = +Z.
"""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector

POWERS_EXPORT = r"C:\WorkSpace\ImNotAnAlien\Game\ImNotAnAlien\Assets\Art\Characters\Protagonista\Protagonista_Poderes.fbx"
POWER_ACTIONS = ("El_Fogo_Carga_Loop", "El_Fogo_Disparo", "El_Gelo_Carga_Loop", "El_Gelo_Disparo",
                 "El_Raio_Carga_Loop", "El_Raio_Disparo")
FINGERS = ("index", "middle", "ring", "pinky")


# ---------- utilidades ----------

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


class _Rig:
    """Posa os ossos usando direções do MUNDO."""

    def __init__(self, arm):
        self.arm = arm
        self.pose = arm.pose.bones
        self.r = arm.matrix_world.to_quaternion()
        self.ri = self.r.inverted()
        self.rest()
        # "Palma" de cada osso do braço no T-pose = para baixo (-Z do mundo), guardada no espaço do osso.
        self.palm_local = {}
        for s in ("l", "r"):
            for n in (f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"):
                pb = self.pose[n]
                self.palm_local[n] = pb.matrix.to_3x3().inverted() @ (self.ri @ Vector((0, 0, -1)))

    def rest(self):
        for pb in self.pose:
            pb.matrix_basis.identity()
        bpy.context.view_layer.update()

    def dir(self, pb):
        return (self.r @ (pb.tail - pb.head)).normalized()

    def rotate(self, pb, q_world):
        """Gira o osso em torno da própria cabeça (rotação dada no mundo)."""
        axis, angle = q_world.to_axis_angle()
        q = Quaternion(self.ri @ axis, angle)
        m = pb.matrix.copy()
        t = m.translation.copy()
        pb.matrix = Matrix.Translation(t) @ q.to_matrix().to_4x4() @ Matrix.Translation(-t) @ m
        bpy.context.view_layer.update()

    def turn(self, name, axis_world, degrees):
        self.rotate(self.pose[name], Quaternion(Vector(axis_world).normalized(), math.radians(degrees)))

    def aim(self, name, direction, palm=None, roll_blend=1.0):
        """Aponta o osso para 'direction'; se 'palm' for dado, gira no próprio eixo até a palma olhar para lá."""
        pb = self.pose[name]
        d = Vector(direction).normalized()
        self.rotate(pb, self.dir(pb).rotation_difference(d))
        if palm is None or name not in self.palm_local:
            return
        now = self.r @ (pb.matrix.to_3x3() @ self.palm_local[name])
        want = Vector(palm)
        now = (now - d * now.dot(d))
        want = (want - d * want.dot(d))
        if now.length < 1e-4 or want.length < 1e-4:
            return
        angle = now.normalized().angle(want.normalized())
        if now.normalized().cross(want.normalized()).dot(d) < 0:
            angle = -angle
        self.rotate(pb, Quaternion(d, angle * roll_blend))

    def fingers(self, s, curl, index=None, thumb=None, spread=0.0):
        """Fecha os dedos (0 = aberto, 1 = punho). Precisa ser chamado ANTES de posar o braço."""
        sign = 1.0 if s == "r" else -1.0
        for i, f in enumerate(FINGERS):
            c = curl if (index is None or f != "index") else index
            for j, deg in ((1, 62), (2, 78), (3, 55)):
                self.turn(f"{f}_0{j}_{s}", (0, 1, 0), sign * deg * c)
            if spread:
                self.turn(f"{f}_01_{s}", (0, 0, 1), sign * spread * (1.5 - i))
        t = curl if thumb is None else thumb
        self.turn(f"thumb_01_{s}", (1, 0, 0), -38 * t)
        self.turn(f"thumb_02_{s}", (0, 0, 1), sign * 30 * t)
        self.turn(f"thumb_03_{s}", (0, 0, 1), sign * 35 * t)


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


def _key(action, arm, pb, f):
    fcs = _fcurves(action, arm)
    path = f'pose.bones["{pb.name}"].rotation_quaternion'
    for i, v in enumerate(pb.rotation_quaternion):
        fc = fcs.find(path, index=i) or fcs.new(path, index=i)
        fc.keyframe_points.insert(f, v, options={'FAST'})


# ---------- poses ----------
# Cada braço: up (braço), low (antebraço), hand (mão), palm (para onde a palma olha), curl (dedos),
# index/thumb (dedos separados, opcional), spread (dedos abertos em leque).
# Tudo descrito para o braço DIREITO; o esquerdo é espelhado (x trocado) quando marcado "mirror".

def _arm(up, low, hand=None, palm=None, curl=0.25, index=None, thumb=None, spread=0.0, shoulder=0.0):
    return {"up": Vector(up), "low": Vector(low), "hand": Vector(hand if hand is not None else low),
            "palm": Vector(palm) if palm is not None else None, "curl": curl,
            "index": curl if index is None else index, "thumb": curl if thumb is None else thumb,
            "spread": spread, "shoulder": shoulder}


def _mirror(a):
    def m(v):
        return None if v is None else Vector((-v.x, v.y, v.z))
    b = dict(a)
    for k in ("up", "low", "hand", "palm"):
        b[k] = m(a[k])
    return b


def _mix(a, b, t):
    out = {}
    for k in ("up", "low", "hand"):
        out[k] = a[k].normalized().slerp(b[k].normalized(), t)
    if a["palm"] is not None and b["palm"] is not None:
        out["palm"] = a["palm"].normalized().slerp(b["palm"].normalized(), t)
    else:
        out["palm"] = b["palm"] if t > 0.5 else a["palm"]
    for k in ("curl", "index", "thumb", "spread", "shoulder"):
        out[k] = a[k] + (b[k] - a[k]) * t
    return out


def _ease(t):
    return t * t * (3 - 2 * t)


def _sample(keys, f):
    """keys = [(quadro, pose, modo)] — modo 'ease' (suave), 'in' (acelera: golpe) ou 'out' (freia)."""
    if f <= keys[0][0]:
        return keys[0][1]
    for (f0, p0, _), (f1, p1, mode) in zip(keys, keys[1:]):
        if f <= f1:
            t = (f - f0) / float(f1 - f0)
            t = t * t if mode == "in" else (1 - (1 - t) ** 2 if mode == "out" else _ease(t))
            return _mix(p0, p1, t)
    return keys[-1][1]


def _apply(rig, s, a):
    rig.fingers(s, a["curl"], index=a["index"], thumb=a["thumb"], spread=a["spread"])
    if a["shoulder"]:
        rig.turn(f"clavicle_{s}", (0, 1, 0), (-1 if s == "r" else 1) * a["shoulder"])  # ombro sobe
    palm = a["palm"]
    rig.aim(f"upperarm_{s}", a["up"], palm, 0.35)
    rig.aim(f"lowerarm_{s}", a["low"], palm, 0.75)
    rig.aim(f"hand_{s}", a["hand"], palm, 1.0)


# Braço solto ao lado do corpo (fim dos disparos; o Unity mistura de volta com a animação normal).
DOWN = _arm((0.16, 0.02, -0.98), (0.10, 0.16, -0.98), palm=(-1, 0, 0), curl=0.3)
DOWN_L = _mirror(DOWN)

# Fogo: bola de fogo na palma, ao lado do corpo.
FIRE_HOLD = _arm((0.36, -0.12, -0.92), (0.42, 0.80, 0.12), hand=(0.35, 0.90, 0.18), palm=(0, 0, 1), curl=0.42, thumb=0.2, spread=6)
FIRE_BACK = _arm((0.62, -0.62, -0.20), (0.30, -0.25, 0.92), hand=(0.2, -0.45, 0.85), palm=(0, 1, 0.3), curl=0.5, thumb=0.3, spread=6, shoulder=10)
FIRE_THROW = _arm((0.20, 0.96, 0.12), (0.04, 1.0, 0.04), hand=(0.0, 0.92, 0.35), palm=(0, 1, -0.3), curl=0.0, spread=10, shoulder=6)
FIRE_FOLLOW = _arm((0.02, 0.86, -0.42), (-0.22, 0.86, -0.42), hand=(-0.2, 0.8, -0.55), palm=(0, 0.2, -1), curl=0.15, spread=6)
FIRE_L_HOLD = _mirror(_arm((0.30, 0.05, -0.94), (0.22, 0.42, -0.86), palm=(-1, 0, 0), curl=0.55))
FIRE_L_BACK = _mirror(_arm((0.34, 0.50, -0.70), (0.10, 0.90, 0.10), palm=(0, 0, -1), curl=0.35))
FIRE_L_THROW = _mirror(_arm((0.46, -0.52, -0.70), (0.42, -0.35, -0.82), palm=(-1, 0, 0), curl=0.7))

# Gelo: as duas mãos juntas na frente do peito; depois empurra as palmas para a frente.
ICE_HOLD = _arm((0.50, 0.36, -0.80), (-0.52, 0.80, 0.26), hand=(-0.25, 0.90, 0.35), palm=(-1, 0.1, 0), curl=0.34, thumb=0.1, spread=8)
ICE_PULL = _arm((0.50, -0.30, -0.80), (-0.10, 0.70, 0.70), hand=(0.0, 0.35, 0.95), palm=(0, 1, 0), curl=0.25, thumb=0.1, spread=8, shoulder=6)
ICE_PUSH = _arm((0.16, 0.98, 0.10), (-0.06, 1.0, 0.06), hand=(0.0, 0.22, 1.0), palm=(0, 1, 0), curl=0.0, thumb=0.0, spread=14, shoulder=8)

# Raio: braço direito para o céu juntando energia; depois desce apontando.
BOLT_UP = _arm((0.30, 0.02, 0.95), (0.06, 0.04, 1.0), hand=(0.0, 0.18, 1.0), palm=(0, 1, 0), curl=0.5, thumb=0.35, spread=12, shoulder=22)
BOLT_POINT = _arm((0.13, 0.98, 0.10), (0.02, 1.0, 0.03), hand=(0.0, 1.0, 0.0), palm=(0, 0, -1), curl=1.0, index=0.0, thumb=0.0, shoulder=8)
BOLT_KICK = _arm((0.15, 0.92, 0.36), (0.03, 0.90, 0.44), hand=(0.0, 0.88, 0.48), palm=(0, 0, -1), curl=1.0, index=0.0, thumb=0.0, shoulder=12)
BOLT_L_UP = _mirror(_arm((0.34, -0.18, -0.92), (0.30, 0.22, -0.92), palm=(-1, 0, 0), curl=1.0))
BOLT_L_POINT = _mirror(_arm((0.40, -0.40, -0.82), (0.36, -0.05, -0.93), palm=(-1, 0, 0), curl=1.0))


def build_power_actions():
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    rig = _Rig(arm)
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = None
    _FC_CACHE.clear()
    for n in POWER_ACTIONS:
        old = bpy.data.actions.get(n)
        if old:
            bpy.data.actions.remove(old)

    def bake(name, length, right, left, extra=None):
        """right/left: função quadro -> pose do braço. extra(rig, quadro): ajustes (tremor, cabeça)."""
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        for f in range(length + 1):
            rig.rest()
            _apply(rig, "r", right(f))
            _apply(rig, "l", left(f))
            if extra:
                extra(rig, f)
            for pb in rig.pose:
                _key(action, arm, pb, f)
        return name

    def loop(length, f, k=1):
        return 2 * math.pi * k * f / length

    def wobble(base, length, amp, k=1, phase=0.0):
        """Pose que balança de leve em loop (some e volta igual no último quadro)."""
        def pose(f):
            w = loop(length, f, k) + phase
            p = dict(base)
            p["low"] = base["low"] + Vector((0, 0, amp * math.sin(w)))
            p["hand"] = base["hand"] + Vector((0, 0, amp * 1.4 * math.sin(w + 0.6)))
            p["up"] = base["up"] + Vector((0, 0, amp * 0.3 * math.sin(w - 0.4)))
            return p
        return pose

    def tremble(names, length, deg, k=6):
        def extra(r, f):
            w = loop(length, f, k)
            for i, n in enumerate(names):
                r.turn(n, (0, 1, 0), deg * math.sin(w + i * 1.7))
                r.turn(n, (1, 0, 0), deg * 0.6 * math.sin(w * 0.5 + i))
        return extra

    done = []
    # ---- Fogo ----
    done.append(bake("El_Fogo_Carga_Loop", 48, wobble(FIRE_HOLD, 48, 0.05), wobble(FIRE_L_HOLD, 48, 0.02, phase=1.0)))
    fire_r = [(0, FIRE_HOLD, "ease"), (3, FIRE_BACK, "out"), (6, FIRE_THROW, "in"), (9, FIRE_FOLLOW, "out"), (18, DOWN, "ease")]
    fire_l = [(0, FIRE_L_HOLD, "ease"), (3, FIRE_L_BACK, "out"), (6, FIRE_L_THROW, "in"), (10, FIRE_L_THROW, "ease"), (18, DOWN_L, "ease")]
    done.append(bake("El_Fogo_Disparo", 18, lambda f: _sample(fire_r, f), lambda f: _sample(fire_l, f)))

    # ---- Gelo ----
    ice_l = _mirror(ICE_HOLD)
    done.append(bake("El_Gelo_Carga_Loop", 48, wobble(ICE_HOLD, 48, 0.03), wobble(ice_l, 48, 0.03),
                     tremble(("lowerarm_r", "lowerarm_l"), 48, 0.8)))
    ice_r = [(0, ICE_HOLD, "ease"), (2, ICE_PULL, "out"), (5, ICE_PUSH, "in"), (10, ICE_PUSH, "ease"), (18, DOWN, "ease")]
    done.append(bake("El_Gelo_Disparo", 18, lambda f: _sample(ice_r, f), lambda f: _mirror(_sample(ice_r, f))))

    # ---- Raio ----
    done.append(bake("El_Raio_Carga_Loop", 48, wobble(BOLT_UP, 48, 0.02, k=2), wobble(BOLT_L_UP, 48, 0.01),
                     tremble(("lowerarm_r", "hand_r"), 48, 2.2, k=8)))
    bolt_r = [(0, BOLT_UP, "ease"), (4, BOLT_POINT, "in"), (6, BOLT_KICK, "out"), (9, BOLT_POINT, "ease"),
              (12, BOLT_POINT, "ease"), (20, DOWN, "ease")]
    bolt_l = [(0, BOLT_L_UP, "ease"), (4, BOLT_L_POINT, "in"), (12, BOLT_L_POINT, "ease"), (20, DOWN_L, "ease")]
    done.append(bake("El_Raio_Disparo", 20, lambda f: _sample(bolt_r, f), lambda f: _sample(bolt_l, f)))

    rig.rest()
    bpy.context.scene.frame_set(0)
    return done


def show_power_pose(action_name, frame):
    """Deixa a armadura na pose de um quadro (para prévias)."""
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    action = bpy.data.actions[action_name]
    arm.animation_data.action = action
    if hasattr(arm.animation_data, "action_slot") and len(action.slots):
        arm.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()


def export_power_actions(path=POWERS_EXPORT):
    """Exporta só o esqueleto com os clipes dos poderes (o Unity tira os clipes deste arquivo)."""
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    keep = [bpy.data.actions[n] for n in POWER_ACTIONS]
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
    return path
