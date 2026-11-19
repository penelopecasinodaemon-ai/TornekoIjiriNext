using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

internal static class Torneko4ActionStore
{
    private static readonly object Sync = new object();
    private static readonly Dictionary<int, ushort> Actions =
        new Dictionary<int, ushort>();

    public static void Set(int monsterId, ushort actionId)
    {
        if (monsterId < 0 || monsterId >= 199)
            throw new ArgumentOutOfRangeException(nameof(monsterId));

        if (actionId > 0xFF)
            throw new ArgumentOutOfRangeException(nameof(actionId));

        lock (Sync)
        {
            Actions[monsterId] = actionId;
        }
    }

    public static bool TryGet(int monsterId, out ushort actionId)
    {
        lock (Sync)
        {
            return Actions.TryGetValue(monsterId, out actionId);
        }
    }

    public static void Remove(int monsterId)
    {
        lock (Sync)
        {
            Actions.Remove(monsterId);
        }
    }

    public static Dictionary<int, ushort> GetCopy()
    {
        lock (Sync)
        {
            return new Dictionary<int, ushort>(Actions);
        }
    }

    public static void ReplaceAll(
        IDictionary<int, ushort> source)
    {
        lock (Sync)
        {
            Actions.Clear();

            if (source == null)
                return;

            foreach (var pair in source)
            {
                if (pair.Key < 0 || pair.Key >= 199)
                    throw new InvalidOperationException(
                        "不正なモンスター番号です: " + pair.Key);

                if (pair.Value > 0xFF)
                    throw new InvalidOperationException(
                        "不正な特技IDです: " + pair.Value);

                Actions[pair.Key] = pair.Value;
            }
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Actions.Clear();
        }
    }
}