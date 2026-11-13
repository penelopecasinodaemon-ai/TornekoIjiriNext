namespace TornekoIjiriNext;

internal sealed record LegacySnapshotSegment(string Name, int Offset, int Length);

/// <summary>Read-only parser for the legacy 512 KiB memory snapshot container.</summary>
internal sealed class LegacyMemorySnapshotModel
{
    public const int SnapshotLength = 524_288;
    public const int VersionOffset = 355_056;
    public const int SegmentStartOffset = 355_072;
    private static readonly (byte MaxVersion, string Name, int Length)[] SegmentSchema =
    [
        (1, "var_150 region", 960), (1, "var_152 region", 64), (1, "var_154 region", 144),
        (1, "var_156 region", 8192), (1, "var_158 region", 11), (1, "var_160 region", 10),
        (1, "var_162 region", 10), (1, "var_164 region", 4), (1, "var_166 region", 512),
        (1, "var_168 region", 512), (2, "v2 companion flag", 1), (2, "var_170 region", 1),
        (3, "var_220 region", 12072), (3, "var_222 region", 4400), (4, "v4 extra flag", 1),
        (5, "fixed-room object region A", 3990), (5, "fixed-room object region B", 1600),
        (5, "monster-name region", 6528)
    ];

    private readonly byte[] _bytes;
    public byte Version { get; }
    public string VersionDescription { get; }
    public IReadOnlyList<LegacySnapshotSegment> Segments { get; }

    public LegacyMemorySnapshotModel(byte[] bytes)
    {
        if (bytes.Length != SnapshotLength) throw new InvalidDataException($"Legacy snapshot must be exactly {SnapshotLength} bytes; got {bytes.Length}.");
        _bytes = (byte[])bytes.Clone();
        Version = _bytes[VersionOffset];
        VersionDescription = Version switch { 0x55 => "0x55 sentinel (extension blocks absent)", >= 1 and <= 5 => $"format marker {Version}", _ => $"unknown marker 0x{Version:X2} (base image only)" };
        if (Version is 0x55 or < 1 or > 5) { Segments = []; return; }

        var end = SegmentStartOffset;
        var result = new List<LegacySnapshotSegment>();
        foreach (var entry in SegmentSchema)
        {
            if (entry.MaxVersion > Version) continue;
            var segment = new LegacySnapshotSegment(entry.Name, end, entry.Length);
            if (segment.Offset < SegmentStartOffset || (long)segment.Offset + segment.Length > _bytes.Length)
                throw new InvalidDataException($"Snapshot segment '{segment.Name}' exceeds the container bounds.");
            result.Add(segment);
            end = checked(end + entry.Length + 16);
        }
        Segments = result;
    }

    public byte[] ReadBaseImage() => (byte[])_bytes.Clone();

    public byte[] ReadSegment(LegacySnapshotSegment segment)
    {
        if (!Segments.Contains(segment)) throw new ArgumentException("Segment does not belong to this snapshot.", nameof(segment));
        return _bytes.AsSpan(segment.Offset, segment.Length).ToArray();
    }

    public byte[] ReadRawContainer() => (byte[])_bytes.Clone();

    public static int CountDifferentBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) throw new ArgumentException("Byte ranges must be the same length.");
        var count = 0;
        for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) count++;
        return count;
    }
}
