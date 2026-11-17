using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TornekoIjiriPort;

internal sealed class EquippedWeaponIdScannerForm : Form
{
    private const int AttackPatchOffset = 0x0020B264;
    private const uint AttackPatchValue = 0x24020001;

    private readonly int[] _candidateOffsets =
    {
        0x006015D4,
        0x009276A4
    };

    private ListView _list = null!;
    private Label _status = null!;

    private bool _patchApplied;
    private uint _originalInstruction;
    private int _patchProcessId;
    private long _patchEeBase;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr written);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(
        IntPtr process,
        IntPtr address,
        out MEMORY_BASIC_INFORMATION information,
        UIntPtr length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(
        IntPtr process,
        IntPtr address,
        UIntPtr size,
        uint newProtect,
        out uint oldProtect);

    public EquippedWeaponIdScannerForm()
    {
        Text = "装備中の武器ID・二回攻撃テスト";
        StartPosition = FormStartPosition.CenterParent;
        Width = 900;
        Height = 560;
        MinimumSize = new Size(760, 440);
        Font = new Font("Yu Gothic UI", 10);

        BuildInterface();
        RefreshValues();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var explanation = new Label
        {
            Dock = DockStyle.Fill,
            Text =
                "候補A・Bの値を読み取り、どちらか一方でも下位16bitが0以外なら二回攻撃コードを適用します。\r\n" +
                "元の命令を読み取って保存し、解除ボタンで元に戻します。",
            TextAlign = ContentAlignment.MiddleLeft
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };

        buttons.Controls.Add(MakeButton(
            "現在値を更新",
            150,
            (_, _) => RefreshValues()));

        buttons.Controls.Add(MakeButton(
            "2回攻撃を有効化",
            190,
            (_, _) => EnableDoubleAttack()));

        buttons.Controls.Add(MakeButton(
            "2回攻撃を解除",
            170,
            (_, _) => RestoreDoubleAttack()));

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true
        };

        _list.Columns.Add("候補", 100);
        _list.Columns.Add("EE RAMオフセット", 180);
        _list.Columns.Add("16bit値", 140);
        _list.Columns.Add("32bit値", 160);
        _list.Columns.Add("判定", 180);

        _status = new Label
        {
            Dock = DockStyle.Fill,
            Text = "PCSX2の接続状態を確認してください。",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        root.Controls.Add(explanation, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(_status, 0, 3);

        Controls.Add(root);
    }

    private static Button MakeButton(
        string text,
        int width,
        EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 40,
            Margin = new Padding(4)
        };

        button.Click += click;
        return button;
    }

    private static uint ReadUInt32(IntPtr process, long address)
    {
        byte[] data = new byte[4];
        UIntPtr read;

        bool success = ReadProcessMemory(
            process,
            new IntPtr(address),
            data,
            new UIntPtr(4),
            out read);

        if (!success || read.ToUInt64() != 4)
        {
            throw new IOException(
                "メモリ読み取り失敗。アドレス: 0x" +
                address.ToString("X") +
                " / Win32エラー: " +
                Marshal.GetLastWin32Error());
        }

        return BitConverter.ToUInt32(data, 0);
    }

    private static void WriteUInt32(
        IntPtr process,
        long address,
        uint value)
    {
        const long PageSize = 0x1000;

        IntPtr target = new IntPtr(address);

        MEMORY_BASIC_INFORMATION mbi;

        UIntPtr queried = VirtualQueryEx(
            process,
            target,
            out mbi,
            new UIntPtr((uint)Marshal.SizeOf(typeof(MEMORY_BASIC_INFORMATION))));

        if (queried == UIntPtr.Zero)
        {
            throw new IOException(
                "VirtualQueryExに失敗しました。" +
                "\r\nアドレス: 0x" + address.ToString("X") +
                "\r\nWin32エラー: " + Marshal.GetLastWin32Error());
        }

        ulong regionStart = unchecked((ulong)mbi.BaseAddress.ToInt64());
        ulong regionSize = mbi.RegionSize.ToUInt64();
        ulong regionEnd = checked(regionStart + regionSize);

        bool committed = mbi.State == 0x1000;
        bool guarded = (mbi.Protect & 0x100) != 0;
        bool noAccess = (mbi.Protect & 0xFF) == 0x01;

        if (!committed || guarded || noAccess)
        {
            throw new IOException(
                "二回攻撃の書き込み先が書き込み対象にできない領域です。" +
                "\r\nアドレス: 0x" + address.ToString("X") +
                "\r\n領域開始: 0x" + regionStart.ToString("X") +
                "\r\n領域サイズ: 0x" + regionSize.ToString("X") +
                "\r\nState: 0x" + mbi.State.ToString("X") +
                "\r\nProtect: 0x" + mbi.Protect.ToString("X") +
                "\r\nType: 0x" + mbi.Type.ToString("X"));
        }

        long pageAddress = address & ~(PageSize - 1);

        ulong pageEnd = checked((ulong)pageAddress + (ulong)PageSize);

        if ((ulong)pageAddress < regionStart || pageEnd > regionEnd)
        {
            throw new IOException(
                "書き込み先ページがメモリ領域の範囲外です。" +
                "\r\nページ: 0x" + pageAddress.ToString("X") +
                "\r\n領域開始: 0x" + regionStart.ToString("X") +
                "\r\n領域終了: 0x" + regionEnd.ToString("X"));
        }

        IntPtr page = new IntPtr(pageAddress);
        UIntPtr pageSize = new UIntPtr((ulong)PageSize);

        uint oldProtect;

        bool changed = VirtualProtectEx(
            process,
            page,
            pageSize,
            0x40,
            out oldProtect);

        if (!changed)
        {
            int error = Marshal.GetLastWin32Error();

            throw new IOException(
                "メモリ保護属性の変更に失敗しました。" +
                "\r\n対象アドレス: 0x" + address.ToString("X") +
                "\r\nページ開始: 0x" + pageAddress.ToString("X") +
                "\r\nページサイズ: 0x" + PageSize.ToString("X") +
                "\r\n領域開始: 0x" + regionStart.ToString("X") +
                "\r\n領域サイズ: 0x" + regionSize.ToString("X") +
                "\r\nState: 0x" + mbi.State.ToString("X") +
                "\r\nProtect: 0x" + mbi.Protect.ToString("X") +
                "\r\nType: 0x" + mbi.Type.ToString("X") +
                "\r\nWin32エラー: " + error);
        }

        Exception writeError = null;

        try
        {
            byte[] data = BitConverter.GetBytes(value);
            UIntPtr written;

            bool success = WriteProcessMemory(
                process,
                target,
                data,
                new UIntPtr(4),
                out written);

            int error = success
                ? 0
                : Marshal.GetLastWin32Error();

            if (!success || written.ToUInt64() != 4)
            {
                writeError = new IOException(
                    "保護属性変更後の書き込みに失敗しました。" +
                    "\r\nアドレス: 0x" + address.ToString("X") +
                    "\r\n書き込みサイズ: " + written.ToUInt64() +
                    "\r\nWin32エラー: " + error);
            }

            if (writeError == null)
            {
                uint verify = ReadUInt32(process, address);

                if (verify != value)
                {
                    writeError = new IOException(
                        "書き込み後の読み戻し値が一致しません。" +
                        "\r\n期待値: 0x" + value.ToString("X8") +
                        "\r\n実際値: 0x" + verify.ToString("X8"));
                }
            }
        }
        catch (Exception ex)
        {
            writeError = ex;
        }
        finally
        {
            uint ignored;

            bool restored = VirtualProtectEx(
                process,
                page,
                pageSize,
                oldProtect,
                out ignored);

            if (!restored)
            {
                int error = Marshal.GetLastWin32Error();

                throw new IOException(
                    "メモリ保護属性の復元に失敗しました。" +
                    "\r\nページ: 0x" + pageAddress.ToString("X") +
                    "\r\nWin32エラー: " + error,
                    writeError);
            }
        }

        if (writeError != null)
            throw writeError;
    }

    private IntPtr OpenConnectedProcess(uint access)
    {
        if (!Pcsx2LiveSession.IsConnected ||
            Pcsx2LiveSession.ProcessId <= 0)
        {
            throw new InvalidOperationException(
                "PCSX2に接続されていません。");
        }

        IntPtr process = OpenProcess(
            access,
            false,
            Pcsx2LiveSession.ProcessId);

        if (process == IntPtr.Zero)
        {
            throw new IOException(
                "PCSX2プロセスを開けません。Win32エラー: " +
                Marshal.GetLastWin32Error());
        }

        return process;
    }

    private long GetConnectedEeBase()
    {
        long eeBase =
            Pcsx2MemoryLayoutBuilder.GetEEmemBase(
                Pcsx2LiveSession.ProcessId);

        if (eeBase <= 0)
        {
            throw new InvalidOperationException(
                "EE RAM基準アドレスを取得できません。");
        }

        return eeBase;
    }

    private void RefreshValues()
    {
        IntPtr process = IntPtr.Zero;

        try
        {
            process = OpenConnectedProcess(0x0010 | 0x0400);
            long eeBase = GetConnectedEeBase();

            _list.Items.Clear();

            bool anyValid = false;

            for (int i = 0; i < _candidateOffsets.Length; i++)
            {
                int offset = _candidateOffsets[i];
                uint value = ReadUInt32(
                    process,
                    eeBase + offset);

                ushort value16 = (ushort)(value & 0xFFFF);
                bool valid = value16 != 0;

                if (valid)
                    anyValid = true;

                var item = new ListViewItem(
                    i == 0 ? "候補A" : "候補B");

                item.SubItems.Add(
                    "0x" + offset.ToString("X8"));

                item.SubItems.Add(
                    "0x" + value16.ToString("X4"));

                item.SubItems.Add(
                    "0x" + value.ToString("X8"));

                item.SubItems.Add(
                    valid ? "値あり・適用判定対象" : "値が0");

                _list.Items.Add(item);
            }

            _status.Text = anyValid
                ? "候補A・Bの少なくとも一方に0以外の値があります。二回攻撃を試せます。"
                : "候補A・Bの下位16bitが両方とも0です。";
        }
        catch (Exception ex)
        {
            ShowError("現在値の取得に失敗しました", ex);
        }
        finally
        {
            if (process != IntPtr.Zero)
                CloseHandle(process);
        }
    }

    private void EnableDoubleAttack()
    {
        if (_patchApplied)
        {
            MessageBox.Show(
                this,
                "二回攻撃コードは既に適用されています。",
                "適用済み",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        IntPtr process = IntPtr.Zero;

        try
        {
            process = OpenConnectedProcess(
                0x0010 | 0x0020 | 0x0008 | 0x0400);

            int processId = Pcsx2LiveSession.ProcessId;
            long eeBase = GetConnectedEeBase();

            var values = new List<uint>();

            for (int i = 0; i < _candidateOffsets.Length; i++)
            {
                uint value = ReadUInt32(
                    process,
                    eeBase + _candidateOffsets[i]);

                values.Add(value);
            }

            bool candidateAValid =
                (values[0] & 0xFFFF) != 0;

            bool candidateBValid =
                (values[1] & 0xFFFF) != 0;

            if (!candidateAValid && !candidateBValid)
            {
                RefreshValues();

                MessageBox.Show(
                    this,
                    "候補A・Bのどちらも下位16bitが0でした。\r\n" +
                    "二回攻撃コードは適用していません。",
                    "適用条件を満たしていません",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            long attackAddress = eeBase + AttackPatchOffset;

            uint original = ReadUInt32(
                process,
                attackAddress);

            if (original == AttackPatchValue)
            {
                MessageBox.Show(
                    this,
                    "二回攻撃コードは既にメモリに存在します。\r\n" +
                    "元の命令を安全に保存できないため、今回は書き込みません。",
                    "既に適用されています",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            WriteUInt32(
                process,
                attackAddress,
                AttackPatchValue);

            uint verification = ReadUInt32(
                process,
                attackAddress);

            if (verification != AttackPatchValue)
            {
                WriteUInt32(
                    process,
                    attackAddress,
                    original);

                throw new IOException(
                    "書き込み後の検証に失敗しました。元の命令へ戻しました。");
            }

            _originalInstruction = original;
            _patchProcessId = processId;
            _patchEeBase = eeBase;
            _patchApplied = true;

            RefreshListWithoutReopen(values);

            string selected =
                candidateAValid && candidateBValid
                    ? "候補A・Bの両方"
                    : candidateAValid
                        ? "候補A"
                        : "候補B";

            _status.Text =
                selected + "を検出。二回攻撃コードを適用しました。";

            MessageBox.Show(
                this,
                "二回攻撃コードを書き込みました。\r\n\r\n" +
                "判定対象: " + selected + "\r\n" +
                "適用先: EE RAM + 0x0020B264\r\n" +
                "適用値: 0x24020001\r\n" +
                "保存した元の命令: 0x" +
                original.ToString("X8") +
                "\r\n\r\nゲーム内で動作を確認してください。",
                "二回攻撃を有効化しました",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("二回攻撃の有効化に失敗しました", ex);
        }
        finally
        {
            if (process != IntPtr.Zero)
                CloseHandle(process);
        }
    }

    private void RefreshListWithoutReopen(List<uint> values)
    {
        _list.Items.Clear();

        for (int i = 0; i < _candidateOffsets.Length; i++)
        {
            uint value = values[i];
            ushort value16 = (ushort)(value & 0xFFFF);

            var item = new ListViewItem(
                i == 0 ? "候補A" : "候補B");

            item.SubItems.Add(
                "0x" + _candidateOffsets[i].ToString("X8"));

            item.SubItems.Add(
                "0x" + value16.ToString("X4"));

            item.SubItems.Add(
                "0x" + value.ToString("X8"));

            item.SubItems.Add(
                value16 != 0 ? "適用判定に使用" : "値が0");

            _list.Items.Add(item);
        }
    }

    private void RestoreDoubleAttack()
    {
        if (!_patchApplied)
        {
            MessageBox.Show(
                this,
                "この画面から適用した二回攻撃コードはありません。",
                "未適用",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        IntPtr process = IntPtr.Zero;

        try
        {
            process = OpenProcess(
                0x0010 | 0x0020 | 0x0008 | 0x0400,
                false,
                _patchProcessId);

            if (process == IntPtr.Zero)
            {
                throw new IOException(
                    "適用時のPCSX2プロセスを開けません。");
            }

            long attackAddress =
                _patchEeBase + AttackPatchOffset;

            uint current = ReadUInt32(
                process,
                attackAddress);

            if (current != AttackPatchValue)
            {
                throw new InvalidOperationException(
                    "適用先の命令が変更されています。\r\n" +
                    "安全のため元の命令への復元を中止しました。\r\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            WriteUInt32(
                process,
                attackAddress,
                _originalInstruction);

            uint verification = ReadUInt32(
                process,
                attackAddress);

            if (verification != _originalInstruction)
            {
                throw new IOException(
                    "復元後の読み戻し確認に失敗しました。");
            }

            _patchApplied = false;

            _status.Text =
                "元の命令 0x" +
                _originalInstruction.ToString("X8") +
                " に復元しました。";

            MessageBox.Show(
                this,
                "元の命令へ復元しました。\r\n" +
                "復元値: 0x" +
                _originalInstruction.ToString("X8"),
                "二回攻撃を解除しました",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError("二回攻撃の解除に失敗しました", ex);
        }
        finally
        {
            if (process != IntPtr.Zero)
                CloseHandle(process);
        }
    }

    private void ShowError(string title, Exception ex)
    {
        _status.Text = title + ": " + ex.Message;

        MessageBox.Show(
            this,
            ex.ToString(),
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}