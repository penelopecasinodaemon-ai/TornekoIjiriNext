
using System.Globalization;

namespace TornekoIjiriNext;

/// <summary>
/// Generic offline binary editor for legacy/unknown assets. It never connects to PINE.
/// Editing is page-based so large assets remain usable while untouched bytes are preserved.
/// </summary>
internal sealed class LegacyBinaryEditorForm : Form
{
    private const int BytesPerPage = 4096;
    private const int BytesPerRow = 16;
    private const int MaximumAssetLength = 64 * 1024 * 1024;

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersWidth = 105,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.CellSelect,
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText
    };

    private readonly NumericUpDown _page = new()
    {
        Minimum = 0,
        Maximum = 0,
        Width = 110,
        ThousandsSeparator = true
    };

    private readonly Label _info = new()
    {
        AutoSize = true,
        Padding = new Padding(8, 7, 4, 0)
    };

    private readonly byte[] _data;
    private readonly string? _sourcePath;

    // 初回保存前はnull。元ファイルを直接上書きしない。
    private string? _currentPath;

    private readonly Func<byte[], string>? _saveToBundle;

    // Save As後にファイル名を更新するためreadonlyにしない。
    private string _assetName;

    private bool _loading;
    private Button? _saveButton;

    // スパース差分管理
    private readonly Dictionary<int, byte> _savedValues = new();
    private readonly Dictionary<int, byte> _lastSavedBytes = new();

    private bool IsDirty => _savedValues.Count > 0;

    // 1バイト単位の編集履歴
    private readonly record struct ByteEditAction(
        int Offset,
        byte OldValue,
        byte NewValue);

    private readonly Stack<ByteEditAction> _undoStack = new();
    private readonly Stack<ByteEditAction> _redoStack = new();

    private int _lastSearchIndex = -1;

    // ジャンプ・検索・差分一覧UI
    private readonly TextBox _offsetInput = new()
    {
        Width = 100,
        Text = "0"
    };

    private readonly RadioButton _rbHexOffset = new()
    {
        Text = "16進数",
        Checked = true,
        AutoSize = true
    };

    private readonly RadioButton _rbDecOffset = new()
    {
        Text = "10進数",
        Checked = false,
        AutoSize = true
    };

    private readonly TextBox _searchInput = new()
    {
        Width = 130
    };

    private readonly ListBox _diffList = new()
    {
        Height = 90,
        Dock = DockStyle.Fill,
        Visible = false
    };

    private readonly Button _toggleDiffButton = new()
    {
        Text = "差分一覧 (0)",
        AutoSize = true
    };

    private TableLayoutPanel? _rootLayout;

    public LegacyBinaryEditorForm(
        string path,
        Func<byte[], string>? saveToBundle = null)
    {
        _sourcePath = Path.GetFullPath(path);

        // 初回保存時はSaveFileDialogを表示する。
        _currentPath = null;

        _assetName = Path.GetFileName(path);
        _saveToBundle = saveToBundle;

        var info = new FileInfo(_sourcePath);

        if (!info.Exists || info.Length > MaximumAssetLength)
        {
            throw new InvalidDataException(
                "ファイルが存在しないか、64 MiBを超えています。");
        }

        _data = File.ReadAllBytes(_sourcePath);

        InitializeUi();
    }

    public LegacyBinaryEditorForm(
        byte[] bytes,
        string displayName = "未保存データ")
    {
        if (bytes.Length > MaximumAssetLength)
        {
            throw new InvalidDataException(
                "データは64 MiB以下にしてください。");
        }

        _data = bytes.ToArray();
        _assetName = displayName;

        InitializeUi();
    }

    private void InitializeUi()
    {
        Text = $"汎用バイナリ編集（オフライン） - {_assetName}";

        Width = 1120;
        Height = 760;
        MinimumSize = new Size(800, 540);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Offset",
            HeaderText = "Offset",
            ReadOnly = true,
            FillWeight = 145,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        for (var column = 0; column < BytesPerRow; column++)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = $"B{column:X1}",
                HeaderText = column.ToString("X2"),
                FillWeight = 50,
                MaxInputLength = 2,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        _grid.CellValidating += ValidateCell;
        _grid.CellValueChanged += CellChanged;

        _grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            _info.Text =
                "入力値が不正です。00〜FFの16進数2桁で入力してください。";
        };

        var previous = new Button
        {
            Text = "前のページ",
            AutoSize = true
        };

        var next = new Button
        {
            Text = "次のページ",
            AutoSize = true
        };

        _saveButton = new Button
        {
            Text = _saveToBundle is null
                ? "名前を付けて保存…"
                : "編集版をプロジェクトへ保存",
            AutoSize = true
        };

        previous.Click += (_, _) =>
        {
            if (CommitEdit() && _page.Value > 0)
                _page.Value--;
        };

        next.Click += (_, _) =>
        {
            if (CommitEdit() && _page.Value < _page.Maximum)
                _page.Value++;
        };

        _page.ValueChanged += (_, _) => LoadPage();
        _saveButton.Click += (_, _) => Save();

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };

        top.Controls.Add(previous);

        top.Controls.Add(new Label
        {
            Text = "ページ (1 page = 4096 bytes)",
            AutoSize = true,
            Padding = new Padding(6, 7, 0, 0)
        });

        top.Controls.Add(_page);
        top.Controls.Add(next);
        top.Controls.Add(_saveButton);
        top.Controls.Add(_info);

        var subBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 2, 0, 4)
        };

        subBar.Controls.Add(new Label
        {
            Text = "オフセット:",
            AutoSize = true,
            Padding = new Padding(2, 6, 0, 0)
        });

        subBar.Controls.Add(_offsetInput);
        subBar.Controls.Add(_rbHexOffset);
        subBar.Controls.Add(_rbDecOffset);

        var jumpButton = new Button
        {
            Text = "ジャンプ",
            AutoSize = true
        };

        jumpButton.Click += (_, _) => JumpToOffset();
        subBar.Controls.Add(jumpButton);

        subBar.Controls.Add(new Label
        {
            Text = "  HEX検索:",
            AutoSize = true,
            Padding = new Padding(6, 6, 0, 0)
        });

        _searchInput.TextChanged += (_, _) =>
        {
            _lastSearchIndex = -1;
        };

        subBar.Controls.Add(_searchInput);

        var searchButton = new Button
        {
            Text = "次を検索",
            AutoSize = true
        };

        searchButton.Click += (_, _) => SearchHexBytes();
        subBar.Controls.Add(searchButton);

        _toggleDiffButton.Click += (_, _) => ToggleDiffPanel();
        subBar.Controls.Add(_toggleDiffButton);

        _diffList.DoubleClick += (_, _) =>
        {
            if (_diffList.SelectedItem is DiffEntry entry)
                JumpToAbsoluteOffset(entry.Offset);
        };

        var note = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(1000, 0),
            ForeColor = Color.DarkSlateBlue,
            Text =
                "ローカルファイルだけを編集します。未編集ページ・未変更byteは保持されます（Ctrl+Z: 元に戻す / Ctrl+Y: やり直し）。ここでの値は意味未判定のraw dataであり、ゲームへの書き込みは行いません。"
        };

        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 5
        };

        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _rootLayout.Controls.Add(top, 0, 0);
        _rootLayout.Controls.Add(subBar, 0, 1);
        _rootLayout.Controls.Add(_grid, 0, 2);
        _rootLayout.Controls.Add(_diffList, 0, 3);
        _rootLayout.Controls.Add(note, 0, 4);

        Controls.Add(_rootLayout);

        _page.Maximum = Math.Max(
            0,
            (_data.Length - 1) / BytesPerPage);

        LoadPage();
    }

    protected override bool ProcessCmdKey(
        ref Message msg,
        Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            Undo();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Y))
        {
            Redo();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Undo()
    {
        if (!CommitEdit())
            return;

        if (_undoStack.Count == 0)
        {
            _info.Text = "元に戻す操作はありません。";
            return;
        }

        var action = _undoStack.Pop();

        _redoStack.Push(action);

        ApplyByteInternal(
            action.Offset,
            action.OldValue);

        JumpToAbsoluteOffset(action.Offset);
        LoadPage();

        _info.Text =
            $"Undo: offset 0x{action.Offset:X8} = {action.OldValue:X2}";
    }

    private void Redo()
    {
        if (!CommitEdit())
            return;

        if (_redoStack.Count == 0)
        {
            _info.Text = "やり直す操作はありません。";
            return;
        }

        var action = _redoStack.Pop();

        _undoStack.Push(action);

        ApplyByteInternal(
            action.Offset,
            action.NewValue);

        JumpToAbsoluteOffset(action.Offset);
        LoadPage();

        _info.Text =
            $"Redo: offset 0x{action.Offset:X8} = {action.NewValue:X2}";
    }

    private void ApplyByteInternal(
        int offset,
        byte newValue)
    {
        if (!_savedValues.ContainsKey(offset))
        {
            byte baseline = _lastSavedBytes.TryGetValue(
                offset,
                out var savedValue)
                    ? savedValue
                    : _data[offset];

            _savedValues[offset] = baseline;
        }

        _data[offset] = newValue;

        if (_savedValues.TryGetValue(offset, out var original) &&
            original == newValue)
        {
            _savedValues.Remove(offset);
        }

        UpdateDiffState();
    }

    private void UpdateDiffState()
    {
        _toggleDiffButton.Text =
            $"差分一覧 ({_savedValues.Count})";

        Text =
            $"汎用バイナリ編集（オフライン） - {_assetName}" +
            (IsDirty ? " *" : "");

        if (_diffList.Visible)
            RefreshDiffList();
    }

    private void ToggleDiffPanel()
    {
        _diffList.Visible = !_diffList.Visible;

        if (_diffList.Visible)
            RefreshDiffList();
    }

    private sealed record DiffEntry(
        int Offset,
        byte Original,
        byte Current)
    {
        public override string ToString() =>
            $"0x{Offset:X8} : {Original:X2} -> {Current:X2}";
    }

    private void RefreshDiffList()
    {
        _diffList.BeginUpdate();

        try
        {
            _diffList.Items.Clear();

            var entries = _savedValues
                .OrderBy(pair => pair.Key)
                .Select(pair =>
                    new DiffEntry(
                        pair.Key,
                        pair.Value,
                        _data[pair.Key]))
                .ToArray();

            _diffList.Items.AddRange(entries);
        }
        finally
        {
            _diffList.EndUpdate();
        }
    }

    private void JumpToOffset()
    {
        if (!CommitEdit())
            return;

        var text = _offsetInput.Text.Trim();

        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            int offset;

            if (_rbHexOffset.Checked)
            {
                if (text.StartsWith(
                    "0x",
                    StringComparison.OrdinalIgnoreCase))
                {
                    text = text[2..];
                }

                offset = int.Parse(
                    text,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture);
            }
            else
            {
                offset = int.Parse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture);
            }

            if (offset < 0 || offset >= _data.Length)
            {
                MessageBox.Show(
                    this,
                    $"オフセットが範囲外です (0 〜 0x{_data.Length - 1:X})。",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            JumpToAbsoluteOffset(offset);

            _info.Text =
                $"ジャンプ完了: offset 0x{offset:X8} ({offset})";
        }
        catch (FormatException)
        {
            MessageBox.Show(
                this,
                "オフセットの入力形式が不正です。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (OverflowException)
        {
            MessageBox.Show(
                this,
                "オフセットの値が大きすぎます。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void JumpToAbsoluteOffset(int offset)
    {
        if (offset < 0 || offset >= _data.Length)
            return;

        if (!CommitEdit())
            return;

        int targetPage = offset / BytesPerPage;

        if (targetPage != decimal.ToInt32(_page.Value))
            _page.Value = targetPage;

        int rowIndex =
            (offset % BytesPerPage) / BytesPerRow;

        int columnIndex =
            (offset % BytesPerRow) + 1;

        if (rowIndex < _grid.Rows.Count &&
            columnIndex < _grid.Columns.Count)
        {
            _grid.ClearSelection();

            _grid.Rows[rowIndex]
                .Cells[columnIndex]
                .Selected = true;

            _grid.CurrentCell =
                _grid.Rows[rowIndex].Cells[columnIndex];
        }
    }

    private void SearchHexBytes()
    {
        if (!CommitEdit())
            return;

        var cleaned = _searchInput.Text
            .Trim()
            .Replace(" ", "")
            .Replace("-", "");

        if (cleaned.Length == 0 ||
            cleaned.Length % 2 != 0)
        {
            MessageBox.Show(
                this,
                "HEXバイト列は偶数桁の16進数で入力してください（例: 01 FF A0）。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        byte[] pattern;

        try
        {
            pattern = Convert.FromHexString(cleaned);
        }
        catch (FormatException)
        {
            MessageBox.Show(
                this,
                "16進数以外の文字が含まれています。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        if (pattern.Length > _data.Length)
        {
            MessageBox.Show(
                this,
                "検索するバイト列がファイルサイズを超えています。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        int start = _lastSearchIndex + 1;

        if (start < 0 || start >= _data.Length)
            start = 0;

        int found = FindPattern(pattern, start);

        if (found == -1 && start > 0)
            found = FindPattern(pattern, 0);

        if (found != -1)
        {
            _lastSearchIndex = found;

            JumpToAbsoluteOffset(found);

            _info.Text =
                $"一致箇所へ移動: offset 0x{found:X8}";
        }
        else
        {
            MessageBox.Show(
                this,
                "指定されたバイト列は見つかりませんでした。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private int FindPattern(
        byte[] pattern,
        int startIndex)
    {
        if (startIndex < 0 ||
            startIndex > _data.Length)
        {
            return -1;
        }

        var span = _data.AsSpan();

        int index =
            span[startIndex..].IndexOf(pattern);

        return index >= 0
            ? startIndex + index
            : -1;
    }

    private void LoadPage()
    {
        _loading = true;

        try
        {
            _grid.Rows.Clear();

            int start =
                decimal.ToInt32(_page.Value) * BytesPerPage;

            int end =
                Math.Min(_data.Length, start + BytesPerPage);

            for (int offset = start;
                offset < end;
                offset += BytesPerRow)
            {
                var row = new object[BytesPerRow + 1];

                row[0] = $"0x{offset:X8}";

                for (int column = 0;
                    column < BytesPerRow;
                    column++)
                {
                    row[column + 1] =
                        offset + column < end
                            ? _data[offset + column].ToString("X2")
                            : "";
                }

                _grid.Rows.Add(row);
            }

            _info.Text =
                $"サイズ: {_data.Length:N0} bytes / 表示: 0x{start:X8}〜0x{Math.Max(start, end - 1):X8}" +
                (IsDirty ? " / 未保存の変更あり" : "");
        }
        finally
        {
            _loading = false;
        }
    }

    private void ValidateCell(
        object? sender,
        DataGridViewCellValidatingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex == 0)
            return;

        var value =
            Convert.ToString(e.FormattedValue)?.Trim() ?? "";

        int absoluteOffset =
            GetAbsoluteOffset(e.RowIndex, e.ColumnIndex - 1);

        if (absoluteOffset >= _data.Length &&
            value.Length == 0)
        {
            return;
        }

        if (value.Length != 2 ||
            !byte.TryParse(
                value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out _))
        {
            e.Cancel = true;

            _info.Text =
                "入力値が不正です。00〜FFの16進数2桁で入力してください。";
        }
    }

    private void CellChanged(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (_loading ||
            e.RowIndex < 0 ||
            e.ColumnIndex <= 0)
        {
            return;
        }

        int offset =
            GetAbsoluteOffset(e.RowIndex, e.ColumnIndex - 1);

        if (offset >= _data.Length)
            return;

        var text =
            Convert.ToString(
                _grid.Rows[e.RowIndex]
                    .Cells[e.ColumnIndex].Value)?.Trim() ?? "";

        if (!byte.TryParse(
            text,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var value))
        {
            return;
        }

        byte oldValue = _data[offset];

        if (oldValue == value)
            return;

        _undoStack.Push(
            new ByteEditAction(offset, oldValue, value));

        _redoStack.Clear();

        ApplyByteInternal(offset, value);

        _info.Text =
            $"変更あり: offset 0x{offset:X8} = {value:X2} / {_data.Length:N0} bytes";
    }

    private int GetAbsoluteOffset(
        int row,
        int byteColumn) =>
        decimal.ToInt32(_page.Value) * BytesPerPage +
        row * BytesPerRow +
        byteColumn;

    private bool CommitEdit()
    {
        if (_grid.IsCurrentCellInEditMode &&
            !_grid.EndEdit())
        {
            return false;
        }

        return Validate();
    }

    private void Save()
    {
        try
        {
            if (!CommitEdit())
                return;

            // プロジェクト保存経路
            if (_saveToBundle is not null)
            {
                if (!IsDirty)
                {
                    _info.Text =
                        "変更がないため、保存をスキップしました。";

                    return;
                }

                string savedName = _saveToBundle(_data);

                foreach (var pair in _savedValues)
                    _lastSavedBytes[pair.Key] = _data[pair.Key];

                _savedValues.Clear();

                UpdateDiffState();

                _info.Text =
                    $"プロジェクトへ保存しました: {savedName}";

                return;
            }

            // 初回保存前は必ず保存先を選択する。
            string? targetPath = _currentPath;

            if (targetPath is null)
            {
                string suggestedFileName =
                    _sourcePath is not null
                        ? Path.GetFileNameWithoutExtension(_sourcePath) +
                          "_edited" +
                          Path.GetExtension(_sourcePath)
                        : Path.GetFileNameWithoutExtension(_assetName) +
                          "_edited" +
                          Path.GetExtension(_assetName);

                using var dialog = new SaveFileDialog
                {
                    Filter =
                        "Binary (*.bin)|*.bin|すべてのファイル (*.*)|*.*",

                    InitialDirectory =
                        _sourcePath is not null
                            ? Path.GetDirectoryName(_sourcePath)
                            : Environment.CurrentDirectory,

                    FileName = suggestedFileName,
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    _info.Text = "保存をキャンセルしました。";
                    return;
                }

                targetPath =
                    Path.GetFullPath(dialog.FileName);
            }

            // 変更がない場合の不要な置換・バックアップを避ける。
            if (!IsDirty &&
                _currentPath is not null &&
                File.Exists(_currentPath))
            {
                _info.Text =
                    "変更がないため、保存をスキップしました。";

                return;
            }

            bool targetAlreadyExists =
                File.Exists(targetPath);

            SaveSafelyWithBackup(
                targetPath,
                _data);

            // 保存成功後にだけ保存先と表示名を変更する。
            _currentPath = targetPath;
            _assetName = Path.GetFileName(targetPath);

            foreach (var pair in _savedValues)
                _lastSavedBytes[pair.Key] = _data[pair.Key];

            _savedValues.Clear();

            UpdateDiffState();

            if (_saveButton is not null)
                _saveButton.Text = "保存";

            _info.Text = targetAlreadyExists
                ? $"保存しました: {targetPath}（既存ファイルのバックアップ作成済み）"
                : $"保存しました: {targetPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "保存失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void SaveSafelyWithBackup(
        string targetPath,
        byte[] data)
    {
        string fullTargetPath =
            Path.GetFullPath(targetPath);

        string dir =
            Path.GetDirectoryName(fullTargetPath) ??
            Environment.CurrentDirectory;

        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                $"保存先フォルダーが存在しません: {dir}");
        }

        string tempPath = Path.Combine(
            dir,
            $".{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

        string backupPath =
            $"{fullTargetPath}.{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.bak";

        try
        {
            using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullTargetPath))
            {
                File.Replace(
                    tempPath,
                    fullTargetPath,
                    backupPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(
                    tempPath,
                    fullTargetPath);
            }
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // 元の保存エラーを維持する。
                }
            }

            throw;
        }
    }

    protected override void OnFormClosing(
        FormClosingEventArgs e)
    {
        if (!CommitEdit())
        {
            e.Cancel = true;
            return;
        }

        if (IsDirty &&
            MessageBox.Show(
                this,
                "未保存の編集があります。閉じてもよろしいですか？",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }
}
