using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class AllyActionTraceForm : Form
{
    private readonly TextBox _output;
    private readonly Button _captureButton;

    internal AllyActionTraceForm()
    {
        Text = "味方・敵メモリ監視（アプリ側記録）";
        Width = 1080;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoScroll = true,
            WrapContents = true
        };

        AddButton(
            buttons,
            "アプリ側監視開始",
            180,
            () => Execute(AppSideTraceService.Start));

        AddButton(
            buttons,
            "記録表示・保存",
            160,
            () => Execute(AppSideTraceService.Read));

        AddButton(
            buttons,
            "アプリ側監視停止",
            160,
            () => Execute(AppSideTraceService.Stop));

        AddButton(
            buttons,
            "監視状態",
            120,
            () => Execute(AppSideTraceService.GetStatus));

        _captureButton = new Button
        {
            Text = "一時停止して即時採取",
            Width = 190,
            Height = 34
        };

        _captureButton.Click += async (_, _) =>
            await CaptureNowAsync();

        buttons.Controls.Add(_captureButton);

        AddButton(
            buttons,
            "実行時メモリ全体採取（読み取り専用）",
            270,
            () => CaptureSnapshotAsync());

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
            "新しい監視方式です。\r\n" +
            "ゲーム本体へのトレース用書き込みは行いません。\r\n" +
            "記録データはトルネコいじりネクスト側で管理します。");
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
            Height = 34
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

    private async Task CaptureNowAsync()
    {
        _captureButton.Enabled = false;

        try
        {
            string result = await Task.Run(
                () => AppSideTraceService.CaptureNow());

            Append(result);
        }
        catch (Exception ex)
        {
            Append("===== 即時採取エラー =====\r\n" + ex);
        }
        finally
        {
            _captureButton.Enabled = true;
        }
    }

    private void CaptureSnapshotAsync()
    {
        try
        {
            string result = AllyRuntimeMemorySnapshot.Capture();
            Append(result);
        }
        catch (Exception ex)
        {
            Append("===== メモリ採取エラー =====\r\n" + ex);
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
