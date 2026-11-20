using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TornekoIjiriPort;

internal static class ExtendedItemNameMemoryWriter
{
    private const long ItemIdOffset = 0x00105441;
    private const long ItemNameOffset = 0x0010544A;
    private const int NameCapacity = 7;
    private const int NameBufferSize = 8;

    private const uint ProcessAccess =
        0x0008 |
        0x0010 |
        0x0020 |
        0x0400;

    private static readonly Encoding Cp932 = CreateCp932();

    private static readonly byte[] ExpectedId =
        Encoding.ASCII.GetBytes("item_164\0");

    private static readonly byte[] OriginalName =
        Cp932.GetBytes("指輪164\0");

    private static readonly byte[] TestName =
        Cp932.GetBytes("試験123\0");

    private static readonly Dictionary<string, byte[]> LastWritten =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    private static readonly object Sync = new object();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint desiredAccess,
        bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr address,
        byte[] buffer,
        UIntPtr size,
        out UIntPtr bytesWritten);

    private static Encoding CreateCp932()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        return Encoding.GetEncoding(
            932,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }

    private static byte[] ReadExact(
        IntPtr handle,
        long address,
        int length)
    {
        if (address <= 0 || length <= 0)
            throw new InvalidOperationException(
                "読み取りアドレスまたはサイズが不正です。");

        byte[] buffer = new byte[length];

        bool success = ReadProcessMemory(
            handle,
            new IntPtr(address),
            buffer,
            new UIntPtr((uint)length),
            out UIntPtr read);

        if (!success || read.ToUInt64() != (ulong)length)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "EE RAMの読み取りに失敗しました。");
        }

        return buffer;
    }

    private static void WriteExact(
        IntPtr handle,
        long address,
        byte[] data)
    {
        if (address <= 0 || data == null || data.Length == 0)
            throw new InvalidOperationException(
                "書き込みアドレスまたはデータが不正です。");

        bool success = WriteProcessMemory(
            handle,
            new IntPtr(address),
            data,
            new UIntPtr((uint)data.Length),
            out UIntPtr written);

        int error = success ? 0 : Marshal.GetLastWin32Error();

        if (!success || written.ToUInt64() != (ulong)data.Length)
        {
            throw new Win32Exception(
                error,
                "EE RAMへの書き込みに失敗しました。");
        }
    }

    private static byte[] CreateReplacement(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                "表示名を入力してください。");

        byte[] encoded;

        try
        {
            encoded = Cp932.GetBytes(name);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidOperationException(
                "表示名にCP932で表現できない文字が含まれています。",
                ex);
        }

        if (encoded.Length > NameCapacity)
        {
            throw new InvalidOperationException(
                "item_164の既存名前領域は7バイトです。\r\n" +
                "入力された名前: " + encoded.Length + " bytes\r\n" +
                "現在の適用上限: " + NameCapacity + " bytes\r\n\r\n" +
                "長い名前の適用には、ゲーム側の名前参照先を拡張する別の実装が必要です。");
        }

        byte[] replacement = new byte[NameBufferSize];
        Array.Copy(encoded, replacement, encoded.Length);

        return replacement;
    }

    public static string Apply(
        int processId,
        string id,
        string name)
    {
        if (processId <= 0)
            throw new InvalidOperationException(
                "PCSX2 v2.8.2が接続されていません。");

        if (!string.Equals(
            id,
            "item_164",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "この試験実装の対象IDは item_164 のみです。");
        }

        byte[] replacement = CreateReplacement(name);

        long eeBase =
            Pcsx2MemoryLayoutBuilder.GetEEmemBase(processId);

        if (eeBase <= 0)
            throw new InvalidOperationException(
                "EE RAMの先頭アドレスが無効です。");

        long idAddress = checked(eeBase + ItemIdOffset);
        long nameAddress = checked(eeBase + ItemNameOffset);

        IntPtr handle = OpenProcess(
            ProcessAccess,
            false,
            processId);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "PCSX2を読み書き用に開けませんでした。");
        }

        string key =
            processId.ToString() + ":" + eeBase.ToString("X");

        Exception operationError = null;
        string result = "";

        try
        {
            byte[] currentId = ReadExact(
                handle,
                idAddress,
                ExpectedId.Length);

            if (!currentId.SequenceEqual(ExpectedId))
            {
                throw new InvalidOperationException(
                    "item_164のID照合に失敗しました。\r\n" +
                    "期待値: " + BitConverter.ToString(ExpectedId) + "\r\n" +
                    "実際値: " + BitConverter.ToString(currentId) + "\r\n" +
                    "誤書き込み防止のため停止しました。");
            }

            byte[] currentName = ReadExact(
                handle,
                nameAddress,
                NameBufferSize);

            bool knownName =
                currentName.SequenceEqual(OriginalName) ||
                currentName.SequenceEqual(TestName);

            lock (Sync)
            {
                if (LastWritten.TryGetValue(
                    key,
                    out byte[] previous))
                {
                    if (currentName.SequenceEqual(previous))
                        knownName = true;
                }
            }

            if (!knownName)
            {
                throw new InvalidOperationException(
                    "item_164の現在の名前が既知の内容と一致しません。\r\n" +
                    "現在値: " + BitConverter.ToString(currentName) + "\r\n" +
                    "誤書き込み防止のため停止しました。");
            }

            WriteExact(handle, nameAddress, replacement);

            byte[] verify = ReadExact(
                handle,
                nameAddress,
                NameBufferSize);

            if (!verify.SequenceEqual(replacement))
            {
                Exception restoreError = null;

                try
                {
                    WriteExact(handle, nameAddress, currentName);

                    byte[] restored = ReadExact(
                        handle,
                        nameAddress,
                        NameBufferSize);

                    if (!restored.SequenceEqual(currentName))
                    {
                        restoreError = new InvalidOperationException(
                            "元の名前への復元読み戻しに失敗しました。");
                    }
                }
                catch (Exception ex)
                {
                    restoreError = ex;
                }

                if (restoreError != null)
                {
                    throw new InvalidOperationException(
                        "書き込み後の検証に失敗し、元データの復元も確認できませんでした。\r\n" +
                        restoreError.Message);
                }

                throw new InvalidOperationException(
                    "書き込み後の読み戻し検証に失敗しました。元の名前へ復元しました。");
            }

            lock (Sync)
            {
                LastWritten[key] = (byte[])replacement.Clone();
            }

            result =
                "ID: item_164\r\n" +
                "適用名: " + name + "\r\n" +
                "CP932: " + replacement.Length + "バイト領域\r\n" +
                "EE RAMアドレス: 0x" + nameAddress.ToString("X") +
                "\r\n書き込み後の読み戻し一致を確認しました。";
        }
        catch (Exception ex)
        {
            operationError = ex;
        }

        bool closed = CloseHandle(handle);

        if (!closed && operationError == null)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "メモリ操作後のPCSX2ハンドルを閉じられませんでした。");
        }

        if (operationError != null)
        {
            throw new InvalidOperationException(
                "拡張名前の適用に失敗しました。",
                operationError);
        }

        return result;
    }
}