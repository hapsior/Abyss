# Otchłań (Abyss)

Roguelike w stylu *Brotato* i *The Binding of Isaac*. Silnik **Godot 4.7.2 (.NET)**, język **C#**.
Wersja 2.0 to przepisanie gry z Pythona (wersja 1.1) i jej rozbudowa. Plan prac jest w [`docs/PLAN.md`](docs/PLAN.md).

## Uruchomienie (Windows)

1. Zainstaluj **.NET SDK 8** (lub nowszy): <https://dotnet.microsoft.com/download>.
2. Pobierz **Godot 4.7.2 – wersja .NET** (`Godot_v4.7.2-stable_mono_win64.zip`): <https://godotengine.org/download/archive/4.7.2-stable/>. Wystarczy rozpakować, nie wymaga instalacji.
3. W Godocie: **Import** → wskaż plik `project.godot` z tego repo → **Import & Edit**.
4. Naciśnij **F5**, żeby uruchomić grę. Kod C# kompiluje się sam przy starcie.

Kod możesz edytować w Visual Studio, Riderze albo VS Code. Godot → Editor Settings → Dotnet → Editor pozwala ustawić zewnętrzny edytor.

## Co jest w grze (2.0)

- **40 pięter w 8 krainach** (po 5 pięter, boss na każdym 5.): Piwnice, Katakumby, Zalane Groty, Kuźnia, Grzybnia, Lodowa Krypta, Mechanizm, Otchłań. Wrogowie są przebarwieni pod krainę. Po 40. piętrze zaczyna się **Głębia**: piętra generowane bez końca, coraz trudniejsze, aż do śmierci.
- **8 bossów**: Król Glutów, Licz, Matka Roju, Władca Kuźni, Matka Grzybni, Szronowy Kolos, Zegarmistrz, Serce Otchłani.
- **12 postaci**: Rycerz, Łowczyni, Berserker, Mag, Złodziej, Alchemik, Upadły oraz nowe: Bard, Inżynier (wieżyczki), Szaman (totem burzy), Mnich (Krok Ki), Rewolwerowiec (bębenek na 6 naboi).
- **74 przedmioty**, awanse z losowaniem ponownie i odrzucaniem, sklep otwierany rozmową ze sklepikarzem (przycisk interakcji), przedmioty na piedestałach bierze się przyciskiem.
- **3 sloty zapisu**: gra zapisuje się sama przy przejściu między pokojami i przy wyjściu; śmierć kasuje slot.
- **Co-op na jednym ekranie** (klawiatura + mysz, druga połowa klawiatury albo pady).
- **Pad**: po wykryciu pada podpowiedzi pokazują przyciski Xbox (A/B/X/Y, LB/RB, LT/RT); po dotknięciu klawiatury lub myszy wracają klawisze.
- **Ekran**: rozdzielczość bazowa 1920×1080 skalowana do każdego monitora (16:9, 21:9, 4:3); okno, pełny ekran, pełny ekran wyłączny, V-Sync i limit klatek 60/120/144/165/240/bez limitu.

## Sterowanie

| Akcja | Klawiatura + mysz | Pad (Xbox) |
|---|---|---|
| Ruch | WASD | lewa gałka |
| Celowanie / strzał | mysz, lewy przycisk | prawa gałka, RT |
| Zryw | Spacja / Shift | A |
| Umiejętność | prawy przycisk myszy / Q | B |
| Interakcja (weź przedmiot, kup, sklep) | E | X |
| Statystyki | Tab | Y |
| Pauza | Esc / P | Start |
| Auto-celowanie | F | – |

## Struktura

| Ścieżka | Zawartość |
|---|---|
| `src/Core` | Autoload `Game` (ekrany, świat, ustawienia), `Boot` (tryby uruchomienia), `Settings`, `JsonStore` (atomowe zapisy w `%APPDATA%\Otchlan`), `Rng` (PCG32 z zapisywalnym stanem). |
| `src/Sim` | Cała rozgrywka bez grafiki: piętra i pokoje, gracz, pociski, wrogowie (`Enemies/`), bossowie (`Bosses/`), przedmioty, balans (`Balance.cs`). Działa też bez okna (testy, bot, symulator balansu). |
| `src/View` | Rysowanie świata: pokój, postacie, pociski, efekty, HUD, wejście graczy (`RunScene` łączy symulację z widokiem). |
| `src/UI` | Menu, wybór slotu/trybu/postaci/trudności, awans, sklep, pauza, wynik, motyw, podpowiedzi przycisków, zrzuty ekranów do QA. |
| `src/Input` | Akcje, domyślne przypisania, wykrywanie pada/klawiatury, glify przycisków. |
| `src/Save` | Sloty zapisu i profil (odblokowania, rekordy). |
| `src/Audio` | Efekty dźwiękowe i muzyka z płynnymi przejściami. |
| `data/` | Dane w JSON: postacie, przedmioty, ulepszenia, wrogowie, bossowie, krainy, trudności, pieczęcie, osiągnięcia. |
| `art/` | Grafika SVG w stylu opisanym w [`docs/ART_STYLE.md`](docs/ART_STYLE.md). |
| `audio/` | Dźwięki i muzyka (generowane przez `tools/gen_audio.py`). |
| `assets/fonts` | Titan One i Baloo 2 (licencja OFL, pełne polskie znaki). |
| `tests/` | Testy uruchamiane w Godocie, bot i symulator balansu. |
| `tools/` | Podgląd SVG, generator dźwięków, build bez dostępu do sieci. |
| `legacy/` | Wersja w Pythonie (źródła 1.0, dane 1.1) – wzorzec przepisania. |

## Nowy boss / postać / przedmiot

- **Boss**: wpis w `data/bosses.json` (ataki, fazy, `hp_mult`), klasa w `src/Sim/Bosses/` z `[EnemyType("klucz")]` dziedzicząca po `Boss` (wzorzec: `SporeMother.cs`), grafika `art/bosses/klucz.svg`, przypisanie do krainy w `data/biomes.json`.
- **Postać**: wpis w `data/characters.json` (statystyki, broń, pocisk, warunek odblokowania), umiejętność w `PlayerSim.UseAbility`, grafika w `art/characters/` i `art/weapons/`.
- **Przedmiot**: wpis w `data/items.json` (statystyki z `PlayerSim`, np. `damage`, `fire_mult`), ikona `art/items/klucz.svg`.

## Uruchamianie z linii poleceń (QA)

```
godot --headless --path . -- --run-tests                     # testy, kod wyjścia 0 = OK
godot --path . -- --windowed --tour zrzuty/                   # zrzuty menu, ekranów i wyprawy sterowanej botem
godot --path . -- --windowed --boss-tour zrzuty/ --bosses clockmaker   # zrzuty każdego ataku bossa
godot --path . -- --windowed --char-tour zrzuty/ --chars bard,mnich    # zrzuty postaci używających umiejętności
godot --path . -- --boss spore_mother                         # od razu walka z bossem (bez zapisu)
godot --headless --path . -- --balance wynik.csv --chars rycerz,mag --floors 45   # symulator balansu
```

## Eksport (Windows)

W Godocie: **Project → Export → Windows Desktop → Export Project** (preset jest w `export_presets.cfg`, wynik w `build/windows/Otchlan.exe`). Wymaga szablonów eksportu 4.7.2 mono (Editor → Manage Export Templates → Download). Workflow `.github/workflows/build.yml` robi to samo w GitHub Actions i udostępnia artefakt `Otchlan-Windows`.
