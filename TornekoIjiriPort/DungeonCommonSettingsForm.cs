using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class DungeonCommonSettingsForm : Form
{
    private const string Host = "127.0.0.1";
    private const int Port = 28011;

    // 改造コード 2022FE04 を PINE用アドレスへ変換
    private const uint SpawnInstructionAddress = 0x0022FE04;

    private readonly NumericUpDown _turns;
    private readonly Label _current;
    private readonly Label _status;

    public DungeonCommonSettingsForm()
    {
        Text = "ダンジョン共通設定";
        StartPosition = FormStartPosition.CenterParent;
        Width = 570;
        Height = 330;
        MinimumSize = new Size(500, 300);
        Font = new Font("Yu Gothic UI", 10);

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

        Controls.Add(root);

        Shown += (_, _) => ReadCurrent();
    }

    private static byte[] ReadExact(NetworkStream stream, int count)
    {
        byte[] result = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int received = stream.Read(result, offset, count - offset);

            if (received <= 0)
                throw new IOException("PINE接続が途中で切断されました。");

            offset += received;
        }

        return result;
    }

    private static byte[] SendPineCommand(byte[] command)
    {
        byte[] request = new byte[4 + command.Length];

        // PINE要求ヘッダーにはヘッダーを含む全体サイズを指定
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

        // PINE応答サイズはヘッダー4バイトを含む全体サイズ
        if (responseLength < 5 || responseLength > 1024)
            throw new IOException(
                "PINE応答サイズが不正です: " + responseLength);

        // ヘッダーは既に読み取っているため、残りだけ受信する
        return ReadExact(stream, (int)responseLength - 4);
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)(
            data[offset] |
            (data[offset + 1] << 8) |
            (data[offset + 2] << 16) |
            (data[offset + 3] << 24));
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static uint ReadInstruction()
    {
        // PINE Read32: opcode 2 + address
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
        // PINE Write32: opcode 6 + address + value
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
        // addiu v0, zero, immediate の命令形式だけを対象にする
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

            if (!TryGetSpawnInterval(instruction, out int turns))
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

            _status.Text = "PCSX2から現在値を読み込みました。";
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

            if (!TryGetSpawnInterval(before, out int oldTurns))
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

            uint newInstruction = 0x24020000u | (uint)turns;

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
}