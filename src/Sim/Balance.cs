using System;

namespace Otchlan.Sim;

/// <summary>
/// Wszystkie liczby balansu w jednym miejscu. Piętra: 1..40 (8 krain × 5), potem Głębia (41+).
/// Wartości startowe – do strojenia symulatorem (etap 4).
/// </summary>
public static class Balance
{
    // --- wzrost siły wrogów z piętrem (wykładniczo, bo gracz też rośnie wykładniczo przez przedmioty) ---
    public const float EnemyHpGrowth = 1.085f;   // było 1.07 – moc gracza (przedmioty + awanse) rośnie szybciej     // piętro 10: ×1.8, 20: ×3.6, 40: ×14
    public const float EnemyDmgGrowth = 1.03f;    // piętro 40: ×3.2
    public const float DeepExtraGrowth = 1.04f;   // dodatkowo w Głębi (41+), na piętro
    // Symulator balansu (--balance): moc gracza rośnie ok. ×4.6 do piętra 10, ×11 do 20, ×27 do 30, ×115 do 40
    // (przedmioty mnożą się), więc od 10. piętra wrogowie rosną szybciej, żeby późne krainy nie były łatwiejsze od wczesnych.
    public const float MidHpGrowth = 1.025f;      // od piętra 10 (bossowie)
    public const float LateHpGrowth = 1.02f;      // od piętra 25 (bossowie)
    // Zwykli wrogowie rosną szybciej niż bossowie: bot „ludzki” czyścił pokoje późnych krain w 4–5 s bez straty HP.
    public const float EnemyMidHpGrowth = 1.035f;
    public const float EnemyLateHpGrowth = 1.03f;

    /// <summary>Dodatkowy mnożnik HP bossów w środkowych i późnych krainach.</summary>
    public static float LateHpFactor(int depth) =>
        MathF.Pow(MidHpGrowth, Math.Max(0, depth - 10)) * MathF.Pow(LateHpGrowth, Math.Max(0, depth - 25));

    /// <summary>Dodatkowy mnożnik HP zwykłych wrogów w środkowych i późnych krainach.</summary>
    public static float EnemyLateHpFactor(int depth) =>
        MathF.Pow(EnemyMidHpGrowth, Math.Max(0, depth - 10)) * MathF.Pow(EnemyLateHpGrowth, Math.Max(0, depth - 25));

    public static float EnemyHpScale(int depth)
    {
        float s = MathF.Pow(EnemyHpGrowth, depth - 1) * EnemyLateHpFactor(depth);
        if (depth > C.StoryFloors)
            s *= MathF.Pow(DeepExtraGrowth, depth - C.StoryFloors);
        return s;
    }

    public static float EnemyDmgScale(int depth)
    {
        float s = MathF.Pow(EnemyDmgGrowth, depth - 1);
        if (depth > C.StoryFloors)
            s *= MathF.Pow(1.02f, depth - C.StoryFloors);
        return s;
    }

    public static float EnemySpeedScale(int depth) => Math.Min(1.25f, 1 + 0.005f * (depth - 1));
    public static float EnemyFireScale(int depth) => Math.Min(1.35f, 1 + 0.007f * (depth - 1));

    public const float EliteHp = 2.3f;
    public const float EliteChancePerFloor = 0.004f;
    public const float SpawnGrace = 0.8f;

    // --- liczba wrogów w pokoju (budżet „kosztu” wrogów) ---
    public const float RoomBudgetBase = 8f;      // hordy jak w Brotato (było 4.5, potem 6)
    public const float RoomBudgetRandom = 4f;
    public const float RoomBudgetPerFloor = 2f;  // wzrost w obrębie krainy
    /// <summary>Kolejna fala wchodzi, gdy z bieżącej zostało tyle (ułamek) – fale nachodzą na siebie, presja jest ciągła.</summary>
    public const float NextWaveAt = 0.3f;

    public static float RoomBudget(int depth, float rnd01, int roomDist)
    {
        int inBiome = (depth - 1) % C.FloorsPerBiome;          // 0..4 – rośnie w obrębie krainy
        int biome = (depth - 1) / C.FloorsPerBiome;            // 0..7 – i lekko między krainami
        float b = RoomBudgetBase + RoomBudgetPerFloor * inBiome + 0.8f * Math.Min(biome, 10) + RoomBudgetRandom * rnd01 + Math.Min(3, roomDist) * 0.4f;
        return Math.Min(b, 30f);
    }

    // --- bossowie ---
    public const float BossHpBase = 700f;
    public const float BossHpGrowth = 1.075f;
    public static float BossHp(int depth) => BossHpBase * MathF.Pow(BossHpGrowth, depth - 1) * LateHpFactor(depth) *
        (depth > C.StoryFloors ? MathF.Pow(DeepExtraGrowth, depth - C.StoryFloors) : 1);
    public static float BossTempo(int depth) => Math.Min(1.3f, 1 + 0.008f * (depth - 5));

    /// <summary>Limit obrażeń na sekundę zadawanych bossowi (model Enter the Gungeon) – żeby nie znikał w 3 s.</summary>
    public static float BossDpsCap(int depth) => 50f * MathF.Pow(1.07f, Math.Max(0, depth - 5));
    public const float BossCapWindow = 3.0f;
    // ---- przedmioty (wyniki tests/ItemBench: --item-bench) ----
    /// <summary>Obrażenia jednego pocisku przy n pociskach: łącznie 1 + 0.45·(n−1) zamiast n (dodatkowy pocisk ≈ +45%, a nie +100%).</summary>
    public static float ProjectileDmgMult(int n) => n <= 1 ? 1 : (1 + 0.45f * (n - 1)) / n;
    public const float PoisonDpsMult = 0.35f;     // trucizna: DPS = 35% obrażeń gracza na poziom
    public const float ChainDmgMult = 0.45f;      // piorun łańcuchowy: 45% obrażeń pocisku na skok
    public const float ExplodeDmgMult = 0.4f;     // wybuchowe pociski: 40% obrażeń w promieniu
    // ---- obrona i ekonomia (bot „ludzki”: w późnych krainach pokoje bez straty HP, ~100 przedmiotów na piętrze 40) ----
    public const float ArmorCap = 0.5f;           // było 0.7
    public const float DodgeCap = 0.4f;           // było 0.6
    public const float PriceGrowth = 0.05f;       // ceny w sklepie +5% na piętro (było 3%)
    public const float CoinPerKill = 0.8f;        // więcej wrogów w pokoju – mniej monet z każdego
    // ---- ołtarz krwi ----
    public const int ShrineMinDepth = 3;
    public const float ShrineChance = 0.25f;      // było 0.6 od piętra 2
    public const int ShrineHpCostPct = 40;        // % maks. HP zabierany z bieżącego życia
    public const float ShrineMaxHpLoss = 0.1f;    // i trwale -10% maks. HP

    public const float BossCapOverflow = 0.08f;   // nadmiar ponad limit (symulator: przy 0.25 boss 40. piętra padał w ~5 s)
    public const float BossCapMaxHit = 3.0f;
    public const float BossIntroArmor = 0.1f;

    // --- SI: żetony ataku (ilu wrogów naraz może strzelać / szarżować) ---
    public static readonly int[] AttackTokens = { 2, 3, 4, 5 };
    public static readonly int[] ChargeTokens = { 1, 1, 2, 2 };
    /// <summary>Głębiej więcej wrogów atakuje naraz: +1 żeton strzału co tyle pięter, +1 szarży co tyle.</summary>
    public const int AttackTokenEvery = 7, ChargeTokenEvery = 14;

    // --- awanse ---
    public const int GuaranteeEvery = 5;
    public const int BanishesPerRun = 2;
    public static float XpNeeded(int level) => 14 + level * 11;   // było 9: bot miał poziom ~58 na piętrze 40

    // --- co-op ---
    public const float CoopEnemyCount = 1.25f, CoopEnemyHp = 1.15f, CoopBossHp = 1.5f, CoopBossCap = 1.7f, CoopXp = 0.8f;
    public const int CoopTokens = 1;

    // --- ekonomia ---
    public static int ItemPrice(int rarity, int depth, float priceMult)
    {
        float baseP = rarity switch { 0 => 15, 1 => 25, 2 => 40, _ => 60 };
        float p = baseP * (1 + 0.06f * (depth - 1)) * priceMult;
        return Math.Max(3, (int)MathF.Round(p));
    }
}
