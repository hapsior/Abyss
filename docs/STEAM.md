# Otchłań 2.0 na Steamie

Instrukcja dla wersji 2.0 (Godot 4.7.2 .NET). Kroki administracyjne (konto Steamworks, opłata, podatki, harmonogram, strona sklepu) są takie same jak dla wersji 1.1 i opisuje je [`legacy/docs/STEAM_PUBLIKACJA.md`](../legacy/docs/STEAM_PUBLIKACJA.md). Tu jest tylko to, co zmieniło się przez przejście na Godota.

## Co jest gotowe, a co zostało

| Gotowe w repozytorium | Do zrobienia |
|---|---|
| Preset eksportu Windows (`export_presets.cfg` → `build/windows/Otchlan.exe`). | Pobranie szablonów eksportu 4.7.2 mono w Godocie albo uruchomienie workflow w GitHub Actions. |
| Osiągnięcia w `data/achievements.json` (pole `api` = API Name w Steamworks) i interfejs platformy w `src/Platform/`. | Dodanie paczki **Steamworks.NET** i klasy `SteamPlatform` (opis niżej). |
| Zapisy w `%APPDATA%\Otchlan\*.json` (ustawienia, profil, 3 sloty). | Auto-Cloud w panelu Steamworks (te same 3 pola co w 1.1). |
| Pady: Xbox natywnie, PlayStation i inne przez Steam Input. | Test na czystym Windows i na Steam Decku (eksport Linux, opcjonalnie). |

## 1. Eksport gry

1. Godot 4.7.2 .NET → **Editor → Manage Export Templates → Download and Install** (raz).
2. **Project → Export → Windows Desktop → Export Project** (bez „Export With Debug”). Wynik: `build/windows/`:
   ```
   Otchlan.exe            ← gra
   Otchlan.pck            ← zasoby (grafika, dźwięk, dane)
   data_Otchlan_windows_x86_64/   ← skompilowany C# i środowisko .NET (dołączone, gracz nic nie instaluje)
   ```
3. Bez Godota na komputerze: workflow `.github/workflows/build.yml` buduje to samo po pushu do GitHuba i udostępnia artefakt `Otchlan-Windows`.

Godot uruchamia grę w jednym procesie, więc Steam poprawnie liczy czas gry, podpina nakładkę i synchronizuje chmurę po wyjściu.

## 2. Steamworks.NET (osiągnięcia)

Gra korzysta z interfejsu `IPlatform` (`Unlock(api)`, `SetStat`). Domyślnie działa `LocalPlatform`: osiągnięcia zapisują się tylko w profilu gracza. Żeby wysyłać je do Steama:

1. Do `Otchlan.csproj` dodaj paczkę:
   ```xml
   <PackageReference Include="Steamworks.NET" Version="<najnowsza z nuget.org>" />
   ```
   Plik `steam_api64.dll` pasujący do tej wersji (jest w paczce albo w Steamworks SDK) skopiuj obok `Otchlan.exe` w depocie.
2. Dodaj klasę `SteamPlatform : IPlatform`:
   - w konstruktorze `SteamAPI.Init()`; gdy zwróci `false`, użyj `LocalPlatform` (gra działa bez Steama);
   - `Unlock(api)` → `SteamUserStats.SetAchievement(api); SteamUserStats.StoreStats();`;
   - co klatkę `SteamAPI.RunCallbacks()` (np. w `Game._Process`), przy wyjściu `SteamAPI.Shutdown()`.
3. Do testów poza Steamem połóż obok exe plik `steam_appid.txt` z App ID. **Nie dodawaj go do depotu.**
4. W panelu Steamworks dodaj osiągnięcia z `data/achievements.json`: API Name = pole `api`, nazwa i opis po polsku, ukryte = pole `hidden`. Ikony 256×256 (zdobyte i niezdobyte).

## 3. Steam Cloud

Bez zmian względem 1.1: **Auto-Cloud**, Root `WinAppDataRoaming`, Subdirectory `Otchlan`, Pattern `*.json`, bez rekurencji. Obejmuje `settings.json`, `profile.json` i `slot_1..3.json`.

## 4. Depot i wysłanie buildu

- Launch Option: `Otchlan.exe`, Windows, bez argumentów. Redistributables: nic (środowisko .NET jest w buildzie).
- Zawartość depotu = cały katalog `build/windows/` + `steam_api64.dll`.
- Wysyłka przez `steamcmd` i skrypty VDF jak w 1.1 (`ContentRoot` = `build/windows`).

## 5. Testy przed wysłaniem

```
godot --headless --path . -- --run-tests          # testy automatyczne
godot --headless --path . -- --balance b.csv --chars rycerz,mag --floors 45   # symulator balansu
```

Na docelowym PC: nowa wyprawa w każdym slocie, „Zapisz i wyjdź” → „Kontynuuj”, pad Xbox (glify), monitor 21:9 i 4:3, 144 Hz z V-Sync i bez.
