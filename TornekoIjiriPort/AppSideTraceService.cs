using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TornekoIjiriPort;

internal static class AppSideTraceService
{
    private static readonly object Sync = new object();

    // 以前の実行時調査で確認した味方・敵のエントリー位置。
    // ゲームメモリへの書き込みには使用しない。
    private static readonly uint[] SlotAddresses =
    {
        0x00396800u,
        0x00396820u
    };

    private static readonly Queue<string> Records =
        new Queue<string>();

    private static readonly Dictionary<uint, string> LastSnapshots =
        new Dictionary<uint, string>();

    private static System.Threading.Timer? _timer;
    private static int _pollBusy;
    private static long _recordCount;
    private static string _logPath = "";

    private const int MaxRecords = 3000;
    private const int PollIntervalMilliseconds = 1000;

    private static string CreateLogPath()
    {
        string desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);

        if (string.IsNullOrWhiteSpace(desktop) ||
            !Directory.Exists(desktop))
        {
            desktop = AppContext.BaseDirectory;
        }

        return Path.Combine(
            desktop,
            "トルネコいじり_アプリ側メモリ監視_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss") +
            ".txt");
    }

    private static void AddRecord(string message)
    {
        string line =
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
            " | " + message;

        lock (Sync)
        {
            Records.Enqueue(line);
            _recordCount++;

            while (Records.Count > MaxRecords)
                Records.Dequeue();
        }
    }

    private static bool IsValidPointer(uint pointer)
    {
        return pointer >= 0x00100000u &&
               pointer <= 0x01FFFF00u &&
               (pointer & 3u) == 0u;
    }

    private static string CaptureSlot(uint slot)
    {
        uint slot00 =
            ActionAutoRestoreService.ReadShared32(slot);

        uint slot04 =
            ActionAutoRestoreService.ReadShared32(slot + 4u);

        uint pointer =
            ActionAutoRestoreService.ReadShared32(slot + 8u);

        var sb = new StringBuilder();

        sb.Append("SLOT=0x")
          .Append(slot.ToString("X8"));

        sb.Append(" S00=0x")
          .Append(slot00.ToString("X8"));

        sb.Append(" S04=0x")
          .Append(slot04.ToString("X8"));

        sb.Append(" PTR=0x")
          .Append(pointer.ToString("X8"));

        if (!IsValidPointer(pointer))
        {
            sb.Append(" STRUCT=INVALID");
            return sb.ToString();
        }

        uint speciesWord =
            ActionAutoRestoreService.ReadShared32(pointer + 4u);

        // +0x1D3 は +0x1D0 の最上位バイト。
        uint factionWord =
            ActionAutoRestoreService.ReadShared32(pointer + 0x1D0u);

        uint speciesId = speciesWord & 0xFFFFu;
        uint faction = (factionWord >> 24) & 0xFFu;

        sb.Append(" RUNTIME_ID=0x")
          .Append(speciesId.ToString("X4"));

        sb.Append(" FACTION=0x")
          .Append(faction.ToString("X2"));

        sb.Append(" DATA=[");

        // 個体構造体の先頭64バイトを読み取る。
        // PINEの読み取り結果はアプリ側だけに保存する。
        for (uint offset = 0u; offset <= 0x3Cu; offset += 4u)
        {
            uint value =
                ActionAutoRestoreService.ReadShared32(pointer + offset);

            if (offset != 0u)
                sb.Append(' ');

            sb.Append('+')
              .Append(offset.ToString("X2"))
              .Append('=')
              .Append(value.ToString("X8"));
        }

        sb.Append(']');

        sb.Append(" FACTION_WORD=0x")
          .Append(factionWord.ToString("X8"));

        return sb.ToString();
    }

    private static void Capture(bool forceRecord)
    {
        foreach (uint slot in SlotAddresses)
        {
            string snapshot = CaptureSlot(slot);
            bool changed = false;

            lock (Sync)
            {
                if (!LastSnapshots.TryGetValue(slot, out string? previous) ||
                    !string.Equals(previous, snapshot, StringComparison.Ordinal))
                {
                    LastSnapshots[slot] = snapshot;
                    changed = true;
                }
            }

            if (changed || forceRecord)
            {
                AddRecord(
                    (changed ? "MEMORY CHANGE | " : "MANUAL SAMPLE | ") +
                    snapshot);
            }
        }
    }

    private static void Poll(object? state)
    {
        if (Interlocked.Exchange(ref _pollBusy, 1) != 0)
            return;

        try
        {
            uint status = ActionAutoRestoreService.GetSharedStatus();

            // PINE Status 0 = 実行中。
            // 停止中は監視を行わず、ゲームの状態を変更しない。
            if (status != 0u)
                return;

            Capture(false);
        }
        catch (Exception ex)
        {
            AddRecord("POLL ERROR | " + ex);
        }
        finally
        {
            Interlocked.Exchange(ref _pollBusy, 0);
        }
    }

    internal static string Start()
    {
        lock (Sync)
        {
            if (_timer != null)
                return
                    "アプリ側メモリ監視は既に動作しています。\n" +
                    "記録件数: " + _recordCount;

            uint status =
                ActionAutoRestoreService.GetSharedStatus();

            if (status != 0u)
            {
                throw new InvalidOperationException(
                    "PCSX2をゲーム実行状態にしてから監視を開始してください。\n" +
                    "現在のPINE Status: " + status);
            }

            Records.Clear();
            LastSnapshots.Clear();
            _recordCount = 0;
            _logPath = CreateLogPath();

            _timer = new System.Threading.Timer(
                Poll,
                null,
                0,
                PollIntervalMilliseconds);
        }

        return
            "アプリ側メモリ監視を開始しました。\n\n" +
            "監視対象: 既知の味方・敵エントリー2か所\n" +
            "監視間隔: " + PollIntervalMilliseconds + " ms\n" +
            "記録先: トルネコいじりネクスト側\n" +
            "ゲームメモリへの書き込み: なし\n" +
            "コードケーブ探索: なし\n\n" +
            "ゲーム内で味方・敵を行動させた後、記録表示を押してください。";
    }

    internal static string CaptureNow()
    {
        uint status =
            ActionAutoRestoreService.GetSharedStatus();

        if (status != 1u)
        {
            throw new InvalidOperationException(
                "手動採取はPCSX2を一時停止してから実行してください。");
        }

        Capture(true);
        return ReadRecords();
    }

    internal static string Read()
    {
        return ReadRecords();
    }

    private static string ReadRecords()
    {
        string[] copy;
        long count;
        string path;

        lock (Sync)
        {
            copy = Records.ToArray();
            count = _recordCount;

            if (string.IsNullOrEmpty(_logPath))
                _logPath = CreateLogPath();

            path = _logPath;
        }

        var sb = new StringBuilder();

        sb.AppendLine("TORNEKO IJIRI NEXT - APP SIDE MEMORY MONITOR");
        sb.AppendLine("READ ONLY");
        sb.AppendLine("Game memory writes: NONE");
        sb.AppendLine("Code cave search: NONE");
        sb.AppendLine("Code cave writes: NONE");
        sb.AppendLine("Recorded changes: " + count);
        sb.AppendLine("Displayed records: " + copy.Length);
        sb.AppendLine("Log file: " + path);
        sb.AppendLine();

        if (copy.Length == 0)
        {
            sb.AppendLine("まだ記録がありません。");
        }
        else
        {
            foreach (string line in copy)
                sb.AppendLine(line);
        }

        string result = sb.ToString();

        try
        {
            File.WriteAllText(
                path,
                result,
                new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            return result +
                Environment.NewLine +
                "ログファイル保存エラー: " + ex;
        }

        return result +
            Environment.NewLine +
            "アプリ側の記録をファイルへ保存しました。";
    }

    internal static string Stop()
    {
        System.Threading.Timer? timer;

        lock (Sync)
        {
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();

        string result = ReadRecords();

        return
            "アプリ側メモリ監視を停止しました。\n\n" +
            result;
    }

    internal static string GetStatus()
    {
        lock (Sync)
        {
            return
                "監視状態: " +
                (_timer == null ? "停止中" : "動作中") +
                Environment.NewLine +
                "記録件数: " + _recordCount +
                Environment.NewLine +
                "記録ファイル: " +
                (string.IsNullOrEmpty(_logPath) ? "未作成" : _logPath);
        }
    }
}
