using System;
using System.Collections.Generic;
using System.Linq;
using Otchlan.Core;

namespace Otchlan.Sim;

/// <summary>Ulepszenie wylosowane przy awansie (konkretna wartość i rzadkość).</summary>
public sealed class Upgrade
{
    public UpgradeDef Def;
    public int Rarity;
    public float Value;
    public string Key => Def.Key;
    public string Name => Def.Name;
    public string Desc => Def.Desc.Replace("{v}", Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>Losowanie przedmiotów i ulepszeń (port items.py).</summary>
public static class Items
{
    /// <summary>Głębokość „efektywna” dla rzadkości – przy 40 piętrach rośnie wolniej niż w wersji 5-piętrowej.</summary>
    public static float RarityDepth(int depth) => Math.Min(12f, 1 + (depth - 1) / 2.5f);

    public static float[] RarityWeights(float luck, float depth)
    {
        var w = new[] { 62f, 27f, 9f, 2f };
        w[0] -= luck * 0.4f + depth * 3;
        w[1] += luck * 0.2f + depth * 1.5f;
        w[2] += luck * 0.15f + depth * 1.0f;
        w[3] += luck * 0.05f + depth * 0.5f;
        w[0] = MathF.Max(10, w[0]);
        return w;
    }

    public static int RollRarity(Rng rng, float luck, float depth, int minRarity = 0)
    {
        var w = RarityWeights(luck, depth);
        for (int i = 0; i < minRarity; i++) w[i] = 0;
        return rng.Weighted(w);
    }

    /// <summary>Losuje przedmiot, którego nikt jeszcze nie ma (gdy pula się wyczerpie – dowolny).</summary>
    public static ItemDef RollItem(Rng rng, ICollection<string> taken, float luck, int depth, int minRarity = 0)
    {
        var all = Content.Db.Items;
        int rar = RollRarity(rng, luck, RarityDepth(depth), minRarity);
        foreach (int delta in new[] { 0, -1, 1, -2, 2, -3, 3 })
        {
            int r = rar + delta;
            if (r < minRarity || r > 3) continue;
            var cands = all.Where(it => it.Rarity == r && !taken.Contains(it.Key)).ToList();
            if (cands.Count > 0)
                return rng.Pick(cands);
        }
        return rng.Pick(all.Where(it => it.Rarity >= minRarity).ToList());
    }

    public static int Price(ItemDef item, int depth, float priceMult)
    {
        float baseP = new[] { 12f, 18f, 27f, 38f }[item.Rarity];
        return Math.Max(3, (int)MathF.Round(baseP * (1 + 0.03f * (depth - 1)) * priceMult));
    }

    /// <summary>Minimalna rzadkość ulepszeń przy awansie: co 5. poziom niezwykłe, co 10. rzadkie.</summary>
    public static int UpgradeMinRarity(int level) =>
        level % (Balance.GuaranteeEvery * 2) == 0 ? 2 : level % Balance.GuaranteeEvery == 0 ? 1 : 0;

    public static List<Upgrade> RollUpgrades(Rng rng, float luck, int level, int n = 3, ICollection<string> banned = null, ICollection<string> exclude = null)
    {
        var pool = Content.Db.Upgrades.Where(u => (banned == null || !banned.Contains(u.Key)) && (exclude == null || !exclude.Contains(u.Key))).ToList();
        if (pool.Count < n)
            pool = Content.Db.Upgrades.Where(u => banned == null || !banned.Contains(u.Key)).ToList();
        rng.Shuffle(pool);
        int minR = UpgradeMinRarity(level);
        var outL = new List<Upgrade>();
        foreach (var up in pool.Take(n))
        {
            int rar = RollRarity(rng, luck, 1 + level / 3f, minR);
            outL.Add(new Upgrade { Def = up, Rarity = rar, Value = up.Values[Math.Clamp(rar, 0, up.Values.Length - 1)] });
        }
        return outL;
    }

    public static void ApplyUpgrade(PlayerSim p, Upgrade up)
    {
        float cur = p.GetStat(up.Def.Stat), v = up.Value;
        switch (up.Def.Op)
        {
            case "%": p.SetStat(up.Def.Stat, cur * (1 + v / 100f)); break;
            case "-%": p.SetStat(up.Def.Stat, cur * (1 - v / 100f)); break;
            case "p": p.SetStat(up.Def.Stat, cur + v / 100f); break;
            default:
                p.SetStat(up.Def.Stat, cur + v);
                if (up.Def.Stat == "max_hp") p.Hp += v;
                break;
        }
        p.UpgradeLog.Add($"{up.Key}:{up.Rarity}");
        p.ClampStats();
    }
}
