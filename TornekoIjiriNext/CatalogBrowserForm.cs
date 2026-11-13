using System.Text.Json;
using System.Data;

namespace TornekoIjiriNext;

/// <summary>Read-only browser for literal master data recovered from the legacy HSP source.</summary>
internal sealed class CatalogBrowserForm : Form
{
    private readonly JsonDocument _catalog;

    public CatalogBrowserForm()
    {
        Text = "旧版マスター一覧（資料由来・読み取り専用）";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 720);
        MinimumSize = new Size(760, 520);
        _catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "game-catalog.json")));
        FormClosed += (_, _) => _catalog.Dispose();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Text = "旧HSPソースに定義された名称・ID・ヒントの一覧です。ここに表示するアドレス候補や行データは、現行ゲーム上の意味・構造・書き込み先として検証されていません。",
            AutoSize = true,
            MaximumSize = new Size(940, 0),
            ForeColor = Color.DarkSlateBlue,
            Padding = new Padding(2, 2, 2, 10)
        }, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(CreatePage("ダンジョン", "dungeons", ["id", "group", "name", "oldHintAddress"]));
        tabs.TabPages.Add(CreatePage("モンスター名", "monsterNames", ["id", "name"]));
        tabs.TabPages.Add(CreatePage("アイテム", "items", ["id", "name", "categoryId"]));
        tabs.TabPages.Add(CreatePage("合成印", "seals", ["sourceCode", "name"]));
        tabs.TabPages.Add(CreatePage("出現テーブル候補", "monsterSpawnTableHints", ["index", "address", "byteLength", "entryCount"]));
        tabs.TabPages.Add(CreatePage("アイテム分類", "itemCategories", ["id", "name"]));
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);
    }

    private TabPage CreatePage(string title, string key, string[] fields)
    {
        var page = new TabPage(title);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(6) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var search = new TextBox { Width = 280, PlaceholderText = "この一覧を検索…", Margin = new Padding(0, 2, 0, 8) };
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoGenerateColumns = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        layout.Controls.Add(search, 0, 0);
        layout.Controls.Add(grid, 0, 1);
        page.Controls.Add(layout);

        var rows = _catalog.RootElement.GetProperty(key).EnumerateArray()
            .Select(element => fields.ToDictionary(field => field, field => ReadCell(element, field)))
            .ToList();
        void Bind(string filter)
        {
            var visible = string.IsNullOrWhiteSpace(filter) ? rows : rows.Where(row => row.Values.Any(value => value.Contains(filter, StringComparison.CurrentCultureIgnoreCase))).ToList();
            var table = new DataTable();
            foreach (var field in fields) table.Columns.Add(field, typeof(string));
            foreach (var row in visible) table.Rows.Add(fields.Select(field => row[field]).ToArray());
            grid.DataSource = table;
            foreach (DataGridViewColumn column in grid.Columns)
                column.HeaderText = column.Name switch
                {
                    "id" => "ID", "group" => "分類", "name" => "名称", "oldHintAddress" => "旧版アドレスヒント（未検証）",
                    "categoryId" => "分類ID", "sourceCode" => "旧版コード", "index" => "旧版行", "address" => "旧版候補アドレス（未検証）",
                    "byteLength" => "旧版長さ（bytes）", "entryCount" => "旧版換算件数", _ => column.Name
                };
        }
        search.TextChanged += (_, _) => Bind(search.Text.Trim());
        Bind("");
        return page;
    }

    private static string ReadCell(JsonElement element, string field)
    {
        var value = element.GetProperty(field);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
    }
}
