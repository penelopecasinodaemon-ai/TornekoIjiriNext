using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class AllyActionTraceForm : Form
{
    private readonly TextBox _output;
    private readonly Button _snapshotButton;

    internal AllyActionTraceForm()
    {
        Text = "味方側特技呼び出しトレース";
        Width = 1080;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = true
        };

        AddButton(buttons, "味方トレース設置", 140,
            () => InvokeTraceMethod("AllyActionTraceService", "Install"));
        AddButton(buttons, "トレース読み取り", 140,
            () => InvokeTraceMethod("AllyActionTraceService", "Read"));
        AddButton(buttons, "元の命令に復元", 140,
            () => InvokeTraceMethod("AllyActionTraceService", "Restore"));

        _snapshotButton = new Button
        {
            Text = "実行時コード採取",
            Width = 160,
            Height = 34
        };
        _snapshotButton.Click += async (_, _) => await CaptureSnapshotAsync();
        buttons.Controls.Add(_snapshotButton);

        AddButton(buttons, "分岐トレース設置", 140,
            () => InvokeTraceMethod("AllySkillRouteTraceService", "Install"));
        AddButton(buttons, "分岐記録読み取り", 140,
            () => InvokeTraceMethod("AllySkillRouteTraceService", "Read"));
        AddButton(buttons, "分岐フック解除", 140,
            () => InvokeTraceMethod("AllySkillRouteTraceService", "Restore"));
        AddButton(buttons, "入口フック設置", 140,
            () => InvokeTraceMethod("AllySkillEntryTraceService", "Install"));
        AddButton(buttons, "入口記録読み取り", 150,
            () => InvokeTraceMethod("AllySkillEntryTraceService", "Read"));
        AddButton(buttons, "入口フック解除", 140,
            () => InvokeTraceMethod("AllySkillEntryTraceService", "Restore"));

        AddButton(buttons, "多点観測設置（24地点）", 190,
            () => InvokeTraceMethod("AllyMultiPointTraceService", "Install"));
        AddButton(buttons, "多点記録読み取り", 160,
            () => InvokeTraceMethod("AllyMultiPointTraceService", "Read"));
        AddButton(buttons, "多点フック一括解除", 170,
            () => InvokeTraceMethod("AllyMultiPointTraceService", "Restore"));

AddButton(buttons, "呼び出し地点トレース設置", 190,
    () => InvokeTraceMethod("AllySkillCallSiteTraceService", "Install"));

AddButton(buttons, "呼び出し地点記録読み取り", 190,
    () => InvokeTraceMethod("AllySkillCallSiteTraceService", "Read"));

AddButton(buttons, "呼び出し地点フック解除", 190,
    () => InvokeTraceMethod("AllySkillCallSiteTraceService", "Restore"));

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

    private async Task CaptureSnapshotAsync()
    {
        _snapshotButton.Enabled = false;
        Append("実行時コード採取開始。PCSX2を一時停止してください。");

        try
        {
            string result = await Task.Run(
                () => AllyRuntimeMemorySnapshot.Capture());
            Append(result);
        }
        catch (Exception ex)
        {
            Append("===== 採取エラー =====\r\n" + ex);
        }
        finally
        {
            _snapshotButton.Enabled = true;
        }
    }

    private void InvokeTraceMethod(string serviceName, string methodName)
    {
        try
        {
            Type serviceType =
                typeof(AllyActionTraceForm).Assembly.GetType(
                    "TornekoIjiriPort." + serviceName, true);

            BindingFlags flags =
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

            MethodInfo method = serviceType.GetMethod(
                methodName, flags, null, Type.EmptyTypes, null);

            if (method == null)
                throw new MissingMethodException(
                    serviceName + "." + methodName + "() が見つかりません。");

            object result = method.Invoke(null, null);
            Append(result == null ? "完了しました。" : result.ToString());
        }
        catch (Exception ex)
        {
            Exception actual = ex;
            if (ex is TargetInvocationException tie && tie.InnerException != null)
                actual = tie.InnerException;

            Append("===== エラー =====\r\n" + actual);
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
