"""Jednorazowa konwersja danych wersji 1.1 (legacy/v1.1/data_dump.json) do plików data/*.json dla Godota.
Po konwersji pliki w data/ są źródłem prawdy – edytuj je bezpośrednio."""
import json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
d = json.load(open(os.path.join(ROOT, "legacy/v1.1/data_dump.json"), encoding="utf-8"))
out = os.path.join(ROOT, "data")

def hexc(c):
    return "#%02x%02x%02x" % tuple(int(v) for v in c[:3])

def dump(name, obj):
    with open(os.path.join(out, name), "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=1)

# --- postacie (kolory zgodne z nową grafiką: jasne, czytelne) ---
body = {"rycerz": "#5b9cff", "lowczyni": "#5fcf6e", "berserker": "#ff7a5c", "mag": "#a77bff",
        "zlodziej": "#8fa3c7", "alchemik": "#ffc24a", "upadly": "#9b6bdb"}
weapon = {"rycerz": "miecz", "lowczyni": "luk", "berserker": "topor", "mag": "kostur", "zlodziej": "sztylet",
          "alchemik": "miotacz", "upadly": "kosa"}
pivot = {"miecz": [44, 48], "luk": [66, 48], "topor": [44, 48], "kostur": [52, 48], "sztylet": [40, 52],
         "miotacz": [40, 48], "kosa": [44, 70]}
chars = []
for c in d["characters"]["CHARACTERS"]:
    chars.append(dict(key=c["key"], name=c["name"], title=c["title"], color=body[c["key"]], accent=hexc(c["accent"]),
                      bullet=c["bullet"], bullet_color=hexc(c["bullet_color"]), sound=c["sound"], passive=c["passive"],
                      ability=c["ability"], ability_desc=c["ability_desc"], ability_cd=c["ability_cd"], stats=c["stats"],
                      unlock=c.get("unlock"), weapon=weapon[c["key"]], weapon_pivot=pivot[weapon[c["key"]]]))
dump("characters.json", chars)

# --- przedmioty i ulepszenia ---
items = []
for it in d["items"]["ITEMS"]:
    items.append(dict(key=it["key"], name=it["name"], desc=it["desc"], rarity=it["rarity"],
                      fx=[dict(stat=a, op=o, value=v) for a, o, v in it["fx"]], special=it.get("special")))
dump("items.json", items)
ups = [dict(key=u[0], name=u[1], desc=u[2], stat=u[3], op=u[4], values=u[5]) for u in d["items"]["UPGRADES"]]
dump("upgrades.json", ups)

# --- trudności, pieczęcie ---
diffs = []
for x in d["config"]["DIFFICULTIES"]:
    y = dict(x); y["color"] = hexc(x["color"]); diffs.append(y)
dump("difficulties.json", diffs)
dump("seals.json", [dict(name=s[0], desc=s[1], mods=s[2]) for s in d["balance"]["SEALS"]])

# --- wrogowie (statystyki z klas; zachowanie jest w kodzie C#) ---
ca = d["enemies__class_attrs"]
base = {k.split(".", 1)[1]: v for k, v in ca.items() if k.startswith("Enemy.")}
classes = {}
for k, v in ca.items():
    cls, attr = k.split(".", 1)
    classes.setdefault(cls, {})[attr] = v
# BigSlime dziedziczy po Slime
enemies = []
for cls, a in classes.items():
    if cls == "Enemy" or "key" not in a:
        continue
    full = dict(base)
    if cls == "BigSlime":
        full.update(classes["Slime"])
    full.update(a)
    key = full["key"]
    enemies.append(dict(key=key, cls=cls, name=full["name"], hp=full["base_hp"], radius=full["radius"],
                        speed=full["base_speed"], contact=full["contact"], xp=full["xp"], coin=full["coin"],
                        flying=bool(full.get("flying")), ghost=bool(full.get("ghost")), knock_resist=full.get("knock_resist", 1.0),
                        color=hexc(full["color"]), cost=d["enemies"]["ENEMY_COST"][key]))
dump("enemies.json", enemies)

# --- bossowie ---
cb = d["bosses__class_attrs"]
bbase = {k.split(".", 1)[1]: v for k, v in cb.items() if k.startswith("Boss.")}
bcls = {}
for k, v in cb.items():
    cls, attr = k.split(".", 1)
    bcls.setdefault(cls, {})[attr] = v
bosses = []
for cls, a in bcls.items():
    if cls == "Boss" or "key" not in a:
        continue
    full = dict(bbase); full.update(a)
    bosses.append(dict(key=full["key"], cls=cls, name=full["name"], title=full["title"], radius=full["radius"],
                       speed=full.get("base_speed", 0), contact=full.get("contact", 16), hp=full["boss_hp"],
                       flying=bool(full.get("flying")), color=hexc(full["color"]), attacks=full["attacks"],
                       phases=full.get("phase_thresholds", [0.5]), rest=full.get("rest", [0.7, 1.1])))
dump("bosses.json", bosses)

# --- krainy: 8 x 5 pięter ---
pools = d["enemies"]["FLOOR_POOLS"]
old = d["config"]["FLOORS"]
def biome(key, name, src, boss, pool, tint=None, extra=None):
    b = dict(key=key, name=name, floor=hexc(src["floor"]), floor2=hexc(src["floor2"]), wall=hexc(src["wall"]),
             wall_top=hexc(src["wall_top"]), accent=hexc(src["accent"]), decor=src["decor"], boss=boss,
             pool=[dict(enemy=k, weight=w) for k, w in pool],
             # odcień wrogów w tej krainie: przesunięcie barwy [0..1], nasycenie, jasność
             tint=tint or dict(hue=0.0, sat=1.0, val=1.0))
    if extra: b.update(extra)
    return b
biomes = [
    biome("piwnice", "Piwnice", old[0], "slime_king", pools["1"]),
    biome("katakumby", "Katakumby", old[1], "lich", pools["2"], dict(hue=0.0, sat=0.75, val=1.0)),
    biome("groty", "Zalane Groty", old[2], "hive", pools["3"], dict(hue=0.45, sat=1.0, val=1.0)),
    biome("kuznia", "Kuźnia", old[3], "forge", pools["4"], dict(hue=0.92, sat=1.15, val=1.0)),
    biome("grzybnia", "Grzybnia", dict(floor=[52, 64, 42], floor2=[46, 57, 37], wall=[28, 36, 22], wall_top=[40, 50, 32],
          accent=[170, 255, 120], decor="spores"), "spore_mother", pools["3"], dict(hue=0.25, sat=1.1, val=1.0)),
    biome("lodowa_krypta", "Lodowa Krypta", dict(floor=[52, 66, 84], floor2=[46, 59, 76], wall=[26, 34, 46], wall_top=[38, 50, 66],
          accent=[180, 230, 255], decor="frost"), "frost_colossus", pools["4"], dict(hue=0.55, sat=0.6, val=1.1)),
    biome("mechanizm", "Mechanizm", dict(floor=[66, 58, 48], floor2=[59, 52, 43], wall=[36, 30, 24], wall_top=[52, 44, 36],
          accent=[230, 190, 80], decor="gears"), "clockmaker", pools["5"], dict(hue=0.1, sat=0.8, val=1.05)),
    biome("otchlan", "Otchłań", old[4], "abyss", pools["5"], dict(hue=0.75, sat=1.1, val=1.0)),
]
dump("biomes.json", biomes)

ach = [dict(api=a["api"], name=a["name"], desc=a["desc"], icon=a["icon"], color=hexc(a["color"]), hidden=a["hidden"],
            goal=a["goal"]) for a in d["achievements"]["ACHIEVEMENTS"]]
dump("achievements.json", ach)
print("ok", len(chars), len(items), len(ups), len(enemies), len(bosses), len(biomes), len(ach))
