using System;
using System.Collections.Generic;

internal static class Torneko4GanirasuBarrierStore
{
    private static readonly object Sync = new object();
    private static bool[] Values = CreateDefaults();

    private static bool[] CreateDefaults()
    {
        bool[] result = new bool[199];

        int[] enabledIds =
        {
            197, 187, 198, 23, 188,
            114, 115, 116, 127, 128
        };

        foreach (int id in enabledIds)
            result[id] = true;

        return result;
    }

    public static bool Get(int monsterId)
    {
        if (monsterId < 0 || monsterId >= 199)
            throw new ArgumentOutOfRangeException(nameof(monsterId));

        lock (Sync)
        {
            return Values[monsterId];
        }
    }

    public static void Set(int monsterId, bool enabled)
    {
        if (monsterId < 0 || monsterId >= 199)
            throw new ArgumentOutOfRangeException(nameof(monsterId));

        lock (Sync)
        {
            Values[monsterId] = enabled;
        }
    }

    public static Dictionary<int, bool> GetCopy()
    {
        lock (Sync)
        {
            var result = new Dictionary<int, bool>();

            for (int i = 0; i < Values.Length; i++)
                result.Add(i, Values[i]);

            return result;
        }
    }

    public static void ReplaceAll(IDictionary<int, bool> settings)
    {
        lock (Sync)
        {
            Values = CreateDefaults();

            if (settings == null)
                return;

            foreach (var pair in settings)
            {
                if (pair.Key < 0 || pair.Key >= 199)
                    throw new ArgumentOutOfRangeException(
                        nameof(settings),
                        "モンスター番号が不正です。");

                Values[pair.Key] = pair.Value;
            }
        }
    }

    public static void Reset()
    {
        lock (Sync)
        {
            Values = CreateDefaults();
        }
    }
}