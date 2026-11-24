using System;
using System.Collections.Generic;
using System.Threading;

namespace TornekoIjiriPort;

/// <summary>
/// スライムの指輪のレベルアップ効果。
///
/// ・仲間10枠のレベルを個別に監視
/// ・仲間レコードの種族IDからモンスター基本データを参照
/// ・SystemFamily の bit 5 (0x20) が立っている仲間だけを対象にする
/// ・スライムの指輪のアイテムIDと装備フラグを確認
/// ・対象の仲間自身の HP / 最大HP / 攻撃力 / 防御力を加算
///
/// ゲーム本体のELFやコード領域には書き込まない。
/// PCSX2 PINE共有メモリを使用する。
/// </summary>
internal sealed class SlimeRingLevelUpService : IDisposable
{
    // ------------------------------------------------------------
    // 仲間ポインタ
    // ------------------------------------------------------------

    private static readonly uint[] AllyPointerSlotAddresses =
    {
        0x00396808u, // 1番目
        0x00396818u, // 2番目
        0x00396828u, // 3番目
        0x00396838u, // 4番目
        0x00396848u, // 5番目
        0x00396858u, // 6番目
        0x00396868u, // 7番目
        0x00396878u, // 8番目
        0x00396888u, // 9番目
        0x00396898u  // 10番目
    };

    // 仲間レコードの有効範囲確認用
    private const uint AllyRecordMinimumAddress = 0x00300000u;
    private const uint AllyRecordMaximumAddress = 0x00400000u;
    private const uint AllyRecordRequiredSize = 0x90u;

    // 仲間レコード内のフィールド
    private const uint SpeciesIdOffset = 0x04u;
    private const uint CurrentHpOffset = 0x78u;
    private const uint MaxHpOffset = 0x7Cu;
    private const uint AttackOffset = 0x88u;

    // 上位16bit = レベル、下位16bit = 防御力
    private const uint DefenseAndLevelOffset = 0x8Cu;

    // ------------------------------------------------------------
    // モンスター基本データ
    // ------------------------------------------------------------

    private const int MonsterRecordSize = 24;
    private const int MonsterRecordCount = 199;
    private const int MonsterSystemFamilyOffset = 6;

    // SystemFamily bit 5 = スライム系
    private const byte SlimeFamilyMask = 0x20;

    // MonsterCreatorForm.TryResolveRecordTable と同じBINポインタ計算
    private const uint EeAddressMask = 0x1FFFFFFFu;
    private const uint EeMemoryBaseAddress = 0x00180000u;
    private const int MonsterTablePointerOffset = 16;

    // ------------------------------------------------------------
    // アイテム欄
    // ------------------------------------------------------------

    private const uint InventoryBaseAddress = 0x00650110u;
    private const uint InventorySlotStride = 0x240u;
    private const int InventorySlotCount = 20;

    private const uint SlimeRingItemId = 0x00A4u;
    private const uint EquippedFlagMask = 0x00100000u;

    // ------------------------------------------------------------
    // 監視状態
    // ------------------------------------------------------------

    private readonly System.Threading.Timer _timer;
    private readonly Dictionary<int, AllyBaseline> _lastAllies =
        new Dictionary<int, AllyBaseline>();

    private int _busy;
    private bool _disposed;
    private int _processId;
    private string _lastStatus = "";

    internal event Action<string>? StatusChanged;

    private sealed class AllyBaseline
    {
        internal uint RecordAddress { get; }
        internal uint SpeciesId { get; }
        internal int Level { get; }

        internal AllyBaseline(uint recordAddress, uint speciesId, int level)
        {
            RecordAddress = recordAddress;
            SpeciesId = speciesId;
            Level = level;
        }
    }

    private sealed class LevelUpEntry
    {
        internal int Slot { get; }
        internal uint RecordAddress { get; }
        internal uint SpeciesId { get; }
        internal int PreviousLevel { get; }
        internal int CurrentLevel { get; }

        internal int Difference => CurrentLevel - PreviousLevel;

        internal LevelUpEntry(
            int slot,
            uint recordAddress,
            uint speciesId,
            int previousLevel,
            int currentLevel)
        {
            Slot = slot;
            RecordAddress = recordAddress;
            SpeciesId = speciesId;
            PreviousLevel = previousLevel;
            CurrentLevel = currentLevel;
        }
    }

    internal SlimeRingLevelUpService()
    {
        _timer = new System.Threading.Timer(
            Poll,
            null,
            1000,
            250);
    }

    // ------------------------------------------------------------
    // メイン監視
    // ------------------------------------------------------------

    private void Poll(object? state)
    {
        if (_disposed)
            return;

        if (Interlocked.Exchange(ref _busy, 1) != 0)
            return;

        try
        {
            if (!Pcsx2LiveSession.IsConnected ||
                Pcsx2LiveSession.ProcessId <= 0)
            {
                _processId = 0;
                _lastAllies.Clear();

                RaiseStatus("スライムの指輪：PCSX2未接続");
                return;
            }

            int processId = Pcsx2LiveSession.ProcessId;

            if (_processId != processId)
            {
                _processId = processId;
                _lastAllies.Clear();
            }

            if (!TryResolveMonsterTableOffset(
                    out int monsterTableOffset,
                    out string tableError))
            {
                _lastAllies.Clear();
                RaiseStatus("スライムの指輪：" + tableError);
                return;
            }

            var levelUps = new List<LevelUpEntry>();
            var nonSlimeLevelUps = new List<string>();

            for (int slotIndex = 0;
                 slotIndex < AllyPointerSlotAddresses.Length;
                 slotIndex++)
            {
                uint pointerSlot = AllyPointerSlotAddresses[slotIndex];

                uint recordAddress =
                    ActionAutoRestoreService.ReadShared32(pointerSlot);

                if (recordAddress == 0)
                {
                    _lastAllies.Remove(slotIndex);
                    continue;
                }

                if (!IsValidAllyRecordAddress(recordAddress))
                {
                    _lastAllies.Remove(slotIndex);
                    continue;
                }

                uint packedSpeciesId =
                    ActionAutoRestoreService.ReadShared32(
                        recordAddress + SpeciesIdOffset);

                // 空き枠は+0x04がゼロなのでスキップ
                if (packedSpeciesId == 0)
                {
                    _lastAllies.Remove(slotIndex);
                    continue;
                }

                // +0x04の下位16bitが種族ID
                uint speciesId = packedSpeciesId & 0xFFFFu;
                uint duplicatedSpeciesId = packedSpeciesId >> 16;

                // 上位16bitと下位16bitの一致も確認
                if (speciesId == 0 ||
                    speciesId > MonsterRecordCount ||
                    duplicatedSpeciesId != speciesId)
                {
                    _lastAllies.Remove(slotIndex);

                    RaiseStatus(
                        "スライムの指輪：仲間" +
                        (slotIndex + 1) +
                        "の種族IDが不正です。" +
                        " / 値=0x" +
                        packedSpeciesId.ToString("X8"));

                    continue;
                }
                uint packedLevelDefense =
                    ActionAutoRestoreService.ReadShared32(
                        recordAddress + DefenseAndLevelOffset);

                int currentLevel =
                    (int)(packedLevelDefense >> 16);

                if (currentLevel <= 0 || currentLevel > 9999)
                {
                    _lastAllies.Remove(slotIndex);
                    continue;
                }

                if (!_lastAllies.TryGetValue(
                        slotIndex,
                        out AllyBaseline? previous) ||
                    previous.RecordAddress != recordAddress ||
                    previous.SpeciesId != speciesId)
                {
                    // 初回検出または仲間の入れ替わり。
                    // 既に起きたレベルアップには反応しない。
                    _lastAllies[slotIndex] =
                        new AllyBaseline(
                            recordAddress,
                            speciesId,
                            currentLevel);

                    continue;
                }

                if (currentLevel == previous.Level)
                    continue;

                if (currentLevel < previous.Level)
                {
                    // ステートロードやレベル変化による基準値の更新
                    _lastAllies[slotIndex] =
                        new AllyBaseline(
                            recordAddress,
                            speciesId,
                            currentLevel);

                    continue;
                }

                int difference =
                    currentLevel - previous.Level;

                // 二重適用防止のため、能力値処理より先に基準レベルを更新
                _lastAllies[slotIndex] =
                    new AllyBaseline(
                        recordAddress,
                        speciesId,
                        currentLevel);

                if (!TryGetSystemFamilyFlags(
                        Pcsx2LiveSession.VanillaBin!,
                        monsterTableOffset,
                        speciesId,
                        out byte systemFamilyFlags))
                {
                    RaiseStatus(
                        "スライムの指輪：仲間" +
                        (slotIndex + 1) +
                        "のSystemFamilyを取得できません。" +
                        " / 種族ID=" + speciesId);

                    continue;
                }

                bool isSlimeFamily =
                    (systemFamilyFlags & SlimeFamilyMask) != 0;

                if (!isSlimeFamily)
                {
                    nonSlimeLevelUps.Add(
                        "仲間" + (slotIndex + 1) +
                        " ID=" + speciesId +
                        " Family=0x" +
                        systemFamilyFlags.ToString("X2"));

                    continue;
                }

                levelUps.Add(
                    new LevelUpEntry(
                        slotIndex + 1,
                        recordAddress,
                        speciesId,
                        previous.Level,
                        currentLevel));
            }

            if (levelUps.Count == 0)
            {
                if (nonSlimeLevelUps.Count > 0)
                {
                    RaiseStatus(
                        "スライムの指輪：レベルアップ検出。" +
                        "スライム系ではないため対象外。" +
                        " / " + string.Join(" / ", nonSlimeLevelUps));
                }

                return;
            }

            bool ringEquipped = IsSlimeRingEquipped(
                out int foundSlot,
                out uint foundFlag,
                out uint foundItemId);

            if (!ringEquipped)
            {
                var descriptions = new List<string>();

                foreach (LevelUpEntry entry in levelUps)
                {
                    descriptions.Add(
                        "仲間" + entry.Slot +
                        " Lv." + entry.PreviousLevel +
                        "→" + entry.CurrentLevel);
                }

                RaiseStatus(
                    "スライムの指輪：スライム系のレベルアップを検出。" +
                    "指輪装備なしのためボーナスなし。" +
                    " / " + string.Join(" / ", descriptions));

                return;
            }

            var applied = new List<string>();

            foreach (LevelUpEntry entry in levelUps)
            {
                ApplyLevelUpBonus(
                    entry.RecordAddress,
                    entry.Difference);

                applied.Add(
                    "仲間" + entry.Slot +
                    " ID=" + entry.SpeciesId +
                    " Lv." + entry.PreviousLevel +
                    "→" + entry.CurrentLevel +
                    " +" + entry.Difference);
            }

            RaiseStatus(
                "スライムの指輪：ボーナス適用成功。" +
                " / " + string.Join(" / ", applied) +
                " / 指輪枠=" + foundSlot +
                " / フラグ=0x" + foundFlag.ToString("X8") +
                " / ID=0x" + foundItemId.ToString("X4") +
                " / HP・最大HP・攻撃・防御を加算");
        }
        catch (Exception ex)
        {
            RaiseStatus(
                "スライムの指輪：監視エラー：" +
                ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // ------------------------------------------------------------
    // 仲間レコードのアドレス確認
    // ------------------------------------------------------------

    private static bool IsValidAllyRecordAddress(uint address)
    {
        if ((address & 3u) != 0)
            return false;

        if (address < AllyRecordMinimumAddress)
            return false;

        if (address > AllyRecordMaximumAddress - AllyRecordRequiredSize)
            return false;

        return true;
    }

    // ------------------------------------------------------------
    // モンスター基本データのテーブル位置を解決
    // MonsterCreatorForm.TryResolveRecordTable と同じ計算
    // ------------------------------------------------------------

    private static bool TryResolveMonsterTableOffset(
        out int tableOffset,
        out string reason)
    {
        tableOffset = -1;

        byte[]? bin = Pcsx2LiveSession.VanillaBin;

        if (!Pcsx2LiveSession.VanillaDataLoaded || bin == null)
        {
            reason =
                "モンスター基本データが未読込です。" +
                "PCSX2接続画面でバニラBINを取得してください。";

            return false;
        }

        if (bin.Length < 524288)
        {
            reason =
                "バニラBINのサイズが524288 bytes未満です。";

            return false;
        }

        uint rawPointer =
            BitConverter.ToUInt32(
                bin,
                MonsterTablePointerOffset);

        uint correction =
            (uint)bin[3] << 24;

        uint pointer =
            unchecked(rawPointer + correction);

        uint physicalAddress =
            pointer & EeAddressMask;

        if (physicalAddress < EeMemoryBaseAddress)
        {
            reason =
                "モンスターデータへのポインタが不正です。" +
                "BINの種類を確認してください。";

            return false;
        }

        long offset =
            (long)physicalAddress -
            EeMemoryBaseAddress +
            MonsterRecordSize;

        long requiredEnd =
            offset +
            (long)MonsterRecordCount * MonsterRecordSize;

        if (offset < 0 ||
            requiredEnd > bin.Length)
        {
            reason =
                "モンスター基本データ領域がBIN内にありません。" +
                " / 推定位置=0x" +
                offset.ToString("X");

            return false;
        }

        tableOffset = checked((int)offset);
        reason = "";
        return true;
    }

    // ------------------------------------------------------------
    // 種族IDからSystemFamilyを取得
    // ランタイム種族ID = モンスターテーブルの0始まり番号 + 1
    // ------------------------------------------------------------

    private static bool TryGetSystemFamilyFlags(
        byte[] bin,
        int tableOffset,
        uint speciesId,
        out byte systemFamilyFlags)
    {
        systemFamilyFlags = 0;

        if (speciesId == 0 ||
            speciesId > MonsterRecordCount)
        {
            return false;
        }

        int recordIndex =
            checked((int)speciesId - 1);

        int recordOffset =
            checked(
                tableOffset +
                recordIndex * MonsterRecordSize);

        int flagsOffset =
            checked(
                recordOffset +
                MonsterSystemFamilyOffset);

        if (flagsOffset < 0 ||
            flagsOffset >= bin.Length)
        {
            return false;
        }

        systemFamilyFlags = bin[flagsOffset];
        return true;
    }

    // ------------------------------------------------------------
    // スライムの指輪装備判定
    // ------------------------------------------------------------

    private static bool IsSlimeRingEquipped(
        out int foundSlot,
        out uint foundFlag,
        out uint foundItemId)
    {
        foundSlot = -1;
        foundFlag = 0;
        foundItemId = 0;

        for (int i = 0; i < InventorySlotCount; i++)
        {
            uint address =
                InventoryBaseAddress +
                (uint)i * InventorySlotStride;

            uint flag =
                ActionAutoRestoreService.ReadShared32(address);

            uint itemId =
                ActionAutoRestoreService.ReadShared32(
                    address + 0x0Cu);

            bool isSlimeRing =
                (itemId & 0xFFFFu) == SlimeRingItemId;

            bool isEquipped =
                (flag & EquippedFlagMask) != 0;

            if (isSlimeRing && isEquipped)
            {
                foundSlot = i + 1;
                foundFlag = flag;
                foundItemId = itemId;

                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------
    // 対象の仲間自身の能力値を加算
    // ------------------------------------------------------------

    private static void ApplyLevelUpBonus(
        uint allyRecordAddress,
        int amount)
    {
        if (amount <= 0)
            return;

        uint currentHpAddress =
            checked(allyRecordAddress + CurrentHpOffset);

        uint maxHpAddress =
            checked(allyRecordAddress + MaxHpOffset);

        uint attackAddress =
            checked(allyRecordAddress + AttackOffset);

        uint defenseAndLevelAddress =
            checked(allyRecordAddress + DefenseAndLevelOffset);

        uint oldHp =
            ActionAutoRestoreService.ReadShared32(
                currentHpAddress);

        uint oldMaxHp =
            ActionAutoRestoreService.ReadShared32(
                maxHpAddress);

        uint oldAttack =
            ActionAutoRestoreService.ReadShared32(
                attackAddress);

        uint oldDefenseLevel =
            ActionAutoRestoreService.ReadShared32(
                defenseAndLevelAddress);

        ushort oldDefense =
            (ushort)(oldDefenseLevel & 0xFFFFu);

        ushort level =
            (ushort)(oldDefenseLevel >> 16);

        ulong newHpLong =
            (ulong)oldHp + (uint)amount;

        ulong newMaxHpLong =
            (ulong)oldMaxHp + (uint)amount;

        ulong newAttackLong =
            (ulong)oldAttack + (uint)amount;

        uint newDefenseLong =
            (uint)oldDefense + (uint)amount;

        if (newHpLong > uint.MaxValue ||
            newMaxHpLong > uint.MaxValue ||
            newAttackLong > uint.MaxValue ||
            newDefenseLong > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                "能力値の加算で上限を超えるため中止しました。" +
                " / 仲間レコード=0x" +
                allyRecordAddress.ToString("X8"));
        }

        uint newHp = (uint)newHpLong;
        uint newMaxHp = (uint)newMaxHpLong;
        uint newAttack = (uint)newAttackLong;

        // 上位16bitのレベルを保持し、下位16bitの防御力だけ加算
        uint newDefenseLevel =
            ((uint)level << 16) |
            (ushort)newDefenseLong;

        var changes =
            new List<(uint Address, uint OldValue)>();

        try
        {
            WriteAndVerify(
                currentHpAddress,
                oldHp,
                newHp,
                changes);

            WriteAndVerify(
                maxHpAddress,
                oldMaxHp,
                newMaxHp,
                changes);

            WriteAndVerify(
                attackAddress,
                oldAttack,
                newAttack,
                changes);

            WriteAndVerify(
                defenseAndLevelAddress,
                oldDefenseLevel,
                newDefenseLevel,
                changes);
        }
        catch (Exception writeError)
        {
            var rollbackErrors = new List<string>();

            for (int i = changes.Count - 1; i >= 0; i--)
            {
                var change = changes[i];

                try
                {
                    ActionAutoRestoreService.WriteShared32(
                        change.Address,
                        change.OldValue);

                    uint verify =
                        ActionAutoRestoreService.ReadShared32(
                            change.Address);

                    if (verify != change.OldValue)
                    {
                        rollbackErrors.Add(
                            "復元確認失敗：0x" +
                            change.Address.ToString("X8"));
                    }
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(
                        "復元失敗：0x" +
                        change.Address.ToString("X8") +
                        " / " + rollbackError.Message);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "能力値の書き込みに失敗しました。" +
                    "一部の復元も確認できません。\r\n" +
                    string.Join("\r\n", rollbackErrors),
                    writeError);
            }

            throw new InvalidOperationException(
                "能力値の書き込みに失敗しました。" +
                "書き込み済みの値は元に戻しました。\r\n" +
                writeError.Message,
                writeError);
        }
    }

    // ------------------------------------------------------------
    // 書き込みと読み戻し検証
    // ------------------------------------------------------------

    private static void WriteAndVerify(
        uint address,
        uint oldValue,
        uint newValue,
        List<(uint Address, uint OldValue)> changes)
    {
        changes.Add((address, oldValue));

        ActionAutoRestoreService.WriteShared32(
            address,
            newValue);

        uint verify =
            ActionAutoRestoreService.ReadShared32(
                address);

        if (verify != newValue)
        {
            throw new InvalidOperationException(
                "書き込み後の読み戻し検証に失敗しました。" +
                " / Address=0x" +
                address.ToString("X8") +
                " / Expected=0x" +
                newValue.ToString("X8") +
                " / Actual=0x" +
                verify.ToString("X8"));
        }
    }

    // ------------------------------------------------------------
    // ステータス表示
    // ------------------------------------------------------------

    private void RaiseStatus(string message)
    {
        if (_lastStatus == message)
            return;

        _lastStatus = message;
        StatusChanged?.Invoke(message);
    }

    // ------------------------------------------------------------
    // 終了処理
    // ------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
    }
}
