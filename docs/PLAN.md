# Plan: Otchłań 2.0

## Cele

| Obszar | Co ma się zmienić |
|---|---|
| Silnik | Godot 4.7.2 (.NET, C#) zamiast Pythona: dalszy rozwój i standardowe narzędzia. |
| Ekran | FPS powyżej 60, pełny ekran, skalowanie do 16:9, 16:10, 21:9 i 4:3. |
| Grafika | Kreskówkowa, w stylu Brotato (wektorowa, ostra nawet w 4K). |
| Pady | Gdy gra wykryje pada, podpowiedzi pokazują przyciski Xboxa. |
| Przedmioty | Branie przedmiotu przyciskiem, menu sklepu. |
| Zawartość | 40 pięter, po nich tryb nieskończony „Głębia”; 3 sloty zapisu; nowe postacie i bossowie. |

## Decyzje projektowe

- **40 pięter** to 8 krain po 5 pięter. Boss pojawia się co 5. piętro, a w środku każdej krainy czeka elita albo mini-boss.

  | Piętra | Kraina | Boss |
  |---|---|---|
  | 1–5 | Piwnice | Król Glutów |
  | 6–10 | Katakumby | Licz |
  | 11–15 | Zalane Groty | Matka Roju |
  | 16–20 | Kuźnia | Władca Kuźni |
  | 21–25 | Grzybnia | *nowy:* Matka Grzybni |
  | 26–30 | Lodowa Krypta | *nowy:* Szronowy Kolos |
  | 31–35 | Mechanizm | *nowy:* Zegarmistrz |
  | 36–40 | Otchłań | Serce Otchłani |

- **Głębia** zaczyna się po 40. piętrze. Piętra generują się bez końca z mieszanych krain. Bossowie wracają z nowymi wzorami ataków i rosnącą siłą. Gra zapisuje rekord głębokości.
- **3 sloty zapisu**, w każdym jedna wyprawa. Zapis dzieje się sam przy wejściu do pokoju. Śmierć kasuje slot, bo to roguelike. Odblokowania i osiągnięcia są wspólne dla wszystkich slotów. Piętra są krótsze (5–8 pokoi).
- **5 nowych postaci**, razem 12:

  | Postać | Specjalność |
  |---|---|
  | Szaman | Przywołuje duchy. |
  | Inżynier | Stawia wieżyczki. |
  | Mnich | Walka wręcz i kombo zrywów. |
  | Bard | Aura wzmacniająca, dobra w co-op. |
  | Rewolwerowiec | Przeładowanie i serie strzałów. |

- **Około 30 nowych przedmiotów** i nowi wrogowie dla nowych krain.
- **Przedmioty:** nic nie podnosi się samo. Gracz dostaje podpowiedź „[E] Weź” albo „[X] Weź”. Sklepikarz otwiera menu sklepu z porównaniem statystyk.

## Etapy

1. **Fundament.** Projekt Godot C#, ekran i FPS, wejście z glifami Xbox, motyw UI, przewodnik stylu i grafika bazowa, testy, zrzuty ekranu. *(gotowe)*
2. **Port wersji 1.1.** Postacie, wrogowie z SI szukającą linii strzału, bossowie, przedmioty, co-op, sloty zapisu, sklep z menu. *(gotowe)*
3. **Nowa zawartość.** 40 pięter, Głębia, 3 nowych bossów (Matka Grzybni, Szronowy Kolos, Zegarmistrz), 5 nowych postaci, 30 nowych przedmiotów, wrogowie przebarwieni pod krainę, muzyka. *(gotowe; nowi wrogowie dla nowych krain – opcjonalnie)*
4. **Wydanie.** Balans z symulatora *(zrobiony pierwszy przebieg)*, nowe osiągnięcia *(gotowe: 71, ekran i toast, interfejs pod Steam)*, Steam (Steamworks.NET), eksport na Windows, testy końcowe.

### Balans (symulator `--balance`, bot nieśmiertelny, 8 postaci)

| Piętro | Czas piętra | Walka z bossem | Uwagi |
|---|---|---|---|
| 5 | ~90 s | ~25 s | najtrudniejszy moment wczesnej gry |
| 10–35 | ~60 s | 24–30 s | moc gracza rośnie przez przedmioty, wrogowie od piętra 10 rosną szybciej (`LateHpFactor`) |
| 40 | ~55 s | ~35–40 s | finał: Serce Otchłani (`hp_mult` 1.5) |
| 41+ | 25–50 s | – | Głębia: dodatkowy wzrost HP/obrażeń wrogów na piętro |

Limit DPS bossa: 50·1.07^(piętro−5) na sekundę, nadmiar zadaje 8% obrażeń.

## Podział pracy

Opus projektuje i robi rzeczy najtrudniejsze, a resztę deleguje do agentów Sonnet i odbiera ich wyniki.

| # | Zadanie | Trudność | Kto |
|---|---|---|---|
| 1 | Architektura i rdzeń gry: pokoje, kolizje, SI z linią strzału, co-op | bardzo wysoka | Opus |
| 2 | Bossowie: system faz i wzorów pocisków, 3 nowych bossów, wersje do Głębi | wysoka | Opus: projekt i pierwszy boss; Sonnet: kolejni według wzoru |
| 3 | 40 pięter i Głębia: generator, krzywe trudności, ekonomia | wysoka | Opus |
| 4 | Sloty zapisu z wersjonowaniem, Steam | średnio-wysoka | Opus |
| 5 | Grafika (ok. 150 elementów) | średnia, dużo pracy | Opus: przewodnik i wzorce; Sonnet: partie grafik |
| 6 | Port przedmiotów i wrogów z Pythona | średnia | Sonnet według wzorca od Opusa |
| 7 | Nowe postacie i przedmioty | średnia | Opus: projekt; Sonnet: wykonanie |
| 8 | UI: menu, sloty, sklep, opcje, glify | średnia | Sonnet według makiet od Opusa |
| 9 | Testy, bot, symulator balansu | średnia | Opus: infrastruktura; Sonnet: testy |
| 10 | Balans, QA, przegląd kodu | średnia | Agenci Sonnet; poprawki Opus |
| 11 | Osiągnięcia, teksty, dokumentacja | niska | Sonnet |

## Poprawki z QA wersji 1.1 do uwzględnienia w porcie

- Zapis musi pamiętać powalonego gracza w co-op. W 1.1 „Zapisz i wyjdź” go wskrzeszało.
- Kolory postaci muszą być jasne: Upadły i Złodziej były za ciemne.
- HUD gracza 2 nie może nachodzić na inne elementy. Tytuł pauzy nie może zasłaniać HUD.
- Komunikaty nie mogą prześwitywać przez ekran awansu. Na ekranie wyniku potrzebny jest wskaźnik „+N” dla przedmiotów, które się nie mieszczą.
- Fonty muszą mieć wszystkie używane znaki. Teraz używamy Titan One i Baloo 2 z pełnym polskim.
