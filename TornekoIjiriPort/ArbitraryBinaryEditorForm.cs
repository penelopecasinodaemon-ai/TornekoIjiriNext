using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class ArbitraryBinaryEditorForm : Form
{
    private const string PineHost = "127.0.0.1";
    private const int PinePort = 28011;
    private const int PageSize = 4096;

    private byte[]? _data;
    private byte[]? _original;
    private uint _pageStart;
    private bool _busy;

    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly TextBox _addressBox;
    private readonly Button _loadButton;
    private readonly Button _refreshButton;
    private readonly Button _previousButton;
    private readonly Button _nextButton;
    private readonly Button _writeButton;

    public ArbitraryBinaryEditorForm()
    {
        Text = "任意バイナリ編集 - PCSX2 PINE";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1200;
        Height = 780;
        MinimumSize = new Size(950, 600);
        Font = new Font("Yu Gothic UI", 9);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var firstLine = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true
        };

        _addressBox = new TextBox
        {
            Width = 145,
            Height = 28,
            Text = "2020B264",
            CharacterCasing = CharacterCasing.Upper
        };

        _loadButton = new Button
        {
            Text = "アドレスを読み込む",
            Width = 145,
            Height = 32
        };
        _loadButton.Click += async (_, _) => await LoadAddressAsync();

        _refreshButton = new Button
        {
            Text = "更新",
            Width = 75,
            Height = 32
        };
        _refreshButton.Click += async (_, _) =>
        {
            if (_data != null)
                await LoadPageAsync(_pageStart);
        };

        _previousButton = new Button
        {
            Text = "前のページ",
            Width = 95,
            Height = 32
        };
        _previousButton.Click += async (_, _) =>
        {
            if (_pageStart >= PageSize)
                await LoadPageAsync(_pageStart - PageSize);
        };

        _nextButton = new Button
        {
            Text = "次のページ",
            Width = 95,
            Height = 32
        };
        _nextButton.Click += async (_, _) =>
        {
            if (_pageStart <= uint.MaxValue - PageSize)
                await LoadPageAsync(_pageStart + PageSize);
        };

        _writeButton = new Button
        {
            Text = "変更をPCSX2へ書き込み",
            Width = 190,
            Height = 32,
            Enabled = false
        };
        _writeButton.Click += async (_, _) => await WriteChangesAsync();

        firstLine.Controls.Add(new Label
        {
            Text = "アドレス / 改造コード:",
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 0)
        });
        firstLine.Controls.Add(_addressBox);
        firstLine.Controls.Add(_loadButton);
        firstLine.Controls.Add(_refreshButton);
        firstLine.Controls.Add(_previousButton);
        firstLine.Controls.Add(_nextButton);
        firstLine.Controls.Add(_writeButton);

        var secondLine = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        secondLine.Controls.Add(new Label
        {
            Text = "例: 2020B264 → 0020B264（旧形式の基準アドレスを自動変換）",
            AutoSize = true,
            ForeColor = Color.DarkSlateGray,
            Padding = new Padding(2, 8, 0, 0)
        });

        toolbar.Controls.Add(firstLine, 0, 0);
        toolbar.Controls.Add(secondLine, 0, 1);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersWidth = 115,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 30,
            RowTemplate = { Height = 23 },
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
        };

        for (int i = 0; i < 16; i++)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Byte" + i.ToString("X"),
                HeaderText = "+" + i.ToString("X"),
                SortMode = DataGridViewColumnSortMode.NotSortable,
                MaxInputLength = 2
            });
        }

        _grid.CellValidating += ValidateCell;
        _grid.CellEndEdit += CommitCell;

        _status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            BorderStyle = BorderStyle.FixedSingle,
            Text = "PCSX2のゲームを起動し、PINEを有効にしてください。"
        };

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);

        UpdateNavigation();
    }

    private static bool TryParseAddress(
        string input, out uint address, out string explanation)
    {
        address = 0;
        explanation = "";

        string value = (input ?? "").Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(2);

        value = value.Replace(" ", "").Replace("_", "");

        if (!uint.TryParse(
            value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out uint parsed))
            return false;

        // 旧形式の改造コードアドレス:
        // 0x20000000 + EE RAM上のオフセット
        // 例: 0x2020B264 -> 0x0020B264
        if (parsed >= 0x20000000u && parsed <= 0x21FFFFFFu)
        {
            address = parsed - 0x20000000u;
            explanation =
                "旧形式 0x" + parsed.ToString("X8") +
                " を 0x" + address.ToString("X8") + " に変換";
            return true;
        }

        address = parsed;
        explanation = "指定アドレスをそのまま使用";
        return true;
    }

    private async Task LoadAddressAsync()
    {
        if (!TryParseAddress(
            _addressBox.Text, out uint address, out string explanation))
        {
            MessageBox.Show(
                this,
                "アドレスを8桁以内の16進数で入力してください。\r\n例: 2020B264",
                "アドレス入力エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        await LoadPageAsync(address, explanation);
    }

    private bool ConfirmDiscardChanges()
    {
        if (_grid.IsCurrentCellInEditMode)
            _grid.EndEdit();

        if (_data == null || _original == null)
            return true;

        int changed = CountChanges();

        if (changed == 0)
            return true;

        return MessageBox.Show(
            this,
            changed.ToString("N0") +
            "バイトの未書き込み変更があります。\r\n" +
            "変更を破棄して別のページを読み込みますか？",
            "未書き込み変更",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private async Task LoadPageAsync(
        uint requestedAddress, string? explanation = null)
    {
        if (_busy)
            return;

        if (!ConfirmDiscardChanges())
            return;

        uint pageStart = requestedAddress & 0xFFFFF000u;

        SetBusy(true);
        _status.Text =
            "PCSX2から読み込み中: 0x" + pageStart.ToString("X8");

        try
        {
            byte[] bytes = await Task.Run(
                () => PineClient.ReadMemory(pageStart, PageSize));

            _pageStart = pageStart;
            _data = bytes;
            _original = (byte[])bytes.Clone();

            DisplayPage();

            // 指定されたアドレスの行・列へ自動移動する
            uint targetOffset = requestedAddress - pageStart;
            int targetRow = (int)(targetOffset / 16);
            int targetCol = (int)(targetOffset % 16);

            _addressBox.Text = requestedAddress.ToString("X8");

            if (targetRow >= 0 &&
                targetRow < _grid.Rows.Count &&
                targetCol >= 0 &&
                targetCol < 16)
            {
                _grid.CurrentCell =
                    _grid.Rows[targetRow].Cells[targetCol];

                _grid.FirstDisplayedScrollingRowIndex =
                    Math.Max(0, targetRow - 5);
            }

            string detail = string.IsNullOrWhiteSpace(explanation)
                ? ""
                : " / " + explanation;

            _status.Text =
                "読み込み成功: 0x" + pageStart.ToString("X8") +
                " ～ 0x" + (pageStart + PageSize - 1).ToString("X8") +
                " / 4096 bytes" + detail;
        }
        catch (Exception ex)
        {
            _status.Text = "読み込み失敗";
            MessageBox.Show(
                this,
                ex.Message,
                "PINE読み込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateNavigation();
        }
    }

    private void DisplayPage()
    {
        _grid.Rows.Clear();

        if (_data == null)
            return;

        for (int rowOffset = 0; rowOffset < PageSize; rowOffset += 16)
        {
            int row = _grid.Rows.Add();

            _grid.Rows[row].HeaderCell.Value =
                (_pageStart + (uint)rowOffset).ToString("X8");

            for (int col = 0; col < 16; col++)
            {
                _grid.Rows[row].Cells[col].Value =
                    _data[rowOffset + col].ToString("X2");
            }
        }

        UpdateNavigation();
    }

    private void ValidateCell(
        object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_data == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        string value =
            Convert.ToString(e.FormattedValue)?.Trim() ?? "";

        if (value.Length < 1 || value.Length > 2 ||
            !byte.TryParse(
                value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out _))
        {
            e.Cancel = true;

            MessageBox.Show(
                this,
                "値は00～FFの16進数で入力してください。",
                "入力値エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void CommitCell(
        object? sender, DataGridViewCellEventArgs e)
    {
        if (_data == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        string value =
            Convert.ToString(
                _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";

        if (!byte.TryParse(
            value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out byte parsed))
            return;

        int index = e.RowIndex * 16 + e.ColumnIndex;

        if (index < 0 || index >= _data.Length)
            return;

        _data[index] = parsed;

        int changed = CountChanges();
        _writeButton.Enabled = changed > 0 && !_busy;

        _status.Text =
            "編集中: 0x" + (_pageStart + (uint)index).ToString("X8") +
            " = " + parsed.ToString("X2") +
            " / 未書き込み変更: " + changed.ToString("N0") + " bytes";
    }

    private int CountChanges()
    {
        if (_data == null || _original == null)
            return 0;

        int count = 0;

        for (int i = 0; i < _data.Length; i++)
        {
            if (_data[i] != _original[i])
                count++;
        }

        return count;
    }

    private async Task WriteChangesAsync()
    {
        if (_busy || _data == null || _original == null)
            return;

        if (_grid.IsCurrentCellInEditMode)
            _grid.EndEdit();

        int changed = CountChanges();

        if (changed == 0)
        {
            MessageBox.Show(this, "書き込む変更はありません。");
            return;
        }

        // 今回の編集機能ではEEメインRAMへの書き込みに限定する。
        // 0x00000000～0x01FFFFFF が対象。
        if (_pageStart > 0x01FFF000u)
        {
            MessageBox.Show(
                this,
                "書き込みはEEメインRAMの範囲に限定しています。\r\n" +
                "今回のページは読み込み専用です。",
                "書き込み対象外",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            "PCSX2のゲームメモリへ " +
            changed.ToString("N0") + " バイトを書き込みます。\r\n\r\n" +
            "開始アドレス: 0x" + _pageStart.ToString("X8") +
            "\r\n\r\nゲームの動作に影響する可能性があります。\r\n" +
            "書き込みを実行しますか？",
            "ゲームメモリ書き込み確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
            return;

        byte[] dataToWrite = (byte[])_data.Clone();
        byte[] originalToCompare = (byte[])_original.Clone();
        uint address = _pageStart;

        SetBusy(true);
        _status.Text = "PCSX2へ書き込み中...";

        try
        {
            await Task.Run(() =>
            {
                PineClient.WriteChangedBytes(
                    address, dataToWrite, originalToCompare);

                byte[] verify =
                    PineClient.ReadMemory(address, PageSize);

                for (int i = 0; i < PageSize; i++)
                {
                    if (verify[i] != dataToWrite[i])
                    {
                        throw new IOException(
                            "書き込み後の読み戻し値が一致しません。\r\n" +
                            "アドレス: 0x" +
                            (address + (uint)i).ToString("X8") +
                            "\r\n期待値: " + dataToWrite[i].ToString("X2") +
                            "\r\n実際の値: " + verify[i].ToString("X2"));
                    }
                }

                _data = verify;
                _original = (byte[])verify.Clone();
            });

            DisplayPage();
            _status.Text =
                "書き込み・読み戻し検証成功: " +
                changed.ToString("N0") + " bytes";
            MessageBox.Show(
                this,
                "PCSX2への書き込みと読み戻し検証が成功しました。",
                "書き込み成功",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _status.Text = "書き込みまたは検証に失敗しました。";
            MessageBox.Show(
                this,
                ex.Message,
                "PINE書き込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            // 書き込み結果が不明な場合も、現在のメモリを再取得する。
            try
            {
                byte[] latest = await Task.Run(
                    () => PineClient.ReadMemory(address, PageSize));
                _data = latest;
                _original = (byte[])latest.Clone();
                DisplayPage();
                _status.Text = "現在のメモリを再取得しました。";
            }
            catch
            {
                _status.Text = "再取得にも失敗しました。PCSX2の接続を確認してください。";
            }
        }
        finally
        {
            SetBusy(false);
            UpdateNavigation();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;

        _loadButton.Enabled = !busy;
        _refreshButton.Enabled = !busy && _data != null;
        _previousButton.Enabled = !busy && _pageStart >= PageSize;
        _nextButton.Enabled =
            !busy && _pageStart <= uint.MaxValue - PageSize;
        _addressBox.Enabled = !busy;
        _writeButton.Enabled =
            !busy && CountChanges() > 0;
    }

    private void UpdateNavigation()
    {
        _previousButton.Enabled =
            !_busy && _data != null && _pageStart >= PageSize;

        _nextButton.Enabled =
            !_busy && _data != null &&
            _pageStart <= uint.MaxValue - PageSize;

        _refreshButton.Enabled = !_busy && _data != null;
        _writeButton.Enabled = !_busy && CountChanges() > 0;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "通信処理が終わってから閉じてください。");
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private static class PineClient
    {
        private const byte MsgRead8 = 0;
        private const byte MsgWrite8 = 4;

        public static byte[] ReadMemory(uint address, int length)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            if ((ulong)address + (ulong)length > 0x100000000UL)
                throw new ArgumentOutOfRangeException(nameof(address));

            // Read8:
            // opcode 1 byte + address 4 bytes
            int commandLength = length * 5;
            byte[] request = new byte[4 + commandLength];

            BinaryPrimitives.WriteInt32LittleEndian(
                request.AsSpan(0, 4), request.Length);

            int offset = 4;

            for (int i = 0; i < length; i++)
            {
                request[offset] = MsgRead8;

                BinaryPrimitives.WriteUInt32LittleEndian(
                    request.AsSpan(offset + 1, 4),
                    address + (uint)i);

                offset += 5;
            }

            byte[] body = SendRequest(request, 5 + length);

            if (body[0] != 0)
                throw new IOException(
                    "PINEがメモリ読み込みを拒否しました。");

            byte[] result = new byte[length];
            Buffer.BlockCopy(body, 1, result, 0, length);

            return result;
        }

        public static void WriteChangedBytes(
            uint address, byte[] data, byte[] original)
        {
            if (data.Length != original.Length)
                throw new ArgumentException("比較データのサイズが一致しません。");

            var changed = new List<int>();

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != original[i])
                    changed.Add(i);
            }

            if (changed.Count == 0)
                return;

            // Write8:
            // opcode 1 byte + address 4 bytes + value 1 byte
            byte[] request = new byte[4 + changed.Count * 6];

            BinaryPrimitives.WriteInt32LittleEndian(
                request.AsSpan(0, 4), request.Length);

            int offset = 4;

            foreach (int index in changed)
            {
                request[offset] = MsgWrite8;

                BinaryPrimitives.WriteUInt32LittleEndian(
                    request.AsSpan(offset + 1, 4),
                    address + (uint)index);

                request[offset + 5] = data[index];
                offset += 6;
            }

            byte[] body = SendRequest(request, 5);

            if (body[0] != 0)
                throw new IOException(
                    "PINEがメモリ書き込みを拒否しました。");
        }

        private static byte[] SendRequest(
            byte[] request, int expectedResponseLength)
        {
            using var client = new TcpClient();

            client.ReceiveTimeout = 10000;
            client.SendTimeout = 10000;

            try
            {
                client.Connect(PineHost, PinePort);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "PCSX2のPINEへ接続できません。\r\n" +
                    "接続先: " + PineHost + ":" + PinePort +
                    "\r\nPCSX2でPINEが有効になっているか確認してください。",
                    ex);
            }

            using NetworkStream stream = client.GetStream();

            stream.Write(request, 0, request.Length);
            stream.Flush();

            byte[] header = ReadExact(stream, 4);

            int responseLength =
                BinaryPrimitives.ReadInt32LittleEndian(header);

            if (responseLength != expectedResponseLength)
            {
                throw new IOException(
                    "PINE応答サイズが一致しません。\r\n" +
                    "期待値: " + expectedResponseLength +
                    "\r\n実際: " + responseLength);
            }

            if (responseLength < 5)
                throw new IOException("PINE応答が短すぎます。");

            return ReadExact(stream, responseLength - 4);
        }

        private static byte[] ReadExact(
            NetworkStream stream, int length)
        {
            byte[] buffer = new byte[length];
            int received = 0;

            while (received < length)
            {
                int count = stream.Read(
                    buffer, received, length - received);

                if (count <= 0)
                    throw new EndOfStreamException(
                        "PINEからの応答が途中で切断されました。");

                received += count;
            }

            return buffer;
        }
    }
}
