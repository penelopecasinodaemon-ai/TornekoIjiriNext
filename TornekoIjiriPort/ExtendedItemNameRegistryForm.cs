using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class ExtendedItemNameRegistryForm : Form
{
    private sealed class OverrideEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        public override string ToString()
        {
            return Id + "  →  " + Name;
        }
    }

    private readonly ListBox _list = new ListBox();
    private readonly TextBox _id = new TextBox();
    private readonly TextBox _name = new TextBox();
    private readonly Label _status = new Label();
    private readonly List<OverrideEntry> _entries = new List<OverrideEntry>();
    private readonly string _filePath;

    private static readonly Encoding Cp932 = CreateCp932();

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            932,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    public ExtendedItemNameRegistryForm(int processId)
    {
        Text = "拡張名前データベース（試験版）";
        Width = 760;
        Height = 560;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new System.Drawing.Size(620, 420);

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TornekoIjiriNext");

        _filePath = Path.Combine(directory, "ExtendedItemNames.json");

        var notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(10),
            Text =
                "試験版：アプリ側の拡張名前データベースです。\r\n" +
                "現在は保存・編集のみ対応しています。ゲーム側の名前取得フックは未接続のため、" +
                "ここで登録した名前はゲーム内表示にはまだ反映されません。"
        };

        _list.Dock = DockStyle.Fill;
        _list.Font = new System.Drawing.Font("Yu Gothic UI", 10);
        _list.SelectedIndexChanged += (_, _) => LoadSelected();

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 300,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 7
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        editor.Controls.Add(new Label
        {
            Text = "アイテムID",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        }, 0, 0);

        _id.Dock = DockStyle.Fill;
        _id.PlaceholderText = "例: item_164";
        editor.Controls.Add(_id, 0, 1);

        editor.Controls.Add(new Label
        {
            Text = "拡張表示名",
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        }, 0, 2);

        _name.Dock = DockStyle.Fill;
        _name.PlaceholderText = "例: スライムの指輪";
        editor.Controls.Add(_name, 0, 3);

        var save = new Button
        {
            Text = "登録・更新",
            Dock = DockStyle.Fill,
            Height = 36
        };
        save.Click += (_, _) => AddOrUpdate();
        editor.Controls.Add(save, 0, 4);

        var remove = new Button
        {
            Text = "選択項目を削除",
            Dock = DockStyle.Fill,
            Height = 36
        };
        remove.Click += (_, _) => RemoveSelected();
        editor.Controls.Add(remove, 0, 5);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 58;
        _status.Padding = new Padding(8);
        _status.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        Controls.Add(_list);
        Controls.Add(editor);
        Controls.Add(_status);
        Controls.Add(notice);

        LoadEntries();
    }

    private void LoadEntries()
    {
        _entries.Clear();

        if (File.Exists(_filePath))
        {
            try
            {
                string json = File.ReadAllText(_filePath, Encoding.UTF8);
                List<OverrideEntry>? loaded =
                    JsonSerializer.Deserialize<List<OverrideEntry>>(json);

                if (loaded != null)
                {
                    foreach (OverrideEntry entry in loaded)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Id))
                            continue;

                        if (string.IsNullOrWhiteSpace(entry.Name))
                            continue;

                        if (_entries.Any(x => string.Equals(
                            x.Id,
                            entry.Id,
                            StringComparison.OrdinalIgnoreCase)))
                            continue;

                        _entries.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                _status.Text = "データベース読込エラー: " + ex.Message;
                MessageBox.Show(
                    this,
                    "拡張名前データベースを読み込めませんでした。\r\n" +
                    "元ファイルは削除・変更していません。\r\n\r\n" +
                    ex.Message,
                    "拡張名前DB読込エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                RefreshList();
                return;
            }
        }

        if (!File.Exists(_filePath) && _entries.Count == 0)
        {
            _entries.Add(new OverrideEntry
            {
                Id = "item_164",
                Name = "スライムの指輪"
            });
            SaveEntries();
        }

        RefreshList();
        _status.Text =
            "登録数: " + _entries.Count +
            " 件\r\n保存先: " + _filePath;
    }

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();

        foreach (OverrideEntry entry in _entries.OrderBy(
            x => x.Id,
            StringComparer.OrdinalIgnoreCase))
        {
            _list.Items.Add(entry);
        }

        _list.EndUpdate();
    }

    private void LoadSelected()
    {
        if (!(_list.SelectedItem is OverrideEntry entry))
            return;

        _id.Text = entry.Id;
        _name.Text = entry.Name;
    }

    private void AddOrUpdate()
    {
        string id = _id.Text.Trim();
        string name = _name.Text.Trim();

        if (!Regex.IsMatch(
            id,
            "^[a-z0-9_]{1,40}$",
            RegexOptions.CultureInvariant))
        {
            MessageBox.Show(
                this,
                "IDは半角英小文字・数字・アンダースコアの1～40文字で入力してください。",
                "ID形式エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (name.Length == 0)
        {
            MessageBox.Show(this, "表示名を入力してください。");
            return;
        }

        byte[] encoded;

        try
        {
            encoded = Cp932.GetBytes(name);
        }
        catch (EncoderFallbackException)
        {
            MessageBox.Show(
                this,
                "表示名にCP932で表現できない文字が含まれています。",
                "文字コードエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (encoded.Length > 100)
        {
            MessageBox.Show(
                this,
                "試験版の登録上限はCP932で100バイトです。\r\n入力: " +
                encoded.Length + " bytes",
                "名前が長すぎます",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        OverrideEntry? existing = _entries.FirstOrDefault(
            x => string.Equals(
                x.Id,
                id,
                StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            existing.Name = name;
        }

        if (existing == null)
        {
            _entries.Add(new OverrideEntry
            {
                Id = id,
                Name = name
            });
        }

        SaveEntries();
        RefreshList();

        for (int i = 0; i < _list.Items.Count; i++)
        {
            if (_list.Items[i] is OverrideEntry item &&
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                _list.SelectedIndex = i;
                break;
            }
        }

        _status.Text =
            "登録・保存しました。\r\n" +
            id + " → " + name +
            "（CP932 " + encoded.Length + " bytes）";
    }

    private void RemoveSelected()
    {
        if (!(_list.SelectedItem is OverrideEntry entry))
        {
            MessageBox.Show(this, "削除する項目を選択してください。");
            return;
        }

        DialogResult answer = MessageBox.Show(
            this,
            entry.Id + " の登録を削除しますか？",
            "登録削除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        _entries.RemoveAll(x => string.Equals(
            x.Id,
            entry.Id,
            StringComparison.OrdinalIgnoreCase));

        SaveEntries();
        RefreshList();
        _id.Clear();
        _name.Clear();
        _status.Text = "登録を削除しました。";
    }

    private void SaveEntries()
    {
        string directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(
            _entries.OrderBy(
                x => x.Id,
                StringComparer.OrdinalIgnoreCase),
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        string temporaryPath = _filePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            json,
            new UTF8Encoding(false));

        File.Move(temporaryPath, _filePath, true);
    }
}
