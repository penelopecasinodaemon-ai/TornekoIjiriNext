
using System;
using System.Collections.Generic;
using System.Threading;

namespace TornekoIjiriPort;

/// <summary>
/// スライムの指輪実験用サービス。
///
/// ・ポポロのアイテム欄20枠を走査
/// ・装備フラグとアイテムIDの両方を確認
/// ・レベルアップを検出
/// ・現在HP、最大HP、攻撃力、防御力をそれぞれ+1
///
/// ゲーム本体のコード領域には書き込まない。
/// PCSX2の共有メモリを直接読み書きする。
/// </summary>
internal sealed class SlimeRingLevelUpService : IDisposable
{
    // ------------------------------------------------------------
    // アイテム欄
    // ------------------------------------------------------------

    private const uint InventoryBaseAddress = 0x00650110u;
    private const uint InventorySlotStride = 0x240u;
    private const int InventorySlotCount = 20;

    // スライムの指輪
    private const uint SlimeRingItemId = 0x00A4u;

    // 装備中を示すフラグ候補
    private const uint EquippedFlagMask = 0x00100000u;

    // ------------------------------------------------------------
    // 能力値
    // ------------------------------------------------------------

    private const uint CurrentHpAddress = 0x00396AE8u;
    private const uint MaxHpAddress = 0x00396AECu;
    private const uint AttackAddress = 0x00396AF8u;

    // 上位16bitがレベル、下位16bitが防御力
    private const uint DefenseAndLevelAddress = 0x00396AFCu;

    // ------------------------------------------------------------
    // 監視
    // ------------------------------------------------------------

    private readonly System.Threading.Timer _timer;

    private int _busy;
    private bool _disposed;

    private int _processId;
    private int? _lastLevel;

    private string _lastStatus = "";

    internal event Action<string>? StatusChanged;

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
                _lastLevel = null;

                RaiseStatus(
                    "スライムの指輪実験：PCSX2未接続");

                return;
            }

            int processId = Pcsx2LiveSession.ProcessId;

            // PCSX2の再接続を検出
            if (_processId != processId)
            {
                _processId = processId;
                _lastLevel = null;
            }

            // レベルと防御力が格納されたDWORD
            uint packedLevelDefense =
                ActionAutoRestoreService.ReadShared32(
                    DefenseAndLevelAddress);

            int currentLevel =
                (int)(packedLevelDefense >> 16);

            if (currentLevel <= 0 || currentLevel > 9999)
            {
                RaiseStatus(
                    "スライムの指輪実験：レベル値が不正です。" +
                    " / 値=0x" +
                    packedLevelDefense.ToString("X8"));

                return;
            }

            // 初回は現在レベルを基準値にする。
            // 起動前のレベルアップに誤反応しないようにする。
            if (!_lastLevel.HasValue)
            {
                _lastLevel = currentLevel;

                RaiseStatus(
                    "スライムの指輪実験：監視中" +
                    " / レベル=" + currentLevel +
                    " / 指輪判定はレベルアップ時に実行");

                return;
            }

            int previousLevel = _lastLevel.Value;

            if (currentLevel == previousLevel)
                return;

            // ステートロードなどでレベルが下がった場合
            if (currentLevel < previousLevel)
            {
                _lastLevel = currentLevel;

                RaiseStatus(
                    "スライムの指輪実験：レベル変化を検出" +
                    " / " + previousLevel +
                    " → " + currentLevel +
                    " / 基準値を更新");

                return;
            }

            int levelDifference =
                currentLevel - previousLevel;

            // 指輪を20枠から検索
            bool ringEquipped = IsSlimeRingEquipped(
                out int foundSlot,
                out uint foundFlag,
                out uint foundItemId);

            // 二重適用を防ぐため、検出したレベルを先に記録
            _lastLevel = currentLevel;

            if (!ringEquipped)
            {
                RaiseStatus(
                    "スライムの指輪実験：レベルアップ検出" +
                    " / " + previousLevel +
                    " → " + currentLevel +
                    " / 指輪装備なし" +
                    " / ボーナスなし");

                return;
            }

            // レベル差分の回数だけ能力値を加算
            ApplyLevelUpBonus(levelDifference);

            RaiseStatus(
                "スライムの指輪実験：ボーナス適用成功" +
                " / レベル=" + currentLevel +
                " / 上昇回数=" + levelDifference +
                " / 指輪枠=" + foundSlot +
                " / フラグ=0x" + foundFlag.ToString("X8") +
                " / ID=0x" + foundItemId.ToString("X4") +
                " / HP・最大HP・攻撃・防御 +" +
                levelDifference);
        }
        catch (Exception ex)
        {
            RaiseStatus(
                "スライムの指輪実験：監視エラー：" +
                ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // ------------------------------------------------------------
    // アイテム欄20枠のスライムの指輪判定
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

            // レコード先頭のフラグ
            uint flag =
                ActionAutoRestoreService.ReadShared32(
                    address);

            // レコード+0x0CのアイテムID
            uint itemId =
                ActionAutoRestoreService.ReadShared32(
                    address + 0x0Cu);

            // アイテムIDと装備フラグを両方確認
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
    // 能力値ボーナス
    // ------------------------------------------------------------

    private static void ApplyLevelUpBonus(int amount)
    {
        if (amount <= 0)
            return;

        uint oldHp =
            ActionAutoRestoreService.ReadShared32(
                CurrentHpAddress);

        uint oldMaxHp =
            ActionAutoRestoreService.ReadShared32(
                MaxHpAddress);

        uint oldAttack =
            ActionAutoRestoreService.ReadShared32(
                AttackAddress);

        uint oldDefenseLevel =
            ActionAutoRestoreService.ReadShared32(
                DefenseAndLevelAddress);

        ushort oldDefense =
            (ushort)(oldDefenseLevel & 0xFFFFu);

        ushort level =
            (ushort)(oldDefenseLevel >> 16);

        // オーバーフロー防止
        ulong newHpLong = (ulong)oldHp + (uint)amount;
        ulong newMaxHpLong = (ulong)oldMaxHp + (uint)amount;
        ulong newAttackLong = (ulong)oldAttack + (uint)amount;
        uint newDefenseLong = (uint)oldDefense + (uint)amount;

        if (newHpLong > uint.MaxValue ||
            newMaxHpLong > uint.MaxValue ||
            newAttackLong > uint.MaxValue ||
            newDefenseLong > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                "能力値の加算で上限を超えるため中止しました。");
        }

        uint newHp = (uint)newHpLong;
        uint newMaxHp = (uint)newMaxHpLong;
        uint newAttack = (uint)newAttackLong;

        // 上位16bitのレベルを維持する
        uint newDefenseLevel =
            ((uint)level << 16) |
            (ushort)newDefenseLong;

        var changes =
            new List<(uint Address, uint OldValue)>();

        try
        {
            WriteAndVerify(
                CurrentHpAddress,
                oldHp,
                newHp,
                changes);

            WriteAndVerify(
                MaxHpAddress,
                oldMaxHp,
                newMaxHp,
                changes);

            WriteAndVerify(
                AttackAddress,
                oldAttack,
                newAttack,
                changes);

            WriteAndVerify(
                DefenseAndLevelAddress,
                oldDefenseLevel,
                newDefenseLevel,
                changes);
        }
        catch (Exception writeError)
        {
            // 途中で失敗した場合は書き込み済みの値を復元
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
        // 書き込み前に復元用データを記録
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
