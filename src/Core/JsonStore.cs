using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Otchlan.Core;

/// <summary>
/// Zapis i odczyt plików JSON w katalogu użytkownika (user:// → %APPDATA%\Otchlan na Windows).
/// Zapis jest atomowy: najpierw plik tymczasowy, potem podmiana – przerwany zapis nie psuje danych.
/// </summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Katalog danych gracza (zapisy, ustawienia, logi).</summary>
    public static string UserDir => ProjectSettings.GlobalizePath("user://");

    public static string PathOf(string fileName) => System.IO.Path.Combine(UserDir, fileName);

    public static bool Exists(string fileName) => File.Exists(PathOf(fileName));

    public static T Load<T>(string fileName) where T : class
    {
        var path = PathOf(fileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception e)
        {
            GD.PushWarning($"Nie udało się wczytać {fileName}: {e.Message}");
            // uszkodzony plik zostawiamy obok, żeby nie zginął bez śladu
            try { File.Copy(path, path + ".broken", true); } catch { /* ignoruj */ }
            return null;
        }
    }

    public static bool Save<T>(string fileName, T data)
    {
        var path = PathOf(fileName);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, path, true);
            return true;
        }
        catch (Exception e)
        {
            GD.PushError($"Nie udało się zapisać {fileName}: {e.Message}");
            return false;
        }
    }

    public static void Delete(string fileName)
    {
        try { File.Delete(PathOf(fileName)); } catch { /* ignoruj */ }
    }
}
