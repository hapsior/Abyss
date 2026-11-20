"""Sceny: menu główne, wybór postaci i trudności, ustawienia, gra, wyniki."""
import math
import random

import pygame

from . import gfx
from .audio import audio
from .characters import CHARACTERS, CHAR_BY_KEY
from .config import (W, H, TITLE, VERSION, DIFFICULTIES, INK, WHITE, GOLD, RARITY_COLORS, RARITY_NAMES,
                     FLOORS, NUM_FLOORS)
from .game import Run, Input
from .items import roll_upgrades, apply_upgrade, ITEM_BY_KEY

TAU = math.tau
GREY_TXT = (170, 165, 185)


def ctrl_key(e):
    """Tłumaczy przyciski pada na klawisze (nawigacja po menu)."""
    if e.type != getattr(pygame, "CONTROLLERBUTTONDOWN", -1):
        return None
    m = {
        getattr(pygame, "CONTROLLER_BUTTON_A", -10): pygame.K_RETURN,
        getattr(pygame, "CONTROLLER_BUTTON_B", -11): pygame.K_ESCAPE,
        getattr(pygame, "CONTROLLER_BUTTON_START", -12): pygame.K_ESCAPE,
        getattr(pygame, "CONTROLLER_BUTTON_DPAD_UP", -13): pygame.K_UP,
        getattr(pygame, "CONTROLLER_BUTTON_DPAD_DOWN", -14): pygame.K_DOWN,
        getattr(pygame, "CONTROLLER_BUTTON_DPAD_LEFT", -15): pygame.K_LEFT,
        getattr(pygame, "CONTROLLER_BUTTON_DPAD_RIGHT", -16): pygame.K_RIGHT,
        getattr(pygame, "CONTROLLER_BUTTON_X", -17): pygame.K_r,
    }
    return m.get(e.button)


def key_of(e):
    if e.type == pygame.KEYDOWN:
        return e.key
    return ctrl_key(e)


# =====================================================================  TŁO MENU
class MenuBG:
    def __init__(self):
        self.t = 0.0
        self.embers = [[random.uniform(0, W), random.uniform(0, H), random.uniform(10, 40), random.uniform(1, 3),
                        random.uniform(0, TAU)] for _ in range(70)]
        self.bg = pygame.Surface((W, H)).convert()
        for y in range(H):
            f = y / H
            c = gfx.lerp_color((26, 18, 40), (8, 6, 12), f)
            pygame.draw.line(self.bg, c, (0, y), (W, y))
        for i in range(260):
            x, y = random.uniform(0, W), random.uniform(0, H * 0.7)
            c = gfx.lerp_color((26, 18, 40), (140, 110, 190), random.uniform(0.1, 0.5))
            pygame.draw.circle(self.bg, c, (int(x), int(y)), 1)
        self.walkers = []
        keys = [c["key"] for c in CHARACTERS]
        for i in range(5):
            self.walkers.append(dict(key=random.choice(keys), x=random.uniform(0, W), dir=random.choice((-1, 1)),
                                     sp=random.uniform(30, 60), t=random.uniform(0, 10)))

    def update(self, dt):
        self.t += dt
        for e in self.embers:
            e[1] -= e[2] * dt
            e[0] += math.sin(self.t + e[4]) * 10 * dt
            if e[1] < -10:
                e[1] = H + 10
                e[0] = random.uniform(0, W)
        for w in self.walkers:
            w["x"] += w["dir"] * w["sp"] * dt
            w["t"] += dt
            if w["x"] < -60:
                w["dir"] = 1
            elif w["x"] > W + 60:
                w["dir"] = -1

    def draw(self, surf, walkers=True):
        surf.blit(self.bg, (0, 0))
        for x, y, sp, s, ph in self.embers:
            c = (255, 140 + int(60 * math.sin(self.t * 2 + ph)), 60)
            gfx.blit_glow(surf, x, y, s * 5, gfx.mul_color(c, 0.5))
            pygame.draw.circle(surf, c, (int(x), int(y)), int(s))
        pygame.draw.rect(surf, (14, 10, 20), (0, H - 70, W, 70))
        pygame.draw.line(surf, (50, 40, 70), (0, H - 70), (W, H - 70), 3)
        if walkers:
            for w in self.walkers:
                c = CHAR_BY_KEY[w["key"]]
                gfx.shadow(surf, w["x"], H - 58, 36, 10)
                gfx.draw_character(surf, w["key"], w["x"], H - 76, 17, 0 if w["dir"] > 0 else math.pi, w["t"], True,
                                   False, c["color"], c["accent"])
        surf.blit(gfx.vignette(W, H), (0, 0))


class Scene:
    def __init__(self, app):
        self.app = app

    def on_enter(self):
        pygame.mouse.set_visible(True)

    def handle(self, e):
        pass

    def update(self, dt):
        pass

    def draw(self, screen):
        pass


def button(surf, rect, label, hover, color=(60, 50, 80), size=24, enabled=True):
    rect = pygame.Rect(rect)
    c = gfx.lerp_color(color, (255, 255, 255), 0.18) if hover else color
    if not enabled:
        c = (40, 36, 48)
    off = -3 if hover else 0
    pygame.draw.rect(surf, INK, rect.move(0, 5), border_radius=12)
    r2 = rect.move(0, off)
    pygame.draw.rect(surf, c, r2, border_radius=12)
    pygame.draw.rect(surf, (255, 220, 120) if hover else gfx.lerp_color(c, WHITE, 0.25), r2, 3, border_radius=12)
    gfx.draw_text(surf, label, r2.centerx, r2.centery, size, WHITE if enabled else (110, 105, 120), "center")


class MenuList:
    """Pionowa lista przycisków z obsługą klawiatury i myszy."""

    def __init__(self, labels, x, y, w=320, h=56, gap=16):
        self.labels = labels
        self.rects = [pygame.Rect(x - w // 2, y + i * (h + gap), w, h) for i in range(len(labels))]
        self.sel = 0

    def handle(self, e):
        k = key_of(e)
        if k in (pygame.K_UP, pygame.K_w):
            self.sel = (self.sel - 1) % len(self.labels)
            audio.play("select")
        elif k in (pygame.K_DOWN, pygame.K_s):
            self.sel = (self.sel + 1) % len(self.labels)
            audio.play("select")
        elif k in (pygame.K_RETURN, pygame.K_SPACE, pygame.K_KP_ENTER):
            audio.play("confirm")
            return self.sel
        elif e.type == pygame.MOUSEMOTION:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos) and self.sel != i:
                    self.sel = i
                    audio.play("select")
        elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos):
                    self.sel = i
                    audio.play("confirm")
                    return i
        return None

    def draw(self, surf, size=24):
        for i, (r, l) in enumerate(zip(self.rects, self.labels)):
            button(surf, r, l, i == self.sel, size=size)


# =====================================================================  MENU GŁÓWNE
class MainMenu(Scene):
    def __init__(self, app):
        super().__init__(app)
        self.bg = app.menu_bg
        self.menu = MenuList(["Graj", "Jak grać", "Ustawienia", "Wyjście"], W // 2, 330)

    def on_enter(self):
        super().on_enter()
        audio.play_music("menu")

    def handle(self, e):
        r = self.menu.handle(e)
        if r == 0:
            self.app.goto(CharSelect(self.app))
        elif r == 1:
            self.app.goto(HowTo(self.app))
        elif r == 2:
            self.app.goto(Options(self.app, self))
        elif r == 3:
            self.app.quit()
        if key_of(e) == pygame.K_ESCAPE:
            self.app.quit()

    def update(self, dt):
        self.bg.update(dt)

    def draw(self, screen):
        self.bg.draw(screen)
        t = self.bg.t
        y = 150 + math.sin(t * 1.5) * 6
        gfx.blit_glow(screen, W // 2, y + 10, 260, (70, 20, 90))
        gfx.draw_text(screen, TITLE.upper(), W // 2, y, 112, (235, 210, 255), "center", outline=6, ocolor=(30, 10, 40))
        gfx.draw_text(screen, "roguelike · zejdź na dno i przeżyj", W // 2, y + 76, 20, (200, 170, 230), "center",
                      heavy=False)
        self.menu.draw(screen)
        st = self.app.data["stats"]
        gfx.draw_text(screen, f"Wyprawy: {st['runs']}   ·   Zwycięstwa: {st['wins']}   ·   Pokonani wrogowie: {st['kills']}",
                      W // 2, H - 98, 15, GREY_TXT, "center", heavy=False)
        gfx.draw_text(screen, f"v{VERSION}   ·   F11 – pełny ekran", W - 14, H - 14, 12, (120, 110, 140), "bottomright", heavy=False)


# =====================================================================  JAK GRAĆ
class HowTo(Scene):
    def __init__(self, app):
        super().__init__(app)
        self.bg = app.menu_bg

    def handle(self, e):
        k = key_of(e)
        if k in (pygame.K_ESCAPE, pygame.K_RETURN, pygame.K_SPACE) or (e.type == pygame.MOUSEBUTTONDOWN and e.button == 1):
            audio.play("confirm")
            self.app.goto(MainMenu(self.app))

    def update(self, dt):
        self.bg.update(dt)

    def draw(self, screen):
        self.bg.draw(screen, walkers=False)
        gfx.draw_text(screen, "JAK GRAĆ", W // 2, 40, 48, (235, 210, 255), "midtop", outline=4)
        cols = [
            ("Sterowanie", [
                "WSAD – ruch",
                "Mysz – celowanie, LPM – strzał",
                "Strzałki – strzał w danym kierunku",
                "Spacja / Shift – zryw (chwilowa nietykalność)",
                "PPM / Q / E – umiejętność postaci",
                "F – auto-celowanie (strzela samo)",
                "TAB – statystyki i przedmioty",
                "Esc / P – pauza, M – wycisz muzykę",
                "Pad: lewa gałka ruch, prawa celowanie,",
                "A/LB – zryw, B/RB – umiejętność, Y – auto",
            ]),
            ("Wyprawa", [
                "Otchłań ma 5 pięter, każde z bossem.",
                "Drzwi zamykają się, dopóki pokój",
                "nie zostanie oczyszczony.",
                "Za doświadczenie awansujesz – wybierasz",
                "1 z 3 ulepszeń (R – losuj ponownie).",
                "Przedmioty łączą się w synergie:",
                "np. naprowadzanie + przebicie + odbicia.",
                "Śmierć oznacza koniec wyprawy –",
                "ale odblokowujesz nowe postacie.",
            ]),
            ("Pokoje na mapie", [
                "★ Skarbiec – darmowy przedmiot",
                "● Sklep – przedmioty i leczenie za monety",
                "♦ Ołtarz Krwi – rzadki dar za maks. HP",
                "☠ Boss – po zwycięstwie właz w dół",
                "",
                "Elity (złota obwódka) są silniejsze,",
                "ale zostawiają więcej monet.",
                "Czerwone beczki wybuchają!",
                "Skrzynie mogą skrywać monety i serca.",
            ]),
        ]
        for i, (title, lines) in enumerate(cols):
            x = 40 + i * 410
            gfx.panel(screen, (x, 120, 390, 470), alpha=220)
            gfx.draw_text(screen, title, x + 20, 136, 24, GOLD)
            for j, l in enumerate(lines):
                gfx.draw_text(screen, l, x + 20, 184 + j * 36, 16, (225, 220, 235), heavy=False)
        gfx.draw_text(screen, "Enter / Esc – powrót", W // 2, H - 40, 16, GREY_TXT, "center", heavy=False)


# =====================================================================  USTAWIENIA
class Options(Scene):
    ITEMS = [
        ("sfx", "Głośność efektów", "slider"),
        ("music", "Głośność muzyki", "slider"),
        ("fullscreen", "Pełny ekran", "toggle"),
        ("shake", "Wstrząsy ekranu", "toggle"),
        ("dmgnums", "Liczby obrażeń", "toggle"),
        ("autoaim", "Auto-celowanie", "toggle"),
        ("back", "Powrót", "button"),
    ]

    def __init__(self, app, back_scene):
        super().__init__(app)
        self.back = back_scene
        self.sel = 0
        self.bg = app.menu_bg
        self.rects = [pygame.Rect(W // 2 - 300, 170 + i * 66, 600, 54) for i in range(len(self.ITEMS))]

    def change(self, idx, delta):
        key, label, kind = self.ITEMS[idx]
        s = self.app.data["settings"]
        if kind == "slider":
            s[key] = round(max(0.0, min(1.0, s[key] + delta * 0.1)), 2)
            audio.set_volumes(s["sfx"], s["music"])
            audio.play("select")
        elif kind == "toggle":
            s[key] = not s[key]
            if key == "fullscreen":
                self.app.toggle_fullscreen()
            audio.play("confirm")
        else:
            self.leave()

    def leave(self):
        self.app.save()
        audio.play("confirm")
        self.app.goto(self.back)

    def handle(self, e):
        k = key_of(e)
        if k == pygame.K_ESCAPE:
            self.leave()
        elif k in (pygame.K_UP, pygame.K_w):
            self.sel = (self.sel - 1) % len(self.ITEMS)
            audio.play("select")
        elif k in (pygame.K_DOWN, pygame.K_s):
            self.sel = (self.sel + 1) % len(self.ITEMS)
            audio.play("select")
        elif k in (pygame.K_LEFT, pygame.K_a):
            if self.ITEMS[self.sel][2] == "slider":
                self.change(self.sel, -1)
        elif k in (pygame.K_RIGHT, pygame.K_d):
            if self.ITEMS[self.sel][2] == "slider":
                self.change(self.sel, 1)
        elif k in (pygame.K_RETURN, pygame.K_SPACE):
            if self.ITEMS[self.sel][2] != "slider":
                self.change(self.sel, 1)
        elif e.type == pygame.MOUSEMOTION:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos):
                    self.sel = i
        elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos):
                    self.sel = i
                    key, _, kind = self.ITEMS[i]
                    if kind == "slider":
                        br = pygame.Rect(r.right - 260, r.y, 200, r.h)
                        s = self.app.data["settings"]
                        if br.collidepoint(e.pos):
                            s[key] = round(max(0.0, min(1.0, (e.pos[0] - br.x) / br.w)) * 20) / 20
                            audio.set_volumes(s["sfx"], s["music"])
                            audio.play("select")
                        else:
                            self.change(i, 1 if e.pos[0] > br.right else -1)
                    else:
                        self.change(i, 1)

    def update(self, dt):
        self.bg.update(dt)
        audio.update()

    def draw(self, screen):
        self.bg.draw(screen, walkers=False)
        gfx.draw_text(screen, "USTAWIENIA", W // 2, 70, 48, (235, 210, 255), "midtop", outline=4)
        s = self.app.data["settings"]
        for i, ((key, label, kind), r) in enumerate(zip(self.ITEMS, self.rects)):
            hover = i == self.sel
            if kind == "button":
                button(screen, r.inflate(-300, 0), label, hover)
                continue
            gfx.panel(screen, r, color=(44, 36, 60) if hover else (32, 26, 44),
                      border=(255, 220, 120) if hover else (70, 60, 90), radius=10)
            gfx.draw_text(screen, label, r.x + 20, r.centery, 20, WHITE, "midleft")
            if kind == "slider":
                br = pygame.Rect(r.right - 260, r.centery - 9, 200, 18)
                gfx.bar(screen, br, s[key], (150, 120, 230))
                gfx.draw_text(screen, f"{int(s[key] * 100)}%", r.right - 20, r.centery, 16, WHITE, "midright")
            else:
                on = s[key]
                gfx.draw_text(screen, "WŁ." if on else "WYŁ.", r.right - 20, r.centery, 20,
                              (120, 230, 120) if on else (230, 110, 110), "midright")
        gfx.draw_text(screen, "Strzałki – zmiana, Enter – przełącz, Esc – powrót", W // 2, H - 40, 15, GREY_TXT,
                      "center", heavy=False)


# =====================================================================  WYBÓR POSTACI
def char_unlocked(app, c):
    return c["key"] in app.data["unlocked"]


class CharSelect(Scene):
    def __init__(self, app):
        super().__init__(app)
        self.bg = app.menu_bg
        keys = [c["key"] for c in CHARACTERS]
        last = app.data.get("last", {}).get("char", "rycerz")
        self.sel = keys.index(last) if last in keys else 0
        n = len(CHARACTERS)
        cw, gap = 158, 12
        x0 = (W - (n * cw + (n - 1) * gap)) // 2
        self.rects = [pygame.Rect(x0 + i * (cw + gap), 100, cw, 196) for i in range(n)]
        self.t = 0.0

    def confirm(self):
        c = CHARACTERS[self.sel]
        if not char_unlocked(self.app, c):
            audio.play("deny")
            return
        audio.play("confirm")
        self.app.data["last"]["char"] = c["key"]
        self.app.goto(DiffSelect(self.app, c["key"]))

    def handle(self, e):
        k = key_of(e)
        n = len(CHARACTERS)
        if k == pygame.K_ESCAPE:
            audio.play("confirm")
            self.app.goto(MainMenu(self.app))
        elif k in (pygame.K_LEFT, pygame.K_a):
            self.sel = (self.sel - 1) % n
            audio.play("select")
        elif k in (pygame.K_RIGHT, pygame.K_d):
            self.sel = (self.sel + 1) % n
            audio.play("select")
        elif k in (pygame.K_RETURN, pygame.K_SPACE):
            self.confirm()
        elif e.type == pygame.MOUSEMOTION:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos) and i != self.sel:
                    self.sel = i
                    audio.play("select")
        elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos):
                    self.sel = i
                    self.confirm()
            if pygame.Rect(W // 2 - 140, H - 88, 280, 50).collidepoint(e.pos):
                self.confirm()

    def update(self, dt):
        self.t += dt
        self.bg.update(dt)

    def draw(self, screen):
        self.bg.draw(screen, walkers=False)
        gfx.draw_text(screen, "WYBIERZ POSTAĆ", W // 2, 30, 44, (235, 210, 255), "midtop", outline=4)
        for i, (c, r) in enumerate(zip(CHARACTERS, self.rects)):
            sel = i == self.sel
            unlocked = char_unlocked(self.app, c)
            rr = r.move(0, -8 if sel else 0)
            gfx.panel(screen, rr, color=(50, 40, 70) if sel else (30, 24, 42),
                      border=(255, 220, 120) if sel else (70, 60, 90), radius=14, width=4 if sel else 3)
            cx, cy = rr.centerx, rr.y + 92
            if unlocked:
                gfx.blit_glow(screen, cx, cy, 70, gfx.mul_color(c["color"], 0.35))
                gfx.shadow(screen, cx, cy + 34, 64, 16)
                gfx.draw_character(screen, c["key"], cx, cy, 32, 0.4 if not sel else math.sin(self.t * 2) * 0.6 + 0.3,
                                   self.t, sel, False, c["color"], c["accent"])
                gfx.draw_text(screen, c["name"], cx, rr.bottom - 38, 20, WHITE, "center")
                best = self.best_floor(c["key"])
                if best:
                    gfx.draw_text(screen, best, cx, rr.bottom - 16, 12, GOLD, "center", heavy=False)
            else:
                tmp = pygame.Surface((120, 120), pygame.SRCALPHA)
                gfx.draw_character(tmp, c["key"], 60, 64, 32, 0.4, 0, False, False, c["color"], c["accent"])
                tmp.fill((24, 20, 32, 255), special_flags=pygame.BLEND_RGBA_MIN)
                screen.blit(tmp, (cx - 60, cy - 64))
                gfx.draw_text(screen, "???", cx, rr.bottom - 38, 20, (120, 110, 140), "center")
                pygame.draw.rect(screen, (150, 140, 160), (cx - 10, cy - 4, 20, 16), border_radius=3)
                pygame.draw.arc(screen, (150, 140, 160), (cx - 8, cy - 16, 16, 20), 0, math.pi, 3)
        self.draw_details(screen, CHARACTERS[self.sel])
        unlocked = char_unlocked(self.app, CHARACTERS[self.sel])
        mp = pygame.mouse.get_pos()
        br = pygame.Rect(W // 2 - 140, H - 88, 280, 50)
        button(screen, br, "Wybierz" if unlocked else "Zablokowana", br.collidepoint(mp), enabled=unlocked)
        gfx.draw_text(screen, "← → wybór · Enter zatwierdź · Esc powrót", W // 2, H - 8, 13, GREY_TXT, "midbottom", heavy=False)

    def best_floor(self, key):
        b = self.app.data["best"].get(key, {})
        wins = self.app.data["wins"].get(key, [])
        if wins:
            return f"Zwycięstwa: {len(wins)}"
        if b:
            return f"Rekord: piętro {max(b.values())}"
        return ""

    def draw_details(self, screen, c):
        r = pygame.Rect(90, 316, W - 180, 300)
        gfx.panel(screen, r, alpha=230)
        unlocked = char_unlocked(self.app, c)
        if not unlocked:
            gfx.draw_text(screen, "Postać zablokowana", r.centerx, r.y + 90, 30, (200, 190, 210), "center")
            gfx.draw_text(screen, "Jak odblokować: " + c["unlock"]["text"], r.centerx, r.y + 140, 20, GOLD, "center", heavy=False)
            return
        gfx.draw_text(screen, c["name"], r.x + 30, r.y + 20, 34, c["color"])
        gfx.draw_text(screen, c["title"], r.x + 30, r.y + 66, 17, GREY_TXT, heavy=False)
        st = c["stats"]
        bars = [
            ("Zdrowie", st["max_hp"] / 150, f"{st['max_hp']}"),
            ("Obrażenia", st["damage"] / 18, f"{st['damage']}"),
            ("Szybkostrzelność", st["fire_rate"] / 3.6, f"{st['fire_rate']:.1f}/s"),
            ("Szybkość", (st["speed"] - 150) / 140, f"{st['speed']}"),
            ("Zasięg", st["range"] / 640, f"{st['range']}"),
        ]
        for i, (name, f, val) in enumerate(bars):
            y = r.y + 104 + i * 36
            gfx.draw_text(screen, name, r.x + 30, y, 16, (210, 205, 220), heavy=False)
            gfx.bar(screen, (r.x + 200, y + 2, 220, 16), f, c["color"], radius=5)
            gfx.draw_text(screen, val, r.x + 434, y, 15, WHITE)
        x2 = r.x + 540
        gfx.draw_text(screen, "Pasywka", x2, r.y + 24, 20, (150, 210, 255))
        for i, l in enumerate(gfx.wrap_text(c["passive"], 17, r.right - x2 - 30)):
            gfx.draw_text(screen, l, x2, r.y + 56 + i * 24, 17, (225, 220, 235), heavy=False)
        gfx.draw_text(screen, f"Umiejętność: {c['ability']}", x2, r.y + 150, 20, (255, 210, 110))
        gfx.draw_text(screen, f"odnowienie {c['ability_cd']:.0f} s", r.right - 30, r.y + 154, 14, GREY_TXT, "topright", heavy=False)
        for i, l in enumerate(gfx.wrap_text(c["ability_desc"], 17, r.right - x2 - 30)):
            gfx.draw_text(screen, l, x2, r.y + 182 + i * 24, 17, (225, 220, 235), heavy=False)


# =====================================================================  WYBÓR TRUDNOŚCI
class DiffSelect(Scene):
    def __init__(self, app, char_key):
        super().__init__(app)
        self.char_key = char_key
        self.bg = app.menu_bg
        self.sel = app.data.get("last", {}).get("diff", 1)
        cw, gap = 270, 20
        x0 = (W - (4 * cw + 3 * gap)) // 2
        self.rects = [pygame.Rect(x0 + i * (cw + gap), 160, cw, 360) for i in range(4)]
        self.t = 0.0

    def confirm(self):
        audio.play("confirm")
        self.app.data["last"]["diff"] = self.sel
        self.app.save()
        self.app.goto(GameScene(self.app, self.char_key, self.sel))

    def handle(self, e):
        k = key_of(e)
        if k == pygame.K_ESCAPE:
            audio.play("confirm")
            self.app.goto(CharSelect(self.app))
        elif k in (pygame.K_LEFT, pygame.K_a):
            self.sel = (self.sel - 1) % 4
            audio.play("select")
        elif k in (pygame.K_RIGHT, pygame.K_d):
            self.sel = (self.sel + 1) % 4
            audio.play("select")
        elif k in (pygame.K_RETURN, pygame.K_SPACE):
            self.confirm()
        elif e.type == pygame.MOUSEMOTION:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos) and i != self.sel:
                    self.sel = i
                    audio.play("select")
        elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1:
            for i, r in enumerate(self.rects):
                if r.collidepoint(e.pos):
                    self.sel = i
                    self.confirm()

    def update(self, dt):
        self.t += dt
        self.bg.update(dt)

    def draw(self, screen):
        self.bg.draw(screen, walkers=False)
        c = CHAR_BY_KEY[self.char_key]
        gfx.draw_text(screen, "POZIOM TRUDNOŚCI", W // 2, 40, 44, (235, 210, 255), "midtop", outline=4)
        gfx.draw_text(screen, f"Postać: {c['name']}", W // 2, 100, 18, c["color"], "midtop")
        best = self.app.data["best"].get(self.char_key, {})
        wins = self.app.data["wins"].get(self.char_key, [])
        icons = ["clover", "shield", "flame", "skull"]
        for i, (d, r) in enumerate(zip(DIFFICULTIES, self.rects)):
            sel = i == self.sel
            rr = r.move(0, -10 if sel else 0)
            gfx.panel(screen, rr, color=(50, 40, 70) if sel else (30, 24, 42),
                      border=d["color"] if sel else (70, 60, 90), radius=16, width=4 if sel else 3)
            iy = rr.y + 70 + (math.sin(self.t * 3) * 4 if sel else 0)
            gfx.blit_glow(screen, rr.centerx, iy, 60, gfx.mul_color(d["color"], 0.35))
            gfx.draw_icon(screen, icons[i], d["color"], rr.centerx, iy, 26)
            gfx.draw_text(screen, d["name"], rr.centerx, rr.y + 128, 30, d["color"], "midtop")
            for j, l in enumerate(d["desc"]):
                gfx.draw_text(screen, l, rr.centerx, rr.y + 184 + j * 26, 15, (220, 215, 230), "midtop", heavy=False)
            mods = f"HP wrogów x{d['enemy_hp']:.2f} · obrażenia x{d['enemy_dmg']:.2f}"
            gfx.draw_text(screen, mods, rr.centerx, rr.bottom - 64, 11, GREY_TXT, "midtop", heavy=False)
            rec = best.get(d["key"])
            if d["key"] in wins:
                gfx.draw_text(screen, "★ Ukończono", rr.centerx, rr.bottom - 36, 16, GOLD, "midtop")
            elif rec:
                gfx.draw_text(screen, f"Rekord: piętro {rec}", rr.centerx, rr.bottom - 36, 15, (200, 200, 220), "midtop")
        gfx.draw_text(screen, "← → wybór · Enter rozpocznij · Esc powrót", W // 2, H - 40, 15, GREY_TXT, "center", heavy=False)


# =====================================================================  GRA
class GameScene(Scene):
    def __init__(self, app, char_key, diff_idx):
        super().__init__(app)
        self.char_key = char_key
        self.diff_idx = diff_idx
        self.run = Run(app, char_key, diff_idx)
        app.data["stats"]["runs"] += 1
        app.save()
        self.overlay = None
        self.choices = []
        self.csel = 0
        self.reroll_cost = 3
        self.tab = False
        self.dash_q = False
        self.ability_q = False
        self.pause_menu = None
        self.confirm_quit = False
        self.ov_t = 0.0
        self.rng = random.Random()
        self.last_mouse = (W // 2, H // 2)
        self.mouse_moved_t = 0.0

    def on_enter(self):
        pygame.mouse.set_visible(False)
        if self.run.boss and self.run.boss.alive:
            audio.play_music("boss")
        else:
            audio.play_music("explore")

    # ------------------------------------------------------------------ nakładki
    def open_levelup(self):
        self.overlay = "levelup"
        self.ov_t = 0.0
        self.reroll_cost = 3
        p = self.run.player
        self.choices = roll_upgrades(self.rng, p.luck, p.level)
        self.csel = 1
        pygame.mouse.set_visible(True)

    def pick_upgrade(self, i):
        up = self.choices[i]
        p = self.run.player
        apply_upgrade(p, up)
        audio.play("confirm")
        self.run.particles.text(p.x, p.y - 40, up["name"], RARITY_COLORS[up["rarity"]], 20, 1.2)
        self.run.pending_levelups -= 1
        if self.run.pending_levelups > 0:
            self.choices = roll_upgrades(self.rng, p.luck, p.level)
            self.ov_t = 0.0
            self.reroll_cost = 3
        else:
            self.overlay = None
            pygame.mouse.set_visible(False)

    def reroll(self):
        p = self.run.player
        if p.coins >= self.reroll_cost:
            p.coins -= self.reroll_cost
            self.reroll_cost += 2
            self.choices = roll_upgrades(self.rng, p.luck, p.level)
            audio.play("coin")
            self.ov_t = 0.1
        else:
            audio.play("deny")

    def open_pause(self):
        self.overlay = "pause"
        self.pause_menu = MenuList(["Wznów", "Ustawienia", "Porzuć wyprawę"], 1080, 230, w=300, h=52, gap=14)
        self.confirm_quit = False
        pygame.mouse.set_visible(True)

    def close_overlay(self):
        self.overlay = None
        pygame.mouse.set_visible(False)

    # ------------------------------------------------------------------ zdarzenia
    def handle(self, e):
        k = None
        if e.type == pygame.KEYDOWN:
            k = e.key
        if self.overlay == "levelup":
            kk = key_of(e)
            if kk in (pygame.K_LEFT, pygame.K_a):
                self.csel = (self.csel - 1) % 3
                audio.play("select")
            elif kk in (pygame.K_RIGHT, pygame.K_d):
                self.csel = (self.csel + 1) % 3
                audio.play("select")
            elif kk in (pygame.K_RETURN, pygame.K_SPACE) and self.ov_t > 0.25:
                self.pick_upgrade(self.csel)
            elif kk in (pygame.K_1, pygame.K_2, pygame.K_3) and self.ov_t > 0.25:
                self.pick_upgrade(kk - pygame.K_1)
            elif kk == pygame.K_r:
                self.reroll()
            elif e.type == pygame.MOUSEMOTION:
                for i, r in enumerate(self.card_rects()):
                    if r.collidepoint(e.pos):
                        self.csel = i
            elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 1 and self.ov_t > 0.25:
                for i, r in enumerate(self.card_rects()):
                    if r.collidepoint(e.pos):
                        self.pick_upgrade(i)
                        return
                if self.reroll_rect().collidepoint(e.pos):
                    self.reroll()
            return
        if self.overlay == "pause":
            kk = key_of(e)
            if kk == pygame.K_ESCAPE or kk == pygame.K_p:
                self.close_overlay()
                return
            r = self.pause_menu.handle(e)
            if r == 0:
                self.close_overlay()
            elif r == 1:
                self.app.goto(Options(self.app, self))
            elif r == 2:
                if self.confirm_quit:
                    self.overlay = None
                    self.app.goto(ResultScene(self.app, self.run, False, abandoned=True))
                else:
                    self.confirm_quit = True
                    self.pause_menu.labels[2] = "Na pewno? Kliknij ponownie"
            return
        # rozgrywka
        if k in (pygame.K_ESCAPE, pygame.K_p):
            if self.run.state in ("play", "transition"):
                self.open_pause()
        elif k in (pygame.K_SPACE, pygame.K_LSHIFT, pygame.K_RSHIFT):
            self.dash_q = True
        elif k in (pygame.K_q, pygame.K_e):
            self.ability_q = True
        elif k == pygame.K_f:
            s = self.app.data["settings"]
            s["autoaim"] = not s.get("autoaim", False)
            audio.play("select")
        elif k == pygame.K_m:
            s = self.app.data["settings"]
            if s["music"] > 0:
                s["_music_prev"] = s["music"]
                s["music"] = 0.0
            else:
                s["music"] = s.get("_music_prev", 0.45) or 0.45
            audio.set_volumes(s["sfx"], s["music"])
        elif k == pygame.K_TAB:
            self.tab = True
        elif e.type == pygame.KEYUP and e.key == pygame.K_TAB:
            self.tab = False
        elif e.type == pygame.MOUSEBUTTONDOWN and e.button == 3:
            self.ability_q = True
        elif e.type == pygame.MOUSEMOTION:
            self.mouse_moved_t = 1.5
        elif e.type == getattr(pygame, "CONTROLLERBUTTONDOWN", -1):
            b = e.button
            if b in (getattr(pygame, "CONTROLLER_BUTTON_A", -1), getattr(pygame, "CONTROLLER_BUTTON_LEFTSHOULDER", -1)):
                self.dash_q = True
            elif b in (getattr(pygame, "CONTROLLER_BUTTON_B", -1), getattr(pygame, "CONTROLLER_BUTTON_RIGHTSHOULDER", -1)):
                self.ability_q = True
            elif b == getattr(pygame, "CONTROLLER_BUTTON_Y", -1):
                s = self.app.data["settings"]
                s["autoaim"] = not s.get("autoaim", False)
            elif b == getattr(pygame, "CONTROLLER_BUTTON_START", -1):
                self.open_pause()
            elif b == getattr(pygame, "CONTROLLER_BUTTON_BACK", -1):
                self.tab = not self.tab

    # ------------------------------------------------------------------ aktualizacja
    def build_input(self):
        run = self.run
        p = run.player
        inp = Input()
        keys = pygame.key.get_pressed()
        mx = (keys[pygame.K_d] - keys[pygame.K_a])
        my = (keys[pygame.K_s] - keys[pygame.K_w])
        ax = (keys[pygame.K_RIGHT] - keys[pygame.K_LEFT])
        ay = (keys[pygame.K_DOWN] - keys[pygame.K_UP])
        mpos = pygame.mouse.get_pos()
        mb = pygame.mouse.get_pressed()
        aim = math.atan2(mpos[1] - p.y, mpos[0] - p.x)
        fire = bool(mb[0])
        target = mpos
        pad = self.app.pad()
        if pad:
            lx, ly, rx, ry, rt = pad
            if abs(lx) > 0.22 or abs(ly) > 0.22:
                mx, my = lx, ly
            if math.hypot(rx, ry) > 0.35:
                aim = math.atan2(ry, rx)
                fire = True
                target = (p.x + math.cos(aim) * 260, p.y + math.sin(aim) * 260)
            if rt > 0.5:
                fire = True
        if ax or ay:
            aim = math.atan2(ay, ax)
            fire = True
            target = (p.x + math.cos(aim) * 260, p.y + math.sin(aim) * 260)
        elif self.app.data["settings"].get("autoaim") and not (pad and math.hypot(pad[2], pad[3]) > 0.35):
            e = run.nearest_enemy(p.x, p.y, 900)
            if e is not None:
                aim = math.atan2(e.y - p.y, e.x - p.x)
                fire = True
                target = (e.x, e.y)
        m = math.hypot(mx, my)
        if m > 1:
            mx, my = mx / m, my / m
        inp.move = (mx, my)
        inp.aim = aim
        inp.fire = fire
        inp.dash = self.dash_q
        inp.ability = self.ability_q
        inp.target = target
        self.dash_q = False
        self.ability_q = False
        return inp

    def update(self, dt):
        audio.update()
        self.ov_t += dt
        run = self.run
        if self.overlay:
            run.particles.update(dt * 0.0)
            return
        inp = self.build_input()
        run.update(dt, inp)
        if (run.pending_levelups > 0 and run.state == "play" and run.room.cleared
                and not any(pk.kind == "xp" for pk in run.room.pickups)):
            self.open_levelup()
        if run.state == "dying" and run.state_t > 1.8:
            self.app.goto(ResultScene(self.app, run, False))
        elif run.state == "won" and run.state_t > 3.5:
            self.app.goto(ResultScene(self.app, run, True))

    # ------------------------------------------------------------------ rysowanie
    def card_rects(self):
        cw, ch, gap = 300, 280, 30
        x0 = (W - (3 * cw + 2 * gap)) // 2
        return [pygame.Rect(x0 + i * (cw + gap), 210, cw, ch) for i in range(3)]

    def reroll_rect(self):
        return pygame.Rect(W // 2 - 150, 530, 300, 50)

    def draw(self, screen):
        run = self.run
        run.draw(screen)
        if self.tab and not self.overlay:
            run.draw_stats_panel(screen, 180, 120)
            run.draw_items_panel(screen, 560, 120)
        if self.overlay == "levelup":
            self.draw_levelup(screen)
        elif self.overlay == "pause":
            self.draw_pause(screen)
        elif run.state in ("play", "transition"):
            mx, my = pygame.mouse.get_pos()
            if not self.app.data["settings"].get("autoaim") or self.mouse_moved_t > 0:
                c = (255, 240, 220)
                pygame.draw.circle(screen, INK, (mx, my), 11, 5)
                pygame.draw.circle(screen, c, (mx, my), 10, 2)
                pygame.draw.circle(screen, c, (mx, my), 2)
        if self.mouse_moved_t > 0:
            self.mouse_moved_t -= 1 / 60

    def dim(self, screen, a=170):
        s = pygame.Surface((W, H), pygame.SRCALPHA)
        s.fill((8, 5, 14, a))
        screen.blit(s, (0, 0))

    def draw_levelup(self, screen):
        self.dim(screen)
        p = self.run.player
        gfx.draw_text(screen, f"AWANS!  Poziom {p.level - self.run.pending_levelups + 1}", W // 2, 100, 48,
                      (150, 255, 150), "midtop", outline=4)
        extra = f"  (jeszcze {self.run.pending_levelups - 1})" if self.run.pending_levelups > 1 else ""
        gfx.draw_text(screen, "Wybierz ulepszenie" + extra, W // 2, 160, 18, (220, 215, 230), "midtop", heavy=False)
        for i, (up, r) in enumerate(zip(self.choices, self.card_rects())):
            sel = i == self.csel
            f = min(1.0, max(0.0, (self.ov_t - i * 0.06) / 0.2))
            rr = r.move(0, int((1 - f) * 40) - (12 if sel else 0))
            rc = RARITY_COLORS[up["rarity"]]
            gfx.panel(screen, rr, color=(46, 38, 64) if sel else (30, 24, 42), border=rc, radius=16, width=5 if sel else 3)
            gfx.draw_text(screen, RARITY_NAMES[up["rarity"]], rr.centerx, rr.y + 18, 14, rc, "midtop")
            icon = {"dmg_mult": "blade", "fire_mult": "bolt", "max_hp": "heart", "speed": "boot", "armor": "shield",
                    "regen": "plus", "range": "scope", "crit": "eye", "lifesteal": "fang", "luck": "clover",
                    "pickup": "magnet", "shot_speed": "arrow", "dodge": "wing", "damage": "gem",
                    "ability_cd_mult": "hourglass"}.get(up["attr"], "star")
            gfx.blit_glow(screen, rr.centerx, rr.y + 96, 50, gfx.mul_color(rc, 0.4))
            gfx.draw_icon(screen, icon, rc, rr.centerx, rr.y + 96, 26)
            gfx.draw_text(screen, up["name"], rr.centerx, rr.y + 150, 26, WHITE, "midtop")
            gfx.draw_text(screen, up["desc"], rr.centerx, rr.y + 196, 18, (230, 225, 240), "midtop", heavy=False)
            gfx.draw_text(screen, f"[{i + 1}]", rr.centerx, rr.bottom - 30, 14, GREY_TXT, "midtop")
        rr = self.reroll_rect()
        mp = pygame.mouse.get_pos()
        button(screen, rr, f"Losuj ponownie (R) – {self.reroll_cost}", rr.collidepoint(mp), size=18,
               enabled=p.coins >= self.reroll_cost)
        gfx.draw_icon(screen, "coin", GOLD, rr.right + 20, rr.centery, 9)
        gfx.draw_text(screen, str(p.coins), rr.right + 34, rr.centery, 18, GOLD, "midleft")

    def draw_pause(self, screen):
        self.dim(screen, 190)
        gfx.draw_text(screen, "PAUZA", W // 2, 30, 54, (235, 210, 255), "midtop", outline=4)
        self.run.draw_stats_panel(screen, 50, 120)
        self.run.draw_items_panel(screen, 430, 120)
        self.pause_menu.draw(screen, size=19)
        gfx.draw_text(screen, "Esc – wznów", 1080, 470, 14, GREY_TXT, "midtop", heavy=False)


# =====================================================================  WYNIKI
class ResultScene(Scene):
    def __init__(self, app, run, won, abandoned=False):
        super().__init__(app)
        self.run = run
        self.won = won
        self.abandoned = abandoned
        self.bg = app.menu_bg
        self.menu = MenuList(["Zagraj ponownie", "Zmień postać", "Menu główne"], W // 2, 528, w=300, h=46, gap=10)
        self.t = 0.0
        app.save()

    def on_enter(self):
        super().on_enter()
        audio.play_music("menu")

    def handle(self, e):
        r = self.menu.handle(e)
        if r == 0:
            self.app.goto(GameScene(self.app, self.run.char_key, self.run.diff_idx))
        elif r == 1:
            self.app.goto(CharSelect(self.app))
        elif r == 2:
            self.app.goto(MainMenu(self.app))

    def update(self, dt):
        self.t += dt
        self.bg.update(dt)

    def draw(self, screen):
        self.bg.draw(screen, walkers=False)
        run = self.run
        p = run.player
        if self.won:
            title, col = "ZWYCIĘSTWO!", (255, 220, 100)
            sub = "Serce Otchłani przestało bić."
        elif self.abandoned:
            title, col = "WYPRAWA PORZUCONA", (200, 190, 220)
            sub = "Otchłań poczeka."
        else:
            title, col = "POLEGŁEŚ", (235, 80, 90)
            sub = f"Piętro {run.depth}: {FLOORS[run.depth - 1]['name']}"
        y = 40 + math.sin(self.t * 2) * 3
        gfx.draw_text(screen, title, W // 2, y, 64, col, "midtop", outline=5)
        gfx.draw_text(screen, sub, W // 2, y + 82, 20, (220, 215, 230), "midtop", heavy=False)
        r = pygame.Rect(W // 2 - 520, 150, 480, 360)
        gfx.panel(screen, r, alpha=230)
        gfx.shadow(screen, r.x + 70, r.y + 92, 70, 18)
        gfx.draw_character(screen, run.char_key, r.x + 70, r.y + 60, 30, 0.3, self.t, False, False, p.color, p.accent)
        gfx.draw_text(screen, p.name, r.x + 130, r.y + 30, 28, p.color)
        gfx.draw_text(screen, run.diff["name"], r.x + 130, r.y + 66, 18, run.diff["color"])
        m, s = divmod(int(run.run_time), 60)
        rows = [
            ("Najgłębsze piętro", f"{run.max_depth} / {NUM_FLOORS}"),
            ("Poziom postaci", str(p.level)),
            ("Pokonani wrogowie", str(run.stats["kills"])),
            ("Pokonani bossowie", str(run.stats["bosses"])),
            ("Zadane obrażenia", f"{int(run.stats['damage_dealt'])}"),
            ("Zebrane monety", str(run.stats["coins"])),
            ("Odwiedzone pokoje", str(run.stats["rooms"])),
            ("Czas", f"{m:02d}:{s:02d}"),
        ]
        for i, (a, b) in enumerate(rows):
            yy = r.y + 120 + i * 30
            gfx.draw_text(screen, a, r.x + 30, yy, 17, (200, 195, 215), heavy=False)
            gfx.draw_text(screen, b, r.right - 30, yy, 17, WHITE, "topright")
        r2 = pygame.Rect(W // 2 + 40, 150, 480, 360)
        gfx.panel(screen, r2, alpha=230)
        gfx.draw_text(screen, f"Przedmioty ({len(p.items)})", r2.x + 24, r2.y + 20, 22, WHITE)
        if not p.items:
            gfx.draw_text(screen, "Brak przedmiotów.", r2.x + 24, r2.y + 70, 17, GREY_TXT, heavy=False)
        for i, k in enumerate(p.items[:40]):
            it = ITEM_BY_KEY.get(k)
            if not it:
                continue
            cx = r2.x + 44 + (i % 10) * 43
            cy = r2.y + 86 + (i // 10) * 48
            gfx.blit_glow(screen, cx, cy, 24, gfx.mul_color(RARITY_COLORS[it["rarity"]], 0.35))
            gfx.draw_icon(screen, it["icon"], it["color"], cx, cy, 13)
        if run.unlocked_now:
            yy = r2.bottom - 80
            gfx.draw_text(screen, "Odblokowano:", r2.x + 24, yy, 18, GOLD)
            gfx.draw_text(screen, ", ".join(run.unlocked_now), r2.x + 24, yy + 28, 20, (255, 240, 200))
        self.menu.draw(screen, size=19)
