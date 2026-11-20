"""Stan jednej rozgrywki: piętra, pokoje, walka, nagrody, HUD."""
import math
import random

import pygame

from . import gfx
from .audio import audio
from .bosses import BOSSES
from .characters import CHAR_BY_KEY
from .config import (W, H, HUD_H, RX0, RY0, RX1, RY1, RCX, RCY, TILE, GCOLS, GROWS, FLOORS, NUM_FLOORS,
                     DIFFICULTIES, INK, WHITE, GOLD, RARITY_COLORS, DOOR_HALF, OPPOSITE,
                     HP_RED, XP_GREEN)
from .dungeon import Floor, door_rect, DOOR_POS
from .enemies import ENEMY_TYPES
from .entities import Bullet, Pickup, Pedestal, PoisonCloud
from .items import roll_item, item_price, ITEM_BY_KEY
from .particles import Particles
from .player import Player

TAU = math.tau


class Input:
    def __init__(self):
        self.move = (0.0, 0.0)
        self.aim = 0.0
        self.fire = False
        self.dash = False
        self.ability = False
        self.target = (RCX, RCY)


class SpawnMarker:
    def __init__(self, key, x, y, elite, delay):
        self.key, self.x, self.y, self.elite = key, x, y, elite
        self.t = self.dur = delay


class Run:
    def __init__(self, app, char_key, diff_idx, seed=None):
        self.app = app
        self.settings = app.data["settings"]
        self.cdef = CHAR_BY_KEY[char_key]
        self.char_key = char_key
        self.diff_idx = diff_idx
        self.diff = DIFFICULTIES[diff_idx]
        self.rng = random.Random(seed)
        self.player = Player(self.cdef)
        self.particles = Particles()
        self.bullets = []
        self.effects = []
        self.enemies = []
        self.spawns = []
        self.time = 0.0
        self.run_time = 0.0
        self.shake_amt = 0.0
        self.hitstop = 0.0
        self.hurt_flash = 0.0
        self.stats = dict(kills=0, damage_taken=0, damage_dealt=0, rooms=0, coins=0, bosses=0)
        self.taken = set()
        self.pending_levelups = 0
        self.banners = []
        self.state = "play"
        self.state_t = 0.0
        self.trans = None
        self.god = False
        self.wave_idx = 0
        self.boss = None
        self.flow = None
        self.flow_t = 0.0
        self.world = pygame.Surface((W, H)).convert()
        self.max_depth = 1
        self.trap_delay = 0.0
        self.won = False
        self.unlocked_now = []
        self.depth = 1
        self.floor = None
        self.room = None
        self.new_floor(1)

    # ================================================================== piętra / pokoje
    def new_floor(self, depth):
        self.depth = depth
        self.max_depth = max(self.max_depth, depth)
        self.floor = Floor(depth, self.diff, self.rng)
        p = self.player
        p.x, p.y = RCX, RCY
        self.enter_room(self.floor.start, None)
        self.banner(f"Piętro {depth}", FLOORS[depth - 1]["name"], FLOORS[depth - 1]["accent"])
        audio.play_music("explore")
        if depth >= 3:
            self.app.unlock("alchemik", self)
        self.app.record_floor(self.char_key, self.diff_idx, depth)

    def enter_room(self, room, came_through):
        p = self.player
        first = not room.visited
        self.room = room
        room.visited = True
        room.seen = True
        for n in room.doors.values():
            n.seen = True
        if room.bg is None:
            room.build_bg(self.depth, show_controls=(room is self.floor.start and self.depth == 1))
        self.bullets.clear()
        self.effects.clear()
        self.enemies = []
        self.spawns.clear()
        self.particles.clear()
        self.boss = None
        if came_through:
            ent = OPPOSITE[came_through]
            dx, dy = DOOR_POS[ent]
            if ent == "W":
                p.x, p.y = RX0 + 46, RCY
            elif ent == "E":
                p.x, p.y = RX1 - 46, RCY
            elif ent == "N":
                p.x, p.y = RCX, RY0 + 46
            else:
                p.x, p.y = RCX, RY1 - 46
            p.vx *= 0.3
            p.vy *= 0.3
        p.dash_t = 0
        p.trail = [(p.x, p.y)]
        for f in p.fams:
            f.x, f.y = p.x, p.y
        if first:
            self.stats["rooms"] += 1
        if not room.cleared:
            if p.room_shield:
                p.shield_up = True
            if room.kind == "combat":
                self.wave_idx = 0
                if room.waves:
                    self.spawn_wave(room.waves[0])
                audio.play("door", 0.8)
            elif room.kind == "boss":
                cls = BOSSES[self.depth]
                b = cls(self, RCX, RCY - 90)
                self.enemies.append(b)
                self.boss = b
                self.banner(b.name, b.title, (255, 80, 80), priority=True)
                audio.play("boss_roar")
                audio.play("door", 0.8)
                audio.play_music("boss")
        if first and not room.reward_given:
            room.reward_given = True
            self.populate_special(room)
        self.flow = room.flow_field(p.x, p.y)
        self.flow_t = 0.25

    def populate_special(self, room):
        p = self.player
        if room.kind == "treasure":
            it = self.roll_new_item(min_rarity=0)
            room.pedestals.append(Pedestal(RCX, RCY - 10, it, 0, "item"))
        elif room.kind == "shop":
            pm = self.diff["price"] * p.price_mult
            xs = [RCX - 240, RCX - 80, RCX + 80]
            for x in xs:
                it = self.roll_new_item()
                room.pedestals.append(Pedestal(x, RCY + 20, it, item_price(it, self.depth, pm), "shop"))
            room.pedestals.append(Pedestal(RCX + 240, RCY + 20, None, max(3, int(round(6 * pm))), "heart_shop",
                                           heal=0.35))
        elif room.kind == "shrine":
            cost = 20 if self.diff_idx < 3 else 25
            for x in (RCX - 120, RCX + 120):
                it = self.roll_new_item(min_rarity=2)
                room.pedestals.append(Pedestal(x, RCY + 10, it, 0, "shrine", group=1, hp_cost=cost))

    def roll_new_item(self, min_rarity=0):
        it = roll_item(self.rng, self.taken | set(self.player.items), self.player.luck, self.depth, min_rarity)
        self.taken.add(it["key"])
        return it

    def give_random_items(self, n):
        for i in range(n):
            it = self.roll_new_item()
            self.player.apply_item(it, self)
            self.banner(it["name"], it["desc"], RARITY_COLORS[it["rarity"]])
            self.app.data["stats"]["items"] += 1

    def spawn_wave(self, keys):
        p = self.player
        free = self.room.free_tiles()
        self.rng.shuffle(free)
        elite_ch = self.diff["elite"] + (0.015 * (self.depth - 1) if self.diff["elite"] > 0 else 0)
        used = set()
        idx = 0
        for k in keys:
            pos = None
            while idx < len(free):
                c, r = free[idx]
                idx += 1
                x = RX0 + c * TILE + TILE / 2
                y = RY0 + r * TILE + TILE / 2
                if math.hypot(x - p.x, y - p.y) < 260 or (c, r) in used:
                    continue
                if c <= 1 or c >= GCOLS - 2 or r <= 0 or r >= GROWS - 1:
                    if self.rng.random() < 0.7:
                        continue
                pos = (x, y)
                used.add((c, r))
                break
            if pos is None:
                pos = (self.rng.uniform(RX0 + 60, RX1 - 60), self.rng.uniform(RY0 + 60, RY1 - 60))
            elite = self.rng.random() < elite_ch
            self.spawns.append(SpawnMarker(k, pos[0], pos[1], elite, self.rng.uniform(0.75, 1.15)))
        audio.play("spawn", 0.5)

    def spawn_enemy(self, key, x, y, minion=False, elite=False):
        cls = ENEMY_TYPES.get(key)
        if cls is None:
            return None
        if len(self.enemies) > 45:
            return None
        x = min(max(x, RX0 + 20), RX1 - 20)
        y = min(max(y, RY0 + 20), RY1 - 20)
        e = cls(self, x, y, elite=elite, minion=minion)
        if not (e.flying or e.ghost):
            e.x, e.y = self.room.resolve_circle(e.x, e.y, e.r)
        self.enemies.append(e)
        self.particles.burst(e.x, e.y, (200, 200, 220), 8, 120, 4, 0.35, kind="smoke")
        return e

    def check_doors(self, nx, ny):
        if not self.room.cleared:
            return False
        p = self.player
        r = p.r
        for dname, nroom in self.room.doors.items():
            go = False
            if dname == "W" and nx - r < RX0 + 1 and abs(ny - RCY) < DOOR_HALF - 4 and p.vx < 0:
                go = True
            elif dname == "E" and nx + r > RX1 - 1 and abs(ny - RCY) < DOOR_HALF - 4 and p.vx > 0:
                go = True
            elif dname == "N" and ny - r < RY0 + 1 and abs(nx - RCX) < DOOR_HALF - 4 and p.vy < 0:
                go = True
            elif dname == "S" and ny + r > RY1 - 1 and abs(nx - RCX) < DOOR_HALF - 4 and p.vy > 0:
                go = True
            if go:
                self.start_transition(dname, nroom)
                return True
        return False

    def start_transition(self, dname, nroom):
        self.draw_world(self.world, with_player=True)
        old = self.world.copy()
        self.enter_room(nroom, dname)
        self.trans = dict(dir=dname, t=0.0, dur=0.3, old=old)
        self.state = "transition"
        p = self.player
        if p.coins < 0:
            p.coins = 0

    # ================================================================== zdarzenia
    def banner(self, title, sub, color, priority=False):
        b = dict(title=title, sub=sub, color=color, t=0.0, dur=2.4)
        if priority and self.banners:
            self.banners[0]["dur"] = min(self.banners[0]["dur"], self.banners[0]["t"] + 0.25)
            self.banners.insert(1, b)
        else:
            self.banners.append(b)

    def shake(self, a):
        if self.settings.get("shake", True):
            self.shake_amt = min(22.0, max(self.shake_amt, a))

    def on_level_up(self):
        self.pending_levelups += 1
        p = self.player
        audio.play("levelup", 0.8)
        self.particles.text(p.x, p.y - 50, "AWANS!", (140, 255, 140), 26, 1.2)
        self.particles.ring(p.x, p.y, (140, 255, 140), 70, 0.5)
        p.heal(p.max_hp * 0.05, self, show=False)

    def on_player_death(self):
        self.state = "dying"
        self.state_t = 0.0
        audio.stop_music(800)
        audio.play("gameover")
        p = self.player
        self.particles.burst(p.x, p.y, p.color, 40, 400, 7, 1.0)
        self.app.data["stats"]["deaths"] += 1

    def target_pos(self):
        p = self.player
        if p.invis_t > 0 and p.decoy:
            return p.decoy
        return p.x, p.y

    def nearest_enemy(self, x, y, maxd=9999, exclude=None):
        best = None
        bd = maxd * maxd
        for e in self.enemies:
            if not e.alive or e.invuln:
                continue
            if exclude and e.id in exclude:
                continue
            d = (e.x - x) ** 2 + (e.y - y) ** 2
            if d < bd:
                bd = d
                best = e
        return best

    def chase_dir(self, e):
        tx, ty = self.target_pos()
        dx, dy = tx - e.x, ty - e.y
        d = math.hypot(dx, dy) or 1
        if e.flying or e.ghost or self.flow is None or self.room.los(e.x, e.y, tx, ty):
            return dx / d, dy / d
        c = int((e.x - RX0) // TILE)
        r = int((e.y - RY0) // TILE)
        if not (0 <= c < GCOLS and 0 <= r < GROWS):
            return dx / d, dy / d
        best = self.flow[r][c]
        bc = None
        for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)):
            nc, nr = c + dc, r + dr
            if not (0 <= nc < GCOLS and 0 <= nr < GROWS):
                continue
            if dc and dr and (self.room.solid(c + dc, r) or self.room.solid(c, r + dr)):
                continue
            v = self.flow[nr][nc]
            if v < best:
                best = v
                bc = (nc, nr)
        if bc is None:
            return dx / d, dy / d
        tx2 = RX0 + bc[0] * TILE + TILE / 2
        ty2 = RY0 + bc[1] * TILE + TILE / 2
        dx2, dy2 = tx2 - e.x, ty2 - e.y
        d2 = math.hypot(dx2, dy2) or 1
        return dx2 / d2, dy2 / d2

    def random_free_pos(self, min_dist=200, fly=False, margin=50):
        p = self.player
        for i in range(60):
            x = self.rng.uniform(RX0 + margin, RX1 - margin)
            y = self.rng.uniform(RY0 + margin, RY1 - margin)
            if math.hypot(x - p.x, y - p.y) < min_dist:
                continue
            if not fly:
                ob = self.room.tile_at(x, y)
                if ob is not None:
                    continue
            return x, y
        return RCX, RCY

    def clear_enemy_bullets(self):
        for b in self.bullets:
            if not b.friendly:
                b.kill(self)

    # ================================================================== obrażenia
    def damage_enemy(self, e, dmg, crit=False, color=None, small=False, kx=0.0, ky=0.0, dot=False):
        if not e.alive or e.invuln or dmg <= 0:
            return
        e.hp -= dmg
        if e.flash_cd <= 0:
            e.flash = 0.04 if dot else 0.07
            e.flash_cd = 0.16 if e.boss else 0.11
        self.stats["damage_dealt"] += dmg
        if self.settings.get("dmgnums", True):
            if crit:
                self.particles.text(e.x + random.uniform(-8, 8), e.y - e.r - 6, f"{int(dmg)}!", (255, 220, 70), 24, 0.75)
            else:
                self.particles.text(e.x + random.uniform(-8, 8), e.y - e.r - 6, f"{max(1, int(round(dmg)))}",
                                    color or WHITE, 14 if small else 17, 0.6)
        if kx or ky:
            e.knock(kx, ky)
        if e.hp <= 0:
            self.kill_enemy(e)
        elif not dot:
            audio.play("crit" if crit else "hit", 0.45, 0.04)

    def kill_enemy(self, e, drops=True):
        if not e.alive and e not in self.enemies:
            return
        e.alive = False
        if e in self.enemies:
            self.enemies.remove(e)
        p = self.player
        self.particles.burst(e.x, e.y, e.color, 16 if not e.boss else 60, 280, 6, 0.6)
        self.particles.burst(e.x, e.y, WHITE, 4, 150, 3, 0.3)
        self.room.stain(e.x, e.y + e.r * 0.4, e.color, e.r * 0.9)
        audio.play("enemy_die", 0.6, 0.03)
        self.stats["kills"] += 1
        self.app.data["stats"]["kills"] += 1
        if drops and not e.minion and not e.boss:
            xpv = e.xp * (1 + 0.3 * (self.depth - 1)) * (3 if e.elite else 1)
            while xpv > 0:
                v = min(8.0, xpv) if xpv >= 8 else (min(3.0, xpv) if xpv >= 3 else xpv)
                self.room.pickups.append(Pickup("xp", e.x, e.y, v))
                xpv -= v
            ch = e.coin * p.coin_mult
            n = int(ch) + (1 if self.rng.random() < ch - int(ch) else 0)
            if e.elite:
                n += 3
            for i in range(n):
                self.room.pickups.append(Pickup("coin", e.x, e.y, 1))
            if self.rng.random() < 0.025 * self.diff["heal"]:
                self.room.pickups.append(Pickup("heart", e.x, e.y))
        if p.poison_burst and e.poison_t > 0 and not e.boss:
            self.effects.append(PoisonCloud(e.x, e.y, 70, p.eff_damage() * 0.9, 2.5))
        e.on_death(self)
        if e.boss:
            self.boss_defeated(e)

    def remove_enemy(self, e, drops=False):
        e.alive = False
        if e in self.enemies:
            self.enemies.remove(e)

    def boss_defeated(self, boss):
        self.stats["bosses"] += 1
        self.boss = None
        self.hitstop = 0.35
        self.shake(18)
        audio.play("explosion")
        audio.play("victory", 0.7)
        for i in range(4):
            self.particles.ring(boss.x, boss.y, (255, 220, 120), 120 + i * 80, 0.5 + i * 0.15, width=8)
        self.particles.burst(boss.x, boss.y, (255, 230, 150), 50, 600, 8, 1.2, glow=True)
        self.clear_enemy_bullets()
        for e in list(self.enemies):
            self.kill_enemy(e, drops=False)
        self.room.cleared = True
        if self.depth >= NUM_FLOORS:
            self.won = True
            self.state = "won"
            self.state_t = 0.0
            audio.stop_music(1500)
            self.app.on_win(self)
            return
        audio.play_music("explore")
        self.room.trapdoor = (RCX, RCY + 110)
        self.trap_delay = 1.6
        it = self.roll_new_item(min_rarity=1)
        self.room.pedestals.append(Pedestal(RCX, RCY - 90, it, 0, "item"))
        xpv = 18 + 14 * self.depth
        while xpv > 0:
            self.room.pickups.append(Pickup("xp", boss.x, boss.y, min(8, xpv)))
            xpv -= 8
        for i in range(2):
            self.room.pickups.append(Pickup("heart", boss.x, boss.y))
        for i in range(4 + self.depth):
            self.room.pickups.append(Pickup("coin", boss.x, boss.y))
        self.banner("Boss pokonany!", "Wejdź do włazu, aby zejść niżej", (255, 220, 120))

    def explode(self, x, y, r, dmg, hurt_player=False, hurt_enemies=True, color=(255, 160, 60), small=False):
        self.particles.burst(x, y, color, 10 if small else 26, r * 4, 6, 0.45, glow=True)
        self.particles.burst(x, y, (90, 80, 80), 6 if small else 14, r * 2, 10, 0.8, kind="smoke")
        self.particles.ring(x, y, (255, 220, 150), r, 0.3, width=6 if not small else 3)
        self.shake(3 if small else 9)
        audio.play("explosion", 0.35 if small else 0.8, 0.06)
        if not small:
            self.room.stain(x, y, (30, 25, 25), r * 0.6)
        if hurt_enemies:
            for e in list(self.enemies):
                d = math.hypot(e.x - x, e.y - y)
                if d < r + e.r:
                    k = 400 / max(1.0, d) if d > 0 else 0
                    self.damage_enemy(e, dmg, kx=(e.x - x) * k, ky=(e.y - y) * k)
        if hurt_player:
            p = self.player
            if math.hypot(p.x - x, p.y - y) < r + p.r * 0.5:
                p.hurt(dmg, self, (x, y))
        # przeszkody
        c0 = int((x - r - RX0) // TILE)
        c1 = int((x + r - RX0) // TILE)
        r0 = int((y - r - RY0) // TILE)
        r1 = int((y + r - RY0) // TILE)
        for row in range(max(0, r0), min(GROWS - 1, r1) + 1):
            for col in range(max(0, c0), min(GCOLS - 1, c1) + 1):
                ob = self.room.grid[row][col]
                if ob is None:
                    continue
                if math.hypot(ob.x - x, ob.y - y) < r + TILE * 0.3:
                    if ob.kind in ("crate", "barrel"):
                        self.damage_obstacle(ob, 999)
                    elif ob.kind == "rock" and not small:
                        self.room.remove_obstacle(ob)
                        self.particles.burst(ob.x, ob.y, (130, 125, 135), 12, 200, 6, 0.5, kind="square")

    def damage_obstacle(self, ob, dmg):
        if ob.kind not in ("crate", "barrel"):
            return
        ob.hp -= dmg
        ob.flash = 0.08
        if ob.hp <= 0 and self.room.grid[ob.row][ob.col] is ob:
            self.room.remove_obstacle(ob)
            audio.play("break", 0.7)
            if ob.kind == "crate":
                self.particles.burst(ob.x, ob.y, (165, 112, 62), 14, 220, 6, 0.6, kind="square")
                r = self.rng.random()
                if r < 0.35:
                    for i in range(self.rng.randint(1, 2)):
                        self.room.pickups.append(Pickup("coin", ob.x, ob.y))
                elif r < 0.35 + 0.08 * self.diff["heal"]:
                    self.room.pickups.append(Pickup("heart", ob.x, ob.y))
            else:
                self.explode(ob.x, ob.y, 95, 24 * (0.5 + 0.5 * self.diff["enemy_dmg"]), hurt_player=True,
                             hurt_enemies=True)

    def chain_lightning(self, x, y, dmg, jumps, first_range=300, jump_range=220, exclude=None):
        hit = set(exclude or ())
        cx, cy = x, y
        any_hit = False
        for i in range(jumps):
            t = self.nearest_enemy(cx, cy, first_range if i == 0 else jump_range, exclude=hit)
            if t is None:
                break
            self.particles.bolt(cx, cy, t.x, t.y)
            hit.add(t.id)
            self.damage_enemy(t, dmg, color=(170, 220, 255))
            cx, cy = t.x, t.y
            any_hit = True
        if any_hit:
            audio.play("lightning", 0.6, 0.05)

    def bullet_hit_enemy(self, b, e):
        p = self.player
        b.hit.add(e.id)
        sp = b.speed() or 1
        kb = 110 * b.knock
        self.damage_enemy(e, b.dmg, crit=b.crit, kx=b.vx / sp * kb, ky=b.vy / sp * kb)
        self.particles.spark(b.x, b.y, b.color, 4, 220, math.atan2(-b.vy, -b.vx), 1.4, 0.2)
        if b.fx:
            if p.poison:
                e.poison_t = 3.0
                e.poison_dps = max(e.poison_dps, p.eff_damage() * 0.45 * p.poison)
            if p.burn and self.rng.random() < p.burn:
                e.burn_t = 3.0
                e.burn_dps = max(e.burn_dps, p.eff_damage() * 0.7)
            if p.slow:
                e.slow_t = 1.5 if not e.boss else 0.5
            if p.chain and self.rng.random() < p.chain:
                self.chain_lightning(e.x, e.y, b.dmg * 0.6, 3 + int(p.chain * 4), 240, 220, exclude={e.id})
            if p.explode:
                self.explode(b.x, b.y, 48 + 14 * (p.explode - 1), b.dmg * 0.55, small=True)
            if p.split and not b.is_split:
                n = 2 + int(p.split)
                base = math.atan2(b.vy, b.vx)
                for i in range(n):
                    a = base + (i - (n - 1) / 2) * 0.6
                    s = Bullet(b.x, b.y, math.cos(a) * sp * 0.8, math.sin(a) * sp * 0.8, b.dmg * 0.4,
                               max(3, b.r * 0.6), b.color, True, 230, b.style, spectral=b.spectral,
                               knock=0.5, source="split")
                    s.is_split = True
                    s.hit = set(b.hit)
                    self.bullets.append(s)
            p.on_hit(self)
        if b.pierce > 0:
            b.pierce -= 1
        else:
            b.kill(self, pop=False)

    # ================================================================== znajdźki i piedestały
    def collect_pickup(self, pk):
        p = self.player
        if pk.kind == "coin":
            p.coins += pk.value
            self.stats["coins"] += pk.value
            audio.play("coin", 0.5, 0.04)
            return True
        if pk.kind == "xp":
            p.add_xp(pk.value, self)
            audio.play("xp", 0.4, 0.03)
            return True
        if pk.kind == "heart":
            if p.hp >= p.max_hp:
                return False
            p.heal((18 + p.max_hp * 0.06) * (0.7 + 0.3 * self.diff["heal"]), self)
            audio.play("heal", 0.7)
            self.particles.burst(p.x, p.y, (255, 100, 120), 10, 160, 4, 0.5)
            return True
        if pk.kind == "chest":
            audio.play("coin")
            audio.play("break", 0.5)
            self.particles.burst(pk.x, pk.y, GOLD, 18, 250, 5, 0.6, glow=True)
            for i in range(self.rng.randint(2, 4)):
                self.room.pickups.append(Pickup("coin", pk.x, pk.y))
            if self.rng.random() < 0.3 * self.diff["heal"]:
                self.room.pickups.append(Pickup("heart", pk.x, pk.y))
            if self.rng.random() < 0.12 + p.luck * 0.004:
                it = self.roll_new_item()
                self.room.pedestals.append(Pedestal(int(pk.x), int(pk.y), it, 0, "item"))
            return True
        return False

    def update_pedestals(self, dt):
        p = self.player
        for ped in self.room.pedestals:
            if not ped.alive:
                continue
            ped.cool = max(0.0, ped.cool - dt)
            if math.hypot(p.x - ped.x, p.y - (ped.y - 4)) > 36:
                continue
            if ped.kind == "item":
                self.take_item(ped)
            elif ped.kind == "shop":
                if p.coins >= ped.price:
                    p.coins -= ped.price
                    audio.play("buy")
                    self.take_item(ped)
                elif ped.cool <= 0:
                    ped.cool = 1.2
                    audio.play("deny")
                    self.particles.text(ped.x, ped.y - 50, "Za mało monet!", (255, 120, 120), 18)
            elif ped.kind == "heart_shop":
                if p.hp >= p.max_hp:
                    if ped.cool <= 0:
                        ped.cool = 1.2
                        self.particles.text(ped.x, ped.y - 50, "Pełne zdrowie", (200, 255, 200), 18)
                elif p.coins >= ped.price:
                    p.coins -= ped.price
                    p.heal(p.max_hp * ped.heal, self)
                    audio.play("heal")
                    ped.alive = False
                    self.particles.burst(ped.x, ped.y - 18, (255, 100, 120), 16, 200, 5, 0.5)
                elif ped.cool <= 0:
                    ped.cool = 1.2
                    audio.play("deny")
                    self.particles.text(ped.x, ped.y - 50, "Za mało monet!", (255, 120, 120), 18)
            elif ped.kind == "shrine":
                if p.max_hp - ped.hp_cost < 10:
                    if ped.cool <= 0:
                        ped.cool = 1.2
                        audio.play("deny")
                        self.particles.text(ped.x, ped.y - 50, "Za mało życia!", (255, 120, 120), 18)
                    continue
                p.max_hp -= ped.hp_cost
                p.hp = min(p.hp, p.max_hp)
                self.particles.burst(p.x, p.y, (200, 20, 40), 30, 300, 6, 0.7)
                self.take_item(ped)
                for other in self.room.pedestals:
                    if other is not ped and other.group == ped.group and other.alive:
                        other.alive = False
                        self.particles.burst(other.x, other.y - 18, (120, 30, 40), 20, 200, 6, 0.6, kind="smoke")
        self.room.pedestals = [pd for pd in self.room.pedestals if pd.alive]

    def take_item(self, ped):
        it = ped.item
        ped.alive = False
        p = self.player
        p.apply_item(it, self)
        audio.play("item", 0.8)
        self.particles.burst(ped.x, ped.y - 18, RARITY_COLORS[it["rarity"]], 30, 300, 6, 0.8, glow=True)
        self.particles.ring(p.x, p.y, RARITY_COLORS[it["rarity"]], 90, 0.5)
        self.banner(it["name"], it["desc"], RARITY_COLORS[it["rarity"]])
        self.app.data["stats"]["items"] += 1

    # ================================================================== pętla
    def update(self, dt, inp):
        self.time += dt
        self.shake_amt = max(0.0, self.shake_amt - dt * 30)
        self.hurt_flash = max(0.0, self.hurt_flash - dt)
        for b in self.banners[:1]:
            b["t"] += dt
            if len(self.banners) > 1 and b["t"] > 1.1:
                b["dur"] = min(b["dur"], b["t"] + 0.35)
        if self.banners and self.banners[0]["t"] > self.banners[0]["dur"]:
            self.banners.pop(0)
        if self.state == "transition":
            self.trans["t"] += dt
            if self.trans["t"] >= self.trans["dur"]:
                self.trans = None
                self.state = "play"
            return
        if self.state == "descend":
            self.state_t += dt
            if self.state_t >= 0.6 and not getattr(self, "_descended", False):
                self._descended = True
                self.new_floor(self.depth + 1)
            if self.state_t >= 1.2:
                self.state = "play"
                self._descended = False
            return
        if self.state == "dying":
            self.state_t += dt
            dt *= 0.25
            self.particles.update(dt)
            for e in self.enemies:
                e.t += dt
            return
        if self.state == "won":
            self.state_t += dt
            self.particles.update(dt)
            if random.random() < dt * 8:
                x, y = random.uniform(RX0, RX1), random.uniform(RY0, RY1)
                self.particles.burst(x, y, random.choice([(255, 220, 90), (255, 120, 200), (120, 220, 255)]), 20, 300, 5, 1.0, glow=True)
            return
        if self.hitstop > 0:
            self.hitstop -= dt
            return
        self.run_time += dt
        p = self.player

        p.update(dt, self, inp)
        if self.state != "play":
            return

        self.flow_t -= dt
        if self.flow_t <= 0:
            self.flow_t = 0.25
            tx, ty = self.target_pos()
            self.flow = self.room.flow_field(tx, ty)

        for e in list(self.enemies):
            if e.alive:
                e.update(dt, self)

        # znaczniki pojawiania się
        for s in self.spawns[:]:
            s.t -= dt
            if s.t <= 0:
                self.spawns.remove(s)
                e = self.spawn_enemy(s.key, s.x, s.y, elite=s.elite)
                if e and s.elite:
                    self.particles.ring(e.x, e.y, (255, 210, 70), 50, 0.4)

        for b in self.bullets:
            if b.alive:
                b.update(dt, self)

        # kolizje pocisków
        pr = p.r * 0.6
        for b in self.bullets:
            if not b.alive:
                continue
            if b.friendly:
                for e in self.enemies:
                    if not e.alive or e.invuln or e.id in b.hit:
                        continue
                    rr = e.r + b.r
                    if (e.x - b.x) ** 2 + (e.y - b.y) ** 2 < rr * rr:
                        self.bullet_hit_enemy(b, e)
                        if not b.alive:
                            break
            else:
                rr = pr + b.r
                if (p.x - b.x) ** 2 + (p.y - b.y) ** 2 < rr * rr:
                    if p.hurt(b.dmg, self, (b.x, b.y)):
                        b.kill(self)
        self.bullets = [b for b in self.bullets if b.alive]
        if len(self.bullets) > 700:
            self.bullets = self.bullets[-700:]

        # kontakt z wrogami
        for e in list(self.enemies):
            if not e.alive or e.contact_dmg <= 0:
                continue
            if e.boss and not getattr(e, "contact_on", True):
                continue
            if e.alpha < 120:
                continue
            rr = e.r + p.r * 0.7
            if (e.x - p.x) ** 2 + (e.y - p.y) ** 2 < rr * rr:
                hurt = p.hurt(e.contact_dmg, self, (e.x, e.y))
                if p.thorns and e.thorn_cd <= 0:
                    e.thorn_cd = 0.5
                    self.damage_enemy(e, p.thorns * (1 + 0.15 * (self.depth - 1)), color=(200, 200, 220))
                if hurt and not e.boss:
                    d = math.hypot(e.x - p.x, e.y - p.y) or 1
                    e.knock((e.x - p.x) / d * 300, (e.y - p.y) / d * 300)

        # rozpychanie wrogów
        en = self.enemies
        for i in range(len(en)):
            a = en[i]
            if a.boss:
                continue
            for j in range(i + 1, len(en)):
                b2 = en[j]
                if b2.boss:
                    continue
                dx, dy = b2.x - a.x, b2.y - a.y
                rr = a.r + b2.r
                d2 = dx * dx + dy * dy
                if 0 < d2 < rr * rr:
                    d = math.sqrt(d2)
                    push = (rr - d) * 0.5
                    nx, ny = dx / d, dy / d
                    if a.knock_resist > 0:
                        a.x -= nx * push
                        a.y -= ny * push
                    if b2.knock_resist > 0:
                        b2.x += nx * push
                        b2.y += ny * push

        for pk in self.room.pickups:
            if pk.alive:
                pk.update(dt, self)
        self.room.pickups = [pk for pk in self.room.pickups if pk.alive]
        self.update_pedestals(dt)
        for ob in self.room.obstacles():
            if ob.flash > 0:
                ob.flash -= dt

        for ef in self.effects:
            if ef.alive:
                ef.update(dt, self)
        self.effects = [ef for ef in self.effects if ef.alive]
        self.particles.update(dt)

        # właz
        if self.trap_delay > 0:
            self.trap_delay -= dt
        if self.room.trapdoor and self.trap_delay <= 0:
            tx, ty = self.room.trapdoor
            if math.hypot(p.x - tx, p.y - ty) < 26:
                self.state = "descend"
                self.state_t = 0.0
                audio.play("trapdoor")

        # czyszczenie pokoju
        room = self.room
        if not room.cleared and not self.spawns:
            if self.wave_idx < len(room.waves) - 1 and len(self.enemies) <= 1 and room.kind == "combat":
                self.wave_idx += 1
                self.spawn_wave(room.waves[self.wave_idx])
            elif not self.enemies and room.kind == "combat":
                self.room_cleared()

    def room_cleared(self):
        room = self.room
        room.cleared = True
        audio.play("door_open", 0.8)
        p = self.player
        r = self.rng.random()
        heal_ch = 0.16 * self.diff["heal"]
        if r < 0.22:
            for i in range(self.rng.randint(1, 3)):
                room.pickups.append(Pickup("coin", RCX, RCY))
        elif r < 0.22 + heal_ch:
            room.pickups.append(Pickup("heart", RCX, RCY))
        elif r < 0.22 + heal_ch + 0.07 + p.luck * 0.002:
            room.pickups.append(Pickup("chest", RCX, RCY, pop=False))
        self.particles.ring(p.x, p.y, (255, 255, 255), 40, 0.3)

    # ================================================================== rysowanie
    def draw_world(self, surf, with_player=True):
        room = self.room
        surf.blit(room.bg, (0, 0))
        theme = FLOORS[self.depth - 1]
        self.draw_doors(surf)
        if room.trapdoor:
            tx, ty = room.trapdoor
            gfx.blit_glow(surf, tx, ty, 60, (60, 40, 20))
            pygame.draw.ellipse(surf, INK, (tx - 34, ty - 22, 68, 44))
            pygame.draw.ellipse(surf, (10, 6, 12), (tx - 30, ty - 18, 60, 36))
            for i in range(3):
                pygame.draw.line(surf, (120, 90, 60), (tx - 14, ty - 10 + i * 10), (tx + 14, ty - 10 + i * 10), 3)
            if self.trap_delay > 0:
                for i in range(5):
                    xx = tx - 24 + i * 12
                    pygame.draw.line(surf, (150, 140, 150), (xx, ty - 16), (xx, ty + 16), 3)
            pygame.draw.ellipse(surf, (140, 110, 80), (tx - 34, ty - 22, 68, 44), 3)
        for ef in self.effects:
            if ef.layer == "floor":
                ef.draw(surf, self)
        for s in self.spawns:
            f = 1 - s.t / s.dur
            col = (255, 210, 70) if s.elite else (255, 70, 70)
            rr = 22 + math.sin(self.time * 18) * 3
            pygame.draw.circle(surf, col, (int(s.x), int(s.y)), int(rr * (0.5 + 0.5 * f)), 2)
            d = 10 * f + 4
            pygame.draw.line(surf, col, (s.x - d, s.y - d), (s.x + d, s.y + d), 3)
            pygame.draw.line(surf, col, (s.x - d, s.y + d), (s.x + d, s.y - d), 3)
        for ob in room.obstacles():
            ob.draw(surf, theme["accent"], theme["wall"])
        if room.kind == "shop":
            self.draw_shopkeeper(surf)
        if room.kind == "shrine":
            self.draw_altar(surf)
        for ped in room.pedestals:
            ped.draw(surf, self.time)
        for pk in room.pickups:
            pk.draw(surf)
        actors = list(self.enemies)
        actors.sort(key=lambda a: a.y)
        p = self.player
        drawn_p = not with_player or p.dead
        for e in actors:
            if not drawn_p and e.y > p.y:
                p.draw(surf, self)
                drawn_p = True
            e.draw(surf, self)
        if not drawn_p:
            p.draw(surf, self)
        for b in self.bullets:
            if b.friendly:
                b.draw(surf)
        for b in self.bullets:
            if not b.friendly:
                b.draw(surf)
        for ef in self.effects:
            if ef.layer == "top":
                ef.draw(surf, self)
        self.particles.draw(surf)
        self.particles.draw_texts(surf)
        # podpowiedzi przy piedestałach
        for ped in room.pedestals:
            if math.hypot(p.x - ped.x, p.y - ped.y) < 110 and ped.item:
                it = ped.item
                gfx.draw_text(surf, it["name"], ped.x, ped.y - 62, 16, RARITY_COLORS[it["rarity"]], "center")
                gfx.draw_text(surf, it["desc"], ped.x, ped.y - 44, 13, (225, 220, 230), "center", heavy=False)
        if room.trapdoor and math.hypot(p.x - room.trapdoor[0], p.y - room.trapdoor[1]) < 140:
            gfx.draw_text(surf, "Zejdź niżej", room.trapdoor[0], room.trapdoor[1] + 36, 15, (230, 220, 200), "center")

    def draw_doors(self, surf):
        room = self.room
        for dname, nroom in room.doors.items():
            r = door_rect(dname)
            k = nroom.kind
            frame = (95, 85, 100)
            if k == "boss" or room.kind == "boss":
                frame = (150, 40, 50)
            elif k == "treasure":
                frame = (200, 160, 60)
            elif k == "shop":
                frame = (90, 150, 90)
            elif k == "shrine":
                frame = (120, 30, 60)
            fr = r.inflate(22, 0) if dname in ("N", "S") else r.inflate(0, 22)
            pygame.draw.rect(surf, INK, fr.inflate(6, 6), border_radius=8)
            pygame.draw.rect(surf, frame, fr, border_radius=7)
            pygame.draw.rect(surf, gfx.lerp_color(frame, WHITE, 0.25), fr, 3, border_radius=7)
            pygame.draw.rect(surf, (12, 8, 16), r, border_radius=4)
            if not room.cleared:
                bc = (130, 110, 90)
                if dname in ("N", "S"):
                    for i in range(5):
                        x = r.x + 10 + i * (r.w - 20) / 4
                        pygame.draw.line(surf, INK, (x, r.y), (x, r.bottom), 7)
                        pygame.draw.line(surf, bc, (x, r.y), (x, r.bottom), 4)
                else:
                    for i in range(5):
                        y = r.y + 10 + i * (r.h - 20) / 4
                        pygame.draw.line(surf, INK, (r.x, y), (r.right, y), 7)
                        pygame.draw.line(surf, bc, (r.x, y), (r.right, y), 4)
            cx, cy = r.center
            ix, iy = cx, cy
            if dname == "N":
                iy = r.y - 6
            elif dname == "S":
                iy = r.bottom + 6
            elif dname == "W":
                ix = r.x - 4
                iy = r.y - 14
            else:
                ix = r.right + 4
                iy = r.y - 14
            if k == "boss":
                gfx.draw_icon(surf, "skull", (235, 230, 215), ix, iy, 10)
            elif k == "treasure":
                gfx.draw_icon(surf, "star", (255, 210, 70), ix, iy, 10)
            elif k == "shop":
                gfx.draw_icon(surf, "coin", GOLD, ix, iy, 10)
            elif k == "shrine":
                gfx.draw_icon(surf, "drop", (220, 40, 60), ix, iy, 9)

    def draw_shopkeeper(self, surf):
        x, y = RCX, RCY - 120
        gfx.shadow(surf, x, y + 26, 60, 16)
        gfx.poly(surf, (70, 50, 90), [(x - 30, y + 28), (x - 18, y - 22), (x + 18, y - 22), (x + 30, y + 28)], 3)
        gfx.outlined_circle(surf, (40, 30, 50), (x, y - 20), 18, 3)
        for side in (-1, 1):
            gfx.blit_glow(surf, x + side * 7, y - 20, 10, (255, 200, 80))
            pygame.draw.circle(surf, (255, 220, 120), (int(x + side * 7), int(y - 20)), 3)
        gfx.draw_text(surf, "SKLEP", x, y - 58, 20, GOLD, "center")

    def draw_altar(self, surf):
        x, y = RCX, RCY - 120
        r = pygame.Rect(0, 0, 120, 40)
        r.center = (x, y)
        pygame.draw.rect(surf, INK, r.inflate(6, 6), border_radius=6)
        pygame.draw.rect(surf, (90, 30, 40), r, border_radius=5)
        for side in (-1, 1):
            cx = x + side * 48
            pygame.draw.rect(surf, (230, 220, 200), (cx - 4, y - 36, 8, 18))
            gfx.blit_glow(surf, cx, y - 42, 16 + math.sin(self.time * 9 + side) * 3, (200, 120, 30))
            pygame.draw.circle(surf, (255, 200, 80), (cx, y - 42), 4)
        gfx.draw_text(surf, "OŁTARZ KRWI", x, y - 76, 20, (230, 60, 80), "center")
        gfx.draw_text(surf, "Wybierz jeden dar – zapłać zdrowiem", x, y + 34, 14, (220, 180, 190), "center", heavy=False)

    def draw(self, screen):
        if self.state == "transition" and self.trans:
            t = self.trans
            f = min(1.0, t["t"] / t["dur"])
            f = 1 - (1 - f) ** 3
            self.draw_world(self.world)
            d = t["dir"]
            dx, dy = {"E": (-1, 0), "W": (1, 0), "N": (0, 1), "S": (0, -1)}[d]
            ox, oy = int(dx * W * f), int(dy * (H - HUD_H) * f)
            screen.fill((0, 0, 0))
            screen.blit(t["old"], (ox, oy))
            screen.blit(self.world, (ox - dx * W, oy - dy * (H - HUD_H)))
        else:
            self.draw_world(self.world)
            sx = sy = 0
            if self.shake_amt > 0:
                a = self.shake_amt
                sx = int(random.uniform(-a, a) * 0.6)
                sy = int(random.uniform(-a, a) * 0.6)
            if sx or sy:
                screen.fill((0, 0, 0))
            screen.blit(self.world, (sx, sy))
        # winieta i błysk obrażeń
        screen.blit(gfx.vignette(W, H), (0, 0))
        p = self.player
        low = p.hp / p.max_hp < 0.3 and not p.dead
        if self.hurt_flash > 0 or low:
            v = gfx.vignette(W, H, (200, 0, 20), 1.0)
            a = self.hurt_flash / 0.4 * 200
            if low:
                a = max(a, 70 + 50 * math.sin(self.time * 5))
            v.set_alpha(int(max(0, min(255, a))))
            screen.blit(v, (0, 0))
        self.draw_minimap(screen)
        self.draw_hud(screen)
        self.draw_banner(screen)
        if self.state == "descend":
            f = self.state_t / 0.6 if self.state_t < 0.6 else max(0.0, 1 - (self.state_t - 0.6) / 0.6)
            s = pygame.Surface((W, H))
            s.fill((0, 0, 0))
            s.set_alpha(int(255 * min(1.0, f)))
            screen.blit(s, (0, 0))

    def draw_banner(self, surf):
        if not self.banners:
            return
        b = self.banners[0]
        t, dur = b["t"], b["dur"]
        a = min(1.0, t / 0.25, (dur - t) / 0.4)
        if a <= 0:
            return
        y = HUD_H + 54 - (1 - min(1.0, t / 0.25)) * 30
        w = max(gfx.font(34).size(b["title"])[0], gfx.font(17, False).size(b["sub"])[0]) + 80
        s = pygame.Surface((w, 92), pygame.SRCALPHA)
        pygame.draw.rect(s, (14, 10, 20, 200), s.get_rect(), border_radius=14)
        pygame.draw.rect(s, (*b["color"], 255), s.get_rect(), 3, border_radius=14)
        s.set_alpha(int(255 * a))
        surf.blit(s, (W // 2 - w // 2, y))
        gfx.draw_text(surf, b["title"], W // 2, y + 10, 34, b["color"], "midtop", outline=3, alpha=255 * a)
        gfx.draw_text(surf, b["sub"], W // 2, y + 58, 17, (230, 225, 235), "midtop", heavy=False, alpha=255 * a)

    def draw_minimap(self, surf):
        rooms = [r for r in self.floor.rooms.values() if r.seen]
        if not rooms:
            return
        cw, ch, gap = 24, 16, 4
        minx = min(r.gx for r in rooms)
        maxx = max(r.gx for r in rooms)
        miny = min(r.gy for r in rooms)
        maxy = max(r.gy for r in rooms)
        mw = (maxx - minx + 1) * (cw + gap) + gap
        mh = (maxy - miny + 1) * (ch + gap) + gap
        x0 = RX1 - mw - 10
        y0 = RY0 + 10
        bg = pygame.Surface((mw + 8, mh + 8), pygame.SRCALPHA)
        pygame.draw.rect(bg, (10, 8, 16, 150), bg.get_rect(), border_radius=8)
        surf.blit(bg, (x0 - 4, y0 - 4))
        for r in rooms:
            x = x0 + gap + (r.gx - minx) * (cw + gap)
            y = y0 + gap + (r.gy - miny) * (ch + gap)
            if r is self.room:
                col = (245, 240, 235)
            elif r.visited:
                col = (130, 125, 145)
            else:
                col = (60, 55, 70)
            pygame.draw.rect(surf, col, (x, y, cw, ch), border_radius=3)
            if r.visited or r.kind != "combat":
                ic = {"boss": ("skull", (235, 230, 215)), "treasure": ("star", (255, 210, 70)),
                      "shop": ("coin", GOLD), "shrine": ("drop", (230, 50, 70))}.get(r.kind)
                if ic:
                    gfx.draw_icon(surf, ic[0], ic[1], x + cw / 2, y + ch / 2, 6)

    def draw_hud(self, surf):
        p = self.player
        pygame.draw.rect(surf, (20, 16, 28), (0, 0, W, HUD_H))
        pygame.draw.line(surf, INK, (0, HUD_H - 1), (W, HUD_H - 1), 3)
        # HP
        gfx.bar(surf, (16, 9, 250, 24), p.hp / p.max_hp, HP_RED)
        gfx.draw_text(surf, f"{int(math.ceil(p.hp))} / {p.max_hp}", 141, 21, 15, WHITE, "center")
        # XP
        gfx.bar(surf, (16, 39, 250, 14), p.xp / p.xp_needed(), XP_GREEN, radius=5)
        gfx.draw_text(surf, f"Poz. {p.level}", 276, 37, 15, (190, 255, 190), "topleft")
        # monety
        gfx.draw_icon(surf, "coin", GOLD, 288, 20, 9)
        gfx.draw_text(surf, str(p.coins), 302, 10, 20, GOLD)
        # zryw
        for i in range(p.dash_charges):
            cx = 360 + i * 16
            full = i < p.dash_left
            pygame.draw.circle(surf, INK, (cx, 21), 7)
            if full:
                pygame.draw.circle(surf, (170, 220, 255), (cx, 21), 5)
            elif i == p.dash_left:
                f = p.dash_recharge / p.dash_cd
                pygame.draw.circle(surf, (60, 80, 100), (cx, 21), 5)
                pygame.draw.circle(surf, (120, 160, 200), (cx, 21), max(1, int(5 * f)))
        # umiejętność
        ar = pygame.Rect(420, 8, 48, 48)
        pygame.draw.rect(surf, INK, ar.inflate(6, 6), border_radius=10)
        pygame.draw.rect(surf, (50, 42, 64), ar, border_radius=8)
        gfx.draw_character(surf, self.char_key, ar.centerx, ar.centery + 4, 13, 0.3, 0, False, False, p.color, p.accent)
        if p.ability_t > 0:
            f = p.ability_t / (p.ability_cd * p.ability_cd_mult)
            sh = pygame.Surface(ar.size, pygame.SRCALPHA)
            pygame.draw.rect(sh, (0, 0, 0, 170), (0, 0, ar.w, int(ar.h * f)), border_radius=8)
            surf.blit(sh, ar.topleft)
            gfx.draw_text(surf, f"{p.ability_t:.0f}", ar.centerx, ar.centery, 18, WHITE, "center")
        else:
            pygame.draw.rect(surf, (255, 220, 100), ar, 2, border_radius=8)
        gfx.draw_text(surf, "Q", ar.right - 2, ar.bottom + 2, 12, (220, 220, 230), "bottomright")
        # środek
        th = FLOORS[self.depth - 1]
        gfx.draw_text(surf, f"Piętro {self.depth} · {th['name']}", W // 2, 8, 20, th["accent"], "midtop")
        m, s = divmod(int(self.run_time), 60)
        gfx.draw_text(surf, f"{self.diff['name']}  ·  {m:02d}:{s:02d}", W // 2, 36, 14, self.diff["color"], "midtop", heavy=False)
        if self.settings.get("autoaim"):
            gfx.draw_text(surf, "AUTO-CEL (F)", 482, 50, 11, (150, 220, 255), "midleft")
        # przedmioty
        x0 = 812
        for i, k in enumerate(p.items[-32:]):
            it = ITEM_BY_KEY.get(k)
            if not it:
                continue
            cx = x0 + (i % 16) * 28
            cy = 18 + (i // 18) * 28
            gfx.draw_icon(surf, it["icon"], it["color"], cx, cy, 9)
        # pasek bossa
        if self.boss and self.boss.alive:
            b = self.boss
            r = pygame.Rect(W // 2 - 300, H - 30, 600, 18)
            gfx.bar(surf, r, b.hp / b.max_hp, (210, 40, 70), radius=6)
            gfx.draw_text(surf, b.name, W // 2, H - 34, 18, (255, 120, 130), "midbottom")

    def draw_stats_panel(self, surf, x, y):
        p = self.player
        gfx.panel(surf, (x, y, 360, 490), alpha=235)
        gfx.draw_text(surf, f"{p.name}", x + 20, y + 14, 24, p.color)
        gfx.draw_text(surf, f"Poziom {p.level}", x + 340, y + 20, 16, (190, 255, 190), "topright")
        rows = [
            ("Maks. HP", f"{p.max_hp}"),
            ("Obrażenia", f"{p.eff_damage():.1f}"),
            ("Strzały / s", f"{1 / p.fire_interval():.2f}"),
            ("Pociski", f"{p.projectiles}" + (f" (+{p.backshot} w tył)" if p.backshot else "")),
            ("Szansa kryt.", f"{p.crit * 100:.0f}%  (x{p.crit_mult:.1f})"),
            ("Zasięg", f"{p.range:.0f}"),
            ("Szybkość", f"{p.speed:.0f}"),
            ("Pancerz", f"{p.armor * 100:.0f}%"),
            ("Unik", f"{p.dodge * 100:.0f}%"),
            ("Regeneracja", f"{p.regen:.1f} HP/s"),
            ("Kradzież życia", f"{p.lifesteal * 100:.0f}%"),
            ("Szczęście", f"{p.luck:.0f}"),
            ("Przebicie / Odbicia", f"{p.pierce} / {p.bounce}"),
            ("Moc umiejętności", f"{p.ability_power * 100:.0f}%"),
        ]
        for i, (a, b) in enumerate(rows):
            yy = y + 54 + i * 24
            gfx.draw_text(surf, a, x + 20, yy, 15, (200, 195, 210), heavy=False)
            gfx.draw_text(surf, b, x + 340, yy, 15, WHITE, "topright")
        yy = y + 398
        for l in gfx.wrap_text(f"{self.cdef['ability']}: {self.cdef['ability_desc']}", 12, 320)[:2]:
            gfx.draw_text(surf, l, x + 20, yy, 12, (255, 220, 120), heavy=False)
            yy += 16
        yy += 4
        for l in gfx.wrap_text(self.cdef["passive"], 12, 320)[:3]:
            gfx.draw_text(surf, l, x + 20, yy, 12, (180, 220, 255), heavy=False)
            yy += 16

    def draw_items_panel(self, surf, x, y):
        p = self.player
        gfx.panel(surf, (x, y, 460, 490), alpha=235)
        gfx.draw_text(surf, f"Przedmioty ({len(p.items)})", x + 20, y + 14, 22, WHITE)
        if not p.items:
            gfx.draw_text(surf, "Jeszcze nic nie znalazłeś.", x + 20, y + 60, 15, GREY_TXT, heavy=False)
        for i, k in enumerate(p.items[:30]):
            it = ITEM_BY_KEY.get(k)
            if not it:
                continue
            col = i // 15
            row = i % 15
            xx = x + 20 + col * 220
            yy = y + 56 + row * 27
            gfx.draw_icon(surf, it["icon"], it["color"], xx + 9, yy + 9, 9)
            gfx.draw_text(surf, it["name"], xx + 26, yy, 14, RARITY_COLORS[it["rarity"]], heavy=False)


GREY_TXT = (170, 165, 180)
