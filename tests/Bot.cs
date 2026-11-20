using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Otchlan.Core;
using Otchlan.Sim;

namespace Otchlan.Tests;

/// <summary>
/// Prosty bot do testów i symulacji balansu: walczy (dystans + unikanie pocisków), zbiera przedmioty,
/// zwiedza piętro i schodzi włazem. Steruje symulacją tak jak gracz (przez SimInput).
/// </summary>
public sealed class Bot
{
    private readonly RunSim _g;
    private readonly PlayerSim _p;
    private readonly Rng _rng;
    public float Skill = 1f;
    private Vector2 _strafe = Vector2.Right;
    private float _strafeT;
    private int[,] _flow;
    private (int, int) _flowCell = (-1, -1);
    private float _interactCd;

    public Bot(RunSim g, PlayerSim p, ulong seed)
    {
        _g = g;
        _p = p;
        _rng = new Rng(seed);
    }

    private Vector2 Pos => new(_p.X, _p.Y);

    private Vector2 WalkTo(Vector2 target)
    {
        var room = _g.Room;
        if (room.Los(_p.X, _p.Y, target.X, target.Y))
            return (target - Pos).Normalized();
        var cell = (Math.Clamp(Room.ColOf(target.X), 0, C.GCols - 1), Math.Clamp(Room.RowOf(target.Y), 0, C.GRows - 1));
        if (cell != _flowCell || _flow == null)
        {
            _flow = room.FlowField(target.X, target.Y);
            _flowCell = cell;
        }
        int c = Math.Clamp(Room.ColOf(_p.X), 0, C.GCols - 1), r = Math.Clamp(Room.RowOf(_p.Y), 0, C.GRows - 1);
        int best = _flow[r, c];
        Vector2 dir = (target - Pos).Normalized();
        foreach (var (dc, dr) in Room.Steps8)
        {
            int nc = c + dc, nr = r + dr;
            if (nc < 0 || nc >= C.GCols || nr < 0 || nr >= C.GRows) continue;
            if (dc != 0 && dr != 0 && (room.Solid(c + dc, r) || room.Solid(c, r + dr))) continue;
            if (_flow[nr, nc] < best)
            {
                best = _flow[nr, nc];
                dir = (Room.CellCenter(nc, nr) - Pos).Normalized();
            }
        }
        return dir;
    }

    /// <summary>Kierunek do drzwi prowadzących najkrótszą drogą do najbliższego nieodwiedzonego pokoju (albo wyjścia).</summary>
    private Vector2? ExploreTarget()
    {
        var floor = _g.Floor;
        var start = _g.Room;
        var prev = new Dictionary<Room, (Room room, Dir dir)>();
        var q = new Queue<Room>();
        q.Enqueue(start);
        var seen = new HashSet<Room> { start };
        Room goal = null;
        while (q.Count > 0)
        {
            var r = q.Dequeue();
            if (r != start && (!r.Visited || r.Kind is RoomKind.Boss or RoomKind.Exit && !r.Cleared))
            {
                goal = r;
                break;
            }
            foreach (var (d, n) in r.Doors)
                if (seen.Add(n))
                {
                    prev[n] = (r, d);
                    q.Enqueue(n);
                }
        }
        if (goal == null)
        {
            // wszystko odwiedzone – idź do pokoju z włazem
            goal = floor.Rooms.Values.FirstOrDefault(r => r.Trapdoor != null);
            if (goal == null || goal == start) return null;
            q.Clear();
            prev.Clear();
            seen = new HashSet<Room> { start };
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var r = q.Dequeue();
                foreach (var (d, n) in r.Doors)
                    if (seen.Add(n)) { prev[n] = (r, d); q.Enqueue(n); }
            }
        }
        var step = goal;
        while (prev.TryGetValue(step, out var pr) && pr.room != start)
            step = pr.room;
        if (!prev.TryGetValue(step, out var first)) return null;
        var dir = first.dir;
        var door = Dirs.DoorPos(dir);
        var (dx, dy) = Dirs.Delta(dir);
        return door + new Vector2(dx, dy) * 30;
    }

    public SimInput Input()
    {
        var inp = new SimInput();
        var g = _g;
        _interactCd -= g.FrameDt;
        if (!_p.Active)
            return inp;
        var enemy = g.NearestEnemy(_p.X, _p.Y);
        Vector2 move = Vector2.Zero;
        if (enemy != null)
        {
            var ep = new Vector2(enemy.X, enemy.Y);
            float d = Pos.DistanceTo(ep);
            inp.Aim = (ep - Pos).Angle();
            inp.Fire = true;
            inp.Target = ep;
            float want = enemy.IsBoss ? 260 : 200;
            if (d > want + 60) move = WalkTo(ep);
            else if (d < want - 40) move = (Pos - ep).Normalized();
            _strafeT -= g.FrameDt;
            if (_strafeT <= 0)
            {
                _strafeT = _rng.Range(0.6f, 1.6f);
                _strafe = _rng.Chance(0.5f) ? Vector2.Right : Vector2.Left;
            }
            var perp = (ep - Pos).Normalized().Orthogonal() * (_strafe.X > 0 ? 1 : -1);
            move += perp * 0.6f;
            // unik pocisków
            foreach (var b in g.Bullets)
            {
                if (b.Friendly) continue;
                var bp = new Vector2(b.X, b.Y);
                float bd = bp.DistanceTo(Pos);
                if (bd > 120 * Skill) continue;
                var bv = new Vector2(b.Vx, b.Vy);
                if (bv.Dot(Pos - bp) <= 0) continue;
                move += (Pos - bp).Normalized().Orthogonal() * (bv.Cross(Pos - bp) > 0 ? -1 : 1) * 1.4f * Skill;
                if (bd < 50 && _p.DashLeft > 0 && _rng.Chance(0.15f * Skill)) inp.Dash = true;
            }
            if (_p.AbilityT <= 0 && d < 260) inp.Ability = true;
        }
        else if (g.Room.Pedestals.Count > 0 && g.Room.Pedestals.Any(pd => pd.Kind == PedestalKind.Item || pd.Kind == PedestalKind.Shop && g.Coins >= pd.Price))
        {
            var pd = g.Room.Pedestals.Where(pd => pd.Kind == PedestalKind.Item || pd.Kind == PedestalKind.Shop && g.Coins >= pd.Price)
                .OrderBy(pd => new Vector2(pd.X, pd.Y).DistanceTo(Pos)).First();
            var tp = new Vector2(pd.X, pd.Y + 6);
            move = WalkTo(tp);
            if (Pos.DistanceTo(tp) < 40 && _interactCd <= 0)
            {
                inp.Interact = true;
                _interactCd = 0.3f;
            }
        }
        else if (g.Room.Pickups.Any(pk => pk.Kind == PickupKind.Heart && _p.Hp < _p.MaxHp || pk.Kind == PickupKind.Chest))
        {
            var pk = g.Room.Pickups.First(pk => pk.Kind == PickupKind.Heart && _p.Hp < _p.MaxHp || pk.Kind == PickupKind.Chest);
            move = WalkTo(new Vector2(pk.X, pk.Y));
        }
        else if (g.Room.Trapdoor is { } td && g.TrapDelay <= 0 && !g.Room.Pickups.Any(pk => pk.Kind is PickupKind.Coin or PickupKind.Xp))
            move = WalkTo(td);
        else if (g.Room.Cleared && !g.Room.Pickups.Any(pk => pk.Kind is PickupKind.Coin or PickupKind.Xp))
        {
            var t = ExploreTarget();
            if (t != null) move = WalkTo(t.Value);
        }
        if (move.LengthSquared() > 1) move = move.Normalized();
        inp.Move = move;
        return inp;
    }
}
