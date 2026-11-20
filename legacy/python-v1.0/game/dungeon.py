"""Generowanie pięter (siatka pokoi jak w Isaacu) i układów przeszkód."""
import math
import random
from collections import deque

import pygame

from . import gfx
from .config import (W, H, HUD_H, WALL, RX0, RY0, RX1, RY1, RCX, RCY, TILE, GCOLS, GROWS, DIRS,
                     FLOORS, INK, DOOR_HALF)
from .entities import Obstacle
from .enemies import roll_wave

MAP_W, MAP_H = 9, 8

DOOR_POS = {
    "N": (RCX, RY0),
    "S": (RCX, RY1),
    "W": (RX0, RCY),
    "E": (RX1, RCY),
}


def _forbidden(col, row):
    # strefy przy drzwiach muszą być wolne
    if row in (4, 5, 6, 7) and (col <= 3 or col >= GCOLS - 4):
        return True
    if 10 <= col <= 14 and (row <= 2 or row >= GROWS - 3):
        return True
    return False


class Room:
    def __init__(self, gx, gy, kind):
        self.gx, self.gy = gx, gy
        self.kind = kind
        self.doors = {}
        self.grid = [[None] * GCOLS for _ in range(GROWS)]
        self.visited = False
        self.seen = False
        self.cleared = kind not in ("combat", "boss")
        self.waves = []
        self.pickups = []
        self.pedestals = []
        self.bg = None
        self.trapdoor = None
        self.seed = random.random()
        self.reward_given = False
        self.dist = 0

    # ------------------------------------------------------------------ geometria
    def tile_at(self, x, y):
        col = int((x - RX0) // TILE)
        row = int((y - RY0) // TILE)
        if 0 <= col < GCOLS and 0 <= row < GROWS:
            return self.grid[row][col]
        return None

    def solid(self, col, row):
        if 0 <= col < GCOLS and 0 <= row < GROWS:
            ob = self.grid[row][col]
            return ob is not None and ob.solid
        return True

    def resolve_circle(self, x, y, r):
        x = min(max(x, RX0 + r), RX1 - r)
        y = min(max(y, RY0 + r), RY1 - r)
        c0 = int((x - r - RX0) // TILE)
        c1 = int((x + r - RX0) // TILE)
        r0 = int((y - r - RY0) // TILE)
        r1 = int((y + r - RY0) // TILE)
        for row in range(max(0, r0), min(GROWS - 1, r1) + 1):
            for col in range(max(0, c0), min(GCOLS - 1, c1) + 1):
                ob = self.grid[row][col]
                if ob is None or not ob.solid:
                    continue
                rx = RX0 + col * TILE + 2
                ry = RY0 + row * TILE + 2
                rw = TILE - 4
                cx = min(max(x, rx), rx + rw)
                cy = min(max(y, ry), ry + rw)
                dx, dy = x - cx, y - cy
                d2 = dx * dx + dy * dy
                if d2 < r * r:
                    if d2 > 1e-6:
                        d = math.sqrt(d2)
                        push = r - d
                        x += dx / d * push
                        y += dy / d * push
                    else:
                        # środek w prostokącie – wypchnij najkrótszą drogą
                        left = x - rx
                        right = rx + rw - x
                        top = y - ry
                        bottom = ry + rw - y
                        m = min(left, right, top, bottom)
                        if m == left:
                            x = rx - r
                        elif m == right:
                            x = rx + rw + r
                        elif m == top:
                            y = ry - r
                        else:
                            y = ry + rw + r
        x = min(max(x, RX0 + r), RX1 - r)
        y = min(max(y, RY0 + r), RY1 - r)
        return x, y

    def los(self, x1, y1, x2, y2):
        d = math.hypot(x2 - x1, y2 - y1)
        n = int(d / 20) + 1
        for i in range(1, n):
            t = i / n
            ob = self.tile_at(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t)
            if ob is not None and ob.solid:
                return False
        return True

    def free_tiles(self):
        return [(c, r) for r in range(GROWS) for c in range(GCOLS) if self.grid[r][c] is None]

    def obstacles(self):
        for row in self.grid:
            for ob in row:
                if ob is not None:
                    yield ob

    def remove_obstacle(self, ob):
        if self.grid[ob.row][ob.col] is ob:
            self.grid[ob.row][ob.col] = None

    # ------------------------------------------------------------------ nawigacja (BFS)
    def flow_field(self, tx, ty):
        tc = int((tx - RX0) // TILE)
        tr = int((ty - RY0) // TILE)
        tc = min(max(tc, 0), GCOLS - 1)
        tr = min(max(tr, 0), GROWS - 1)
        dist = [[9999] * GCOLS for _ in range(GROWS)]
        dist[tr][tc] = 0
        q = deque([(tc, tr)])
        while q:
            c, r = q.popleft()
            d = dist[r][c] + 1
            for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nc, nr = c + dc, r + dr
                if 0 <= nc < GCOLS and 0 <= nr < GROWS and dist[nr][nc] > d and not self.solid(nc, nr):
                    dist[nr][nc] = d
                    q.append((nc, nr))
        return dist

    # ------------------------------------------------------------------ tło
    def build_bg(self, depth, show_controls=False):
        theme = FLOORS[depth - 1]
        rng = random.Random(int(self.seed * 1e9))
        s = pygame.Surface((W, H)).convert()
        s.fill(theme["wall"])
        # podłoga w kafle
        f1, f2 = theme["floor"], theme["floor2"]
        for row in range(GROWS):
            for col in range(GCOLS):
                c = f1 if (row + col) % 2 == 0 else f2
                n = rng.uniform(-4, 4)
                c = gfx.mul_color(c, 1 + n / 100)
                pygame.draw.rect(s, c, (RX0 + col * TILE, RY0 + row * TILE, TILE, TILE))
        # dekoracje
        dec = theme["decor"]
        acc = theme["accent"]
        for i in range(rng.randint(14, 26)):
            x = rng.uniform(RX0 + 10, RX1 - 10)
            y = rng.uniform(RY0 + 10, RY1 - 10)
            if dec == "pebbles":
                c = gfx.mul_color(f1, rng.uniform(0.75, 0.9))
                pygame.draw.ellipse(s, c, (x, y, rng.uniform(4, 10), rng.uniform(3, 7)))
                if rng.random() < 0.3:
                    pygame.draw.line(s, gfx.mul_color(f1, 0.8), (x, y), (x + rng.uniform(-30, 30), y + rng.uniform(-20, 20)), 2)
            elif dec == "bones":
                c = gfx.lerp_color(f1, acc, 0.35)
                a = rng.uniform(0, math.tau)
                dx, dy = math.cos(a) * 9, math.sin(a) * 9
                pygame.draw.line(s, c, (x - dx, y - dy), (x + dx, y + dy), 3)
                for sx, sy in ((x - dx, y - dy), (x + dx, y + dy)):
                    pygame.draw.circle(s, c, (int(sx), int(sy)), 3)
                if rng.random() < 0.15:
                    pygame.draw.circle(s, c, (int(x + 20), int(y)), 7)
                    pygame.draw.circle(s, f1, (int(x + 17), int(y - 1)), 2)
                    pygame.draw.circle(s, f1, (int(x + 23), int(y - 1)), 2)
            elif dec == "puddles":
                c = gfx.lerp_color(f1, acc, 0.18)
                w = rng.uniform(30, 80)
                pygame.draw.ellipse(s, c, (x, y, w, w * 0.45))
                pygame.draw.ellipse(s, gfx.lerp_color(c, (255, 255, 255), 0.1), (x + w * 0.2, y + 3, w * 0.3, w * 0.12))
            elif dec == "lava":
                c = gfx.lerp_color(f1, acc, 0.45)
                px, py = x, y
                for k in range(rng.randint(2, 5)):
                    nx, ny = px + rng.uniform(-28, 28), py + rng.uniform(-18, 18)
                    pygame.draw.line(s, c, (px, py), (nx, ny), 2)
                    px, py = nx, ny
            elif dec == "stars":
                c = gfx.lerp_color(f1, acc, rng.uniform(0.2, 0.6))
                pygame.draw.circle(s, c, (int(x), int(y)), rng.choice((1, 1, 2)))
        # cień przy ścianach
        shade = pygame.Surface((RX1 - RX0, 22), pygame.SRCALPHA)
        for i in range(22):
            pygame.draw.line(shade, (0, 0, 0, int(90 * (1 - i / 22))), (0, i), (RX1 - RX0, i))
        s.blit(shade, (RX0, RY0))
        # ściany: cegły
        wt = theme["wall_top"]
        for y in range(HUD_H, H, 20):
            off = ((y - HUD_H) // 20) % 2 * 24
            for x in range(-24 + off, W, 48):
                r = pygame.Rect(x + 1, y + 1, 46, 18)
                if r.colliderect(pygame.Rect(RX0, RY0, RX1 - RX0, RY1 - RY0)):
                    continue
                c = gfx.mul_color(wt, rng.uniform(0.9, 1.08))
                pygame.draw.rect(s, c, r, border_radius=3)
                pygame.draw.line(s, gfx.mul_color(c, 1.15), (r.x + 2, r.y + 1), (r.right - 3, r.y + 1), 1)
        pygame.draw.rect(s, INK, (RX0 - 4, RY0 - 4, RX1 - RX0 + 8, RY1 - RY0 + 8), 4, border_radius=4)
        # podpowiedzi sterowania na podłodze startowego pokoju
        if show_controls:
            lines = ["WSAD – ruch", "MYSZ – celowanie, LPM – strzał (lub strzałki)", "SPACJA / SHIFT – zryw",
                     "PPM / Q – umiejętność", "F – auto-celowanie, TAB – statystyki"]
            for i, l in enumerate(lines):
                ts = gfx.text_surf(l, 20, gfx.mul_color(f1, 1.45), outline=0)
                ts = ts.copy()
                ts.set_alpha(150)
                s.blit(ts, ts.get_rect(center=(RCX, RCY - 70 + i * 30)))
        self.bg = s
        return s

    def stain(self, x, y, color, r):
        if self.bg is None:
            return
        rng = random
        c = gfx.mul_color(color, 0.55)
        for i in range(rng.randint(3, 6)):
            ox, oy = rng.uniform(-r, r), rng.uniform(-r, r) * 0.6
            rr = rng.uniform(r * 0.3, r * 0.7)
            px, py = x + ox, y + oy
            if RX0 + rr < px < RX1 - rr and RY0 + rr < py < RY1 - rr:
                pygame.draw.ellipse(self.bg, c, (px - rr, py - rr * 0.6, rr * 2, rr * 1.2))


# ====================================================================== układy przeszkód
def _layout(room, rng, depth):
    pat = rng.choice(["scatter", "pillars", "walls", "box", "diag", "empty", "scatter", "pillars", "crates"])
    cells = set()
    hc, hr = GCOLS // 2 + 1, GROWS // 2  # ćwiartka (z kolumną środkową)
    if pat == "scatter":
        for i in range(rng.randint(3, 8)):
            cells.add((rng.randrange(1, hc), rng.randrange(1, hr)))
    elif pat == "pillars":
        for i in range(rng.randint(1, 3)):
            c, r = rng.randrange(2, hc - 2), rng.randrange(1, hr - 1)
            cells.update({(c, r), (c + 1, r), (c, r + 1), (c + 1, r + 1)})
    elif pat == "walls":
        r = rng.randrange(2, hr)
        c0 = rng.randrange(3, 8)
        for c in range(c0, min(hc - 1, c0 + rng.randint(3, 6))):
            cells.add((c, r))
    elif pat == "box":
        c0, r0 = rng.randrange(6, 9), rng.randrange(2, 4)
        for c in range(c0, hc):
            cells.add((c, r0))
        for r in range(r0, hr):
            cells.add((c0, r))
    elif pat == "diag":
        c, r = rng.randrange(4, 8), 1
        while r < hr and c < hc:
            cells.add((c, r))
            c += 1
            r += 1
    full = set()
    for (c, r) in cells:
        for cc, rr in ((c, r), (GCOLS - 1 - c, r), (c, GROWS - 1 - r), (GCOLS - 1 - c, GROWS - 1 - r)):
            if 0 <= cc < GCOLS and 0 <= rr < GROWS and not _forbidden(cc, rr):
                full.add((cc, rr))
    for (c, r) in full:
        room.grid[r][c] = Obstacle("rock", c, r)
    # skrzynie i beczki
    n_extra = rng.randint(0, 3) + (4 if pat == "crates" else 0)
    for i in range(n_extra):
        c, r = rng.randrange(1, GCOLS - 1), rng.randrange(1, GROWS - 1)
        if room.grid[r][c] is None and not _forbidden(c, r) and abs(c - GCOLS // 2) + abs(r - GROWS // 2) > 2:
            room.grid[r][c] = Obstacle(rng.choice(("crate", "crate", "barrel")), c, r)


def _connected(room):
    free = room.free_tiles()
    if not free:
        return False
    start = (2, 5)
    if room.grid[5][2] is not None:
        return False
    seen = {start}
    q = deque([start])
    while q:
        c, r = q.popleft()
        for dc, dr in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nc, nr = c + dc, r + dr
            if 0 <= nc < GCOLS and 0 <= nr < GROWS and (nc, nr) not in seen and room.grid[nr][nc] is None:
                seen.add((nc, nr))
                q.append((nc, nr))
    return len(seen) == len(free)


def make_layout(room, rng, depth):
    for attempt in range(15):
        room.grid = [[None] * GCOLS for _ in range(GROWS)]
        _layout(room, rng, depth)
        if _connected(room):
            return
    room.grid = [[None] * GCOLS for _ in range(GROWS)]


# ====================================================================== piętro
class Floor:
    def __init__(self, depth, diff, rng):
        self.depth = depth
        self.rng = rng
        self.rooms = {}
        self.start = None
        self._generate(diff)

    def _neighbors(self, cells, c):
        return sum(1 for d in DIRS.values() if (c[0] + d[0], c[1] + d[1]) in cells)

    def _generate(self, diff):
        rng = self.rng
        depth = self.depth
        start = (4, 4)
        want_shrine = depth >= 2 and rng.random() < 0.6
        need = 3 + (1 if want_shrine else 0)
        target = min(int(6 + depth * 1.7) + rng.randint(0, 2), 17)
        cells = None
        ok = False
        for attempt in range(500):
            cells = {start}
            q = deque([start])
            while q and len(cells) < target:
                c = q.popleft()
                dirs = list(DIRS.values())
                rng.shuffle(dirs)
                for d in dirs:
                    n = (c[0] + d[0], c[1] + d[1])
                    if not (0 <= n[0] < MAP_W and 0 <= n[1] < MAP_H) or n in cells:
                        continue
                    if self._neighbors(cells, n) > 1 or len(cells) >= target:
                        continue
                    if rng.random() < 0.5:
                        continue
                    cells.add(n)
                    q.append(n)
                if not q and len(cells) < target:
                    q.append(rng.choice(list(cells)))
            if len(cells) < target:
                continue
            dead = [c for c in cells if c != start and self._neighbors(cells, c) == 1]
            if len(dead) < need:
                continue
            dist = self._bfs(cells, start)
            dead.sort(key=lambda c: -dist[c])
            if dist[dead[0]] < 3:
                continue
            ok = True
            break
        if not ok:
            # awaryjny, zawsze poprawny układ
            cells = {start, (5, 4), (6, 4), (7, 4), (8, 4), (4, 3), (4, 5), (3, 4), (2, 4), (6, 3), (6, 5)}
            want_shrine = True
        dist = self._bfs(cells, start)
        dead = [c for c in cells if c != start and self._neighbors(cells, c) == 1]
        dead.sort(key=lambda c: -dist[c])
        kinds = {start: "start"}
        if dead:
            kinds[dead[0]] = "boss"
        rest = dead[1:]
        rng.shuffle(rest)
        for k in ["treasure", "shop"] + (["shrine"] if want_shrine else []):
            if rest:
                kinds[rest.pop()] = k
        for c in cells:
            kind = kinds.get(c, "combat")
            room = Room(c[0], c[1], kind)
            room.dist = dist[c]
            self.rooms[c] = room
        for c, room in self.rooms.items():
            for dname, d in DIRS.items():
                n = (c[0] + d[0], c[1] + d[1])
                if n in self.rooms:
                    room.doors[dname] = self.rooms[n]
        self.start = self.rooms[start]
        # zawartość pokoi
        for room in self.rooms.values():
            if room.kind == "combat":
                make_layout(room, rng, depth)
                budget = (4.5 + depth * 2.0 + rng.uniform(0, 2.5) + min(3, room.dist) * 0.4) * diff["enemy_count"]
                if budget > 9:
                    b1 = budget * 0.6
                    room.waves = [roll_wave(rng, depth, b1), roll_wave(rng, depth, budget - b1)]
                else:
                    room.waves = [roll_wave(rng, depth, budget)]
            elif room.kind in ("treasure", "shrine"):
                for (c, r) in ((2, 1), (GCOLS - 3, 1), (2, GROWS - 2), (GCOLS - 3, GROWS - 2)):
                    room.grid[r][c] = Obstacle("rock", c, r)
        self.start.visited = True
        self.start.seen = True
        for n in self.start.doors.values():
            n.seen = True

    @staticmethod
    def _bfs(cells, start):
        dist = {start: 0}
        q = deque([start])
        while q:
            c = q.popleft()
            for d in DIRS.values():
                n = (c[0] + d[0], c[1] + d[1])
                if n in cells and n not in dist:
                    dist[n] = dist[c] + 1
                    q.append(n)
        return dist

    def neighbor(self, room, dname):
        return room.doors.get(dname)


def door_rect(dname):
    x, y = DOOR_POS[dname]
    if dname in ("N", "S"):
        return pygame.Rect(x - DOOR_HALF, y - WALL if dname == "N" else y, DOOR_HALF * 2, WALL)
    return pygame.Rect(x - WALL if dname == "W" else x, y - DOOR_HALF, WALL, DOOR_HALF * 2)
