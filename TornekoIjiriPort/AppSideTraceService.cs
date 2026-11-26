using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace TornekoIjiriPort;

internal static class AppSideTraceService
{
    private static readonly object Sync = new();

    private static readonly uint[] SlotAddresses =
    {
        0x00396800u,
        0x00396820u
    };

    private static readonly Queue<string> Records = new();
    private static readonly Dictionary<uint, uint[]> LastSnapshots = new();

    private static System.Threading.Timer? _timer;
    private static int _pollBusy;
    private static long _recordCount;
    private static string _logPath = "";
    private static string _csvPath = "";

    private const int MaxRecords = 30000;
    private const int PollIntervalMilliseconds = 500;
    private const uint StructureSize = 0x200u;

    private static string CreatePath(string extension, string prefix)
    {
        string desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);

        if (string.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop))
            desktop = AppContext.BaseDirectory;

        return Path.Combine(
            desktop,
            prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + extension);
    }

    private static void AddRecord(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
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

    private static uint Read(uint address)
    {
        return ActionAutoRestoreService.ReadShared32(address);
    }

    private static uint GetPointer(uint slot)
    {
        return Read(slot + 8u);
    }

    private static string GetIdentity(uint slot, uint pointer)
    {
        uint slot00 = Read(slot);
        uint slot04 = Read(slot + 4u);

        uint speciesWord = Read(pointer + 4u);
        uint factionWord = Read(pointer + 0x1D0u);

        uint runtimeId = speciesWord & 0xFFFFu;
        uint faction = (factionWord >> 24) & 0xFFu;

        return
            "SLOT=0x" + slot.ToString("X8") +
            " S00=0x" + slot00.ToString("X8") +
            " S04=0x" + slot04.ToString("X8") +
            " PTR=0x" + pointer.ToString("X8") +
            " RUNTIME_ID=0x" + runtimeId.ToString("X4") +
            " FACTION=0x" + faction.ToString("X2");
    }

    private static uint[] ReadWords(uint pointer, uint length)
    {
        int count = checked((int)(length / 4u));
        uint[] values = new uint[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = Read(pointer + (uint)(i * 4));
        }

        return values;
    }

    private static void CaptureFast()
    {
        foreach (uint slot in SlotAddresses)
        {
            uint pointer = GetPointer(slot);

            if (!IsValidPointer(pointer))
            {
                AddRecord(
                    "INVALID POINTER | SLOT=0x" +
                    slot.ToString("X8") +
                    " PTR=0x" + pointer.ToString("X8"));
                continue;
            }

            string identity = GetIdentity(slot, pointer);
            uint[] values = ReadWords(pointer, 0x40u);

            bool first;
            uint[]? previous;

            lock (Sync)
            {
                first = !LastSnapshots.TryGetValue(slot, out previous);
                LastSnapshots[slot] = values;
            }

            if (first)
            {
                AddRecord("INITIAL | " + identity);
                continue;
            }

            if (previous == null)
                continue;

            for (int i = 0; i < values.Length; i++)
            {
                if (previous[i] == values[i])
                    continue;

                AddRecord(
                    "WORD CHANGE | " + identity +
                    " OFFSET=+0x" + (i * 4).ToString("X3") +
                    " BEFORE=0x" + previous[i].ToString("X8") +
                    " AFTER=0x" + values[i].ToString("X8"));
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

            // PINE Status 0 = 実行中
            if (status == 0u)
                CaptureFast();
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
                return "監視は既に動作しています。\n" + GetStatusCore();

            uint status = ActionAutoRestoreService.GetSharedStatus();

            if (status != 0u)
            {
                throw new InvalidOperationException(
                    "PCSX2をゲーム実行状態にしてから開始してください。");
            }

            Records.Clear();
            LastSnapshots.Clear();
            _recordCount = 0;

            _logPath = CreatePath(
                ".txt",
                "トルネコいじり_詳細メモリ監視_");

            _csvPath = CreatePath(
                ".csv",
                "トルネコいじり_詳細メモリ差分_");

            _timer = new System.Threading.Timer(
                Poll,
                null,
                0,
                PollIntervalMilliseconds);
        }

        return
            "詳細メモリ監視を開始しました。\n\n" +
            "監視対象: 味方・敵スロット2か所\n" +
            "自動監視範囲: 個体データ先頭64バイト\n" +
            "監視間隔: " + PollIntervalMilliseconds + " ms\n" +
            "詳細採取範囲: 個体データ512バイト\n" +
            "詳細採取: PCSX2一時停止中に実行\n" +
            "保存先: デスクトップ\n" +
            "ゲームメモリへの書き込み: なし\n" +
            "コードケーブ探索: なし";
    }

    internal static string Mark(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            label = "ユーザー目印";

        AddRecord("===== USER MARKER ===== " + label.Trim());

        return "目印を記録しました。\n" + label.Trim();
    }

    // 既存の呼び出し元との互換性を維持
    internal static string CaptureNow()
    {
        return CaptureDetailed();
    }
    internal static string CaptureDetailed()
    {
        uint status = ActionAutoRestoreService.GetSharedStatus();

        if (status != 1u)
        {
            throw new InvalidOperationException(
                "詳細採取はPCSX2を一時停止してから実行してください。");
        }

        var output = new StringBuilder();

        output.AppendLine("===== DETAILED MEMORY SNAPSHOT =====");
        output.AppendLine("TIME=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        output.AppendLine("READ ONLY");
        output.AppendLine("STRUCTURE SIZE=0x200");

        foreach (uint slot in SlotAddresses)
        {
            uint pointer = GetPointer(slot);

            if (!IsValidPointer(pointer))
            {
                output.AppendLine(
                    "INVALID POINTER SLOT=0x" + slot.ToString("X8") +
                    " PTR=0x" + pointer.ToString("X8"));
                continue;
            }

            string identity = GetIdentity(slot, pointer);
            uint[] values = ReadWords(pointer, StructureSize);

            output.AppendLine();
            output.AppendLine(identity);

            for (int i = 0; i < values.Length; i++)
            {
                output.AppendLine(
                    "OFFSET=+0x" + (i * 4).ToString("X3") +
                    " VALUE=0x" + values[i].ToString("X8") +
                    " DEC=" + values[i].ToString(CultureInfo.InvariantCulture));
            }

            AddRecord("DETAILED SNAPSHOT | " + identity);

            lock (Sync)
            {
                LastSnapshots[slot] = values.Take(16).ToArray();
            }
        }

        string result = output.ToString();

        string path;
        lock (Sync)
        {
            if (string.IsNullOrEmpty(_logPath))
                _logPath = CreatePath(".txt", "トルネコいじり_詳細メモリ監視_");

            path = _logPath;
        }

        File.AppendAllText(
            path,
            Environment.NewLine + result,
            new UTF8Encoding(false));

        return result +
            Environment.NewLine +
            "詳細スナップショットを保存しました: " + path;
    }

    internal static string Read()
    {
        string[] copy;
        long count;
        string path;
        string csv;

        lock (Sync)
        {
            copy = Records.ToArray();
            count = _recordCount;

            if (string.IsNullOrEmpty(_logPath))
                _logPath = CreatePath(".txt", "トルネコいじり_詳細メモリ監視_");

            if (string.IsNullOrEmpty(_csvPath))
                _csvPath = CreatePath(".csv", "トルネコいじり_詳細メモリ差分_");

            path = _logPath;
            csv = _csvPath;
        }

        var sb = new StringBuilder();

        sb.AppendLine("TORNEKO IJIRI NEXT - DETAILED APP SIDE MONITOR");
        sb.AppendLine("READ ONLY");
        sb.AppendLine("Game memory writes: NONE");
        sb.AppendLine("Code cave search: NONE");
        sb.AppendLine("Recorded records: " + count);
        sb.AppendLine("Displayed records: " + copy.Length);
        sb.AppendLine("Log file: " + path);
        sb.AppendLine("CSV file: " + csv);
        sb.AppendLine();

        foreach (string line in copy)
            sb.AppendLine(line);

        string result = sb.ToString();

        File.WriteAllText(path, result, new UTF8Encoding(false));

        var csvText = new StringBuilder();
        csvText.AppendLine("Timestamp,Record");

        foreach (string line in copy)
        {
            int separator = line.IndexOf(" | ", StringComparison.Ordinal);
            string timestamp = separator >= 0 ? line[..separator] : "";
            string body = separator >= 0 ? line[(separator + 3)..] : line;

            csvText.Append('"').Append(timestamp.Replace("\"", "\"\""))
                .Append("\",\"")
                .Append(body.Replace("\"", "\"\""))
                .AppendLine("\"");
        }

        File.WriteAllText(csv, csvText.ToString(), new UTF8Encoding(true));

        return result +
            Environment.NewLine +
            "TXTとCSVへ保存しました。";
    }

    private static string GetStatusCore()
    {
        return
            "監視状態: " + (_timer == null ? "停止中" : "動作中") +
            Environment.NewLine +
            "記録件数: " + _recordCount +
            Environment.NewLine +
            "監視間隔: " + PollIntervalMilliseconds + " ms";
    }

    internal static string GetStatus()
    {
        lock (Sync)
            return GetStatusCore();
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

        return "監視を停止しました。\n\n" + Read();
    }
}

