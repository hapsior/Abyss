using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Otchlan.Core;
using Otchlan.Sim;

namespace Otchlan.Save;

/// <summary>
/// Zapisuje telemetrię wypraw do %APPDATA%\Otchlan\telemetry\run_*.jsonl (jeden wiersz JSON na pokój).
/// Pliki są tylko lokalne – nic nie jest wysyłane. Trzyma 30 najnowszych wypraw.
/// </summary>
public static class TelemetryWriter
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string Dir => Path.Combine(JsonStore.UserDir, "telemetry");

    public static void Flush(RunTelemetry t)
    {
        if (t.Finished.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, $"run_{t.RunId}.jsonl");
            bool fresh = !File.Exists(path);
            using (var w = new StreamWriter(path, append: true))
                while (t.Finished.Count > 0)
                    w.WriteLine(JsonSerializer.Serialize(t.Finished.Dequeue(), Opt));
            if (fresh) Prune();
        }
        catch (Exception e)
        {
            Godot.GD.PushWarning($"Telemetria: {e.Message}");
            t.Finished.Clear();
        }
    }

    private static void Prune()
    {
        var files = new DirectoryInfo(Dir).GetFiles("run_*.jsonl").OrderByDescending(f => f.LastWriteTimeUtc).Skip(30);
        foreach (var f in files)
            try { f.Delete(); } catch { /* plik w użyciu – trudno */ }
    }
}
