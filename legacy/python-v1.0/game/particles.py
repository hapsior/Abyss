"""Cząsteczki, pierścienie i latające liczby obrażeń."""
import math
import random

import pygame

from . import gfx


class Particle:
    __slots__ = ("x", "y", "vx", "vy", "life", "max_life", "size", "color", "drag", "grav", "kind", "glow")

    def __init__(self, x, y, vx, vy, life, size, color, drag=3.0, grav=0.0, kind="circle", glow=False):
        self.x, self.y, self.vx, self.vy = x, y, vx, vy
        self.life = self.max_life = life
        self.size = size
        self.color = color
        self.drag = drag
        self.grav = grav
        self.kind = kind
        self.glow = glow


class Ring:
    __slots__ = ("x", "y", "r0", "r1", "life", "max_life", "color", "width")

    def __init__(self, x, y, r0, r1, life, color, width=4):
        self.x, self.y, self.r0, self.r1 = x, y, r0, r1
        self.life = self.max_life = life
        self.color = color
        self.width = width


class FloatText:
    __slots__ = ("x", "y", "text", "color", "size", "life", "max_life", "vy", "vx")

    def __init__(self, x, y, text, color, size, life=0.8):
        self.x, self.y = x, y
        self.text = text
        self.color = color
        self.size = size
        self.life = self.max_life = life
        self.vy = -90
        self.vx = random.uniform(-25, 25)


class Particles:
    MAX = 900

    def __init__(self):
        self.parts = []
        self.rings = []
        self.texts = []
        self.lines = []  # (pts, color, life, max_life, width)

    def clear(self):
        self.parts.clear()
        self.rings.clear()
        self.texts.clear()
        self.lines.clear()

    def burst(self, x, y, color, n=10, speed=200, size=4, life=0.5, drag=4.0, grav=0.0, glow=False, spread=math.tau, angle=0.0, kind="circle"):
        if len(self.parts) > self.MAX:
            n = max(1, n // 4)
        for _ in range(n):
            a = angle + random.uniform(-spread / 2, spread / 2)
            s = random.uniform(0.3, 1.0) * speed
            self.parts.append(Particle(x, y, math.cos(a) * s, math.sin(a) * s,
                                       random.uniform(0.6, 1.0) * life, random.uniform(0.6, 1.0) * size,
                                       color, drag, grav, kind, glow))

    def spark(self, x, y, color, n=6, speed=300, angle=0.0, spread=1.2, life=0.25):
        self.burst(x, y, color, n, speed, 3, life, drag=6, spread=spread, angle=angle, kind="spark")

    def add(self, p):
        if len(self.parts) < self.MAX * 1.2:
            self.parts.append(p)

    def ring(self, x, y, color, r1=60, life=0.35, r0=4, width=4):
        self.rings.append(Ring(x, y, r0, r1, life, color, width))

    def text(self, x, y, s, color=(255, 255, 255), size=18, life=0.8):
        if len(self.texts) > 80:
            self.texts.pop(0)
        self.texts.append(FloatText(x, y, s, color, size, life))

    def bolt(self, x1, y1, x2, y2, color=(160, 220, 255), life=0.18, width=3, jag=14):
        d = math.hypot(x2 - x1, y2 - y1)
        n = max(2, int(d / 22))
        pts = [(x1, y1)]
        nx, ny = (-(y2 - y1) / d, (x2 - x1) / d) if d > 0 else (0, 0)
        for i in range(1, n):
            t = i / n
            o = random.uniform(-jag, jag)
            pts.append((x1 + (x2 - x1) * t + nx * o, y1 + (y2 - y1) * t + ny * o))
        pts.append((x2, y2))
        self.lines.append([pts, color, life, life, width])

    def update(self, dt):
        alive = []
        for p in self.parts:
            p.life -= dt
            if p.life <= 0:
                continue
            k = max(0.0, 1 - p.drag * dt)
            p.vx *= k
            p.vy = p.vy * k + p.grav * dt
            p.x += p.vx * dt
            p.y += p.vy * dt
            alive.append(p)
        self.parts = alive
        self.rings = [r for r in self.rings if self._dec(r, dt)]
        tx = []
        for t in self.texts:
            t.life -= dt
            if t.life > 0:
                t.y += t.vy * dt
                t.x += t.vx * dt
                t.vy *= max(0.0, 1 - 4 * dt)
                tx.append(t)
        self.texts = tx
        ln = []
        for l in self.lines:
            l[2] -= dt
            if l[2] > 0:
                ln.append(l)
        self.lines = ln

    @staticmethod
    def _dec(o, dt):
        o.life -= dt
        return o.life > 0

    def draw(self, surf):
        for p in self.parts:
            f = p.life / p.max_life
            if p.kind == "spark":
                sp = math.hypot(p.vx, p.vy)
                if sp > 1:
                    l = min(18, sp * 0.04) * f + 2
                    ex, ey = p.x - p.vx / sp * l, p.y - p.vy / sp * l
                    pygame.draw.line(surf, p.color, (p.x, p.y), (ex, ey), max(1, int(p.size * f + 1)))
            elif p.kind == "square":
                s = max(1, int(p.size * f))
                pygame.draw.rect(surf, p.color, (p.x - s, p.y - s, s * 2, s * 2))
            elif p.kind == "smoke":
                s = int(p.size * (1.5 - f * 0.5))
                c = gfx.mul_color(p.color, 0.4 + 0.6 * f)
                pygame.draw.circle(surf, c, (int(p.x), int(p.y)), max(1, s))
            else:
                s = p.size * (0.4 + 0.6 * f)
                if p.glow:
                    gfx.blit_glow(surf, p.x, p.y, s * 3.5, gfx.mul_color(p.color, f))
                pygame.draw.circle(surf, p.color, (int(p.x), int(p.y)), max(1, int(s)))
        for r in self.rings:
            f = 1 - r.life / r.max_life
            rad = r.r0 + (r.r1 - r.r0) * (1 - (1 - f) ** 2)
            w = max(1, int(r.width * (1 - f) + 1))
            if rad > w:
                pygame.draw.circle(surf, r.color, (int(r.x), int(r.y)), int(rad), w)
        for pts, color, life, ml, width in self.lines:
            f = life / ml
            if len(pts) >= 2:
                pygame.draw.lines(surf, gfx.mul_color(color, 0.5), False, pts, max(1, int(width * 2.5 * f)))
                pygame.draw.lines(surf, (255, 255, 255), False, pts, max(1, int(width * f)))

    def draw_texts(self, surf):
        for t in self.texts:
            f = t.life / t.max_life
            a = 255 if f > 0.3 else int(255 * f / 0.3)
            scale_up = 1.0 + max(0.0, (f - 0.85)) * 3
            gfx.draw_text(surf, t.text, t.x, t.y, int(t.size * scale_up), t.color, "center", outline=2, alpha=a)
