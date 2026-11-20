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

1. **Fundament.** Projekt Godot C#, ekran i FPS, wejście z glifami Xbox, motyw UI, przewodnik stylu i grafika bazowa, testy, zrzuty ekranu. *(w toku)*
2. **Port wersji 1.1.** Postacie, wrogowie z SI szukającą linii strzału, bossowie, przedmioty, co-op, sloty zapisu, sklep z menu, osiągnięcia.
3. **Nowa zawartość.** 40 pięter, Głębia, nowi bossowie, postacie, przedmioty i wrogowie.
4. **Wydanie.** Balans, nowe osiągnięcia, Steam (Steamworks.NET), eksport na Windows, testy końcowe.

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
