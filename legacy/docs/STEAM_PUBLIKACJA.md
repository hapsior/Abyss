# Otchłań na Steamie – jak wydać grę za opłatą

Instrukcja krok po kroku dla wersji 1.1. Stan wiedzy: październik 2026. Szczegóły i źródła są w `docs/research_steam.md`.

## Co jest gotowe, a co musisz zrobić Ty

| Gotowe w repozytorium | Do zrobienia przez Ciebie |
|---|---|
| Build Windows gotowy do wysłania (`tools/build_steam.py`). | Konto Steamworks, opłata 100 USD, dane podatkowe i bankowe. |
| Launcher `Otchlan.exe`: jeden proces, osadzony Python, bez instalatora. | Pobranie Steamworks SDK (plik `steam_api64.dll`). |
| 44 osiągnięcia działające lokalnie i przez Steam (ctypes, bez wrapperów C++). | Wpisanie osiągnięć w panelu Steamworks (lista i ikony są gotowe). |
| Zapis gry w `%APPDATA%\Otchlan\*.json`, pod Steam Cloud bez zmian w kodzie. | Włączenie Auto-Cloud w panelu (3 pola). |
| Skrypty SteamPipe (`build/steam/scripts/*.vdf`). | Wysłanie buildu przez `steamcmd` i wystawienie go na gałąź. |
| Szkice kapsuł sklepu, ikony, zrzuty z rozgrywki (`tools/export_steam_assets.py`). | Strona sklepu: opis, cena, ankieta treści. Docelowo kapsuła od grafika. |
| Autotest na docelowym PC: `Otchlan.exe --selftest`. | Test na czystym Windows i (opcjonalnie) na Steam Decku. |

## 1. Harmonogram

Minimalna droga od opłaty do premiery to około **7 tygodni**. Realny plan z festiwalem Next Fest:

| Kiedy | Krok |
|---|---|
| teraz | Rejestracja w Steamworks (partner.steamgames.com) i opłata **Steam Direct: 100 USD**. Opłata jest bezzwrotna, ale Valve ją oddaje po przekroczeniu 1000 USD przychodu. |
| +0…30 dni | Weryfikacja tożsamości, banku i podatków. **Formularz W-8BEN** z polskim TIN: umowa PL–USA daje 10% podatku u źródła zamiast 30%. VAT w UE odprowadza Valve. PIT w Polsce skonsultuj z księgowym. |
| +30 dni | Koniec karencji od opłaty. Valve podaje w różnych miejscach 21 albo 30 dni; bezpieczniej przyjąć 30. |
| koniec XI / XII 2026 | Strona **Coming Soon** publiczna i zbieranie wishlistów. Musi wisieć co najmniej 2 tygodnie przed premierą. Przegląd strony trwa 3–5 dni roboczych. |
| do **10.01.2027** | Zgłoszenie na **Steam Next Fest** (22.02–01.03.2027). Wymaga publicznego dema. Każda gra może wziąć udział tylko raz. |
| II 2027 | Przegląd buildu (3–5 dni roboczych, planuj 7) i demo na festiwal. |
| III 2027 | Premiera. Zniżka premierowa maksymalnie 40%, na 7–14 dni. |

## 2. Jak wygląda binarka

```
Otchlan/                       ← to jest zawartość depotu (build/steam/content, ok. 43 MB)
├── Otchlan.exe                ← launcher C (84 KB), budowany przez zig, ikona gry w zasobach
├── main.py                    ← punkt wejścia
├── game/*.pyc                 ← kod gry skompilowany do bajtkodu Pythona 3.12 (bez plików .py)
├── assets/fonts/              ← font Inter (licencja OFL)
├── python/                    ← Python 3.12.7 (python-build-standalone, okrojony) + pygame-ce 2.5.8
├── licenses/                  ← licencje Pythona, pygame-ce (LGPL) i fontu
└── steam_api64.dll            ← ze Steamworks SDK (dodawany parametrem --steam-sdk)
```

Dlaczego tak, a nie przez PyInstaller:

- **Jeden proces.** `Otchlan.exe` ładuje `python312.dll` i uruchamia grę w tym samym procesie (`Py_Main`). Steam widzi więc grę jako jeden proces. Czas gry liczy się poprawnie, Steam Cloud synchronizuje się dopiero po wyjściu z gry, a nakładka (Shift+Tab) podpina się pod okno gry. Launcher, który uruchamia drugi proces i od razu się zamyka, psuje wszystkie trzy rzeczy. Blender miał dokładnie ten problem.
- **Mniej fałszywych alarmów antywirusów.** Wersje „onefile” z PyInstallera rozpakowują się do katalogu tymczasowego i często są oznaczane jako podejrzane.
- **Szybki start i łatwe łatki.** Aktualizacja zmienia tylko kilka plików `.pyc`, więc Steam pobiera graczom kilkaset KB zamiast całej gry.

Sprawdzone na Twoim Windows 11: `Otchlan.exe --selftest` daje wynik OK, zapis gry działa, gra chodzi w 143 FPS (vsync).

## 3. Budowanie

Jednorazowo:

```
pip install ziglang pygame-ce
```

Następnie pobierz **Steamworks SDK** (Steamworks → SDK → Download) i rozpakuj, np. do `C:\steamworks_sdk`.

Build na Steam:

```
python tools/build_steam.py --steam-sdk C:\steamworks_sdk --appid <TWOJE_APP_ID>
```

- `--appid` zapisuje App ID w grze. Jeśli ktoś uruchomi exe z pominięciem Steama, gra poprosi Steama o restart przez `SteamAPI_RestartAppIfNecessary`. Wpisuje też App ID do skryptów VDF.
- `--depotid` domyślnie przyjmuje App ID + 1 (tak Steamworks nadaje pierwszy depot).
- `--dev` dodaje `steam_appid.txt` do lokalnego testowania poza Steamem. Ten plik **nigdy** nie trafia do depotu, bo skrypt go wyklucza.
- Wynik:
  - `build/steam/content` – depot;
  - `build/steam/scripts` – skrypty dla steamcmd;
  - `build/Otchlan-Windows-1.1.zip` – wersja bez Steama, np. dla testerów.

Grafiki i osiągnięcia do panelu:

```
python tools/export_steam_assets.py        # → build/steam_assets/
```

## 4. Testy przed wysłaniem

1. Na komputerze z zalogowanym Steamem zbuduj grę z `--dev` i uruchom `Otchlan.exe --selftest`. Plik `selftest.txt` powinien zawierać `Steam: OK`.
2. Odblokuj w grze pierwsze osiągnięcie, np. zabij pierwszego wroga („Pierwsza krew”). Powinien pojawić się popup Steama. Do resetu osiągnięć w trakcie testów służy w konsoli Steam (`steam://open/console`) komenda `achievement_clear <AppID> <API>`.
3. Sprawdź `Zapisz i wyjdź` → `Kontynuuj` oraz pada (Xbox lub PlayStation przez Steam Input).
4. Uruchom testy automatyczne (sekcja 9).

## 5. Konfiguracja w Steamworks (App Admin)

**Installation → General Installation:** Launch Option
- Executable: `Otchlan.exe`
- Operating System: Windows
- Arguments: puste

**Installation → Redistributables:** nic nie zaznaczaj. Python i pygame są w depocie, a launcher używa tylko bibliotek systemowych Windows (UCRT jest w Windows 10/11).

**SteamPipe → Depots:** jeden depot, `Operating System: Windows`, `Language: All languages`.

**Steam Cloud:**
1. Ustaw *Byte quota per user* = `1048576`, *Number of files per user* = `10` → Save.
2. W sekcji Auto-Cloud dodaj Root Path: `WinAppDataRoaming`, Subdirectory `Otchlan`, Pattern `*.json`, bez rekurencji.
3. Testuj w trybie „developers only”: w konsoli Steam wpisz `testappcloudpaths <AppID>`. Potem wyłącz ten tryb i opublikuj zmiany.

Wzorzec `*.json` obejmuje `save.json` (postępy, odblokowania, statystyki) i `run_save.json` (przerwana wyprawa). Pomija `crash.log`, `runs_log.jsonl` i pliki tymczasowe.

**Stats & Achievements → Achievements:**
- Dodaj 44 osiągnięcia z `build/steam_assets/achievements.md` (CSV jest obok). **API Name musi być identyczne.**
- Ikony: `achievements/<API>.png` (zdobyte) i `<API>_locked.png` (niezdobyte), 256×256.
- „Władca Otchłani” i zwycięstwo Upadłym oznacz jako ukryte (Hidden).
- Kliknij **Publish**.

Po każdej zmianie w panelu: **Publish → Prepare for Publishing → Publish to Steam**.

## 6. Wysłanie buildu (SteamPipe)

1. Utwórz osobne konto Steam tylko do buildów, z uprawnieniem „Edit App Metadata / Publish App Changes To Steam”. Valve to zaleca, bo konto z 2FA w CI jest kłopotliwe.
2. Pobierz `steamcmd` (jest w SDK: `tools/ContentBuilder/builder/steamcmd.exe`). Uruchom go raz, żeby się zaktualizował.
3. Wyślij build:
   ```
   steamcmd.exe +login <konto_buildowe> +run_app_build C:\...\otchlan\build\steam\scripts\app_build_<APPID>.vdf +quit
   ```
   Ścieżki w VDF są względne (`../content/`), więc folder `build/steam` można przenieść w dowolne miejsce.
4. W Steamworks przejdź do **SteamPipe → Builds**. Nowy build ustaw na gałęzi `default` (albo najpierw na `beta` z hasłem dla testerów) i opublikuj.
5. W bibliotece Steam zainstaluj grę jak gracz. Sprawdź, czy się uruchamia, czy działają osiągnięcia i czy po wyjściu działa synchronizacja w chmurze.

## 7. Strona sklepu

| Grafika | Rozmiar | Plik szkicu w `build/steam_assets/store/` |
|---|---|---|
| Header Capsule | 920×430 | `header_capsule_920x430.png` |
| Small Capsule | 462×174 | `small_capsule_462x174.png` |
| Main Capsule | 1232×706 | `main_capsule_1232x706.png` |
| Vertical Capsule | 748×896 | `vertical_capsule_748x896.png` |
| Page Background (opcjonalnie) | 1438×810 | `page_background_1438x810.png` |
| Library Capsule | 600×900 | `library_capsule_600x900.png` |
| Library Header | 920×430 | `library_header_920x430.png` |
| Library Hero (bez tekstu) | 3840×1240 | `library_hero_3840x1240.png` |
| Library Logo (przezroczyste) | 1280 szer. | `library_logo_1280w.png` |
| Shortcut Icon | 256×256 | `shortcut_icon_256.png` |
| App Icon | 184×184 | `app_icon_184.jpg` |
| Event Cover | 800×450 | `event_cover_800x450.png` |
| Zrzuty ekranu | min. 1920×1080, co najmniej 5 | `../screenshots/*.png` (prawdziwa rozgrywka) |

Zasady przeglądu:
- Na kapsułach może być tylko tytuł lub logo, bez cytatów i nagród.
- Zrzuty mogą pokazywać tylko rozgrywkę.
- W opisie nie może być linków zewnętrznych.
- Strona może wymieniać tylko funkcje, które istnieją w dniu premiery: osiągnięcia, Steam Cloud, pełne wsparcie pada, co-op na jednym ekranie (*Shared/Split Screen Co-op*, *Remote Play Together* działa automatycznie).

Kapsuły z eksportu to porządne szkice na start (Coming Soon, testy). **Main capsule najbardziej wpływa na kliknięcia w sklepie**, więc przed premierą warto zamówić ją u grafika.

Ankieta treści (Content Survey):
- Przemoc jest kreskówkowa i bez krwi realistycznej, czyli zwykle oznaczenie PEGI 7–12.
- **Ujawnianie AI:** kod i teksty gry powstały z pomocą AI, a szkice grafik sklepu wygenerował kod. Narzędzia do pomocy w pisaniu kodu nie trzeba deklarować. Teksty w grze (nazwy przedmiotów, opisy, osiągnięcia) są jednak treścią, którą widzi gracz, więc **zadeklaruj je jako „Pre-Generated”**. To uczciwe wobec graczy i zabezpiecza przed problemami w przeglądzie.

Cena: indie roguelike w tej skali (około 1–3 h na pierwsze zwycięstwo, 7 postaci, 5 pięter, co-op) mieszczą się zwykle w przedziale **4,99–9,99 USD**. Narzędzie *Propose Pricing* zaproponuje ceny regionalne w 37 walutach. To wskazówka rynkowa, nie porada finansowa.

## 8. Przed premierą – rekomendacje

1. **Angielska wersja językowa** to największa dźwignia sprzedaży. Gra jest teraz tylko po polsku, a angielski to większość rynku Steam. Teksty są w kilku plikach (`items.py`, `achievements.py`, `characters.py`, `scenes.py`), więc tłumaczenie to 1–2 dni pracy.
2. **Demo na Next Fest:** np. tylko piętra 1–2 i 3 postacie (flaga w `config.py`), jako osobna aplikacja „Demo” w Steamworks.
3. **Steam Deck:**
   - przetestuj grę pod Protonem;
   - dla oceny *Verified* podpowiedzi w grze przy grze padem muszą pokazywać przyciski pada, nie klawiatury;
   - gra działa w 1280×720 z czarnymi pasami na ekranie 1280×800, co jest dopuszczalne.
4. **Podpis cyfrowy exe** nie jest potrzebny na Steamie. Pomaga tylko przy wersji ZIP (SmartScreen).
5. **Aktualizacje:**
   - zmień `VERSION` w `game/config.py`, zbuduj, wyślij i ustaw build na `default`;
   - jeśli zmieniasz format zapisu wyprawy, podbij `SAVE_VERSION` w `game/savegame.py`; stary zapis zostanie wtedy bezpiecznie odrzucony zamiast wywracać grę.

## 9. Testy i agenci

| Narzędzie | Co robi |
|---|---|
| `python -m pytest -q tests` | Testy jednostkowe: zapis gry, balans, SI, osiągnięcia, co-op. |
| `python tools/bot_test.py [--quick]` | Boty przechodzą grę każdą postacią i w co-op, z zapisem i wczytaniem oraz zrzutami ekranu w `build/shots`. |
| `python tools/balance_sim.py --runs 10 [--diff 2] [--coop]` | Czas pokoi i bossów, DPS i utracone HP na każdym piętrze. Porównanie z `docs/balance/*.json`. |
| `Otchlan.exe --selftest` | Autotest prawdziwego buildu na docelowym komputerze. |
| `.claude/agents/` | Definicje agentów Claude Code: `code-reviewer`, `qa-playtester`, `balance-analyst`. W Claude Code: „użyj agenta qa-playtester, żeby sprawdzić build”. |

## Licencje w buildzie

| Komponent | Licencja | Co to znaczy dla gry |
|---|---|---|
| Python | PSF | Pozwala na dystrybucję. |
| pygame-ce | LGPL 2.1 | Biblioteka jest dołączona jako osobne, niezmienione pliki, więc można ją wymienić, a gra może być płatna i zamknięta. Wymagane: tekst licencji i link do źródeł (oba są w `licenses/`). |
| Font Inter | OFL | Pozwala na dystrybucję. |
| `steam_api64.dll` | Steamworks SDK | Valve pozwala go dołączać. |

Biblioteki SDL2 i inne zależności pygame-ce mają licencje zlib, FTL i podobne, które pozwalają dołączać je do gry.

## Źródła

- https://partner.steamgames.com/steamdirect
- https://partner.steamgames.com/doc/gettingstarted/onboarding
- https://partner.steamgames.com/doc/store/assets/standard
- https://partner.steamgames.com/doc/store/assets/libraryassets
- https://partner.steamgames.com/doc/store/review_process
- https://partner.steamgames.com/doc/gettingstarted/contentsurvey
- https://partner.steamgames.com/doc/sdk/uploading
- https://partner.steamgames.com/doc/features/achievements
- https://partner.steamgames.com/doc/features/cloud
- https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/feb_2027
- https://partner.steamgames.com/doc/steamdeck/compat
- https://partner.steamgames.com/doc/finance/taxfaq
- https://lists.blender.org/pipermail/bf-blender-cvs/2022-November/181181.html (launcher a licznik czasu gry)
