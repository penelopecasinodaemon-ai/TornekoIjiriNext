using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TornekoIjiriPort;

/// <summary>スライムの指輪装備中だけ17種の個別設定を上書きするランタイム処理。</summary>
internal sealed class SlimeRingRuntimeService : IDisposable
{
    private const uint InventoryBaseAddress = 0x00650110u;
    private const uint InventorySlotStride = 0x240u;
    private const int InventorySlotCount = 20;
    private const uint SlimeRingItemId = 0x00A4u;
    private const uint EquippedFlagMask = 0x00100000u;
    private const int SkillRangeDataOffset = 382206;
    private const int SkillAllyItemDataOffset = 386212;
    private const int MonsterRecordSize = 24;
    private const int MonsterRecordCount = 199;
    private const int MonsterTablePointerOffset = 16;
    private const uint EeAddressMask = 0x1FFFFFFFu;
    private const uint EeMemoryBaseAddress = 0x00180000u;
    private const uint SkillMessagePointerBase = 0x209126D0u;

    private readonly Func<byte[]?> _getCurrentBin;
    private readonly ActionAutoRestoreService _actionRestore;
    private readonly System.Threading.Timer _timer;
    private readonly object _sync = new object();
    private Dictionary<int, ushort>? _baseActions;
    private HashSet<int>? _baseIonazun;
    private Dictionary<int, bool>? _baseBarrier;
    private bool _ringActive;
    private bool _disposed;
    private int _processId;
    private DateTime _settingsWriteTime = DateTime.MinValue;
    private string _lastStatus = string.Empty;
    private string _lastBarrierTrace = string.Empty;
    private int _busy;
    private int _forceApplyRequested;

    internal event Action<string>? StatusChanged;

    internal SlimeRingRuntimeService(Func<byte[]?> getCurrentBin, ActionAutoRestoreService actionRestore)
    {
        _getCurrentBin = getCurrentBin ?? throw new ArgumentNullException(nameof(getCurrentBin));
        _actionRestore = actionRestore ?? throw new ArgumentNullException(nameof(actionRestore));
        _timer = new System.Threading.Timer(Poll, null, 1200, 500);
    }

    // Torneko4読み込み後に基準設定を更新し、指輪設定の再適用を予約する。
    internal void NotifyTorneko4Loaded()
    {
        lock (_sync)
        {
            if (_ringActive)
            {
                // Torneko4から読み込まれた設定を新しい基準値にする。
                _baseActions = Torneko4ActionStore.GetCopy();
                _baseIonazun = Torneko4ActionStore.GetIonazunForceCopy();
                _baseBarrier = global::Torneko4GanirasuBarrierStore.GetCopy();
            }
        }

        // 未装備の場合も要求を保持し、装備検出後に適用する。
        Interlocked.Exchange(ref _forceApplyRequested, 1);
    }
    private void Poll(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            if (!Pcsx2LiveSession.IsConnected || Pcsx2LiveSession.ProcessId <= 0 || Pcsx2LiveSession.MemoryLayout == null)
            {
                if (_ringActive) Deactivate("PCSX2未接続のため指輪専用設定を解除しました。");
                _processId = 0;
                return;
            }

            int pid = Pcsx2LiveSession.ProcessId;
            if (_processId != 0 && _processId != pid && _ringActive)
                Deactivate("PCSX2の接続先が変わったため指輪専用設定を解除しました。");
            _processId = pid;

            bool equipped;
            try { equipped = IsSlimeRingEquipped(); }
            catch (Exception ex)
            {
                RaiseStatus("スライムの指輪：装備状態の確認待ち / " + ex.Message);
                return;
            }

            if (!equipped)
            {
                if (_ringActive) Deactivate("スライムの指輪を外したため、モンスタークリエイター設定へ戻しました。");
                else RaiseStatus("スライムの指輪：未装備");
                return;
            }

            DateTime writeTime = File.Exists(SlimeRingSettings.SettingsPath)
                ? File.GetLastWriteTimeUtc(SlimeRingSettings.SettingsPath)
                : DateTime.MinValue;
            // Torneko4読み込み後の再適用要求を確認する。
            bool forceApply =
                Interlocked.Exchange(ref _forceApplyRequested, 0) != 0;

            bool settingsChanged = writeTime != _settingsWriteTime;

            if (!_ringActive)
            {
                Activate();
                settingsChanged = true;
            }

            if (settingsChanged || forceApply)
            {
                try
                {
                    ApplyRingSettings();
                    _settingsWriteTime = writeTime;
                }
                catch
                {
                    if (forceApply)
                        Interlocked.Exchange(ref _forceApplyRequested, 1);

                    throw;
                }
            }

            if (_ringActive)
            {
                string trace =
                    Torneko4GanirasuBarrierRuntime.ReadRuntimeTrace();

                if (!string.IsNullOrEmpty(trace) &&
                    trace != _lastBarrierTrace)
                {
                    _lastBarrierTrace = trace;
                    RaiseStatus(trace);
                }
            }
        }
        catch (Exception ex) { RaiseStatus("スライムの指輪：反映エラー / " + ex.Message); }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    private static bool IsSlimeRingEquipped()
    {
        for (int i = 0; i < InventorySlotCount; i++)
        {
            uint address = InventoryBaseAddress + (uint)i * InventorySlotStride;
            uint flag = ActionAutoRestoreService.ReadShared32(address);
            uint itemId = ActionAutoRestoreService.ReadShared32(address + 0x0Cu);
            if ((itemId & 0xFFFFu) == SlimeRingItemId && (flag & EquippedFlagMask) != 0) return true;
        }
        return false;
    }

    private void Activate()
    {
        lock (_sync)
        {
            _baseActions = Torneko4ActionStore.GetCopy();
            _baseIonazun = Torneko4ActionStore.GetIonazunForceCopy();
            _baseBarrier = global::Torneko4GanirasuBarrierStore.GetCopy();
            _ringActive = true;
            _lastBarrierTrace = string.Empty;
        }
        RaiseStatus("スライムの指輪：装備を検出。17種の専用設定を適用します。");
    }

    private void ApplyRingSettings()
    {
        byte[]? source = _getCurrentBin();
        if (source == null || source.Length != 524288) source = Pcsx2LiveSession.VanillaBin;
        if (source == null || source.Length != 524288) throw new InvalidOperationException("反映元BINがありません。先にPCSX2からバニラBINを取得してください。");

        SlimeRingSettings settings = SlimeRingSettings.Load(SlimeRingEditorForm.TargetMonsterIds);
        byte[] work = (byte[])source.Clone();
        int recordTableOffset = ResolveMonsterRecordTableOffset(work);
        var writes = new List<Tuple<int, byte[], string>>();

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            SlimeRingMonsterSettings item = settings.GetMonster(speciesId);
            int rangeOffset = checked(SkillRangeDataOffset + (speciesId + 1) * 20);
            int allyOffset = checked(SkillAllyItemDataOffset + (speciesId + 1) * 8);
            int recordOffset = checked(recordTableOffset + speciesId * MonsterRecordSize);
            CheckRange(work, rangeOffset, 20);
            CheckRange(work, allyOffset, 8);
            CheckRange(work, recordOffset + 22, 1);

            WriteUInt16(work, rangeOffset + 6, (ushort)Clamp(item.SkillRange, 0, 7));
            int messageIndex = Clamp(item.SkillMessage, 0, 41);
            uint messagePointer = messageIndex == 0 ? 0u : checked(SkillMessagePointerBase + (uint)(messageIndex - 1) * 16u);
            WriteUInt32(work, rangeOffset + 10, messagePointer);
            WriteUInt32(work, rangeOffset + 14, messagePointer);
            WriteUInt16(work, rangeOffset + 18, (ushort)Clamp(item.EnemyThrowItem, 0, 65535));
            WriteUInt16(work, allyOffset, (ushort)Clamp(item.AllyThrowItem1, 0, 65535));
            WriteUInt16(work, allyOffset + 2, (ushort)Clamp(item.AllyThrowItem2, 0, 65535));
            work[recordOffset + 22] = (byte)Clamp(item.SkillRate, 0, 255);

            writes.Add(Tuple.Create(rangeOffset, Slice(work, rangeOffset, 20), "指輪・特技データ ID=" + speciesId));
            writes.Add(Tuple.Create(allyOffset, Slice(work, allyOffset, 8), "指輪・味方投擲データ ID=" + speciesId));
            writes.Add(Tuple.Create(recordOffset + 22, Slice(work, recordOffset + 22, 1), "指輪・特技使用率 ID=" + speciesId));
        }

        string applyResult = Pcsx2BinApplier.ApplyRanges(Pcsx2LiveSession.ProcessId, Pcsx2LiveSession.MemoryLayout!, writes);
        string runtimeResult = ApplyActionAndExtraSettings(settings);
        RaiseStatus(
            "スライムの指輪：17種の個別設定を適用中" +
            Environment.NewLine + applyResult +
            Environment.NewLine + "--- 特技・バリア適用結果 ---" +
            Environment.NewLine + runtimeResult);
    }

    private string ApplyActionAndExtraSettings(SlimeRingSettings settings)
    {
        Dictionary<int, ushort> actions;
        HashSet<int> ionazun;
        Dictionary<int, bool> barriers;
        lock (_sync)
        {
            actions = new Dictionary<int, ushort>(_baseActions ?? new Dictionary<int, ushort>());
            ionazun = new HashSet<int>(_baseIonazun ?? new HashSet<int>());
            barriers = new Dictionary<int, bool>(_baseBarrier ?? global::Torneko4GanirasuBarrierStore.GetDefaultCopy());
        }

        foreach (int id in SlimeRingEditorForm.TargetMonsterIds)
        {
            SlimeRingMonsterSettings item = settings.GetMonster(id);
            // 特技ストアにはモンスター一覧番号を登録し、ディスパッチャー側で+1して照合する。
            int actionStoreId = id;

            // ガニラスバリアストアはモンスタークリエイターと同じ番号を使う。
            int barrierStoreId = id;

            if (item.ActionId >= 0)
                actions[actionStoreId] = checked((ushort)item.ActionId);
            else
                actions.Remove(actionStoreId);

            if (item.CoromageIonazunAlways)
            {
                actions[actionStoreId] = 0xBF;
                ionazun.Add(actionStoreId);
            }
            else
            {
                ionazun.Remove(actionStoreId);
            }

            // 指輪側でチェックしたモンスターだけ、
            // モンスタークリエイター側の設定より優先して所持させる。
            // チェックなしの場合は基準設定をそのまま維持する。
            if (item.GanirasuBarrier)
            {
                barriers[barrierStoreId] = true;
            }
        }

        Torneko4ActionStore.ReplaceAll(actions);
        for (int i = 0; i < 199; i++) Torneko4ActionStore.SetIonazunForce(i, ionazun.Contains(i));
        global::Torneko4GanirasuBarrierStore.ReplaceAll(barriers);

        if (!_actionRestore.TryApply(actions, out string actionMessage))
            throw new InvalidOperationException("特技変更の適用に失敗しました: " + actionMessage);

        if (!Torneko4GanirasuBarrierRuntime.TryApply(barriers, out string barrierMessage))
            throw new InvalidOperationException("ガニラスバリアの適用に失敗しました: " + barrierMessage);

        return actionMessage + Environment.NewLine + barrierMessage;
    }

    private void Deactivate(string status)
    {
        lock (_sync)
        {
            if (!_ringActive) return;
            if (_baseActions != null) Torneko4ActionStore.ReplaceAll(_baseActions);
            if (_baseIonazun != null)
                for (int i = 0; i < 199; i++) Torneko4ActionStore.SetIonazunForce(i, _baseIonazun.Contains(i));
            if (_baseBarrier != null) global::Torneko4GanirasuBarrierStore.ReplaceAll(_baseBarrier);
            _ringActive = false;
            _lastBarrierTrace = string.Empty;
            _baseActions = null;
            _baseIonazun = null;
            _baseBarrier = null;
        }

        try
        {
            _actionRestore.TryApply(Torneko4ActionStore.GetCopy(), out _);
            Torneko4GanirasuBarrierRuntime.TryApply(global::Torneko4GanirasuBarrierStore.GetCopy(), out _);
        }
        catch { }

        try { ApplyBaselineSkillData(); }
        catch (Exception ex)
        {
            RaiseStatus(status + " / 基準データ復元エラー: " + ex.Message);
            return;
        }
        _settingsWriteTime = DateTime.MinValue;
        RaiseStatus(status);
    }

    private void ApplyBaselineSkillData()
    {
        byte[]? source = _getCurrentBin();
        if (source == null || source.Length != 524288) source = Pcsx2LiveSession.VanillaBin;
        if (source == null || source.Length != 524288 || Pcsx2LiveSession.MemoryLayout == null || Pcsx2LiveSession.ProcessId <= 0) return;

        int recordTableOffset = ResolveMonsterRecordTableOffset(source);
        var writes = new List<Tuple<int, byte[], string>>();
        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            int rangeOffset = SkillRangeDataOffset + (speciesId + 1) * 20;
            int allyOffset = SkillAllyItemDataOffset + (speciesId + 1) * 8;
            int recordOffset = recordTableOffset + speciesId * MonsterRecordSize;
            CheckRange(source, rangeOffset, 20);
            CheckRange(source, allyOffset, 8);
            CheckRange(source, recordOffset + 22, 1);
            writes.Add(Tuple.Create(rangeOffset, Slice(source, rangeOffset, 20), "指輪解除・特技データ ID=" + speciesId));
            writes.Add(Tuple.Create(allyOffset, Slice(source, allyOffset, 8), "指輪解除・味方投擲データ ID=" + speciesId));
            writes.Add(Tuple.Create(recordOffset + 22, Slice(source, recordOffset + 22, 1), "指輪解除・特技使用率 ID=" + speciesId));
        }
        Pcsx2BinApplier.ApplyRanges(Pcsx2LiveSession.ProcessId, Pcsx2LiveSession.MemoryLayout, writes);
    }

    private static int ResolveMonsterRecordTableOffset(byte[] bin)
    {
        if (bin == null || bin.Length < 524288) throw new InvalidOperationException("BINサイズが不正です。");
        uint rawPointer = BitConverter.ToUInt32(bin, MonsterTablePointerOffset);
        uint correction = (uint)bin[3] << 24;
        uint physicalAddress = unchecked(rawPointer + correction) & EeAddressMask;
        if (physicalAddress < EeMemoryBaseAddress) throw new InvalidOperationException("モンスターテーブルのポインタが不正です。");
        long offset = (long)physicalAddress - EeMemoryBaseAddress + MonsterRecordSize;
        if (offset < 0 || offset + (long)MonsterRecordCount * MonsterRecordSize > bin.Length)
            throw new InvalidOperationException("モンスターテーブルがBIN範囲外です。");
        return checked((int)offset);
    }

    private static byte[] Slice(byte[] source, int offset, int length)
    {
        CheckRange(source, offset, length);
        byte[] result = new byte[length];
        Array.Copy(source, offset, result, 0, length);
        return result;
    }
    private static void CheckRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
            throw new InvalidOperationException("BIN書き込み範囲が不正です: 0x" + offset.ToString("X"));
    }
    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        CheckRange(data, offset, 2); data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8);
    }
    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        CheckRange(data, offset, 4); data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8); data[offset + 2] = (byte)(value >> 16); data[offset + 3] = (byte)(value >> 24);
    }
    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    private void RaiseStatus(string message)
    {
        if (message == _lastStatus) return;
        _lastStatus = message;
        StatusChanged?.Invoke(message);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _timer.Dispose();
        if (_ringActive) Deactivate("アプリ終了：指輪専用設定を解除しました。");
        _disposed = true;
    }
}