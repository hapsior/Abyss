using System;

namespace Otchlan.Core;

/// <summary>
/// Deterministyczny generator PCG32. Stan da się zapisać w pliku zapisu,
/// więc po wczytaniu wyprawy losowanie toczy się dokładnie tak samo.
/// </summary>
public sealed class Rng
{
    private ulong _state;
    private ulong _inc;

    public Rng(ulong seed, ulong stream = 54)
    {
        _state = 0;
        _inc = (stream << 1) | 1;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public Rng(RngState s)
    {
        _state = s.State;
        _inc = s.Inc;
    }

    public RngState GetState() => new() { State = _state, Inc = _inc };

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * 6364136223846793005UL + _inc);
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    /// <summary>Liczba z przedziału [0, 1).</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>Liczba całkowita z przedziału [min, max] (włącznie).</summary>
    public int RangeInt(int min, int max)
    {
        if (max <= min) return min;
        uint span = (uint)(max - min + 1);
        return min + (int)(NextUInt() % span);
    }

    public bool Chance(float p) => NextFloat() < p;

    public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> list) => list[RangeInt(0, list.Count - 1)];

    /// <summary>Losowanie ważone: weights[i] ≥ 0. Zwraca indeks.</summary>
    public int Weighted(System.Collections.Generic.IReadOnlyList<float> weights)
    {
        float total = 0;
        foreach (var w in weights) total += Math.Max(0, w);
        if (total <= 0) return RangeInt(0, weights.Count - 1);
        float r = NextFloat() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            r -= Math.Max(0, weights[i]);
            if (r < 0) return i;
        }
        return weights.Count - 1;
    }

    public void Shuffle<T>(System.Collections.Generic.IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = RangeInt(0, i);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Nowe ziarno z zegara – do nowych wypraw.</summary>
    public static ulong RandomSeed() => (ulong)DateTime.UtcNow.Ticks ^ (ulong)Environment.TickCount64 * 2654435761UL;
}

public struct RngState
{
    public ulong State;
    public ulong Inc;
}
