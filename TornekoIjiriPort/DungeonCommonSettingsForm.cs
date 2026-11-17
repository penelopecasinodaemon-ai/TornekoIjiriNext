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

    private const long HayabusaNameAddress = 0x1B322C27C42L;
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

        Shown += (_, _) => ReadCurrent();
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
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 5
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));

        var heading = new Label
        {
            Text = "エレファントクロウ → はやぶさの爪",
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 15, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var nameLabel = new Label
        {
            Text = "変更後のアイテム名：ﾊﾔぶｻﾉ爪",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var description = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Yu Gothic UI", 11),
            Text =
                "[ﾊﾔぶｻﾉﾂﾒ]" + Environment.NewLine +
                "装備ｽﾙﾄ攻撃力が上がﾙぞ。" + Environment.NewLine +
                "ﾊﾔぶｻﾉﾖｳﾆ2回攻撃でｷﾙぞ。" + Environment.NewLine +
                "ポポロ専用ﾉ武器だぞ。"
        };

        var applyButton = new Button
        {
            Text = "エレファントクロウをはやぶさの爪に変更",
            Dock = DockStyle.Fill,
            Height = 48,
            Font = new Font("Yu Gothic UI", 12, FontStyle.Bold),
            BackColor = Color.FromArgb(220, 240, 255)
        };

        applyButton.Click += (_, _) => ApplyHayabusa();

        _itemStatus = new Label
        {
            Text = "PCSX2に接続して、ゲームを起動した状態で実行してください。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(nameLabel, 0, 1);
        root.Controls.Add(description, 0, 2);
        root.Controls.Add(applyButton, 0, 3);
        root.Controls.Add(_itemStatus, 0, 4);

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

    private static byte[] SendPineCommand(byte[] command)
    {
        byte[] request = new byte[4 + command.Length];

        WriteUInt32(request, 0, (uint)request.Length);
        Buffer.BlockCopy(command, 0, request, 4, command.Length);

        using var client = new TcpClient();
        client.ReceiveTimeout = 4000;
        client.SendTimeout = 4000;
        client.Connect(Host, Port);

        using NetworkStream stream = client.GetStream();
        stream.Write(request, 0, request.Length);
        stream.Flush();

        byte[] header = ReadExact(stream, 4);
        uint responseLength = ReadUInt32(header, 0);

        if (responseLength < 5 || responseLength > 1024)
            throw new IOException(
                "PINE応答サイズが不正です: " + responseLength);

        return ReadExact(stream, (int)responseLength - 4);
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
        byte[] data)
    {
        if (address <= 0 || data == null || data.Length == 0)
            throw new InvalidOperationException(
                "書き込みアドレスまたはデータが不正です。");

        UIntPtr written;

        bool success = WriteProcessMemory(
            process,
            new IntPtr(address),
            data,
            new UIntPtr((uint)data.Length),
            out written);

        if (!success || written.ToUInt64() != (ulong)data.Length)
        {
            throw new IOException(
                "メモリ書き込みに失敗しました。\r\n" +
                "アドレス: 0x" + address.ToString("X") +
                "\r\n要求サイズ: " + data.Length +
                "\r\n書き込みサイズ: " + written.ToUInt64() +
                "\r\nWin32エラー: " +
                Marshal.GetLastWin32Error());
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

            long nameAddress = HayabusaNameAddress;
            long staticDescriptionAddress =
                checked(eeBase + ItemTableDescriptionOffset);
            long runtimeDescriptionAddress =
                checked(eeBase + RuntimeDescriptionOffset);

            byte[] nameData =
                MakeNullTerminated("ﾊﾔぶｻﾉ爪");

            byte[] descriptionData =
                MakeFixedDescription();

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

            _itemStatus.Text =
                "書き込み先を事前確認しています...";

            byte[] oldName =
                ReadMemory(process, nameAddress, nameData.Length);

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

            _itemStatus.Text =
                "変更完了。3か所すべての読み戻し検証に成功しました。";

            MessageBox.Show(
                this,
                "はやぶさの爪への変更が完了しました！\r\n\r\n" +
                "アイテム名: 0x" +
                nameAddress.ToString("X") + "\r\n" +
                "説明文本体: EE+0x" +
                ItemTableDescriptionOffset.ToString("X8") + "\r\n" +
                "説明文実行時コピー: EE+0x" +
                RuntimeDescriptionOffset.ToString("X8") + "\r\n\r\n" +
                "3か所すべての書き込みと読み戻しを確認しました。\r\n" +
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