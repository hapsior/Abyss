using System;
using System.Collections.Generic;
using System.Linq;

namespace Otchlan.Sim;

/// <summary>Zapis jednego pokoju wyprawy – do porównania gry człowieka z botem (balans trudności).</summary>
public sealed class RoomLog
{
    public string Run { get; set; }
    public string Chars { get; set; }
    public int Diff { get; set; }
    public int Depth { get; set; }
    public string Biome { get; set; }
    public string Room { get; set; }
    public bool FirstVisit { get; set; }
    public float Seconds { get; set; }
    public float HpStart { get; set; }
    public float HpEnd { get; set; }
    public float MaxHp { get; set; }
    /// <summary>Obrażenia po pancerzu (to, co ubyło z paska życia).</summary>
    public float Damage { get; set; }
    public int Hits { get; set; }
    public int Dodged { get; set; }
    public int Blocked { get; set; }
    public int Kills { get; set; }
    public int Dashes { get; set; }
    public int Abilities { get; set; }
    public float Healed { get; set; }
    public int Level { get; set; }
    public int Items { get; set; }
    public int Coins { get; set; }
    /// <summary>Obrażenia według źródła (klucz wroga/bossa, „barrel”, „fire”…).</summary>
    public Dictionary<string, float> DamageBy { get; set; } = new();
    public Dictionary<string, int> HitsBy { get; set; } = new();
    /// <summary>Koniec pokoju: „cleared” (oczyszczony), „left” (wyjście drzwiami/włazem), „death”, „quit”.</summary>
    public string End { get; set; }
    public string DeathCause { get; set; }
}

/// <summary>
/// Telemetria wyprawy (tylko lokalnie): pokój po pokoju – czas, utracone HP, trafienia według źródła, zabójstwa, uniki.
/// Symulacja wypełnia <see cref="Finished"/>; widok dopisuje gotowe wpisy do pliku (user://telemetry/), bot – do CSV.
/// </summary>
public sealed class RunTelemetry
{
    public readonly Queue<RoomLog> Finished = new();
    public readonly List<RoomLog> All = new();
    private RoomLog _cur;
    private float _t0, _hp0;
    private int _kills0, _dashes0, _abil0;
    public string RunId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

    private static float TeamHp(RunSim g) => g.Players.Where(p => !p.Dead).Sum(p => p.Hp);

    public void BeginRoom(RunSim g, Room room, bool first)
    {
        EndRoom(g, "left");
        _cur = new RoomLog
        {
            Run = RunId,
            Chars = string.Join("+", g.Players.Select(p => p.Key)),
            Diff = g.DiffIdx,
            Depth = g.Depth,
            Biome = g.Floor?.Biome?.Key,
            Room = room.Kind.ToString(),
            FirstVisit = first,
            HpStart = TeamHp(g),
            MaxHp = g.Players.Sum(p => p.MaxHp),
        };
        _t0 = g.RunTime;
        _hp0 = TeamHp(g);
        _kills0 = g.Stats.Kills;
        _dashes0 = g.Stats.Dashes;
        _abil0 = g.Stats.Abilities;
    }

    public void Hit(int dmg, string source)
    {
        if (_cur == null) return;
        _cur.Damage += dmg;
        _cur.Hits++;
        source ??= "?";
        _cur.DamageBy[source] = _cur.DamageBy.GetValueOrDefault(source) + dmg;
        _cur.HitsBy[source] = _cur.HitsBy.GetValueOrDefault(source) + 1;
    }

    public void Dodge() { if (_cur != null) _cur.Dodged++; }
    public void Block() { if (_cur != null) _cur.Blocked++; }

    public void Death(string cause)
    {
        if (_cur != null) _cur.DeathCause = cause;
    }

    /// <summary>Zamyka bieżący pokój (wołane przy wejściu do następnego, przy śmierci i przy wyjściu z gry).</summary>
    public void EndRoom(RunSim g, string end)
    {
        if (_cur == null) return;
        var c = _cur;
        _cur = null;
        if (c.Room == nameof(RoomKind.Start) && c.Hits == 0 && end == "left" && g.RunTime - _t0 < 1) return;
        c.Seconds = g.RunTime - _t0;
        c.HpEnd = TeamHp(g);
        c.Healed = MathF.Max(0, c.HpEnd - (_hp0 - c.Damage));
        c.Kills = g.Stats.Kills - _kills0;
        c.Dashes = g.Stats.Dashes - _dashes0;
        c.Abilities = g.Stats.Abilities - _abil0;
        c.Level = g.Players.Max(p => p.Level);
        c.Items = g.Players.Sum(p => p.Items.Count);
        c.Coins = g.Coins;
        c.End = end == "left" && g.Room != null && g.Room.Cleared ? "cleared" : end;
        if (c.Seconds < 0.5f && c.Hits == 0 && c.Kills == 0) return;   // przejście przez pokój bez znaczenia
        Finished.Enqueue(c);
        All.Add(c);
    }
}
