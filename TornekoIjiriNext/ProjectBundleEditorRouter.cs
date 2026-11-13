namespace TornekoIjiriNext;

internal enum ProjectBundleEditorKind
{
    LegacyIni,
    MonsterSpawnTable,
    FloorData,
    ItemCategoryRates,
    IndividualItemRates,
    MonsterStatus,
    MonsterGrowth,
    FixedRoomMap,
    FixedRoomObjectTable,
    LegacySnapshot,
    RawBinary
}

/// <summary>Explicit category/extension routing so a bundled raw file is never guessed by extension alone.</summary>
internal static class ProjectBundleEditorRouter
{
    public const int FixedRoomObjectTableLength = 220 * 20;

    public static ProjectBundleEditorKind? Resolve(ProjectBundleEntry entry, long assetLength)
    {
        var extension = Path.GetExtension(entry.FileName);
        bool Is(params string[] values) => values.Contains(extension, StringComparer.OrdinalIgnoreCase);
        return entry.Category switch
        {
            "INI設定" when Is(".ini") => ProjectBundleEditorKind.LegacyIni,
            "モンスター出現率" when Is(".bin", ".json") => ProjectBundleEditorKind.MonsterSpawnTable,
            "フロア設定" when Is(".bin", ".json") => ProjectBundleEditorKind.FloorData,
            "アイテム出現率" or "アイテムカテゴリ率" when Is(".bin", ".json") => ProjectBundleEditorKind.ItemCategoryRates,
            "個別アイテム率" when Is(".bin", ".json") => ProjectBundleEditorKind.IndividualItemRates,
            "モンスター能力" when Is(".bin", ".dat") => ProjectBundleEditorKind.MonsterStatus,
            "成長テーブル" when Is(".bin", ".dat", ".json") => ProjectBundleEditorKind.MonsterGrowth,
            "固定部屋" when Is(".bin", ".json") && assetLength != FixedRoomObjectTableLength => ProjectBundleEditorKind.FixedRoomMap,
            "固定部屋object table" when Is(".bin") && assetLength == FixedRoomObjectTableLength => ProjectBundleEditorKind.FixedRoomObjectTable,
            "snapshot" when Is(".bin", ".dat") && assetLength == LegacyMemorySnapshotModel.SnapshotLength => ProjectBundleEditorKind.LegacySnapshot,
            "その他" when Is(".bin", ".dat", ".raw", ".sav", ".dmp") => ProjectBundleEditorKind.RawBinary,
            _ => null
        };
    }
}
