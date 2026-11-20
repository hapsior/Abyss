"""Główna pętla aplikacji, zarządzanie scenami, zapis postępów, pady."""
import sys

import pygame

from . import gfx, save
from .audio import audio
from .characters import CHAR_BY_KEY
from .config import W, H, FPS, TITLE, DIFFICULTIES


class App:
    def __init__(self, headless=False):
        try:
            pygame.mixer.pre_init(22050, -16, 1, 512)
        except Exception:
            pass
        pygame.init()
        self.data = save.load()
        self.headless = headless
        flags = pygame.SCALED | pygame.RESIZABLE
        try:
            self.screen = pygame.display.set_mode((W, H), flags, vsync=1)
        except Exception:
            try:
                self.screen = pygame.display.set_mode((W, H), flags)
            except Exception:
                self.screen = pygame.display.set_mode((W, H))
        pygame.display.set_caption(TITLE)
        self._set_icon()
        self.fullscreen = False
        if self.data["settings"].get("fullscreen") and not headless:
            self.toggle_fullscreen(force=True)
        gfx.init()
        st = self.data["settings"]
        audio.init(st.get("sfx", 0.7), st.get("music", 0.45))
        self.clock = pygame.time.Clock()
        self.running = True
        self.controllers = []
        self._init_controllers()
        from .scenes import MenuBG, MainMenu
        self.menu_bg = MenuBG()
        self.scene = None
        self.goto(MainMenu(self))

    def _set_icon(self):
        try:
            ic = pygame.Surface((32, 32), pygame.SRCALPHA)
            pygame.draw.circle(ic, (16, 12, 20), (16, 16), 15)
            pygame.draw.circle(ic, (160, 90, 230), (16, 16), 12)
            pygame.draw.circle(ic, (250, 250, 250), (11, 14), 4)
            pygame.draw.circle(ic, (250, 250, 250), (21, 14), 4)
            pygame.draw.circle(ic, (16, 12, 20), (12, 15), 2)
            pygame.draw.circle(ic, (16, 12, 20), (22, 15), 2)
            pygame.display.set_icon(ic)
        except Exception:
            pass

    # ------------------------------------------------------------------ pady
    def _init_controllers(self):
        self._ctrl_mod = None
        try:
            from pygame._sdl2 import controller as ctrl
            ctrl.init()
            self._ctrl_mod = ctrl
            for i in range(ctrl.get_count()):
                if ctrl.is_controller(i):
                    self.controllers.append(ctrl.Controller(i))
        except Exception:
            self._ctrl_mod = None

    def _ctrl_added(self, index):
        if not self._ctrl_mod:
            return
        try:
            if self._ctrl_mod.is_controller(index):
                self.controllers.append(self._ctrl_mod.Controller(index))
        except Exception:
            pass

    def pad(self):
        """(lx, ly, rx, ry, rt) z pierwszego pada albo None."""
        if not self.controllers:
            return None
        c = self.controllers[0]
        try:
            ax = lambda a: c.get_axis(a) / 32767.0
            return (ax(pygame.CONTROLLER_AXIS_LEFTX), ax(pygame.CONTROLLER_AXIS_LEFTY),
                    ax(pygame.CONTROLLER_AXIS_RIGHTX), ax(pygame.CONTROLLER_AXIS_RIGHTY),
                    ax(pygame.CONTROLLER_AXIS_TRIGGERRIGHT))
        except Exception:
            return None

    # ------------------------------------------------------------------ sceny i zapis
    def goto(self, scene):
        self.scene = scene
        scene.on_enter()

    def save(self):
        save.save(self.data)

    def unlock(self, key, run=None):
        if key in self.data["unlocked"]:
            return
        self.data["unlocked"].append(key)
        self.save()
        c = CHAR_BY_KEY[key]
        if run is not None:
            run.unlocked_now.append(c["name"])
            run.banner("Odblokowano postać!", c["name"], (255, 220, 100))

    def record_floor(self, char_key, diff_idx, depth):
        dk = DIFFICULTIES[diff_idx]["key"]
        b = self.data["best"].setdefault(char_key, {})
        if depth > b.get(dk, 0):
            b[dk] = depth
            self.save()

    def on_win(self, run):
        dk = DIFFICULTIES[run.diff_idx]["key"]
        self.data["stats"]["wins"] += 1
        w = self.data["wins"].setdefault(run.char_key, [])
        if dk not in w:
            w.append(dk)
        self.unlock("upadly", run)
        self.save()

    def toggle_fullscreen(self, force=False):
        try:
            pygame.display.toggle_fullscreen()
            self.fullscreen = not self.fullscreen
        except Exception:
            pass

    def quit(self):
        self.running = False

    # ------------------------------------------------------------------ pętla
    def run(self):
        while self.running:
            dt = self.clock.tick(FPS) / 1000.0
            dt = min(dt, 1 / 30)
            for e in pygame.event.get():
                if e.type == pygame.QUIT:
                    self.running = False
                elif e.type == pygame.KEYDOWN and e.key == pygame.K_F11:
                    self.toggle_fullscreen()
                    self.data["settings"]["fullscreen"] = not self.data["settings"].get("fullscreen", False)
                    continue
                elif e.type == getattr(pygame, "CONTROLLERDEVICEADDED", -1):
                    self._ctrl_added(e.device_index)
                    continue
                elif e.type == getattr(pygame, "CONTROLLERDEVICEREMOVED", -1):
                    self.controllers = [c for c in self.controllers if c.attached()] if self.controllers else []
                    continue
                self.scene.handle(e)
            audio.update()
            self.scene.update(dt)
            self.scene.draw(self.screen)
            pygame.display.flip()
        self.save()
        pygame.quit()


def main():
    app = App()
    app.run()
    sys.exit(0)
