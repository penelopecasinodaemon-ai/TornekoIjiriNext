using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class AllyActionTraceForm : Form
{
    private readonly TextBox _output;
    private readonly Button _detailButton;

    internal AllyActionTraceForm()
    {
        Text = "味方・敵 詳細メモリ監視（アプリ側記録）";
        Width = 1150;
        Height = 760;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 175));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true
        };

        AddButton(buttons, "監視開始", 130,
            () => Execute(AppSideTraceService.Start));

        AddButton(buttons, "記録表示・TXT/CSV保存", 210,
            () => Execute(AppSideTraceService.Read));

        AddButton(buttons, "監視停止", 130,
            () => Execute(AppSideTraceService.Stop));

        AddButton(buttons, "監視状態", 120,
            () => Execute(AppSideTraceService.GetStatus));

        _detailButton = new Button
        {
            Text = "一時停止して512バイト詳細採取",
            Width = 250,
            Height = 36
        };

        _detailButton.Click += async (_, _) => await CaptureDetailedAsync();
        buttons.Controls.Add(_detailButton);

        AddButton(buttons, "目印：イオナズン", 150,
            () => Execute(() => AppSideTraceService.Mark("イオナズン使用")));

        AddButton(buttons, "目印：通常攻撃", 150,
            () => Execute(() => AppSideTraceService.Mark("通常攻撃")));

        AddButton(buttons, "目印：敵撃破", 140,
            () => Execute(() => AppSideTraceService.Mark("敵撃破")));

        AddButton(buttons, "任意の目印を入力", 170,
            () => AddCustomMarker());

        var description = new Label
        {
            AutoSize = false,
            Width = 1050,
            Height = 48,
            Text =
                "自動監視は500ms間隔・個体データ先頭64バイトです。" +
                "512バイト全体の詳細採取はPCSX2を一時停止して実行してください。",
            Padding = new Padding(4)
        };

        buttons.Controls.Add(description);

        _output = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 9.5f)
        };

        root.Controls.Add(buttons, 0, 0);
        root.Controls.Add(_output, 0, 1);
        Controls.Add(root);

        Append(
            "詳細メモリ監視画面です。\r\n" +
            "ゲーム本体への書き込みは行いません。\r\n" +
            "目印を使うと行動とメモリ変化を時系列で比較できます。");
    }

    private static void AddButton(
        FlowLayoutPanel panel,
        string text,
        int width,
        Action action)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 36
        };

        button.Click += (_, _) => action();
        panel.Controls.Add(button);
    }

    private void Execute(Func<string> action)
    {
        try
        {
            Append(action());
        }
        catch (Exception ex)
        {
            Append("===== エラー =====\r\n" + ex);
        }
    }

    private async Task CaptureDetailedAsync()
    {
        _detailButton.Enabled = false;

        try
        {
            string result = await Task.Run(
                () => AppSideTraceService.CaptureDetailed());

            Append(result);
        }
        catch (Exception ex)
        {
            Append("===== 詳細採取エラー =====\r\n" + ex);
        }
        finally
        {
            _detailButton.Enabled = true;
        }
    }

    private void AddCustomMarker()
    {
        using var dialog = new Form
        {
            Text = "調査目印",
            Width = 420,
            Height = 150,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false
        };

        var input = new TextBox
        {
            Left = 12,
            Top = 12,
            Width = 380
        };

        var ok = new Button
        {
            Text = "記録",
            Left = 230,
            Top = 48,
            Width = 75,
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "キャンセル",
            Left = 315,
            Top = 48,
            Width = 75,
            DialogResult = DialogResult.Cancel
        };

        dialog.Controls.Add(input);
        dialog.Controls.Add(ok);
        dialog.Controls.Add(cancel);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(this) == DialogResult.OK &&
            !string.IsNullOrWhiteSpace(input.Text))
        {
            Execute(() => AppSideTraceService.Mark(input.Text));
        }
    }

    private void Append(string message)
    {
        _output.AppendText(
            Environment.NewLine +
            "========================================" +
            Environment.NewLine +
            DateTime.Now.ToString("HH:mm:ss") +
            Environment.NewLine +
            message +
            Environment.NewLine);
    }
}
