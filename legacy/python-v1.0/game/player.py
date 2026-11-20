"""Postać gracza: statystyki, ruch, strzelanie, zryw, umiejętności."""
import math
import random

import pygame

from . import gfx
from .audio import audio
from .config import RX0, RX1, RY0, RY1, RCX, RCY
from .entities import Bullet, BlackHole, Flask, Familiar, clamp

TAU = math.tau

STAT_DEFAULTS = dict(
    max_hp=100, damage=10.0, dmg_mult=1.0, fire_rate=3.0, fire_mult=1.0, shot_speed=520.0, range=450.0,
    speed=235.0, crit=0.05, crit_mult=2.0, armor=0.0, regen=0.0, lifesteal=0.0, luck=0.0, pickup=110.0,
    dodge=0.0, projectiles=1, pierce=0, bounce=0, homing=0.0, poison=0, burn=0.0, slow=0, chain=0.0,
    explode=0, split=0, spectral=0, backshot=0, shot_size=1.0, knockback=1.0, dash_charges=1, dash_cd=1.1,
    ability_cd_mult=1.0, ability_power=1.0, thorns=0, coin_mult=1.0, orbitals=0, familiars=0, revives=0,
    eshield=0, blackhole=0, price_mult=1.0, room_shield=0, rage=0, poison_burst=0, xp_mult=1.0,
)


class Player:
    def __init__(self, cdef):
        self.cdef = cdef
        self.key = cdef["key"]
        self.name = cdef["name"]
        self.color = cdef["color"]
        self.accent = cdef["accent"]
        for k, v in STAT_DEFAULTS.items():
            setattr(self, k, v)
        for k, v in cdef["stats"].items():
            setattr(self, k, v)
        self.ability_cd = cdef["ability_cd"]
        self.hp = float(self.max_hp)
        self.x, self.y = float(RCX), float(RCY)
        self.r = 17
        self.vx = self.vy = 0.0
        self.aim = 0.0
        self.fire_cd = 0.0
        self.iframes = 0.0
        self.dash_t = 0.0
        self.dash_left = self.dash_charges
        self.dash_recharge = 0.0
        self.dash_dx, self.dash_dy = 1.0, 0.0
        self.ability_t = 0.0
        self.rage_t = 0.0
        self.invis_t = 0.0
        self.level = 1
        self.xp = 0.0
        self.coins = 0
        self.items = []
        self.shield_up = False
        self.eshield_t = 0.0
        self.eshield_up = False
        self.bh_t = 6.0
        self.fams = []
        self.orb_angle = 0.0
        self.trail = []
        self.ghosts = []
        self.flash = 0.0
        self.moving = False
        self.t = 0.0
        self.ls_cd = 0.0
        self.dead = False
        self.decoy = None
        self.regen_acc = 0.0
        self.clamp_stats()

    # ------------------------------------------------------------------ statystyki
    def clamp_stats(self):
        self.armor = clamp(self.armor, -0.5, 0.7)
        self.dodge = clamp(self.dodge, 0.0, 0.6)
        self.crit = clamp(self.crit, 0.0, 1.0)
        self.max_hp = max(10, int(round(self.max_hp)))
        self.hp = min(self.hp, self.max_hp)
        self.speed = clamp(self.speed, 120, 520)
        self.fire_mult = max(0.2, self.fire_mult)
        self.dash_charges = int(self.dash_charges)
        self.projectiles = int(self.projectiles)
        self.range = clamp(self.range, 150, 1400)
        self.shot_speed = clamp(self.shot_speed, 200, 1400)
        self.ability_cd_mult = max(0.25, self.ability_cd_mult)
        self.dash_cd = max(0.3, self.dash_cd)

    def xp_needed(self):
        return 14 + self.level * 9

    def add_xp(self, v, g):
        self.xp += v * self.xp_mult
        while self.xp >= self.xp_needed():
            self.xp -= self.xp_needed()
            self.level += 1
            g.on_level_up()

    def eff_damage(self):
        d = self.damage * self.dmg_mult
        if self.rage:
            d *= 1 + 0.8 * (1 - self.hp / max(1, self.max_hp))
        if self.rage_t > 0:
            d *= 1.4
        return d

    def fire_interval(self):
        fr = self.fire_rate * self.fire_mult
        if self.rage_t > 0:
            fr *= 1.6
        return max(0.05, 1.0 / fr)

    def move_speed(self):
        s = self.speed
        if self.rage_t > 0:
            s *= 1.4
        if self.invis_t > 0:
            s *= 1.4
        return s

    def apply_item(self, item, g):
        self.items.append(item["key"])
        for attr, op, v in item["fx"]:
            cur = getattr(self, attr)
            if op == "+":
                setattr(self, attr, cur + v)
                if attr == "max_hp" and v > 0:
                    self.hp += v
            elif op == "*":
                setattr(self, attr, cur * v)
            elif op == "=":
                setattr(self, attr, v)
            if attr == "dash_charges":
                self.dash_left = min(self.dash_left + int(v), int(self.dash_charges + v))
        sp = item.get("special")
        if sp == "coins15":
            self.coins += 15
        elif sp == "chaos":
            g.give_random_items(2)
        self.clamp_stats()
        if self.max_hp < self.hp:
            self.hp = self.max_hp

    # ------------------------------------------------------------------ aktualizacja
    def update(self, dt, g, inp):
        self.t += dt
        self.iframes = max(0.0, self.iframes - dt)
        self.flash = max(0.0, self.flash - dt)
        self.ability_t = max(0.0, self.ability_t - dt)
        self.ls_cd = max(0.0, self.ls_cd - dt)
        if self.rage_t > 0:
            self.rage_t -= dt
            if random.random() < dt * 25:
                g.particles.burst(self.x + random.uniform(-14, 14), self.y + 10, (255, 70, 50), 1, 30, 4, 0.5, grav=-120)
        if self.invis_t > 0:
            self.invis_t -= dt
            if self.invis_t <= 0:
                self.decoy = None
        if self.regen > 0 and self.hp < self.max_hp:
            self.hp = min(self.max_hp, self.hp + self.regen * dt)

        # zryw
        if self.dash_left < self.dash_charges:
            self.dash_recharge += dt
            if self.dash_recharge >= self.dash_cd:
                self.dash_recharge = 0.0
                self.dash_left += 1
        else:
            self.dash_recharge = 0.0
        mx, my = inp.move
        if inp.dash and self.dash_left > 0 and self.dash_t <= 0:
            self.dash_left -= 1
            self.dash_t = 0.17
            if mx or my:
                self.dash_dx, self.dash_dy = mx, my
            else:
                self.dash_dx, self.dash_dy = math.cos(self.aim), math.sin(self.aim)
            audio.play("dash", 0.8)
            g.particles.burst(self.x, self.y + 10, (200, 200, 210), 10, 140, 5, 0.4, kind="smoke")

        if self.dash_t > 0:
            self.dash_t -= dt
            sp = max(720.0, self.move_speed() * 3.2)
            self.vx, self.vy = self.dash_dx * sp, self.dash_dy * sp
            self.ghosts.append([self.x, self.y, 0.25])
        else:
            tx, ty = mx * self.move_speed(), my * self.move_speed()
            k = min(1.0, dt * 14)
            self.vx += (tx - self.vx) * k
            self.vy += (ty - self.vy) * k
        self.moving = abs(self.vx) + abs(self.vy) > 30
        nx, ny = self.x + self.vx * dt, self.y + self.vy * dt
        if g.check_doors(nx, ny):
            return
        self.x, self.y = g.room.resolve_circle(nx, ny, self.r)
        if self.moving and random.random() < dt * 8 and self.dash_t <= 0:
            g.particles.burst(self.x, self.y + self.r - 2, (150, 140, 140), 1, 20, 3, 0.35, kind="smoke")

        for gh in self.ghosts:
            gh[2] -= dt
        self.ghosts = [gh for gh in self.ghosts if gh[2] > 0]

        self.trail.append((self.x, self.y))
        if len(self.trail) > 90:
            self.trail.pop(0)

        # celowanie i strzał
        self.aim = inp.aim
        self.fire_cd -= dt
        if inp.fire and self.fire_cd <= 0:
            self.shoot(g)
            self.fire_cd = self.fire_interval()
        if self.fire_cd < -1:
            self.fire_cd = 0

        if inp.ability and self.ability_t <= 0:
            self.use_ability(g, inp)
            self.ability_t = self.ability_cd * self.ability_cd_mult

        # tarcza energii
        if self.eshield > 0 and not self.eshield_up:
            self.eshield_t += dt
            if self.eshield_t >= 12.0 / (1 + 0.35 * (self.eshield - 1)):
                self.eshield_up = True
                self.eshield_t = 0
                audio.play("shield", 0.6)

        # czarna dziura
        if self.blackhole > 0 and g.enemies:
            self.bh_t -= dt
            if self.bh_t <= 0:
                self.bh_t = 8.0 / (1 + 0.3 * (self.blackhole - 1))
                e = random.choice(g.enemies)
                g.effects.append(BlackHole(e.x, e.y, max(4, self.eff_damage() * 0.35)))
                audio.play("ability", 0.5)

        # towarzysze
        while len(self.fams) < int(self.familiars):
            f = Familiar(len(self.fams))
            f.x, f.y = self.x, self.y
            self.fams.append(f)
        for i, f in enumerate(self.fams):
            idx = max(0, len(self.trail) - 1 - 14 * (i + 1))
            tx, ty = self.trail[idx] if self.trail else (self.x, self.y)
            f.update(dt, g, tx - 18, ty - 26)

        # orbity
        if self.orbitals > 0:
            self.orb_angle += dt * 3.0
            od = max(5.0, self.eff_damage() * 0.6)
            for (ox, oy) in self.orbital_positions():
                for e in g.enemies:
                    if e.orb_cd <= 0 and (e.x - ox) ** 2 + (e.y - oy) ** 2 < (e.r + 11) ** 2:
                        e.orb_cd = 0.3
                        g.damage_enemy(e, od, color=(140, 255, 255), small=True)
                for b in g.bullets:
                    if not b.friendly and b.alive and (b.x - ox) ** 2 + (b.y - oy) ** 2 < (b.r + 10) ** 2:
                        b.kill(g)

    def orbital_positions(self):
        n = int(self.orbitals)
        out = []
        for i in range(n):
            a = self.orb_angle + i * TAU / n
            rr = 66 + (i // 4) * 26
            out.append((self.x + math.cos(a) * rr, self.y + math.sin(a) * rr))
        return out

    # ------------------------------------------------------------------ strzelanie
    def make_bullet(self, g, a, dmg_mult=1.0, pierce_bonus=0, range_mult=1.0, speed_mult=1.0, fx=True, small=False):
        cdef = self.cdef
        sp = self.shot_speed * speed_mult
        crit = random.random() < self.crit or self.invis_t > 0
        dmg = self.eff_damage() * dmg_mult
        if crit:
            dmg *= self.crit_mult
        r = 6.5 * self.shot_size * (0.6 if small else 1.0)
        mx = self.x + math.cos(a) * (self.r + 10)
        my = self.y + math.sin(a) * (self.r + 10) - 4
        b = Bullet(mx, my, math.cos(a) * sp, math.sin(a) * sp, dmg, r, cdef["bullet_color"], True,
                   self.range * range_mult, cdef["bullet"], pierce=int(self.pierce) + pierce_bonus,
                   bounce=int(self.bounce), homing=self.homing, spectral=bool(self.spectral), crit=crit, fx=fx,
                   knock=self.knockback, source="player")
        return b

    def shoot(self, g):
        n = max(1, int(self.projectiles))
        spread = math.radians(min(11 * (n - 1), 70)) if n > 1 else 0.0
        jitter = 0.04 if self.fire_mult * self.fire_rate > 6 else 0.015
        for i in range(n):
            off = (-spread / 2 + spread * i / (n - 1)) if n > 1 else 0.0
            g.bullets.append(self.make_bullet(g, self.aim + off + random.uniform(-jitter, jitter)))
        for k in range(int(self.backshot)):
            g.bullets.append(self.make_bullet(g, self.aim + math.pi + (k - (self.backshot - 1) / 2) * 0.25))
        mx = self.x + math.cos(self.aim) * (self.r + 14)
        my = self.y + math.sin(self.aim) * (self.r + 14) - 4
        g.particles.spark(mx, my, self.cdef["bullet_color"], 3, 200, self.aim, 0.6, 0.12)
        audio.play(self.cdef["sound"], 0.55, 0.045)

    # ------------------------------------------------------------------ umiejętności
    def use_ability(self, g, inp):
        k = self.key
        pw = self.ability_power
        audio.play("ability", 0.8)
        if k == "rycerz":
            rad = 240 * (1 + 0.2 * (pw - 1))
            g.particles.ring(self.x, self.y, (190, 220, 255), rad, 0.4, width=8)
            g.particles.ring(self.x, self.y, (255, 255, 255), rad * 0.7, 0.3, width=4)
            g.shake(7)
            for e in list(g.enemies):
                dx, dy = e.x - self.x, e.y - self.y
                d = math.hypot(dx, dy) or 1
                if d < rad + e.r:
                    g.damage_enemy(e, self.eff_damage() * 3 * pw, kx=dx / d * 800, ky=dy / d * 800)
            for b in g.bullets:
                if not b.friendly and (b.x - self.x) ** 2 + (b.y - self.y) ** 2 < (rad * 1.1) ** 2:
                    b.kill(g)
        elif k == "lowczyni":
            n = int(20 * (1 + 0.5 * (pw - 1)))
            for i in range(n):
                a = i * TAU / n + self.aim
                b = self.make_bullet(g, a, dmg_mult=1.3 * pw, pierce_bonus=3, range_mult=1.3, speed_mult=1.1)
                g.bullets.append(b)
            g.particles.ring(self.x, self.y, (230, 255, 200), 70, 0.3)
        elif k == "berserker":
            self.rage_t = 5.0 * pw
            self.heal(self.max_hp * 0.1, g)
            g.particles.ring(self.x, self.y, (255, 70, 50), 120, 0.4, width=8)
            g.shake(5)
        elif k == "mag":
            jumps = 7 + int(2 * (pw - 1))
            g.chain_lightning(self.x, self.y, self.eff_damage() * 4 * pw, jumps, first_range=650, jump_range=340)
            g.particles.ring(self.x, self.y, (200, 160, 255), 80, 0.3)
        elif k == "alchemik":
            tx, ty = inp.target
            dx, dy = tx - self.x, ty - self.y
            d = math.hypot(dx, dy)
            if d > 400:
                tx, ty = self.x + dx / d * 400, self.y + dy / d * 400
            tx = clamp(tx, RX0 + 30, RX1 - 30)
            ty = clamp(ty, RY0 + 30, RY1 - 30)
            g.effects.append(Flask(self.x, self.y, tx, ty, self.eff_damage() * 2.4 * pw, 110 * (1 + 0.25 * (pw - 1)), 5.0))
        elif k == "zlodziej":
            self.invis_t = 2.5 * pw
            self.iframes = max(self.iframes, self.invis_t)
            self.decoy = (self.x, self.y)
            g.particles.burst(self.x, self.y, (120, 120, 140), 30, 220, 9, 0.9, kind="smoke")
        elif k == "upadly":
            cost = max(1.0, self.hp * 0.08)
            if self.hp - cost >= 1:
                self.hp -= cost
            rad = 260 * (1 + 0.2 * (pw - 1))
            g.particles.ring(self.x, self.y, (255, 50, 80), rad, 0.45, width=10)
            g.particles.burst(self.x, self.y, (255, 40, 70), 40, 500, 6, 0.6, glow=True)
            g.shake(10)
            for e in list(g.enemies):
                dx, dy = e.x - self.x, e.y - self.y
                d = math.hypot(dx, dy) or 1
                if d < rad + e.r:
                    g.damage_enemy(e, self.eff_damage() * 6 * pw, kx=dx / d * 500, ky=dy / d * 500)

    # ------------------------------------------------------------------ obrażenia / leczenie
    def hurt(self, dmg, g, src=None):
        if self.dead or self.iframes > 0 or self.dash_t > 0 or self.invis_t > 0 or g.god:
            return False
        if random.random() < self.dodge:
            g.particles.text(self.x, self.y - 30, "UNIK", (180, 230, 255), 18)
            self.iframes = 0.35
            audio.play("dodge")
            return False
        if self.shield_up or self.eshield_up:
            if self.shield_up:
                self.shield_up = False
            else:
                self.eshield_up = False
                self.eshield_t = 0
            self.iframes = 0.8
            audio.play("block")
            g.particles.ring(self.x, self.y, (120, 220, 255), 50, 0.3, width=5)
            g.particles.text(self.x, self.y - 30, "BLOK", (120, 220, 255), 18)
            return False
        dmg = max(1, int(round(dmg * (1 - self.armor))))
        self.hp -= dmg
        self.iframes = 1.0
        self.flash = 0.15
        g.shake(9)
        g.hurt_flash = 0.4
        g.hitstop = max(g.hitstop, 0.05)
        audio.play("player_hurt", 0.9)
        g.particles.text(self.x, self.y - 34, f"-{dmg}", (255, 80, 90), 22)
        g.particles.burst(self.x, self.y, (230, 60, 70), 12, 250, 5, 0.5)
        g.stats["damage_taken"] += dmg
        if self.hp <= 0:
            if self.revives > 0:
                self.revives -= 1
                self.hp = self.max_hp * 0.5
                self.iframes = 2.5
                audio.play("item")
                g.particles.ring(self.x, self.y, (255, 160, 60), 300, 0.6, width=10)
                g.particles.burst(self.x, self.y, (255, 170, 60), 50, 500, 6, 0.8, glow=True)
                g.banner("WSKRZESZENIE!", "Pióro Feniksa spłonęło", (255, 150, 60))
                for b in g.bullets:
                    if not b.friendly:
                        b.kill(g)
                if "feniks" in self.items:
                    self.items.remove("feniks")
            else:
                self.hp = 0
                self.dead = True
                g.on_player_death()
        return True

    def heal(self, v, g, show=True):
        if v <= 0:
            return
        before = self.hp
        self.hp = min(self.max_hp, self.hp + v)
        gained = self.hp - before
        if show and gained >= 1:
            g.particles.text(self.x, self.y - 36, f"+{int(gained)}", (120, 255, 130), 18)

    def on_hit(self, g):
        if self.lifesteal > 0 and self.ls_cd <= 0 and random.random() < self.lifesteal:
            self.ls_cd = 0.12
            self.heal(1 + self.max_hp * 0.012, g, show=False)
            g.particles.burst(self.x, self.y - 10, (120, 255, 130), 2, 60, 3, 0.4)

    # ------------------------------------------------------------------ rysowanie
    def draw(self, surf, g):
        for gx, gy, life in self.ghosts:
            a = life / 0.25
            gfx.blit_glow(surf, gx, gy, self.r * 1.6, gfx.mul_color(self.color, 0.35 * a))
        gfx.shadow(surf, self.x, self.y + self.r - 1, self.r * 2.1, self.r * 0.8)
        if self.rage_t > 0:
            gfx.blit_glow(surf, self.x, self.y, 50 + math.sin(self.t * 15) * 5, (150, 30, 20))
        blink = self.iframes > 0 and self.invis_t <= 0 and self.dash_t <= 0 and int(self.t * 18) % 2 == 0
        if not blink:
            alpha = 90 if self.invis_t > 0 else 255
            gfx.draw_character(surf, self.key, self.x, self.y, self.r, self.aim, self.t, self.moving,
                               self.flash > 0, self.color, self.accent, alpha)
        if self.shield_up or self.eshield_up:
            rr = self.r + 12 + math.sin(self.t * 5) * 2
            col = (150, 210, 255) if self.shield_up else (90, 230, 255)
            pygame.draw.circle(surf, col, (int(self.x), int(self.y - 2)), int(rr), 2)
        for (ox, oy) in self.orbital_positions():
            gfx.blit_glow(surf, ox, oy, 22, (40, 140, 140))
            gfx.draw_icon(surf, "gem", (110, 240, 240), ox, oy, 9)
        for f in self.fams:
            f.draw(surf, g)
