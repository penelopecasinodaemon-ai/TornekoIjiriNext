using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class EquippedWeaponIdScannerForm : Form
{
    private const string PineHost = "127.0.0.1";
    private const int PinePort = 28011;

    private const uint AttackPatchAddress = 0x0020B264;
    private const uint AttackPatchValue = 0x24020001;

    private readonly uint[] _candidateAddresses =
    {
        0x006015D4,
        0x009276A4
    };

    private ListView _list = null!;
    private Label _status = null!;

    private bool _patchApplied;
    private uint _originalInstruction;

    public EquippedWeaponIdScannerForm()
    {
        Text = "装備中の武器ID・二回攻撃設定";
        StartPosition = FormStartPosition.CenterParent;
        Width = 900;
        Height = 560;
        MinimumSize = new Size(760, 440);
        Font = new Font("Yu Gothic UI", 10);

        BuildInterface();
        RefreshValues();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var explanation = new Label
        {
            Dock = DockStyle.Fill,
            Text =
                "PCSX2のPINE通信を利用して装備候補を読み取ります。\r\n" +
                "候補A・Bのどちらかに値があれば二回攻撃コードを適用します。",
            TextAlign = ContentAlignment.MiddleLeft
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        buttons.Controls.Add(MakeButton(
            "現在値を更新",
            150,
            (_, _) => RefreshValues()));

        buttons.Controls.Add(MakeButton(
            "2回攻撃を有効化",
            190,
            (_, _) => EnableDoubleAttack()));

        buttons.Controls.Add(MakeButton(
            "2回攻撃を解除",
            170,
            (_, _) => RestoreDoubleAttack()));

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };

        _list.Columns.Add("候補", 100);
        _list.Columns.Add("EE RAMアドレス", 180);
        _list.Columns.Add("16bit値", 140);
        _list.Columns.Add("32bit値", 160);
        _list.Columns.Add("判定", 180);

        _status = new Label
        {
            Dock = DockStyle.Fill,
            Text = "PCSX2の接続状態を確認してください。",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        root.Controls.Add(explanation, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(_status, 0, 3);

        Controls.Add(root);
    }

    private static Button MakeButton(
        string text,
        int width,
        EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 40,
            Margin = new Padding(4)
        };

        button.Click += click;
        return button;
    }

    private static void EnsureConnected()
    {
        if (!Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.ProcessId <= 0)
        {
            throw new InvalidOperationException(
                "先にメイン画面からPCSX2へ接続してください。");
        }
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
            {
                throw new IOException(
                    "PINE接続が途中で切断されました。");
            }

            offset += received;
        }

        return result;
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

    private static byte[] SendPineCommand(byte[] command)
    {
        byte[] request = new byte[4 + command.Length];

        WriteUInt32(request, 0, (uint)request.Length);
        Buffer.BlockCopy(command, 0, request, 4, command.Length);

        using var client = new TcpClient();
        client.ReceiveTimeout = 4000;
        client.SendTimeout = 4000;
        client.Connect(PineHost, PinePort);

        using NetworkStream stream = client.GetStream();

        stream.Write(request, 0, request.Length);
        stream.Flush();

        byte[] header = ReadExact(stream, 4);
        uint responseLength = ReadUInt32(header, 0);

        if (responseLength < 5 || responseLength > 1024)
        {
            throw new IOException(
                "PINE応答サイズが不正です: " + responseLength);
        }

        return ReadExact(stream, (int)responseLength - 4);
    }

    private static uint ReadPine32(uint address)
    {
        byte[] command = new byte[5];

        command[0] = 2;
        WriteUInt32(command, 1, address);

        byte[] response = SendPineCommand(command);

        if (response.Length != 5)
        {
            throw new IOException(
                "PINE Read32応答サイズが一致しません。実際: " +
                response.Length);
        }

        if (response[0] != 0)
        {
            throw new IOException(
                "PINE Read32が失敗しました。ステータス: " +
                response[0].ToString("X2"));
        }

        return ReadUInt32(response, 1);
    }

    private static void WritePine32(
        uint address,
        uint value)
    {
        byte[] command = new byte[9];

        command[0] = 6;
        WriteUInt32(command, 1, address);
        WriteUInt32(command, 5, value);

        byte[] response = SendPineCommand(command);

        if (response.Length != 1)
        {
            throw new IOException(
                "PINE Write32応答サイズが一致しません。実際: " +
                response.Length);
        }

        if (response[0] != 0)
        {
            throw new IOException(
                "PINE Write32が失敗しました。ステータス: " +
                response[0].ToString("X2"));
        }
    }

    private void RefreshValues()
    {
        try
        {
            EnsureConnected();

            _list.Items.Clear();

            bool anyValid = false;

            for (int i = 0; i < _candidateAddresses.Length; i++)
            {
                uint address = _candidateAddresses[i];

                uint value = ReadPine32(address);
                ushort value16 = (ushort)(value & 0xFFFF);

                bool valid = value16 != 0;

                if (valid)
                    anyValid = true;

                var item = new ListViewItem(
                    i == 0 ? "候補A" : "候補B");

                item.SubItems.Add(
                    "0x" + address.ToString("X8"));

                item.SubItems.Add(
                    "0x" + value16.ToString("X4"));

                item.SubItems.Add(
                    "0x" + value.ToString("X8"));

                item.SubItems.Add(
                    valid ? "値あり・適用判定対象" : "値が0");

                _list.Items.Add(item);
            }

            _status.Text = anyValid
                ? "候補A・Bの少なくとも一方に値があります。二回攻撃を試せます。"
                : "候補A・Bの下位16bitが両方とも0です。";
        }
        catch (Exception ex)
        {
            ShowError("現在値の取得に失敗しました", ex);
        }
    }

    private void EnableDoubleAttack()
    {
        if (_patchApplied)
        {
            MessageBox.Show(
                this,
                "この画面から二回攻撃コードは既に適用されています。",
                "適用済み",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        try
        {
            EnsureConnected();

            uint[] values = new uint[_candidateAddresses.Length];

            for (int i = 0; i < _candidateAddresses.Length; i++)
            {
                values[i] = ReadPine32(_candidateAddresses[i]);
            }

            bool candidateAValid =
                (values[0] & 0xFFFF) != 0;

            bool candidateBValid =
                (values[1] & 0xFFFF) != 0;

            if (!candidateAValid && !candidateBValid)
            {
                RefreshValues();

                MessageBox.Show(
                    this,
                    "候補A・Bのどちらも下位16bitが0でした。\r\n" +
                    "二回攻撃コードは適用していません。",
                    "適用条件を満たしていません",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            uint original = ReadPine32(AttackPatchAddress);

            if (original == AttackPatchValue)
            {
                MessageBox.Show(
                    this,
                    "対象アドレスには既に二回攻撃コードが存在します。\r\n" +
                    "元の命令を安全に保存できないため、今回は書き込みません。",
                    "既に適用されています",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            if (MessageBox.Show(
                this,
                "二回攻撃コードを適用します。\r\n\r\n" +
                "適用先: EE RAM + 0x0020B264\r\n" +
                "適用値: 0x24020001\r\n" +
                "元の命令: 0x" + original.ToString("X8") +
                "\r\n\r\nPCSX2のPINE通信で書き込みますか？",
                "二回攻撃の有効化",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            WritePine32(AttackPatchAddress, AttackPatchValue);

            uint verification = ReadPine32(AttackPatchAddress);

            if (verification != AttackPatchValue)
            {
                uint current = ReadPine32(AttackPatchAddress);

                if (current == AttackPatchValue)
                {
                    WritePine32(AttackPatchAddress, original);
                }

                throw new IOException(
                    "書き込み後の読み戻し値が一致しません。\r\n" +
                    "期待値: 0x" + AttackPatchValue.ToString("X8") +
                    "\r\n実際値: 0x" + verification.ToString("X8"));
            }

            _originalInstruction = original;
            _patchApplied = true;

            string selected =
                candidateAValid && candidateBValid
                    ? "候補A・Bの両方"
                    : candidateAValid
                        ? "候補A"
                        : "候補B";

            _status.Text =
                selected + "を検出。PINE経由の書き込み検証に成功しました。";

            MessageBox.Show(
                this,
                "二回攻撃コードの書き込みと読み戻し検証に成功しました。\r\n\r\n" +
                "判定対象: " + selected + "\r\n" +
                "適用先: 0x0020B264\r\n" +
                "適用値: 0x24020001\r\n" +
                "保存した元の命令: 0x" +
                original.ToString("X8") +
                "\r\n\r\nゲーム内で動作を確認してください。",
                "二回攻撃コード適用成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("二回攻撃の有効化に失敗しました", ex);
        }
    }

    private void RestoreDoubleAttack()
    {
        if (!_patchApplied)
        {
            MessageBox.Show(
                this,
                "この画面から適用した二回攻撃コードはありません。",
                "未適用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        try
        {
            EnsureConnected();

            uint current = ReadPine32(AttackPatchAddress);

            if (current != AttackPatchValue)
            {
                throw new InvalidOperationException(
                    "適用先の命令が変更されています。\r\n" +
                    "安全のため復元を中止しました。\r\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            WritePine32(
                AttackPatchAddress,
                _originalInstruction);

            uint verification = ReadPine32(AttackPatchAddress);

            if (verification != _originalInstruction)
            {
                throw new IOException(
                    "復元後の読み戻し値が一致しません。\r\n" +
                    "期待値: 0x" + _originalInstruction.ToString("X8") +
                    "\r\n実際値: 0x" + verification.ToString("X8"));
            }

            _patchApplied = false;

            _status.Text =
                "元の命令へ復元しました: 0x" +
                _originalInstruction.ToString("X8");

            MessageBox.Show(
                this,
                "元の命令へ復元しました。\r\n" +
                "復元値: 0x" +
                _originalInstruction.ToString("X8"),
                "二回攻撃を解除しました",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("二回攻撃の解除に失敗しました", ex);
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _status.Text = title + ": " + ex.Message;

        MessageBox.Show(
            this,
            ex.ToString(),
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
