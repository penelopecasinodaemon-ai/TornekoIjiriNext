using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;

namespace Tr3LookPngConverter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly Label _dropLabel = new();
    private readonly Label _templateLabel = new();
    private readonly TextBox _log = new();
    private JsonObject _template = new();
    private Bitmap? _baseImage;
    private byte[] _palette = Array.Empty<byte>();

    private string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tr3LookPngConverter", "settings.json");

    public MainForm()
    {
        Text = "PNG → TR3LOOK 変換ツール";
        Width = 760; Height = 520; MinimumSize = new Size(620, 420);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        Font = new Font("Yu Gothic UI", 10F);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        Controls.Add(root);

        root.Controls.Add(new Label {
            Text = "PNG画像をドロップすると .tr3look に変換します",
            AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 0, 0, 10)
        }, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 10) };
        var choose = new Button { Text = "テンプレート／元PNGを変更", AutoSize = true };
        choose.Click += (_, _) => ChooseTemplateAndBase();
        var reset = new Button { Text = "標準のいしにんぎょうに戻す", AutoSize = true };
        reset.Click += (_, _) => ResetDefault();
        buttons.Controls.Add(choose); buttons.Controls.Add(reset);
        root.Controls.Add(buttons, 0, 1);

        var drop = new Panel { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(247,249,252), AllowDrop = true, Margin = new Padding(0,0,0,10) };
        _dropLabel.Dock = DockStyle.Fill; _dropLabel.TextAlign = ContentAlignment.MiddleCenter;
        _dropLabel.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
        _dropLabel.Text = "ここにPNGファイルをドラッグ＆ドロップ\r\n\r\n複数ファイルもまとめて変換できます";
        drop.Controls.Add(_dropLabel); drop.DragEnter += OnDragEnter; drop.DragDrop += OnDragDrop;
        root.Controls.Add(drop, 0, 2);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        _templateLabel.AutoSize = true; _templateLabel.Padding = new Padding(0,0,0,5);
        bottom.Controls.Add(_templateLabel,0,0);
        _log.Dock = DockStyle.Fill; _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical; _log.Font = new Font("Consolas",9F);
        bottom.Controls.Add(_log,0,1); root.Controls.Add(bottom,0,3);
        DragEnter += OnDragEnter; DragDrop += OnDragDrop;
        LoadInitialAssets();
    }

    private void LoadInitialAssets()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8));
                var tp = doc.RootElement.GetProperty("templatePath").GetString();
                var bp = doc.RootElement.GetProperty("basePngPath").GetString();
                if (!string.IsNullOrWhiteSpace(tp) && !string.IsNullOrWhiteSpace(bp) && File.Exists(tp) && File.Exists(bp))
                {
                    LoadAssets(tp, bp); Log("前回選択したテンプレートを読み込みました。"); return;
                }
            }
        }
        catch (Exception ex) { Log("保存済み設定を読み込めません: " + ex.Message); }
        LoadEmbeddedDefaults();
    }

    private void LoadEmbeddedDefaults()
    {
        using var ts = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tr3LookPngConverter.Assets.default_template.tr3look")
            ?? throw new InvalidOperationException("標準テンプレートがありません。");
        using var tr = new StreamReader(ts, Encoding.UTF8);
        using var bs = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tr3LookPngConverter.Assets.default_base.png.b64")
            ?? throw new InvalidOperationException("標準PNGがありません。");
        using var br = new StreamReader(bs, Encoding.ASCII);
        var bytes = Convert.FromBase64String(br.ReadToEnd().Trim());
        using var ms = new MemoryStream(bytes);
        using var bmp = new Bitmap(ms);
        ApplyAssets(tr.ReadToEnd(), new Bitmap(bmp));
        _templateLabel.Text = "使用中のテンプレート: 標準・いしにんぎょう";
        Log("標準のいしにんぎょうテンプレートを読み込みました。");
    }

    private void LoadAssets(string templatePath, string basePngPath)
    {
        var json = File.ReadAllText(templatePath, Encoding.UTF8);
        using var source = new Bitmap(basePngPath);
        ApplyAssets(json, new Bitmap(source));
        _templateLabel.Text = "使用中のテンプレート: " + Path.GetFileName(templatePath);
    }

    private void ApplyAssets(string json, Bitmap baseImage)
    {
        try
        {
            var parsed = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("テンプレートJSONが不正です。");
            if (parsed["format"]?.GetValue<string>() != "TR3LOOK1") throw new InvalidDataException("TR3LOOK1形式を選択してください。");
            if (parsed["points"]?.GetValue<int>() != 0 || parsed["runs"] is not JsonArray runs || runs.Count != 0)
                throw new InvalidDataException("未塗装（points=0、runs=[]）の元ファイルを指定してください。");
            var pal = Convert.FromBase64String(parsed["pal"]?.GetValue<string>() ?? throw new InvalidDataException("palがありません。"));
            if (pal.Length != 1024) throw new InvalidDataException("パレットは1024 bytesである必要があります。");
            if (baseImage.Width != 256 || baseImage.Height != 256) throw new InvalidDataException("元PNGは256×256である必要があります。");
            _baseImage?.Dispose(); _baseImage = baseImage; _template = parsed; _palette = pal;
        }
        catch { baseImage.Dispose(); throw; }
    }

    private void ChooseTemplateAndBase()
    {
        using var td = new OpenFileDialog { Title = "未塗装の元 .tr3look を選択", Filter = "TR3LOOK (*.tr3look)|*.tr3look|JSON (*.json)|*.json", CheckFileExists = true };
        if (td.ShowDialog(this) != DialogResult.OK) return;
        using var pd = new OpenFileDialog { Title = "同じモデルの未編集・元PNGを選択", Filter = "PNG (*.png)|*.png", CheckFileExists = true };
        if (pd.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            LoadAssets(td.FileName,pd.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { templatePath=td.FileName, basePngPath=pd.FileName }), new UTF8Encoding(false));
            Log("テンプレートを変更しました。次回起動後も保持されます。");
        }
        catch (Exception ex) { MessageBox.Show(this,ex.Message,"設定エラー",MessageBoxButtons.OK,MessageBoxIcon.Error); Log("設定エラー: "+ex.Message); }
    }

    private void ResetDefault()
    {
        try { if (File.Exists(SettingsPath)) File.Delete(SettingsPath); LoadEmbeddedDefaults(); }
        catch (Exception ex) { MessageBox.Show(this,ex.Message,"設定エラー",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
        var pngs = files.Where(f => string.Equals(Path.GetExtension(f),".png",StringComparison.OrdinalIgnoreCase)).ToArray();
        if (pngs.Length == 0) { Log("PNGファイルが見つかりません。"); return; }
        foreach (var file in pngs)
        {
            try
            {
                var r = ConvertPng(file);
                Log($"成功: {Path.GetFileName(file)} → {Path.GetFileName(r.Path)} / 変更 {r.Points:N0} pixel / runs {r.Runs:N0}");
            }
            catch (Exception ex) { Log($"失敗: {Path.GetFileName(file)} / {ex.Message}"); }
        }
    }

    private (string Path,int Points,int Runs) ConvertPng(string path)
    {
        if (_baseImage is null || _palette.Length != 1024) throw new InvalidOperationException("テンプレート未読込です。");
        using var edited = new Bitmap(path);
        if (edited.Size != _baseImage.Size) throw new InvalidDataException($"画像サイズ不一致。必要: {_baseImage.Width}×{_baseImage.Height} / 入力: {edited.Width}×{edited.Height}");
        var changes = new List<(int Index,byte Value)>();
        for (int y=0;y<edited.Height;y++)
        for (int x=0;x<edited.Width;x++)
        {
            var a=_baseImage.GetPixel(x,y); var b=edited.GetPixel(x,y);
            // RGB差分のみを対象にし、画像編集ソフトによるalpha正規化を無視する。
            if (a.R==b.R && a.G==b.G && a.B==b.B) continue;
            changes.Add((y*edited.Width+x,NearestPalette(b)));
        }

        var runs = new JsonArray(); int runCount=0; int cursor=0;
        while(cursor<changes.Count)
        {
            int start=changes[cursor].Index, previous=start;
            var bytes=new List<byte>{changes[cursor].Value}; cursor++;
            while(cursor<changes.Count && changes[cursor].Index==previous+1)
            { bytes.Add(changes[cursor].Value); previous=changes[cursor].Index; cursor++; }
            runs.Add(new JsonObject { ["i"]=start, ["d"]=Convert.ToBase64String(bytes.ToArray()) });
            runCount++;
        }

        var output=(JsonObject)_template.DeepClone();
        output["name"]=System.IO.Path.GetFileNameWithoutExtension(path);
        output["made"]=DateTime.Now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        output["points"]=changes.Count; output["runs"]=runs;
        var outputPath=UniqueOutputPath(path);
        File.WriteAllText(outputPath,output.ToJsonString(new JsonSerializerOptions{WriteIndented=false}),new UTF8Encoding(false));
        return (outputPath,changes.Count,runCount);
    }

    private byte NearestPalette(Color c)
    {
        int best=0; long bestDistance=long.MaxValue;
        for(int i=0;i<256;i++)
        {
            int o=i*4,dr=c.R-_palette[o],dg=c.G-_palette[o+1],db=c.B-_palette[o+2];
            long d=(long)dr*dr+(long)dg*dg+(long)db*db;
            if(d<bestDistance) { bestDistance=d; best=i; if(d==0) break; }
        }
        return (byte)best;
    }

    private static string UniqueOutputPath(string input)
    {
        var desired=System.IO.Path.ChangeExtension(input,".tr3look");
        if(!File.Exists(desired)) return desired;
        var dir=System.IO.Path.GetDirectoryName(desired)!; var name=System.IO.Path.GetFileNameWithoutExtension(desired);
        var candidate=System.IO.Path.Combine(dir,name+"_converted.tr3look"); int n=2;
        while(File.Exists(candidate)) candidate=System.IO.Path.Combine(dir,$"{name}_converted_{n++}.tr3look");
        return candidate;
    }

    private void Log(string message) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    protected override void Dispose(bool disposing)
    {
        if(disposing) _baseImage?.Dispose();
        base.Dispose(disposing);
    }
}