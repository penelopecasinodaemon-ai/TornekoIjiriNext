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
    private Dictionary<int, bool>? _baseBarrier;
    private bool _ringActive;
    private static volatile bool _overlayActive;
    private static readonly object OverlaySync = new object();
    private static HashSet<int> _overlaySpeciesIds = new HashSet<int>();

    // 指輪装備中かつ、モンスタークリエイターの
    // スライム系統フラグが有効な種族だけを上書き対象にする。
    internal static bool IsOverlayActiveForMonster(int speciesId)
    {
        lock (OverlaySync)
            return _overlayActive && _overlaySpeciesIds.Contains(speciesId);
    }

    private static void SetOverlaySpeciesIds(IEnumerable<int> speciesIds)
    {
        lock (OverlaySync)
            _overlaySpeciesIds = new HashSet<int>(speciesIds);
    }

    private static bool OverlaySpeciesIdsMatch(ISet<int> speciesIds)
    {
        lock (OverlaySync)
            return _overlaySpeciesIds.SetEquals(speciesIds);
    }
    private bool _disposed;
    private int _processId;
    private bool? _lastObservedEquipped;
    private bool _ringAllyItemDataApplied;
    private bool _ringSkillStatApplied;
    private bool _ringAllyActionHooksApplied;
    private bool _ringActionSettingsSubmitted;
    private DateTime _settingsWriteTime = DateTime.MinValue;
    private string _lastStatus = string.Empty;
    private string _lastBarrierTrace = string.Empty;
    private int _busy;
    private int _forceApplyRequested;

    // モンスタークリエイターの特技使用率・範囲の変更検出用
    private uint _creatorSkillSettingsFingerprint;
    private bool _hasCreatorSkillSettingsFingerprint;

    internal event Action<string>? StatusChanged;

    internal SlimeRingRuntimeService(Func<byte[]?> getCurrentBin, ActionAutoRestoreService actionRestore)
    {
        _getCurrentBin = getCurrentBin ?? throw new ArgumentNullException(nameof(getCurrentBin));
        _actionRestore = actionRestore ?? throw new ArgumentNullException(nameof(actionRestore));
        _timer = new System.Threading.Timer(Poll, null, 1200, 500);
    }

    // 互換性のためメソッドは残すが、読み取り専用監視では何も適用しない。
    internal void NotifyTorneko4Loaded()
    {
    }

    // 装備状態を監視し、味方専用データ・特技率/範囲・行動IDを反映する。
    // 行動IDは味方側の特技判定・行動ID読込・対象判定を一組として適用する。
    // 敵側はモンスタークリエイター設定を維持し、イオナズン確定判定も既存処理を使う。
    private void Poll(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _busy, 1) != 0)
            return;

        try
        {
            if (!Pcsx2LiveSession.IsConnected ||
                Pcsx2LiveSession.ProcessId <= 0 ||
                Pcsx2LiveSession.MemoryLayout == null)
            {
                if (_ringActionSettingsSubmitted)
                {
                    _actionRestore.TryApplyAllyRingOverrides(
                        Array.Empty<KeyValuePair<int, ushort>>(),
                        new HashSet<int>(),
                        out _);
                    _ringActionSettingsSubmitted = false;
                }

                _processId = 0;
                _lastObservedEquipped = null;
                _ringAllyItemDataApplied = false;
                _ringAllyActionHooksApplied = false;
                _overlayActive = false;
                SetOverlaySpeciesIds(Array.Empty<int>());
                RaiseStatus("スライムの指輪：PCSX2接続待ち（読み取り専用監視）");
                return;
            }

            int pid = Pcsx2LiveSession.ProcessId;
            if (_processId != pid)
            {
                if (_ringActionSettingsSubmitted)
                {
                    _actionRestore.TryApplyAllyRingOverrides(
                        Array.Empty<KeyValuePair<int, ushort>>(),
                        new HashSet<int>(),
                        out _);
                    _ringActionSettingsSubmitted = false;
                }

                _processId = pid;
                _lastObservedEquipped = null;
                _ringAllyItemDataApplied = false;
                _ringAllyActionHooksApplied = false;
            }

            // 装備スロットを読み取り、装備状態の変化時だけ指輪専用処理を更新する。
            // 指輪専用の味方設定を段階ごとに適用する。
            bool equipped = IsSlimeRingEquipped();

            if (_lastObservedEquipped != equipped)
            {
                _overlayActive = false;
                SetOverlaySpeciesIds(Array.Empty<int>());

                if (equipped)
                {
                    // 段階1：味方専用投擲データ。
                    ApplyRingAllyItemDataOnly(true);

                    // 段階2：特技使用率・範囲フック。
                    ApplyRingSkillStatOnly();

                    // 段階3：味方専用の特技判定・行動ID読込・対象判定を一括適用する。
                    // 行動者の陣営フラグと種族IDを読み取り、味方の対象スライムだけ指輪の行動IDを返す。
                    // 敵側では元の行動IDを返し、共有AIテーブル全体の入れ替えは行わない。
                    // 5か所のフックがすべて検証されるまで共有有効フラグは0のまま。
                    ApplyRingAllyActionOnly(true);
                }
                else
                {
                    // 指輪上書き設定を必ず消去し、前回接続時の残留設定も防ぐ。
                    ApplyRingAllyActionOnly(false);

                    // 次に特技率フックを元へ戻し、その後に投擲データを復元する。
                    if (_ringSkillStatApplied)
                    {
                        SlimeRingSkillStatRuntimeService.RestoreHook();
                        _ringSkillStatApplied = false;
                    }

                    if (_ringAllyItemDataApplied)
                        ApplyRingAllyItemDataOnly(false);
                }

                _lastObservedEquipped = equipped;

                RaiseStatus(equipped
                    ? "スライムの指輪：段階3適用済み（味方専用データ＋特技使用率・範囲＋味方専用行動ID）"
                    : "スライムの指輪：未装備（指輪専用処理を解除）");
            }
        }
        catch (Exception ex)
        {
            RaiseStatus("スライムの指輪：装備状態の読み取りエラー / " + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // 段階3：味方専用の特技判定・行動ID読込・対象判定を一括適用する。
    // 敵は通常設定テーブルを使用し、既存のイオナズン確定判定処理を維持する。
    private void ApplyRingAllyActionOnly(bool equipped)
    {
        if (!equipped)
        {
            bool restored = _actionRestore.TryApplyAllyRingOverrides(
                Array.Empty<KeyValuePair<int, ushort>>(),
                new HashSet<int>(),
                out string restoreMessage);

            // TryApplyは接続可否にかかわらず先に内部の指輪設定を空にする。
            _ringActionSettingsSubmitted = false;
            _ringAllyActionHooksApplied = false;

            if (!restored)
                throw new InvalidOperationException(restoreMessage);

            RaiseStatus(restoreMessage);
            return;
        }

        byte[]? source = _getCurrentBin();
        if (source == null || source.Length != 524288)
            source = Pcsx2LiveSession.VanillaBin;

        if (source == null || source.Length != 524288)
            throw new InvalidOperationException(
                "段階3：基準BINがありません。");

        int recordTableOffset = ResolveMonsterRecordTableOffset(source);
        HashSet<int> slimeFamilyIds =
            GetSlimeFamilySpeciesIds(source, recordTableOffset);
        SlimeRingSettings settings =
            SlimeRingSettings.Load(SlimeRingEditorForm.TargetMonsterIds);

        var ringActions = new Dictionary<int, ushort>();
        var ionazunForce = new HashSet<int>();

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            if (!slimeFamilyIds.Contains(speciesId))
                continue;

            SlimeRingMonsterSettings item = settings.GetMonster(speciesId);
            if (item.ActionId < 0)
                continue;

            if (item.ActionId > ushort.MaxValue)
                throw new InvalidOperationException(
                    "段階3：行動IDが範囲外です。種族ID=" + speciesId);

            ringActions[speciesId] = (ushort)item.ActionId;

            // 既存のイオナズン確定チェック設定を保持して渡す。
            // 実際の分岐・連発判定はActionAutoRestoreServiceの既存処理を使用する。
            if (item.CoromageIonazunAlways)
                ionazunForce.Add(speciesId);
        }

        // 接続エラー時もActionAutoRestoreService側に残った設定を追跡できるよう、
        // 呼び出し前に「設定を渡した」状態にする。
        _ringActionSettingsSubmitted = true;

        if (!_actionRestore.TryApplyAllyRingOverrides(
            ringActions,
            ionazunForce,
            out string message))
        {
            throw new InvalidOperationException(message);
        }

        _ringAllyActionHooksApplied = ringActions.Count > 0;
        RaiseStatus(message);
    }

    // 段階2：特技使用率・範囲フックだけを設置する。
    // 味方の特技率・範囲を指輪設定へ切り替えるが、行動IDは変更しない。
    private void ApplyRingSkillStatOnly()
    {
        byte[]? source = _getCurrentBin();
        if (source == null || source.Length != 524288)
            source = Pcsx2LiveSession.VanillaBin;

        if (source == null || source.Length != 524288)
            throw new InvalidOperationException(
                "段階2：基準BINがありません。");

        int recordTableOffset = ResolveMonsterRecordTableOffset(source);
        HashSet<int> slimeFamilyIds =
            GetSlimeFamilySpeciesIds(source, recordTableOffset);
        SlimeRingSettings settings =
            SlimeRingSettings.Load(SlimeRingEditorForm.TargetMonsterIds);

        try
        {
            string result = SlimeRingSkillStatRuntimeService.Apply(
                source,
                recordTableOffset,
                settings,
                slimeFamilyIds);

            _ringSkillStatApplied = true;
            RaiseStatus(
                "スライムの指輪：段階2・特技使用率／範囲フック適用済み" +
                Environment.NewLine +
                "行動IDディスパッチャー：未設置" +
                Environment.NewLine +
                result);
        }
        catch
        {
            // 設置途中で失敗した場合、当サービス所有のフックだけを元に戻す。
            try
            {
                SlimeRingSkillStatRuntimeService.RestoreHook();
            }
            catch
            {
                // 元命令への復元に失敗した場合は、元の例外を優先して通知する。
            }

            _ringSkillStatApplied = false;
            throw;
        }
    }


    // 段階1専用。指輪の味方専用投擲データだけを更新する。
    // 特技使用率フック、行動ディスパッチャー、イオナズン分岐、バリアには触れない。
    private void ApplyRingAllyItemDataOnly(bool equipped)
    {
        byte[]? source = _getCurrentBin();
        if (source == null || source.Length != 524288)
            source = Pcsx2LiveSession.VanillaBin;

        if (source == null || source.Length != 524288)
            throw new InvalidOperationException(
                "段階1：基準BINがありません。先にPCSX2からバニラBINを取得してください。");

        if (Pcsx2LiveSession.MemoryLayout == null ||
            Pcsx2LiveSession.ProcessId <= 0)
        {
            throw new InvalidOperationException(
                "段階1：PCSX2のメモリ接続情報がありません。");
        }

        SlimeRingSettings settings =
            SlimeRingSettings.Load(SlimeRingEditorForm.TargetMonsterIds);

        byte[] work = (byte[])source.Clone();
        int recordTableOffset = ResolveMonsterRecordTableOffset(source);

        HashSet<int> slimeFamilyIds =
            GetSlimeFamilySpeciesIds(source, recordTableOffset);

        var writes = new List<Tuple<int, byte[], string>>();

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            int allyOffset =
                checked(SkillAllyItemDataOffset + (speciesId + 1) * 8);

            CheckRange(work, allyOffset, 8);

            // 装備中かつスライム系統フラグが有効な種族だけ指輪値を使う。
            // 解除時・対象外種族はBIN基準値をそのまま書き戻す。
            if (equipped && slimeFamilyIds.Contains(speciesId))
            {
                SlimeRingMonsterSettings item =
                    settings.GetMonster(speciesId);

                WriteUInt16(
                    work,
                    allyOffset,
                    (ushort)Clamp(item.AllyThrowItem1, 0, 65535));

                WriteUInt16(
                    work,
                    allyOffset + 2,
                    (ushort)Clamp(item.AllyThrowItem2, 0, 65535));
            }

            writes.Add(Tuple.Create(
                allyOffset,
                Slice(work, allyOffset, 8),
                "段階1・味方専用投擲データ ID=" + speciesId));
        }

        Pcsx2BinApplier.ApplyRanges(
            Pcsx2LiveSession.ProcessId,
            Pcsx2LiveSession.MemoryLayout!,
            writes);

        _ringAllyItemDataApplied = equipped;
    }

    private static uint CalculateCreatorSkillSettingsFingerprint(
        byte[] bin,
        int recordTableOffset)
    {
        if (bin == null || bin.Length != 524288)
            throw new InvalidOperationException(
                "特技設定の監視対象BINが不正です。");

        // FNV-1a 32bit
        uint hash = 2166136261u;

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            int recordOffset =
                checked(recordTableOffset + speciesId * MonsterRecordSize);

            int rangeOffset =
                checked(SkillRangeDataOffset + (speciesId + 1) * 20);

            if (recordOffset < 0 ||
                recordOffset + MonsterRecordSize > bin.Length)
            {
                throw new InvalidOperationException(
                    "特技使用率の監視範囲が不正です。種族ID=" + speciesId);
            }

            if (rangeOffset < 0 ||
                rangeOffset + 8 > bin.Length)
            {
                throw new InvalidOperationException(
                    "特技範囲の監視範囲が不正です。種族ID=" + speciesId);
            }

            // モンスターレコード +22: 特技使用率
            hash ^= bin[recordOffset + 22];
            hash *= 16777619u;

            // 特技範囲データ +6～+7: 特技使用範囲
            hash ^= bin[rangeOffset + 6];
            hash *= 16777619u;

            hash ^= bin[rangeOffset + 7];
            hash *= 16777619u;
        }

        return hash;
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
            // 敵用ディスパッチャーには触れない。ActionIdは味方側専用処理が担当する。
            _baseBarrier = global::Torneko4GanirasuBarrierStore.GetCopy();
            _ringActive = true;
            _overlayActive = true;
            _lastBarrierTrace = string.Empty;
        }
        SetOverlaySpeciesIds(Array.Empty<int>());
        RaiseStatus("スライムの指輪：装備を検出。スライム系統フラグを確認します。");
    }

    private void ApplyRingSettings()
    {
        byte[]? source = _getCurrentBin();
        if (source == null || source.Length != 524288) source = Pcsx2LiveSession.VanillaBin;
        if (source == null || source.Length != 524288) throw new InvalidOperationException("反映元BINがありません。先にPCSX2からバニラBINを取得してください。");

        SlimeRingSettings settings = SlimeRingSettings.Load(SlimeRingEditorForm.TargetMonsterIds);
        byte[] work = (byte[])source.Clone();
        var writes = new List<Tuple<int, byte[], string>>();

        int recordTableOffset = ResolveMonsterRecordTableOffset(source);

        HashSet<int> slimeFamilyIds =
            GetSlimeFamilySpeciesIds(source, recordTableOffset);

        SetOverlaySpeciesIds(slimeFamilyIds);

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            int allyOffset =
                checked(SkillAllyItemDataOffset + (speciesId + 1) * 8);

            CheckRange(work, allyOffset, 8);

            // スライム系統フラグが有効な種族だけ指輪設定を適用する。
            // 対象外の種族はクリエイターの基準値を維持する。
            if (slimeFamilyIds.Contains(speciesId))
            {
                SlimeRingMonsterSettings item =
                    settings.GetMonster(speciesId);

                WriteUInt16(
                    work,
                    allyOffset,
                    (ushort)Clamp(item.AllyThrowItem1, 0, 65535));

                WriteUInt16(
                    work,
                    allyOffset + 2,
                    (ushort)Clamp(item.AllyThrowItem2, 0, 65535));
            }

            // =============================================
            // 系統フラグが後から外れた種族も基準値に戻せるよう、
            // 対象全種族のデータを書き込む。
            writes.Add(Tuple.Create(
                allyOffset,
                Slice(work, allyOffset, 8),
                "指輪・味方専用投擲データ ID=" + speciesId));
        }

        Pcsx2BinApplier.ApplyRanges(
            Pcsx2LiveSession.ProcessId,
            Pcsx2LiveSession.MemoryLayout!,
            writes);

        string skillStatResult =
            SlimeRingSkillStatRuntimeService.Apply(
                source,
                recordTableOffset,
                settings,
                slimeFamilyIds);

        string applyResult =
            "スライム系統フラグが有効な対象種族数: " +
            slimeFamilyIds.Count +
            Environment.NewLine +
            "味方専用投擲データを適用しました。" +
            Environment.NewLine +
            skillStatResult;

        string runtimeResult =
            ApplyActionAndExtraSettings(settings, slimeFamilyIds);

        RaiseStatus(
            "スライムの指輪：スライム系統フラグが有効な味方種族へ適用中" +
            Environment.NewLine + applyResult +
            Environment.NewLine + "--- 特技・バリア適用結果 ---" +
            Environment.NewLine + runtimeResult);
    }

    private string ApplyActionAndExtraSettings(
        SlimeRingSettings settings,
        ISet<int> slimeFamilyIds)
    {
        Dictionary<int, bool> barriers;

        lock (_sync)
        {
            barriers = new Dictionary<int, bool>(
                _baseBarrier ??
                global::Torneko4GanirasuBarrierStore.GetDefaultCopy());
        }

        // 指輪の特技設定は独立テーブルへ渡す。
        // ディスパッチャー側で「味方なら指輪設定を優先、敵なら通常設定」を行う。
        var ringActions = new Dictionary<int, ushort>();
        var ringIonazunForce = new HashSet<int>();

        foreach (int id in SlimeRingEditorForm.TargetMonsterIds)
        {
            if (!slimeFamilyIds.Contains(id))
                continue;

            SlimeRingMonsterSettings item = settings.GetMonster(id);

            if (item.ActionId > 0 &&
                item.ActionId <= byte.MaxValue &&
                Array.Exists(
                    ActionAutoRestoreService.ActionChoices,
                    x => x.Id == item.ActionId))
            {
                ringActions[id] = (ushort)item.ActionId;

                if (item.ActionId == 0x00BF && item.CoromageIonazunAlways)
                    ringIonazunForce.Add(id);
            }

            // 指輪のガニラスバリアは個体別陣営判定が未実装のため、ここでは追加しない。

        }

        if (!_actionRestore.TryApplyAllyRingOverrides(
            ringActions,
            ringIonazunForce,
            out string actionMessage))
        {
            throw new InvalidOperationException(
                "指輪専用の味方特技設定を反映できません: " + actionMessage);
        }

        if (!Torneko4GanirasuBarrierRuntime.TryApply(
            barriers,
            out string barrierMessage))
        {
            throw new InvalidOperationException(
                "指輪専用ガニラスバリアの適用に失敗しました: " +
                barrierMessage);
        }

        return
            "通常設定：敵・味方共通でモンスタークリエイター設定を使用。" +
            Environment.NewLine +
            "指輪装備中：対象17種の味方だけ指輪の特技設定を優先。" +
            Environment.NewLine +
            "敵は指輪設定の対象外です。" +
            Environment.NewLine +
            actionMessage +
            Environment.NewLine +
            "ガニラスバリア：個体別陣営判定対応まで指輪からの追加を停止。" +
            Environment.NewLine +
            "クリエイター基準設定を維持します。" +
            Environment.NewLine +
            barrierMessage;
    }
    private void Deactivate(string status)
    {
        Dictionary<int, bool>? baseBarrier;

        lock (_sync)
        {
            if (!_ringActive) return;

            baseBarrier = _baseBarrier == null
                ? null
                : new Dictionary<int, bool>(_baseBarrier);

            _ringActive = false;
            _overlayActive = false;
            _lastBarrierTrace = string.Empty;
            _baseBarrier = null;
        }

        SetOverlaySpeciesIds(Array.Empty<int>());

        // 使用率・範囲の陣営判定フックを解除する。
        try
        {
            SlimeRingSkillStatRuntimeService.RestoreHook();
        }
        catch (Exception ex)
        {
            RaiseStatus(status + " / 特技使用率・範囲フック解除エラー: " + ex.Message);
            return;
        }

        // 指輪専用の味方上書きだけを解除し、通常設定へ戻す。
        _actionRestore.TryApplyAllyRingOverrides(
            new Dictionary<int, ushort>(),
            new HashSet<int>(),
            out _);

        try
        {
            if (baseBarrier != null)
            {
                Torneko4GanirasuBarrierRuntime.TryApply(
                    baseBarrier,
                    out _);
            }
        }
        catch (Exception ex)
        {
            RaiseStatus(status + " / 実行時設定の復元エラー: " + ex.Message);
        }


        try
        {
            ApplyBaselineSkillData();
        }
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

        var writes = new List<Tuple<int, byte[], string>>();

        // 実験版・使用率復元用モンスターレコード位置
        int recordTableOffset =
            ResolveMonsterRecordTableOffset(source);

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            int rangeOffset = SkillRangeDataOffset + (speciesId + 1) * 20;
            int allyOffset = SkillAllyItemDataOffset + (speciesId + 1) * 8;
            CheckRange(source, rangeOffset, 20);
            CheckRange(source, allyOffset, 8);

            // 旧バージョンで指輪設定が共有領域へ残っていた場合も、
            // クリエイターの基準値へ確実に復元する。
            writes.Add(Tuple.Create(
                rangeOffset,
                Slice(source, rangeOffset, 20),
                "指輪解除・クリエイター特技データ復元 ID=" + speciesId));

            writes.Add(Tuple.Create(
                allyOffset,
                Slice(source, allyOffset, 8),
                "指輪解除・味方専用投擲データ ID=" + speciesId));

            // 実験版・指輪解除時の特技使用率A復元
            int recordOffset =
                recordTableOffset + speciesId * MonsterRecordSize;

            int rateOffset = recordOffset + 22;

            CheckRange(source, rateOffset, 1);

            writes.Add(Tuple.Create(
                rateOffset,
                Slice(source, rateOffset, 1),
                "実験版・指輪解除時の特技使用率A復元 ID=" + speciesId));
        }
        Pcsx2BinApplier.ApplyRanges(
            Pcsx2LiveSession.ProcessId,
            Pcsx2LiveSession.MemoryLayout,
            writes);
    }

    private static HashSet<int> GetSlimeFamilySpeciesIds(
        byte[] bin,
        int recordTableOffset)
    {
        // モンスタークリエイターの系統フラグ bit 5 = スライム系統
        const byte SlimeFamilyFlagMask = 0x20;

        var result = new HashSet<int>();

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            int recordOffset = checked(
                recordTableOffset + speciesId * MonsterRecordSize);

            CheckRange(bin, recordOffset, MonsterRecordSize);

            if ((bin[recordOffset + 6] & SlimeFamilyFlagMask) != 0)
                result.Add(speciesId);
        }

        return result;
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
        if (_disposed)
            return;

        _disposed = true;
        _timer.Dispose();
        _overlayActive = false;
        SetOverlaySpeciesIds(Array.Empty<int>());

        // 終了時もゲームメモリには触れない。
    }
}