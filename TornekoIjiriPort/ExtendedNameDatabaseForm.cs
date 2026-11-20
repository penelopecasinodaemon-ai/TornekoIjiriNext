using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class ExtendedNameDatabaseEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Encoding { get; set; } = "CP932";

    public override string ToString()
    {
        return Id + "  →  " + Name;
    }
}

internal sealed class ExtendedNameDatabaseForm : Form
{
    private static readonly Encoding Cp932 = CreateCp932();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _databasePath;
    private readonly List<ExtendedNameDatabaseEntry> _entries = new();
    private readonly ListBox _list = new();
    private readonly TextBox _idBox = new();
    private readonly TextBox _nameBox = new();
    private readonly Label _status = new();

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            932,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    public ExtendedNameDatabaseForm()
    {
        Text = "拡張名前データベース（試験版）";
        StartPosition = FormStartPosition.CenterParent;
        Width = 820;
        Height = 570;
        MinimumSize = new Size(720, 480);
        Font = new Font("Yu Gothic UI", 10);

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TornekoIjiriNext");
        _databasePath = Path.Combine(directory, "ExtendedNameDatabase.json");

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        _list.Dock = DockStyle.Fill;
        _list.Font = new Font("Yu Gothic UI", 10);
        _list.SelectedIndexChanged += (_, _) => LoadSelectedEntry();

        var editPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(14, 0, 0, 0)
        };
        editPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        editPanel.Controls.Add(new Label
        {
            Text = "リソースID",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _idBox.Dock = DockStyle.Fill;
        _idBox.PlaceholderText = "例: item_164";
        editPanel.Controls.Add(_idBox, 0, 1);

        editPanel.Controls.Add(new Label
        {
            Text = "拡張名（文字数制限なし）",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 2);

        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Multiline = true;
        _nameBox.ScrollBars = ScrollBars.Vertical;
        _nameBox.Font = new Font("Yu Gothic UI", 12);
        editPanel.Controls.Add(_nameBox, 0, 3);

        var editButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var saveButton = new Button
        {
            Text = "追加・更新",
            Width = 110,
            Height = 34
        };
        saveButton.Click += (_, _) => AddOrUpdateEntry();

        var deleteButton = new Button
        {
            Text = "選択項目を削除",
            Width = 130,
            Height = 34
        };
        deleteButton.Click += (_, _) => DeleteSelectedEntry();

        editButtons.Controls.Add(saveButton);
        editButtons.Controls.Add(deleteButton);
        editPanel.Controls.Add(editButtons, 0, 4);

        var explanation = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.DarkSlateGray,
            Text = "この試験版は拡張名の保存・編集を行います。\r\n" +
                "現段階ではゲーム側の名前取得処理へのフックは未接続です。\r\n" +
                "保存した名前がゲーム内に反映される段階ではありません。",
            TextAlign = ContentAlignment.TopLeft
        };
        editPanel.Controls.Add(explanation, 0, 5);

        var statusPanel = new Panel { Dock = DockStyle.Fill };
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        statusPanel.Controls.Add(_status);

        root.Controls.Add(_list, 0, 0);
        root.Controls.Add(editPanel, 1, 0);
        root.Controls.Add(statusPanel, 0, 1);
        root.SetColumnSpan(statusPanel, 2);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var closeButton = new Button
        {
            Text = "閉じる",
            Width = 100,
            Height = 34
        };
        closeButton.Click += (_, _) => Close();

        var reloadButton = new Button
        {
            Text = "再読み込み",
            Width = 110,
            Height = 34
        };
        reloadButton.Click += (_, _) => LoadDatabase();

        bottom.Controls.Add(closeButton);
        bottom.Controls.Add(reloadButton);
        root.Controls.Add(bottom, 0, 2);
        root.SetColumnSpan(bottom, 2);

        Controls.Add(root);
        Load += (_, _) => LoadDatabase();
    }

    private void LoadDatabase()
    {
        try
        {
            _entries.Clear();
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            if (File.Exists(_databasePath))
            {
                string json = File.ReadAllText(_databasePath, Encoding.UTF8);
                List<ExtendedNameDatabaseEntry>? loaded =
                    JsonSerializer.Deserialize<List<ExtendedNameDatabaseEntry>>(json);

                if (loaded != null)
                    _entries.AddRange(loaded.Where(IsValidStoredEntry));
            }

            RefreshEntries();
            _status.Text =
                "保存先: " + _databasePath + Environment.NewLine +
                "登録件数: " + _entries.Count +
                " 件 / CP932で保存可能な文字列を管理";
        }
        catch (Exception ex)
        {
            _status.Text = "読み込みエラー: " + ex.Message;
            MessageBox.Show(
                this,
                ex.ToString(),
                "拡張名前データベース読み込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool IsValidStoredEntry(ExtendedNameDatabaseEntry entry)
    {
        if (entry == null)
            return false;
        if (!Regex.IsMatch(entry.Id ?? "", "^[a-z0-9_]+$"))
            return false;
        if (string.IsNullOrWhiteSpace(entry.Name))
            return false;
        if (entry.Name.Contains('\0') || entry.Name.Any(char.IsControl))
            return false;
        return true;
    }

    private void SaveDatabase()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        string json = JsonSerializer.Serialize(_entries, JsonOptions);
        string temporaryPath = _databasePath + ".tmp";
        File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        File.Move(temporaryPath, _databasePath, true);
    }

    private void RefreshEntries()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (ExtendedNameDatabaseEntry entry in _entries
            .OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            _list.Items.Add(entry);
        }

        _list.EndUpdate();
    }

    private void LoadSelectedEntry()
    {
        if (!(_list.SelectedItem is ExtendedNameDatabaseEntry entry))
            return;

        _idBox.Text = entry.Id;
        _nameBox.Text = entry.Name;
    }

    private void AddOrUpdateEntry()
    {
        try
        {
            string id = _idBox.Text.Trim();
            string name = _nameBox.Text;

            if (!Regex.IsMatch(id, "^[a-z0-9_]+$"))
                throw new InvalidOperationException(
                    "IDは半角英小文字・数字・アンダースコアで入力してください。");

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("拡張名を入力してください。");

            if (name.Contains('\0') || name.Any(char.IsControl))
                throw new InvalidOperationException(
                    "改行などの制御文字やNULL文字は名前に使用できません。");

            byte[] encoded = Cp932.GetBytes(name);
            if (encoded.Length == 0)
                throw new InvalidOperationException("CP932変換後の名前が空です。");

            ExtendedNameDatabaseEntry? entry = _entries.FirstOrDefault(
                x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                entry = new ExtendedNameDatabaseEntry
                {
                    Id = id,
                    Name = name,
                    Encoding = "CP932"
                };
                _entries.Add(entry);
            }

            entry.Name = name;
            entry.Encoding = "CP932";

            SaveDatabase();
            RefreshEntries();

            int selectedIndex = -1;
            for (int i = 0; i < _list.Items.Count; i++)
            {
                if (_list.Items[i] is ExtendedNameDatabaseEntry item &&
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex >= 0)
                _list.SelectedIndex = selectedIndex;

            _status.Text =
                "データベース保存成功: " + id + " → " + name +
                " / CP932 " + encoded.Length + " bytes";
        }
        catch (Exception ex)
        {
            _status.Text = "保存エラー: " + ex.Message;
            MessageBox.Show(
                this,
                ex.ToString(),
                "拡張名前データベース保存エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void DeleteSelectedEntry()
    {
        if (!(_list.SelectedItem is ExtendedNameDatabaseEntry entry))
        {
            MessageBox.Show(this, "削除する項目を選択してください。");
            return;
        }

        DialogResult result = MessageBox.Show(
            this,
            entry.Id + " の拡張名データを削除しますか？",
            "削除確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
            return;

        _entries.RemoveAll(
            x => string.Equals(x.Id, entry.Id, StringComparison.OrdinalIgnoreCase));

        try
        {
            SaveDatabase();
            RefreshEntries();
            _idBox.Clear();
            _nameBox.Clear();
            _status.Text = "削除・保存しました: " + entry.Id;
        }
        catch (Exception ex)
        {
            _status.Text = "削除保存エラー: " + ex.Message;
            MessageBox.Show(this, ex.ToString(), "削除エラー",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public override string ToString()
    {
        return "拡張名前データベース";
    }
}
