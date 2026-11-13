using System.Globalization;
using System.Text.Json;

namespace TornekoIjiriNext;

internal sealed record CaptureRegionDescriptor(uint Address, int Length, string BytesHex);
internal sealed record CaptureDescriptor(string Kind, DateTime CapturedAtUtc, string Pcsx2Version, string GameTitle, string GameId, string GameCrc, string GameVersion, IReadOnlyList<CaptureRegionDescriptor> Regions);
internal sealed record CaptureByteDifference(int Offset, byte Before, byte After);
internal sealed record CaptureRegionComparison(uint Address, int Length, int ChangedBytes, IReadOnlyList<CaptureByteDifference> Changes, bool IsTruncated);
internal sealed record CaptureComparison(bool RegionLayoutMatches, bool GameIdentityMatches, IReadOnlyList<CaptureRegionComparison> Regions);

/// <summary>Validates and describes existing read-only PINE capture JSON without interpreting game data.</summary>
internal static class ProjectCaptureModel
{
    public static CaptureDescriptor Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 64L * 1024 * 1024) throw new InvalidDataException("Capture JSON is missing or exceeds 64 MiB.");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("PINE capture root must be a JSON object.");
        var hasVersion = TryGet(root, "FormatVersion", out var versionElement);
        if (hasVersion && (!versionElement.TryGetInt32(out var version) || version != 1))
            throw new InvalidDataException("Unsupported PINE capture format version.");

        var capturedAt = RequiredDate(root, "CapturedAtUtc");
        var pcsx2 = RequiredString(root, "Pcsx2Version");
        var title = RequiredString(root, "GameTitle");
        var gameId = RequiredString(root, "GameId");
        var crc = RequiredString(root, "GameCrc");
        var gameVersion = RequiredString(root, "GameVersion");
        var regions = new List<CaptureRegionDescriptor>();
        string kind;

        if (TryGet(root, "Address", out var addressElement))
        {
            kind = hasVersion ? "single" : "legacy-single";
            var address = ReadAddress(addressElement, "Address");
            var length = RequiredInt(root, "Length");
            var bytesHex = RequiredString(root, "BytesHex");
            AddValidatedRegion(regions, address, length, bytesHex);
        }
        else if (TryGet(root, "Regions", out var regionsElement) && regionsElement.ValueKind == JsonValueKind.Array)
        {
            kind = hasVersion ? "batch" : "legacy-batch";
            AddRegionArray(regions, regionsElement, 1024, requireLength: true);
            // Older diagnostic captures also carried independent probe points alongside Regions.
            if (TryGet(root, "Points", out var points) && points.ValueKind == JsonValueKind.Array)
                AddRegionArray(regions, points, 1024, requireLength: false);
        }
        else if (!hasVersion && TryGet(root, "PointerTargets64Bytes", out var targets) && targets.ValueKind == JsonValueKind.Array)
        {
            kind = "legacy-batch";
            if (TryGet(root, "PointerTable", out var table) && table.ValueKind == JsonValueKind.Object)
                regions.Add(ReadRegion(table, requireLength: false));
            AddRegionArray(regions, targets, 1024, requireLength: false);
            if (TryGet(root, "PreviousProbePoints", out var probes) && probes.ValueKind == JsonValueKind.Array)
                AddRegionArray(regions, probes, 1024, requireLength: false);
        }
        else throw new InvalidDataException("JSON is neither a supported PINE capture nor a recognized legacy read-only evidence file.");

        if (regions.Count is < 1 or > 1024) throw new InvalidDataException("Capture must contain 1..1024 regions.");
        return new CaptureDescriptor(kind, capturedAt, pcsx2, title, gameId, crc, gameVersion, regions);
    }

    private static void AddRegionArray(List<CaptureRegionDescriptor> destination, JsonElement array, int maximum, bool requireLength)
    {
        if (array.GetArrayLength() < 1 || array.GetArrayLength() > maximum)
            throw new InvalidDataException($"Capture region list must contain 1..{maximum} entries.");
        foreach (var region in array.EnumerateArray()) destination.Add(ReadRegion(region, requireLength));
    }

    private static CaptureRegionDescriptor ReadRegion(JsonElement region, bool requireLength)
    {
        var address = ReadAddress(Required(region, "Address"), "Address");
        var bytesHex = RequiredString(region, "BytesHex");
        var length = TryGet(region, "Length", out var lengthElement)
            ? (lengthElement.TryGetInt32(out var declared) ? declared : throw new InvalidDataException("Invalid region length."))
            : requireLength ? throw new InvalidDataException("Missing region length.") : bytesHex.Length / 2;
        var result = new List<CaptureRegionDescriptor>(1);
        AddValidatedRegion(result, address, length, bytesHex);
        return result[0];
    }

    private static void AddValidatedRegion(List<CaptureRegionDescriptor> regions, uint address, int length, string bytesHex)
    {
        ValidateHexBytes(bytesHex, length);
        if ((ulong)address + (uint)length - 1 > uint.MaxValue) throw new InvalidDataException("Capture region overflows the 32-bit address range.");
        regions.Add(new CaptureRegionDescriptor(address, length, bytesHex));
    }

    public static string BuildCategory(CaptureDescriptor capture) => $"PINE capture {capture.Kind} | {capture.GameId} | CRC {capture.GameCrc} | {capture.GameVersion}";

    public static CaptureComparison Compare(CaptureDescriptor before, CaptureDescriptor after, int maximumDisplayedChangesPerRegion = 10000)
    {
        if (maximumDisplayedChangesPerRegion is < 0 or > 100000) throw new ArgumentOutOfRangeException(nameof(maximumDisplayedChangesPerRegion));
        var sameLayout = before.Regions.Count == after.Regions.Count && before.Regions.Zip(after.Regions).All(pair => pair.First.Address == pair.Second.Address && pair.First.Length == pair.Second.Length);
        var sameGame = string.Equals(before.GameId, after.GameId, StringComparison.Ordinal) && string.Equals(before.GameCrc, after.GameCrc, StringComparison.OrdinalIgnoreCase) && string.Equals(before.GameVersion, after.GameVersion, StringComparison.Ordinal);
        if (!sameLayout) return new CaptureComparison(false, sameGame, Array.Empty<CaptureRegionComparison>());
        var comparisons = new List<CaptureRegionComparison>(before.Regions.Count);
        foreach (var (left, right) in before.Regions.Zip(after.Regions))
        {
            var leftBytes = Convert.FromHexString(left.BytesHex);
            var rightBytes = Convert.FromHexString(right.BytesHex);
            if (leftBytes.Length != left.Length || rightBytes.Length != right.Length) throw new InvalidDataException("Capture raw bytes do not match the declared region length.");
            var changes = new List<CaptureByteDifference>(Math.Min(maximumDisplayedChangesPerRegion, left.Length));
            var total = 0;
            for (var i = 0; i < leftBytes.Length; i++)
            {
                if (leftBytes[i] == rightBytes[i]) continue;
                total++;
                if (changes.Count < maximumDisplayedChangesPerRegion) changes.Add(new CaptureByteDifference(i, leftBytes[i], rightBytes[i]));
            }
            comparisons.Add(new CaptureRegionComparison(left.Address, left.Length, total, changes, total > changes.Count));
        }
        return new CaptureComparison(true, sameGame, comparisons);
    }

    private static void ValidateHexBytes(string hex, int length)
    {
        if (length is < 1 or > 64 * 1024 * 1024 || hex.Length != (long)length * 2 || (hex.Length & 1) != 0 || !hex.All(Uri.IsHexDigit))
            throw new InvalidDataException("Capture byte string is invalid or its length does not match the metadata.");
    }

    private static uint ReadAddress(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var numeric)) return numeric;
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()?.Trim() ?? "";
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) return hex;
        }
        throw new InvalidDataException($"Invalid 32-bit address field: {name}.");
    }

    private static int RequiredInt(JsonElement element, string name)
    {
        var value = Required(element, name);
        if (!value.TryGetInt32(out var result)) throw new InvalidDataException($"Invalid integer field: {name}.");
        return result;
    }

    private static DateTime RequiredDate(JsonElement element, string name)
    {
        if (!DateTime.TryParse(RequiredString(element, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            throw new InvalidDataException($"Invalid timestamp field: {name}.");
        return value.ToUniversalTime();
    }

    private static string RequiredString(JsonElement element, string name)
    {
        var value = Required(element, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) throw new InvalidDataException($"Invalid string field: {name}.");
        return value.GetString()!;
    }

    private static JsonElement Required(JsonElement element, string name) => TryGet(element, name, out var value) ? value : throw new InvalidDataException($"Missing capture field: {name}.");

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        value = default;
        return false;
    }
}
