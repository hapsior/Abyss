"""Bossowie – ataki zapisane jako generatory (skrypty), kilka faz."""
import math
import random

import pygame

from . import gfx
from .audio import audio
from .config import INK, RX0, RX1, RY0, RY1, RCX, RCY, ENEMY_BULLET, ENEMY_BULLET2, ENEMY_BULLET3, WHITE
from .enemies import Enemy, blob
from .entities import FirePool, Telegraph

TAU = math.tau


class Boss(Enemy):
    boss = True
    knock_resist = 0.04
    boss_hp = 800
    title = "Boss"
    attacks = []
    phase_thresholds = (0.5,)
    rest = (0.7, 1.1)

    def __init__(self, g, x, y):
        super().__init__(g, x, y)
        self.max_hp = self.hp = self.boss_hp * g.diff["enemy_hp"]
        self.r = self.radius
        self.intro = 1.8
        self.script = None
        self.wait = 0.0
        self.phase = 1
        self.last_attack = None
        self.dt = 0.0
        self.z = 0.0
        self.mv = None
        self.contact_on = True

    def frac(self):
        return self.hp / self.max_hp

    def next_attack(self, g):
        opts = [a for a in self.available_attacks() if a != self.last_attack] or self.available_attacks()
        a = random.choice(opts)
        self.last_attack = a
        return getattr(self, "atk_" + a)(g)

    def available_attacks(self):
        return self.attacks

    def ai(self, dt, g):
        self.dt = dt
        if self.intro > 0:
            self.intro -= dt
            return
        # zmiana fazy
        new_phase = 1 + sum(1 for th in self.phase_thresholds if self.frac() <= th)
        if new_phase > self.phase:
            self.phase = new_phase
            self.script = self.phase_change(g)
            self.wait = 0
        self.wait -= dt
        guard = 0
        while self.wait <= 0 and guard < 50:
            guard += 1
            if self.script is None:
                self.script = self.next_attack(g)
            try:
                w = next(self.script)
            except StopIteration:
                self.script = None
                self.wait += random.uniform(*self.rest) / (1 + 0.15 * (self.phase - 1))
                continue
            if not w:
                self.wait = 0
                break
            self.wait += w
        if self.mv:
            kind = self.mv[0]
            if kind == "chase":
                self.chase(g, self.mv[1], dt)
            elif kind == "to":
                tx, ty, sp = self.mv[1], self.mv[2], self.mv[3]
                dx, dy = tx - self.x, ty - self.y
                d = math.hypot(dx, dy)
                if d > 6:
                    self.vx, self.vy = dx / d * sp, dy / d * sp
                else:
                    self.mv = None
            elif kind == "dir":
                self.vx, self.vy = self.mv[1], self.mv[2]

    def reset_state(self):
        """Przywraca stan po przerwaniu ataku (np. przy zmianie fazy)."""
        self.z = 0.0
        self.invuln = False
        self.contact_on = True
        self.mv = None
        for attr, val in (("fade", 1.0), ("charge_dir", None), ("tele", 0.0), ("lift", 0.0),
                          ("tele_dir", None), ("cast", 0.0)):
            if hasattr(self, attr):
                setattr(self, attr, val)

    def phase_change(self, g):
        audio.play("boss_roar", 0.8)
        g.shake(10)
        g.particles.ring(self.x, self.y, (255, 80, 80), 200, 0.5, width=8)
        self.reset_state()
        g.clear_enemy_bullets()
        t = 0.0
        while t < 1.0:
            t += self.dt
            self.flash = 0.05 if int(t * 12) % 2 else 0
            yield 0
        yield 0.3

    def wait_time(self, T):
        t = 0.0
        while t < T:
            t += self.dt
            yield 0

    def on_death(self, g):
        pass


# ======================================================================  1. KRÓL GLUTÓW
class SlimeKing(Boss):
    key = "slime_king"
    name = "Król Glutów"
    title = "Władca Piwnic"
    radius = 46
    base_speed = 0
    contact = 16
    boss_hp = 750
    color = (100, 200, 90)
    attacks = ["hops", "jump", "spit"]

    def setup(self, g):
        self.squash = 0.0

    def land(self, g, n, speed=170, offset=0.0):
        self.fire_ring(g, n, speed, offset=offset, r=9)
        g.shake(6)
        audio.play("slam", 0.6)
        g.particles.burst(self.x, self.y + self.r * 0.7, (90, 170, 80), 16, 260, 6, 0.5)
        self.squash = 1.0

    def atk_hops(self, g):
        hops = 3 if self.phase == 1 else 4
        for k in range(hops):
            tx, ty = g.target_pos()
            sx, sy = self.x, self.y
            T = 0.55 if self.phase == 1 else 0.45
            t = 0.0
            while t < T:
                t += self.dt
                f = min(1.0, t / T)
                self.x = sx + (tx - sx) * f * 0.7
                self.y = sy + (ty - sy) * f * 0.7
                self.z = math.sin(f * math.pi) * 70
                self.invuln = self.z > 40
                yield 0
            self.z = 0
            self.invuln = False
            self.land(g, 10 if self.phase == 1 else 14, offset=random.uniform(0, 1))
            yield 0.3

    def atk_jump(self, g):
        audio.play("charge", 0.6)
        t = 0.0
        while t < 0.5:
            t += self.dt
            self.z = (t / 0.5) ** 2 * 700
            self.invuln = self.z > 40
            self.contact_on = False
            yield 0
        t = 0.0
        while t < 1.1:
            t += self.dt
            tx, ty = g.target_pos()
            self.x += (tx - self.x) * min(1.0, self.dt * 3)
            self.y += (ty - self.y) * min(1.0, self.dt * 3)
            yield 0
        t = 0.0
        while t < 0.35:
            t += self.dt
            self.z = 700 * (1 - t / 0.35) ** 2
            yield 0
        self.z = 0
        self.invuln = False
        self.contact_on = True
        self.land(g, 20, 190)
        g.shake(12)
        if self.phase >= 2:
            yield 0.2
            self.fire_ring(g, 20, 130, offset=TAU / 40, r=9, color=ENEMY_BULLET2)
        slimes = sum(1 for e in g.enemies if e.key == "slime")
        if slimes < 4:
            for i in range(2):
                a = random.uniform(0, TAU)
                e = g.spawn_enemy("slime", self.x + math.cos(a) * 60, self.y + math.sin(a) * 60, minion=True)
                if e:
                    e.knock(math.cos(a) * 400, math.sin(a) * 400)
        yield 0.5

    def atk_spit(self, g):
        for k in range(3 if self.phase == 1 else 5):
            self.squash = 0.6
            a = self.angle_to(g)
            self.fire_spread(g, a, 5, 0.2, 210, r=11, color=ENEMY_BULLET2)
            audio.play("enemy_shoot")
            yield 0.45

    def ai(self, dt, g):
        self.squash = max(0.0, self.squash - dt * 3)
        super().ai(dt, g)

    def draw(self, surf, g):
        gfx.shadow(surf, self.x, self.y + self.r * 0.75, self.r * 2.2 * (1 - min(0.6, self.z / 900)), self.r * 0.8, 90)
        y = self.y - self.z
        s = math.sin(self.squash * math.pi) * 0.25
        rw, rh = self.r * (1 + s), self.r * (1 - s)
        if self.z > 0:
            rw, rh = self.r * 0.88, self.r * 1.12
        if y + rh < RY0 - 40:
            return
        c = WHITE if self.flash > 0 else self.color
        if self.phase >= 2 and self.flash <= 0:
            c = (150, 210, 80)
        blob(surf, self.x, y, rw, rh, c, self.flash > 0)
        # korona
        cx, cy = self.x, y - rh + 4
        pts = [(cx - 26, cy + 6), (cx - 28, cy - 18), (cx - 13, cy - 6), (cx, cy - 24), (cx + 13, cy - 6), (cx + 28, cy - 18), (cx + 26, cy + 6)]
        gfx.poly(surf, (255, 200, 60), pts, 3)
        for px in (-14, 0, 14):
            pygame.draw.circle(surf, (230, 50, 70), (int(cx + px), int(cy)), 3)
        gfx.eyes(surf, self.x, y + 6, self.r * 0.8, self.face, angry=self.phase >= 2)
        pygame.draw.arc(surf, INK, (self.x - 16, y + 12, 32, 20), math.pi * 1.1, math.pi * 1.9, 4)


# ======================================================================  2. LICZ
class Lich(Boss):
    key = "lich"
    name = "Licz"
    title = "Pan Kości"
    radius = 34
    base_speed = 0
    contact = 14
    boss_hp = 1150
    flying = True
    color = (110, 80, 160)
    attacks = ["spiral", "teleport", "summon"]

    def setup(self, g):
        self.fade = 1.0
        self.cast = 0.0

    def available_attacks(self):
        if self.phase >= 2:
            return ["spiral", "teleport", "summon", "cross"]
        return self.attacks

    def atk_spiral(self, g):
        arms = 3 if self.phase == 1 else 5
        ang = random.uniform(0, TAU)
        d = random.choice((-1, 1))
        t = 0.0
        self.cast = 1.0
        while t < 3.2:
            for i in range(arms):
                self.fire(g, ang + i * TAU / arms, 165, r=7, color=ENEMY_BULLET3)
            ang += 0.21 * d
            audio.play("enemy_shoot", 0.25, 0.08)
            yield 0.1
            t += 0.1
        self.cast = 0.0

    def atk_cross(self, g):
        ang = 0.0
        t = 0.0
        while t < 3.6:
            for i in range(4):
                self.fire(g, ang + i * TAU / 4, 180, r=7, color=ENEMY_BULLET3)
                self.fire(g, -ang + i * TAU / 4 + TAU / 8, 140, r=7, color=ENEMY_BULLET)
            ang += 0.16
            yield 0.13
            t += 0.13

    def atk_teleport(self, g):
        for k in range(2 if self.phase == 1 else 3):
            t = 0.0
            while t < 0.35:
                t += self.dt
                self.fade = 1 - t / 0.35
                self.invuln = self.fade < 0.3
                yield 0
            g.particles.burst(self.x, self.y, (180, 120, 255), 20, 250, 5, 0.5)
            self.x, self.y = g.random_free_pos(260, fly=True, margin=80)
            t = 0.0
            while t < 0.35:
                t += self.dt
                self.fade = t / 0.35
                self.invuln = self.fade < 0.3
                yield 0
            self.fade = 1.0
            self.invuln = False
            for b in range(3):
                self.fire_spread(g, self.angle_to(g), 5 + self.extra * 2, 0.18, 230, r=7)
                audio.play("enemy_shoot", 0.6)
                yield 0.3

    def atk_summon(self, g):
        self.cast = 1.0
        yield 0.5
        mine = sum(1 for e in g.enemies if e.minion)
        for i in range(min(3, 5 - mine)):
            a = random.uniform(0, TAU)
            g.spawn_enemy("ghost", self.x + math.cos(a) * 70, self.y + math.sin(a) * 70, minion=True)
        audio.play("spawn")
        self.fire_ring(g, 16, 120, r=8, color=ENEMY_BULLET3)
        g.particles.ring(self.x, self.y, (160, 120, 255), 120, 0.4)
        self.cast = 0.0
        yield 0.6

    def ai(self, dt, g):
        super().ai(dt, g)
        if self.intro <= 0 and self.mv is None and self.fade >= 1:
            tx, ty = g.target_pos()
            a = self.t * 0.6
            self.vx = math.cos(a) * 60
            self.vy = math.sin(a * 1.3) * 40

    def draw(self, surf, g):
        if self.fade <= 0.02:
            return
        if self.fade < 1:
            sz = int(self.r * 7)
            tmp = pygame.Surface((sz, sz), pygame.SRCALPHA)
            ox, oy = self.x, self.y
            self.x, self.y = sz / 2, sz / 2
            self._draw(tmp, g)
            self.x, self.y = ox, oy
            tmp.set_alpha(int(255 * self.fade))
            surf.blit(tmp, (ox - sz / 2, oy - sz / 2))
            gfx.shadow(surf, ox, oy + self.r + 18, self.r * 1.8 * self.fade, 12)
        else:
            gfx.shadow(surf, self.x, self.y + self.r + 18, self.r * 1.8, 12)
            self._draw(surf, g)

    def _draw(self, surf, g):
        x, y = self.x, self.y - 10 + math.sin(self.t * 2) * 5
        c = WHITE if self.flash > 0 else self.color
        for i in range(3):
            a = self.t * 1.5 + i * TAU / 3
            rx, ry = x + math.cos(a) * 58, y + math.sin(a) * 26 + 10
            gfx.blit_glow(surf, rx, ry, 16, (90, 50, 150))
            gfx.draw_icon(surf, "rune", (190, 140, 255), rx, ry, 8)
        pts = [(x - 36, y + 38), (x - 20, y - 8), (x + 20, y - 8), (x + 36, y + 38), (x + 18, y + 30), (x, y + 40), (x - 18, y + 30)]
        gfx.poly(surf, c, pts, 3)
        if self.phase >= 2 and self.flash <= 0:
            gfx.blit_glow(surf, x, y - 18, 50, (80, 20, 40))
        gfx.outlined_circle(surf, (235, 230, 215) if self.flash <= 0 else WHITE, (x, y - 18), 20, 3)
        pygame.draw.rect(surf, INK, (x - 11, y - 6, 22, 9), border_radius=3)
        for i in range(4):
            pygame.draw.line(surf, (235, 230, 215), (x - 8 + i * 5, y - 6), (x - 8 + i * 5, y + 2), 2)
        ec = (255, 60, 90) if self.phase >= 2 else (190, 120, 255)
        for side in (-1, 1):
            pygame.draw.circle(surf, INK, (int(x + side * 8), int(y - 20)), 6)
            gfx.blit_glow(surf, x + side * 8, y - 20, 12, ec)
            pygame.draw.circle(surf, ec, (int(x + side * 8), int(y - 20)), 3)
        for i in range(5):
            bx = x - 16 + i * 8
            gfx.poly(surf, (230, 220, 200), [(bx - 3, y - 34), (bx, y - 46 - (i % 2) * 6), (bx + 3, y - 34)], 2)
        sx = x + 44
        pygame.draw.line(surf, INK, (sx, y - 30), (sx, y + 40), 7)
        pygame.draw.line(surf, (130, 100, 80), (sx, y - 30), (sx, y + 40), 4)
        gfx.blit_glow(surf, sx, y - 36, 22 + self.cast * 15, (120, 60, 200))
        gfx.outlined_circle(surf, (200, 150, 255), (sx, y - 36), 8, 2)


# ======================================================================  3. MATKA ROJU
class HiveMother(Boss):
    key = "hive"
    name = "Matka Roju"
    title = "Królowa Grot"
    radius = 44
    base_speed = 70
    contact = 16
    boss_hp = 1550
    color = (230, 190, 60)
    attacks = ["brood", "shotgun", "charge", "wave"]

    def setup(self, g):
        self.charge_dir = None
        self.tele = 0.0

    def atk_brood(self, g):
        self.mv = None
        yield 0.4
        mine = sum(1 for e in g.enemies if e.minion)
        for i in range(min(4, 8 - mine)):
            a = random.uniform(0, TAU)
            g.spawn_enemy("bat", self.x + math.cos(a) * 60, self.y + math.sin(a) * 60, minion=True)
        audio.play("spawn")
        g.shake(4)
        if self.phase >= 2:
            self.fire_ring(g, 14, 150, r=8)
        yield 0.5

    def atk_shotgun(self, g):
        self.mv = ("chase", self.spd * 0.6)
        for k in range(3 if self.phase == 1 else 4):
            a = self.angle_to(g)
            n = 12 + self.extra * 3
            for i in range(n):
                self.fire(g, a + random.uniform(-0.6, 0.6), random.uniform(180, 290), r=7, color=ENEMY_BULLET2)
            audio.play("enemy_shoot")
            g.shake(3)
            yield 0.7
        self.mv = None

    def atk_charge(self, g):
        self.mv = None
        for k in range(2 if self.phase == 1 else 3):
            a = self.angle_to(g)
            self.charge_dir = (math.cos(a), math.sin(a))
            self.tele = 0.6
            audio.play("charge", 0.7)
            t = 0.0
            while t < 0.6:
                t += self.dt
                a = self.angle_to(g)
                if t < 0.4:
                    self.charge_dir = (math.cos(a), math.sin(a))
                self.tele = 0.6 - t
                yield 0
            self.tele = 0.0
            sp = 620 * self.bspd
            t = 0.0
            while t < 1.6:
                t += self.dt
                self.vx, self.vy = self.charge_dir[0] * sp, self.charge_dir[1] * sp
                self.mv = ("dir", self.vx, self.vy)
                nx, ny = self.x + self.vx * self.dt * 2, self.y + self.vy * self.dt * 2
                if not (RX0 + self.r < nx < RX1 - self.r and RY0 + self.r < ny < RY1 - self.r):
                    break
                yield 0
            self.mv = None
            g.shake(8)
            audio.play("slam", 0.6)
            self.fire_ring(g, 10 + self.extra * 4, 170, r=8, offset=random.uniform(0, 1))
            yield 0.4
        self.charge_dir = None

    def atk_wave(self, g):
        self.mv = None
        for k in range(3):
            n = 32
            gap_center = random.randint(0, n - 1)
            gap = 5 if self.phase == 1 else 4
            for i in range(n):
                if min((i - gap_center) % n, (gap_center - i) % n) <= gap // 2:
                    continue
                self.fire(g, i * TAU / n, 150, r=8, color=ENEMY_BULLET3)
            audio.play("enemy_shoot")
            yield 0.9

    def ai(self, dt, g):
        super().ai(dt, g)
        if self.intro <= 0 and self.mv is None and self.charge_dir is None:
            self.vx = math.cos(self.t * 0.7) * 50
            self.vy = math.sin(self.t * 0.9) * 40

    def draw(self, surf, g):
        if self.charge_dir and self.tele > 0:
            ex, ey = self.x + self.charge_dir[0] * 1400, self.y + self.charge_dir[1] * 1400
            pygame.draw.line(surf, (255, 60, 60), (self.x, self.y), (ex, ey), 3 if int(self.t * 20) % 2 else 1)
        gfx.shadow(surf, self.x, self.y + self.r * 0.9, self.r * 2.4, self.r * 0.7, 90)
        x, y = self.x, self.y - 6
        c = WHITE if self.flash > 0 else self.color
        flap = math.sin(self.t * 30)
        ws = pygame.Surface((240, 140), pygame.SRCALPHA)
        for side in (-1, 1):
            cx = 120 + side * 60
            pygame.draw.ellipse(ws, (220, 240, 255, 110), (cx - 50, 40 - flap * 12, 100, 50))
            pygame.draw.ellipse(ws, (40, 40, 60, 160), (cx - 50, 40 - flap * 12, 100, 50), 2)
        surf.blit(ws, (x - 120, y - 90))
        blob(surf, x, y + 20, self.r * 0.9, self.r * 0.75, gfx.mul_color(c, 0.9), self.flash > 0)
        if self.flash <= 0:
            for i in range(3):
                pygame.draw.arc(surf, (40, 30, 30), (x - self.r * 0.85, y + 5 + i * 11, self.r * 1.7, 22), math.pi * 1.1, math.pi * 1.9, 5)
        blob(surf, x, y - 18, self.r * 0.62, self.r * 0.55, c, self.flash > 0)
        for i, (ox, oy) in enumerate(((-12, -22), (12, -22), (-6, -12), (6, -12))):
            col = (255, 60, 60) if self.phase >= 2 else (200, 30, 40)
            pygame.draw.circle(surf, INK, (int(x + ox), int(y + oy)), 6 if i < 2 else 4)
            pygame.draw.circle(surf, col, (int(x + ox), int(y + oy)), 4 if i < 2 else 3)
        for side in (-1, 1):
            pygame.draw.line(surf, INK, (x + side * 8, y - 40), (x + side * 22, y - 62), 3)
            pygame.draw.circle(surf, INK, (int(x + side * 22), int(y - 62)), 4)


# ======================================================================  4. WŁADCA KUŹNI
class ForgeLord(Boss):
    key = "forge"
    name = "Władca Kuźni"
    title = "Żelazny Tyran"
    radius = 44
    base_speed = 80
    contact = 18
    boss_hp = 2050
    color = (90, 85, 95)
    attacks = ["slam", "flame", "dash"]

    def setup(self, g):
        self.lift = 0.0
        self.tele_dir = None
        self.tele = 0.0

    def available_attacks(self):
        if self.phase >= 2:
            return ["slam", "flame", "dash", "meteors"]
        return self.attacks

    def atk_slam(self, g):
        self.mv = None
        t = 0.0
        audio.play("charge", 0.6)
        while t < 0.8:
            t += self.dt
            self.lift = t / 0.8
            yield 0
        self.lift = 0
        audio.play("slam")
        g.shake(12)
        g.particles.ring(self.x, self.y, (255, 150, 60), 160, 0.4, width=8)
        rings = 3 if self.phase == 1 else 4
        for k in range(rings):
            self.fire_ring(g, 22, 200, offset=(k % 2) * TAU / 44, r=8, color=ENEMY_BULLET2)
            yield 0.25
        if self.phase >= 2:
            for i in range(3):
                x, y = g.random_free_pos(0, fly=True, margin=60)
                g.effects.append(FirePool(x, y, 50, 12 * self.dmg_scale, 4.5))
        yield 0.4

    def atk_flame(self, g):
        self.mv = ("chase", self.spd * 0.4)
        streams = 3 if self.phase == 1 else 4
        ang = random.uniform(0, TAU)
        d = random.choice((-1, 1))
        t = 0.0
        while t < 4.0:
            for i in range(streams):
                self.fire(g, ang + i * TAU / streams, 230, r=7, color=(255, 140, 50))
            ang += d * 0.11
            yield 0.075
            t += 0.075
        self.mv = None

    def atk_dash(self, g):
        for k in range(3):
            a = self.angle_to(g)
            self.tele_dir = (math.cos(a), math.sin(a))
            t = 0.0
            while t < 0.45:
                t += self.dt
                self.tele = 0.45 - t
                yield 0
            self.tele = 0
            sp = 680 * self.bspd
            t = 0.0
            while t < 0.5:
                t += self.dt
                self.mv = ("dir", self.tele_dir[0] * sp, self.tele_dir[1] * sp)
                yield 0
            self.mv = None
            self.fire_ring(g, 8 + self.extra * 4, 190, r=8, color=ENEMY_BULLET2, offset=random.uniform(0, 1))
            g.shake(5)
            audio.play("thunk", 0.8)
            yield 0.25
        self.tele_dir = None

    def atk_meteors(self, g):
        self.mv = None
        p = g.player
        for i in range(7):
            x = min(RX1 - 50, max(RX0 + 50, p.x + random.uniform(-220, 220)))
            y = min(RY1 - 50, max(RY0 + 50, p.y + random.uniform(-160, 160)))
            if i == 0:
                x, y = p.x, p.y

            def boom(gg, x=x, y=y, s=self):
                gg.explode(x, y, 70, 18 * s.dmg_scale, hurt_player=True, hurt_enemies=False)
                gg.effects.append(FirePool(x, y, 42, 10 * s.dmg_scale, 3.0))
            g.effects.append(Telegraph(x, y, 70, 1.1 + i * 0.12, boom, (255, 120, 40)))
        yield 1.8

    def draw(self, surf, g):
        if self.tele_dir and self.tele > 0:
            ex, ey = self.x + self.tele_dir[0] * 400, self.y + self.tele_dir[1] * 400
            pygame.draw.line(surf, (255, 90, 40), (self.x, self.y), (ex, ey), 3 if int(self.t * 20) % 2 else 1)
        if self.lift > 0:
            pygame.draw.circle(surf, (255, 80, 40), (int(self.x), int(self.y)), int(60 + 100 * self.lift), 2)
        gfx.shadow(surf, self.x, self.y + self.r * 0.85, self.r * 2.3, self.r * 0.7, 90)
        x, y = self.x, self.y - self.lift * 20
        c = WHITE if self.flash > 0 else self.color
        core = (255, 80, 40) if self.phase >= 2 else (255, 150, 50)
        gfx.blit_glow(surf, x, y, 70 + math.sin(self.t * 5) * 6, gfx.mul_color(core, 0.5))
        pts = [(x - 40, y + 36), (x - 46, y - 10), (x - 26, y - 38), (x + 26, y - 38), (x + 46, y - 10), (x + 40, y + 36)]
        gfx.poly(surf, c, pts, 4)
        if self.flash <= 0:
            pygame.draw.rect(surf, gfx.mul_color(c, 1.3), (x - 30, y - 10, 60, 8))
            gfx.outlined_circle(surf, core, (x, y + 12), 12, 3)
            pygame.draw.circle(surf, (255, 240, 180), (int(x), int(y + 12)), 5)
        for side in (-1, 1):
            gfx.poly(surf, (60, 55, 60), [(x + side * 22, y - 34), (x + side * 44, y - 64), (x + side * 34, y - 30)], 3)
        for side in (-1, 1):
            pygame.draw.circle(surf, INK, (int(x + side * 12), int(y - 22)), 7)
            gfx.blit_glow(surf, x + side * 12, y - 22, 14, core)
            pygame.draw.circle(surf, core, (int(x + side * 12), int(y - 22)), 4)
        ha = self.face + 0.6
        hx, hy = x + math.cos(ha) * 54, y + math.sin(ha) * 30 + 6
        pygame.draw.line(surf, INK, (x + 30, y + 6), (hx, hy), 9)
        pygame.draw.line(surf, (120, 80, 50), (x + 30, y + 6), (hx, hy), 5)
        hr = pygame.Rect(0, 0, 30, 22)
        hr.center = (int(hx), int(hy))
        pygame.draw.rect(surf, INK, hr.inflate(6, 6), border_radius=4)
        pygame.draw.rect(surf, (150, 150, 160), hr, border_radius=3)


# ======================================================================  5. SERCE OTCHŁANI
class AbyssHeart(Boss):
    key = "abyss"
    name = "Serce Otchłani"
    title = "Koniec Wszystkiego"
    radius = 54
    base_speed = 50
    contact = 20
    boss_hp = 3300
    flying = True
    color = (150, 40, 90)
    phase_thresholds = (0.66, 0.33)
    attacks = ["flower", "aimed", "spawn"]
    rest = (0.5, 0.9)

    def setup(self, g):
        self.beat = 0.0
        self.home = (RCX, RCY - 60)

    def available_attacks(self):
        if self.phase == 1:
            return ["flower", "aimed", "spawn"]
        if self.phase == 2:
            return ["flower", "aimed", "streams", "rain", "spawn"]
        return ["streams", "rain", "hell", "aimed", "flower"]

    def atk_flower(self, g):
        rings = 5 if self.phase < 3 else 7
        n = 18
        for k in range(rings):
            self.fire_ring(g, n, 150 + self.phase * 10, offset=k * 0.09, r=8, color=ENEMY_BULLET3)
            audio.play("enemy_shoot", 0.5)
            yield 0.28

    def atk_aimed(self, g):
        shots = 8 + self.phase * 2
        for k in range(shots):
            a = self.angle_to(g) + random.uniform(-0.1, 0.1)
            self.fire(g, a, 270, r=7)
            if self.phase >= 2:
                self.fire(g, a + 0.3, 230, r=7)
                self.fire(g, a - 0.3, 230, r=7)
            audio.play("enemy_shoot", 0.5)
            yield 0.12
        yield 0.3

    def atk_spawn(self, g):
        mine = sum(1 for e in g.enemies if e.minion)
        for i in range(min(2, 4 - mine)):
            a = random.uniform(0, TAU)
            g.spawn_enemy(random.choice(("imp", "ghost")), self.x + math.cos(a) * 90, self.y + math.sin(a) * 90, minion=True)
        audio.play("spawn")
        self.fire_ring(g, 24, 120, r=8, color=ENEMY_BULLET)
        yield 0.7

    def atk_streams(self, g):
        streams = 4 if self.phase == 2 else 5
        ang = random.uniform(0, TAU)
        d = random.choice((-1, 1))
        t = 0.0
        while t < 4.5:
            for i in range(streams):
                self.fire(g, ang + i * TAU / streams, 120, r=7, accel=140, max_speed=320, color=ENEMY_BULLET2)
            ang += d * 0.12
            yield 0.1
            t += 0.1

    def atk_rain(self, g):
        p = g.player
        count = 6 if self.phase == 2 else 9
        for i in range(count):
            x = min(RX1 - 40, max(RX0 + 40, p.x + random.uniform(-300, 300)))
            y = min(RY1 - 40, max(RY0 + 40, p.y + random.uniform(-200, 200)))

            def burst(gg, x=x, y=y, s=self):
                for k in range(8):
                    a = k * TAU / 8 + 0.2
                    s.fire(gg, a, 170, r=7, x=x, y=y, color=ENEMY_BULLET3)
                gg.particles.burst(x, y, (200, 90, 255), 12, 200, 5, 0.4)
                audio.play("enemy_shoot", 0.6)
            g.effects.append(Telegraph(x, y, 40, 0.9 + i * 0.15, burst, (200, 80, 255)))
        yield 2.0

    def atk_hell(self, g):
        ang = 0.0
        t = 0.0
        while t < 5.0:
            for i in range(3):
                self.fire(g, ang + i * TAU / 3, 160, r=7, color=ENEMY_BULLET3)
                self.fire(g, -ang * 1.3 + i * TAU / 3, 140, r=7, color=ENEMY_BULLET)
            ang += 0.17
            if int(t / 0.1) % 12 == 0:
                self.fire(g, self.angle_to(g), 290, r=8, color=ENEMY_BULLET2)
            yield 0.1
            t += 0.1

    def phase_change(self, g):
        gen = super().phase_change(g)
        for w in gen:
            yield w
        for i in range(2):
            a = random.uniform(0, TAU)
            g.spawn_enemy("ghost", self.x + math.cos(a) * 100, self.y + math.sin(a) * 100, minion=True)
        self.fire_ring(g, 36, 160, r=8, color=ENEMY_BULLET2)
        yield 0.6

    def ai(self, dt, g):
        self.beat += dt * (1.6 + self.phase * 0.5)
        super().ai(dt, g)
        if self.intro <= 0 and self.mv is None:
            hx = self.home[0] + math.sin(self.t * 0.5) * 260
            hy = self.home[1] + math.sin(self.t * 0.8) * 70
            self.vx = (hx - self.x) * 1.2
            self.vy = (hy - self.y) * 1.2

    def draw(self, surf, g):
        gfx.shadow(surf, self.x, self.y + self.r + 26, self.r * 1.8, 16, 80)
        pulse = max(0.0, math.sin(self.beat * math.pi * 2)) ** 6
        x, y = self.x, self.y - 10
        r = self.r * (1 + pulse * 0.08)
        glowc = (120, 20, 60) if self.phase < 3 else (180, 20, 30)
        gfx.blit_glow(surf, x, y, r * 2.4, glowc)
        for i in range(6):
            a = self.t * 0.8 + i * TAU / 6
            ox, oy = x + math.cos(a) * (r + 30), y + math.sin(a) * (r + 18)
            gfx.blit_glow(surf, ox, oy, 18, (90, 30, 140))
            gfx.outlined_circle(surf, (40, 15, 60), (ox, oy), 9, 2)
        c = WHITE if self.flash > 0 else (self.color if self.phase < 3 else (190, 30, 50))
        pts = []
        for i in range(32):
            a = i / 32 * TAU
            hx = 16 * math.sin(a) ** 3
            hy = -(13 * math.cos(a) - 5 * math.cos(2 * a) - 2 * math.cos(3 * a) - math.cos(4 * a))
            pts.append((x + hx * r / 16, y + hy * r / 16))
        gfx.poly(surf, c, pts, 4)
        if self.flash <= 0:
            for i in range(5):
                a = -0.6 + i * 0.3
                pygame.draw.line(surf, gfx.mul_color(c, 0.6), (x, y + 6), (x + math.sin(a) * r * 0.8, y - math.cos(a) * r * 0.6), 3)
        gfx.outlined_ellipse(surf, (245, 235, 230), x, y + 2, 24, 18, 3)
        ca, sa = math.cos(self.face), math.sin(self.face)
        ic = (255, 50, 80) if self.phase >= 2 else (190, 90, 255)
        pygame.draw.circle(surf, ic, (int(x + ca * 8), int(y + 2 + sa * 6)), 11)
        pygame.draw.ellipse(surf, INK, (x + ca * 9 - 3, y + 2 + sa * 7 - 8, 6, 16))


BOSSES = {1: SlimeKing, 2: Lich, 3: HiveMother, 4: ForgeLord, 5: AbyssHeart}
