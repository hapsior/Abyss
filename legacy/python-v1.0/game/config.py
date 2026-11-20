"""Stałe, ścieżki, motywy pięter i poziomy trudności."""
import os
import sys

TITLE = "Otchłań"
VERSION = "1.0"

W, H = 1280, 720
FPS = 60

HUD_H = 64
WALL = 40
RX0 = WALL                # lewa krawędź wnętrza pokoju
RY0 = HUD_H + WALL        # górna krawędź wnętrza
RX1 = W - WALL            # prawa
RY1 = H - WALL            # dolna
RCX = (RX0 + RX1) // 2    # środek pokoju
RCY = (RY0 + RY1) // 2

TILE = 48
GCOLS = (RX1 - RX0) // TILE   # 25
GROWS = (RY1 - RY0) // TILE   # 12

DOOR_HALF = 52  # połowa szerokości drzwi

DIRS = {
    "N": (0, -1),
    "S": (0, 1),
    "W": (-1, 0),
    "E": (1, 0),
}
OPPOSITE = {"N": "S", "S": "N", "W": "E", "E": "W"}

# Kolory ogólne
BG = (18, 14, 24)
INK = (16, 12, 20)          # obrys
WHITE = (245, 240, 235)
GREY = (150, 145, 160)
DARK = (40, 34, 50)
RED = (235, 70, 70)
GREEN = (110, 220, 110)
YELLOW = (255, 214, 90)
GOLD = (255, 190, 60)
BLUE = (100, 170, 255)
PURPLE = (185, 110, 255)
ORANGE = (255, 150, 50)
HP_RED = (220, 60, 70)
XP_GREEN = (120, 220, 120)

RARITY_NAMES = ["Zwykły", "Niezwykły", "Rzadki", "Legendarny"]
RARITY_COLORS = [(210, 210, 220), (90, 170, 255), (190, 105, 255), (255, 85, 85)]

ENEMY_BULLET = (255, 90, 120)
ENEMY_BULLET2 = (255, 170, 60)
ENEMY_BULLET3 = (190, 120, 255)

FLOORS = [
    dict(name="Piwnice", floor=(70, 56, 50), floor2=(63, 50, 45), wall=(40, 31, 30),
         wall_top=(56, 44, 41), accent=(210, 160, 100), decor="pebbles"),
    dict(name="Katakumby", floor=(58, 58, 68), floor2=(52, 52, 62), wall=(32, 32, 42),
         wall_top=(47, 47, 60), accent=(200, 200, 180), decor="bones"),
    dict(name="Zalane Groty", floor=(40, 66, 72), floor2=(36, 59, 65), wall=(22, 40, 46),
         wall_top=(31, 53, 60), accent=(90, 210, 220), decor="puddles"),
    dict(name="Kuźnia", floor=(74, 46, 40), floor2=(67, 41, 36), wall=(42, 24, 22),
         wall_top=(60, 34, 30), accent=(255, 130, 50), decor="lava"),
    dict(name="Otchłań", floor=(44, 30, 60), floor2=(39, 27, 54), wall=(22, 14, 34),
         wall_top=(34, 22, 50), accent=(195, 95, 255), decor="stars"),
]
NUM_FLOORS = len(FLOORS)

DIFFICULTIES = [
    dict(key="easy", name="Łatwy", color=(120, 220, 120),
         desc=["Wrogowie słabsi i wolniejsi", "Więcej leczenia", "Tańszy sklep"],
         enemy_hp=0.75, enemy_dmg=0.6, bullet_speed=0.85, enemy_count=0.8, elite=0.0,
         heal=1.5, price=0.85, fire_rate=0.8, extra_bullets=0),
    dict(key="normal", name="Normalny", color=(255, 214, 90),
         desc=["Zbalansowane wyzwanie", "Tak jak zaprojektowano"],
         enemy_hp=1.0, enemy_dmg=1.0, bullet_speed=1.0, enemy_count=1.0, elite=0.06,
         heal=1.0, price=1.0, fire_rate=1.0, extra_bullets=0),
    dict(key="hard", name="Trudny", color=(255, 140, 60),
         desc=["Silniejsi i liczniejsi wrogowie", "Częste elity", "Mniej leczenia"],
         enemy_hp=1.3, enemy_dmg=1.35, bullet_speed=1.12, enemy_count=1.2, elite=0.16,
         heal=0.75, price=1.1, fire_rate=1.15, extra_bullets=0),
    dict(key="nightmare", name="Koszmar", color=(235, 60, 80),
         desc=["Wrogowie strzelają gęściej", "Elity na każdym kroku", "Leczenie o połowę słabsze"],
         enemy_hp=1.65, enemy_dmg=1.75, bullet_speed=1.25, enemy_count=1.4, elite=0.3,
         heal=0.5, price=1.25, fire_rate=1.3, extra_bullets=1),
]


def resource_path(*parts):
    """Ścieżka do zasobów, działa też po spakowaniu PyInstallerem."""
    base = getattr(sys, "_MEIPASS", None)
    if base is None:
        base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(base, *parts)


def save_dir():
    if os.name == "nt" and os.environ.get("APPDATA"):
        d = os.path.join(os.environ["APPDATA"], "Otchlan")
    else:
        d = os.path.join(os.path.expanduser("~"), ".otchlan")
    try:
        os.makedirs(d, exist_ok=True)
    except OSError:
        d = os.path.dirname(os.path.abspath(__file__))
    return d
