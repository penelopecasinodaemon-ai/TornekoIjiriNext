using System.Drawing;
using System.Windows.Forms;

namespace TornekoIjiriNext;

/// <summary>
/// 複数のオンライン数値編集項目をまとめて表示するワークスペース。
/// </summary>
internal sealed class PineOnlineEditWorkspaceForm : Form
{
    private readonly PineOnlineEditService _service;
    private readonly List<PineOnlineEditFieldDefinition> _definitions;
    private readonly List<PineOnlineNumericEditor> _editors = new();

    private readonly FlowLayoutPanel _fieldPanel;
    private readonly Label _summary;
    private readonly Label _connectionStatus;
    private readonly Button _refreshAll;
    private readonly Button _enableVerified;
    private readonly Button _disableAll;
    private readonly Button _addField;
    private readonly Button _saveProfile;
    private readonly Button _loadProfile;
    private readonly Button _close;

    public PineOnlineEditWorkspaceForm(
        PineOnlineEditService service,
        IEnumerable<PineOnlineEditFieldDefinition> definitions,
        string title = "オンライン編集")
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _definitions = definitions?.ToList()
            ?? throw new ArgumentNullException(nameof(definitions));

        ValidateDefinitions(_definitions);

        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 520);
        Size = new Size(980, 720);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        var titleLabel = new Label
        {
            Text = "オンライン編集ワークスペース",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 13, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 8)
        };

        _connectionStatus = new Label
        {
            Text = "接続状態は各操作時に確認します。",
            AutoSize = true,
            ForeColor = SystemColors.ControlText
        };

        _summary = new Label
        {
            AutoSize = true,
            Text = "",
            Margin = new Padding(3, 6, 3, 6)
        };

        header.Controls.Add(titleLabel);
        header.Controls.Add(_connectionStatus);
        header.Controls.Add(_summary);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 5, 0, 8)
        };

        _refreshAll = new Button
        {
            Text = "全項目を再取得",
            AutoSize = true
        };

        _enableVerified = new Button
        {
            Text = "検証済み項目を編集可能にする",
            AutoSize = true
        };

        _disableAll = new Button
        {
            Text = "編集を一時停止",
            AutoSize = true
        };

        _addField = new Button
        {
            Text = "編集項目を追加...",
            AutoSize = true
        };

        _saveProfile = new Button
        {
            Text = "構成を保存...",
            AutoSize = true
        };

        _loadProfile = new Button
        {
            Text = "構成を読み込む...",
            AutoSize = true
        };

        _refreshAll.Click += (_, _) => RefreshAll();
        _enableVerified.Click += (_, _) => SetVerifiedEditing(true);
        _disableAll.Click += (_, _) => SetVerifiedEditing(false);
        _addField.Click += (_, _) => AddField();
        _saveProfile.Click += (_, _) => SaveProfile();
        _loadProfile.Click += (_, _) => LoadProfile();

        toolbar.Controls.Add(_refreshAll);
        toolbar.Controls.Add(_enableVerified);
        toolbar.Controls.Add(_disableAll);
        toolbar.Controls.Add(_addField);
        toolbar.Controls.Add(_saveProfile);
        toolbar.Controls.Add(_loadProfile);

        _fieldPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(4)
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };

        _close = new Button
        {
            Text = "閉じる",
            AutoSize = true
        };
        _close.Click += (_, _) => Close();

        footer.Controls.Add(_close);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(_fieldPanel, 0, 2);
        root.Controls.Add(footer, 0, 3);

        Controls.Add(root);

        BuildEditors();
        UpdateSummary();
    }

    private static void ValidateDefinitions(
        List<PineOnlineEditFieldDefinition> definitions)
    {
        if (definitions.Count > 512)
            throw new ArgumentException("編集項目は512件以内にしてください。");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in definitions)
        {
            if (item is null)
                throw new ArgumentException("編集項目にnullが含まれています。");

            if (string.IsNullOrWhiteSpace(item.GroupName))
                throw new ArgumentException("グループ名が空の項目があります。");

            if (string.IsNullOrWhiteSpace(item.FieldName))
                throw new ArgumentException("項目名が空の項目があります。");

            if (item.WidthBytes is not (1 or 2 or 4))
                throw new ArgumentException(
                    $"データ幅が不正です: {item.FieldName}");

            ulong end = (ulong)item.Address + (uint)item.WidthBytes - 1;
            if (end > uint.MaxValue)
                throw new ArgumentException(
                    $"アドレス範囲が不正です: {item.FieldName}");

            ulong maxValue = item.WidthBytes switch
            {
                1 => byte.MaxValue,
                2 => ushort.MaxValue,
                4 => uint.MaxValue,
                _ => 0
            };

            if (item.InitialValue > maxValue)
                throw new ArgumentException(
                    $"初期値がデータ幅を超えています: {item.FieldName}");

            string key = $"{item.GroupName}\0{item.FieldName}";
            if (!keys.Add(key))
                throw new ArgumentException(
                    $"同じグループに重複項目があります: {item.FieldName}");
        }
    }

    private void BuildEditors()
    {
        _fieldPanel.SuspendLayout();

        try
        {
            _fieldPanel.Controls.Clear();
            _editors.Clear();

            foreach (var group in _definitions.GroupBy(
            x => x.GroupName,
            StringComparer.OrdinalIgnoreCase))
        {
            var groupBox = new GroupBox
            {
                Text = group.Key,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Width = 880,
                Padding = new Padding(8),
                Margin = new Padding(4, 4, 4, 12)
            };

            var layout = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            foreach (var definition in group)
            {
                var row = new Panel
                {
                    Width = 840,
                    Height = string.IsNullOrWhiteSpace(definition.Description)
                        ? 82 : 105,
                    Margin = new Padding(2)
                };

                var editor = new PineOnlineNumericEditor(
                    _service,
                    definition.FieldName,
                    definition.Address,
                    definition.WidthBytes,
                    definition.InitialValue,
                    definition.AddressVerified)
                {
                    Location = new Point(0, 0),
                    Width = 710,
                    Anchor = AnchorStyles.Left | AnchorStyles.Top
                };

                var removeButton = new Button
                {
                    Text = "削除",
                    AutoSize = true,
                    Location = new Point(730, 5)
                };

                removeButton.Click += (_, _) =>
                {
                    var answer = MessageBox.Show(
                        this,
                        $"「{definition.FieldName}」を構成から削除しますか？",
                        "編集項目の削除",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (answer != DialogResult.Yes)
                        return;

                    _definitions.Remove(definition);
                    BuildEditors();
                };

                row.Controls.Add(editor);
                row.Controls.Add(removeButton);

                if (!string.IsNullOrWhiteSpace(definition.Description))
                {
                    var description = new Label
                    {
                        Text = definition.Description,
                        AutoSize = true,
                        Location = new Point(5, 78),
                        ForeColor = SystemColors.GrayText
                    };
                    row.Controls.Add(description);
                }

                layout.Controls.Add(row);
                _editors.Add(editor);
            }

            groupBox.Controls.Add(layout);
            _fieldPanel.Controls.Add(groupBox);
            }
        }
        finally
        {
            _fieldPanel.ResumeLayout(true);
        }

        UpdateSummary();
    }

    private void AddField()
    {
        using var dialog = new Form
        {
            Text = "オンライン編集項目の追加",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(480, 350),
            Font = Font
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 7
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < 6; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var groupInput = new TextBox
        {
            Text = "基本能力",
            Dock = DockStyle.Fill
        };

        var nameInput = new TextBox
        {
            Dock = DockStyle.Fill
        };

        var addressInput = new TextBox
        {
            Text = "00000000",
            Dock = DockStyle.Fill
        };

        var widthInput = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Left,
            Width = 150
        };
        widthInput.Items.AddRange(new object[] { "BYTE (1 byte)", "WORD (2 bytes)", "DWORD (4 bytes)" });
        widthInput.SelectedIndex = 0;

        var initialInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = uint.MaxValue,
            DecimalPlaces = 0,
            ThousandsSeparator = true,
            Dock = DockStyle.Left,
            Width = 180
        };

        var descriptionInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 55,
            ScrollBars = ScrollBars.Vertical
        };

        void AddRow(string label, Control control, int row)
        {
            layout.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);

            layout.Controls.Add(control, 1, row);
        }

        AddRow("グループ名", groupInput, 0);
        AddRow("項目名", nameInput, 1);
        AddRow("メモリアドレス", addressInput, 2);
        AddRow("データ幅", widthInput, 3);
        AddRow("初期表示値", initialInput, 4);
        AddRow("説明", descriptionInput, 5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };

        var cancel = new Button
        {
            Text = "キャンセル",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var create = new Button
        {
            Text = "項目を登録",
            AutoSize = true
        };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(create);
        layout.Controls.Add(buttons, 0, 6);
        layout.SetColumnSpan(buttons, 2);

        dialog.Controls.Add(layout);
        dialog.AcceptButton = create;
        dialog.CancelButton = cancel;

        create.Click += (_, _) =>
        {
            string groupName = groupInput.Text.Trim();
            string fieldName = nameInput.Text.Trim();
            string addressText = addressInput.Text.Trim()
                .Replace("0x", "", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(groupName) ||
                string.IsNullOrWhiteSpace(fieldName))
            {
                MessageBox.Show(
                    dialog,
                    "グループ名と項目名を入力してください。",
                    "入力エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (groupName.Length > 100 || fieldName.Length > 100)
            {
                MessageBox.Show(
                    dialog,
                    "グループ名・項目名は100文字以内にしてください。",
                    "入力エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!uint.TryParse(
                addressText,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out uint address))
            {
                MessageBox.Show(
                    dialog,
                    "アドレスは8桁以内の16進数で入力してください。",
                    "入力エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int width = widthInput.SelectedIndex switch
            {
                0 => 1,
                1 => 2,
                2 => 4,
                _ => 0
            };

            if (width == 0)
                return;

            uint initialValue = decimal.ToUInt32(initialInput.Value);

            ulong maxValue = width switch
            {
                1 => byte.MaxValue,
                2 => ushort.MaxValue,
                4 => uint.MaxValue,
                _ => 0
            };

            if (initialValue > maxValue)
            {
                MessageBox.Show(
                    dialog,
                    "初期表示値がデータ幅の上限を超えています。",
                    "入力エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_definitions.Any(x =>
                string.Equals(x.GroupName, groupName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.FieldName, fieldName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show(
                    dialog,
                    "同じグループに同じ項目名が既にあります。",
                    "重複エラー",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_definitions.Count >= 512)
            {
                MessageBox.Show(
                    dialog,
                    "編集項目は512件までです。",
                    "登録上限",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _definitions.Add(new PineOnlineEditFieldDefinition(
                groupName,
                fieldName,
                address,
                width,
                initialValue,
                false,
                descriptionInput.Text.Trim()));

            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        BuildEditors();

        _connectionStatus.Text =
            "項目を登録しました。アドレスは未検証のため編集できません。";
        _connectionStatus.ForeColor = Color.DarkOrange;
    }
    private void RefreshAll()
    {
        int success = 0;
        int failure = 0;
        int skipped = 0;

        foreach (var editor in _editors)
        {
            if (!editor.IsAddressVerified)
            {
                skipped++;
                continue;
            }

            if (editor.RefreshFromGame())
                success++;
            else
                failure++;
        }

        _connectionStatus.Text =
            $"一括再取得: 成功 {success} / 失敗 {failure} / 未検証 {skipped}";

        _connectionStatus.ForeColor =
            failure == 0 ? Color.DarkGreen : Color.DarkRed;
    }

    private void SetVerifiedEditing(bool enabled)
    {
        foreach (var editor in _editors)
            editor.SetEditingEnabled(enabled);

        _connectionStatus.Text = enabled
            ? "検証済み項目の編集を有効化しました。"
            : "編集を一時停止しました。";

        UpdateSummary();
    }

    private void SaveProfile()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "オンライン編集構成を保存",
            Filter = "オンライン編集構成 (*.pineedit.json)|*.pineedit.json|JSONファイル (*.json)|*.json",
            DefaultExt = "pineedit.json",
            AddExtension = true,
            FileName = "OnlineEditProfile.pineedit.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            PineOnlineEditProfileStore.Save(dialog.FileName, _definitions);

            _connectionStatus.Text =
                $"構成を保存しました: {Path.GetFileName(dialog.FileName)}";
            _connectionStatus.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "構成保存エラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void LoadProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "オンライン編集構成を読み込む",
            Filter = "オンライン編集構成 (*.pineedit.json;*.json)|*.pineedit.json;*.json|すべてのファイル (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var definitions =
                PineOnlineEditProfileStore.Load(dialog.FileName);

            var form = new PineOnlineEditWorkspaceForm(
                _service,
                definitions,
                "オンライン編集 - " + Path.GetFileName(dialog.FileName));

            form.Show(this);

            _connectionStatus.Text =
                $"構成を読み込みました: {definitions.Count}項目";
            _connectionStatus.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "構成読み込みエラー",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdateSummary()
    {
        int verified = _definitions.Count(x =>
            x.AddressVerified &&
            _service.IsWriteTargetVerified(
                x.Address,
                x.WidthBytes));
        int unverified = _definitions.Count - verified;

        _summary.Text =
            $"登録項目: {_definitions.Count}件 / " +
            $"アドレス検証済み: {verified}件 / " +
            $"未検証: {unverified}件";
    }
}