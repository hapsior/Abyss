"""Zwykli przeciwnicy."""
import itertools
import math
import random

import pygame

from . import gfx
from .audio import audio
from .config import INK, RX0, RX1, RY0, RY1, ENEMY_BULLET, ENEMY_BULLET2, ENEMY_BULLET3, WHITE
from .entities import Bullet, FirePool, clamp

TAU = math.tau
_ids = itertools.count(1)


def blob(surf, x, y, rw, rh, color, flash=False, outline=3):
    c = (255, 255, 255) if flash else color
    gfx.outlined_ellipse(surf, c, x, y, rw, rh, outline)
    if not flash:
        pygame.draw.ellipse(surf, gfx.lerp_color(c, (0, 0, 0), 0.22), (x - rw * 0.8, y + rh * 0.25, rw * 1.6, rh * 0.65))
        pygame.draw.ellipse(surf, gfx.lerp_color(c, (255, 255, 255), 0.35), (x - rw * 0.55, y - rh * 0.7, rw * 0.55, rh * 0.38))


class Enemy:
    key = "enemy"
    name = "Wróg"
    base_hp = 20
    radius = 15
    base_speed = 80
    contact = 10
    xp = 1
    coin = 0.3
    flying = False
    boss = False
    color = (200, 80, 80)
    knock_resist = 1.0
    ghost = False  # przenika przeszkody

    def __init__(self, g, x, y, elite=False, minion=False):
        diff = g.diff
        depth = g.depth
        self.id = next(_ids)
        self.elite = elite
        self.minion = minion
        hp_scale = (1 + 0.42 * (depth - 1)) * diff["enemy_hp"]
        self.max_hp = self.hp = self.base_hp * hp_scale * (2.3 if elite else 1.0)
        self.r = self.radius * (1.25 if elite else 1.0)
        self.dmg_scale = (1 + 0.2 * (depth - 1)) * diff["enemy_dmg"] * (1.15 if elite else 1.0)
        self.contact_dmg = self.contact * self.dmg_scale
        self.bullet_dmg = 8 * self.dmg_scale
        self.spd = self.base_speed * (1 + 0.04 * (depth - 1)) * (1.1 if elite else 1.0)
        self.x, self.y = x, y
        self.vx = self.vy = 0.0
        self.kvx = self.kvy = 0.0
        self.flash = 0.0
        self.flash_cd = 0.0
        self.poison_t = self.poison_dps = 0.0
        self.burn_t = self.burn_dps = 0.0
        self.dot_tick = 0.0
        self.slow_t = 0.0
        self.orb_cd = 0.0
        self.thorn_cd = 0.0
        self.t = random.uniform(0, 10)
        self.alive = True
        self.state = "idle"
        self.timer = random.uniform(0.5, 1.5)
        self.face = 0.0
        self.nav_t = 0.0
        self.nav = (0.0, 0.0)
        self.fr = diff["fire_rate"]
        self.extra = diff["extra_bullets"] + (1 if elite else 0)
        self.bspd = diff["bullet_speed"]
        self.invuln = False
        self.alpha = 255
        self.summoner = None
        self.setup(g)

    def setup(self, g):
        pass

    # ------------------------------------------------------------------ narzędzia
    def fire(self, g, angle, speed, r=7, color=ENEMY_BULLET, dmg_mult=1.0, accel=0.0, turn=0.0, life=8.0,
             x=None, y=None, max_speed=None):
        sp = speed * self.bspd
        bx = self.x if x is None else x
        by = self.y if y is None else y
        b = Bullet(bx, by, math.cos(angle) * sp, math.sin(angle) * sp, self.bullet_dmg * dmg_mult, r, color,
                   False, life=life, accel=accel * self.bspd, turn=turn)
        if max_speed:
            b.max_speed = max_speed * self.bspd
        g.bullets.append(b)
        return b

    def fire_spread(self, g, angle, n, step, speed, **kw):
        for i in range(n):
            self.fire(g, angle + (i - (n - 1) / 2) * step, speed, **kw)

    def fire_ring(self, g, n, speed, offset=0.0, **kw):
        for i in range(n):
            self.fire(g, offset + i * TAU / n, speed, **kw)

    def angle_to(self, g):
        tx, ty = g.target_pos()
        return math.atan2(ty - self.y, tx - self.x)

    def dist_to(self, g):
        tx, ty = g.target_pos()
        return math.hypot(tx - self.x, ty - self.y)

    def chase(self, g, speed, dt):
        self.nav_t -= dt
        if self.nav_t <= 0:
            self.nav_t = 0.15
            self.nav = g.chase_dir(self)
        self.vx, self.vy = self.nav[0] * speed, self.nav[1] * speed

    def flee(self, g, speed):
        tx, ty = g.target_pos()
        dx, dy = self.x - tx, self.y - ty
        d = math.hypot(dx, dy) or 1
        self.vx, self.vy = dx / d * speed, dy / d * speed

    # ------------------------------------------------------------------ pętla
    def update(self, dt, g):
        self.t += dt
        self.flash = max(0.0, self.flash - dt)
        self.flash_cd = max(0.0, self.flash_cd - dt)
        self.orb_cd = max(0.0, self.orb_cd - dt)
        self.thorn_cd = max(0.0, self.thorn_cd - dt)
        if self.poison_t > 0 or self.burn_t > 0:
            self.dot_tick -= dt
            if self.dot_tick <= 0:
                self.dot_tick = 0.5
                d = 0.0
                if self.poison_t > 0:
                    d += self.poison_dps * 0.5
                if self.burn_t > 0:
                    d += self.burn_dps * 0.5
                g.damage_enemy(self, d, color=(150, 240, 90) if self.poison_t > 0 else (255, 150, 50), small=True,
                               dot=True)
                if not self.alive:
                    return
            self.poison_t -= dt
            self.burn_t -= dt
            if self.burn_t > 0 and random.random() < dt * 14:
                g.particles.burst(self.x + random.uniform(-self.r, self.r) * 0.6, self.y - self.r * 0.3,
                                  (255, 140, 40), 1, 40, 4, 0.4, grav=-140, glow=True)
            if self.poison_t > 0 and random.random() < dt * 6:
                g.particles.burst(self.x + random.uniform(-self.r, self.r) * 0.6, self.y - self.r * 0.5,
                                  (140, 230, 80), 1, 20, 3, 0.5, grav=-60)
        slow = 1.0
        if self.slow_t > 0:
            self.slow_t -= dt
            slow = 0.55
        self.vx = self.vy = 0.0
        self.ai(dt, g)
        if not self.alive:
            return
        self.x += (self.vx * slow + self.kvx) * dt
        self.y += (self.vy * slow + self.kvy) * dt
        k = max(0.0, 1 - 9 * dt)
        self.kvx *= k
        self.kvy *= k
        if self.flying or self.ghost:
            self.x = clamp(self.x, RX0 + self.r, RX1 - self.r)
            self.y = clamp(self.y, RY0 + self.r, RY1 - self.r)
        else:
            self.x, self.y = g.room.resolve_circle(self.x, self.y, self.r)
        tx, ty = g.target_pos()
        self.face = math.atan2(ty - self.y, tx - self.x)

    def ai(self, dt, g):
        self.chase(g, self.spd, dt)

    def knock(self, kx, ky):
        self.kvx += kx * self.knock_resist
        self.kvy += ky * self.knock_resist

    def on_death(self, g):
        pass

    # ------------------------------------------------------------------ rysowanie
    def draw(self, surf, g):
        if not self.flying:
            gfx.shadow(surf, self.x, self.y + self.r * 0.85, self.r * 2.1, self.r * 0.75)
        else:
            gfx.shadow(surf, self.x, self.y + self.r + 14, self.r * 1.6, self.r * 0.5, 50)
        if self.elite:
            gfx.blit_glow(surf, self.x, self.y, self.r * 2.4 + math.sin(self.t * 6) * 3, (120, 90, 10))
        self.draw_body(surf, g)
        if self.elite:
            pygame.draw.circle(surf, (255, 210, 70), (int(self.x), int(self.y)), int(self.r + 6), 2)
        if self.slow_t > 0:
            pygame.draw.circle(surf, (150, 220, 255), (int(self.x), int(self.y)), int(self.r + 3), 2)
        if self.hp < self.max_hp and not self.boss:
            w = max(26, int(self.r * 2))
            rect = pygame.Rect(0, 0, w, 6)
            rect.midbottom = (int(self.x), int(self.y - self.r - 8))
            pygame.draw.rect(surf, INK, rect.inflate(4, 4), border_radius=3)
            pygame.draw.rect(surf, (60, 20, 25), rect, border_radius=2)
            fw = int(w * max(0.0, self.hp / self.max_hp))
            if fw > 0:
                pygame.draw.rect(surf, (230, 70, 80) if not self.elite else (255, 200, 60), (rect.x, rect.y, fw, 6), border_radius=2)

    def draw_body(self, surf, g):
        blob(surf, self.x, self.y, self.r, self.r, self.color, self.flash > 0)
        gfx.eyes(surf, self.x, self.y, self.r, self.face)


# =====================================================================  TYPY
class Slime(Enemy):
    key = "slime"
    name = "Glut"
    base_hp = 22
    radius = 15
    base_speed = 95
    contact = 10
    xp = 1
    color = (110, 205, 95)

    def setup(self, g):
        self.cycle = random.uniform(0, 1)

    def ai(self, dt, g):
        self.cycle += dt / 0.9
        ph = self.cycle % 1.0
        if ph < 0.38:
            self.chase(g, self.spd * 2.4, dt)

    def draw_body(self, surf, g):
        ph = self.cycle % 1.0
        if ph < 0.38:
            s = math.sin(ph / 0.38 * math.pi)
            rw, rh, lift = self.r * (1 - 0.12 * s), self.r * (1 + 0.15 * s), s * 10
        else:
            k = max(0.0, 1 - (ph - 0.38) / 0.2)
            rw, rh, lift = self.r * (1 + 0.18 * k), self.r * (1 - 0.18 * k), 0
        y = self.y - lift
        blob(surf, self.x, y, rw, rh, self.color, self.flash > 0)
        gfx.eyes(surf, self.x, y, self.r, self.face)


class BigSlime(Slime):
    key = "big_slime"
    name = "Wielki Glut"
    base_hp = 60
    radius = 25
    base_speed = 75
    contact = 14
    xp = 3
    coin = 0.5
    color = (80, 175, 80)

    def on_death(self, g):
        for i in range(2):
            a = random.uniform(0, TAU)
            e = g.spawn_enemy("slime", self.x + math.cos(a) * 14, self.y + math.sin(a) * 14)
            if e:
                e.knock(math.cos(a) * 300, math.sin(a) * 300)


class Bat(Enemy):
    key = "bat"
    name = "Nietoperz"
    base_hp = 12
    radius = 12
    base_speed = 150
    contact = 8
    xp = 1
    coin = 0.2
    flying = True
    color = (120, 70, 150)

    def setup(self, g):
        self.wob = random.uniform(0, TAU)

    def ai(self, dt, g):
        self.wob += dt * 5
        a = self.angle_to(g) + math.sin(self.wob) * 0.9
        self.vx, self.vy = math.cos(a) * self.spd, math.sin(a) * self.spd

    def draw_body(self, surf, g):
        x, y = self.x, self.y - 8
        flap = math.sin(self.t * 22)
        c = (255, 255, 255) if self.flash > 0 else self.color
        wc = gfx.mul_color(c, 0.75)
        for side in (-1, 1):
            pts = [(x + side * self.r * 0.6, y - 2), (x + side * self.r * 2.1, y - 10 - flap * 10),
                   (x + side * self.r * 1.7, y + 4 - flap * 4), (x + side * self.r * 1.2, y + 2),
                   (x + side * self.r * 0.8, y + 7)]
            gfx.poly(surf, wc, pts, 2)
        blob(surf, x, y, self.r, self.r * 0.95, c, self.flash > 0)
        for side in (-1, 1):
            pygame.draw.polygon(surf, INK, [(x + side * 4, y - self.r + 2), (x + side * 9, y - self.r - 7), (x + side * 10, y - self.r + 4)])
        gfx.eyes(surf, x, y, self.r, self.face, white=(255, 220, 90))


class Shooter(Enemy):
    key = "shooter"
    name = "Kultysta"
    base_hp = 24
    radius = 15
    base_speed = 95
    contact = 10
    xp = 2
    coin = 0.35
    color = (190, 70, 90)

    def setup(self, g):
        self.timer = random.uniform(1.0, 2.0)
        self.strafe = random.choice((-1, 1))
        self.charge = 0.0

    def ai(self, dt, g):
        d = self.dist_to(g)
        a = self.angle_to(g)
        if d < 210:
            self.flee(g, self.spd)
        elif d > 360:
            self.chase(g, self.spd, dt)
        else:
            pa = a + math.pi / 2 * self.strafe
            self.vx, self.vy = math.cos(pa) * self.spd * 0.6, math.sin(pa) * self.spd * 0.6
            if random.random() < dt * 0.4:
                self.strafe *= -1
        self.timer -= dt * self.fr
        if self.timer < 0.35:
            self.charge = 1 - max(0.0, self.timer) / 0.35
        if self.timer <= 0:
            self.timer = random.uniform(1.6, 2.2)
            self.charge = 0.0
            n = 1 + self.extra * 2
            self.fire_spread(g, a, n, 0.22, 235)
            audio.play("enemy_shoot", 0.6)

    def draw_body(self, surf, g):
        c = (255, 255, 255) if self.flash > 0 else self.color
        x, y = self.x, self.y
        pts = [(x - self.r * 1.05, y + self.r), (x, y - self.r * 1.5), (x + self.r * 1.05, y + self.r)]
        gfx.poly(surf, gfx.mul_color(c, 0.7), pts, 3)
        blob(surf, x, y, self.r * 0.85, self.r * 0.8, c, self.flash > 0)
        if self.charge > 0:
            gfx.blit_glow(surf, x, y - 2, 20 + 20 * self.charge, (200, 40, 70))
        gfx.eyes(surf, x, y, self.r * 0.85, self.face, white=(255, 230, 120) if self.charge > 0 else (250, 250, 250))


class Charger(Enemy):
    key = "charger"
    name = "Dzik"
    base_hp = 42
    radius = 18
    base_speed = 70
    contact = 14
    xp = 2
    coin = 0.4
    color = (165, 105, 70)

    def setup(self, g):
        self.state = "walk"
        self.timer = random.uniform(1.0, 2.0)
        self.dir = (1.0, 0.0)

    def ai(self, dt, g):
        self.timer -= dt
        if self.state == "walk":
            self.chase(g, self.spd, dt)
            if self.timer <= 0 and self.dist_to(g) < 460 and g.room.los(self.x, self.y, *g.target_pos()):
                self.state = "windup"
                self.timer = 0.6
                a = self.angle_to(g)
                self.dir = (math.cos(a), math.sin(a))
                audio.play("charge", 0.5)
        elif self.state == "windup":
            a = self.angle_to(g)
            if self.timer > 0.2:
                self.dir = (math.cos(a), math.sin(a))
            if self.timer <= 0:
                self.state = "charge"
                self.timer = 0.75
        elif self.state == "charge":
            sp = 560 * self.bspd
            self.vx, self.vy = self.dir[0] * sp, self.dir[1] * sp
            nx, ny = self.x + self.vx * dt * 1.5, self.y + self.vy * dt * 1.5
            ob = g.room.tile_at(nx + self.dir[0] * self.r, ny + self.dir[1] * self.r)
            wall = not (RX0 + self.r < nx < RX1 - self.r and RY0 + self.r < ny < RY1 - self.r)
            if (ob is not None and ob.solid) or wall:
                self.state = "stun"
                self.timer = 0.7
                g.shake(4)
                audio.play("thunk")
                g.particles.burst(self.x + self.dir[0] * self.r, self.y + self.dir[1] * self.r, (200, 180, 160), 10, 200, 4, 0.4)
                if ob is not None and ob.kind in ("crate", "barrel"):
                    g.damage_obstacle(ob, 999)
            elif self.timer <= 0:
                self.state = "walk"
                self.timer = random.uniform(1.2, 2.0)
            if random.random() < dt * 30:
                g.particles.burst(self.x, self.y + self.r, (170, 150, 130), 1, 60, 4, 0.4, kind="smoke")
        elif self.state == "stun":
            if self.timer <= 0:
                self.state = "walk"
                self.timer = random.uniform(1.0, 1.8)

    def draw(self, surf, g):
        if self.state == "windup":
            ex, ey = self.x + self.dir[0] * 300, self.y + self.dir[1] * 300
            s = 1 if int(self.t * 20) % 2 else 2
            pygame.draw.line(surf, (255, 60, 60), (self.x, self.y), (ex, ey), s)
        super().draw(surf, g)

    def draw_body(self, surf, g):
        x, y = self.x, self.y
        if self.state == "windup":
            x += random.uniform(-2, 2)
        c = (255, 255, 255) if self.flash > 0 else self.color
        blob(surf, x, y, self.r * 1.1, self.r * 0.9, c, self.flash > 0)
        fa = self.face if self.state != "charge" else math.atan2(self.dir[1], self.dir[0])
        ca, sa = math.cos(fa), math.sin(fa)
        for side in (-1, 1):
            bx = x + ca * self.r * 0.7 - sa * side * self.r * 0.45
            by = y + sa * self.r * 0.7 + ca * side * self.r * 0.45 + 4
            tip = (bx + ca * 12 - sa * side * 2, by + sa * 12 - 6)
            gfx.poly(surf, (240, 235, 210), [(bx - 3, by), (bx + 3, by), tip], 2)
        gfx.eyes(surf, x, y, self.r, fa, angry=True)
        if self.state == "stun":
            for i in range(3):
                a = self.t * 6 + i * TAU / 3
                pygame.draw.circle(surf, (255, 230, 90), (int(x + math.cos(a) * 14), int(y - self.r - 6 + math.sin(a) * 4)), 3)


class Turret(Enemy):
    key = "turret"
    name = "Oko"
    base_hp = 36
    radius = 17
    base_speed = 0
    contact = 10
    xp = 2
    coin = 0.35
    color = (90, 85, 100)
    knock_resist = 0.0

    def setup(self, g):
        self.timer = random.uniform(1.0, 2.2)
        self.mode = random.randint(0, 1)
        self.charge = 0.0

    def ai(self, dt, g):
        self.timer -= dt * self.fr
        self.charge = max(0.0, 1 - self.timer / 0.4) if self.timer < 0.4 else 0.0
        if self.timer <= 0:
            self.timer = 2.3
            n = 4 if self.extra == 0 else 8
            off = (math.pi / 4) * self.mode if n == 4 else 0.0
            self.fire_ring(g, n, 175, offset=off, color=ENEMY_BULLET3)
            self.mode ^= 1
            audio.play("enemy_shoot", 0.6)

    def draw_body(self, surf, g):
        x, y = self.x, self.y
        c = (255, 255, 255) if self.flash > 0 else self.color
        gfx.poly(surf, c, [(x - self.r, y + self.r * 0.2), (x + self.r, y + self.r * 0.2),
                           (x + self.r * 0.8, y + self.r), (x - self.r * 0.8, y + self.r)], 3)
        gfx.outlined_circle(surf, (240, 235, 230) if self.flash <= 0 else WHITE, (x, y - 4), self.r * 0.85)
        ca, sa = math.cos(self.face), math.sin(self.face)
        ic = gfx.lerp_color((170, 90, 230), (255, 80, 80), self.charge)
        pygame.draw.circle(surf, ic, (int(x + ca * 5), int(y - 4 + sa * 5)), int(self.r * 0.45))
        pygame.draw.circle(surf, INK, (int(x + ca * 7), int(y - 4 + sa * 7)), int(self.r * 0.2))
        if self.charge > 0:
            gfx.blit_glow(surf, x, y - 4, 20 + 25 * self.charge, (120, 40, 160))


class Bomber(Enemy):
    key = "bomber"
    name = "Wybuchacz"
    base_hp = 18
    radius = 14
    base_speed = 135
    contact = 0
    xp = 1
    coin = 0.3
    color = (60, 60, 72)

    def setup(self, g):
        self.fuse = -1.0

    def ai(self, dt, g):
        if self.fuse < 0:
            self.chase(g, self.spd, dt)
            if self.dist_to(g) < 78:
                self.fuse = 0.75
                audio.play("charge", 0.4)
        else:
            self.fuse -= dt
            if self.fuse <= 0:
                self.alive = False
                self.hp = 0
                g.explode(self.x, self.y, 88, 22 * self.dmg_scale, hurt_player=True, hurt_enemies=True)
                g.remove_enemy(self, drops=False)

    def on_death(self, g):
        # zabity przez gracza – wybucha raniąc tylko wrogów
        g.explode(self.x, self.y, 70, 30, hurt_player=False, hurt_enemies=True, small=True)

    def draw_body(self, surf, g):
        x, y = self.x, self.y
        s = 1.0
        c = self.color
        if self.fuse >= 0:
            s = 1 + (0.75 - self.fuse) * 0.4
            if int(self.fuse * 16) % 2 == 0:
                c = (230, 60, 50)
        if self.flash > 0:
            c = WHITE
        blob(surf, x, y, self.r * s, self.r * s, c, False)
        pygame.draw.line(surf, (200, 160, 100), (x + 4, y - self.r * s + 2), (x + 9, y - self.r * s - 8), 3)
        sp = (x + 9, y - self.r * s - 9)
        gfx.blit_glow(surf, sp[0], sp[1], 10 + random.random() * 6, (200, 130, 30))
        pygame.draw.circle(surf, (255, 230, 120), (int(sp[0]), int(sp[1])), 3)
        gfx.eyes(surf, x, y, self.r * s, self.face, angry=True)


class Summoner(Enemy):
    key = "summoner"
    name = "Nekromanta"
    base_hp = 42
    radius = 16
    base_speed = 85
    contact = 10
    xp = 4
    coin = 0.6
    color = (70, 120, 90)

    def setup(self, g):
        self.timer = random.uniform(1.5, 2.5)
        self.tp_cd = 2.0
        self.cast = 0.0
        self.fade = 0.0
        self.tp_target = None

    def ai(self, dt, g):
        d = self.dist_to(g)
        self.tp_cd -= dt
        if self.fade > 0:
            self.fade -= dt
            if self.fade <= 0.25 and self.tp_target:
                g.particles.burst(self.x, self.y, (120, 255, 160), 14, 200, 4, 0.4)
                self.x, self.y = self.tp_target
                self.tp_target = None
                g.particles.burst(self.x, self.y, (120, 255, 160), 14, 200, 4, 0.4)
            self.alpha = int(255 * abs(self.fade - 0.25) / 0.25) if self.fade > 0 else 255
            return
        self.alpha = 255
        if d < 170 and self.tp_cd <= 0:
            self.tp_cd = 3.5
            self.fade = 0.5
            self.tp_target = g.random_free_pos(320)
            return
        if d < 300:
            self.flee(g, self.spd)
        elif d > 450:
            self.chase(g, self.spd * 0.8, dt)
        self.timer -= dt * self.fr
        if self.timer < 0.6:
            self.cast = 1 - max(0.0, self.timer) / 0.6
        if self.timer <= 0:
            self.timer = 4.5
            self.cast = 0
            mine = sum(1 for e in g.enemies if e.summoner == self.id)
            n = min(2 + (1 if self.elite else 0), 6 - mine)
            for i in range(n):
                a = random.uniform(0, TAU)
                kind = "bat" if g.depth != 2 else random.choice(("bat", "slime"))
                e = g.spawn_enemy(kind, self.x + math.cos(a) * 30, self.y + math.sin(a) * 30, minion=True)
                if e:
                    e.summoner = self.id
            if n > 0:
                audio.play("spawn", 0.8)
                g.particles.ring(self.x, self.y, (120, 255, 160), 50, 0.3)
            if self.extra > 1:
                self.fire_ring(g, 8, 160, color=ENEMY_BULLET3)

    def draw_body(self, surf, g):
        if self.alpha < 255:
            tmp = pygame.Surface((self.r * 6, self.r * 6), pygame.SRCALPHA)
            ox, oy = self.x, self.y
            self.x, self.y = self.r * 3, self.r * 3
            self._body(tmp)
            self.x, self.y = ox, oy
            tmp.set_alpha(self.alpha)
            surf.blit(tmp, (ox - self.r * 3, oy - self.r * 3))
        else:
            self._body(surf)

    def _body(self, surf):
        x, y = self.x, self.y
        c = (255, 255, 255) if self.flash > 0 else self.color
        pts = [(x - self.r * 1.1, y + self.r), (x - self.r * 0.6, y - self.r * 1.2), (x + self.r * 0.6, y - self.r * 1.2), (x + self.r * 1.1, y + self.r)]
        gfx.poly(surf, gfx.mul_color(c, 0.6), pts, 3)
        gfx.outlined_circle(surf, (40, 30, 45), (x, y - self.r * 0.35), self.r * 0.7, 2)
        if self.cast > 0:
            gfx.blit_glow(surf, x, y - self.r * 1.6, 15 + 20 * self.cast, (60, 200, 100))
        gfx.eyes(surf, x, y - self.r * 0.3, self.r * 0.75, self.face, glow_col=(120, 255, 160))
        pygame.draw.line(surf, (120, 90, 60), (x + self.r * 1.2, y - self.r * 1.4), (x + self.r * 1.2, y + self.r), 3)
        gfx.draw_icon(surf, "skull", (40, 30, 45), x + self.r * 1.2, y - self.r * 1.6, 7)


class Golem(Enemy):
    key = "golem"
    name = "Golem"
    base_hp = 110
    radius = 26
    base_speed = 55
    contact = 18
    xp = 5
    coin = 0.7
    color = (120, 115, 125)
    knock_resist = 0.3

    def setup(self, g):
        self.timer = random.uniform(2.0, 3.5)
        self.state = "walk"
        self.lift = 0.0

    def ai(self, dt, g):
        self.timer -= dt * self.fr
        if self.state == "walk":
            self.chase(g, self.spd, dt)
            if self.timer <= 0 and self.dist_to(g) < 430:
                self.state = "windup"
                self.timer = 0.7
        elif self.state == "windup":
            self.lift = min(1.0, self.lift + dt * 2)
            if self.timer <= 0:
                self.state = "walk"
                self.timer = 3.4
                self.lift = 0
                n = 12 if self.extra == 0 else 18
                self.fire_ring(g, n, 180, offset=random.uniform(0, 1), r=8, color=ENEMY_BULLET2)
                g.shake(5)
                audio.play("slam", 0.7)
                g.particles.ring(self.x, self.y + self.r * 0.5, (200, 190, 170), 90, 0.35, width=6)
                g.particles.burst(self.x, self.y + self.r * 0.6, (160, 150, 140), 14, 220, 6, 0.5, kind="smoke")

    def draw(self, surf, g):
        if self.state == "windup":
            f = 1 - self.timer / 0.7
            pygame.draw.circle(surf, (255, 80, 70), (int(self.x), int(self.y)), int(40 + 60 * f), 2)
        super().draw(surf, g)

    def draw_body(self, surf, g):
        x, y = self.x, self.y - self.lift * 12
        c = (255, 255, 255) if self.flash > 0 else self.color
        pts = []
        for i in range(8):
            a = i / 8 * TAU + 0.2
            rr = self.r * (1 + 0.08 * math.sin(i * 2.7))
            pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr * 0.92))
        gfx.poly(surf, c, pts, 3)
        if self.flash <= 0:
            for i in range(3):
                a = i * 2.1 + 0.4
                pygame.draw.line(surf, (255, 140, 60), (x + math.cos(a) * 6, y + math.sin(a) * 6),
                                 (x + math.cos(a) * self.r * 0.8, y + math.sin(a) * self.r * 0.8), 3)
        for side in (-1, 1):
            gfx.outlined_circle(surf, gfx.mul_color(c, 0.85), (x + side * self.r * 1.05, y + 6), self.r * 0.38, 3)
        gfx.eyes(surf, x, y - 4, self.r * 0.8, self.face, glow_col=(255, 150, 60))


class Sniper(Enemy):
    key = "sniper"
    name = "Snajper"
    base_hp = 28
    radius = 14
    base_speed = 110
    contact = 8
    xp = 3
    coin = 0.45
    color = (80, 110, 150)

    def setup(self, g):
        self.state = "move"
        self.timer = random.uniform(0.6, 1.2)
        self.aim = 0.0
        self.dest = None

    def ai(self, dt, g):
        self.timer -= dt
        if self.state == "move":
            if self.dest is None:
                self.dest = g.random_free_pos(300)
            dx, dy = self.dest[0] - self.x, self.dest[1] - self.y
            d = math.hypot(dx, dy)
            if d > 10:
                self.vx, self.vy = dx / d * self.spd, dy / d * self.spd
            if self.timer <= 0 or d <= 10:
                self.state = "aim"
                self.timer = 1.25 / self.fr
                self.dest = None
        elif self.state == "aim":
            if self.timer > 0.3:
                self.aim = self.angle_to(g)
            if self.timer <= 0:
                self.fire(g, self.aim, 640, r=6, dmg_mult=1.4, color=(255, 240, 120))
                if self.extra:
                    self.fire_spread(g, self.aim, 2, 0.35, 520, r=6, color=(255, 240, 120))
                audio.play("enemy_shoot", 0.9)
                g.particles.spark(self.x, self.y, (255, 240, 150), 6, 300, self.aim, 0.4)
                self.state = "move"
                self.timer = random.uniform(0.9, 1.5)

    def draw(self, surf, g):
        if self.state == "aim":
            locked = self.timer <= 0.3
            if not locked or int(self.t * 30) % 2 == 0:
                ex, ey = self.x + math.cos(self.aim) * 1400, self.y + math.sin(self.aim) * 1400
                pygame.draw.line(surf, (255, 60, 60) if locked else (255, 120, 120), (self.x, self.y), (ex, ey), 2 if locked else 1)
        super().draw(surf, g)

    def draw_body(self, surf, g):
        x, y = self.x, self.y
        c = (255, 255, 255) if self.flash > 0 else self.color
        blob(surf, x, y, self.r, self.r * 1.05, c, self.flash > 0)
        a = self.aim if self.state == "aim" else self.face
        ca, sa = math.cos(a), math.sin(a)
        pygame.draw.line(surf, INK, (x, y), (x + ca * 28, y + sa * 28), 7)
        pygame.draw.line(surf, (60, 60, 70), (x, y), (x + ca * 27, y + sa * 27), 4)
        gfx.outlined_circle(surf, (240, 240, 240), (x + ca * 3, y - 3 + sa * 3), self.r * 0.45, 2)
        pygame.draw.circle(surf, (255, 60, 60), (int(x + ca * 5), int(y - 3 + sa * 5)), int(self.r * 0.22))


class Ghost(Enemy):
    key = "ghost"
    name = "Upiór"
    base_hp = 26
    radius = 15
    base_speed = 95
    contact = 10
    xp = 2
    coin = 0.35
    flying = True
    ghost = True
    color = (190, 215, 245)

    def setup(self, g):
        self.timer = random.uniform(2.0, 3.5)
        self.dash = 0.0
        self.dir = (0.0, 0.0)
        self.wob = random.uniform(0, TAU)

    def ai(self, dt, g):
        self.timer -= dt
        self.wob += dt * 3
        if self.dash > 0:
            self.dash -= dt
            self.vx, self.vy = self.dir[0] * 330 * self.bspd, self.dir[1] * 330 * self.bspd
            if random.random() < dt * 40:
                g.particles.burst(self.x, self.y, (180, 210, 255), 1, 10, 6, 0.4)
            return
        a = self.angle_to(g) + math.sin(self.wob) * 0.6
        self.vx, self.vy = math.cos(a) * self.spd, math.sin(a) * self.spd
        if self.timer <= 0:
            self.timer = random.uniform(2.6, 3.6)
            self.dash = 0.5
            a = self.angle_to(g)
            self.dir = (math.cos(a), math.sin(a))
            if self.extra > 1:
                self.fire_ring(g, 6, 140, color=ENEMY_BULLET3)

    def draw_body(self, surf, g):
        x, y = self.x, self.y - 10 + math.sin(self.t * 3) * 3
        c = (255, 255, 255) if self.flash > 0 else self.color
        r = self.r
        pts = [(x - r, y + r)]
        for i in range(13):
            a = math.pi + i / 12 * math.pi
            pts.append((x + math.cos(a) * r, y + math.sin(a) * r * 1.1))
        w = math.sin(self.t * 8) * 3
        pts += [(x + r, y + r), (x + r * 0.5, y + r * 0.7 + w), (x, y + r + 2 - w), (x - r * 0.5, y + r * 0.7 + w)]
        gfx.blit_glow(surf, x, y, r * 2.2, (40, 60, 90))
        gfx.poly(surf, c, pts, 3)
        ca, sa = math.cos(self.face), math.sin(self.face)
        for side in (-1, 1):
            pygame.draw.ellipse(surf, INK, (x + side * r * 0.38 - 4 + ca * 3, y - 6 + sa * 2, 8, 11))
        pygame.draw.ellipse(surf, INK, (x - 4 + ca * 3, y + 6 + sa * 2, 8, 6))


class Spinner(Enemy):
    key = "spinner"
    name = "Kolczak"
    base_hp = 45
    radius = 17
    base_speed = 210
    contact = 14
    xp = 3
    coin = 0.4
    color = (150, 160, 175)
    knock_resist = 0.15

    def setup(self, g):
        a = random.choice((0.25, 0.75, 1.25, 1.75)) * math.pi + random.uniform(-0.2, 0.2)
        self.dir = [math.cos(a), math.sin(a)]

    def ai(self, dt, g):
        sp = self.spd
        nx = self.x + self.dir[0] * sp * dt
        ny = self.y + self.dir[1] * sp * dt
        if nx < RX0 + self.r or nx > RX1 - self.r:
            self.dir[0] *= -1
        if ny < RY0 + self.r or ny > RY1 - self.r:
            self.dir[1] *= -1
        ob = g.room.tile_at(nx + self.dir[0] * self.r, self.y)
        if ob is not None and ob.solid:
            self.dir[0] *= -1
        ob = g.room.tile_at(self.x, ny + self.dir[1] * self.r)
        if ob is not None and ob.solid:
            self.dir[1] *= -1
        self.vx, self.vy = self.dir[0] * sp, self.dir[1] * sp

    def draw_body(self, surf, g):
        c = (255, 255, 255) if self.flash > 0 else self.color
        pts = gfx.star_points(self.x, self.y, self.r * 1.35, self.r * 0.85, 8, self.t * 6)
        gfx.poly(surf, c, pts, 3)
        gfx.outlined_circle(surf, gfx.mul_color(c, 0.8), (self.x, self.y), self.r * 0.6, 2)
        gfx.eyes(surf, self.x, self.y, self.r * 0.7, self.face, angry=True)


class Imp(Enemy):
    key = "imp"
    name = "Chochlik"
    base_hp = 30
    radius = 14
    base_speed = 120
    contact = 10
    xp = 3
    coin = 0.45
    color = (230, 90, 50)
    flying = True

    def setup(self, g):
        self.timer = random.uniform(0.8, 1.6)
        self.state = "idle"
        self.fade = 0.0
        self.dest = None

    def ai(self, dt, g):
        self.timer -= dt * self.fr
        if self.state == "idle":
            a = self.angle_to(g) + math.pi / 2
            self.vx, self.vy = math.cos(a) * self.spd * 0.5, math.sin(a) * self.spd * 0.5
            if self.timer <= 0:
                n = 5 + self.extra * 2
                self.fire_spread(g, self.angle_to(g), n, 0.2, 130, accel=220, max_speed=330, color=ENEMY_BULLET2)
                audio.play("enemy_shoot", 0.7)
                self.state = "out"
                self.fade = 0.4
                self.timer = 2.0
                if random.random() < 0.35:
                    g.effects.append(FirePool(self.x, self.y + 6, 36, 10 * self.dmg_scale, 3.0))
        elif self.state == "out":
            self.fade -= dt
            self.alpha = int(255 * max(0.0, self.fade) / 0.4)
            self.invuln = self.fade < 0.2
            if self.fade <= 0:
                self.x, self.y = g.random_free_pos(220, fly=True)
                self.state = "in"
                self.fade = 0.4
                g.particles.burst(self.x, self.y, (255, 120, 50), 10, 150, 4, 0.4)
        elif self.state == "in":
            self.fade -= dt
            self.alpha = int(255 * (1 - max(0.0, self.fade) / 0.4))
            self.invuln = self.fade > 0.2
            if self.fade <= 0:
                self.state = "idle"
                self.alpha = 255
                self.invuln = False
                self.timer = random.uniform(0.7, 1.3)

    def draw_body(self, surf, g):
        if self.alpha < 20:
            return
        if self.alpha < 255:
            tmp = pygame.Surface((self.r * 6, self.r * 6), pygame.SRCALPHA)
            ox, oy = self.x, self.y
            self.x, self.y = self.r * 3, self.r * 3
            self._body(tmp)
            self.x, self.y = ox, oy
            tmp.set_alpha(self.alpha)
            surf.blit(tmp, (ox - self.r * 3, oy - self.r * 3))
        else:
            self._body(surf)

    def _body(self, surf):
        x, y = self.x, self.y - 8 + math.sin(self.t * 5) * 3
        c = (255, 255, 255) if self.flash > 0 else self.color
        for side in (-1, 1):
            flap = math.sin(self.t * 18) * 5
            gfx.poly(surf, (120, 40, 40), [(x + side * 8, y), (x + side * 26, y - 10 - flap), (x + side * 20, y + 6)], 2)
        blob(surf, x, y, self.r, self.r, c, self.flash > 0)
        for side in (-1, 1):
            gfx.poly(surf, (60, 30, 30), [(x + side * 5, y - self.r + 3), (x + side * 11, y - self.r - 9), (x + side * 12, y - self.r + 5)], 2)
        gfx.eyes(surf, x, y, self.r, self.face, white=(255, 240, 120), angry=True)


ENEMY_TYPES = {cls.key: cls for cls in (Slime, BigSlime, Bat, Shooter, Charger, Turret, Bomber, Summoner, Golem,
                                         Sniper, Ghost, Spinner, Imp)}

ENEMY_COST = dict(slime=1, big_slime=3, bat=1, shooter=2, charger=2.5, turret=2, bomber=1.5, summoner=3.5,
                  golem=4.5, sniper=3, ghost=2, spinner=3, imp=3)

FLOOR_POOLS = {
    1: [("slime", 5), ("bat", 4), ("shooter", 3), ("charger", 2), ("big_slime", 2), ("bomber", 1)],
    2: [("ghost", 4), ("shooter", 3), ("summoner", 2), ("turret", 3), ("slime", 2), ("charger", 2), ("bomber", 2), ("bat", 2)],
    3: [("big_slime", 3), ("turret", 3), ("golem", 2), ("sniper", 3), ("bat", 3), ("spinner", 2), ("shooter", 2), ("ghost", 1)],
    4: [("imp", 4), ("golem", 2), ("bomber", 3), ("charger", 3), ("sniper", 2), ("spinner", 2), ("turret", 2), ("shooter", 1)],
    5: [("ghost", 3), ("imp", 3), ("golem", 2), ("sniper", 2), ("summoner", 2), ("spinner", 2), ("bomber", 2), ("charger", 2), ("turret", 1)],
}


def roll_wave(rng, depth, budget):
    pool = FLOOR_POOLS[min(depth, 5)]
    total_w = sum(w for _, w in pool)
    out = []
    guard = 0
    while budget > 0.5 and guard < 60:
        guard += 1
        r = rng.random() * total_w
        for k, w in pool:
            r -= w
            if r <= 0:
                break
        c = ENEMY_COST[k]
        if c > budget + 0.5 and len(out) > 0:
            cheap = [kk for kk, _ in pool if ENEMY_COST[kk] <= budget + 0.5]
            if not cheap:
                break
            k = rng.choice(cheap)
            c = ENEMY_COST[k]
        out.append(k)
        budget -= c
    return out
