# Otchłań (Abyss)

Roguelike w stylu *Brotato* i *The Binding of Isaac*. Silnik **Godot 4.7.2 (.NET)**, język **C#**.
Wersja 2.0 jest w budowie: to przepisanie gry z Pythona (wersja 1.1) i rozbudowa. Plan prac jest w [`docs/PLAN.md`](docs/PLAN.md).

## Uruchomienie (Windows)

1. Zainstaluj **.NET SDK 8** (lub nowszy): <https://dotnet.microsoft.com/download>.
2. Pobierz **Godot 4.7.2 – wersja .NET** (`Godot_v4.7.2-stable_mono_win64.zip`): <https://godotengine.org/download/archive/4.7.2-stable/>. Wystarczy rozpakować, nie wymaga instalacji.
3. W Godocie: **Import** → wskaż plik `project.godot` z tego repo → **Import & Edit**.
4. Naciśnij **F5**, żeby uruchomić grę. Kod C# kompiluje się sam przy starcie.

Kod możesz edytować w Visual Studio, Riderze albo VS Code. Godot → Editor Settings → Dotnet → Editor pozwala ustawić zewnętrzny edytor.

## Struktura

| Ścieżka | Zawartość |
|---|---|
| `src/Core` | Autoload `Game` (ekrany, świat, ustawienia), `Settings` (ekran, FPS, dźwięk), `JsonStore` (atomowe zapisy w `%APPDATA%\Otchlan`), `Rng` (PCG32 z zapisywalnym stanem). |
| `src/Input` | Akcje i domyślne przypisania; wykrywanie pada lub klawiatury; glify przycisków (Xbox / klawisze / mysz). |
| `src/UI` | Kreskówkowy motyw, menu, opcje, podpowiedzi przycisków, licznik FPS, automatyczne zrzuty ekranów. |
| `src/World` | Świat gry. Na razie arena testowa etapu 1. |
| `art/` | Grafika wektorowa SVG: postacie, bronie, wrogowie, przedmioty, kafle 8 krain, glify, UI. Styl opisuje [`docs/ART_STYLE.md`](docs/ART_STYLE.md). |
| `assets/fonts` | Titan One i Baloo 2 (licencja OFL, pełne polskie znaki). |
| `tests/` | Testy uruchamiane w Godocie. |
| `tools/` | Narzędzia: podgląd SVG, build bez dostępu do sieci. |
| `legacy/` | Wersja w Pythonie: źródła 1.0, dane 1.1, stare dokumenty. Służy jako wzorzec do przepisania. |

## Ekran i wydajność

- Rozdzielczość bazowa to 1920×1080. Obraz skaluje się do każdego monitora (tryb `canvas_items`, `expand`), więc na 21:9 i 4:3 widać więcej tła, a interfejs trzyma się krawędzi ekranu.
- Opcje → Ekran: okno, pełny ekran, pełny ekran wyłączny, V-Sync, limit klatek (60/120/144/165/240/bez limitu) i licznik FPS.

## Testy

```
godot --headless --path . -- --run-tests                    # testy, kod wyjścia 0 = OK
godot --path . -- --windowed --tour zrzuty/                  # zrzuty kolejnych ekranów do katalogu
godot --path . -- --sandbox                                  # od razu arena testowa
```
