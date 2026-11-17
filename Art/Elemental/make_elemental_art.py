"""Gera a arte 2D dos PODERES ELEMENTAIS (Missão 21): ícones e texturas dos efeitos.

    python make_elemental_art.py <pasta de saída>

Saída (PNG com transparência), usada pelo jogo em Assets/Resources/Elemental:
    icon_fogo / icon_gelo / icon_raio   ícones do HUD (256 px)
    fx_glow    brilho redondo e macio          fx_flame   labareda
    fx_smoke   fumaça / névoa                  fx_spark   faísca em estrela
    fx_flake   floco de neve                   fx_ring    anel de onda de choque
    fx_beam    perfil do raio (para linhas)    fx_crack   rachaduras de gelo / chamuscado
As texturas de efeito são BRANCAS (a cor vem do jogo) e só a transparência desenha a forma.
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SS = 4  # desenha 4x maior e reduz (bordas lisas)
rng = np.random.default_rng(21)


def grid(size):
    y, x = np.mgrid[0:size, 0:size].astype(np.float32)
    u = (x + 0.5) / size * 2 - 1
    v = (y + 0.5) / size * 2 - 1
    return u, v


def save_alpha(path, alpha):
    a = np.clip(alpha, 0, 1)
    img = np.zeros(a.shape + (4,), np.uint8)
    img[..., :3] = 255
    img[..., 3] = (a * 255 + 0.5).astype(np.uint8)
    Image.fromarray(img, "RGBA").save(path)


def blur(a, radius):
    im = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(radius))
    return np.asarray(im, np.float32) / 255


def value_noise(size, cells, seed):
    r = np.random.default_rng(seed)
    g = r.random((cells + 1, cells + 1)).astype(np.float32)
    im = Image.fromarray((g * 255).astype(np.uint8), "L").resize((size, size), Image.BICUBIC)
    return np.asarray(im, np.float32) / 255


def fbm(size, seed):
    out = np.zeros((size, size), np.float32)
    amp, total = 1.0, 0.0
    for i, cells in enumerate((3, 6, 12, 24)):
        out += value_noise(size, cells, seed + i) * amp
        total += amp
        amp *= 0.5
    return out / total


# ---------- texturas de efeito ----------

def fx_glow(path, size=128):
    u, v = grid(size)
    r = np.sqrt(u * u + v * v)
    save_alpha(path, np.clip(1 - r, 0, 1) ** 2.2)


def fx_flame(path, size=256):
    u, v = grid(size)
    n = fbm(size, 5)
    # Labareda em gota: ponta fina em cima, barriga redonda embaixo, borda comida por ruído.
    t = np.clip((v + 0.86) / 1.6, 0, 1)          # 0 = ponta, 1 = base
    width = 0.56 * t ** 0.8 * np.sqrt(np.clip(1 - t ** 5, 0, 1))
    sway = 0.10 * np.sin(t * 5.0 + 0.6) * (1 - t)  # a ponta dança para o lado
    edge = (width - np.abs(u - sway)) + (n - 0.5) * 0.22 * (0.4 + (1 - t))
    a = np.clip(edge / 0.16, 0, 1)
    core = np.clip(edge / 0.42, 0, 1) ** 1.5
    a = np.clip(a * 0.55 + core * 0.6, 0, 1) * (t > 0) * (t < 1)
    save_alpha(path, blur(a, 2.5))


def fx_smoke(path, size=256):
    u, v = grid(size)
    a = np.zeros((size, size), np.float32)
    for _ in range(11):
        cx, cy = rng.uniform(-0.35, 0.35, 2)
        s = rng.uniform(0.22, 0.42)
        a += np.exp(-((u - cx) ** 2 + (v - cy) ** 2) / (2 * s * s)) * rng.uniform(0.35, 0.7)
    a = a / a.max()
    a = a * (0.65 + 0.35 * fbm(size, 9))
    r = np.sqrt(u * u + v * v)
    save_alpha(path, a * np.clip((1 - r) * 2.2, 0, 1))


def fx_spark(path, size=128):
    u, v = grid(size)
    r = np.sqrt(u * u + v * v)
    arm = np.exp(-(v / 0.035) ** 2) * np.clip(1 - np.abs(u), 0, 1) ** 1.5
    arm2 = np.exp(-(u / 0.035) ** 2) * np.clip(1 - np.abs(v), 0, 1) ** 1.5
    core = np.exp(-(r / 0.16) ** 2)
    save_alpha(path, np.clip(arm + arm2 * 0.8 + core, 0, 1))


def fx_flake(path, size=128):
    big = size * SS
    im = Image.new("L", (big, big), 0)
    d = ImageDraw.Draw(im)
    c = big / 2
    for k in range(6):
        ang = math.radians(60 * k + 30)
        ex, ey = c + math.cos(ang) * big * 0.44, c + math.sin(ang) * big * 0.44
        d.line([(c, c), (ex, ey)], fill=255, width=int(big * 0.05))
        for t, ln in ((0.45, 0.16), (0.7, 0.11)):
            bx, by = c + math.cos(ang) * big * 0.44 * t, c + math.sin(ang) * big * 0.44 * t
            for sgn in (-1, 1):
                a2 = ang + sgn * math.radians(55)
                d.line([(bx, by), (bx + math.cos(a2) * big * ln, by + math.sin(a2) * big * ln)], fill=255, width=int(big * 0.04))
    d.ellipse([c - big * 0.07, c - big * 0.07, c + big * 0.07, c + big * 0.07], fill=255)
    im = im.resize((size, size), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.6))
    a = np.asarray(im, np.float32) / 255
    save_alpha(path, np.clip(a * 1.15 + blur(a, 5) * 0.5, 0, 1))


def fx_ring(path, size=256):
    u, v = grid(size)
    r = np.sqrt(u * u + v * v)
    ang = np.arctan2(v, u)
    wob = 0.012 * np.sin(ang * 7) + 0.008 * np.sin(ang * 13 + 1.0)
    ring = np.exp(-((r - 0.78 - wob) / 0.05) ** 2)
    inner = np.clip((r - 0.35) / 0.43, 0, 1) ** 3 * (r < 0.78) * 0.35
    save_alpha(path, np.clip(ring + inner, 0, 1))


def fx_beam(path, size=64):
    u, v = grid(size)
    core = np.exp(-(v / 0.12) ** 2)
    halo = np.exp(-(v / 0.45) ** 2) * 0.45
    save_alpha(path, np.clip(core + halo, 0, 1) * np.clip(1.02 - np.abs(v), 0, 1) ** 0.3)


def fx_crack(path, size=256):
    """Rachaduras em estrela (marca no chão: gelo estilhaçado ou chão chamuscado)."""
    big = size * SS
    im = Image.new("L", (big, big), 0)
    d = ImageDraw.Draw(im)
    c = big / 2
    r = np.random.default_rng(4)
    for k in range(11):
        ang = 2 * math.pi * k / 11 + r.uniform(-0.2, 0.2)
        x, y = c, c
        length = big * r.uniform(0.28, 0.46)
        steps = 7
        for s in range(steps):
            ang += r.uniform(-0.35, 0.35)
            nx, ny = x + math.cos(ang) * length / steps, y + math.sin(ang) * length / steps
            d.line([(x, y), (nx, ny)], fill=255, width=max(2, int(big * 0.016 * (1 - s / steps) + 2)))
            if s in (2, 4) and r.random() < 0.7:
                a2 = ang + r.choice([-1, 1]) * r.uniform(0.5, 0.9)
                d.line([(nx, ny), (nx + math.cos(a2) * length * 0.25, ny + math.sin(a2) * length * 0.25)], fill=200, width=max(2, int(big * 0.008)))
            x, y = nx, ny
    im = im.resize((size, size), Image.LANCZOS)
    a = np.asarray(im, np.float32) / 255
    u, v = grid(size)
    rr = np.sqrt(u * u + v * v)
    stain = np.clip(1 - rr / 0.9, 0, 1) ** 1.5 * (0.55 + 0.45 * fbm(size, 31))
    save_alpha(path, np.clip(a * 0.9 + stain * 0.75, 0, 1) * np.clip((1 - rr) * 4, 0, 1))


# ---------- ícones ----------

def smooth(points, per=14):
    """Curva fechada suave (Catmull-Rom) passando pelos pontos."""
    out = []
    n = len(points)
    for i in range(n):
        p0, p1, p2, p3 = points[(i - 1) % n], points[i], points[(i + 1) % n], points[(i + 2) % n]
        for s in range(per):
            t = s / per
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[k]) + (-p0[k] + p2[k]) * t + (2 * p0[k] - 5 * p1[k] + 4 * p2[k] - p3[k]) * t2 +
                                    (-p0[k] + 3 * p1[k] - 3 * p2[k] + p3[k]) * t3) for k in (0, 1)))
    return out


def to_px(points, big, scale=1.0, dx=0.0, dy=0.0):
    return [((x * scale + dx) * 0.5 + 0.5) * big for x, y in points], [((y * scale + dy) * 0.5 + 0.5) * big for x, y in points]


def poly(draw, points, big, fill, scale=1.0, dx=0.0, dy=0.0):
    xs, ys = to_px(points, big, scale, dx, dy)
    draw.polygon(list(zip(xs, ys)), fill=fill)


def vertical_gradient(big, top, bottom, y0=0.0, y1=1.0):
    t = np.clip((np.linspace(0, 1, big) - y0) / max(1e-5, y1 - y0), 0, 1)[:, None, None]
    col = np.array(top, np.float32)[None, None, :] * (1 - t) + np.array(bottom, np.float32)[None, None, :] * t
    return np.repeat(col, big, axis=1)


def paint(mask_img, colors):
    """Imagem RGBA: 'colors' (array HxWx3 ou cor única) recortada pela máscara L."""
    big = mask_img.size[0]
    if not isinstance(colors, np.ndarray):
        colors = np.tile(np.array(colors, np.float32)[None, None, :], (big, big, 1))
    out = np.zeros((big, big, 4), np.uint8)
    out[..., :3] = np.clip(colors, 0, 255).astype(np.uint8)
    out[..., 3] = np.asarray(mask_img)
    return Image.fromarray(out, "RGBA")


def badge(big, rim, glow):
    """Fundo do ícone: disco escuro com aro na cor do elemento e brilho interno."""
    base = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    m = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m).ellipse([big * 0.04, big * 0.04, big * 0.96, big * 0.96], fill=255)
    base.alpha_composite(paint(m, rim))
    m2 = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m2).ellipse([big * 0.085, big * 0.085, big * 0.915, big * 0.915], fill=255)
    u, v = grid(big)
    r = np.sqrt(u * u + (v - 0.15) ** 2)
    t = np.clip(1 - r / 0.95, 0, 1)[..., None] ** 1.4
    inner = np.array((16, 14, 34), np.float32)[None, None, :] * (1 - t * 0.75) + np.array(glow, np.float32)[None, None, :] * t * 0.75
    base.alpha_composite(paint(m2, inner))
    return base


def shape_mask(big, points, scale=1.0, dx=0.0, dy=0.0):
    m = Image.new("L", (big, big), 0)
    poly(ImageDraw.Draw(m), points, big, 255, scale, dx, dy)
    return m


def add_glow(base, mask, color, radius, strength=1.0):
    g = mask.filter(ImageFilter.GaussianBlur(radius))
    a = (np.asarray(g, np.float32) * strength).clip(0, 255).astype(np.uint8)
    base.alpha_composite(paint(Image.fromarray(a, "L"), color))


def finish(base, path, size=256):
    base.resize((size, size), Image.LANCZOS).save(path)


FLAME = [(0.0, -0.80), (0.20, -0.52), (0.42, -0.30), (0.56, 0.02), (0.54, 0.34), (0.38, 0.60), (0.12, 0.74), (-0.14, 0.74),
         (-0.40, 0.60), (-0.56, 0.32), (-0.54, 0.02), (-0.40, -0.16), (-0.30, 0.04), (-0.20, -0.20), (-0.16, -0.50)]
FLAME_IN = [(0.02, -0.26), (0.20, -0.04), (0.30, 0.22), (0.24, 0.48), (0.06, 0.62), (-0.12, 0.60), (-0.26, 0.44),
            (-0.28, 0.22), (-0.14, 0.02), (-0.06, -0.10)]
BOLT = [(0.16, -0.80), (-0.42, 0.06), (-0.06, 0.06), (-0.22, 0.80), (0.44, -0.16), (0.06, -0.16), (0.36, -0.80)]


def icon_fogo(path):
    big = 256 * SS
    base = badge(big, (255, 122, 40), (150, 40, 10))
    outer = shape_mask(big, smooth(FLAME), 0.78, 0.0, 0.03)
    add_glow(base, outer, (255, 90, 20), big * 0.05, 0.9)
    base.alpha_composite(paint(outer, vertical_gradient(big, (255, 208, 60), (232, 52, 24), 0.15, 0.85)))
    inner = shape_mask(big, smooth(FLAME_IN), 0.78, 0.0, 0.05)
    base.alpha_composite(paint(inner, vertical_gradient(big, (255, 250, 200), (255, 176, 40), 0.35, 0.8)))
    finish(base, path)


def icon_gelo(path):
    big = 256 * SS
    base = badge(big, (120, 220, 255), (20, 70, 130))
    m = Image.new("L", (big, big), 0)
    d = ImageDraw.Draw(m)
    c = big / 2
    R = big * 0.33

    def line(a, b, w):
        d.line([a, b], fill=255, width=int(w))
        for p in (a, b):
            d.ellipse([p[0] - w / 2, p[1] - w / 2, p[0] + w / 2, p[1] + w / 2], fill=255)

    for k in range(6):
        ang = math.radians(60 * k - 90)
        e = (c + math.cos(ang) * R, c + math.sin(ang) * R)
        line((c, c), e, big * 0.052)
        for t, ln in ((0.48, 0.115), (0.76, 0.08)):
            b = (c + math.cos(ang) * R * t, c + math.sin(ang) * R * t)
            for sgn in (-1, 1):
                a2 = ang + sgn * math.radians(52)
                line(b, (b[0] + math.cos(a2) * big * ln, b[1] + math.sin(a2) * big * ln), big * 0.04)
    hexagon = [(c + math.cos(math.radians(60 * k)) * big * 0.085, c + math.sin(math.radians(60 * k)) * big * 0.085) for k in range(6)]
    d.polygon(hexagon, fill=255)
    add_glow(base, m, (90, 200, 255), big * 0.04, 1.0)
    base.alpha_composite(paint(m, vertical_gradient(big, (255, 255, 255), (150, 226, 255), 0.2, 0.85)))
    finish(base, path)


def icon_raio(path):
    big = 256 * SS
    base = badge(big, (255, 226, 70), (120, 90, 10))
    outer = shape_mask(big, BOLT, 0.80)
    add_glow(base, outer, (255, 230, 80), big * 0.05, 1.0)
    edge = shape_mask(big, BOLT, 0.86)
    base.alpha_composite(paint(edge, (214, 120, 10)))
    base.alpha_composite(paint(outer, vertical_gradient(big, (255, 255, 190), (255, 196, 30), 0.15, 0.85)))
    core = shape_mask(big, BOLT, 0.52, 0.0, -0.02)
    base.alpha_composite(paint(core.filter(ImageFilter.GaussianBlur(big * 0.01)), (255, 255, 240)))
    finish(base, path)


def main(out):
    os.makedirs(out, exist_ok=True)
    for name, fn in (("fx_glow", fx_glow), ("fx_flame", fx_flame), ("fx_smoke", fx_smoke), ("fx_spark", fx_spark),
                     ("fx_flake", fx_flake), ("fx_ring", fx_ring), ("fx_beam", fx_beam), ("fx_crack", fx_crack),
                     ("icon_fogo", icon_fogo), ("icon_gelo", icon_gelo), ("icon_raio", icon_raio)):
        fn(os.path.join(out, name + ".png"))
    print("ok:", out)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "out")
