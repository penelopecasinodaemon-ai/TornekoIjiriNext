using System.Buffers.Binary;

namespace TornekoIjiriNext;

internal sealed record SpawnTableSnapshot(string Name, byte[] Bytes);
internal sealed record SpawnDuplicate(int MonsterId, int TableCount, int OccurrenceCount, string Kind, string Locations);

internal static class MonsterSpawnDuplicateScanner
{
    public const int EntrySize = 6;

    public static IReadOnlyList<SpawnDuplicate> FindDuplicates(IEnumerable<SpawnTableSnapshot> snapshots)
    {
        var tables = snapshots.ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            if (!seen.Add(table.Name)) throw new InvalidDataException($"重複するテーブル名です: {table.Name}");
            if (table.Bytes.Length == 0 || table.Bytes.Length % EntrySize != 0)
                throw new InvalidDataException($"{table.Name}: データ長は6-byte entryの倍数である必要があります。");
        }

        var occurrences = new List<(int Id, string Table, int Row, int Level, int Cdf)>();
        foreach (var table in tables)
        {
            for (var offset = 0; offset < table.Bytes.Length; offset += EntrySize)
            {
                var span = table.Bytes.AsSpan(offset, EntrySize);
                occurrences.Add((BinaryPrimitives.ReadUInt16LittleEndian(span), table.Name, offset / EntrySize,
                    BinaryPrimitives.ReadUInt16LittleEndian(span[4..]), BinaryPrimitives.ReadUInt16LittleEndian(span[2..])));
            }
        }

        return occurrences.GroupBy(o => o.Id).Where(g => g.Count() > 1).OrderBy(g => g.Key).Select(g =>
        {
            var tableCount = g.Select(o => o.Table).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var locations = string.Join("; ", g.Select(o => $"{o.Table} / row {o.Row} / Lv {o.Level} / CDF {o.Cdf}"));
            return new SpawnDuplicate(g.Key, tableCount, g.Count(), tableCount > 1 ? "複数テーブル" : "同一テーブル内", locations);
        }).ToArray();
    }
}
