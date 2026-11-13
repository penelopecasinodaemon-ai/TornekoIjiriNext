using System.Globalization;
using System.Text;

namespace TornekoIjiriNext;

internal sealed record LegacyIniField(int Index, string Variable, string Description, bool IsText, int Maximum);

/// <summary>Offline round-trip model for the old 22-line and 25-line positional INI.</summary>
internal sealed class LegacyIniModel
{
    private static readonly Encoding ShiftJis = CreateShiftJis();
    public static readonly IReadOnlyList<LegacyIniField> Fields = BuildFields();
    private readonly List<string> _values;

    private LegacyIniModel(List<string> values) => _values = values;
    public int Count => _values.Count;
    public IReadOnlyList<string> Values => _values.AsReadOnly();

    public static LegacyIniModel Parse(ReadOnlySpan<byte> bytes)
    {
        var text = ShiftJis.GetString(bytes);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count is not (22 or 25)) throw new InvalidDataException($"旧INIは22行または25行です。入力は{lines.Count}行です。");
        for (var i = 0; i < lines.Count; i++)
        {
            if (Fields[i].IsText) continue;
            if (!int.TryParse(lines[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException($"旧INIの{(i + 1)}行目 (var {Fields[i].Variable}) は整数ではありません。");
            if (value < 0 || value > Fields[i].Maximum)
                throw new InvalidDataException($"旧INIの{(i + 1)}行目 (var {Fields[i].Variable}) は範囲0〜{Fields[i].Maximum}外です。");
        }
        if (lines.Count == 25)
        {
            for (var i = 23; i <= 24; i++)
            {
                if (ShiftJis.GetByteCount(lines[i]) > 512) throw new InvalidDataException($"旧INIの{(i + 1)}行目が512 bytesを超えています。");
                if (lines[i].Contains('\n') || lines[i].Contains('\r')) throw new InvalidDataException("INI text fields cannot contain newlines.");
            }
        }
        return new LegacyIniModel(lines);
    }

    public void SetValue(int index, string value)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        var field = Fields[index];
        if (field.IsText)
        {
            if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException("Text fields cannot contain newlines.", nameof(value));
            if (ShiftJis.GetByteCount(value) > 512) throw new ArgumentException("Text fields must fit within 512 Shift-JIS bytes.", nameof(value));
        }
        else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0 || number > field.Maximum)
            throw new ArgumentException($"{field.Variable} must be an integer from 0 to {field.Maximum}.", nameof(value));
        _values[index] = value;
    }

    public byte[] Serialize()
    {
        foreach (var (value, index) in _values.Select((value, index) => (value, index)))
        {
            var field = Fields[index];
            if (field.IsText) _ = ShiftJis.GetByteCount(value);
            else if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0 || number > field.Maximum)
                throw new InvalidDataException($"{field.Variable} is outside its legacy range.");
        }
        return ShiftJis.GetBytes(string.Join("\n", _values) + "\n");
    }

    private static Encoding CreateShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static IReadOnlyList<LegacyIniField> BuildFields()
    {
        var fields = new List<LegacyIniField>();
        var rev0Variables = new[] { "var_807", "var_808", "var_809", "var_810", "var_811", "var_812", "var_813", "var_814" };
        var rev0Memory = new[] { "var_201", "var_200", "var_202", "var_203", "var_204", "var_205", "var_206", "var_207" };
        for (var i = 0; i < rev0Variables.Length; i++) fields.Add(new LegacyIniField(i, rev0Variables[i], $"旧テンポ設定 byte ({rev0Memory[i]}, Rev0)", false, 255));
        fields.Add(new LegacyIniField(8, "var_866", "tempo screen checkbox (source label unverified)", false, 1));
        var rev1Variables = new[] { "var_815", "var_816", "var_817", "var_818", "var_819", "var_820", "var_821", "var_822", "var_823" };
        var rev1Memory = new[] { "var_208", "var_209", "var_210", "var_211", "var_212", "var_213", "var_214", "var_215", "var_216" };
        for (var i = 0; i < rev1Variables.Length; i++) fields.Add(new LegacyIniField(9 + i, rev1Variables[i], $"旧テンポ設定 byte ({rev1Memory[i]}, Rev1)", false, 255));
        fields.Add(new LegacyIniField(18, "var_842", "tempo screen checkbox", false, 1));
        fields.Add(new LegacyIniField(19, "var_844", "tempo screen checkbox", false, 1));
        fields.Add(new LegacyIniField(20, "var_846", "legacy auto-save checkbox candidate", false, 1));
        fields.Add(new LegacyIniField(21, "var_224", "旧版numeric input (3 digits)", false, 999));
        fields.Add(new LegacyIniField(22, "var_433", "main screen checkbox", false, 1));
        fields.Add(new LegacyIniField(23, "var_353", "旧版text/path field (meaning unverified)", true, 512));
        fields.Add(new LegacyIniField(24, "var_431", "旧版text field / ranking name candidate", true, 512));
        return fields;
    }
}
