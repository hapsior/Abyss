"""Gracz, pociski, znajdźki, piedestały, przeszkody i efekty obszarowe."""
import math
import random

import pygame

from . import gfx
from .audio import audio
from .config import (INK, RX0, RX1, RY0, RY1, TILE, WHITE, GOLD, RARITY_COLORS)

TAU = math.tau


def clamp(v, a, b):
    return a if v < a else b if v > b else v


# =====================================================================  POCISK
class Bullet:
    __slots__ = ("x", "y", "vx", "vy", "dmg", "r", "color", "friendly", "range", "dist", "style", "pierce",
                 "bounce", "homing", "spectral", "crit", "hit", "alive", "life", "accel", "turn", "fx",
                 "is_split", "min_speed", "max_speed", "knock", "trail_t", "source")

    def __init__(self, x, y, vx, vy, dmg, r, color, friendly, range_=600.0, style="orb", pierce=0, bounce=0,
                 homing=0.0, spectral=False, crit=False, fx=False, life=9.0, accel=0.0, turn=0.0,
                 knock=1.0, source=None):
        self.x, self.y, self.vx, self.vy = x, y, vx, vy
        self.dmg = dmg
        self.r = r
        self.color = color
        self.friendly = friendly
        self.range = range_
        self.dist = 0.0
        self.style = style
        self.pierce = pierce
        self.bounce = bounce
        self.homing = homing
        self.spectral = spectral
        self.crit = crit
        self.hit = set() if friendly else None
        self.alive = True
        self.life = life
        self.accel = accel
        self.turn = turn
        self.fx = fx
        self.is_split = False
        self.min_speed = 40.0
        self.max_speed = 900.0
        self.knock = knock
        self.trail_t = 0.0
        self.source = source

    def speed(self):
        return math.hypot(self.vx, self.vy)

    def kill(self, g, pop=True):
        if not self.alive:
            return
        self.alive = False
        if pop:
            g.particles.burst(self.x, self.y, self.color, 4, 90, 3, 0.25)

    def update(self, dt, g):
        if self.friendly and self.homing > 0:
            tgt = g.nearest_enemy(self.x, self.y, 380, exclude=self.hit)
            if tgt is not None:
                desired = math.atan2(tgt.y - self.y, tgt.x - self.x)
                cur = math.atan2(self.vy, self.vx)
                diff = (desired - cur + math.pi) % TAU - math.pi
                step = self.homing * dt
                cur += clamp(diff, -step, step)
                sp = self.speed()
                self.vx, self.vy = math.cos(cur) * sp, math.sin(cur) * sp
        if self.turn:
            a = math.atan2(self.vy, self.vx) + self.turn * dt
            sp = self.speed()
            self.vx, self.vy = math.cos(a) * sp, math.sin(a) * sp
        if self.accel:
            sp = self.speed()
            if sp > 0:
                ns = clamp(sp + self.accel * dt, self.min_speed, self.max_speed)
                k = ns / sp
                self.vx *= k
                self.vy *= k
        ox, oy = self.x, self.y
        self.x += self.vx * dt
        self.y += self.vy * dt
        if self.friendly:
            self.dist += self.speed() * dt
            if self.dist > self.range:
                self.kill(g)
                return
            if self.style == "magic":
                self.trail_t -= dt
                if self.trail_t <= 0:
                    self.trail_t = 0.03
                    g.particles.burst(self.x, self.y, self.color, 1, 30, 3, 0.3, glow=False)
        else:
            self.life -= dt
            if self.life <= 0:
                self.kill(g, False)
                return
        # ściany
        r = self.r
        hitwall = False
        if self.x < RX0 + r:
            self.x = RX0 + r
            self.vx = abs(self.vx)
            hitwall = True
        elif self.x > RX1 - r:
            self.x = RX1 - r
            self.vx = -abs(self.vx)
            hitwall = True
        if self.y < RY0 + r:
            self.y = RY0 + r
            self.vy = abs(self.vy)
            hitwall = True
        elif self.y > RY1 - r:
            self.y = RY1 - r
            self.vy = -abs(self.vy)
            hitwall = True
        if hitwall:
            if self.bounce > 0:
                self.bounce -= 1
                self.hit = set() if self.friendly else None
            else:
                g.particles.spark(self.x, self.y, self.color, 3, 150, math.atan2(-self.vy, -self.vx))
                self.kill(g, False)
                return
        # przeszkody
        if not self.spectral:
            ob = g.room.tile_at(self.x, self.y)
            if ob is not None and ob.solid:
                if self.friendly:
                    g.damage_obstacle(ob, self.dmg)
                if self.bounce > 0:
                    self.bounce -= 1
                    oc = g.room.tile_at(ox, self.y)
                    orow = g.room.tile_at(self.x, oy)
                    if oc is None or not oc.solid:
                        self.vx = -self.vx
                    elif orow is None or not orow.solid:
                        self.vy = -self.vy
                    else:
                        self.vx, self.vy = -self.vx, -self.vy
                    self.x, self.y = ox, oy
                else:
                    g.particles.burst(self.x, self.y, self.color, 4, 100, 3, 0.25)
                    self.kill(g, False)

    def draw(self, surf):
        x, y, r = self.x, self.y, self.r
        st = self.style
        if not self.friendly:
            gfx.blit_glow(surf, x, y, r * 2.6, self.color)
            pygame.draw.circle(surf, INK, (int(x), int(y)), int(r + 2))
            pygame.draw.circle(surf, self.color, (int(x), int(y)), int(r))
            pygame.draw.circle(surf, (255, 245, 245), (int(x), int(y)), max(2, int(r * 0.5)))
            return
        if st in ("arrow", "dagger"):
            sp = self.speed() or 1
            dx, dy = self.vx / sp, self.vy / sp
            L = 12 + r
            tx, ty = x - dx * L, y - dy * L
            pygame.draw.line(surf, INK, (tx, ty), (x + dx * 4, y + dy * 4), int(r * 0.9 + 4))
            pygame.draw.line(surf, self.color, (tx, ty), (x + dx * 3, y + dy * 3), max(2, int(r * 0.9)))
            if st == "arrow":
                px, py = -dy, dx
                pts = [(x + dx * 8, y + dy * 8), (x + px * 5, y + py * 5), (x - px * 5, y - py * 5)]
                pygame.draw.polygon(surf, (240, 240, 250), pts)
            if self.crit:
                gfx.blit_glow(surf, x, y, r * 3, (255, 220, 80))
        elif st == "disc":
            gfx.blit_glow(surf, x, y, r * 2.2, gfx.mul_color(self.color, 0.7))
            pygame.draw.circle(surf, INK, (int(x), int(y)), int(r + 3))
            pygame.draw.circle(surf, self.color, (int(x), int(y)), int(r))
            pygame.draw.circle(surf, gfx.lerp_color(self.color, WHITE, 0.5), (int(x - r * 0.3), int(y - r * 0.3)), max(2, int(r * 0.35)))
        elif st == "dark":
            gfx.blit_glow(surf, x, y, r * 3, self.color)
            pygame.draw.circle(surf, self.color, (int(x), int(y)), int(r + 2))
            pygame.draw.circle(surf, (20, 5, 15), (int(x), int(y)), max(2, int(r * 0.7)))
        else:  # orb / magic / drop
            gfx.blit_glow(surf, x, y, r * (3.2 if st == "magic" else 2.4), self.color)
            pygame.draw.circle(surf, INK, (int(x), int(y)), int(r + 2))
            pygame.draw.circle(surf, self.color, (int(x), int(y)), int(r))
            pygame.draw.circle(surf, (255, 255, 255), (int(x - r * 0.25), int(y - r * 0.25)), max(1, int(r * 0.4)))
        if self.crit and st not in ("arrow", "dagger"):
            pygame.draw.circle(surf, (255, 230, 90), (int(x), int(y)), int(r + 4), 2)


# =====================================================================  ZNAJDŹKI
class Pickup:
    __slots__ = ("kind", "x", "y", "vx", "vy", "value", "t", "alive", "magnet", "z", "vz")

    def __init__(self, kind, x, y, value=1, pop=True):
        self.kind = kind
        self.x, self.y = x, y
        a = random.uniform(0, TAU)
        s = random.uniform(60, 200) if pop else 0
        self.vx, self.vy = math.cos(a) * s, math.sin(a) * s
        self.value = value
        self.t = random.uniform(0, 10)
        self.alive = True
        self.magnet = False
        self.z = 0.0
        self.vz = random.uniform(120, 220) if pop else 0.0

    def update(self, dt, g):
        self.t += dt
        p = g.player
        dx, dy = p.x - self.x, p.y - self.y
        d = math.hypot(dx, dy) or 1
        attract = self.kind in ("xp", "coin") and (g.room.cleared or d < p.pickup)
        if self.kind == "heart" and d < p.pickup * 0.6 and p.hp < p.max_hp:
            attract = True
        if attract and self.z <= 0:
            self.magnet = True
        if self.magnet:
            # prędkość dążąca wprost do gracza (bez krążenia po orbicie)
            sp = 420 + max(0, 400 - d) * 1.5
            k = min(1.0, dt * 12)
            self.vx += (dx / d * sp - self.vx) * k
            self.vy += (dy / d * sp - self.vy) * k
        else:
            k = max(0.0, 1 - 5 * dt)
            self.vx *= k
            self.vy *= k
        self.x += self.vx * dt
        self.y += self.vy * dt
        if self.vz or self.z > 0:
            self.vz -= 700 * dt
            self.z += self.vz * dt
            if self.z <= 0:
                self.z = 0
                self.vz = -self.vz * 0.35 if abs(self.vz) > 80 else 0
        self.x = clamp(self.x, RX0 + 8, RX1 - 8)
        self.y = clamp(self.y, RY0 + 8, RY1 - 8)
        if d < p.r + 12 and self.z < 8:
            if g.collect_pickup(self):
                self.alive = False

    def draw(self, surf):
        x, y = self.x, self.y - self.z
        bob = math.sin(self.t * 5) * 2
        if self.kind == "coin":
            gfx.shadow(surf, self.x, self.y + 7, 14, 6)
            w = abs(math.cos(self.t * 4)) * 7 + 2
            pygame.draw.ellipse(surf, INK, (x - w - 2, y - 9 + bob, w * 2 + 4, 18))
            pygame.draw.ellipse(surf, GOLD, (x - w, y - 7 + bob, w * 2, 14))
            if w > 4:
                pygame.draw.ellipse(surf, (255, 235, 140), (x - w * 0.5, y - 5 + bob, w * 0.6, 6))
        elif self.kind == "xp":
            c = (110, 230, 120) if self.value < 3 else (90, 200, 255) if self.value < 8 else (220, 120, 255)
            s = 5 if self.value < 3 else 7 if self.value < 8 else 9
            gfx.blit_glow(surf, x, y + bob, s * 3, gfx.mul_color(c, 0.6))
            pts = [(x, y - s + bob), (x + s * 0.7, y + bob), (x, y + s + bob), (x - s * 0.7, y + bob)]
            gfx.poly(surf, c, pts, 2)
        elif self.kind == "heart":
            gfx.shadow(surf, self.x, self.y + 10, 20, 7)
            gfx.draw_icon(surf, "heart", (235, 55, 70), x, y + bob - 2, 11)
        elif self.kind == "chest":
            gfx.shadow(surf, self.x, self.y + 14, 40, 10)
            r = pygame.Rect(0, 0, 34, 24)
            r.center = (int(x), int(y))
            pygame.draw.rect(surf, INK, r.inflate(6, 6), border_radius=5)
            pygame.draw.rect(surf, (150, 95, 50), r, border_radius=4)
            pygame.draw.rect(surf, (120, 72, 38), (r.x, r.y, r.w, 9), border_radius=4)
            pygame.draw.rect(surf, GOLD, (r.centerx - 4, r.y + 5, 8, 9), border_radius=2)


# =====================================================================  PIEDESTAŁ
class Pedestal:
    def __init__(self, x, y, item=None, price=0, kind="item", group=None, heal=0, hp_cost=0):
        self.x, self.y = x, y
        self.item = item
        self.price = price
        self.kind = kind  # item / shop / shrine / heart_shop
        self.group = group
        self.heal = heal
        self.hp_cost = hp_cost
        self.t = random.uniform(0, 10)
        self.alive = True
        self.cool = 0.0

    def draw(self, surf, t):
        x, y = self.x, self.y
        gfx.shadow(surf, x, y + 20, 56, 14)
        base = pygame.Rect(0, 0, 44, 26)
        base.center = (x, y + 10)
        col = (110, 100, 120) if self.kind != "shrine" else (110, 40, 50)
        pygame.draw.rect(surf, INK, base.inflate(6, 6), border_radius=6)
        pygame.draw.rect(surf, col, base, border_radius=5)
        pygame.draw.rect(surf, gfx.lerp_color(col, WHITE, 0.25), (base.x, base.y, base.w, 8), border_radius=5)
        bob = math.sin(t * 2.5 + self.t) * 4
        if self.kind == "heart_shop":
            gfx.blit_glow(surf, x, y - 18 + bob, 30, (120, 30, 40))
            gfx.draw_icon(surf, "heart", (235, 55, 70), x, y - 18 + bob, 13)
        elif self.item:
            rc = RARITY_COLORS[self.item["rarity"]]
            gfx.blit_glow(surf, x, y - 18 + bob, 34, gfx.mul_color(rc, 0.55))
            gfx.draw_icon(surf, self.item["icon"], self.item["color"], x, y - 18 + bob, 15)
        if self.price > 0:
            gfx.draw_text(surf, f"{self.price}", x + 6, y + 36, 16, GOLD, "center")
            gfx.draw_icon(surf, "coin", GOLD, x - 14, y + 36, 7)
        if self.hp_cost > 0:
            gfx.draw_text(surf, f"-{self.hp_cost} maks. HP", x, y + 36, 14, (255, 90, 100), "center")


# =====================================================================  PRZESZKODY
class Obstacle:
    __slots__ = ("kind", "col", "row", "hp", "solid", "seed", "x", "y", "flash")

    def __init__(self, kind, col, row):
        self.kind = kind
        self.col, self.row = col, row
        self.hp = {"rock": 99999, "crate": 28, "barrel": 12, "pillar": 99999}[kind]
        self.solid = True
        self.seed = random.random()
        self.x = RX0 + col * TILE + TILE / 2
        self.y = RY0 + row * TILE + TILE / 2
        self.flash = 0.0

    def draw(self, surf, accent, wall_col):
        x0 = RX0 + self.col * TILE
        y0 = RY0 + self.row * TILE
        cx, cy = x0 + TILE / 2, y0 + TILE / 2
        fl = self.flash > 0
        if self.kind in ("rock", "pillar"):
            gfx.shadow(surf, cx, y0 + TILE - 4, TILE * 0.95, 14, 80)
            base = gfx.lerp_color(wall_col, (120, 115, 125), 0.45)
            s = self.seed
            pts = []
            for i in range(8):
                a = i / 8 * TAU + s
                rr = TILE * (0.42 + 0.07 * math.sin(s * 37 + i * 2.3))
                pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr * 0.9 + 2))
            gfx.poly(surf, base, pts, 3)
            top = [(px, py - 5) for px, py in pts]
            hi = gfx.lerp_color(base, WHITE, 0.18)
            pygame.draw.polygon(surf, hi, [((px - cx) * 0.75 + cx, (py - cy) * 0.6 + cy - 5) for px, py in top])
            pygame.draw.line(surf, gfx.mul_color(base, 0.7), (cx - 6, cy + 2), (cx + 4, cy + 9), 2)
        elif self.kind == "crate":
            r = pygame.Rect(x0 + 5, y0 + 4, TILE - 10, TILE - 10)
            gfx.shadow(surf, cx, y0 + TILE - 4, TILE * 0.9, 12, 80)
            col = (240, 240, 240) if fl else (165, 112, 62)
            pygame.draw.rect(surf, INK, r.inflate(6, 6), border_radius=4)
            pygame.draw.rect(surf, col, r, border_radius=3)
            d = gfx.mul_color(col, 0.75)
            pygame.draw.rect(surf, d, r, 3, border_radius=3)
            pygame.draw.line(surf, d, r.topleft, r.bottomright, 4)
            pygame.draw.line(surf, d, r.topright, r.bottomleft, 4)
        elif self.kind == "barrel":
            gfx.shadow(surf, cx, y0 + TILE - 4, TILE * 0.8, 12, 80)
            col = (255, 255, 255) if fl else (200, 55, 45)
            r = pygame.Rect(0, 0, TILE - 14, TILE - 6)
            r.center = (cx, cy)
            pygame.draw.rect(surf, INK, r.inflate(6, 6), border_radius=10)
            pygame.draw.rect(surf, col, r, border_radius=8)
            pygame.draw.rect(surf, (90, 80, 80), (r.x, r.y + 8, r.w, 4))
            pygame.draw.rect(surf, (90, 80, 80), (r.x, r.bottom - 12, r.w, 4))
            gfx.draw_icon(surf, "flame", (255, 220, 80), cx, cy, 6)


# =====================================================================  EFEKTY
class Effect:
    layer = "floor"
    alive = True

    def update(self, dt, g):
        pass

    def draw(self, surf, g):
        pass


class PoisonCloud(Effect):
    def __init__(self, x, y, r, dps, duration, color=(120, 230, 80), slow=True):
        self.x, self.y, self.r = x, y, r
        self.dps = dps
        self.t = self.dur = duration
        self.tick = 0.0
        self.color = color
        self.slow = slow
        self.alive = True
        self.seed = random.random() * 10

    def update(self, dt, g):
        self.t -= dt
        if self.t <= 0:
            self.alive = False
            return
        self.tick -= dt
        if self.tick <= 0:
            self.tick = 0.35
            for e in g.enemies:
                if (e.x - self.x) ** 2 + (e.y - self.y) ** 2 < (self.r + e.r) ** 2:
                    g.damage_enemy(e, self.dps * 0.35, color=self.color, small=True)
                    if self.slow:
                        e.slow_t = max(e.slow_t, 0.6)
        if random.random() < dt * 14:
            a = random.uniform(0, TAU)
            d = random.uniform(0, self.r)
            g.particles.burst(self.x + math.cos(a) * d, self.y + math.sin(a) * d, self.color, 1, 20, 4, 0.6, grav=-40)

    def draw(self, surf, g):
        f = min(1.0, self.t / 0.5, (self.dur - self.t) / 0.25 + 0.2)
        rr = int(self.r * (0.9 + 0.1 * math.sin(g.time * 3 + self.seed)))
        s = pygame.Surface((rr * 2 + 4, rr * 2 + 4), pygame.SRCALPHA)
        pygame.draw.circle(s, (*self.color, int(70 * f)), (rr + 2, rr + 2), rr)
        pygame.draw.circle(s, (*self.color, int(140 * f)), (rr + 2, rr + 2), rr, 3)
        surf.blit(s, (self.x - rr - 2, self.y - rr - 2))


class FirePool(Effect):
    """Wroga strefa ognia – rani gracza."""

    def __init__(self, x, y, r, dmg, duration):
        self.x, self.y, self.r = x, y, r
        self.dmg = dmg
        self.t = self.dur = duration
        self.alive = True
        self.warm = 0.5

    def update(self, dt, g):
        self.t -= dt
        self.warm -= dt
        if self.t <= 0:
            self.alive = False
            return
        p = g.player
        if self.warm <= 0 and (p.x - self.x) ** 2 + (p.y - self.y) ** 2 < (self.r + p.r * 0.5) ** 2:
            p.hurt(self.dmg, g, (self.x, self.y))
        if random.random() < dt * 10:
            a = random.uniform(0, TAU)
            d = random.uniform(0, self.r)
            g.particles.burst(self.x + math.cos(a) * d, self.y + math.sin(a) * d, (255, 150, 40), 1, 30, 4, 0.5, grav=-80, glow=True)

    def draw(self, surf, g):
        f = min(1.0, self.t / 0.4)
        rr = int(self.r)
        s = pygame.Surface((rr * 2 + 4, rr * 2 + 4), pygame.SRCALPHA)
        a = 50 if self.warm > 0 else 110
        pygame.draw.circle(s, (255, 110, 30, int(a * f)), (rr + 2, rr + 2), rr)
        pygame.draw.circle(s, (255, 200, 60, int(170 * f)), (rr + 2, rr + 2), rr, 3)
        surf.blit(s, (self.x - rr - 2, self.y - rr - 2))


class BlackHole(Effect):
    layer = "floor"

    def __init__(self, x, y, dmg, duration=2.6, radius=230):
        self.x, self.y = x, y
        self.dmg = dmg
        self.t = self.dur = duration
        self.radius = radius
        self.tick = 0
        self.alive = True

    def update(self, dt, g):
        self.t -= dt
        if self.t <= 0:
            self.alive = False
            g.particles.ring(self.x, self.y, (190, 100, 255), 90, 0.3)
            return
        self.tick -= dt
        for e in g.enemies:
            if e.boss:
                continue
            dx, dy = self.x - e.x, self.y - e.y
            d = math.hypot(dx, dy) or 1
            if d < self.radius:
                pull = 260 * (1 - d / self.radius) + 60
                e.kvx += dx / d * pull * dt * 4
                e.kvy += dy / d * pull * dt * 4
        if self.tick <= 0:
            self.tick = 0.25
            for e in g.enemies:
                if (e.x - self.x) ** 2 + (e.y - self.y) ** 2 < 70 ** 2:
                    g.damage_enemy(e, self.dmg, color=(200, 130, 255), small=True)
        if random.random() < dt * 30:
            a = random.uniform(0, TAU)
            px, py = self.x + math.cos(a) * 140, self.y + math.sin(a) * 140
            g.particles.add(_part(px, py, (self.x - px) * 2.2, (self.y - py) * 2.2, 0.45, 3, (180, 110, 255)))

    def draw(self, surf, g):
        f = min(1.0, self.t / 0.3, (self.dur - self.t) / 0.2)
        r = 34 * f + math.sin(g.time * 12) * 2
        gfx.blit_glow(surf, self.x, self.y, r * 3, (120, 50, 200))
        pygame.draw.circle(surf, (10, 0, 20), (int(self.x), int(self.y)), int(max(2, r)))
        pygame.draw.circle(surf, (200, 130, 255), (int(self.x), int(self.y)), int(max(3, r)), 3)


def _part(x, y, vx, vy, life, size, color):
    from .particles import Particle
    return Particle(x, y, vx, vy, life, size, color, drag=0.5)


class Flask(Effect):
    """Rzucona kolba alchemika – leci łukiem i tworzy trującą kałużę."""
    layer = "top"

    def __init__(self, x0, y0, x1, y1, dps, radius, duration):
        self.x0, self.y0, self.x1, self.y1 = x0, y0, x1, y1
        self.t = 0.0
        self.T = 0.45
        self.dps, self.radius, self.duration = dps, radius, duration
        self.alive = True

    def update(self, dt, g):
        self.t += dt
        if self.t >= self.T:
            self.alive = False
            audio.play("poison")
            audio.play("break")
            g.particles.burst(self.x1, self.y1, (150, 255, 100), 18, 220, 5, 0.5)
            g.particles.ring(self.x1, self.y1, (150, 255, 100), self.radius, 0.3)
            g.effects.append(PoisonCloud(self.x1, self.y1, self.radius, self.dps, self.duration))

    def draw(self, surf, g):
        f = self.t / self.T
        x = self.x0 + (self.x1 - self.x0) * f
        y = self.y0 + (self.y1 - self.y0) * f - math.sin(f * math.pi) * 90
        gfx.shadow(surf, self.x0 + (self.x1 - self.x0) * f, self.y0 + (self.y1 - self.y0) * f, 16, 6)
        gfx.draw_icon(surf, "potion", (130, 240, 90), x, y, 10)


class Telegraph(Effect):
    """Ostrzeżenie (czerwony okrąg) przed atakiem obszarowym."""
    layer = "floor"

    def __init__(self, x, y, r, duration, callback, color=(255, 60, 60)):
        self.x, self.y, self.r = x, y, r
        self.t = self.dur = duration
        self.cb = callback
        self.color = color
        self.alive = True

    def update(self, dt, g):
        self.t -= dt
        if self.t <= 0:
            self.alive = False
            self.cb(g)

    def draw(self, surf, g):
        f = 1 - self.t / self.dur
        rr = int(self.r)
        s = pygame.Surface((rr * 2 + 4, rr * 2 + 4), pygame.SRCALPHA)
        pygame.draw.circle(s, (*self.color, int(40 + 60 * f)), (rr + 2, rr + 2), int(rr * f))
        pygame.draw.circle(s, (*self.color, 200), (rr + 2, rr + 2), rr, 3)
        surf.blit(s, (self.x - rr - 2, self.y - rr - 2))


# =====================================================================  TOWARZYSZ
class Familiar:
    def __init__(self, idx):
        self.idx = idx
        self.x = self.y = 0.0
        self.cd = random.uniform(0.2, 0.8)
        self.t = random.random() * 10

    def update(self, dt, g, tx, ty):
        self.t += dt
        k = min(1.0, dt * 6)
        self.x += (tx - self.x) * k
        self.y += (ty - self.y) * k
        self.cd -= dt
        if self.cd <= 0:
            e = g.nearest_enemy(self.x, self.y, 520)
            if e is not None:
                self.cd = 0.75
                a = math.atan2(e.y - self.y, e.x - self.x)
                p = g.player
                dmg = max(4.0, p.damage * p.dmg_mult * 0.5)
                g.bullets.append(Bullet(self.x, self.y, math.cos(a) * 480, math.sin(a) * 480, dmg, 5,
                                        (170, 220, 255), True, 520, "orb", source="fam"))
            else:
                self.cd = 0.2

    def draw(self, surf, g):
        y = self.y + math.sin(self.t * 4) * 4
        gfx.blit_glow(surf, self.x, y, 22, (60, 90, 140))
        pts = [(self.x - 10, y + 10)]
        for i in range(11):
            a = math.pi + i / 10 * math.pi
            pts.append((self.x + math.cos(a) * 10, y + math.sin(a) * 11))
        pts += [(self.x + 10, y + 10), (self.x + 5, y + 6), (self.x, y + 11), (self.x - 5, y + 6)]
        gfx.poly(surf, (200, 225, 255), pts, 2)
        pygame.draw.circle(surf, INK, (int(self.x - 4), int(y - 2)), 2)
        pygame.draw.circle(surf, INK, (int(self.x + 4), int(y - 2)), 2)
