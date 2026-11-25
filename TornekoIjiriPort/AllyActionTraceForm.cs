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
        Width = 760;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            WrapContents = false
        };

        var install = new Button
        {
            Text = "味方トレース設置",
            Width = 140,
            Height = 34
        };

        install.Click += (_, _) =>
            InvokeTraceMethod("Install", "味方側トレースを設置しました。");

        var read = new Button
        {
            Text = "トレース読み取り",
            Width = 140,
            Height = 34
        };

        read.Click += (_, _) =>
            InvokeTraceMethod("Read", "トレースを読み取りました。");

        var restore = new Button
        {
            Text = "元の命令に復元",
            Width = 140,
            Height = 34
        };

        restore.Click += (_, _) =>
            InvokeTraceMethod("Restore", "元の命令に復元しました。");

        _snapshotButton = new Button
        {
            Text = "実行時コード採取",
            Width = 160,
            Height = 34
        };

        _snapshotButton.Click += async (_, _) =>
            await CaptureSnapshotAsync();

        buttons.Controls.Add(install);
        buttons.Controls.Add(read);
        buttons.Controls.Add(restore);
        buttons.Controls.Add(_snapshotButton);

        _output = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new System.Drawing.Font("Consolas", 10)
        };

        root.Controls.Add(buttons, 0, 0);
        root.Controls.Add(_output, 0, 1);
        Controls.Add(root);
    }

    private async Task CaptureSnapshotAsync()
    {
        _snapshotButton.Enabled = false;

        _output.AppendText(
            Environment.NewLine +
            "========================================" +
            Environment.NewLine +
            DateTime.Now.ToString("HH:mm:ss") +
            "  実行時コード採取開始" +
            Environment.NewLine +
            "PCSX2を一時停止している必要があります。" +
            Environment.NewLine);

        try
        {
            string result = await Task.Run(
                () => AllyRuntimeMemorySnapshot.Capture());

            _output.AppendText(
                Environment.NewLine +
                result +
                Environment.NewLine);
        }
        catch (Exception ex)
        {
            _output.AppendText(
                Environment.NewLine +
                "===== 採取エラー =====" +
                Environment.NewLine +
                ex.ToString() +
                Environment.NewLine);
        }
        finally
        {
            _snapshotButton.Enabled = true;
        }
    }

    private void InvokeTraceMethod(
        string methodName,
        string successMessage)
    {
        try
        {
            Type serviceType =
                typeof(AllyActionTraceForm).Assembly.GetType(
                    "TornekoIjiriPort.AllyActionTraceService",
                    true);

            BindingFlags flags =
                BindingFlags.Static |
                BindingFlags.NonPublic |
                BindingFlags.Public;

            MethodInfo method = serviceType.GetMethod(
                methodName,
                flags,
                null,
                Type.EmptyTypes,
                null);

            if (method == null)
            {
                throw new MissingMethodException(
                    "AllyActionTraceService." + methodName +
                    "() が見つかりません。");
            }

            object result = method.Invoke(null, null);

            string output = result == null
                ? successMessage
                : result.ToString();

            _output.AppendText(
                Environment.NewLine +
                "========================================" +
                Environment.NewLine +
                DateTime.Now.ToString("HH:mm:ss") +
                "  " + methodName +
                Environment.NewLine +
                output +
                Environment.NewLine);
        }
        catch (Exception ex)
        {
            Exception actual = ex;

            if (ex is TargetInvocationException tie &&
                tie.InnerException != null)
            {
                actual = tie.InnerException;
            }

            _output.AppendText(
                Environment.NewLine +
                "===== エラー =====" +
                Environment.NewLine +
                actual.ToString() +
                Environment.NewLine);
        }
    }
}