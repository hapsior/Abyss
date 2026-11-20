# Otchłań – przewodnik stylu grafiki

Styl: **kreskówka w duchu Brotato**. Proste, okrągłe kształty, grube ciemne kontury, płaskie jaskrawe kolory, jeden cień i jeden połysk, duże wyraziste oczy. Każdy element musi być czytelny w ułamku sekundy w środku walki pełnej pocisków.

Wzorce (traktuj je jako źródło prawdy):

| Plik | Co pokazuje |
|---|---|
| `art/characters/rycerz.svg` | Bohater: ciało-fasolka, akcesoria, twarz. |
| `art/enemies/slime.svg` | Zwykły wróg: gniewne brwi, kieł, cień pod spodem. |
| `art/items/serce_otchlani.svg` | Ikona przedmiotu. |
| `art/projectiles/kula_fiolet.svg` | Pocisk wroga. |
| `art/glyphs/xbox_a.svg` | Przycisk pada. |

Podgląd tak, jak renderuje gra:

```
godot --headless --path . --script res://tools/render_svgs.gd -- podglad.png 256 art/characters
```

## Zasady techniczne SVG

Pliki SVG renderuje Godot (silnik ThorVG). Dozwolone są tylko proste elementy:

- **Elementy:** `path`, `circle`, `ellipse`, `rect`, `polygon`, `line`, `g`, `transform`.
- **Atrybuty:** `fill`, `stroke`, `stroke-width`, `stroke-linejoin="round"`, `stroke-linecap="round"`, `opacity`.
- **Zakazane:**
  - `<text>` (gra nie ma tych fontów w SVG);
  - filtry, rozmycia i `<style>`/CSS;
  - obrazy rastrowe i odwołania do innych plików;
  - gradienty (wyjątek: delikatny połysk z `opacity`);
  - `clipPath` i maski.
- **Nagłówek:** `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 W H" width="W" height="H">`, gdzie W i H to rozmiar płótna z tabeli niżej.
- **Pierwszy element:** komentarz z nazwą obiektu (po polsku) i jego rolą.
- **Nazwy plików:** małe litery ASCII, podkreślenia, bez polskich znaków, np. `lowczyni.svg`, `kula_ognia.svg`.
- **Rysunek nie może dotykać krawędzi płótna:** zostaw ≥6% marginesu, żeby kontur nie był ucięty.

## Kontur, cień, połysk

- **Kontur:** kolor `#1a0f1f` (prawie czarny fiolet), zawsze zaokrąglone łączenia.

  | Płótno | Grubość konturu |
  |---|---|
  | 256 | 10 |
  | 192 | 9 |
  | 128 | 7 |
  | 64 | 4–5 |
  | 48 | 5 |

  Szczegóły wewnętrzne (oczy, usta, paski) mają kontur o ~30% cieńszy.
- **Kolejność warstw** (jak we wzorcach):
  1. cień na ziemi (opcjonalnie, czarna elipsa `opacity 0.25`);
  2. stopy i elementy z tyłu;
  3. wypełnienie ciała;
  4. **cień cel-shading**: kształt w ciemniejszym odcieniu (ok. −20% jasności) przy prawej dolnej krawędzi, bez konturu;
  5. **połysk**: biała elipsa `opacity 0.45–0.7` przy lewej górnej krawędzi;
  6. **obrys ciała**: ta sama ścieżka co ciało, `fill="none"` i kontur. Dzięki temu cień nie zjada konturu;
  7. akcesoria, twarz i oczy.
- **Światło** zawsze pada z lewej góry.

## Twarze

- **Oczy:** białe elipsy z konturem i czarne źrenice przesunięte w kierunku patrzenia (w prawo), z małym białym błyskiem.
- **Bohaterowie:** przyjazne miny, mały uśmiech, opcjonalnie rumieńce (`#ff7fa0`, `opacity 0.55`).
- **Wrogowie:** gniewne brwi (dwie grube kreski skośnie do środka), zęby i kły, zmrużone oczy. Mogą być śmieszni, ale wyraźnie wrodzy.
- **Bossowie:** większe, dziwniejsze twarze. Wiele oczu, korony i rogi są w porządku.

## Kierunek i punkt obrotu

- Postacie i wrogowie **patrzą w prawo**. Gra odbija sprite w poziomie, gdy celują w lewo.
- **Bronie** są rysowane poziomo, ostrzem lub lufą w prawo.
- Punkt chwytu (pivot) zapisz w drugim komentarzu: `<!-- pivot: x,y -->`, we współrzędnych płótna. Wokół tego punktu gra obraca broń.

## Płótna i skala w grze

Gra działa w rozdzielczości bazowej 1920×1080 i skaluje się do monitora.

| Kategoria | Płótno | Wielkość w grze (1080p) | Uwagi |
|---|---|---|---|
| Bohater (`characters/`) | 256×256 | ok. 72 px | Ciało (fasolka ok. 155×155) wyśrodkowane, stopy na dole. |
| Broń (`weapons/`) | 192×96 | ok. 54 px długości | Pivot przy rękojeści. |
| Zwykły wróg (`enemies/`) | 192×192 | 48–70 px | Sylwetka wyraźnie inna niż u każdego innego wroga. |
| Duży wróg / elita | 256×256 | 80–100 px | |
| Boss (`bosses/`) | 512×512 | 220–300 px | Można dzielić na części: `_body`, `_eye` itd. |
| Ikona przedmiotu (`items/`) | 128×128 | 48–96 px | Obiekt mieści się w 12..116. Bez tła i ramki (ramkę rzadkości rysuje UI). |
| Pocisk (`projectiles/`) | 48×48 (lub 64×32 podłużny) | 14–24 px | Biały rdzeń, kontur. Podłużne lecą w prawo. |
| Znajdźka (`pickups/`) | 64×64 | 20–32 px | Moneta, serce, kryształ XP, klucz. |
| Przeszkoda (`props/`) | 128×128 | 48–96 px | Kamienie, beczki, skrzynie, kości, filary. Cień na ziemi. |
| Kafel podłogi (`tiles/`) | 128×128 | 1:1 | **Bezszwowy**, niski kontrast, bez konturów, stonowany. |
| Glif sterowania (`glyphs/`) | 64×64 | 28–40 px | Pad Xbox, klawisze, mysz. |
| Ikona UI (`ui/`) | 64×64 | 24–48 px | Statystyki: HP, obrażenia, szybkość, szansa krytyczna itd. |

## Paleta

Kolory podstawowe (wypełnienie / cień):

| Nazwa | Wypełnienie | Cień |
|---|---|---|
| kontur | `#1a0f1f` | |
| biel | `#ffffff` | |
| niebieski (Rycerz) | `#5b9cff` | `#3f75d1` |
| stal | `#c8d2e6` | `#9aa6c2` |
| czerwony | `#ff4d5e` | `#c9303f` |
| różowy (serce) | `#ff4d6d` | `#d42e52` |
| zielony (szlam) | `#7ee05a` | `#4fb33a` |
| leśny | `#3fbf7f` | `#2a8f5c` |
| fiolet | `#a066ff` | `#7442d1` |
| magenta | `#e04fd6` | `#a92ea1` |
| pomarańcz | `#ff9a3c` | `#d9701b` |
| złoto | `#ffd23f` | `#d9a514` |
| brąz / drewno | `#b97a4a` | `#8a5530` |
| kość | `#f1e6c8` | `#c9b98f` |
| skóra ciepła | `#ffcf9e` | `#e0a172` |
| szary kamień | `#8e8aa6` | `#66627d` |
| ciemny fiolet (Upadły) | `#7a4fb8` | `#55348a` |
| turkus | `#3fd6d0` | `#23a5a0` |

Zasady kolorów:
- Bohaterowie mają kolor ciała z karty postaci. Musi być **jasny i nasycony**, bo za ciemne postaci są nieczytelne na tle podłogi (to był błąd w wersji 1.1).
- Pociski i zagrożenia wrogów: magenta, fiolet, pomarańcz, czerwień.
- Pociski gracza: cyjan, biel, złoto. Gracz musi od razu odróżnić je od pocisków wrogów.
- Podłogi i ściany: stonowane i ciemniejsze (jasność ≤ 40%), żeby postacie i pociski się wybijały.

## Animacja

Gra nie używa klatek animacji. Ruch jest proceduralny:
- podskok i zgniecenie przy chodzeniu;
- oddychanie (skala Y ±4%);
- przechył przy zrywie;
- biały błysk przy trafieniu;
- „pop” przy śmierci.

Wynika z tego, że:
- jeden SVG to jedna poza;
- elementy, które mają się ruszać niezależnie (oko bossa, skrzydła), rysuj jako **osobne pliki** z sufiksem (`lich_body.svg`, `lich_hand.svg`) na tym samym płótnie i we wspólnym układzie współrzędnych.

## Kontrola jakości przed oddaniem

1. Wyrenderuj skryptem `tools/render_svgs.gd` w pełnym rozmiarze i w miniaturze (komórka 64 lub 96).
2. Sprawdź w miniaturze:
   - Czy sylwetkę da się rozpoznać? Czy wróg nie jest mylony z innym wrogiem?
   - Czy kontur jest ciągły?
   - Czy nic nie jest ucięte przy krawędzi?
   - Czy styl pasuje do wzorców obok?
