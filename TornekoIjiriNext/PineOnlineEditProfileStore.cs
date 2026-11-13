using System.Text.Json;

namespace TornekoIjiriNext;

/// <summary>
/// オンライン編集項目の構成ファイルを保存・読み込みする。
///
/// 重要:
/// AddressVerifiedはファイルに保存しない。
/// 読み込み時は必ずfalseにする。
/// 構成ファイルだけでメモリ書き込みが許可されることを防ぐ。
/// </summary>
internal static class PineOnlineEditProfileStore
{
    private const int CurrentFormatVersion = 1;
    private const int MaximumFields = 512;
    private const long MaximumFileBytes = 1024 * 1024;

    private sealed class ProfileDocument
    {
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public DateTime CreatedAtUtc { get; set; }
        public string ProfileType { get; set; } = "PINE_ONLINE_EDIT_PROFILE";
        public List<ProfileField> Fields { get; set; } = new();
    }

    private sealed class ProfileField
    {
        public string GroupName { get; set; } = "";
        public string FieldName { get; set; } = "";
        public uint Address { get; set; }
        public int WidthBytes { get; set; }
        public uint InitialValue { get; set; }
        public string Description { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16
    };

    public static void Save(
        string path,
        IEnumerable<PineOnlineEditFieldDefinition> definitions)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("保存先が指定されていません。");

        ArgumentNullException.ThrowIfNull(definitions);

        var items = definitions.ToList();
        ValidateDefinitions(items);

        var document = new ProfileDocument
        {
            FormatVersion = CurrentFormatVersion,
            CreatedAtUtc = DateTime.UtcNow,
            ProfileType = "PINE_ONLINE_EDIT_PROFILE",
            Fields = items.Select(x => new ProfileField
            {
                GroupName = x.GroupName,
                FieldName = x.FieldName,
                Address = x.Address,
                WidthBytes = x.WidthBytes,
                InitialValue = x.InitialValue,
                Description = x.Description ?? ""
            }).ToList()
        };

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            document,
            JsonOptions);

        if (bytes.Length > MaximumFileBytes)
            throw new InvalidDataException("構成ファイルが大きすぎます。");

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory))
            throw new IOException("保存先フォルダを取得できません。");

        Directory.CreateDirectory(directory);

        string temporaryPath =
            fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            File.WriteAllBytes(temporaryPath, bytes);

            if (File.Exists(fullPath))
            {
                string backupPath =
                    fullPath + "." +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".bak";

                File.Copy(fullPath, backupPath, false);
                File.Move(temporaryPath, fullPath, true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static List<PineOnlineEditFieldDefinition> Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("読み込み元が指定されていません。");

        var info = new FileInfo(path);

        if (!info.Exists)
            throw new FileNotFoundException("構成ファイルがありません。", path);

        if (info.Length < 1 || info.Length > MaximumFileBytes)
            throw new InvalidDataException("構成ファイルのサイズが不正です。");

        byte[] bytes = File.ReadAllBytes(path);

        var document = JsonSerializer.Deserialize<ProfileDocument>(
            bytes,
            JsonOptions);

        if (document is null)
            throw new InvalidDataException("構成ファイルを解析できません。");

        if (document.FormatVersion != CurrentFormatVersion)
            throw new InvalidDataException(
                $"未対応の構成バージョンです: {document.FormatVersion}");

        if (!string.Equals(
            document.ProfileType,
            "PINE_ONLINE_EDIT_PROFILE",
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("構成ファイルの種類が一致しません。");
        }

        if (document.Fields is null)
            throw new InvalidDataException("編集項目がありません。");

        var result = document.Fields.Select(x =>
            new PineOnlineEditFieldDefinition(
                x.GroupName,
                x.FieldName,
                x.Address,
                x.WidthBytes,
                x.InitialValue,
                false,
                x.Description ?? ""
            )).ToList();

        ValidateDefinitions(result);

        return result;
    }

    private static void ValidateDefinitions(
        List<PineOnlineEditFieldDefinition> definitions)
    {
        if (definitions.Count > MaximumFields)
            throw new InvalidDataException("編集項目は512件以内にしてください。");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in definitions)
        {
            if (item is null)
                throw new InvalidDataException("nullの編集項目があります。");

            if (string.IsNullOrWhiteSpace(item.GroupName) ||
                item.GroupName.Length > 100)
                throw new InvalidDataException("グループ名が不正です。");

            if (string.IsNullOrWhiteSpace(item.FieldName) ||
                item.FieldName.Length > 100)
                throw new InvalidDataException("項目名が不正です。");

            if (item.WidthBytes is not (1 or 2 or 4))
                throw new InvalidDataException("データ幅が不正です。");

            ulong end =
                (ulong)item.Address + (uint)item.WidthBytes - 1;

            if (end > uint.MaxValue)
                throw new InvalidDataException("アドレス範囲が不正です。");

            ulong maxValue = item.WidthBytes switch
            {
                1 => byte.MaxValue,
                2 => ushort.MaxValue,
                4 => uint.MaxValue,
                _ => 0
            };

            if (item.InitialValue > maxValue)
                throw new InvalidDataException("初期値がデータ幅を超えています。");

            if ((item.Description ?? "").Length > 500)
                throw new InvalidDataException("説明文が長すぎます。");

            string key = item.GroupName + "\0" + item.FieldName;

            if (!keys.Add(key))
                throw new InvalidDataException(
                    $"重複した項目があります: {item.FieldName}");
        }
    }
}