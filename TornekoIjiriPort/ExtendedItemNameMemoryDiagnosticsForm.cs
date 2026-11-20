using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TornekoIjiriPort
{
    internal sealed class ExtendedItemNameMemoryDiagnosticsForm : Form
    {
        private const int EeRamSize = 0x02000000;
        private const int ChunkSize = 0x00100000;
        private const int IdOffset = 0x00105441;
        private const int NameOffset = 0x0010544A;
        private const uint IdPointerValue = 0x00105441;
        private const uint NamePointerValue = 0x0010544A;
        private const uint Item163IdPointerValue = 0x0010546C;
        private const uint Item163ExtendedIdPointerValue = 0x00105452;

        private readonly int _processId;
        private readonly TextBox _output;
        private readonly Button _scanButton;

        private const uint ProcessVmRead = 0x0010;
        private const uint ProcessQueryInformation = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint dwDesiredAccess,
            bool bInheritHandle,
            int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            [Out] byte[] lpBuffer,
            int nSize,
            out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        public ExtendedItemNameMemoryDiagnosticsForm(int processId)
        {
            _processId = processId;

            Text = "拡張名前メモリ診断";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(1000, 720);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var description = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text =
                    "PCSX2のEE RAMを読み取り専用で調査します。" +
                    Environment.NewLine +
                    "item_164のID文字列とポインター候補を検索します。" +
                    Environment.NewLine +
                    "ゲームメモリへの書き込みは行いません。",
                Padding = new Padding(0, 0, 0, 8)
            };

            _scanButton = new Button
            {
                Text = "EE RAMを調査",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 0, 8)
            };

            _output = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font("Consolas", 9F),
                HideSelection = false
            };

            _scanButton.Click += (_, _) => ScanMemory();

            root.Controls.Add(description, 0, 0);
            root.Controls.Add(_scanButton, 0, 1);
            root.Controls.Add(_output, 0, 2);

            Controls.Add(root);
        }

        private void Append(string message)
        {
            _output.AppendText(message + Environment.NewLine);
        }

        private void ScanMemory()
        {
            _scanButton.Enabled = false;
            _output.Clear();

            IntPtr process = IntPtr.Zero;

            try
            {
                Append("診断開始");
                Append("PID: " + _processId);

                long eeBase = Pcsx2MemoryLayoutBuilder.GetEEmemBase(_processId);

                if (eeBase <= 0)
                {
                    throw new InvalidOperationException(
                        "EE RAMのベースアドレスを取得できませんでした。");
                }

                Append(
                    "EE RAM Base: 0x" +
                    eeBase.ToString("X"));

                process = OpenProcess(
                    ProcessVmRead | ProcessQueryInformation,
                    false,
                    _processId);

                if (process == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "OpenProcess失敗 Win32Error=" +
                        Marshal.GetLastWin32Error());
                }

                ScanEeRam(process, eeBase);

                Append("診断終了");
            }
            catch (Exception ex)
            {
                Append("診断エラー:");
                Append(ex.ToString());
            }

            if (process != IntPtr.Zero)
            {
                bool closed = CloseHandle(process);

                if (!closed)
                {
                    Append(
                        "CloseHandle警告 Win32Error=" +
                        Marshal.GetLastWin32Error());
                }
            }

            _scanButton.Enabled = true;
        }

        private void ScanEeRam(IntPtr process, long eeBase)
        {
            byte[] idPattern =
                Encoding.ASCII.GetBytes("item_164\0");

            var pointerPatterns = new Dictionary<string, byte[]>
            {
                ["item_164 IDアドレス"] = BitConverter.GetBytes(IdPointerValue),
                ["item_164 名前アドレス"] = BitConverter.GetBytes(NamePointerValue),
                ["item_163 IDアドレス"] = BitConverter.GetBytes(Item163IdPointerValue),
                ["item_163_e 文字列アドレス"] = BitConverter.GetBytes(Item163ExtendedIdPointerValue)
            };

            var idHits = new List<long>();
            var pointerHits = new Dictionary<string, List<long>>();

            foreach (string key in pointerPatterns.Keys)
            {
                pointerHits[key] = new List<long>();
            }

            byte[] carry = Array.Empty<byte>();
            int completed = 0;

            Append("EE RAMサイズ: 0x" + EeRamSize.ToString("X"));
            Append("検索文字列: item_164");
            Append(
                "ポインター検索値: 0x" +
                IdPointerValue.ToString("X8"));
            Append("");

            while (completed < EeRamSize)
            {
                int requested = Math.Min(
                    ChunkSize,
                    EeRamSize - completed);

                byte[] buffer = new byte[requested];

                IntPtr address = new IntPtr(checked(eeBase + completed));

                bool readOk = ReadProcessMemory(
                    process,
                    address,
                    buffer,
                    requested,
                    out IntPtr bytesRead);

                long actualLong = bytesRead.ToInt64();

                if (!readOk || actualLong != requested)
                {
                    throw new InvalidOperationException(
                        "EE RAM読み取り失敗。" +
                        " Offset=0x" + completed.ToString("X") +
                        " Requested=" + requested +
                        " Read=" + actualLong +
                        " Win32Error=" + Marshal.GetLastWin32Error());
                }

                byte[] combined =
                    new byte[carry.Length + requested];

                Buffer.BlockCopy(
                    carry, 0,
                    combined, 0,
                    carry.Length);

                Buffer.BlockCopy(
                    buffer, 0,
                    combined, carry.Length,
                    requested);

                long combinedStart =
                    completed - carry.Length;

                FindPattern(
                    combined,
                    idPattern,
                    combinedStart,
                    idHits,
                    1000);

                foreach (KeyValuePair<string, byte[]> entry in pointerPatterns)
                {
                    FindPattern(
                        combined,
                        entry.Value,
                        combinedStart,
                        pointerHits[entry.Key],
                        1000);
                }

                int keep = idPattern.Length - 1;

                carry = new byte[keep];

                Buffer.BlockCopy(
                    combined,
                    combined.Length - keep,
                    carry,
                    0,
                    keep);

                completed += requested;

                if (completed % (4 * ChunkSize) == 0 ||
                    completed == EeRamSize)
                {
                    Append(
                        "読み取り進捗: 0x" +
                        completed.ToString("X") +
                        " / 0x" + EeRamSize.ToString("X"));

                    Application.DoEvents();
                }
            }

            Append("");
            Append("===== item_164 ID文字列 =====");
            Append("検出数: " + idHits.Count);

            foreach (long offset in idHits)
            {
                Append("EE+0x" + offset.ToString("X8"));
            }

            foreach (KeyValuePair<string, List<long>> entry in pointerHits)
            {
                Append("");
                Append("===== " + entry.Key + " のポインター候補 =====");
                Append("検索値: " + BitConverter.ToString(pointerPatterns[entry.Key]));
                Append("検出数: " + entry.Value.Count);

                foreach (long offset in entry.Value)
                {
                    string alignment = offset % 4 == 0
                        ? "4バイト境界に整列"
                        : "非整列（余り=" + (offset % 4) + "）";

                    Append("EE+0x" + offset.ToString("X8") + " : " + alignment);
                }
            }

            Append("");
            Append("===== 既知領域 =====");

            ReadAndDisplay(
                process,
                eeBase,
                IdOffset,
                16,
                "item_164 ID領域");

            ReadAndDisplay(
                process,
                eeBase,
                NameOffset,
                32,
                "item_164 名前領域");

            Append("");
            Append("===== ポインター候補周辺 =====");
            ReadAndDisplay(
                process,
                eeBase,
                0x00100758,
                0x80,
                "EE+0x00100758 ～ 0x001007D7");

            Append("");
            Append("===== item_164 ID・名前周辺 =====");
            ReadAndDisplay(
                process,
                eeBase,
                0x00105420,
                0x60,
                "EE+0x00105420 ～ 0x0010547F");

            Append("");
            Append("===== 2つ目のID文字列周辺 =====");
            ReadAndDisplay(
                process,
                eeBase,
                0x00907570,
                0x40,
                "EE+0x00907570 ～ 0x009075AF");

            Append("");
            Append("===== 名前ポインター候補周辺 =====");
            ReadAndDisplay(
                process,
                eeBase,
                0x0000ABF0,
                0x60,
                "EE+0x0000ABF0 ～ 0x0000AC4F");

            Append("");
            ScanMipsTableReferences(process, eeBase);

            Append("");
            ScanMipsMemoryOperandReferences(process, eeBase);

            Append("");
            DumpItemStringPointerTable(process, eeBase);

            Append("");
            Append("注意: ポインター候補は値の一致による候補です。");
            Append("実際の名前表示処理が参照するかは未確認です。");
        }

        private void ScanMipsTableReferences(
            IntPtr process,
            long eeBase)
        {
            Append("");
            Append("===== MIPS命令によるテーブルアドレス参照検索 =====");
            Append("検索方式: LUI + ADDI/ADDIU/ORI");
            Append("命令間隔: 1～8命令");
            Append("命令境界: 4バイト単位");
            Append("");

            var targets = new Dictionary<uint, string>
            {
                [0x00100758] = "テーブル先頭",
                [0x00100798] = "item_164テーブル要素",
                [0x0010079C] = "次のテーブル要素"
            };

            var hits = new Dictionary<uint, List<int>>();

            foreach (uint target in targets.Keys)
            {
                hits[target] = new List<int>();
            }

            byte[] ram = new byte[EeRamSize];

            int completed = 0;

            while (completed < EeRamSize)
            {
                int requested = Math.Min(
                    ChunkSize,
                    EeRamSize - completed);

                byte[] chunk = new byte[requested];

                IntPtr address = new IntPtr(
                    checked(eeBase + completed));

                bool readOk = ReadProcessMemory(
                    process,
                    address,
                    chunk,
                    requested,
                    out IntPtr bytesRead);

                long actualLong = bytesRead.ToInt64();

                if (!readOk || actualLong != requested)
                {
                    throw new InvalidOperationException(
                        "MIPS検索用EE RAM読み取り失敗。" +
                        " Offset=0x" + completed.ToString("X") +
                        " Requested=" + requested +
                        " Read=" + actualLong +
                        " Win32Error=" + Marshal.GetLastWin32Error());
                }

                Buffer.BlockCopy(
                    chunk,
                    0,
                    ram,
                    completed,
                    requested);

                completed += requested;

                if (completed % (4 * ChunkSize) == 0 ||
                    completed == EeRamSize)
                {
                    Append(
                        "MIPS検索用読み取り進捗: 0x" +
                        completed.ToString("X") +
                        " / 0x" + EeRamSize.ToString("X"));

                    Application.DoEvents();
                }
            }

            Append("");
            Append("===== アドレス生成命令の走査 =====");

            int instructionCount = EeRamSize / 4;

            for (int i = 0; i < instructionCount; i++)
            {
                int byteOffset = i * 4;

                uint luiWord = BitConverter.ToUInt32(
                    ram,
                    byteOffset);

                uint opcode = luiWord >> 26;

                if (opcode != 0x0F)
                {
                    continue;
                }

                uint registerT = (luiWord >> 16) & 0x1F;
                uint upper = (luiWord & 0xFFFF) << 16;

                if (registerT == 0)
                {
                    continue;
                }

                int maximumDistance = Math.Min(
                    8,
                    instructionCount - i - 1);

                for (int distance = 1;
                    distance <= maximumDistance;
                    distance++)
                {
                    int lowIndex = i + distance;
                    int lowOffset = lowIndex * 4;

                    uint lowWord = BitConverter.ToUInt32(
                        ram,
                        lowOffset);

                    uint lowOpcode = lowWord >> 26;
                    uint registerS = (lowWord >> 21) & 0x1F;

                    if (registerS != registerT)
                    {
                        continue;
                    }

                    uint immediate = lowWord & 0xFFFF;
                    uint effectiveAddress = 0;
                    bool addressGenerated = false;

                    if (lowOpcode == 0x08 ||
                        lowOpcode == 0x09)
                    {
                        long signedImmediate = (short)immediate;

                        effectiveAddress = unchecked(
                            (uint)((long)upper + signedImmediate));

                        addressGenerated = true;
                    }

                    if (lowOpcode == 0x0D)
                    {
                        effectiveAddress = upper | immediate;
                        addressGenerated = true;
                    }

                    if (!addressGenerated)
                    {
                        continue;
                    }

                    if (!hits.ContainsKey(effectiveAddress))
                    {
                        continue;
                    }

                    if (!hits[effectiveAddress].Contains(byteOffset))
                    {
                        hits[effectiveAddress].Add(byteOffset);
                    }
                }
            }

            foreach (KeyValuePair<uint, string> target in targets)
            {
                Append("");
                Append("===== " + target.Value + " =====");
                Append(
                    "検索アドレス: 0x" +
                    target.Key.ToString("X8"));

                List<int> targetHits = hits[target.Key];

                Append("検出数: " + targetHits.Count);

                int displayCount = Math.Min(
                    targetHits.Count,
                    100);

                for (int hitIndex = 0;
                    hitIndex < displayCount;
                    hitIndex++)
                {
                    int luiOffset = targetHits[hitIndex];

                    uint luiWord = BitConverter.ToUInt32(
                        ram,
                        luiOffset);

                    uint registerT = (luiWord >> 16) & 0x1F;

                    Append("");
                    Append(
                        "候補 " + (hitIndex + 1) +
                        " : EE+0x" + luiOffset.ToString("X8"));

                    Append(
                        "LUI命令: 0x" + luiWord.ToString("X8") +
                        " / 対象レジスタ=" + registerT);

                    for (int distance = 1;
                        distance <= 8;
                        distance++)
                    {
                        int lowOffset = luiOffset + distance * 4;

                        if (lowOffset + 4 > EeRamSize)
                        {
                            break;
                        }

                        uint lowWord = BitConverter.ToUInt32(
                            ram,
                            lowOffset);

                        uint lowOpcode = lowWord >> 26;
                        uint registerS = (lowWord >> 21) & 0x1F;

                        if (registerS != registerT)
                        {
                            continue;
                        }

                        if (lowOpcode != 0x08 &&
                            lowOpcode != 0x09 &&
                            lowOpcode != 0x0D)
                        {
                            continue;
                        }

                        uint immediate = lowWord & 0xFFFF;
                        uint upper = (luiWord & 0xFFFF) << 16;
                        uint effectiveAddress = 0;

                        if (lowOpcode == 0x0D)
                        {
                            effectiveAddress = upper | immediate;
                        }

                        if (lowOpcode == 0x08 ||
                            lowOpcode == 0x09)
                        {
                            effectiveAddress = unchecked(
                                (uint)((long)upper + (short)immediate));
                        }

                        if (effectiveAddress != target.Key)
                        {
                            continue;
                        }

                        string instructionName = "UNKNOWN";

                        if (lowOpcode == 0x08)
                        {
                            instructionName = "ADDI";
                        }

                        if (lowOpcode == 0x09)
                        {
                            instructionName = "ADDIU";
                        }

                        if (lowOpcode == 0x0D)
                        {
                            instructionName = "ORI";
                        }

                        Append(
                            instructionName +
                            "命令: EE+0x" +
                            lowOffset.ToString("X8") +
                            " = 0x" + lowWord.ToString("X8"));
                    }

                    Append("前後の命令データ:");

                    int firstOffset = Math.Max(
                        0,
                        luiOffset - 16);

                    int lastOffset = Math.Min(
                        EeRamSize - 4,
                        luiOffset + 40);

                    for (int contextOffset = firstOffset;
                        contextOffset <= lastOffset;
                        contextOffset += 4)
                    {
                        uint word = BitConverter.ToUInt32(
                            ram,
                            contextOffset);

                        string marker = contextOffset == luiOffset
                            ? "  <LUI候補>"
                            : "";

                        Append(
                            "EE+0x" +
                            contextOffset.ToString("X8") +
                            " : " +
                            word.ToString("X8") +
                            marker);
                    }
                }

                if (targetHits.Count > 100)
                {
                    Append("表示上限100件。残りは省略しました。");
                }
            }

            Append("");
            Append("MIPS検索終了。命令候補は参照元の確定を意味しません。");
        }

        private void ScanMipsMemoryOperandReferences(
            IntPtr process,
            long eeBase)
        {
            Append("");
            Append("===== MIPSロード・ストア命令の参照候補検索 =====");
            Append("対象: 0x00100758 / 0x00100798 / 0x0010079C");
            Append("方式: LUIベース参照 + 即値一致検索");
            Append("");

            var targets = new Dictionary<uint, string>
            {
                [0x0758] = "テーブル先頭",
                [0x0798] = "item_164テーブル要素",
                [0x079C] = "次のテーブル要素"
            };

            byte[] ram = new byte[EeRamSize];

            int completed = 0;

            while (completed < EeRamSize)
            {
                int requested = Math.Min(
                    ChunkSize,
                    EeRamSize - completed);

                byte[] chunk = new byte[requested];

                IntPtr address = new IntPtr(
                    checked(eeBase + completed));

                bool readOk = ReadProcessMemory(
                    process,
                    address,
                    chunk,
                    requested,
                    out IntPtr bytesRead);

                long actualLong = bytesRead.ToInt64();

                if (!readOk || actualLong != requested)
                {
                    throw new InvalidOperationException(
                        "MIPSメモリアクセス検索用RAM読み取り失敗。" +
                        " Offset=0x" + completed.ToString("X") +
                        " Requested=" + requested +
                        " Read=" + actualLong +
                        " Win32Error=" + Marshal.GetLastWin32Error());
                }

                Buffer.BlockCopy(
                    chunk,
                    0,
                    ram,
                    completed,
                    requested);

                completed += requested;

                if (completed % (4 * ChunkSize) == 0 ||
                    completed == EeRamSize)
                {
                    Append(
                        "ロード・ストア検索用読み取り進捗: 0x" +
                        completed.ToString("X") +
                        " / 0x" + EeRamSize.ToString("X"));

                    Application.DoEvents();
                }
            }

            Append("");
            Append("===== 即値一致によるロード・ストア候補 =====");

            int instructionCount = EeRamSize / 4;

            foreach (KeyValuePair<uint, string> target in targets)
            {
                Append("");
                Append("----- " + target.Value + " -----");
                Append("即値: 0x" + target.Key.ToString("X4"));

                int hitCount = 0;
                int displayed = 0;

                for (int i = 0; i < instructionCount; i++)
                {
                    int offset = i * 4;

                    uint word = BitConverter.ToUInt32(
                        ram,
                        offset);

                    uint opcode = word >> 26;
                    uint immediate = word & 0xFFFF;

                    if (immediate != target.Key)
                    {
                        continue;
                    }

                    string instructionName = "";

                    if (opcode == 0x20) instructionName = "LB";
                    if (opcode == 0x21) instructionName = "LH";
                    if (opcode == 0x23) instructionName = "LW";
                    if (opcode == 0x24) instructionName = "LBU";
                    if (opcode == 0x25) instructionName = "LHU";
                    if (opcode == 0x28) instructionName = "SB";
                    if (opcode == 0x29) instructionName = "SH";
                    if (opcode == 0x2B) instructionName = "SW";
                    if (opcode == 0x22) instructionName = "LWL";
                    if (opcode == 0x26) instructionName = "LWR";
                    if (opcode == 0x2A) instructionName = "SWL";
                    if (opcode == 0x2E) instructionName = "SWR";

                    if (instructionName.Length == 0)
                    {
                        continue;
                    }

                    hitCount++;

                    if (displayed >= 80)
                    {
                        continue;
                    }

                    uint registerS = (word >> 21) & 0x1F;
                    uint registerT = (word >> 16) & 0x1F;

                    Append("");
                    Append(
                        "候補 EE+0x" +
                        offset.ToString("X8") +
                        " : " + instructionName);

                    Append(
                        "命令: 0x" + word.ToString("X8") +
                        " / BaseReg=" + registerS +
                        " / TargetReg=" + registerT +
                        " / Offset=0x" + immediate.ToString("X4"));

                    int first = Math.Max(0, offset - 16);
                    int last = Math.Min(EeRamSize - 4, offset + 16);

                    for (int context = first;
                        context <= last;
                        context += 4)
                    {
                        uint contextWord = BitConverter.ToUInt32(
                            ram,
                            context);

                        string marker = context == offset
                            ? "  <対象命令>"
                            : "";

                        Append(
                            "  EE+0x" +
                            context.ToString("X8") +
                            " : 0x" +
                            contextWord.ToString("X8") +
                            marker);
                    }

                    displayed++;
                }

                Append("");
                Append("検出数: " + hitCount);

                if (hitCount > 80)
                {
                    Append("表示上限80件。残りは省略しました。");
                }
            }

            Append("");
            Append("===== LUI 0x0010 とメモリアクセス命令の組み合わせ =====");

            int pairCount = 0;
            int pairDisplayed = 0;

            for (int i = 0; i < instructionCount; i++)
            {
                int luiOffset = i * 4;

                uint luiWord = BitConverter.ToUInt32(
                    ram,
                    luiOffset);

                uint luiOpcode = luiWord >> 26;

                if (luiOpcode != 0x0F)
                {
                    continue;
                }

                uint upperImmediate = luiWord & 0xFFFF;

                if (upperImmediate != 0x0010)
                {
                    continue;
                }

                uint luiRegister = (luiWord >> 16) & 0x1F;

                if (luiRegister == 0)
                {
                    continue;
                }

                int maxDistance = Math.Min(
                    16,
                    instructionCount - i - 1);

                for (int distance = 1;
                    distance <= maxDistance;
                    distance++)
                {
                    int accessOffset = luiOffset + distance * 4;

                    uint accessWord = BitConverter.ToUInt32(
                        ram,
                        accessOffset);

                    uint opcode = accessWord >> 26;
                    uint baseRegister = (accessWord >> 21) & 0x1F;
                    uint immediate = accessWord & 0xFFFF;

                    if (baseRegister != luiRegister)
                    {
                        continue;
                    }

                    string instructionName = "";

                    if (opcode == 0x20) instructionName = "LB";
                    if (opcode == 0x21) instructionName = "LH";
                    if (opcode == 0x23) instructionName = "LW";
                    if (opcode == 0x24) instructionName = "LBU";
                    if (opcode == 0x25) instructionName = "LHU";
                    if (opcode == 0x28) instructionName = "SB";
                    if (opcode == 0x29) instructionName = "SH";
                    if (opcode == 0x2B) instructionName = "SW";

                    if (instructionName.Length == 0)
                    {
                        continue;
                    }

                    if (!targets.ContainsKey(immediate))
                    {
                        continue;
                    }

                    pairCount++;

                    if (pairDisplayed >= 100)
                    {
                        continue;
                    }

                    Append("");
                    Append(
                        "LUI EE+0x" +
                        luiOffset.ToString("X8") +
                        " : 0x" + luiWord.ToString("X8"));

                    Append(
                        instructionName +
                        " EE+0x" +
                        accessOffset.ToString("X8") +
                        " : 0x" + accessWord.ToString("X8"));

                    Append(
                        "対象: " +
                        targets[immediate] +
                        " / 推定アドレス=0x0010" +
                        immediate.ToString("X4"));

                    for (int context = Math.Max(0, luiOffset - 8);
                        context <= Math.Min(EeRamSize - 4, accessOffset + 8);
                        context += 4)
                    {
                        uint contextWord = BitConverter.ToUInt32(
                            ram,
                            context);

                        string marker = context == luiOffset
                            ? "  <LUI>"
                            : "";

                        if (context == accessOffset)
                        {
                            marker = "  <MEMORY ACCESS>";
                        }

                        Append(
                            "  EE+0x" +
                            context.ToString("X8") +
                            " : 0x" +
                            contextWord.ToString("X8") +
                            marker);
                    }

                    pairDisplayed++;
                }
            }

            Append("");
            Append("LUI組み合わせ検出数: " + pairCount);

            if (pairCount > 100)
            {
                Append("表示上限100件。残りは省略しました。");
            }

            Append("");
            Append("ロード・ストア命令検索終了。");
            Append("命令候補は実際の実行経路やテーブル参照を確定するものではありません。");
        }

        private void DumpItemStringPointerTable(
            IntPtr process,
            long eeBase)
        {
            Append("");
            Append("===== アイテム文字列ポインタ配列の内容 =====");
            Append("配列開始: EE+0x00100758");
            Append("読み取り件数: 32ポインタ");
            Append("");

            const int tableOffset = 0x00100758;
            const int entryCount = 128;
            const int entrySize = 4;
            const int targetReadSize = 48;

            byte[] table = new byte[entryCount * entrySize];

            bool tableReadOk = ReadProcessMemory(
                process,
                new IntPtr(checked(eeBase + tableOffset)),
                table,
                table.Length,
                out IntPtr tableBytesRead);

            long tableActual = tableBytesRead.ToInt64();

            if (!tableReadOk || tableActual != table.Length)
            {
                Append("ポインタ配列の読み取りに失敗しました。");
                Append("Read=" + tableActual);
                Append("Win32Error=" + Marshal.GetLastWin32Error());
                return;
            }

            Encoding cp932 = Encoding.GetEncoding(932);

            for (int i = 0; i < entryCount; i++)
            {
                uint pointer = BitConverter.ToUInt32(
                    table,
                    i * entrySize);

                int entryOffset = tableOffset + i * entrySize;

                Append("");
                Append(
                    "[" + i.ToString("D2") + "] " +
                    "配列位置=EE+0x" +
                    entryOffset.ToString("X8"));

                Append(
                    "ポインタ値=0x" +
                    pointer.ToString("X8"));

                if (pointer >= EeRamSize ||
                    pointer + targetReadSize > EeRamSize)
                {
                    Append("参照先がEE RAM範囲外です。");
                    continue;
                }

                byte[] prefix = new byte[5];

                bool prefixReadOk = ReadProcessMemory(
                    process,
                    new IntPtr(checked(eeBase + (long)pointer)),
                    prefix,
                    prefix.Length,
                    out IntPtr prefixBytesRead);

                if (!prefixReadOk || prefixBytesRead.ToInt64() != 5)
                {
                    Append("参照先の先頭5バイトを読み取れません。");
                    break;
                }

                bool validItemPrefix =
                    prefix[0] == 0x69 &&
                    prefix[1] == 0x74 &&
                    prefix[2] == 0x65 &&
                    prefix[3] == 0x6D &&
                    prefix[4] == 0x5F;

                if (!validItemPrefix)
                {
                    Append("item_文字列ではないポインタを検出しました。");
                    Append("この位置を配列終端候補とします。");
                    break;
                }

                byte[] targetBytes = new byte[targetReadSize];

                bool targetReadOk = ReadProcessMemory(
                    process,
                    new IntPtr(checked(eeBase + (long)pointer)),
                    targetBytes,
                    targetBytes.Length,
                    out IntPtr targetBytesRead);

                long targetActual = targetBytesRead.ToInt64();

                if (!targetReadOk || targetActual != targetBytes.Length)
                {
                    Append("参照先の読み取りに失敗しました。");
                    Append("Read=" + targetActual);
                    continue;
                }

                int stringLength = Array.IndexOf(
                    targetBytes,
                    (byte)0);

                if (stringLength < 0)
                {
                    stringLength = targetBytes.Length;
                }

                byte[] stringBytes = new byte[stringLength];

                Buffer.BlockCopy(
                    targetBytes,
                    0,
                    stringBytes,
                    0,
                    stringLength);

                Append(
                    "参照先=EE+0x" +
                    pointer.ToString("X8"));

                Append(
                    "文字列バイト=" +
                    BitConverter.ToString(stringBytes));

                Append(
                    "ASCII=" +
                    Encoding.ASCII.GetString(stringBytes));

                Append(
                    "CP932=" +
                    cp932.GetString(stringBytes));

                Append(
                    "参照先48バイト=" +
                    BitConverter.ToString(targetBytes));
            }

            Append("");
            Append("===== ポインタ配列の読み取り終了 =====");
        }

        private static void FindPattern(
            byte[] source,
            byte[] pattern,
            long sourceStart,
            List<long> results,
            int maximum)
        {
            if (pattern.Length == 0)
            {
                return;
            }

            int last = source.Length - pattern.Length;

            for (int i = 0; i <= last; i++)
            {
                bool matched = true;

                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    long offset = sourceStart + i;

                    if (offset >= 0 && results.Count < maximum)
                    {
                        results.Add(offset);
                    }
                }
            }
        }

        private void ReadAndDisplay(
            IntPtr process,
            long eeBase,
            int offset,
            int length,
            string label)
        {
            byte[] data = new byte[length];

            IntPtr address = new IntPtr(checked(eeBase + offset));

            bool ok = ReadProcessMemory(
                process,
                address,
                data,
                data.Length,
                out IntPtr read);

            Append(label + ":");

            if (!ok || read.ToInt64() != length)
            {
                Append(
                    "読み取り失敗 Win32Error=" +
                    Marshal.GetLastWin32Error());
                return;
            }

            Append("Offset: EE+0x" + offset.ToString("X8"));
            Append("HEX: " + BitConverter.ToString(data));

            int nullIndex = Array.IndexOf(data, (byte)0);

            int textLength = nullIndex >= 0
                ? nullIndex
                : data.Length;

            Encoding.RegisterProvider(
                CodePagesEncodingProvider.Instance);

            string decoded = Encoding.GetEncoding(932).GetString(
                data,
                0,
                textLength);

            Append("CP932: " + decoded);
        }
    }
}