"""Pomocnicze funkcje rysowania: czcionki, tekst z obrysem, poświata, ikony, postacie."""
import math
import pygame

from .config import INK, resource_path, WHITE

_fonts = {}
_text_cache = {}
_glow_cache = {}
_shadow_cache = {}
_vignette_cache = {}
_FONT_HEAVY = None
_FONT_BOLD = None


def init():
    global _FONT_HEAVY, _FONT_BOLD
    p1 = resource_path("assets", "fonts", "Inter-Black.otf")
    p2 = resource_path("assets", "fonts", "Inter-Bold.otf")
    try:
        pygame.font.Font(p1, 12)
        _FONT_HEAVY = p1
    except Exception:
        _FONT_HEAVY = None
    try:
        pygame.font.Font(p2, 12)
        _FONT_BOLD = p2
    except Exception:
        _FONT_BOLD = None


def font(size, heavy=True):
    key = (size, heavy)
    f = _fonts.get(key)
    if f is None:
        path = _FONT_HEAVY if heavy else _FONT_BOLD
        try:
            f = pygame.font.Font(path, size) if path else pygame.font.Font(None, int(size * 1.35))
        except Exception:
            f = pygame.font.Font(None, int(size * 1.35))
        _fonts[key] = f
    return f


def text_surf(s, size, color=WHITE, outline=2, ocolor=INK, heavy=True):
    key = (s, size, color, outline, ocolor, heavy)
    surf = _text_cache.get(key)
    if surf is not None:
        return surf
    if len(_text_cache) > 3000:
        _text_cache.clear()
    f = font(size, heavy)
    base = f.render(s, True, color)
    if outline > 0:
        o = outline
        w, h = base.get_width() + o * 2, base.get_height() + o * 2
        surf = pygame.Surface((w, h), pygame.SRCALPHA)
        osurf = f.render(s, True, ocolor)
        steps = 12 if o > 2 else 8
        for i in range(steps):
            a = i / steps * math.tau
            surf.blit(osurf, (o + round(math.cos(a) * o), o + round(math.sin(a) * o)))
        surf.blit(base, (o, o))
    else:
        surf = base
    _text_cache[key] = surf
    return surf


def draw_text(surf, s, x, y, size=20, color=WHITE, anchor="topleft", outline=2,
              ocolor=INK, heavy=True, alpha=None):
    ts = text_surf(str(s), size, color, outline, ocolor, heavy)
    r = ts.get_rect(**{anchor: (int(x), int(y))})
    if alpha is not None and alpha < 255:
        ts = ts.copy()
        ts.set_alpha(max(0, int(alpha)))
    surf.blit(ts, r)
    return r


def wrap_text(s, size, max_w, heavy=False):
    f = font(size, heavy)
    words = s.split()
    lines, cur = [], ""
    for w in words:
        t = (cur + " " + w).strip()
        if f.size(t)[0] <= max_w:
            cur = t
        else:
            if cur:
                lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return lines


def lerp(a, b, t):
    return a + (b - a) * t


def lerp_color(a, b, t):
    t = max(0.0, min(1.0, t))
    return (int(a[0] + (b[0] - a[0]) * t), int(a[1] + (b[1] - a[1]) * t), int(a[2] + (b[2] - a[2]) * t))


def mul_color(c, f):
    return (max(0, min(255, int(c[0] * f))), max(0, min(255, int(c[1] * f))), max(0, min(255, int(c[2] * f))))


def glow(radius, color):
    radius = max(2, int(radius))
    key = (radius, color)
    s = _glow_cache.get(key)
    if s is None:
        if len(_glow_cache) > 400:
            _glow_cache.clear()
        s = pygame.Surface((radius * 2, radius * 2))
        s.fill((0, 0, 0))
        steps = min(radius, 14)
        for i in range(steps, 0, -1):
            f = i / steps
            k = (1 - f) ** 1.8 * 0.55
            pygame.draw.circle(s, mul_color(color, k), (radius, radius), max(1, int(radius * f)))
        _glow_cache[key] = s
    return s


def blit_glow(surf, x, y, radius, color):
    g = glow(radius, color)
    surf.blit(g, (int(x) - g.get_width() // 2, int(y) - g.get_height() // 2), special_flags=pygame.BLEND_RGB_ADD)


def shadow(surf, x, y, w, h, alpha=70):
    key = (int(w), int(h), alpha)
    s = _shadow_cache.get(key)
    if s is None:
        s = pygame.Surface((max(2, int(w)), max(2, int(h))), pygame.SRCALPHA)
        pygame.draw.ellipse(s, (0, 0, 0, alpha), s.get_rect())
        _shadow_cache[key] = s
    surf.blit(s, (int(x - w / 2), int(y - h / 2)))


def vignette(w, h, color=(0, 0, 0), strength=1.0):
    key = (w, h, color, strength)
    v = _vignette_cache.get(key)
    if v is None:
        sw, sh = 128, 72
        small = pygame.Surface((sw, sh), pygame.SRCALPHA)
        for yy in range(sh):
            for xx in range(sw):
                dx = (xx - sw / 2) / (sw / 2)
                dy = (yy - sh / 2) / (sh / 2)
                d = math.sqrt(dx * dx * 0.8 + dy * dy * 0.9)
                a = max(0.0, min(1.0, (d - 0.55) / 0.6)) ** 1.6
                small.set_at((xx, yy), (*color, int(255 * a * strength)))
        v = pygame.transform.smoothscale(small, (w, h))
        _vignette_cache[key] = v
    return v


def panel(surf, rect, color=(34, 28, 46), border=(70, 60, 90), radius=12, width=3, alpha=None):
    rect = pygame.Rect(rect)
    if alpha is not None:
        s = pygame.Surface(rect.size, pygame.SRCALPHA)
        pygame.draw.rect(s, (*color, alpha), s.get_rect(), border_radius=radius)
        surf.blit(s, rect.topleft)
    else:
        pygame.draw.rect(surf, color, rect, border_radius=radius)
    if width:
        pygame.draw.rect(surf, border, rect, width, border_radius=radius)


def bar(surf, rect, frac, color, bg=(30, 24, 36), border=INK, radius=6):
    rect = pygame.Rect(rect)
    pygame.draw.rect(surf, bg, rect, border_radius=radius)
    frac = max(0.0, min(1.0, frac))
    if frac > 0:
        r2 = rect.copy()
        r2.width = max(radius * 2 if frac > 0.02 else 2, int(rect.width * frac))
        pygame.draw.rect(surf, color, r2, border_radius=radius)
        hl = r2.copy()
        hl.height = max(2, rect.height // 3)
        hl.y += 2
        hl.x += 3
        hl.width = max(0, hl.width - 6)
        if hl.width > 0:
            pygame.draw.rect(surf, lerp_color(color, (255, 255, 255), 0.35), hl, border_radius=radius)
    pygame.draw.rect(surf, border, rect, 3, border_radius=radius)


def rot(cx, cy, px, py, ca, sa):
    return (cx + px * ca - py * sa, cy + px * sa + py * ca)


def poly(surf, color, pts, outline=2):
    if len(pts) < 3:
        return
    pygame.draw.polygon(surf, color, pts)
    if outline:
        pygame.draw.polygon(surf, INK, pts, outline)


def outlined_circle(surf, color, pos, r, outline=3):
    pygame.draw.circle(surf, INK, (int(pos[0]), int(pos[1])), int(r + outline))
    pygame.draw.circle(surf, color, (int(pos[0]), int(pos[1])), max(1, int(r)))


def outlined_ellipse(surf, color, cx, cy, rw, rh, outline=3):
    pygame.draw.ellipse(surf, INK, (cx - rw - outline, cy - rh - outline, (rw + outline) * 2, (rh + outline) * 2))
    pygame.draw.ellipse(surf, color, (cx - rw, cy - rh, rw * 2, rh * 2))


def eyes(surf, x, y, r, aim, white=(250, 250, 250), pupil=INK, spacing=0.38, size=0.24, angry=False,
         glow_col=None):
    dx, dy = math.cos(aim), math.sin(aim)
    ex = x + dx * r * 0.22
    ey = y - r * 0.12 + dy * r * 0.12
    sp = r * spacing
    for side in (-1, 1):
        cx = ex + side * sp
        if glow_col:
            blit_glow(surf, cx, ey, r * 0.5, glow_col)
            pygame.draw.circle(surf, glow_col, (int(cx), int(ey)), max(2, int(r * 0.16)))
            continue
        ew, eh = r * size, r * size * 1.2
        pygame.draw.ellipse(surf, white, (cx - ew, ey - eh, ew * 2, eh * 2))
        pr = max(2, r * size * 0.55)
        pygame.draw.circle(surf, pupil, (int(cx + dx * ew * 0.4), int(ey + dy * eh * 0.35)), int(pr))
        if angry:
            bx = cx
            by = ey - eh - 2
            pygame.draw.line(surf, INK, (bx - ew * 1.1, by - side * 3 - 2), (bx + ew * 1.1, by + side * 3 - 2), 3)


# --------------------------------------------------------------------- Postacie

def _weapon(surf, key, x, y, r, aim, t, accent):
    ca, sa = math.cos(aim), math.sin(aim)
    hx, hy = x + ca * r * 0.75, y + sa * r * 0.75 + 4

    def P(px, py):
        return rot(hx, hy, px, py, ca, sa)

    if key == "rycerz":
        poly(surf, (210, 215, 225), [P(8, -3), P(34, -3), P(40, 0), P(34, 3), P(8, 3)])
        poly(surf, (230, 180, 60), [P(6, -9), P(10, -9), P(10, 9), P(6, 9)])
        poly(surf, (120, 80, 50), [P(-2, -2.5), P(6, -2.5), P(6, 2.5), P(-2, 2.5)])
    elif key == "lowczyni":
        pts = []
        for i in range(9):
            u = -1 + i / 4
            pts.append(P(6 + 9 * (1 - u * u), u * 17))
        pygame.draw.lines(surf, INK, False, pts, 7)
        pygame.draw.lines(surf, (150, 100, 60), False, pts, 4)
        pygame.draw.line(surf, (235, 235, 220), pts[0], pts[-1], 1)
    elif key == "berserker":
        poly(surf, (120, 80, 50), [P(-2, -2.5), P(30, -2.5), P(30, 2.5), P(-2, 2.5)])
        poly(surf, (200, 205, 215), [P(20, -3), P(32, -14), P(36, -4), P(36, 4), P(32, 14), P(20, 3)])
    elif key == "mag":
        poly(surf, (110, 70, 45), [P(-6, -2.5), P(32, -2.5), P(32, 2.5), P(-6, 2.5)])
        ox, oy = P(37, 0)
        blit_glow(surf, ox, oy, 18 + math.sin(t * 6) * 3, accent)
        outlined_circle(surf, accent, (ox, oy), 6, 2)
        pygame.draw.circle(surf, (255, 255, 255), (int(ox - 2), int(oy - 2)), 2)
    elif key == "alchemik":
        bx, by = P(16, 0)
        poly(surf, (200, 230, 230), [P(4, -3), P(10, -3), P(10, 3), P(4, 3)])
        outlined_circle(surf, (120, 230, 90), (bx, by), 7, 2)
        pygame.draw.circle(surf, (210, 255, 180), (int(bx - 2), int(by - 2)), 2)
    elif key == "zlodziej":
        poly(surf, (220, 225, 235), [P(6, -2.5), P(22, -1.5), P(26, 0), P(22, 1.5), P(6, 2.5)])
        poly(surf, (170, 40, 50), [P(3, -6), P(6, -6), P(6, 6), P(3, 6)])
    elif key == "upadly":
        poly(surf, (60, 45, 60), [P(-6, -2), P(36, -2), P(36, 2), P(-6, 2)])
        poly(surf, (200, 40, 60), [P(34, -2), P(28, -20), P(18, -26), P(32, -24), P(40, -10), P(38, 0)])
    else:
        poly(surf, (200, 200, 210), [P(6, -3), P(26, -3), P(26, 3), P(6, 3)])


def draw_character(surf, key, x, y, r, aim, t, moving=False, flash=False, color=(200, 200, 200),
                   accent=(255, 255, 255), alpha=255):
    if alpha < 255:
        size = int(r * 7)
        tmp = pygame.Surface((size, size), pygame.SRCALPHA)
        draw_character(tmp, key, size / 2, size / 2, r, aim, t, moving, flash, color, accent, 255)
        tmp.set_alpha(alpha)
        surf.blit(tmp, (int(x - size / 2), int(y - size / 2)))
        return
    bob = math.sin(t * 16) * 2.2 if moving else math.sin(t * 3) * 1.0
    sq = math.sin(t * 16) * 0.06 if moving else math.sin(t * 3) * 0.03
    y = y + bob - 2
    rw, rh = r * (1 + sq), r * (1 - sq)
    body = (255, 255, 255) if flash else color
    behind = math.sin(aim) < -0.2
    if behind:
        _weapon(surf, key, x, y, r, aim, t, accent)

    # akcesoria za ciałem
    if key == "lowczyni":
        pygame.draw.polygon(surf, INK, [(x - r * 0.5, y - r * 0.6), (x - r * 1.35, y - r * 1.5), (x - r * 0.1, y - r * 0.9)])
        pygame.draw.polygon(surf, (60, 120, 70), [(x - r * 0.5, y - r * 0.55), (x - r * 1.2, y - r * 1.35), (x - r * 0.15, y - r * 0.85)])

    outlined_ellipse(surf, body, x, y, rw, rh)
    if not flash:
        pygame.draw.ellipse(surf, lerp_color(body, (0, 0, 0), 0.2), (x - rw * 0.8, y + rh * 0.25, rw * 1.6, rh * 0.65))
        pygame.draw.ellipse(surf, lerp_color(body, (255, 255, 255), 0.3), (x - rw * 0.6, y - rh * 0.75, rw * 0.6, rh * 0.4))

    # akcesoria na głowie
    if key == "rycerz":
        hc = (165, 175, 190) if not flash else body
        pygame.draw.circle(surf, INK, (int(x), int(y)), int(r + 4), draw_top_left=True, draw_top_right=True)
        pygame.draw.circle(surf, hc, (int(x), int(y)), int(r + 1), draw_top_left=True, draw_top_right=True)
        pygame.draw.rect(surf, INK, (x - r - 3, y - 3, 2 * r + 6, 5))
        pygame.draw.ellipse(surf, INK, (x - 6, y - r - 15, 12, 18))
        pygame.draw.ellipse(surf, (220, 60, 60), (x - 4, y - r - 13, 8, 14))
    elif key == "lowczyni":
        pygame.draw.circle(surf, INK, (int(x), int(y)), int(r + 4), draw_top_left=True, draw_top_right=True)
        pygame.draw.circle(surf, (60, 120, 70), (int(x), int(y)), int(r + 1), draw_top_left=True, draw_top_right=True)
        pygame.draw.ellipse(surf, body, (x - r * 0.75, y - r * 0.55, r * 1.5, r * 0.9))
    elif key == "berserker":
        for side in (-1, 1):
            pts = [(x + side * r * 0.55, y - r * 0.7), (x + side * r * 1.25, y - r * 1.55), (x + side * r * 0.95, y - r * 0.45)]
            poly(surf, (235, 225, 195), pts)
    elif key == "mag":
        hc = (90, 60, 170)
        pts = [(x - r * 0.95, y - r * 0.55), (x + r * 0.95, y - r * 0.55), (x + r * 0.45, y - r * 1.5), (x + r * 0.9, y - r * 2.2)]
        poly(surf, hc, pts)
        pygame.draw.ellipse(surf, INK, (x - r * 1.25, y - r * 0.8, r * 2.5, r * 0.6))
        pygame.draw.ellipse(surf, hc, (x - r * 1.15, y - r * 0.75, r * 2.3, r * 0.45))
        pygame.draw.circle(surf, (255, 230, 120), (int(x + r * 0.2), int(y - r * 1.1)), 3)
    elif key == "alchemik":
        pygame.draw.line(surf, (110, 70, 40), (x - r, y - r * 0.55), (x + r, y - r * 0.55), 4)
        for side in (-1, 1):
            outlined_circle(surf, (170, 220, 230), (x + side * r * 0.36, y - r * 0.6), r * 0.24, 2)
            pygame.draw.circle(surf, (230, 250, 255), (int(x + side * r * 0.36 - 2), int(y - r * 0.6 - 2)), 2)
    elif key == "zlodziej":
        pygame.draw.circle(surf, INK, (int(x), int(y)), int(r + 4), draw_top_left=True, draw_top_right=True)
        pygame.draw.circle(surf, (190, 45, 55), (int(x), int(y - 2)), int(r + 1), draw_top_left=True, draw_top_right=True)
        pygame.draw.polygon(surf, (190, 45, 55), [(x - r * 0.9, y - r * 0.3), (x - r * 1.5, y - r * 0.1), (x - r * 1.3, y + r * 0.3)])
        pygame.draw.rect(surf, (30, 28, 36), (x - r * 0.95, y - r * 0.38, r * 1.9, r * 0.5), border_radius=4)
    elif key == "upadly":
        rect = (x - r * 0.9, y - r * 1.75, r * 1.8, r * 0.6)
        pygame.draw.arc(surf, INK, (rect[0] - 2, rect[1] - 2, rect[2] + 4, rect[3] + 4), 0.6, math.pi * 2 - 0.2, 6)
        pygame.draw.arc(surf, (255, 200, 80), rect, 0.6, math.pi * 2 - 0.2, 3)

    # oczy
    if key == "upadly":
        eyes(surf, x, y, r, aim, glow_col=(255, 50, 70))
    elif key == "zlodziej":
        eyes(surf, x, y, r, aim, size=0.18)
    else:
        eyes(surf, x, y, r, aim, angry=(key == "berserker"))

    if not behind:
        _weapon(surf, key, x, y, r, aim, t, accent)


# --------------------------------------------------------------------- Ikony przedmiotów

def star_points(cx, cy, r1, r2, n=5, rot0=-math.pi / 2):
    pts = []
    for i in range(n * 2):
        rr = r1 if i % 2 == 0 else r2
        a = rot0 + i * math.pi / n
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    return pts


def draw_icon(surf, shape, color, x, y, s=14):
    light = lerp_color(color, (255, 255, 255), 0.45)
    dark = lerp_color(color, (0, 0, 0), 0.35)
    if shape == "gem":
        pts = [(x, y - s), (x + s * 0.8, y - s * 0.2), (x, y + s), (x - s * 0.8, y - s * 0.2)]
        poly(surf, color, pts)
        pygame.draw.polygon(surf, light, [(x, y - s * 0.75), (x + s * 0.4, y - s * 0.2), (x, y), (x - s * 0.4, y - s * 0.2)])
    elif shape == "orb":
        outlined_circle(surf, color, (x, y), s * 0.85, 2)
        pygame.draw.circle(surf, light, (int(x - s * 0.3), int(y - s * 0.3)), max(2, int(s * 0.25)))
    elif shape == "star":
        poly(surf, color, star_points(x, y, s, s * 0.45))
    elif shape == "heart":
        pts = []
        for i in range(24):
            a = i / 24 * math.tau
            hx = 16 * math.sin(a) ** 3
            hy = -(13 * math.cos(a) - 5 * math.cos(2 * a) - 2 * math.cos(3 * a) - math.cos(4 * a))
            pts.append((x + hx * s / 17, y + hy * s / 17))
        poly(surf, color, pts)
        pygame.draw.circle(surf, light, (int(x - s * 0.4), int(y - s * 0.3)), max(2, int(s * 0.18)))
    elif shape == "drop":
        pts = [(x, y - s)]
        for i in range(13):
            a = -0.25 * math.pi + i / 12 * 1.5 * math.pi
            pts.append((x + math.cos(a) * s * 0.65, y + s * 0.3 + math.sin(a) * s * 0.65))
        poly(surf, color, pts)
        pygame.draw.circle(surf, light, (int(x - s * 0.22), int(y + s * 0.15)), max(2, int(s * 0.15)))
    elif shape == "bolt":
        pts = [(x + s * 0.2, y - s), (x - s * 0.6, y + s * 0.15), (x - s * 0.05, y + s * 0.15),
               (x - s * 0.25, y + s), (x + s * 0.6, y - s * 0.2), (x + s * 0.05, y - s * 0.2)]
        poly(surf, color, pts)
    elif shape == "shield":
        pts = [(x - s * 0.8, y - s * 0.8), (x + s * 0.8, y - s * 0.8), (x + s * 0.8, y), (x, y + s), (x - s * 0.8, y)]
        poly(surf, color, pts)
        pygame.draw.line(surf, light, (x, y - s * 0.6), (x, y + s * 0.6), 3)
    elif shape == "eye":
        outlined_ellipse(surf, (245, 245, 245), x, y, s, s * 0.6, 2)
        pygame.draw.circle(surf, color, (int(x), int(y)), int(s * 0.45))
        pygame.draw.circle(surf, INK, (int(x), int(y)), int(s * 0.2))
    elif shape == "crown":
        pts = [(x - s, y + s * 0.6), (x - s, y - s * 0.5), (x - s * 0.5, y), (x, y - s * 0.8),
               (x + s * 0.5, y), (x + s, y - s * 0.5), (x + s, y + s * 0.6)]
        poly(surf, color, pts)
        pygame.draw.circle(surf, (220, 50, 60), (int(x), int(y + s * 0.2)), max(2, int(s * 0.18)))
    elif shape == "flame":
        pts = []
        for i in range(17):
            a = i / 16 * math.tau
            rr = s * (0.7 + 0.35 * max(0, -math.sin(a)) ** 3)
            pts.append((x + math.cos(a) * s * 0.65, y + math.sin(a) * rr * (1.4 if math.sin(a) < 0 else 0.7)))
        poly(surf, color, pts)
        pygame.draw.circle(surf, (255, 240, 160), (int(x), int(y + s * 0.2)), max(2, int(s * 0.3)))
    elif shape == "plus":
        w = s * 0.35
        pts = [(x - w, y - s), (x + w, y - s), (x + w, y - w), (x + s, y - w), (x + s, y + w), (x + w, y + w),
               (x + w, y + s), (x - w, y + s), (x - w, y + w), (x - s, y + w), (x - s, y - w), (x - w, y - w)]
        poly(surf, color, pts)
    elif shape == "ring":
        pygame.draw.circle(surf, INK, (int(x), int(y + s * 0.2)), int(s * 0.85), max(2, int(s * 0.45)))
        pygame.draw.circle(surf, color, (int(x), int(y + s * 0.2)), int(s * 0.75), max(2, int(s * 0.25)))
        draw_icon(surf, "gem", (120, 220, 255), x, y - s * 0.55, s * 0.45)
    elif shape == "potion":
        poly(surf, (210, 225, 230), [(x - s * 0.2, y - s), (x + s * 0.2, y - s), (x + s * 0.2, y - s * 0.4), (x - s * 0.2, y - s * 0.4)])
        outlined_circle(surf, color, (x, y + s * 0.2), s * 0.7, 2)
        pygame.draw.circle(surf, light, (int(x - s * 0.25), int(y)), max(2, int(s * 0.18)))
    elif shape == "skull":
        outlined_circle(surf, (235, 230, 215), (x, y - s * 0.15), s * 0.8, 2)
        pygame.draw.rect(surf, INK, (x - s * 0.45, y + s * 0.35, s * 0.9, s * 0.55), border_radius=3)
        pygame.draw.rect(surf, (235, 230, 215), (x - s * 0.38, y + s * 0.35, s * 0.76, s * 0.45), border_radius=2)
        for side in (-1, 1):
            pygame.draw.circle(surf, color if color != (235, 230, 215) else INK, (int(x + side * s * 0.33), int(y - s * 0.15)), max(2, int(s * 0.22)))
    elif shape == "arrow":
        pts = [(x + s, y - s), (x + s * 0.2, y - s * 0.75), (x + s * 0.45, y - s * 0.45), (x - s * 0.8, y + s * 0.8),
               (x - s * 0.6, y + s), (x + s * 0.6, y - s * 0.3), (x + s * 0.8, y - s * 0.1)]
        poly(surf, color, pts)
    elif shape == "boot":
        pts = [(x - s * 0.5, y - s), (x + s * 0.2, y - s), (x + s * 0.2, y + s * 0.2), (x + s, y + s * 0.4),
               (x + s, y + s), (x - s * 0.5, y + s)]
        poly(surf, color, pts)
        pygame.draw.line(surf, light, (x - s * 0.6, y - s * 0.6), (x + s * 0.2, y - s * 0.6), 3)
    elif shape == "clover":
        for dx, dy in ((0, -1), (1, 0), (0, 1), (-1, 0)):
            outlined_circle(surf, color, (x + dx * s * 0.42, y + dy * s * 0.42 - s * 0.1), s * 0.42, 2)
        pygame.draw.line(surf, dark, (x, y), (x + s * 0.4, y + s), 3)
    elif shape == "hourglass":
        poly(surf, (220, 200, 150), [(x - s * 0.7, y - s), (x + s * 0.7, y - s), (x, y), (x + s * 0.7, y + s), (x - s * 0.7, y + s), (x, y)])
        pygame.draw.polygon(surf, color, [(x - s * 0.4, y + s * 0.85), (x + s * 0.4, y + s * 0.85), (x, y + s * 0.3)])
    elif shape == "book":
        poly(surf, color, [(x - s * 0.8, y - s * 0.9), (x + s * 0.8, y - s * 0.9), (x + s * 0.8, y + s * 0.9), (x - s * 0.8, y + s * 0.9)])
        pygame.draw.rect(surf, (240, 230, 200), (x - s * 0.55, y - s * 0.7, s * 1.2, s * 0.25))
        draw_icon(surf, "star", (255, 220, 100), x, y + s * 0.2, s * 0.4)
    elif shape == "coin":
        outlined_circle(surf, color, (x, y), s * 0.85, 2)
        pygame.draw.circle(surf, dark, (int(x), int(y)), int(s * 0.5), 2)
    elif shape == "snow":
        for i in range(3):
            a = i * math.pi / 3
            p1 = (x + math.cos(a) * s, y + math.sin(a) * s)
            p2 = (x - math.cos(a) * s, y - math.sin(a) * s)
            pygame.draw.line(surf, INK, p1, p2, 6)
        for i in range(3):
            a = i * math.pi / 3
            p1 = (x + math.cos(a) * s, y + math.sin(a) * s)
            p2 = (x - math.cos(a) * s, y - math.sin(a) * s)
            pygame.draw.line(surf, color, p1, p2, 3)
    elif shape == "fang":
        for side in (-1, 1):
            poly(surf, (245, 240, 230), [(x + side * s * 0.15, y - s * 0.8), (x + side * s * 0.85, y - s * 0.8), (x + side * s * 0.45, y + s)])
        pygame.draw.circle(surf, color, (int(x), int(y + s * 0.8)), max(2, int(s * 0.2)))
    elif shape == "dice":
        poly(surf, (240, 240, 240), [(x - s * 0.8, y - s * 0.8), (x + s * 0.8, y - s * 0.8), (x + s * 0.8, y + s * 0.8), (x - s * 0.8, y + s * 0.8)])
        for dx, dy in ((-0.4, -0.4), (0.4, 0.4), (0, 0), (0.4, -0.4), (-0.4, 0.4)):
            pygame.draw.circle(surf, color, (int(x + dx * s), int(y + dy * s)), max(2, int(s * 0.13)))
    elif shape == "magnet":
        pygame.draw.arc(surf, INK, (x - s, y - s * 0.6, s * 2, s * 1.8), math.pi, math.tau, max(4, int(s * 0.7)))
        pygame.draw.arc(surf, color, (x - s * 0.9, y - s * 0.5, s * 1.8, s * 1.6), math.pi, math.tau, max(3, int(s * 0.5)))
        pygame.draw.rect(surf, (230, 230, 240), (x - s, y - s * 0.9, s * 0.55, s * 0.4))
        pygame.draw.rect(surf, (230, 230, 240), (x + s * 0.45, y - s * 0.9, s * 0.55, s * 0.4))
    elif shape == "bomb":
        outlined_circle(surf, (50, 50, 60), (x, y + s * 0.15), s * 0.75, 2)
        pygame.draw.line(surf, (200, 160, 100), (x + s * 0.4, y - s * 0.4), (x + s * 0.7, y - s * 0.85), 3)
        pygame.draw.circle(surf, color, (int(x + s * 0.75), int(y - s * 0.9)), max(2, int(s * 0.22)))
    elif shape == "mirror":
        pygame.draw.line(surf, (140, 100, 60), (x, y + s * 0.4), (x, y + s), 5)
        outlined_ellipse(surf, (200, 160, 80), x, y - s * 0.2, s * 0.65, s * 0.75, 2)
        pygame.draw.ellipse(surf, color, (x - s * 0.45, y - s * 0.75, s * 0.9, s * 1.1))
    elif shape == "cup":
        poly(surf, (240, 235, 225), [(x - s * 0.7, y - s * 0.6), (x + s * 0.6, y - s * 0.6), (x + s * 0.45, y + s * 0.8), (x - s * 0.55, y + s * 0.8)])
        pygame.draw.circle(surf, INK, (int(x + s * 0.7), int(y)), int(s * 0.35), 3)
        pygame.draw.ellipse(surf, color, (x - s * 0.6, y - s * 0.65, s * 1.2, s * 0.3))
    elif shape == "scope":
        pygame.draw.circle(surf, INK, (int(x), int(y)), int(s * 0.9), 5)
        pygame.draw.circle(surf, color, (int(x), int(y)), int(s * 0.8), 3)
        pygame.draw.line(surf, color, (x - s, y), (x + s, y), 2)
        pygame.draw.line(surf, color, (x, y - s), (x, y + s), 2)
    elif shape == "blade":
        poly(surf, (225, 230, 240), [(x - s * 0.7, y + s * 0.7), (x + s * 0.6, y - s), (x + s * 0.9, y - s * 0.7), (x - s * 0.4, y + s * 0.9)])
        pygame.draw.line(surf, color, (x - s * 0.9, y + s * 0.6), (x - s * 0.4, y + s * 1.0), 5)
    elif shape == "swirl":
        pts = []
        for i in range(40):
            a = i * 0.45
            rr = s * i / 40
            pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
        outlined_circle(surf, (20, 10, 30), (x, y), s * 0.95, 2)
        pygame.draw.lines(surf, color, False, pts, 3)
    elif shape == "wing":
        pts = [(x - s * 0.9, y + s * 0.4), (x - s * 0.2, y - s), (x + s, y - s * 0.6), (x + s * 0.5, y - s * 0.1),
               (x + s * 0.8, y + s * 0.2), (x + s * 0.2, y + s * 0.5), (x + s * 0.4, y + s * 0.8)]
        poly(surf, color, pts)
    elif shape == "rune":
        poly(surf, color, [(x, y - s), (x + s * 0.85, y), (x, y + s), (x - s * 0.85, y)])
        pygame.draw.line(surf, INK, (x, y - s * 0.5), (x, y + s * 0.5), 3)
        pygame.draw.line(surf, INK, (x - s * 0.35, y - s * 0.1), (x + s * 0.35, y + s * 0.2), 3)
    else:
        outlined_circle(surf, color, (x, y), s * 0.8, 2)
