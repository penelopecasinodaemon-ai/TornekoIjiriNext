using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class DungeonCommonSettingsForm : Form
{
    private const string Host = "127.0.0.1";
    private const int Port = 28011;

    private const uint SpawnInstructionAddress = 0x0022FE04;


    private const long RuntimeDescriptionOffset = 0x01FFF1F0L;
    private const long ItemTableDescriptionOffset = 0x00107BC7L;

    private const int OriginalDescriptionLength = 114;

    private NumericUpDown _turns;
    private Label _current;
    private Label _status;
    private Label _itemStatus;

    private static readonly Encoding Cp932 = CreateCp932();

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(
        IntPtr process,
        IntPtr address,
        UIntPtr size,
        uint newProtection,
        out uint oldProtection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(
        IntPtr process,
        IntPtr address,
        UIntPtr size);

    private sealed class MemoryChange
    {
        public string Name { get; }
        public long Address { get; }
        public byte[] NewData { get; }
        public byte[] OldData { get; }

        public MemoryChange(
            string name,
            long address,
            byte[] newData,
            byte[] oldData)
        {
            Name = name;
            Address = address;
            NewData = newData;
            OldData = oldData;
        }
    }

    public DungeonCommonSettingsForm()
    {
        Text = "ダンジョン共通設定";
        StartPosition = FormStartPosition.CenterParent;
        Width = 720;
        Height = 470;
        MinimumSize = new Size(620, 400);
        Font = new Font("Yu Gothic UI", 10);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 5)
        };

        var spawnPage = new TabPage("敵湧き設定");
        var itemPage = new TabPage("アイテム変更");

        tabs.TabPages.Add(spawnPage);
        tabs.TabPages.Add(itemPage);

        BuildSpawnPage(spawnPage);
        BuildItemPage(itemPage);

        Controls.Add(tabs);

        // ウィンドウ表示時の自動読み込みは行わない。
    }

    private void BuildSpawnPage(TabPage page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 6
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var heading = new Label
        {
            Text = "敵の新規出現間隔",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 13, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var inputPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight
        };

        _turns = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 32767,
            Value = 30,
            Width = 130,
            Height = 32,
            Font = new Font("Yu Gothic UI", 12)
        };

        inputPanel.Controls.Add(_turns);
        inputPanel.Controls.Add(new Label
        {
            Text = "ターンごとに新規出現",
            AutoSize = true,
            Padding = new Padding(8, 8, 0, 0)
        });

        var readButton = new Button
        {
            Text = "現在の設定を読み込む",
            Dock = DockStyle.Fill,
            Height = 36
        };
        readButton.Click += (_, _) => ReadCurrent();

        var writeButton = new Button
        {
            Text = "PCSX2へ適用",
            Dock = DockStyle.Fill,
            Height = 36
        };
        writeButton.Click += (_, _) => ApplySetting();

        _current = new Label
        {
            Text = "現在値: 未取得",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _status = new Label
        {
            Text = "PCSX2を起動し、トルネコ3を読み込んでください。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(inputPanel, 0, 1);
        root.Controls.Add(readButton, 0, 2);
        root.Controls.Add(writeButton, 0, 3);
        root.Controls.Add(_current, 0, 4);
        root.Controls.Add(_status, 0, 5);

        page.Controls.Add(root);
    }

        private void BuildItemPage(TabPage page)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        var hayabusaButton = new Button
        {
            Text = "エレファントクロウ → はやぶさの爪",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 11, FontStyle.Bold)
        };

        hayabusaButton.Click += (_, _) => ApplyHayabusa();

        var yamabikoButton = new Button
        {
            Text = "魔封じの指輪 → やまびこの指輪",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 11, FontStyle.Bold)
        };

        yamabikoButton.Click += (_, _) => ApplyRingUpgrade(true);

        var hoshifuriButton = new Button
        {
            Text = "見切りの指輪 → ほしふりの指輪",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 11, FontStyle.Bold)
        };

        hoshifuriButton.Click += (_, _) => ApplyRingUpgrade(false);

        var slimeRingButton = new Button
        {
            Text = "指輪164→スライムの指輪",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 11, FontStyle.Bold)
        };

        slimeRingButton.Click += (_, _) => ApplySlimeRing();

        _itemStatus = new Label
        {
            Text = "",
            Visible = false
        };

        root.Controls.Add(hayabusaButton, 0, 0);
        root.Controls.Add(yamabikoButton, 0, 1);
        root.Controls.Add(hoshifuriButton, 0, 2);
        root.Controls.Add(slimeRingButton, 0, 3);

        page.Controls.Add(root);
    }

    private static byte[] ReadExact(
        NetworkStream stream,
        int count)
    {
        byte[] result = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int received = stream.Read(
                result,
                offset,
                count - offset);

            if (received <= 0)
                throw new IOException(
                    "PINE接続が途中で切断されました。");

            offset += received;
        }

        return result;
    }

    private static void AppendPineDiagnostic(string message)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "pine-communication.log");
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine;
            File.AppendAllText(path, line);
        }
        catch
        {
        }
    }
    private static byte[] SendPineCommand(byte[] command)
    {
        byte[] request = new byte[4 + command.Length];

        WriteUInt32(request, 0, (uint)request.Length);
        Buffer.BlockCopy(command, 0, request, 4, command.Length);

        AppendPineDiagnostic("開始 Command=" + BitConverter.ToString(command));
        AppendPineDiagnostic("Request=" + BitConverter.ToString(request));

        using var client = new TcpClient(System.Net.Sockets.AddressFamily.InterNetwork);
        client.ReceiveTimeout = 4000;
        client.SendTimeout = 4000;

        try
        {
            AppendPineDiagnostic("Connect開始 Host=" + Host + " Port=" + Port);
            client.Connect(System.Net.IPAddress.Parse(Host), Port);
            AppendPineDiagnostic("Connect成功 Local=" + client.Client.LocalEndPoint + " Remote=" + client.Client.RemoteEndPoint);

            using NetworkStream stream = client.GetStream();
            stream.WriteTimeout = 4000;
            stream.ReadTimeout = 4000;

            AppendPineDiagnostic("Write開始");
            stream.Write(request, 0, request.Length);
            stream.Flush();
            AppendPineDiagnostic("Write完了");

            byte[] header;
            try
            {
                AppendPineDiagnostic("応答ヘッダー受信開始 必要バイト数=4");
                header = ReadExact(stream, 4);
                AppendPineDiagnostic("応答ヘッダー受信成功 Header=" + BitConverter.ToString(header));
            }
            catch (Exception ex)
            {
                AppendPineDiagnostic("応答ヘッダー受信失敗 " + ex.ToString());
                throw new IOException("PINE応答ヘッダーの受信に失敗しました。送信=" + BitConverter.ToString(request), ex);
            }

            uint responseLength = ReadUInt32(header, 0);
            AppendPineDiagnostic("ResponseLength=" + responseLength);

            if (responseLength < 5 || responseLength > 1024)
                throw new IOException("PINE応答サイズが不正です: " + responseLength);

            byte[] response = ReadExact(stream, (int)responseLength - 4);
            AppendPineDiagnostic("応答データ受信成功 Response=" + BitConverter.ToString(response));

            return response;
        }
        catch (Exception ex)
        {
            AppendPineDiagnostic("通信処理例外 " + ex.ToString());
            throw;
        }
    }


    private static uint ReadUInt32(
        byte[] data,
        int offset)
    {
        return (uint)(
            data[offset] |
            (data[offset + 1] << 8) |
            (data[offset + 2] << 16) |
            (data[offset + 3] << 24));
    }

    private static void WriteUInt32(
        byte[] data,
        int offset,
        uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static uint ReadInstruction()
    {
        byte[] command = new byte[5];
        command[0] = 2;
        WriteUInt32(command, 1, SpawnInstructionAddress);

        byte[] response = SendPineCommand(command);

        if (response.Length != 5)
            throw new IOException(
                "Read32応答サイズが一致しません。実際: " +
                response.Length);

        if (response[0] != 0)
            throw new IOException(
                "PINE Read32が失敗しました。ステータス: " +
                response[0].ToString("X2"));

        return ReadUInt32(response, 1);
    }

    private static void WriteInstruction(uint instruction)
    {
        byte[] command = new byte[9];
        command[0] = 6;
        WriteUInt32(command, 1, SpawnInstructionAddress);
        WriteUInt32(command, 5, instruction);

        byte[] response = SendPineCommand(command);

        if (response.Length != 1)
            throw new IOException(
                "Write32応答サイズが一致しません。実際: " +
                response.Length);

        if (response[0] != 0)
            throw new IOException(
                "PINE Write32が失敗しました。ステータス: " +
                response[0].ToString("X2"));
    }

    private static bool TryGetSpawnInterval(
        uint instruction,
        out int turns)
    {
        if ((instruction & 0xFFFF0000u) != 0x24020000u)
        {
            turns = 0;
            return false;
        }

        turns = (int)(instruction & 0xFFFFu);
        return turns >= 1 && turns <= 32767;
    }

    private void ShowCopyableError(string title, string message)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Width = 720,
            Height = 460,
            MinimumSize = new Size(520, 320),
            Font = new Font("Yu Gothic UI", 10)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 2
        };

        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var errorText = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Yu Gothic UI", 10),
            Text = message
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var closeButton = new Button
        {
            Text = "閉じる",
            Width = 110,
            Height = 36
        };

        closeButton.Click += (_, _) => dialog.Close();

        var copyButton = new Button
        {
            Text = "エラー全文をコピー",
            Width = 180,
            Height = 36
        };

        copyButton.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(errorText.Text);
                MessageBox.Show(
                    dialog,
                    "エラー全文をクリップボードにコピーしました。",
                    "コピー成功",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception copyError)
            {
                MessageBox.Show(
                    dialog,
                    copyError.Message,
                    "コピー失敗",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        buttonPanel.Controls.Add(closeButton);
        buttonPanel.Controls.Add(copyButton);

        root.Controls.Add(errorText, 0, 0);
        root.Controls.Add(buttonPanel, 0, 1);
        dialog.Controls.Add(root);

        dialog.ShowDialog(this);
    }
    private void ReadCurrent()
    {
        try
        {
            uint instruction = ReadInstruction();

            if (!TryGetSpawnInterval(
                instruction,
                out int turns))
            {
                _current.Text =
                    "現在の命令: 0x" + instruction.ToString("X8");

                _status.Text =
                    "想定した命令形式ではありません。安全のため変更できません。";

                return;
            }

            _turns.Value = turns;
            _current.Text =
                "現在値: " + turns + "ターン / 命令: 0x" +
                instruction.ToString("X8");

            _status.Text =
                "PCSX2から現在値を読み込みました。";
        }
        catch (Exception ex)
        {
            _status.Text = "読み込み失敗: " + ex.Message;
            ShowCopyableError("敵湧き設定の読み込みエラー", ex.ToString());
        }
    }

    private void ApplySetting()
    {
        int turns = (int)_turns.Value;

        try
        {
            uint before = ReadInstruction();

            if (!TryGetSpawnInterval(
                before,
                out int oldTurns))
            {
                _current.Text =
                    "現在の命令: 0x" + before.ToString("X8");

                MessageBox.Show(
                    this,
                    "対象アドレスの命令が想定形式ではありません。\r\n" +
                    "誤書き込み防止のため適用を中止しました。",
                    "命令形式不一致",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            uint newInstruction =
                0x24020000u | (uint)turns;

            if (MessageBox.Show(
                this,
                "敵の新規出現間隔を変更します。\r\n\r\n" +
                "変更前: " + oldTurns + "ターン\r\n" +
                "変更後: " + turns + "ターン\r\n\r\n" +
                "命令: 0x" + newInstruction.ToString("X8") + "\r\n" +
                "アドレス: 0x0022FE04\r\n\r\n" +
                "PCSX2へ書き込みますか？",
                "設定適用確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            WriteInstruction(newInstruction);

            uint verify = ReadInstruction();

            if (verify != newInstruction)
                throw new IOException(
                    "書き込み後の読み戻し値が一致しません。\r\n" +
                    "期待値: 0x" + newInstruction.ToString("X8") +
                    "\r\n実際: 0x" + verify.ToString("X8"));

            _current.Text =
                "現在値: " + turns + "ターン / 命令: 0x" +
                verify.ToString("X8");

            _status.Text =
                "書き込み・読み戻し検証成功。ゲーム内で出現間隔を確認してください。";

            MessageBox.Show(
                this,
                "PCSX2への書き込みと読み戻し検証に成功しました。\r\n\r\n" +
                "設定値: " + turns + "ターン",
                "適用成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "適用失敗: " + ex.Message;

            MessageBox.Show(
                this,
                ex.ToString(),
                "設定適用エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static byte[] ReadMemory(
        IntPtr process,
        long address,
        int length)
    {
        if (address <= 0 || length <= 0)
            throw new InvalidOperationException(
                "読み取りアドレスまたはサイズが不正です。");

        byte[] buffer = new byte[length];
        UIntPtr read;

        bool success = ReadProcessMemory(
            process,
            new IntPtr(address),
            buffer,
            new UIntPtr((uint)length),
            out read);

        if (!success || read.ToUInt64() != (ulong)length)
        {
            throw new IOException(
                "メモリ読み取りに失敗しました。\r\n" +
                "アドレス: 0x" + address.ToString("X") +
                "\r\nWin32エラー: " +
                Marshal.GetLastWin32Error());
        }

        return buffer;
    }

    private static void WriteMemory(
        IntPtr process,
        long address,
        byte[] data,
        bool executable = false)
    {
        if (address <= 0 || data == null || data.Length == 0)
            throw new InvalidOperationException(
                "書き込みアドレスまたはデータが不正です。");

        if (!executable)
        {
            UIntPtr dataWritten;

            bool dataSuccess = WriteProcessMemory(
                process,
                new IntPtr(address),
                data,
                new UIntPtr((uint)data.Length),
                out dataWritten);

            int dataError = Marshal.GetLastWin32Error();

            if (!dataSuccess || dataWritten.ToUInt64() != (ulong)data.Length)
            {
                throw new IOException(
                    "通常データのメモリ書き込みに失敗しました。" +
                    "\r\nアドレス: 0x" + address.ToString("X") +
                    "\r\n要求サイズ: " + data.Length +
                    "\r\n書き込みサイズ: " + dataWritten.ToUInt64() +
                    "\r\nWin32エラー: " + dataError);
            }

            return;
        }

        uint oldProtection;

        bool protectionChanged = VirtualProtectEx(
            process,
            new IntPtr(address),
            new UIntPtr((uint)data.Length),
            0x04,
            out oldProtection);

        if (!protectionChanged)
        {
            throw new IOException(
                "実行コードのメモリ保護変更に失敗しました。" +
                "\r\nアドレス: 0x" + address.ToString("X") +
                "\r\nWin32エラー: " + Marshal.GetLastWin32Error());
        }

        UIntPtr written;

        bool success = WriteProcessMemory(
            process,
            new IntPtr(address),
            data,
            new UIntPtr((uint)data.Length),
            out written);

        int writeError = Marshal.GetLastWin32Error();

        bool protectionRestored = VirtualProtectEx(
            process,
            new IntPtr(address),
            new UIntPtr((uint)data.Length),
            oldProtection,
            out uint ignoredProtection);

        if (!protectionRestored)
        {
            throw new IOException(
                "実行コードのメモリ保護を元に戻せませんでした。" +
                "\r\nアドレス: 0x" + address.ToString("X") +
                "\r\nWin32エラー: " + Marshal.GetLastWin32Error());
        }

        if (!success || written.ToUInt64() != (ulong)data.Length)
        {
            throw new IOException(
                "実行コードのメモリ書き込みに失敗しました。" +
                "\r\nアドレス: 0x" + address.ToString("X") +
                "\r\n要求サイズ: " + data.Length +
                "\r\n書き込みサイズ: " + written.ToUInt64() +
                "\r\nWin32エラー: " + writeError);
        }

        bool flushed = FlushInstructionCache(
            process,
            new IntPtr(address),
            new UIntPtr((uint)data.Length));

        if (!flushed)
        {
            throw new IOException(
                "命令キャッシュの更新に失敗しました。" +
                "\r\nアドレス: 0x" + address.ToString("X") +
                "\r\nWin32エラー: " + Marshal.GetLastWin32Error());
        }
    }

    private static uint ReadPineWord(uint address)
    {
        byte[] command = new byte[5];
        command[0] = 2;
        WriteUInt32(command, 1, address);

        byte[] response = SendPineCommand(command);

        if (response.Length != 5)
            throw new IOException("PINE Read32応答サイズが不正です。");

        if (response[0] != 0)
            throw new IOException(
                "PINE Read32失敗。アドレス: 0x" +
                address.ToString("X8") +
                " / ステータス: " + response[0].ToString("X2"));

        return ReadUInt32(response, 1);
    }

    private static void WritePineWord(uint address, uint value)
    {
        byte[] command = new byte[9];
        command[0] = 6;
        WriteUInt32(command, 1, address);
        WriteUInt32(command, 5, value);

        byte[] response = SendPineCommand(command);

        if (response.Length != 1)
            throw new IOException("PINE Write32応答サイズが不正です。");

        if (response[0] != 0)
            throw new IOException(
                "PINE Write32失敗。アドレス: 0x" +
                address.ToString("X8") +
                " / ステータス: " + response[0].ToString("X2"));
    }

    private static void WritePineMemory(long address, byte[] data)
    {
        const long EeRamSize = 0x02000000L;

        if (address < 0 || data == null || data.Length == 0)
            throw new InvalidOperationException("PINE書き込み先またはデータが不正です。");

        long endAddress = checked(address + data.Length);

        if (endAddress > EeRamSize)
            throw new InvalidOperationException("PINE書き込み範囲がEE RAMを超えています。");

        long firstWord = address & ~3L;
        long lastWord = (endAddress - 1) & ~3L;

        for (long wordAddress = firstWord;
             wordAddress <= lastWord;
             wordAddress += 4)
        {
            uint originalWord = ReadPineWord(checked((uint)wordAddress));
            byte[] word = BitConverter.GetBytes(originalWord);

            for (int byteIndex = 0; byteIndex < 4; byteIndex++)
            {
                long currentAddress = wordAddress + byteIndex;

                if (currentAddress >= address && currentAddress < endAddress)
                {
                    int sourceIndex = checked((int)(currentAddress - address));
                    word[byteIndex] = data[sourceIndex];
                }
            }

            uint newWord = BitConverter.ToUInt32(word, 0);
            WritePineWord(checked((uint)wordAddress), newWord);

            uint verifyWord = ReadPineWord(checked((uint)wordAddress));

            if (verifyWord != newWord)
                throw new IOException(
                    "PINE書き込み後の検証に失敗しました。" +
                    "\r\nアドレス: 0x" + wordAddress.ToString("X8") +
                    "\r\n期待値: 0x" + newWord.ToString("X8") +
                    "\r\n実際: 0x" + verifyWord.ToString("X8"));
        }
    }

    private static byte[] MakeNullTerminated(
        string value)
    {
        byte[] text = Cp932.GetBytes(value);
        byte[] result = new byte[text.Length + 1];

        Buffer.BlockCopy(
            text,
            0,
            result,
            0,
            text.Length);

        result[text.Length] = 0;
        return result;
    }

    private static (
        long Address,
        byte[] OriginalData,
        string MatchedName) FindHayabusaNameAddress(
            IntPtr process,
            long eeBase)
    {
        const int EeRamSize = 0x02000000;

        if (process == IntPtr.Zero)
            throw new InvalidOperationException(
                "PCSX2プロセスハンドルが無効です。");

        if (eeBase <= 0)
            throw new InvalidOperationException(
                "EE RAM基準アドレスが不正です。");

        // 既知の表示名候補をCP932・NUL終端で検索する。
        // 候補を推測で選ばず、EE RAM全体で一致が1件だけの場合に限り採用する。
        string[] candidates =
        {
            "エレファントクロウ",
            "ｴﾚﾌｧﾝﾄｸﾛｳ",
            "エレファントクロー",
            "ｴﾚﾌｧﾝﾄｸﾛｰ",
            "象牙の爪"
        };

        byte[] memory =
            ReadMemory(process, eeBase, EeRamSize);

        var matches =
            new System.Collections.Generic.List<(
                int Offset,
                string Name,
                byte[] Data)>();

        foreach (string candidate in candidates)
        {
            byte[] pattern = MakeNullTerminated(candidate);

            int searchFrom = 0;

            while (searchFrom <= memory.Length - pattern.Length)
            {
                int hit = Array.IndexOf(
                    memory,
                    pattern[0],
                    searchFrom);

                if (hit < 0)
                    break;

                bool same = true;

                for (int i = 0; i < pattern.Length; i++)
                {
                    if (memory[hit + i] != pattern[i])
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    matches.Add((
                        hit,
                        candidate,
                        (byte[])pattern.Clone()));

                    searchFrom = hit + pattern.Length;
                }

                if (!same)
                    searchFrom = hit + 1;
            }
        }

        if (matches.Count != 1)
        {
            var detail = new StringBuilder();

            foreach (var match in matches)
            {
                detail.Append("候補: ")
                    .Append(match.Name)
                    .Append(" / EE+0x")
                    .Append(match.Offset.ToString("X8"))
                    .Append("\r\n");
            }

            if (matches.Count == 0)
                detail.Append("候補文字列の完全一致は見つかりませんでした。");

            if (matches.Count > 1)
                detail.Append("複数の一致があるため、誤書き込み防止のため停止しました。");

            throw new InvalidOperationException(
                "はやぶさの爪の元アイテム名を一意に特定できません。\r\n" +
                "EE RAM検索結果: " + matches.Count + " 件\r\n" +
                detail.ToString() +
                "\r\nゲーム内のアイテム名データを確認する必要があります。");
        }

        var found = matches[0];

        return (
            checked(eeBase + found.Offset),
            found.Data,
            found.Name);
    }

    private static byte[] MakeFixedDescription()
    {
        string value =
            "[ﾊﾔぶｻﾉﾂﾒ]\r" +
            "装備ｽﾙﾄ攻撃力が上がﾙぞ。\r" +
            "ﾊﾔぶｻﾉﾖｳﾆ2回攻撃でｷﾙぞ。\r" +
            "ポポロ専用ﾉ武器だぞ。";

        byte[] encoded = Cp932.GetBytes(value);

        if (encoded.Length + 1 > OriginalDescriptionLength)
        {
            throw new InvalidOperationException(
                "新しい説明文が元の説明文領域より長くなっています。\r\n" +
                "文字数ではなくCP932のバイト数で検証しました。\r\n" +
                "必要サイズ: " + (encoded.Length + 1) +
                "\r\n確保サイズ: " + OriginalDescriptionLength);
        }

        byte[] result =
            new byte[OriginalDescriptionLength];

        Buffer.BlockCopy(
            encoded,
            0,
            result,
            0,
            encoded.Length);

        result[encoded.Length] = 0;

        return result;
    }

    private static (long Offset, byte[] OriginalData, string MatchedName) FindUniqueRingName(
        byte[] ram,
        string[] candidates,
        string itemLabel)
    {
        var matches = new System.Collections.Generic.List<(int Offset, string Name, byte[] Data)>();

        foreach (string candidate in candidates)
        {
            byte[] pattern = MakeNullTerminated(candidate);
            int searchFrom = 0;

            while (searchFrom <= ram.Length - pattern.Length)
            {
                int hit = Array.IndexOf(ram, pattern[0], searchFrom);

                if (hit < 0)
                    break;

                bool same = true;

                for (int i = 0; i < pattern.Length; i++)
                {
                    if (ram[hit + i] != pattern[i])
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    matches.Add((hit, candidate, (byte[])pattern.Clone()));
                    searchFrom = hit + pattern.Length;
                }

                if (!same)
                    searchFrom = hit + 1;
            }
        }

        if (matches.Count != 1)
        {
            var detail = new StringBuilder();

            foreach (var match in matches)
            {
                detail.Append("候補: ")
                    .Append(match.Name)
                    .Append(" / EE+0x")
                    .Append(match.Offset.ToString("X8"))
                    .Append("\r\n");
            }

            throw new InvalidOperationException(
                itemLabel + "の名前をEE RAM内で一意に特定できません。\r\n" +
                "検出件数: " + matches.Count + "\r\n" +
                detail.ToString() +
                "安全のため書き込みを中止しました。");
        }

        var found = matches[0];
        return (found.Offset, found.Data, found.Name);
    }

    private static long FindUniqueStaticDescriptionOffset(
        byte[] ram,
        byte[] originalPattern)
    {
        if (ram == null || originalPattern == null || originalPattern.Length < 2)
            throw new InvalidOperationException("説明文検索データが不正です。");

        int foundOffset = -1;
        int matchCount = 0;
        int searchFrom = 0;

        while (searchFrom <= ram.Length - originalPattern.Length)
        {
            int hit = Array.IndexOf(ram, originalPattern[0], searchFrom);

            if (hit < 0)
                break;

            bool same = true;

            for (int i = 0; i < originalPattern.Length; i++)
            {
                if (ram[hit + i] != originalPattern[i])
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                if (hit != RuntimeDescriptionOffset)
                {
                    foundOffset = hit;
                    matchCount++;
                }

                searchFrom = hit + originalPattern.Length;
            }

            if (!same)
                searchFrom = hit + 1;

            if (matchCount > 1)
                break;
        }

        if (matchCount != 1)
        {
            throw new InvalidOperationException(
                "静的な指輪説明文をEE RAM内で一意に特定できません。" +
                "\r\n一致件数: " + matchCount +
                "\r\n検索サイズ: " + originalPattern.Length +
                "\r\n実行時表示バッファ EE+0x" + RuntimeDescriptionOffset.ToString("X8") + " は検索対象から除外しました。" +
                "\r\n誤書き込み防止のため停止しました。");
        }

        return foundOffset;
    }

    private static byte[] MakeFixedRingDescription(string value, int capacity)
{
    byte[] encoded = Cp932.GetBytes(value);

    if (capacity < 1)
        throw new InvalidOperationException("指輪説明文の確保サイズが不正です。");

    if (encoded.Length + 1 > capacity)
    {
        throw new InvalidOperationException(
            "新しい指輪の説明文が元の領域より長くなっています。\r\n" +
            "必要サイズ: " + (encoded.Length + 1) + " bytes\r\n" +
            "確保サイズ: " + capacity + " bytes");
    }

    byte[] result = new byte[capacity];
    Buffer.BlockCopy(encoded, 0, result, 0, encoded.Length);
    result[encoded.Length] = 0;
    return result;
}

    private void ApplyRingUpgrade(bool yamabiko)
    {
        if (!Pcsx2LiveSession.IsConnected || Pcsx2LiveSession.ProcessId <= 0)
        {
            MessageBox.Show(
                this,
                "先にメイン画面からPCSX2へ接続してください。",
                "PCSX2未接続",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        string itemLabel = yamabiko ? "魔封じの指輪" : "見切りの指輪";
        string patchPrefix = yamabiko ? "ﾔﾏびｺﾉ指輪" : "ﾎｼﾌﾘﾉ指輪";

        string descriptionText = yamabiko
            ? "[ﾔﾏびｺﾉﾕびﾜ]\r魔法攻撃ｦ 防ぐｺﾄがでｷﾙぞ。\rｺﾉ指輪ｦ装備ｼﾃﾓ\r動ｷが遅ｸﾅﾗﾅｲぞ。"
            : "[ﾎｼﾌﾘﾉﾕびﾜ]\r攻撃ｦﾖｹﾔｽｸﾅﾙぞ。\rｺﾉ指輪ｦ装備ｼﾃﾓ\r動ｷが遅ｸﾅﾗﾅｲぞ。";

        string[] nameCandidates = yamabiko
            ? new string[]
            {
                "魔封じの指輪",
                "まふうじの指輪",
                "まふうじのゆびわ",
                "マフウジノ指輪",
                "マフウジノユビワ",
                "魔封じﾉ指輪",
                "ﾏﾌｳｼﾞﾉﾕﾋﾞﾜ"
            }
            : new string[]
            {
                "見切りの指輪",
                "みきりの指輪",
                "みきりのゆびわ",
                "ミキリノ指輪",
                "ミキリノユビワ",
                "見切ﾘﾉ指輪",
                "ﾐｷﾘﾉﾕﾋﾞﾜ"
            };

        if (MessageBox.Show(
            this,
            itemLabel + "の説明文と鈍足解除を変更します。\r\n\r\n" +
            "ゲーム内の説明画面を開く必要はありません。\r\n" +
            "EE RAM内の元データを直接検索して変更します。\r\n\r\n" +
            "変更内容:\r\n" +
            "・アイテム名\r\n" +
            "・静的説明文\r\n" +
            "・鈍足解除コード 2か所\r\n\r\n" +
            "PCSX2のメモリを書き換えます。実行しますか？",
            "指輪改造の確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        IntPtr process = IntPtr.Zero;
        var changes = new System.Collections.Generic.List<MemoryChange>();
        var completed = new System.Collections.Generic.List<MemoryChange>();
        long eeBase = 0;

        try
        {
            _itemStatus.Text = "PCSX2のEE RAMを読み取っています...";

            eeBase = Pcsx2MemoryLayoutBuilder.GetEEmemBase(
                Pcsx2LiveSession.ProcessId);

            if (eeBase <= 0)
                throw new InvalidOperationException("EE RAM基準アドレスを取得できません。");

            const uint access = 0x0008 | 0x0010 | 0x0020 | 0x0400;

            process = OpenProcess(
                access,
                false,
                Pcsx2LiveSession.ProcessId);

            if (process == IntPtr.Zero)
                throw new IOException(
                    "PCSX2プロセスを開けません。\r\nWin32エラー: " +
                    Marshal.GetLastWin32Error());

            const int EeRamSize = 0x02000000;
            byte[] ram = ReadMemory(process, eeBase, EeRamSize);


            string originalDescriptionText = yamabiko
                ? "[ﾏﾌｳじﾉﾕびﾜ]\r魔法攻撃ｦ 防ぐｺﾄがでｷﾙぞ。\rｺﾉ指輪ｦ装備ｽﾙﾄ\r動ｷが遅ｸﾅｯﾃｼﾏｳぞ。"
                : "[ﾐｷﾘﾉﾕびﾜ]\r攻撃ｦﾖｹﾔｽｸﾅﾙぞ。\rｺﾉ指輪ｦ装備ｽﾙﾄ\r動ｷが遅ｸﾅｯﾃｼﾏｳぞ。";

            byte[] originalDescriptionPattern = MakeNullTerminated(
                originalDescriptionText);

            long staticDescriptionOffset = FindUniqueStaticDescriptionOffset(
                ram,
                originalDescriptionPattern);

            if (staticDescriptionOffset < 0 || staticDescriptionOffset > ram.Length - originalDescriptionPattern.Length)
                throw new InvalidOperationException("静的説明文の書き込み範囲がEE RAM外です。");

            byte[] oldStaticDescription = new byte[originalDescriptionPattern.Length];
            Array.Copy(ram, checked((int)staticDescriptionOffset), oldStaticDescription, 0, originalDescriptionPattern.Length);

            byte[] descriptionData = MakeFixedRingDescription(descriptionText, originalDescriptionPattern.Length);

            long staticDescriptionAddress = checked(eeBase + staticDescriptionOffset);
            long nameOffset = yamabiko ? 0x0010536DL : 0x00105304L;
            string originalName = yamabiko ? "魔封じﾉ指輪" : "見切ﾘﾉ指輪";
            string newName = yamabiko ? "ﾔﾏびｺﾉ指輪" : "ﾎｼﾌﾘﾉ指輪";

            byte[] oldNameData = MakeNullTerminated(originalName);
            byte[] currentNameData = new byte[oldNameData.Length];
            Array.Copy(ram, checked((int)nameOffset), currentNameData, 0, currentNameData.Length);

            if (!System.Linq.Enumerable.SequenceEqual(oldNameData, currentNameData))
            {
                throw new InvalidOperationException(
                    "指輪の元の名前が指定アドレスにありません。" +
                    "\r\n対象: " + itemLabel +
                    "\r\nEE+0x" + nameOffset.ToString("X8") +
                    "\r\n誤書き込み防止のため停止しました。");
            }

            byte[] requestedNameData = MakeNullTerminated(newName);

            if (requestedNameData.Length > oldNameData.Length)
                throw new InvalidOperationException("変更後の名前が元の名前領域より長くなっています。");

            byte[] nameData = new byte[oldNameData.Length];
            Buffer.BlockCopy(requestedNameData, 0, nameData, 0, requestedNameData.Length);

            long nameAddress = checked(eeBase + nameOffset);


            changes.Add(new MemoryChange(
                "指輪アイテム名",
                nameAddress,
                nameData,
                oldNameData));
            changes.Add(new MemoryChange(
                "指輪の説明文・静的データ",
                staticDescriptionAddress,
                descriptionData,
                oldStaticDescription));

            long[] codeOffsets = { 0x002C8F18L, 0x002C8F04L };
            string[] codeNames =
            {
                "鈍足解除・A02C8F18",
                "鈍足解除・A02C8F04"
            };

            for (int i = 0; i < codeOffsets.Length; i++)
            {
                if (codeOffsets[i] < 0 || codeOffsets[i] > ram.Length - 4)
                    throw new InvalidOperationException(
                        "鈍足解除コードのアドレスがEE RAM範囲外です。\r\n" +
                        "EE+0x" + codeOffsets[i].ToString("X8"));

                byte[] oldCode = new byte[4];
                Array.Copy(ram, checked((int)codeOffsets[i]), oldCode, 0, 4);

                bool alreadyZero =
                    oldCode[0] == 0 &&
                    oldCode[1] == 0 &&
                    oldCode[2] == 0 &&
                    oldCode[3] == 0;

                if (!alreadyZero)
                {
                    changes.Add(new MemoryChange(
                        codeNames[i],
                        checked(eeBase + codeOffsets[i]),
                        new byte[4],
                        oldCode));
                }
            }

            _itemStatus.Text = "書き込み先を事前照合しています...";

            foreach (MemoryChange change in changes)
            {
                byte[] current = ReadMemory(
                    process,
                    change.Address,
                    change.OldData.Length);

                if (!System.Linq.Enumerable.SequenceEqual(current, change.OldData))
                    throw new IOException(
                        change.Name + "の書き込み直前照合に失敗しました。\r\n" +
                        "アドレス: 0x" + change.Address.ToString("X"));
            }

            foreach (MemoryChange change in changes)
            {
                WritePineMemory(checked(change.Address - eeBase), change.NewData);
                completed.Add(change);

                byte[] verify = ReadMemory(
                    process,
                    change.Address,
                    change.NewData.Length);

                if (!System.Linq.Enumerable.SequenceEqual(verify, change.NewData))
                    throw new IOException(
                        change.Name + "の読み戻し検証に失敗しました。\r\n" +
                        "アドレス: 0x" + change.Address.ToString("X"));
            }


            Torneko4PatchStore.Register(
                patchPrefix + "・アイテム名",
                nameOffset,
                nameData);
            Torneko4PatchStore.Register(
                patchPrefix + "・説明文",
                staticDescriptionOffset,
                descriptionData);

            Torneko4PatchStore.Register(
                "鈍足解除・A02C8F18",
                0x002C8F18L,
                new byte[4]);

            Torneko4PatchStore.Register(
                "鈍足解除・A02C8F04",
                0x002C8F04L,
                new byte[4]);

            _itemStatus.Text =
                itemLabel + "の改造が完了しました。変更箇所 " +
                changes.Count.ToString() + "か所の読み戻し検証に成功しました。";

            MessageBox.Show(
                this,
                itemLabel + "の改造が完了しました。\r\n\r\n" +
                "アイテム名: " + newName + "\r\n" +

                "アイテム名アドレス: EE+0x" + nameOffset.ToString("X8") + "\r\n" +
                "説明文アドレス: EE+0x" + staticDescriptionOffset.ToString("X8") + "\r\n" +
                "鈍足解除: EE+0x002C8F18 / EE+0x002C8F04\r\n\r\n" +
                "実行時説明文バッファは変更していません。\r\n" +
                ".Torneko4保存対象に登録しました。",
                "指輪改造成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var rollbackReport = new StringBuilder();

            for (int i = completed.Count - 1; i >= 0; i--)
            {
                MemoryChange change = completed[i];

                try
                {
                    WritePineMemory(checked(change.Address - eeBase), change.OldData);

                    byte[] restored = ReadMemory(
                        process,
                        change.Address,
                        change.OldData.Length);

                    bool success = System.Linq.Enumerable.SequenceEqual(
                        restored,
                        change.OldData);

                    rollbackReport.AppendLine(
                        change.Name + ": " + (success ? "復元成功" : "復元確認失敗"));
                }
                catch (Exception rollbackError)
                {
                    rollbackReport.AppendLine(
                        change.Name + ": 復元エラー " + rollbackError.Message);
                }
            }

            _itemStatus.Text = "指輪改造失敗: " + ex.Message;

            MessageBox.Show(
                this,
                "指輪改造に失敗しました。\r\n\r\n" +
                ex.Message + "\r\n\r\n" +
                "書き込み済みデータの復元結果:\r\n" +
                (rollbackReport.Length == 0
                    ? "書き込み前の確認段階で停止しました。"
                    : rollbackReport.ToString()),
                "指輪改造エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        if (process != IntPtr.Zero)
            CloseHandle(process);
    }

    private void ApplySlimeRing()
    {
        if (!Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.ProcessId <= 0)
        {
            MessageBox.Show(
                this,
                "先にメイン画面からPCSX2へ接続してください。",
                "PCSX2未接続",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        const int EeRamSize = 0x02000000;

        const long DescriptionRecordOffset = 0x00139D00L;
        const long NameRecordOffset = 0x00139D50L;

        const long DescriptionIndexOffset = 0x00100794L;
        const long NameIndexOffset = 0x00100798L;

        const long OriginalDescriptionRecordOffset = 0x00105427L;
        const long OriginalNameRecordOffset = 0x00105441L;

        byte[] oldPriceSignature =
        {
            0x88, 0x13, 0x00, 0xC4, 0x09,
            0x00, 0x03, 0x05, 0xA4, 0x00
        };

        byte[] newPriceSignature =
        {
            0x98, 0x3A, 0x00, 0x4C, 0x1D,
            0x00, 0x03, 0x05, 0xA4, 0x00
        };

        byte[] newBuyPriceData =
            BitConverter.GetBytes((ushort)15000);

        byte[] newSellPriceData =
            BitConverter.GetBytes((ushort)7500);

        string newName = "スライムの指輪";

        string descriptionText =
            "[ｽﾗｲﾑﾉﾕびﾜ]\r" +
            "スライム系ﾉ仲間が強ｸﾅﾙぞ。\r" +
            "ｺﾉ指輪ﾉ効果ﾊ合成でｷﾅｲぞ。";

        if (MessageBox.Show(
            this,
            "アイテム164をスライムの指輪に変更します。\r\n\r\n" +
            "アイテム名:\r\n" +
            newName + "\r\n\r\n" +
            "説明文:\r\n" +
            descriptionText.Replace("\r", "\r\n") + "\r\n\r\n" +
            "説明文と名前のリソースレコードを新しい領域へ移設します。\r\n" +
            "元のレコードは上書きしません。\r\n\r\n" +
            "価格変更: 今回は対象外です。\r\n" +
            "" +
            "価格データ7件を変更します。\r\n\r\n" +
            "先にPCSX2を一時停止してください。停止中に実行しますか？",
            "スライムの指輪への変更",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        IntPtr process = IntPtr.Zero;

        var changes =
            new System.Collections.Generic.List<MemoryChange>();

        var completed =
            new System.Collections.Generic.List<MemoryChange>();

        long eeBase = 0;

        try
        {
            _itemStatus.Text =
                "PCSX2のEE RAMを確認しています...";

            eeBase = Pcsx2MemoryLayoutBuilder.GetEEmemBase(
                Pcsx2LiveSession.ProcessId);

            if (eeBase <= 0)
                throw new InvalidOperationException(
                    "EE RAM基準アドレスを取得できません。");

            const uint access =
                0x0008 |
                0x0010 |
                0x0020 |
                0x0400;

            process = OpenProcess(
                access,
                false,
                Pcsx2LiveSession.ProcessId);

            if (process == IntPtr.Zero)
                throw new IOException(
                    "PCSX2プロセスを開けません。\r\nWin32エラー: " +
                    Marshal.GetLastWin32Error());

            // PINEの状態確認コマンドは使用しない。
            // 既存のWindowsプロセスメモリ読み取りでEE RAMを取得する。
            byte[] ram = ReadMemory(
                process,
                eeBase,
                EeRamSize);

            // ------------------------------------------------
            // Verify original resource IDs
            // ------------------------------------------------

            byte[] expectedDescriptionId =
                MakeNullTerminated("item_164_e");

            byte[] currentDescriptionId = ReadMemory(
                process,
                checked(eeBase + OriginalDescriptionRecordOffset),
                expectedDescriptionId.Length);

            if (!System.Linq.Enumerable.SequenceEqual(
                expectedDescriptionId,
                currentDescriptionId))
            {
                throw new InvalidOperationException(
                    "説明文レコードIDが想定と一致しません。\r\n" +
                    "確認先: EE+0x00105427\r\n" +
                    "安全のため停止しました。");
            }

            byte[] expectedNameId =
                MakeNullTerminated("item_164");

            byte[] currentNameId = ReadMemory(
                process,
                checked(eeBase + OriginalNameRecordOffset),
                expectedNameId.Length);

            if (!System.Linq.Enumerable.SequenceEqual(
                expectedNameId,
                currentNameId))
            {
                throw new InvalidOperationException(
                    "アイテム名レコードIDが想定と一致しません。\r\n" +
                    "確認先: EE+0x00105441\r\n" +
                    "安全のため停止しました。");
            }

            // ------------------------------------------------
            // Verify original description
            // ------------------------------------------------

            byte[] expectedOriginalDescription =
                MakeNullTerminated("指輪ﾉﾖび２だ。");

            byte[] currentOriginalDescription = ReadMemory(
                process,
                checked(eeBase + 0x00105432L),
                expectedOriginalDescription.Length);

            if (!System.Linq.Enumerable.SequenceEqual(
                expectedOriginalDescription,
                currentOriginalDescription))
            {
                throw new InvalidOperationException(
                    "アイテム164の元説明文が想定と一致しません。\r\n" +
                    "確認先: EE+0x00105432\r\n" +
                    "安全のため停止しました。");
            }

            // ------------------------------------------------
            // Verify index pointers
            // ------------------------------------------------

            byte[] expectedDescriptionPointer =
                BitConverter.GetBytes(
                    (uint)OriginalDescriptionRecordOffset);

            byte[] expectedNamePointer =
                BitConverter.GetBytes(
                    (uint)OriginalNameRecordOffset);

            byte[] currentDescriptionPointer = ReadMemory(
                process,
                checked(eeBase + DescriptionIndexOffset),
                4);

            byte[] currentNamePointer = ReadMemory(
                process,
                checked(eeBase + NameIndexOffset),
                4);

            if (!System.Linq.Enumerable.SequenceEqual(
                expectedDescriptionPointer,
                currentDescriptionPointer))
            {
                throw new InvalidOperationException(
                    "説明文の索引ポインタが想定と一致しません。\r\n" +
                    "確認先: EE+0x00100794\r\n" +
                    "安全のため停止しました。");
            }

            if (!System.Linq.Enumerable.SequenceEqual(
                expectedNamePointer,
                currentNamePointer))
            {
                throw new InvalidOperationException(
                    "名前の索引ポインタが想定と一致しません。\r\n" +
                    "確認先: EE+0x00100798\r\n" +
                    "安全のため停止しました。");
            }

            // ------------------------------------------------
            // Verify destination area
            // ------------------------------------------------

            byte[] freeRegion = ReadMemory(
                process,
                checked(eeBase + DescriptionRecordOffset),
                0x100);

            for (int i = 0; i < freeRegion.Length; i++)
            {
                if (freeRegion[i] != 0)
                {
                    throw new InvalidOperationException(
                        "移設先の空き領域にデータがあります。\r\n" +
                        "確認先: EE+0x00139D00\r\n" +
                        "誤上書き防止のため停止しました。");
                }
            }

            // ------------------------------------------------
            // Construct description record
            // ------------------------------------------------

            byte[] descriptionId =
                MakeNullTerminated("item_164_e");

            byte[] descriptionPayload =
                MakeNullTerminated(descriptionText);

            byte[] descriptionRecord =
                new byte[descriptionId.Length + descriptionPayload.Length];

            Array.Copy(
                descriptionId,
                0,
                descriptionRecord,
                0,
                descriptionId.Length);

            Array.Copy(
                descriptionPayload,
                0,
                descriptionRecord,
                descriptionId.Length,
                descriptionPayload.Length);

            // ------------------------------------------------
            // Construct name record
            // ------------------------------------------------

            byte[] nameId =
                MakeNullTerminated("item_164");

            byte[] namePayload =
                MakeNullTerminated(newName);

            byte[] nameRecord =
                new byte[nameId.Length + namePayload.Length];

            Array.Copy(
                nameId,
                0,
                nameRecord,
                0,
                nameId.Length);

            Array.Copy(
                namePayload,
                0,
                nameRecord,
                nameId.Length,
                namePayload.Length);

            if (descriptionRecord.Length >
                NameRecordOffset - DescriptionRecordOffset)
            {
                throw new InvalidOperationException(
                    "説明文レコードが名前レコードの配置位置を超えています。");
            }

            if (DescriptionRecordOffset + descriptionRecord.Length >
                NameRecordOffset)
            {
                throw new InvalidOperationException(
                    "説明文レコードと名前レコードが重複します。");
            }

            // ------------------------------------------------
            // Prepare changes
            // ------------------------------------------------

            changes.Add(new MemoryChange(
                "説明文レコード移設",
                checked(eeBase + DescriptionRecordOffset),
                descriptionRecord,
                new byte[descriptionRecord.Length]));

            changes.Add(new MemoryChange(
                "アイテム名レコード移設",
                checked(eeBase + NameRecordOffset),
                nameRecord,
                new byte[nameRecord.Length]));

            changes.Add(new MemoryChange(
                "説明文索引ポインタ",
                checked(eeBase + DescriptionIndexOffset),
                BitConverter.GetBytes((uint)DescriptionRecordOffset),
                expectedDescriptionPointer));

            changes.Add(new MemoryChange(
                "アイテム名索引ポインタ",
                checked(eeBase + NameIndexOffset),
                BitConverter.GetBytes((uint)NameRecordOffset),
                expectedNamePointer));

            // ------------------------------------------------
            // Slime Ring price records
            // ------------------------------------------------

            var priceRecordOffsets =
                new System.Collections.Generic.List<int>();

            for (int offset = 0;
                 offset <= ram.Length - oldPriceSignature.Length;
                 offset++)
            {
                bool originalMatch = true;
                bool modifiedMatch = true;

                for (int j = 0; j < oldPriceSignature.Length; j++)
                {
                    if (ram[offset + j] != oldPriceSignature[j])
                        originalMatch = false;

                    if (ram[offset + j] != newPriceSignature[j])
                        modifiedMatch = false;

                    if (!originalMatch && !modifiedMatch)
                        break;
                }

                if (originalMatch || modifiedMatch)
                    priceRecordOffsets.Add(offset);
            }

            if (priceRecordOffsets.Count == 0)
            {
                var report = new StringBuilder();

                report.AppendLine("Slime Ring Price Record Diagnostic");
                report.AppendLine("Detected count: " + priceRecordOffsets.Count);
                report.AppendLine("EE RAM size: 0x" + ram.Length.ToString("X8"));
                report.AppendLine();

                report.AppendLine("=== Detected records ===");

                foreach (int foundOffset in priceRecordOffsets)
                {
                    report.AppendLine(
                        "EE+0x" + foundOffset.ToString("X8") +
                        " : " +
                        Convert.ToHexString(ram, foundOffset, 10));
                }

                long[] knownOffsets =
                {
                    0x00650114L,
                    0x00650354L,
                    0x00650594L,
                    0x006507D4L,
                    0x00650A14L,
                    0x01FFA694L,
                    0x01FFA8D4L,
                    0x01FFAB14L,
                    0x01FFAD54L,
                    0x01FFAF94L
                };

                report.AppendLine();
                report.AppendLine("=== Previously identified 10 offsets ===");

                foreach (long knownOffset in knownOffsets)
                {
                    if (knownOffset >= 0 &&
                        knownOffset + 10 <= ram.Length)
                    {
                        report.AppendLine(
                            "EE+0x" + knownOffset.ToString("X8") +
                            " : " +
                            Convert.ToHexString(ram, (int)knownOffset, 10));
                    }
                }

                string reportPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "SlimeRingPriceScan.txt");

                File.WriteAllText(
                    reportPath,
                    report.ToString(),
                    Encoding.UTF8);

                throw new InvalidOperationException(
                    "価格レコードが1件も検出されませんでした。" +
                    "\r\n検出件数: " + priceRecordOffsets.Count +
                    "\r\n診断レポート: " + reportPath +
                    "\r\n書き込みは行っていません。");
            }

            foreach (int priceOffset in priceRecordOffsets)
            {
                byte[] currentBuyPrice = new byte[2];
                byte[] currentSellPrice = new byte[2];

                Array.Copy(
                    ram,
                    priceOffset,
                    currentBuyPrice,
                    0,
                    2);

                Array.Copy(
                    ram,
                    priceOffset + 3,
                    currentSellPrice,
                    0,
                    2);

                if (!System.Linq.Enumerable.SequenceEqual(
                    currentBuyPrice,
                    newBuyPriceData))
                {
                    changes.Add(new MemoryChange(
                        "スライムの指輪・買値15000G",
                        checked(eeBase + priceOffset),
                        newBuyPriceData,
                        currentBuyPrice));
                }

                if (!System.Linq.Enumerable.SequenceEqual(
                    currentSellPrice,
                    newSellPriceData))
                {
                    changes.Add(new MemoryChange(
                        "スライムの指輪・売値7500G",
                        checked(eeBase + priceOffset + 3),
                        newSellPriceData,
                        currentSellPrice));
                }
            }

            // ------------------------------------------------
            // Preflight verification
            // ------------------------------------------------

            _itemStatus.Text =
                "名前・説明文・価格の書き込み先を事前照合しています...";

            foreach (MemoryChange change in changes)
            {
                byte[] current = ReadMemory(
                    process,
                    change.Address,
                    change.OldData.Length);

                if (!System.Linq.Enumerable.SequenceEqual(
                    current,
                    change.OldData))
                {
                    throw new IOException(
                        change.Name + "の書き込み前照合に失敗しました。\r\n" +
                        "アドレス: 0x" + change.Address.ToString("X"));
                }
            }

            // ------------------------------------------------
            // Write and verify
            // ------------------------------------------------

            foreach (MemoryChange change in changes)
            {
                WriteMemory(
                    process,
                    change.Address,
                    change.NewData);

                completed.Add(change);

                byte[] verify = ReadMemory(
                    process,
                    change.Address,
                    change.NewData.Length);

                if (!System.Linq.Enumerable.SequenceEqual(
                    verify,
                    change.NewData))
                {
                    throw new IOException(
                        change.Name + "の読み戻し検証に失敗しました。\r\n" +
                        "アドレス: 0x" + change.Address.ToString("X"));
                }
            }

            // ------------------------------------------------
            // Register persistent patches
            // ------------------------------------------------

            Torneko4PatchStore.Register(
                "スライムの指輪・説明文レコード",
                DescriptionRecordOffset,
                descriptionRecord);

            Torneko4PatchStore.Register(
                "スライムの指輪・名前レコード",
                NameRecordOffset,
                nameRecord);

            Torneko4PatchStore.Register(
                "スライムの指輪・説明文索引",
                DescriptionIndexOffset,
                BitConverter.GetBytes((uint)DescriptionRecordOffset));

            Torneko4PatchStore.Register(
                "スライムの指輪・名前索引",
                NameIndexOffset,
                BitConverter.GetBytes((uint)NameRecordOffset));

            foreach (int priceOffset in priceRecordOffsets)
            {
                Torneko4PatchStore.Register(
                    "スライムの指輪・買値15000G",
                    priceOffset,
                    newBuyPriceData);

                Torneko4PatchStore.Register(
                    "スライムの指輪・売値7500G",
                    priceOffset + 3,
                    newSellPriceData);
            }

            _itemStatus.Text =
                "スライムの指輪への変更が完了しました。";

            MessageBox.Show(
                this,
                "スライムの指輪への変更が完了しました！\r\n\r\n" +
                "アイテム名: " + newName + "\r\n\r\n" +
                "説明文:\r\n" +
                descriptionText.Replace("\r", "\r\n") + "\r\n\r\n" +
                "説明文レコード: EE+0x00139D00\r\n" +
                "名前レコード: EE+0x00139D50\r\n" +
                "説明文索引: EE+0x00100794\r\n" +
                "名前索引: EE+0x00100798\r\n" +
                "価格: 買値15000G / 売値7500G\r\n" +
                "価格レコード: " + priceRecordOffsets.Count + "件\r\n\r\n" +
                "名前・説明文・索引と価格レコード" + priceRecordOffsets.Count + "件の処理を確認しました。\r\n" +
                "元のレコードは変更していません。\r\n" +
                "保存対象への登録も完了しました。",
                "スライムの指輪・変更成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var rollbackReport = new StringBuilder();

            for (int i = completed.Count - 1; i >= 0; i--)
            {
                MemoryChange change = completed[i];

                try
                {
                    WriteMemory(
                        process,
                        change.Address,
                        change.OldData);

                    byte[] restored = ReadMemory(
                        process,
                        change.Address,
                        change.OldData.Length);

                    bool success =
                        System.Linq.Enumerable.SequenceEqual(
                            restored,
                            change.OldData);

                    rollbackReport.AppendLine(
                        change.Name + ": " +
                        (success ? "復元成功" : "復元確認失敗"));
                }
                catch (Exception rollbackError)
                {
                    rollbackReport.AppendLine(
                        change.Name + ": 復元エラー " +
                        rollbackError.Message);
                }
            }

            _itemStatus.Text =
                "スライムの指輪への変更に失敗しました: " + ex.Message;

            if (process != IntPtr.Zero)
            {
                CloseHandle(process);
                process = IntPtr.Zero;
            }

            MessageBox.Show(
                this,
                "スライムの指輪への変更に失敗しました。\r\n\r\n" +
                ex.Message + "\r\n\r\n" +
                "書き込み済みデータの復元結果:\r\n" +
                (rollbackReport.Length == 0
                    ? "書き込み前の確認段階で停止しました。"
                    : rollbackReport.ToString()),
                "スライムの指輪・変更エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        if (process != IntPtr.Zero)
            CloseHandle(process);
    }
    private void ApplyHayabusa()
    {
        if (!Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.ProcessId <= 0)
        {
            MessageBox.Show(
                this,
                "先にメイン画面からPCSX2へ接続してください。",
                "PCSX2未接続",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        if (MessageBox.Show(
            this,
            "エレファントクロウをはやぶさの爪に変更します。\r\n\r\n" +
            "アイテム名:\r\n" +
            "ﾊﾔぶｻﾉ爪\r\n\r\n" +
            "説明文:\r\n" +
            "[ﾊﾔぶｻﾉﾂﾒ]\r\n" +
            "装備ｽﾙﾄ攻撃力が上がﾙぞ。\r\n" +
            "ﾊﾔぶｻﾉﾖｳﾆ2回攻撃でｷﾙぞ。\r\n" +
            "ポポロ専用ﾉ武器だぞ。\r\n\r\n" +
            "PCSX2のメモリを書き換えます。実行しますか？",
            "はやぶさの爪への変更",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        IntPtr process = IntPtr.Zero;
        var changes = new System.Collections.Generic.List<MemoryChange>();
        var completed = new System.Collections.Generic.List<MemoryChange>();
        try
        {
            _itemStatus.Text =
                "PCSX2のメモリを確認しています...";

            long eeBase =
                Pcsx2MemoryLayoutBuilder.GetEEmemBase(
                    Pcsx2LiveSession.ProcessId);

            if (eeBase <= 0)
                throw new InvalidOperationException(
                    "PCSX2のEE RAM基準アドレスを取得できません。");

            const uint access =
                0x0008 |
                0x0010 |
                0x0020 |
                0x0400;

            process = OpenProcess(
                access,
                false,
                Pcsx2LiveSession.ProcessId);

            if (process == IntPtr.Zero)
            {
                throw new IOException(
                    "PCSX2プロセスを開けません。\r\nWin32エラー: " +
                    Marshal.GetLastWin32Error());
            }

            var nameMatch =
                FindHayabusaNameAddress(process, eeBase);

            long nameAddress = nameMatch.Address;
            long nameOffset = checked(nameAddress - eeBase);

            if (nameOffset < 0 || nameOffset >= 0x02000000L)
                throw new InvalidOperationException(
                    "検索で特定した武器名アドレスがEE RAMの範囲外です。");

            byte[] oldName = nameMatch.OriginalData;
            byte[] requestedNameData =
                MakeNullTerminated("ﾊﾔぶｻﾉ爪");

            if (requestedNameData.Length > oldName.Length)
            {
                throw new InvalidOperationException(
                    "変更後の武器名が元の名前領域より長くなっています。\r\n" +
                    "元の領域: " + oldName.Length + " bytes\r\n" +
                    "変更後: " + requestedNameData.Length + " bytes");
            }

            // 元の名前領域と同じ長さで書き込み、残りはNULで埋める。
            byte[] nameData = new byte[oldName.Length];

            Buffer.BlockCopy(
                requestedNameData,
                0,
                nameData,
                0,
                requestedNameData.Length);

            long staticDescriptionAddress =
                checked(eeBase + ItemTableDescriptionOffset);
            long runtimeDescriptionAddress =
                checked(eeBase + RuntimeDescriptionOffset);


            byte[] descriptionData =
                MakeFixedDescription();

            _itemStatus.Text =
                "書き込み先を事前確認しています...";

            byte[] currentName =
                ReadMemory(process, nameAddress, oldName.Length);

            if (!System.Linq.Enumerable.SequenceEqual(
                oldName,
                currentName))
            {
                throw new InvalidOperationException(
                    "検索後に武器名のメモリ内容が変化しました。\r\n" +
                    "安全のため書き込みを中止しました。");
            }

            byte[] oldStaticDescription =
                ReadMemory(
                    process,
                    staticDescriptionAddress,
                    OriginalDescriptionLength);

            byte[] oldRuntimeDescription =
                ReadMemory(
                    process,
                    runtimeDescriptionAddress,
                    OriginalDescriptionLength);

            changes.Add(new MemoryChange(
                "アイテム名",
                nameAddress,
                nameData,
                oldName));

            changes.Add(new MemoryChange(
                "アイテム説明文・本体",
                staticDescriptionAddress,
                descriptionData,
                oldStaticDescription));

            changes.Add(new MemoryChange(
                "アイテム説明文・実行時コピー",
                runtimeDescriptionAddress,
                descriptionData,
                oldRuntimeDescription));

            _itemStatus.Text =
                "3か所の書き込み先を確認しました。変更を適用しています...";

            foreach (MemoryChange change in changes)
            {
                WriteMemory(
                    process,
                    change.Address,
                    change.NewData);

                completed.Add(change);

                byte[] verify = ReadMemory(
                    process,
                    change.Address,
                    change.NewData.Length);

                if (!System.Linq.Enumerable.SequenceEqual(
                    change.NewData,
                    verify))
                {
                    throw new IOException(
                        change.Name + "の読み戻し検証に失敗しました。\r\n" +
                        "アドレス: 0x" +
                        change.Address.ToString("X"));
                }
            }

            // 説明文2か所とアイテム名をEE RAM相対アドレスで登録する。
            Torneko4PatchStore.Register(
                "はやぶさの爪・アイテム説明文",
                ItemTableDescriptionOffset,
                descriptionData);

            Torneko4PatchStore.Register(
                "はやぶさの爪・実行時説明文",
                RuntimeDescriptionOffset,
                descriptionData);

            // 武器名もEE RAM相対アドレスとして保存する。
            Torneko4PatchStore.Register(
                "はやぶさ設定・アイテム名",
                nameOffset,
                nameData);

            _itemStatus.Text =
                "変更完了。3か所すべての読み戻し検証に成功しました。";
            HayabusaDoubleAttackMonitor.Start(_itemStatus);

            MessageBox.Show(
                this,
                "はやぶさの爪への変更が完了しました！\r\n\r\n" +
                "アイテム名: 0x" +
                nameAddress.ToString("X") + "\r\n" +
                "説明文本体: EE+0x" +
                ItemTableDescriptionOffset.ToString("X8") + "\r\n" +
                "説明文実行時コピー: EE+0x" +
                RuntimeDescriptionOffset.ToString("X8") + "\r\n\r\n" +
                "4か所すべての書き込みと読み戻しを確認しました。\r\n" +
                "ゲーム画面でアイテム名と説明文を確認してください。",
                "変更成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            string rollbackReport = "";

            for (int i = completed.Count - 1; i >= 0; i--)
            {
                MemoryChange change = completed[i];

                try
                {
                    WriteMemory(
                        process,
                        change.Address,
                        change.OldData);

                    byte[] verify = ReadMemory(
                        process,
                        change.Address,
                        change.OldData.Length);

                    bool restored =
                        System.Linq.Enumerable.SequenceEqual(
                            change.OldData,
                            verify);

                    rollbackReport +=
                        change.Name + ": " +
                        (restored ? "復元成功" : "復元確認失敗") +
                        "\r\n";
                }
                catch (Exception rollbackError)
                {
                    rollbackReport +=
                        change.Name + ": 復元エラー " +
                        rollbackError.Message + "\r\n";
                }
            }

            _itemStatus.Text =
                "変更失敗: " + ex.Message;

            MessageBox.Show(
                this,
                "はやぶさの爪への変更に失敗しました。\r\n\r\n" +
                ex.Message + "\r\n\r\n" +
                "書き込み済みデータの復元結果:\r\n" +
                (rollbackReport.Length == 0
                    ? "書き込み前の確認段階で停止しました。"
                    : rollbackReport),
                "変更エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (process != IntPtr.Zero)
                CloseHandle(process);
        }
    }
}