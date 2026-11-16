
using System;
using System.IO;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class ArbitraryBinaryEditorForm : Form
{
    private const int PageSize = 4096;

    private byte[]? _data;
    private string? _path;
    private long _pageStart;

    private readonly DataGridView _grid;
    private readonly Label _status;
    private readonly TextBox _offsetBox;
    private readonly Button _previousButton;
    private readonly Button _nextButton;

    public ArbitraryBinaryEditorForm()
    {
        Text = "任意バイナリ編集";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1050;
        Height = 720;
        MinimumSize = new Size(850, 550);
        Font = new Font("Yu Gothic UI", 9);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        var openButton = new Button { Text = "ファイルを開く", Width = 115, Height = 32 };
        openButton.Click += (_, _) => OpenFile();

        var saveButton = new Button { Text = "上書き保存", Width = 105, Height = 32 };
        saveButton.Click += (_, _) => SaveFile();

        var saveAsButton = new Button { Text = "別名保存", Width = 95, Height = 32 };
        saveAsButton.Click += (_, _) => SaveFileAs();

        _previousButton = new Button { Text = "前のページ", Width = 95, Height = 32 };
        _previousButton.Click += (_, _) => ChangePage(-PageSize);

        _nextButton = new Button { Text = "次のページ", Width = 95, Height = 32 };
        _nextButton.Click += (_, _) => ChangePage(PageSize);

        _offsetBox = new TextBox
        {
            Width = 110,
            Height = 28,
            Text = "0"
        };

        var jumpButton = new Button { Text = "オフセットへ移動", Width = 125, Height = 32 };
        jumpButton.Click += (_, _) => JumpToOffset();

        toolbar.Controls.Add(openButton);
        toolbar.Controls.Add(saveButton);
        toolbar.Controls.Add(saveAsButton);
        toolbar.Controls.Add(_previousButton);
        toolbar.Controls.Add(_nextButton);
        toolbar.Controls.Add(new Label
        {
            Text = "移動先 (16進数):",
            AutoSize = true,
            Padding = new Padding(5, 8, 0, 0)
        });
        toolbar.Controls.Add(_offsetBox);
        toolbar.Controls.Add(jumpButton);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersWidth = 115,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 30,
            RowTemplate = { Height = 24 },
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
            Text = "バイナリファイル未読み込み"
        };

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_status, 0, 2);
        Controls.Add(root);

        UpdateNavigation();
    }

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "編集するバイナリファイルを選択",
            Filter = "すべてのファイル (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            byte[] loaded = File.ReadAllBytes(dialog.FileName);

            _data = loaded;
            _path = dialog.FileName;
            _pageStart = 0;

            DisplayPage();

            _status.Text =
                Path.GetFileName(_path) + " / " +
                _data.Length.ToString("N0") + " bytes";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "読み込みエラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DisplayPage()
    {
        _grid.Rows.Clear();

        if (_data == null)
        {
            UpdateNavigation();
            return;
        }

        long end = Math.Min(_pageStart + PageSize, _data.Length);

        for (long rowOffset = _pageStart; rowOffset < end; rowOffset += 16)
        {
            int row = _grid.Rows.Add();
            _grid.Rows[row].HeaderCell.Value =
                rowOffset.ToString("X8", CultureInfo.InvariantCulture);

            for (int col = 0; col < 16; col++)
            {
                long absolute = rowOffset + col;
                if (absolute >= end)
                    break;

                _grid.Rows[row].Cells[col].Value =
                    _data[(int)absolute].ToString("X2");
            }
        }

        _status.Text =
            "開始: 0x" + _pageStart.ToString("X8") +
            " / 全体: " + _data.Length.ToString("N0") + " bytes" +
            " / ページ: " + PageSize.ToString("N0") + " bytes";

        UpdateNavigation();
    }

    private void ValidateCell(object? sender, DataGridViewCellValidatingEventArgs e)
    {
        if (_data == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        string value = Convert.ToString(e.FormattedValue)?.Trim() ?? "";

        if (value.Length < 1 || value.Length > 2 ||
            !byte.TryParse(value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out _))
        {
            e.Cancel = true;
            MessageBox.Show(this,
                "値は00～FFの16進数で入力してください。",
                "入力値エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void CommitCell(object? sender, DataGridViewCellEventArgs e)
    {
        if (_data == null || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        string value = Convert.ToString(
            _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";

        if (!byte.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out byte parsed))
            return;

        long absolute = _pageStart + e.RowIndex * 16L + e.ColumnIndex;

        if (absolute < 0 || absolute >= _data.Length)
            return;

        _data[(int)absolute] = parsed;
        _status.Text = "編集済み: 0x" +
            absolute.ToString("X8") + " = " + parsed.ToString("X2");
    }

    private void ChangePage(long amount)
    {
        if (_data == null)
            return;

        if (_grid.IsCurrentCellInEditMode)
            _grid.EndEdit();

        long next = _pageStart + amount;

        if (next < 0)
            next = 0;

        if (next >= _data.Length)
            next = Math.Max(0, ((_data.Length - 1) / PageSize) * PageSize);

        _pageStart = (next / PageSize) * PageSize;
        DisplayPage();
    }

    private void JumpToOffset()
    {
        if (_data == null)
            return;

        string value = _offsetBox.Text.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            value = value.Substring(2);

        if (!long.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out long offset) ||
            offset < 0 || offset >= _data.Length)
        {
            MessageBox.Show(this, "有効な16進数オフセットを入力してください。");
            return;
        }

        if (_grid.IsCurrentCellInEditMode)
            _grid.EndEdit();

        _pageStart = (offset / PageSize) * PageSize;
        DisplayPage();

        int row = (int)((offset - _pageStart) / 16);
        int col = (int)((offset - _pageStart) % 16);

        if (row < _grid.Rows.Count)
        {
            _grid.CurrentCell = _grid.Rows[row].Cells[col];
            _grid.FirstDisplayedScrollingRowIndex = row;
        }
    }

    private void UpdateNavigation()
    {
        _previousButton.Enabled = _data != null && _pageStart > 0;
        _nextButton.Enabled = _data != null &&
            _pageStart + PageSize < _data.Length;
    }

    private void SaveFile()
    {
        if (_data == null || string.IsNullOrWhiteSpace(_path))
        {
            MessageBox.Show(this, "先にファイルを開いてください。");
            return;
        }

        if (_grid.IsCurrentCellInEditMode)
            _grid.EndEdit();

        if (MessageBox.Show(this,
            "元ファイルをバックアップしてから上書き保存します。\r\n続行しますか？",
            "上書き保存確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            WriteSafely(_path, _data, true);
            _status.Text = "保存完了: " + _path;
            MessageBox.Show(this, "保存と読み戻し検証が完了しました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "保存エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveFileAs()
    {
        if (_data == null)
        {
            MessageBox.Show(this, "先にファイルを開いてください。");
            return;
        }

        if (_grid.IsCurrentCellInEditMode)
            _grid.EndEdit();

        using var dialog = new SaveFileDialog
        {
            Title = "バイナリファイルを別名保存",
            Filter = "すべてのファイル (*.*)|*.*",
            FileName = _path == null ? "edited.bin" : Path.GetFileName(_path),
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            WriteSafely(dialog.FileName, _data, true);
            _path = dialog.FileName;
            _status.Text = "保存完了: " + _path;
            MessageBox.Show(this, "別名保存と読み戻し検証が完了しました。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.ToString(), "保存エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void WriteSafely(string path, byte[] data, bool backup)
    {
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("保存先フォルダが不明です。");

        Directory.CreateDirectory(directory);

        string tempPath = fullPath + "." +
            Guid.NewGuid().ToString("N") + ".tmp";

        string? backupPath = null;

        try
        {
            if (File.Exists(fullPath) && backup)
            {
                backupPath = fullPath + ".backup_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                File.Copy(fullPath, backupPath, false);
            }

            File.WriteAllBytes(tempPath, data);

            byte[] verify = File.ReadAllBytes(tempPath);
            if (verify.Length != data.Length)
                throw new IOException("一時保存ファイルのサイズが一致しません。");

            for (int i = 0; i < data.Length; i++)
            {
                if (verify[i] != data[i])
                    throw new IOException("一時保存ファイルの内容が一致しません。位置: " + i);
            }

            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, null);
            else
                File.Move(tempPath, fullPath);

            byte[] finalData = File.ReadAllBytes(fullPath);
            if (finalData.Length != data.Length)
                throw new IOException("保存後のサイズが一致しません。");

            for (int i = 0; i < data.Length; i++)
            {
                if (finalData[i] != data[i])
                    throw new IOException("保存後の内容が一致しません。位置: " + i);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
