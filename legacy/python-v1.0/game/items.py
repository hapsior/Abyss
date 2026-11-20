"""Przedmioty (pasywne, jak w Isaacu) i ulepszenia przy awansie (jak w Brotato)."""

# fx: lista (atrybut, operacja, wartość); operacje: "+" dodaj, "*" pomnóż, "=" ustaw
ITEMS = [
    # ---------------- Zwykłe
    dict(key="ostry_kamien", name="Ostry Kamień", desc="+3 obrażeń", rarity=0, icon="gem", color=(190, 190, 205),
         fx=[("damage", "+", 3)]),
    dict(key="kawa", name="Kubek Kawy", desc="+15% szybkostrzelności, +5% szybkości ruchu", rarity=0, icon="cup",
         color=(120, 70, 40), fx=[("fire_mult", "*", 1.15), ("speed", "*", 1.05)]),
    dict(key="buty", name="Buty Wędrowca", desc="+15% szybkości ruchu", rarity=0, icon="boot", color=(160, 105, 60),
         fx=[("speed", "*", 1.15)]),
    dict(key="serce", name="Serce Golema", desc="+25 maks. HP", rarity=0, icon="heart", color=(225, 60, 70),
         fx=[("max_hp", "+", 25)]),
    dict(key="luneta", name="Luneta", desc="+30% zasięgu, +15% prędkości pocisków", rarity=0, icon="scope",
         color=(120, 200, 255), fx=[("range", "*", 1.3), ("shot_speed", "*", 1.15)]),
    dict(key="koniczyna", name="Czterolistna Koniczyna", desc="+15 szczęścia (lepsze nagrody)", rarity=0,
         icon="clover", color=(90, 200, 90), fx=[("luck", "+", 15)]),
    dict(key="zbroja", name="Skórzana Zbroja", desc="+8% pancerza", rarity=0, icon="shield", color=(160, 110, 70),
         fx=[("armor", "+", 0.08)]),
    dict(key="magnes", name="Magnes", desc="+80% zasięgu zbierania, +10% monet", rarity=0, icon="magnet",
         color=(220, 60, 60), fx=[("pickup", "*", 1.8), ("coin_mult", "+", 0.1)]),
    dict(key="bandaz", name="Bandaż", desc="+0.6 HP/s regeneracji", rarity=0, icon="plus", color=(240, 240, 235),
         fx=[("regen", "+", 0.6)]),
    dict(key="noz", name="Nóż Rzeźnika", desc="+2 obrażeń, +5% szansy na krytyka", rarity=0, icon="blade",
         color=(130, 80, 50), fx=[("damage", "+", 2), ("crit", "+", 0.05)]),
    dict(key="skarbonka", name="Skarbonka", desc="+15 monet od razu, +25% monet", rarity=0, icon="coin",
         color=(255, 200, 70), fx=[("coin_mult", "+", 0.25)], special="coins15"),
    dict(key="pioro", name="Lekkie Pióro", desc="+1 ładunek zrywu", rarity=0, icon="wing", color=(235, 235, 245),
         fx=[("dash_charges", "+", 1)]),
    # ---------------- Niezwykłe
    dict(key="podwojny", name="Podwójny Strzał", desc="+1 pocisk, -10% obrażeń", rarity=1, icon="arrow",
         color=(255, 220, 120), fx=[("projectiles", "+", 1), ("dmg_mult", "*", 0.9)]),
    dict(key="groty", name="Przebijające Groty", desc="Pociski przebijają +1 wroga", rarity=1, icon="arrow",
         color=(120, 200, 255), fx=[("pierce", "+", 1)]),
    dict(key="gumowe", name="Gumowe Kule", desc="Pociski odbijają się od ścian (+2)", rarity=1, icon="orb",
         color=(255, 120, 190), fx=[("bounce", "+", 2), ("range", "*", 1.15)]),
    dict(key="oko_szukacza", name="Oko Szukacza", desc="Pociski naprowadzają się na wrogów", rarity=1, icon="eye",
         color=(170, 90, 255), fx=[("homing", "+", 2.6)]),
    dict(key="jad", name="Jad Żmii", desc="Pociski zatruwają wrogów", rarity=1, icon="drop", color=(110, 220, 80),
         fx=[("poison", "+", 1)]),
    dict(key="esencja_ognia", name="Esencja Ognia", desc="25% szans na podpalenie wroga", rarity=1, icon="flame",
         color=(255, 130, 40), fx=[("burn", "+", 0.25)]),
    dict(key="lodowy", name="Lodowy Odłamek", desc="Pociski spowalniają wrogów", rarity=1, icon="snow",
         color=(160, 230, 255), fx=[("slow", "+", 1)]),
    dict(key="krytyczne_oko", name="Oko Krytyka", desc="+10% szansy na krytyka, +50% obrażeń krytycznych",
         rarity=1, icon="eye", color=(235, 60, 60), fx=[("crit", "+", 0.10), ("crit_mult", "+", 0.5)]),
    dict(key="kolce", name="Kolczasta Zbroja", desc="Wrogowie dotykający cię obrywają, +5% pancerza", rarity=1,
         icon="shield", color=(150, 155, 170), fx=[("thorns", "+", 25), ("armor", "+", 0.05)]),
    dict(key="orbita", name="Kryształowa Orbita", desc="Kryształ krąży wokół ciebie i rani wrogów", rarity=1,
         icon="gem", color=(90, 230, 230), fx=[("orbitals", "+", 1)]),
    dict(key="duszek", name="Mały Duszek", desc="Towarzysz, który strzela do wrogów", rarity=1, icon="skull",
         color=(150, 200, 255), fx=[("familiars", "+", 1)]),
    dict(key="lustro", name="Lustro", desc="Dodatkowy pocisk leci do tyłu", rarity=1, icon="mirror",
         color=(170, 220, 255), fx=[("backshot", "+", 1)]),
    dict(key="klepsydra", name="Klepsydra", desc="-30% czasu odnowienia umiejętności", rarity=1, icon="hourglass",
         color=(230, 200, 120), fx=[("ability_cd_mult", "*", 0.7)]),
    dict(key="ksiega", name="Księga Zaklęć", desc="+50% mocy umiejętności", rarity=1, icon="book",
         color=(130, 80, 200), fx=[("ability_power", "+", 0.5)]),
    dict(key="kly", name="Wampirze Kły", desc="+4% kradzieży życia", rarity=1, icon="fang", color=(200, 30, 40),
         fx=[("lifesteal", "+", 0.04)]),
    dict(key="tarcza_energii", name="Tarcza Energii", desc="Co 12 s blokuje jedno trafienie", rarity=1,
         icon="shield", color=(80, 220, 255), fx=[("eshield", "+", 1)]),
    dict(key="gigant", name="Grzyb Gigantyzmu", desc="Większe pociski, +30% obrażeń, -15% szybkostrzelności",
         rarity=1, icon="orb", color=(230, 70, 70), fx=[("shot_size", "*", 1.4), ("dmg_mult", "*", 1.3), ("fire_mult", "*", 0.85)]),
    dict(key="mechanizm", name="Mechanizm Zegarowy", desc="+40% szybkostrzelności, -15% obrażeń", rarity=1,
         icon="rune", color=(230, 190, 80), fx=[("fire_mult", "*", 1.4), ("dmg_mult", "*", 0.85)]),
    # ---------------- Rzadkie
    dict(key="wybuchowe", name="Wybuchowe Pociski", desc="Pociski wybuchają przy trafieniu", rarity=2, icon="bomb",
         color=(255, 150, 40), fx=[("explode", "+", 1), ("fire_mult", "*", 0.9)]),
    dict(key="blyskawica", name="Słoik Błyskawic", desc="25% szans na wyładowanie łańcuchowe", rarity=2,
         icon="bolt", color=(255, 230, 80), fx=[("chain", "+", 0.25)]),
    dict(key="rozszczepienie", name="Rozszczepienie", desc="Pociski rozpadają się na 3 odłamki po trafieniu",
         rarity=2, icon="star", color=(200, 140, 255), fx=[("split", "+", 1)]),
    dict(key="widmowe", name="Widmowe Pociski", desc="Pociski przenikają przeszkody, +1 przebicie", rarity=2,
         icon="orb", color=(200, 200, 255), fx=[("spectral", "=", 1), ("pierce", "+", 1)]),
    dict(key="trzecie_oko", name="Trzecie Oko", desc="+2 pociski, lekkie naprowadzanie, -15% obrażeń", rarity=2,
         icon="eye", color=(255, 200, 60), fx=[("projectiles", "+", 2), ("homing", "+", 1.2), ("dmg_mult", "*", 0.85)]),
    dict(key="czarna_dziura", name="Czarna Dziura", desc="Co 8 s tworzy wir wciągający i raniący wrogów",
         rarity=2, icon="swirl", color=(190, 100, 255), fx=[("blackhole", "+", 1)]),
    dict(key="rekawica", name="Rękawica Tytana", desc="+5 obrażeń, silniejszy odrzut", rarity=2, icon="gem",
         color=(230, 80, 60), fx=[("damage", "+", 5), ("knockback", "*", 1.6)]),
    dict(key="siedmiomilowe", name="Buty Siedmiomilowe", desc="+25% szybkości, -30% odnowienia zrywu", rarity=2,
         icon="boot", color=(90, 140, 255), fx=[("speed", "*", 1.25), ("dash_cd", "*", 0.7)]),
    # ---------------- Legendarne
    dict(key="korona", name="Korona Króla", desc="+25% obrażeń, +15% szybkostrzelności, +20 maks. HP", rarity=3,
         icon="crown", color=(255, 200, 60), fx=[("dmg_mult", "*", 1.25), ("fire_mult", "*", 1.15), ("max_hp", "+", 20)]),
    dict(key="feniks", name="Pióro Feniksa", desc="Raz wskrzesza cię z połową zdrowia", rarity=3, icon="wing",
         color=(255, 120, 40), fx=[("revives", "+", 1)]),
    dict(key="kostka", name="Kostka Chaosu", desc="Daje 2 losowe przedmioty", rarity=3, icon="dice",
         color=(200, 40, 60), fx=[], special="chaos"),
    dict(key="krwawy_pakt", name="Krwawy Pakt", desc="+80% obrażeń, -25 maks. HP", rarity=3, icon="skull",
         color=(200, 20, 40), fx=[("dmg_mult", "*", 1.8), ("max_hp", "+", -25)]),
    dict(key="burzowe_serce", name="Burzowe Serce", desc="+15% wyładowań, +20% szybkostrzelności, +10% szybkości",
         rarity=3, icon="heart", color=(255, 230, 70), fx=[("chain", "+", 0.15), ("fire_mult", "*", 1.2), ("speed", "*", 1.1)]),
    dict(key="kielich", name="Kielich Nieśmiertelności", desc="+50 maks. HP, +1 HP/s regeneracji", rarity=3,
         icon="cup", color=(255, 190, 50), fx=[("max_hp", "+", 50), ("regen", "+", 1.0)]),
]

ITEM_BY_KEY = {it["key"]: it for it in ITEMS}

# Ulepszenia przy awansie: (klucz, nazwa, opis_format, atrybut, operacja, wartości dla 4 poziomów rzadkości)
UPGRADES = [
    ("sila", "Siła", "+{v}% obrażeń", "dmg_mult", "%", [8, 12, 18, 28]),
    ("furia", "Furia", "+{v}% szybkostrzelności", "fire_mult", "%", [7, 11, 16, 24]),
    ("witalnosc", "Witalność", "+{v} maks. HP", "max_hp", "+", [10, 16, 24, 36]),
    ("zwinnosc", "Zwinność", "+{v}% szybkości ruchu", "speed", "%", [5, 8, 11, 15]),
    ("hart", "Hart", "+{v}% pancerza", "armor", "p", [3, 5, 7, 10]),
    ("odnowa", "Odnowa", "+{v} HP/s regeneracji", "regen", "+", [0.3, 0.5, 0.8, 1.2]),
    ("sokoli", "Sokoli Wzrok", "+{v}% zasięgu", "range", "%", [10, 15, 22, 32]),
    ("precyzja", "Precyzja", "+{v}% szansy na krytyka", "crit", "p", [3, 5, 8, 12]),
    ("glod", "Głód Krwi", "+{v}% kradzieży życia", "lifesteal", "p", [2, 3, 5, 7]),
    ("fart", "Fart", "+{v} szczęścia", "luck", "+", [5, 8, 12, 18]),
    ("przyciaganie", "Przyciąganie", "+{v}% zasięgu zbierania", "pickup", "%", [25, 35, 50, 70]),
    ("moc", "Moc", "+{v}% prędkości pocisków", "shot_speed", "%", [8, 12, 16, 22]),
    ("uniki", "Uniki", "+{v}% szansy na unik", "dodge", "p", [2, 4, 6, 9]),
    ("ostrze", "Ostrze", "+{v} obrażeń bazowych", "damage", "+", [1, 2, 3, 5]),
    ("skupienie", "Skupienie", "-{v}% odnowienia umiejętności", "ability_cd_mult", "-%", [6, 9, 13, 18]),
]


def rarity_weights(luck, depth=1):
    w = [62.0, 27.0, 9.0, 2.0]
    w[0] -= luck * 0.4 + depth * 3
    w[1] += luck * 0.2 + depth * 1.5
    w[2] += luck * 0.15 + depth * 1.0
    w[3] += luck * 0.05 + depth * 0.5
    w[0] = max(10.0, w[0])
    return w


def roll_rarity(rng, luck, depth=1, min_rarity=0):
    w = rarity_weights(luck, depth)
    for i in range(min_rarity):
        w[i] = 0
    total = sum(w)
    r = rng.random() * total
    for i, x in enumerate(w):
        r -= x
        if r <= 0:
            return i
    return 3


def roll_item(rng, taken, luck, depth=1, min_rarity=0):
    """Losuje przedmiot, którego gracz jeszcze nie ma (gdy pula się wyczerpie – dowolny)."""
    rar = roll_rarity(rng, luck, depth, min_rarity)
    for delta in (0, -1, 1, -2, 2, -3, 3):
        r = rar + delta
        if r < min_rarity or r > 3:
            continue
        cands = [it for it in ITEMS if it["rarity"] == r and it["key"] not in taken]
        if cands:
            return rng.choice(cands)
    cands = [it for it in ITEMS if it["rarity"] >= min_rarity]
    return rng.choice(cands)


def roll_upgrades(rng, luck, level, n=3):
    picks = rng.sample(UPGRADES, n)
    out = []
    for up in picks:
        rar = roll_rarity(rng, luck, 1 + level // 3)
        out.append(make_upgrade(up, rar))
    return out


def make_upgrade(up, rar):
    key, name, fmt, attr, op, vals = up
    v = vals[rar]
    vs = ("%g" % v)
    return dict(key=key, name=name, desc=fmt.format(v=vs), attr=attr, op=op, value=v, rarity=rar)


def apply_upgrade(player, up):
    attr, op, v = up["attr"], up["op"], up["value"]
    cur = getattr(player, attr)
    if op == "%":
        setattr(player, attr, cur * (1 + v / 100.0))
    elif op == "-%":
        setattr(player, attr, cur * (1 - v / 100.0))
    elif op == "p":
        setattr(player, attr, cur + v / 100.0)
    else:
        setattr(player, attr, cur + v)
        if attr == "max_hp":
            player.hp += v
    player.clamp_stats()


def item_price(item, depth, price_mult):
    base = [12, 18, 27, 38][item["rarity"]]
    return max(3, int(round(base * (1 + 0.08 * (depth - 1)) * price_mult)))

