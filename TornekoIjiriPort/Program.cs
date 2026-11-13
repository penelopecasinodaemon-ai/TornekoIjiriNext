using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TornekoIjiriPort;

// =========================================================
// 旧HSP互換 PCSX2接続・メモリ検索基盤
// 現段階は読み取り専用
// =========================================================

internal sealed class Pcsx2ScanResult
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public long FirstSignatureAddress { get; set; }
    public long SecondSignatureAddress { get; set; }
    public long ScannedBytes { get; set; }
    public string Message { get; set; } = "";
}

internal static class Pcsx2MemoryScanner
{
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_NOACCESS = 0x01;
    private const uint PAGE_GUARD = 0x100;

    private static readonly byte[] SignatureA =
    {
        0x20, 0x00, 0x18, 0x00, 0xA0,
        0x00, 0x18, 0x00, 0x20, 0x01,
        0x18, 0x00, 0xC0, 0x10, 0x18
    };

    private static readonly byte[] SignatureB =
    {
        0x69, 0x74, 0x65, 0x6D,
        0x5F, 0x65, 0x71, 0x75,
        0x69, 0x70, 0x6F, 0x66,
        0x66, 0x00, 0x00, 0x00
    };

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
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

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        UIntPtr nSize,
        out UIntPtr lpNumberOfBytesRead);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(
        IntPtr hProcess,
        IntPtr lpAddress,
        out MEMORY_BASIC_INFORMATION lpBuffer,
        UIntPtr dwLength);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process(
        IntPtr hProcess,
        out bool wow64Process);

    private static ulong ToUInt64(IntPtr value)
    {
        return unchecked((ulong)value.ToInt64());
    }

    private static IntPtr ToIntPtr(ulong value)
    {
        return new IntPtr(unchecked((long)value));
    }

    private static int FindPattern(
        byte[] buffer,
        int length,
        byte[] pattern)
    {
        if (length < pattern.Length)
            return -1;

        int last = length - pattern.Length;

        for (int i = 0; i <= last; i++)
        {
            bool match = true;

            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    public static System.Collections.Generic.List<System.Diagnostics.Process>
        FindProcesses()
    {
        var result =
            new System.Collections.Generic.List<System.Diagnostics.Process>();

        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                string name = process.ProcessName;

                if (name.IndexOf(
                    "pcsx2",
                    System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(process);
                }
            }
            catch
            {
                // 終了済みプロセスなどは無視
            }
        }

        return result;
    }

    // メモリ全域を検索せず、PCSX2プロセスを開くだけの接続処理。
    public static IntPtr OpenConnection(int processId)
    {
        IntPtr handle = OpenProcess(
            PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false,
            processId);

        if (handle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "PCSX2プロセスへの接続に失敗しました。");
        }

        return handle;
    }

    public static void CloseConnection(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
        }
    }

    public static Pcsx2ScanResult Scan(
    int processId,
    System.Action<string> report)
{
    var result = new Pcsx2ScanResult();
    result.ProcessId = processId;

    IntPtr handle = OpenProcess(
        PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
        false,
        processId);

    if (handle == IntPtr.Zero)
    {
        throw new System.ComponentModel.Win32Exception(
            System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
            "PCSX2プロセスを開けませんでした。");
    }

    try
    {
        using (var process = System.Diagnostics.Process.GetProcessById(processId))
        {
            result.ProcessName = process.ProcessName;
        }

        bool isWow64 = false;
        bool architectureKnown = IsWow64Process(handle, out isWow64);

        ulong maximumAddress;

        if (architectureKnown && isWow64)
            maximumAddress = 0x7FFEFFFF;
        else if (!System.Environment.Is64BitOperatingSystem)
            maximumAddress = 0x7FFEFFFF;
        else
            maximumAddress = 0x00007FFFFFFEFFFF;

        ulong address = 0x10000;

        ulong mbiSize = (ulong)System.Runtime.InteropServices.Marshal.SizeOf(
            typeof(MEMORY_BASIC_INFORMATION));

        const int CHUNK_SIZE = 1024 * 1024;
        const int FALLBACK_SIZE = 4096;

        byte[] buffer = new byte[CHUNK_SIZE + 32];

        ulong firstAddress = 0;
        ulong secondAddress = 0;

        while (address < maximumAddress)
        {
            MEMORY_BASIC_INFORMATION mbi;

            UIntPtr queryResult = VirtualQueryEx(
                handle,
                ToIntPtr(address),
                out mbi,
                new UIntPtr(mbiSize));

            if (queryResult == UIntPtr.Zero)
                break;

            ulong baseAddress = ToUInt64(mbi.BaseAddress);
            ulong regionSize = mbi.RegionSize.ToUInt64();

            if (regionSize == 0)
            {
                address += 0x1000;
                continue;
            }

            ulong nextAddress = baseAddress + regionSize;

            bool readable =
                mbi.State == MEM_COMMIT &&
                (mbi.Protect & PAGE_NOACCESS) == 0 &&
                (mbi.Protect & PAGE_GUARD) == 0;

            if (readable && regionSize <= 1024UL * 1024UL * 1024UL)
            {
                report?.Invoke(
                    "メモリ領域を検索中: 0x" +
                    baseAddress.ToString("X") +
                    " / " +
                    (regionSize / 1024).ToString() +
                    " KB");

                ulong offset = 0;
                int carry = 0;

                while (offset < regionSize)
                {
                    int request = (int)System.Math.Min(
                        (ulong)CHUNK_SIZE,
                        regionSize - offset);

                    if (carry > 0)
                    {
                        System.Array.Copy(
                            buffer,
                            CHUNK_SIZE,
                            buffer,
                            0,
                            carry);
                    }

                    byte[] chunkBuffer = new byte[request];
                    UIntPtr bytesRead;

                    bool readOk = ReadProcessMemory(
                        handle,
                        ToIntPtr(baseAddress + offset),
                        chunkBuffer,
                        new UIntPtr((uint)request),
                        out bytesRead);

                    // ReadProcessMemoryがfalseでも、
                    // 実際に読み取れたバイト数があれば利用する。
                    int actual = (int)System.Math.Min(
                        bytesRead.ToUInt64(),
                        (ulong)request);

                    if (actual <= 0)
                    {
                        // 大きな読み取りに失敗した場合は
                        // 4KBずつ先へ進み、読み取り可能な場所を探す。
                        ulong skip = System.Math.Min(
                            (ulong)FALLBACK_SIZE,
                            regionSize - offset);

                        offset += skip;
                        carry = 0;
                        continue;
                    }

                    System.Array.Copy(
                        chunkBuffer,
                        0,
                        buffer,
                        carry,
                        actual);

                    int total = carry + actual;

                    // AとBは別々のメモリ領域に存在してもよい。
                    if (firstAddress == 0)
                    {
                        int foundA = FindPattern(
                            buffer,
                            total,
                            SignatureA);

                        if (foundA >= 0)
                        {
                            firstAddress =
                                baseAddress + offset -
                                (ulong)carry + (ulong)foundA;

                            report?.Invoke(
                                "シグネチャA検出: 0x" +
                                firstAddress.ToString("X"));
                        }
                    }

                    if (secondAddress == 0)
                    {
                        int foundB = FindPattern(
                            buffer,
                            total,
                            SignatureB);

                        if (foundB >= 0)
                        {
                            secondAddress =
                                baseAddress + offset -
                                (ulong)carry + (ulong)foundB;

                            report?.Invoke(
                                "シグネチャB検出: 0x" +
                                secondAddress.ToString("X"));
                        }
                    }

                    result.ScannedBytes += actual;

                    if (firstAddress != 0 && secondAddress != 0)
                        break;

                    int maxPatternLength = System.Math.Max(
                        SignatureA.Length,
                        SignatureB.Length);

                    carry = System.Math.Min(
                        maxPatternLength - 1,
                        total);

                    if (carry > 0)
                    {
                        System.Array.Copy(
                            buffer,
                            total - carry,
                            buffer,
                            CHUNK_SIZE,
                            carry);
                    }

                    offset += (ulong)actual;
                }
            }

            if (firstAddress != 0 && secondAddress != 0)
                break;

            if (nextAddress <= address)
                break;

            address = nextAddress;
        }

        result.FirstSignatureAddress =
            unchecked((long)firstAddress);

        result.SecondSignatureAddress =
            unchecked((long)secondAddress);

        if (firstAddress != 0 && secondAddress != 0)
        {
            result.Message =
                "旧版の2種類の識別パターンを検出しました。";
        }
        else if (firstAddress != 0)
        {
            result.Message =
                "シグネチャAのみ検出。シグネチャBは未検出です。";
        }
        else if (secondAddress != 0)
        {
            result.Message =
                "シグネチャBのみ検出。シグネチャAは未検出です。";
        }
        else
        {
            result.Message =
                "両方のシグネチャを検出できませんでした。";
        }

        return result;
    }
    finally
    {
        CloseHandle(handle);
    }
}

// =========================================================
}

// 旧HSP互換 ポインタ計算・メモリ領域アドレス定義
// 読み取り専用
// =========================================================

internal sealed class Pcsx2MemoryLayout
{
    public long SignatureA { get; set; }
    public long SignatureB { get; set; }

    public uint Correction { get; set; }

    public uint[] PointerValues { get; set; } =
        new uint[5];

    public System.Collections.Generic.Dictionary<string, long>
        Addresses { get; set; } =
        new System.Collections.Generic.Dictionary<string, long>();

    public string Describe()
    {
        var lines = new System.Collections.Generic.List<string>();

        lines.Add("第1識別アドレス: 0x" + SignatureA.ToString("X"));
        lines.Add("第2識別アドレス: 0x" + SignatureB.ToString("X"));
        lines.Add("32bit補正値: 0x" + Correction.ToString("X8"));

        for (int i = 0; i < PointerValues.Length; i++)
        {
            lines.Add(
                "ポインタ[" + i + "]: 0x" +
                PointerValues[i].ToString("X8"));
        }

        lines.Add("");
        lines.Add("旧HSP由来の主要アドレス:");

        foreach (var pair in Addresses)
        {
            lines.Add(
                pair.Key + ": 0x" +
                pair.Value.ToString("X"));
        }

        return string.Join(
            System.Environment.NewLine,
            lines);
    }
}

internal static class Pcsx2MemoryLayoutBuilder
{
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern System.IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(
        System.IntPtr handle);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        System.IntPtr process,
        System.IntPtr address,
        byte[] buffer,
        System.UIntPtr size,
        out System.UIntPtr read);

    private static byte[] Read(
        System.IntPtr handle,
        long address,
        int size)
    {
        if (address <= 0)
            throw new System.ArgumentOutOfRangeException(
                nameof(address));

        byte[] buffer = new byte[size];
        System.UIntPtr read;

        bool ok = ReadProcessMemory(
            handle,
            new System.IntPtr(address),
            buffer,
            new System.UIntPtr((uint)size),
            out read);

        if (!ok || read.ToUInt64() != (ulong)size)
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "ゲームメモリの読み取りに失敗しました。");
        }

        return buffer;
    }

    private static uint ReadUInt32(
        System.IntPtr handle,
        long address)
    {
        return System.BitConverter.ToUInt32(
            Read(handle, address, 4), 0);
    }

    private static long AddOffset(long address, long offset)
    {
        return checked(address + offset);
    }

    public static Pcsx2MemoryLayout Build(
        int processId,
        long signatureA,
        long signatureB)
    {
        var layout = new Pcsx2MemoryLayout();

        layout.SignatureA = signatureA;
        layout.SignatureB = signatureB;

        System.IntPtr handle = OpenProcess(
            PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false,
            processId);

        if (handle == System.IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "ポインタ計算用にPCSX2を開けません。");
        }

        try
        {
            // 旧HSP:
            // var_142 = peek(var_134, 3)
            // var_143 = var_142 * 16777216
            byte highByte = Read(handle, signatureA + 3, 1)[0];
            uint correction = (uint)highByte << 24;

            layout.Correction = correction;

            // 旧HSP:
            // var_144 ～ var_149
            // 4バイト値を5個読み、同じ補正値を加算する。
            for (int i = 0; i < 5; i++)
            {
                uint raw = ReadUInt32(
                    handle,
                    signatureA + (i * 4));

                layout.PointerValues[i] =
                    unchecked(raw + correction);
            }

            // 旧HSP label_073 のアドレス式を忠実に反映。
// var_134 = 第1識別パターンのアドレス
// var_136 = 第2識別パターンのアドレス
long a = signatureA;
long b = signatureB;

var map = layout.Addresses;

// 第2識別アドレスを基準にする領域
map["var_150"] = AddOffset(b, 87592);
map["var_152"] = AddOffset(b, 89224);
map["var_154"] = AddOffset(b, 16);
map["var_156"] = AddOffset(b, 23797200);
map["var_170"] = AddOffset(b, 232804);

// サイズ・定数。アドレスではない。
map["var_151_size"] = 32 * 30;
map["var_153_size"] = 64;
map["var_155_size"] = 144;
map["var_157_size"] = 8192;
map["var_159_size"] = 11;
map["var_161_size"] = 10;
map["var_163_size"] = 10;
map["var_165_size"] = 4;
map["var_167_size"] = 256 * 2;
map["var_169_size"] = 256 * 2;
map["var_171_size"] = 1;

// 第1識別アドレスを基準にする領域
map["var_158"] = AddOffset(a, -350603);
map["var_160"] = AddOffset(a, -297327);
map["var_162"] = AddOffset(a, -297238);
map["var_164"] = AddOffset(a, 1371800);

// 補正値を基準にする領域
map["var_166"] = AddOffset(9384904L, correction);
map["var_168"] = AddOffset(9385504L, correction);

// 旧HSPの固定アドレス差分。
// すべて var_134 + (固定値 - 538443776)
map["var_172"] = AddOffset(a, 543012008L - 538443776L);
map["var_173"] = AddOffset(a, 543012010L - 538443776L);
map["var_174"] = AddOffset(a, 543012012L - 538443776L);
map["var_175"] = AddOffset(a, 543012014L - 538443776L);
map["var_176"] = AddOffset(a, 543012016L - 538443776L);
map["var_177"] = AddOffset(a, 543012020L - 538443776L);

map["var_178"] = AddOffset(a, 543011948L - 538443776L);
map["var_179"] = AddOffset(a, 543011950L - 538443776L);
map["var_180"] = AddOffset(a, 543011952L - 538443776L);
map["var_181"] = AddOffset(a, 543011954L - 538443776L);
map["var_182"] = AddOffset(a, 543011956L - 538443776L);
map["var_183"] = AddOffset(a, 543011960L - 538443776L);

map["var_184"] = AddOffset(a, 546469264L - 538443776L);
map["var_185"] = AddOffset(a, 543011700L - 538443776L);
map["var_186"] = AddOffset(a, 540633860L - 538443776L);

map["var_187"] = AddOffset(a, 543138894L - 538443776L);
map["var_188"] = AddOffset(a, 543138872L - 538443776L);
map["var_189"] = AddOffset(a, 543138876L - 538443776L);
map["var_190"] = AddOffset(a, 543138880L - 538443776L);
map["var_191"] = AddOffset(a, 543138884L - 538443776L);
map["var_192"] = AddOffset(a, 543138888L - 538443776L);
map["var_193"] = AddOffset(a, 543138890L - 538443776L);
map["var_194"] = AddOffset(a, 543138904L - 538443776L);

map["var_195"] = AddOffset(a, 546466584L - 538443776L);
map["var_196"] = AddOffset(a, 546469264L - 538443776L);
map["var_197"] = AddOffset(a, 546469266L - 538443776L);
map["var_198"] = AddOffset(a, 543478226L - 538443776L);
map["var_199"] = AddOffset(a, 543489746L - 538443776L);

// 第2識別アドレスを基準にする追加領域
map["var_200"] = AddOffset(b, 2722880);
map["var_201"] = AddOffset(b, 2772128);
map["var_202"] = AddOffset(b, 3427744);
map["var_203"] = AddOffset(b, 3503392);
map["var_204"] = AddOffset(b, 3818496);
map["var_205"] = AddOffset(b, 3298672);
map["var_206"] = AddOffset(b, 3332912);
map["var_207"] = AddOffset(b, 3742160);

// 第1識別アドレス基準の追加領域
map["var_208"] = AddOffset(a, 550005776L - 538443776L);
map["var_209"] = AddOffset(a, 550041552L - 538443776L);
map["var_210"] = AddOffset(a, 550468000L - 538443776L);
map["var_211"] = AddOffset(a, 550504160L - 538443776L);
map["var_212"] = AddOffset(a, 550542992L - 538443776L);
map["var_213"] = AddOffset(a, 551741936L - 538443776L);
map["var_214"] = AddOffset(a, 550865712L - 538443776L);
map["var_215"] = AddOffset(a, 551089872L - 538443776L);
map["var_216"] = AddOffset(a, 551116928L - 538443776L);

map["var_217"] = AddOffset(a, 546466530L - 538443776L);
map["var_218"] = AddOffset(a, 546466532L - 538443776L);
map["var_219"] = AddOffset(a, 546466534L - 538443776L);
map["var_220"] = AddOffset(a, 543843152L - 538443776L);

// HSPの定数・サイズ
map["var_221_constant"] = 6984312L - 6972240L;
map["var_223_size"] = 220 * 20;

// var_222 = 543676328 - 16 * 20 - 538443776 + var_134
map["var_222"] = AddOffset(
    a,
    543676328L - 16L * 20L - 538443776L);

// 5個目の補正済みポインタ var_149 を基準にした領域
long p149 = layout.PointerValues[4];

map["var_227"] = AddOffset(p149, 9);
map["var_228"] = AddOffset(p149, 12);
map["var_229"] = p149;
map["var_230"] = AddOffset(p149, 11);
map["var_231"] = AddOffset(p149, 2);
map["var_232"] = AddOffset(p149, 4);
map["var_233"] = AddOffset(p149, 20);
map["var_234"] = AddOffset(p149, 6);
map["var_235"] = AddOffset(p149, 7);
map["var_236"] = AddOffset(p149, 18);
map["var_237"] = AddOffset(p149, 10);
map["var_238"] = AddOffset(p149, 16);

// HSPの固定値。これらはアドレスではなく相対オフセット。
map["var_239_offset"] = 692944;
map["var_240_offset"] = 919568;
map["var_241_offset"] = 919569;
map["var_242_offset"] = 919570;
map["var_243_offset"] = 919580;
map["var_244_offset"] = 919583;
map["var_245_offset"] = 919584;
map["var_246_offset"] = 919572;
map["var_247_offset"] = 919588;
map["var_248_offset"] = 919585;
map["var_249_offset"] = 931848;
map["var_250_offset"] = 931852;
map["var_251_offset"] = 931856;
map["var_252_offset"] = 931860;
map["var_253_offset"] = 1647976;

map["var_644"] = AddOffset(
    a,
    546379562L + 6L - 538443776L);

map["var_761_size"] =
    546383552L - 546379562L;

map["var_648"] = AddOffset(
    a,
    546392048L - 538443776L);

map["var_762_size"] =
    546393648L - 546392048L;

map["var_254"] = AddOffset(a, 538156288L - 538443776L);
map["var_255_difference"] =
    (538162816L - 538443776L) -
    (538156288L - 538443776L);
map["var_256_size"] = 112;

return layout;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}

// =========================================================
// PCSX2メモリから旧HSP互換BINを構築する。
// 読み取り専用。ゲームメモリへの書き込みは行わない。
// =========================================================

internal static class Pcsx2VanillaBinReader
{
    private const int BaseSize = 524288;
    private const int VersionOffset = 355056;
    private const int ExtensionOffset = 355072;

    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern System.IntPtr OpenProcess(
        uint access,
        bool inherit,
        int processId);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(
        System.IntPtr handle);

    [System.Runtime.InteropServices.DllImport(
        "kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        System.IntPtr process,
        System.IntPtr address,
        byte[] buffer,
        System.UIntPtr size,
        out System.UIntPtr read);

    private static byte[] Read(
        System.IntPtr handle,
        long address,
        int length)
    {
        if (handle == System.IntPtr.Zero)
            throw new System.ArgumentException("PCSX2プロセスハンドルが無効です。");

        if (address <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(address));

        if (length < 0 || length > 64 * 1024 * 1024)
            throw new System.ArgumentOutOfRangeException(nameof(length));

        byte[] buffer = new byte[length];
        System.UIntPtr read;

        bool ok = ReadProcessMemory(
            handle,
            new System.IntPtr(address),
            buffer,
            new System.UIntPtr((uint)length),
            out read);

        // GetLastWin32Error は別のAPIを呼ぶ前に取得する。
        int win32Error = ok
            ? 0
            : System.Runtime.InteropServices.Marshal.GetLastWin32Error();

        ulong actual = read.ToUInt64();

        if (!ok || actual != (ulong)length)
        {
            string detail =
                "PCSX2メモリ読み取り失敗。" +
                " アドレス=0x" + address.ToString("X") +
                " (decimal=" + address.ToString() + ")" +
                " 要求サイズ=" + length.ToString() + " bytes" +
                " 実読取サイズ=" + actual.ToString() + " bytes" +
                " ReadProcessMemory=" + ok.ToString() +
                " Win32Error=" + win32Error.ToString() +
                " (0x" + win32Error.ToString("X") + ")";

            throw new System.InvalidOperationException(detail);
        }

        return buffer;
    }

    private static long Address(
        Pcsx2MemoryLayout layout,
        string key)
    {
        if (!layout.Addresses.TryGetValue(key, out long value))
            throw new System.InvalidOperationException(
                "メモリアドレスが定義されていません: " + key);

        return value;
    }

    private static int Size(
        Pcsx2MemoryLayout layout,
        string key)
    {
        long value = Address(layout, key);

        if (value <= 0 || value > 1024 * 1024)
            throw new System.InvalidOperationException(
                "メモリ領域サイズが不正です: " + key);

        return (int)value;
    }

    private static void Append(
        System.IntPtr handle,
        byte[] bin,
        ref int destination,
        long address,
        int length)
    {
        if (destination < 0 ||
            length < 0 ||
            destination > bin.Length ||
            length > bin.Length - destination)
        {
            throw new System.InvalidOperationException(
                "BIN組み立て先が範囲外です。位置: " +
                destination + " / サイズ: " + length);
        }

        byte[] part = Read(handle, address, length);

        System.Buffer.BlockCopy(
            part,
            0,
            bin,
            destination,
            length);

        // 旧HSP版のmemcpy後の領域間隔。
        destination = checked(destination + length + 16);
    }

    private static void AppendFlag(
        byte[] bin,
        ref int destination,
        byte value)
    {
        if (destination < 0 || destination >= bin.Length)
            throw new System.InvalidOperationException(
                "BINフラグ位置が範囲外です。");

        bin[destination] = value;
        destination = checked(destination + 17);
    }

    public static byte[] Capture(
        int processId,
        Pcsx2MemoryLayout layout)
    {
        if (layout == null)
            throw new System.ArgumentNullException(nameof(layout));

        System.IntPtr handle = OpenProcess(
            PROCESS_VM_READ | PROCESS_QUERY_INFORMATION,
            false,
            processId);

        if (handle == System.IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
                "BIN取得用にPCSX2を開けません。");
        }

        try
        {
            // 旧HSP:
            // ReadProcessMemory var_130, var_134,
            // varptr(var_226), 524288, var_145
            byte[] bin = Read(
                handle,
                layout.SignatureA,
                BaseSize);

            // 旧HSP版のBIN出力はバージョン5として構築する。
            bin[VersionOffset] = 5;

            int destination = ExtensionOffset;

            // 旧HSP label_155 の読み込み順序を維持。
            Append(handle, bin, ref destination,
                Address(layout, "var_150"),
                Size(layout, "var_151_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_152"),
                Size(layout, "var_153_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_154"),
                Size(layout, "var_155_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_156"),
                Size(layout, "var_157_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_158"),
                Size(layout, "var_159_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_160"),
                Size(layout, "var_161_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_162"),
                Size(layout, "var_163_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_164"),
                Size(layout, "var_165_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_166"),
                Size(layout, "var_167_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_168"),
                Size(layout, "var_169_size"));

            // 旧HSPのチェックボックス値。
            // 新しいC#画面では初期値を0として扱う。
            AppendFlag(bin, ref destination, 0);

            Append(handle, bin, ref destination,
                Address(layout, "var_170"),
                Size(layout, "var_171_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_220"),
                Size(layout, "var_221_constant"));

            Append(handle, bin, ref destination,
                Address(layout, "var_222"),
                Size(layout, "var_223_size"));

            AppendFlag(bin, ref destination, 0);

            // バージョン5の追加領域。
            Append(handle, bin, ref destination,
                checked(Address(layout, "var_644") - 6),
                Size(layout, "var_761_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_648"),
                Size(layout, "var_762_size"));

            Append(handle, bin, ref destination,
                Address(layout, "var_254"),
                Size(layout, "var_255_difference"));

            if (destination > BaseSize)
            {
                throw new System.InvalidOperationException(
                    "旧HSP互換BINの構築位置が524288 bytesを超えました。");
            }

            if (bin.Length != BaseSize)
                throw new System.InvalidOperationException(
                    "BINサイズが524288 bytesではありません。");

            return bin;
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}

// ============================================================
// メインメニューとPCSX2接続画面で共有する状態。
// 接続とBIN読み込みを別々に管理する。
// ============================================================

internal static class Pcsx2LiveSession
{
    public static bool IsConnected { get; private set; }
    public static bool VanillaDataLoaded { get; private set; }
    public static int ProcessId { get; private set; }
    public static byte[]? VanillaBin { get; private set; }

    public static event System.Action? StateChanged;

    public static void SetConnected(int processId)
    {
        IsConnected = true;
        ProcessId = processId;

        // 新しい接続では新しい取得を待つ。
        VanillaDataLoaded = false;
        VanillaBin = null;

        StateChanged?.Invoke();
    }

    public static void SetVanillaData(byte[] data)
    {
        if (data == null || data.Length != 524288)
            throw new System.ArgumentException(
                "取得BINは524288 bytesである必要があります。");

        VanillaBin = (byte[])data.Clone();
        VanillaDataLoaded = true;

        StateChanged?.Invoke();
    }

    public static void SetDisconnected()
    {
        IsConnected = false;
        ProcessId = 0;

        // 取得済みBINはメイン画面の編集バッファに残す。
        StateChanged?.Invoke();
    }
}

// =========================================================
// PCSX2接続画面
// =========================================================

internal sealed class Pcsx2ConnectionForm :
    System.Windows.Forms.Form
{
    private readonly System.Windows.Forms.ListBox processList;
    private readonly System.Windows.Forms.Button refreshButton;
    private readonly System.Windows.Forms.Button scanButton;
    private readonly System.Windows.Forms.Button memoryScanButton;
    private readonly System.Windows.Forms.TextBox logBox;
    private readonly System.Windows.Forms.Label statusLabel;
    private System.IntPtr _processHandle = System.IntPtr.Zero;
    private int _connectedProcessId;

    public Pcsx2ConnectionForm()
    {
        ShowInTaskbar = false;
        Opacity = 0;
        ShowInTaskbar = false;
        Text = "PCSX2接続・メモリ基準アドレス検索";
        Width = 760;
        Height = 560;
        StartPosition =
            System.Windows.Forms.FormStartPosition.CenterParent;

        var topPanel = new System.Windows.Forms.Panel();
        topPanel.Dock = System.Windows.Forms.DockStyle.Top;
        topPanel.Height = 90;

        processList = new System.Windows.Forms.ListBox();
        processList.Left = 12;
        processList.Top = 12;
        processList.Width = 360;
        processList.Height = 65;

        refreshButton = new System.Windows.Forms.Button();
        refreshButton.Text = "PCSX2再検索";
        refreshButton.Left = 385;
        refreshButton.Top = 12;
        refreshButton.Width = 130;
        refreshButton.Height = 30;
        refreshButton.Click += (s, e) => RefreshProcesses();

        scanButton = new System.Windows.Forms.Button();
        scanButton.Text = "PCSX2へ接続";
        scanButton.Left = 385;
        scanButton.Top = 48;
        scanButton.Width = 220;
        scanButton.Height = 30;
        scanButton.Click += (s, e) => ConnectSelected();

        memoryScanButton = new System.Windows.Forms.Button();
        memoryScanButton.Text = "基準アドレス検索";
        memoryScanButton.Left = 615;
        memoryScanButton.Top = 48;
        memoryScanButton.Width = 130;
        memoryScanButton.Height = 30;
        memoryScanButton.Enabled = false;
        memoryScanButton.Click += (s, e) => ScanMemoryAndBuild();

        topPanel.Controls.Add(processList);
        topPanel.Controls.Add(refreshButton);
        topPanel.Controls.Add(scanButton);
        topPanel.Controls.Add(memoryScanButton);

        statusLabel = new System.Windows.Forms.Label();
        statusLabel.Dock = System.Windows.Forms.DockStyle.Top;
        statusLabel.Height = 28;
        statusLabel.Text = "PCSX2を検索してください。";

        logBox = new System.Windows.Forms.TextBox();
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars =
            System.Windows.Forms.ScrollBars.Vertical;
        logBox.Dock = System.Windows.Forms.DockStyle.Fill;
        logBox.Font = new System.Drawing.Font(
            "Consolas", 9F);

        Controls.Add(logBox);
        Controls.Add(statusLabel);
        Controls.Add(topPanel);

        Shown += (_, _) => ConnectSelected();
        RefreshProcesses();
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new System.Action(
                () => Log(message)));
            return;
        }

        logBox.AppendText(
            "[" + System.DateTime.Now.ToString("HH:mm:ss") +
            "] " + message + System.Environment.NewLine);
    }

    private void RefreshProcesses()
    {
        processList.Items.Clear();

        var processes = Pcsx2MemoryScanner.FindProcesses();

        foreach (var process in processes)
        {
            processList.Items.Add(
                process.ProcessName + "  PID:" + process.Id);
        }

        if (processList.Items.Count > 0)
        {
            processList.SelectedIndex = 0;
        }

        statusLabel.Text =
            "PCSX2候補: " + processes.Count + " 件";

        Log("プロセス検索完了: " + processes.Count + " 件");

        if (processes.Count > 0)
        {
            Log("接続対象を自動選択しました。PID付き一覧の先頭を使用します。");
        }
    }

    protected override void OnFormClosed(
        System.Windows.Forms.FormClosedEventArgs e)
    {
        if (_processHandle != System.IntPtr.Zero)
        {
            Pcsx2MemoryScanner.CloseConnection(_processHandle);
            _processHandle = System.IntPtr.Zero;
            _connectedProcessId = 0;
        }

        Pcsx2LiveSession.SetDisconnected();

        base.OnFormClosed(e);
    }

    private async void ScanMemoryAndBuild()
    {
        if (_processHandle == System.IntPtr.Zero || _connectedProcessId <= 0)
        {
            Log("先にPCSX2へ接続してください。");
            statusLabel.Text = "未接続";
            return;
        }

        int processId = _connectedProcessId;

        memoryScanButton.Enabled = false;
        scanButton.Enabled = false;
        refreshButton.Enabled = false;
        statusLabel.Text = "メモリ検索中...";

        Log("========================================");
        Log("旧HSP互換シグネチャ検索を開始します。");
        Log("接続済みPID: " + processId);
        Log("この処理はメモリ全体を検索します。接続処理とは別です。");

        try
        {
            var result = await System.Threading.Tasks.Task.Run(
                () => Pcsx2MemoryScanner.Scan(
                    processId,
                    message => Log(message)));

            Log(result.Message);
            Log("検索済みバイト数: " + result.ScannedBytes.ToString("N0"));

            if (result.FirstSignatureAddress == 0 ||
                result.SecondSignatureAddress == 0)
            {
                throw new System.InvalidOperationException(
                    "必要なシグネチャが見つかりませんでした。PCSX2とゲームの状態を確認してください。");
            }

            Log("シグネチャA: 0x" +
                result.FirstSignatureAddress.ToString("X"));
            Log("シグネチャB: 0x" +
                result.SecondSignatureAddress.ToString("X"));

            Log("旧HSP互換ポインタ計算を開始します。");

            var layout = await System.Threading.Tasks.Task.Run(
                () => Pcsx2MemoryLayoutBuilder.Build(
                    processId,
                    result.FirstSignatureAddress,
                    result.SecondSignatureAddress));

            Log("旧HSP互換アドレス計算が完了しました。");
            Log(layout.Describe());

            Log("旧HSP互換の524288 bytes BINを構築します。");

            byte[] vanillaBin =
                await System.Threading.Tasks.Task.Run(
                    () => Pcsx2VanillaBinReader.Capture(
                        processId,
                        layout));

            Pcsx2LiveSession.SetVanillaData(vanillaBin);

            Log("バニラBINの取得に成功しました。");
            Log("基本領域: " + vanillaBin.Length.ToString("N0") + " bytes");
            Log("バージョンマーカー: " +
                vanillaBin[355056].ToString());

            statusLabel.Text =
                "接続中！ / バニラデータ読み込み済み";

            Log("メインメニューのBIN編集バッファへ反映しました。");
        }
        catch (System.Exception ex)
        {
            statusLabel.Text = "検索失敗";
            Log("検索エラー: " + ex.Message);
            Log(ex.ToString());

            // 詳細エラー画面を表示し、全文コピーを可能にする。
            UiFactory.Error(this, ex);
        }
        finally
        {
            memoryScanButton.Enabled =
                _processHandle != System.IntPtr.Zero;
            scanButton.Enabled = true;
            refreshButton.Enabled = true;
        }
    }
    private async void ConnectSelected()
    {
        if (_processHandle != System.IntPtr.Zero)
        {
            Log("既にPCSX2へ接続しています。");
            statusLabel.Text =
                "接続済み PID:" + _connectedProcessId;
            return;
        }

        if (processList.SelectedIndex < 0)
        {
            RefreshProcesses();
        }

        if (processList.SelectedIndex < 0 ||
            processList.SelectedItem == null)
        {
            statusLabel.Text = "PCSX2が見つかりません。";
            Log("PCSX2が見つかりません。起動してから再検索してください。");
            return;
        }

        string selected =
            processList.SelectedItem.ToString() ?? "";

        int marker = selected.LastIndexOf("PID:");

        if (marker < 0)
        {
            Log("PID情報を取得できません。");
            statusLabel.Text = "接続失敗";
            return;
        }

        if (!int.TryParse(
            selected.Substring(marker + 4).Trim(),
            out int processId))
        {
            Log("PIDの解析に失敗しました。");
            statusLabel.Text = "接続失敗";
            return;
        }

        refreshButton.Enabled = false;
        scanButton.Enabled = false;
        statusLabel.Text = "PCSX2へ接続中...";

        try
        {
            Log("PCSX2プロセスへ接続します。");
            Log("対象: " + selected);

            _processHandle =
                Pcsx2MemoryScanner.OpenConnection(processId);

            _connectedProcessId = processId;
            memoryScanButton.Enabled = true;

            Log("PCSX2への接続に成功しました。");
            Log("PID: " + processId);
            Log("プロセスハンドルを保持しています。");
            Log("全メモリ検索は実行していません。");
            Log("ポインタ計算も実行していません。");

            statusLabel.Text =
                "接続済み PID:" + processId;
        }
        catch (System.Exception ex)
        {
            _processHandle = System.IntPtr.Zero;
            _connectedProcessId = 0;

            Log("接続エラー: " + ex.Message);
            statusLabel.Text = "接続に失敗しました。";
        }
        finally
        {
            refreshButton.Enabled = true;
            scanButton.Enabled = true;
        }
    }
}


internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var mainForm = new MainForm();



Application.Run(mainForm);
    }
}

// ============================================================
// BINファイル共通処理
// ============================================================
internal static class BinFileService
{
    public const int ExpectedSize = 524288;
    public const int VersionMarkerOffset = 355056;
    public const int ExtensionStartOffset = 355072;

    public static byte[] Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("BINファイルが見つかりません。", path);

        byte[] data = File.ReadAllBytes(path);

        if (data.Length < ExpectedSize)
        {
            throw new InvalidDataException(
                $"BINサイズが不正です。\r\n" +
                $"必要サイズ: {ExpectedSize:N0} bytes\r\n" +
                $"実際のサイズ: {data.Length:N0} bytes");
        }

        return data;
    }

    public static void Save(string path, byte[] data, bool overwrite)
    {
        if (data.Length < ExpectedSize)
            throw new InvalidDataException("保存データのサイズが524288バイトではありません。");

        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("保存先フォルダを取得できません。");

        Directory.CreateDirectory(directory);

        if (File.Exists(fullPath))
        {
            if (!overwrite)
                throw new IOException("保存先ファイルが既に存在します。");

            string backupPath = fullPath + "." +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak";

            File.Copy(fullPath, backupPath, false);
        }

        string tempPath = fullPath + ".tmp";

        try
        {
            File.WriteAllBytes(tempPath, data);

            if (File.Exists(fullPath))
                File.Move(tempPath, fullPath, true);
            else
                File.Move(tempPath, fullPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static ushort ReadUInt16(byte[] data, int offset)
    {
        CheckRange(data, offset, 2);
        return BitConverter.ToUInt16(data, offset);
    }

    public static uint ReadUInt32(byte[] data, int offset)
    {
        CheckRange(data, offset, 4);
        return BitConverter.ToUInt32(data, offset);
    }

    public static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        CheckRange(data, offset, 2);
        BitConverter.GetBytes(value).CopyTo(data, offset);
    }

    public static void WriteUInt32(byte[] data, int offset, uint value)
    {
        CheckRange(data, offset, 4);
        BitConverter.GetBytes(value).CopyTo(data, offset);
    }

    public static void CheckRange(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 0 ||
            offset > data.Length - length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset), "BINデータの範囲外です。");
        }
    }
}

// ============================================================
// フロアデータ
// ============================================================
internal sealed class FloorData
{
    public const int RecordSize = 768;
    public const int BasicSize = 112;

    public int FloorNumber { get; }
    public byte[] Data { get; }

    public FloorData(int floorNumber, byte[] source)
    {
        if (source.Length != RecordSize)
            throw new ArgumentException("フロアデータは768バイト必要です。");

        FloorNumber = floorNumber;
        Data = (byte[])source.Clone();
    }

    public byte ReadByte(int offset)
    {
        BinFileService.CheckRange(Data, offset, 1);
        return Data[offset];
    }

    public void WriteByte(int offset, byte value)
    {
        BinFileService.CheckRange(Data, offset, 1);
        Data[offset] = value;
    }

    public ushort ReadUInt16(int offset)
    {
        return BinFileService.ReadUInt16(Data, offset);
    }

    public void WriteUInt16(int offset, ushort value)
    {
        BinFileService.WriteUInt16(Data, offset, value);
    }
}

// ============================================================
// モンスター出現テーブル
// ============================================================
internal sealed class MonsterSpawnRecord
{
    public const int RecordSize = 6;

    public ushort MonsterId { get; set; }
    public ushort CumulativeRate { get; set; }
    public ushort QuantityValue { get; set; }

    public static MonsterSpawnRecord Read(byte[] data, int offset)
    {
        BinFileService.CheckRange(data, offset, RecordSize);

        return new MonsterSpawnRecord
        {
            MonsterId = BinFileService.ReadUInt16(data, offset),
            CumulativeRate = BinFileService.ReadUInt16(data, offset + 2),
            QuantityValue = BinFileService.ReadUInt16(data, offset + 4)
        };
    }

    public void Write(byte[] data, int offset)
    {
        BinFileService.CheckRange(data, offset, RecordSize);

        BinFileService.WriteUInt16(data, offset, MonsterId);
        BinFileService.WriteUInt16(data, offset + 2, CumulativeRate);
        BinFileService.WriteUInt16(data, offset + 4, QuantityValue);
    }
}

// ============================================================
// アイテムレコード
// ============================================================
internal sealed class ItemRecord
{
    public const int RecordSize = 20;

    public byte[] Data { get; }

    public ItemRecord(byte[] source)
    {
        if (source.Length != RecordSize)
            throw new ArgumentException("アイテムレコードは20バイト必要です。");

        Data = (byte[])source.Clone();
    }
}

// ============================================================
// モンスターレコード
// ============================================================
internal sealed class MonsterRecord
{
    public const int RecordSize = 24;

    public byte[] Data { get; }

    public MonsterRecord(byte[] source)
    {
        if (source.Length != RecordSize)
            throw new ArgumentException("モンスターレコードは24バイト必要です。");

        Data = (byte[])source.Clone();
    }
}

// ============================================================
// INI移行基盤
// ============================================================
internal sealed class LegacyIniSettings
{
    public bool ShowAdventureTurns { get; set; }
    public string MemoryFilePath { get; set; } = "";
    public string RankingDisplayName { get; set; } = "";

    public static LegacyIniSettings Load(string path)
    {
        var result = new LegacyIniSettings();

        if (!File.Exists(path))
            return result;

        Encoding.RegisterProvider(
            System.Text.CodePagesEncodingProvider.Instance);

        string[] lines = File.ReadAllLines(
            path, Encoding.GetEncoding(932));

        // 旧INIの25項目を基準に読み込む。
        // 削除対象のパッチ・重複アイテム階層設定は移行しない。
        if (lines.Length > 19)
            result.ShowAdventureTurns =
                lines[19].Trim() == "1";

        if (lines.Length > 23)
            result.MemoryFilePath = lines[23].Trim();

        if (lines.Length > 24)
            result.RankingDisplayName = lines[24].Trim();

        return result;
    }
}

// ============================================================
// アプリ独自設定
// ============================================================
internal sealed class AppSettings
{
    public int MonsterSpawnCycle { get; set; } = 30;
    public bool ShowAdventureTurns { get; set; }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "TornekoIjiriPort",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json)
                ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        string directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(
            this,
            new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(SettingsPath, json, Encoding.UTF8);
    }
}

// ============================================================
// 共通UI
// ============================================================
internal static class UiFactory
{
    public static Button Button(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Width = 210,
            Height = 42,
            Margin = new Padding(8),
            Font = new Font("Yu Gothic UI", 10)
        };

        button.Click += click;
        return button;
    }

    public static Label Label(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Yu Gothic UI", 10),
            Margin = new Padding(8)
        };
    }

    public static void Error(IWin32Window owner, Exception ex)
    {
        var errorForm = new System.Windows.Forms.Form
        {
            Text = "エラー",
            Width = 820,
            Height = 500,
            MinimumSize = new System.Drawing.Size(500, 300),
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent,
            ShowIcon = false
        };

        var message = new System.Windows.Forms.TextBox
        {
            Multiline = true,
            ReadOnly = true,
            WordWrap = false,
            ScrollBars = System.Windows.Forms.ScrollBars.Both,
            Dock = System.Windows.Forms.DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 10),
            BackColor = System.Drawing.SystemColors.Window,
            ForeColor = System.Drawing.SystemColors.WindowText,
            Text = ex.ToString(),
            HideSelection = false
        };

        var buttons = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Bottom,
            Height = 48,
            FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
            Padding = new System.Windows.Forms.Padding(8),
            WrapContents = false
        };

        var closeButton = new System.Windows.Forms.Button
        {
            Text = "閉じる",
            Width = 100,
            Height = 30,
            DialogResult = System.Windows.Forms.DialogResult.OK
        };

        var copyButton = new System.Windows.Forms.Button
        {
            Text = "全文コピー",
            Width = 110,
            Height = 30
        };

        copyButton.Click += (sender, e) =>
        {
            try
            {
                System.Windows.Forms.Clipboard.SetText(message.Text);
                copyButton.Text = "コピーしました";
            }
            catch (System.Exception copyError)
            {
                System.Windows.Forms.MessageBox.Show(
                    errorForm,
                    copyError.Message,
                    "コピー失敗",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        };

        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(copyButton);

        errorForm.Controls.Add(message);
        errorForm.Controls.Add(buttons);
        errorForm.AcceptButton = closeButton;
        errorForm.CancelButton = closeButton;

        message.Select(0, 0);

        if (owner != null)
            errorForm.ShowDialog(owner);
        else
            errorForm.ShowDialog();

        errorForm.Dispose();
    }
}

// ============================================================
// メイン画面
// ============================================================
internal sealed class MainForm : Form
{
    private byte[]? _bin;
    private TornekoBinPreservationSnapshot? _binSnapshot;
    private string? _binPath;
    private readonly Label _status;
    private readonly AppSettings _settings;

    private readonly Label _pcsx2ConnectionStatus =
        new Label();

    private readonly Label _pcsx2DataStatus =
        new Label();

    private bool _pcsx2Connecting;

    public MainForm()
    {
        // TornekoIjiriPort 統合データ編集画面
        try
        {
            var __twMenu = new global::System.Windows.Forms.MenuStrip();
            var __twRoot = new global::System.Windows.Forms.ToolStripMenuItem("追加データ編集");
            var __twOpen = new global::System.Windows.Forms.ToolStripMenuItem("統合データ編集画面");

            __twOpen.Click += (sender, e) =>
            {
                try
                {
                    byte[] __twBuffer =
                        TornekoDataBufferLocator.Find(this);

                    if (__twBuffer == null)
                    {
                        global::System.Windows.Forms.MessageBox.Show(
                            this,
                            "BIN編集用のbyte配列をMainFormから取得できませんでした。\n" +
                            "既存のBIN読み込み機能でファイルを開いているか確認してください。",
                            "統合データ編集",
                            global::System.Windows.Forms.MessageBoxButtons.OK,
                            global::System.Windows.Forms.MessageBoxIcon.Warning);
                        return;
                    }

                    var __twForm =
                        new TornekoDataWorkbenchForm(__twBuffer);

                    __twForm.Show(this);
                }
                catch (global::System.Exception ex)
                {
                    global::System.Windows.Forms.MessageBox.Show(
                        this,
                        ex.ToString(),
                        "統合データ編集エラー");
                }
            };

            __twRoot.DropDownItems.Add(__twOpen);
            __twMenu.Items.Add(__twRoot);
            this.Controls.Add(__twMenu);
            this.MainMenuStrip = __twMenu;
            __twMenu.BringToFront();
        }
        catch
        {
            // メニュー追加に失敗しても既存画面の起動を妨げない。
        }

        Text = "トルネコいじり Port";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1050;
        Height = 760;
        MinimumSize = new Size(850, 600);
        Font = new Font("Yu Gothic UI", 10);

        _settings = AppSettings.Load();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12)
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        // メインメニュー最上部の独立PCSX2接続パネル。
        var pcsx2Panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 248, 252)
        };

        var pcsx2Title = new Label
        {
            Text = "PCSX2 接続",
            Font = new Font("Yu Gothic UI", 13, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(10, 5)
        };

        var pcsx2Button = new Button
        {
            Text = "PCSX2に接続",
            Width = 170,
            Height = 42,
            Location = new Point(12, 32),
            Font = new Font("Yu Gothic UI", 10, FontStyle.Bold)
        };

        pcsx2Button.Click += (_, _) => OpenPcsx2Connection();

        _pcsx2ConnectionStatus = new Label
        {
            Text = "未接続",
            AutoSize = true,
            Location = new Point(205, 34),
            Font = new Font("Yu Gothic UI", 12, FontStyle.Bold),
            ForeColor = Color.DarkRed
        };

        _pcsx2DataStatus = new Label
        {
            Text = "バニラデータ未読み込み",
            AutoSize = true,
            Location = new Point(205, 58),
            Font = new Font("Yu Gothic UI", 9)
        };

        pcsx2Panel.Controls.Add(pcsx2Title);
        pcsx2Panel.Controls.Add(pcsx2Button);
        pcsx2Panel.Controls.Add(_pcsx2ConnectionStatus);
        pcsx2Panel.Controls.Add(_pcsx2DataStatus);

        var header = new Panel { Dock = DockStyle.Fill };

        var title = new Label
        {
            Text = "トルネコいじり Port",
            Font = new Font("Yu Gothic UI", 22, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(8, 4)
        };

        var subtitle = new Label
        {
            Text = "旧版HSPからのC#移植版",
            AutoSize = true,
            Location = new Point(12, 52)
        };

        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        buttons.Controls.Add(UiFactory.Button(
            "BINファイルを開く", (_, _) => OpenBin()));

        buttons.Controls.Add(UiFactory.Button(
            "別名でBIN保存", (_, _) => SaveBinAs()));

        buttons.Controls.Add(UiFactory.Button(
            "BINを上書き保存", (_, _) => SaveBin()));

        buttons.Controls.Add(UiFactory.Button(
            "フロアデータ編集", (_, _) => OpenFloorEditor()));

        buttons.Controls.Add(UiFactory.Button(
            "モンスター出現テーブル", (_, _) => OpenSpawnEditor()));

        buttons.Controls.Add(UiFactory.Button(
            "アイテムデータ編集", (_, _) =>
                OpenGenericEditor("アイテムデータ", 20)));

        buttons.Controls.Add(UiFactory.Button(
            "モンスター基本データ", (_, _) =>
                OpenGenericEditor("モンスター基本データ", 24)));

        buttons.Controls.Add(UiFactory.Button(
            "共通設定", (_, _) => OpenSettings()));

        buttons.Controls.Add(UiFactory.Button(
            "BIN情報", (_, _) => ShowBinInfo()));


        _status = new Label
        {
            Text = "BINファイル未読み込み",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            BorderStyle = BorderStyle.FixedSingle
        };

        root.Controls.Add(pcsx2Panel, 0, 0);
        root.Controls.Add(header, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        root.Controls.Add(_status, 0, 3);

        Controls.Add(root);

        Pcsx2LiveSession.StateChanged += UpdatePcsx2Status;
        FormClosed += (_, _) =>
        {
            Pcsx2LiveSession.StateChanged -= UpdatePcsx2Status;
        };

        UpdateStatus();
        UpdatePcsx2Status();
    }

    private async void OpenPcsx2Connection()
    {
        if (_pcsx2Connecting)
            return;

        _pcsx2Connecting = true;

        _pcsx2ConnectionStatus.Text = "PCSX2を検索中...";
        _pcsx2ConnectionStatus.ForeColor = Color.DarkOrange;
        _pcsx2DataStatus.Text = "バニラデータ未読み込み";

        System.Collections.Generic.List<System.Diagnostics.Process> processes =
            new System.Collections.Generic.List<System.Diagnostics.Process>();

        try
        {
            // PCSX2プロセスを自動検出
            processes = Pcsx2MemoryScanner.FindProcesses();

            if (processes.Count == 0)
            {
                throw new System.InvalidOperationException(
                    "PCSX2が見つかりません。PCSX2を起動し、ゲームを開始してから再度接続してください。");
            }

            // 最初に検出されたPCSX2を使用
            int processId = processes[0].Id;

            foreach (var process in processes)
                process.Dispose();

            processes.Clear();

            _pcsx2ConnectionStatus.Text =
                "接続中！ PID: " + processId;

            _pcsx2DataStatus.Text =
                "PCSX2プロセス確認中...";

            // プロセスを開けることを確認
            System.IntPtr testHandle = System.IntPtr.Zero;

            try
            {
                testHandle =
                    Pcsx2MemoryScanner.OpenConnection(processId);
            }
            finally
            {
                if (testHandle != System.IntPtr.Zero)
                    Pcsx2MemoryScanner.CloseConnection(testHandle);
            }

            Pcsx2LiveSession.SetConnected(processId);

            // 旧HSP互換シグネチャを検索
            _pcsx2DataStatus.Text =
                "旧HSP互換メモリ検索中...";

            var scanResult =
                await System.Threading.Tasks.Task.Run(
                    () => Pcsx2MemoryScanner.Scan(processId, null));

            if (scanResult.FirstSignatureAddress == 0 ||
                scanResult.SecondSignatureAddress == 0)
            {
                throw new System.InvalidOperationException(
                    "旧HSP互換シグネチャを検出できませんでした。ゲームが起動しているか確認してください。");
            }

            // 旧HSP互換アドレス計算
            _pcsx2DataStatus.Text =
                "旧HSP互換アドレス計算中...";

            var layout =
                await System.Threading.Tasks.Task.Run(
                    () => Pcsx2MemoryLayoutBuilder.Build(
                        processId,
                        scanResult.FirstSignatureAddress,
                        scanResult.SecondSignatureAddress));

            // 旧HSP互換BIN取得
            _pcsx2DataStatus.Text =
                "バニラBIN取得中...";

            byte[] vanillaBin =
                await System.Threading.Tasks.Task.Run(
                    () => Pcsx2VanillaBinReader.Capture(
                        processId,
                        layout));

            if (vanillaBin == null || vanillaBin.Length != 524288)
            {
                throw new System.InvalidOperationException(
                    "取得したBINのサイズが524288 bytesではありません。");
            }

            // メイン画面のBIN編集データへ反映
            Pcsx2LiveSession.SetVanillaData(vanillaBin);

            _pcsx2ConnectionStatus.Text =
                "接続中！ PID: " + processId;

            _pcsx2ConnectionStatus.ForeColor =
                Color.DarkGreen;

            _pcsx2DataStatus.Text =
                "バニラデータ読み込み済み（524,288 bytes）";
        }
        catch (System.Exception ex)
        {
            // メモリ取得失敗でもPCSX2接続自体は維持する。

            _pcsx2ConnectionStatus.Text = "接続・取得失敗";
            _pcsx2ConnectionStatus.ForeColor = Color.DarkRed;

            _pcsx2DataStatus.Text =
                "取得失敗: " + ex.Message;
        }
        finally
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }

            _pcsx2Connecting = false;
        }
    }
    private void UpdatePcsx2Status()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new System.Action(UpdatePcsx2Status));
            return;
        }

        if (Pcsx2LiveSession.IsConnected)
        {
            _pcsx2ConnectionStatus.Text =
                "接続中！ PID: " +
                Pcsx2LiveSession.ProcessId;

            _pcsx2ConnectionStatus.ForeColor =
                Color.DarkGreen;
        }
        else
        {
            _pcsx2ConnectionStatus.Text = "未接続";
            _pcsx2ConnectionStatus.ForeColor =
                Color.DarkRed;
        }

        if (Pcsx2LiveSession.VanillaDataLoaded &&
            Pcsx2LiveSession.VanillaBin != null)
        {
            LoadPcsx2VanillaData(
                Pcsx2LiveSession.VanillaBin);

            _pcsx2DataStatus.Text =
                "バニラデータ読み込み済み（524,288 bytes）";
        }
        else if (_bin != null && _binPath == null)
        {
            _pcsx2DataStatus.Text =
                "前回取得したバニラデータを保持中";
        }
        else
        {
            _pcsx2DataStatus.Text =
                "バニラデータ未読み込み";
        }
    }

    private void LoadPcsx2VanillaData(byte[] data)
    {
        if (data == null || data.Length != 524288)
            throw new System.InvalidOperationException(
                "PCSX2から取得したBINサイズが不正です。");

        _bin = (byte[])data.Clone();

        _binSnapshot =
            TornekoBinPreservation.CreateInMemorySnapshot(_bin);

        // 実在しない仮ファイルを開いたことにはしない。
        _binPath = null;

        UpdateStatus();
    }

    private void OpenBin()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "編集するBINファイルを選択",
            Filter = "BINファイル (*.bin)|*.bin|すべてのファイル (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            // ファイル全体を保持する。
            TornekoBinPreservationSnapshot snapshot =
                TornekoBinPreservation.Load(dialog.FileName);

            // 編集対象は先頭524288 bytesのみ。
            byte[] loaded =
                TornekoBinEditPreserver.ExtractBaseRegion(snapshot);

            if (loaded.Length != BinFileService.ExpectedSize)
            {
                throw new InvalidDataException(
                    "基本領域のサイズが想定と一致しません。");
            }

            _binSnapshot = snapshot;
            _bin = loaded;
            _binPath = dialog.FileName;

            UpdateStatus();

            MessageBox.Show(
                this,
                "BINファイル全体を読み込みました。\r\n" +
                $"基本領域: {loaded.Length:N0} bytes\r\n" +
                $"拡張領域: {snapshot.GetCopy().Length - loaded.Length:N0} bytes",
                "読み込み完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }

        private void SaveBin()
    {
        if (_bin == null ||
            _binSnapshot == null ||
            string.IsNullOrWhiteSpace(_binPath))
        {
            MessageBox.Show(this, "先にBINファイルを開いてください。");
            return;
        }

        if (MessageBox.Show(
            this,
            "現在のBINファイルを上書き保存します。\r\n" +
            "拡張領域は元の内容を維持します。\r\n" +
            "保存前に元ファイルのバックアップを作成します。\r\n\r\n" +
            "続行しますか？",
            "上書き確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            string backupPath =
                _binPath + ".backup_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss");

            File.Copy(_binPath, backupPath, false);

            TornekoBinEditPreserver.SaveEditedBaseCopy(
                _binSnapshot,
                _bin,
                _binPath);

            // 保存した全体ファイルを新しい基準にする。
            _binSnapshot = TornekoBinPreservation.Load(_binPath);

            UpdateStatus();

            MessageBox.Show(
                this,
                "保存しました。\r\n" +
                "拡張領域は元の内容を維持しています。\r\n\r\n" +
                "バックアップ:\r\n" + backupPath,
                "保存完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }

        private void SaveBinAs()
    {
        if (_bin == null || _binSnapshot == null)
        {
            MessageBox.Show(this, "先にBINファイルを開いてください。");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "BINファイルを別名で保存",
            Filter = "BINファイル (*.bin)|*.bin",
            FileName = "edited.bin",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            string destination = Path.GetFullPath(dialog.FileName);

            if (File.Exists(destination))
            {
                string backupPath =
                    destination + ".backup_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss");

                File.Copy(destination, backupPath, false);
            }

            TornekoBinEditPreserver.SaveEditedBaseCopy(
                _binSnapshot,
                _bin,
                destination);

            TornekoBinPreservationSnapshot newSnapshot =
                TornekoBinPreservation.Load(destination);

            _binPath = destination;
            _binSnapshot = newSnapshot;

            UpdateStatus();

            MessageBox.Show(
                this,
                "別名保存しました。\r\n" +
                "拡張領域も保持されています。",
                "保存完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }

    private void OpenFloorEditor()
    {
        if (_bin == null)
        {
            MessageBox.Show(this, "先にBINファイルを開いてください。");
            return;
        }

        using var form = new FloorEditorForm(_bin);
        form.ShowDialog(this);
        UpdateStatus();
    }

    private void OpenSpawnEditor()
    {
        if (_bin == null)
        {
            MessageBox.Show(this, "先にBINファイルを開いてください。");
            return;
        }

        using var form = new SpawnTableEditorForm(_bin);
        form.ShowDialog(this);
        UpdateStatus();
    }

    private void OpenGenericEditor(string title, int recordSize)
    {
        if (_bin == null)
        {
            MessageBox.Show(this, "先にBINファイルを開いてください。");
            return;
        }

        using var form = new GenericRecordEditorForm(
            _bin, title, recordSize);

        form.ShowDialog(this);
        UpdateStatus();
    }

    private void OpenSettings()
    {
        using var form = new CommonSettingsForm(_settings);
        form.ShowDialog(this);
        UpdateStatus();
    }

    private void ShowBinInfo()
    {
        if (_bin == null)
        {
            MessageBox.Show(this, "BINファイル未読み込みです。");
            return;
        }

        int version = _bin[BinFileService.VersionMarkerOffset];

        MessageBox.Show(
            this,
            $"ファイル: {_binPath}\r\n" +
            $"サイズ: {_bin.Length:N0} bytes\r\n" +
            $"バージョンマーカー位置: {BinFileService.VersionMarkerOffset}\r\n" +
            $"バージョン値: {version}\r\n" +
            $"拡張データ開始: {BinFileService.ExtensionStartOffset}",
            "BIN情報");
    }

    private void UpdateStatus()
    {
        if (_bin == null)
        {
            _status.Text = "BINファイル未読み込み";
            return;
        }

        if (string.IsNullOrWhiteSpace(_binPath))
        {
            _status.Text =
                "PCSX2取得バニラBIN　|　" +
                _bin.Length.ToString("N0") +
                " bytes　|　メモリ上の編集データ";
        }
        else
        {
            _status.Text =
                $"読み込み中: {_binPath}　|　{_bin.Length:N0} bytes";
        }
    }
}

// ============================================================
// フロアデータ編集画面
// ============================================================
internal sealed class FloorEditorForm : Form
{
    private readonly byte[] _bin;
    private readonly NumericUpDown _floorNumber;
    private readonly DataGridView _grid;
    private byte[]? _working;

    public FloorEditorForm(byte[] bin)
    {
        _bin = bin;

        Text = "フロアデータ編集";
        StartPosition = FormStartPosition.CenterParent;
        Width = 900;
        Height = 700;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(8)
        };

        top.Controls.Add(UiFactory.Label("フロア番号"));

        _floorNumber = new NumericUpDown
        {
            Minimum = 0,
            Maximum = Math.Max(0, bin.Length / FloorData.RecordSize - 1),
            Width = 100,
            Value = 0
        };

        var loadButton = new Button
        {
            Text = "フロア読み込み",
            Width = 150,
            Height = 30
        };

        loadButton.Click += (_, _) => LoadFloor();

        top.Controls.Add(_floorNumber);
        top.Controls.Add(loadButton);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false
        };

        _grid.Columns.Add("Offset", "オフセット");
        _grid.Columns.Add("Value", "値 (HEX)");
        _grid.Columns.Add("Decimal", "値 (DEC)");

        var saveButton = new Button
        {
            Text = "フロア編集を反映",
            Dock = DockStyle.Bottom,
            Height = 42
        };

        saveButton.Click += (_, _) => ApplyChanges();

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(saveButton);

        LoadFloor();
    }

    private void LoadFloor()
    {
        try
        {
            int index = (int)_floorNumber.Value;
            int offset = checked(index * FloorData.RecordSize);

            BinFileService.CheckRange(
                _bin, offset, FloorData.RecordSize);

            _working = new byte[FloorData.RecordSize];
            Array.Copy(_bin, offset, _working, 0, _working.Length);

            _grid.Rows.Clear();

            for (int i = 0; i < _working.Length; i++)
            {
                _grid.Rows.Add(
                    $"0x{i:X3}",
                    _working[i].ToString("X2"),
                    _working[i].ToString());
            }
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }

    private void ApplyChanges()
    {
        if (_working == null)
            return;

        try
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                int offset = Convert.ToInt32(
                    row.Cells[0].Value?.ToString()?.Replace("0x", ""),
                    16);

                string valueText =
                    row.Cells[1].Value?.ToString() ?? "";

                if (!byte.TryParse(
                    valueText,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out byte value))
                {
                    throw new FormatException(
                        $"オフセット0x{offset:X}の値が不正です。");
                }

                _working[offset] = value;
            }

            int target = checked(
                (int)_floorNumber.Value * FloorData.RecordSize);

            BinFileService.CheckRange(
                _bin, target, FloorData.RecordSize);

            Array.Copy(
                _working, 0, _bin, target, _working.Length);

            MessageBox.Show(
                this,
                "フロアデータをBIN編集領域へ反映しました。\r\n" +
                "ファイルへの保存はメイン画面から行ってください。",
                "反映完了");
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }
}

// ============================================================
// 汎用レコード編集画面
// ============================================================
internal sealed class GenericRecordEditorForm : Form
{
    private readonly byte[] _bin;
    private readonly int _recordSize;
    private readonly DataGridView _grid;
    private readonly NumericUpDown _recordIndex;

    public GenericRecordEditorForm(
        byte[] bin, string title, int recordSize)
    {
        _bin = bin;
        _recordSize = recordSize;

        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        Width = 850;
        Height = 650;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(8)
        };

        top.Controls.Add(UiFactory.Label("レコード番号"));

        _recordIndex = new NumericUpDown
        {
            Minimum = 0,
            Maximum = Math.Max(0, bin.Length / recordSize - 1),
            Width = 100
        };

        var load = new Button
        {
            Text = "読み込み",
            Width = 120,
            Height = 30
        };

        load.Click += (_, _) => LoadRecord();

        top.Controls.Add(_recordIndex);
        top.Controls.Add(load);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _grid.Columns.Add("Offset", "レコード内オフセット");
        _grid.Columns.Add("Value", "HEX値");

        var apply = new Button
        {
            Text = "変更を反映",
            Dock = DockStyle.Bottom,
            Height = 42
        };

        apply.Click += (_, _) => ApplyRecord();

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(apply);

        LoadRecord();
    }

    private int CurrentOffset =>
        checked((int)_recordIndex.Value * _recordSize);

    private void LoadRecord()
    {
        try
        {
            int offset = CurrentOffset;
            BinFileService.CheckRange(_bin, offset, _recordSize);

            _grid.Rows.Clear();

            for (int i = 0; i < _recordSize; i++)
            {
                _grid.Rows.Add(
                    $"0x{i:X2}",
                    _bin[offset + i].ToString("X2"));
            }
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }

    private void ApplyRecord()
    {
        try
        {
            int offset = CurrentOffset;
            BinFileService.CheckRange(_bin, offset, _recordSize);

            byte[] temporary = new byte[_recordSize];

            foreach (DataGridViewRow row in _grid.Rows)
            {
                int index = Convert.ToInt32(
                    row.Cells[0].Value?.ToString()?.Replace("0x", ""),
                    16);

                string valueText =
                    row.Cells[1].Value?.ToString() ?? "";

                if (index < 0 || index >= _recordSize)
                    throw new InvalidDataException("レコード範囲外です。");

                if (!byte.TryParse(
                    valueText,
                    System.Globalization.NumberStyles.HexNumber,
                    null,
                    out byte value))
                {
                    throw new FormatException(
                        $"オフセット0x{index:X}の値が不正です。");
                }

                temporary[index] = value;
            }

            Array.Copy(temporary, 0, _bin, offset, _recordSize);

            MessageBox.Show(
                this,
                "レコードをBIN編集領域へ反映しました。\r\n" +
                "ファイルへの保存はメイン画面から行ってください。",
                "反映完了");
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }
}

// ============================================================
// モンスター出現テーブル編集画面
// ============================================================
internal sealed class SpawnTableEditorForm : Form
{
    private readonly byte[] _bin;
    private readonly NumericUpDown _baseOffset;
    private readonly DataGridView _grid;

    public SpawnTableEditorForm(byte[] bin)
    {
        _bin = bin;

        Text = "モンスター出現テーブル";
        StartPosition = FormStartPosition.CenterParent;
        Width = 900;
        Height = 650;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 55,
            Padding = new Padding(8)
        };

        top.Controls.Add(UiFactory.Label("BIN内オフセット"));

        _baseOffset = new NumericUpDown
        {
            Minimum = 0,
            Maximum = bin.Length - MonsterSpawnRecord.RecordSize,
            Width = 160,
            Hexadecimal = false
        };

        var load = new Button
        {
            Text = "読み込み",
            Width = 120,
            Height = 30
        };

        load.Click += (_, _) => LoadRecords();

        top.Controls.Add(_baseOffset);
        top.Controls.Add(load);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _grid.Columns.Add("Id", "モンスターID");
        _grid.Columns.Add("Rate", "累積出現率");
        _grid.Columns.Add("Quantity", "数量関連値");

        var apply = new Button
        {
            Text = "変更を反映",
            Dock = DockStyle.Bottom,
            Height = 42
        };

        apply.Click += (_, _) => ApplyRecords();

        Controls.Add(_grid);
        Controls.Add(top);
        Controls.Add(apply);
    }

    private void LoadRecords()
    {
        try
        {
            int offset = (int)_baseOffset.Value;
            int count = Math.Min(
                100,
                (_bin.Length - offset) / MonsterSpawnRecord.RecordSize);

            _grid.Rows.Clear();

            for (int i = 0; i < count; i++)
            {
                var record = MonsterSpawnRecord.Read(
                    _bin,
                    offset + i * MonsterSpawnRecord.RecordSize);

                _grid.Rows.Add(
                    record.MonsterId,
                    record.CumulativeRate,
                    record.QuantityValue);
            }
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }

    private void ApplyRecords()
    {
        try
        {
            int offset = (int)_baseOffset.Value;

            if (_grid.Rows.Count == 0)
                return;

            BinFileService.CheckRange(
                _bin,
                offset,
                checked(_grid.Rows.Count * MonsterSpawnRecord.RecordSize));

            byte[] temporary = new byte[
                _grid.Rows.Count * MonsterSpawnRecord.RecordSize];

            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                DataGridViewRow row = _grid.Rows[i];

                ushort id = Convert.ToUInt16(row.Cells[0].Value);
                ushort rate = Convert.ToUInt16(row.Cells[1].Value);
                ushort quantity = Convert.ToUInt16(row.Cells[2].Value);

                var record = new MonsterSpawnRecord
                {
                    MonsterId = id,
                    CumulativeRate = rate,
                    QuantityValue = quantity
                };

                record.Write(
                    temporary,
                    i * MonsterSpawnRecord.RecordSize);
            }

            Array.Copy(temporary, 0, _bin, offset, temporary.Length);

            MessageBox.Show(
                this,
                "テーブルをBIN編集領域へ反映しました。\r\n" +
                "保存はメイン画面から行ってください。",
                "反映完了");
        }
        catch (Exception ex)
        {
            UiFactory.Error(this, ex);
        }
    }
}

// ============================================================
// 共通設定画面
// ============================================================
internal sealed class CommonSettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly NumericUpDown _spawnCycle;
    private readonly CheckBox _showTurns;

    public CommonSettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "共通設定";
        StartPosition = FormStartPosition.CenterParent;
        Width = 550;
        Height = 350;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 4
        };

        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 45));

        layout.Controls.Add(
            UiFactory.Label("モンスター発生周期（ターン）"), 0, 0);

        _spawnCycle = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 999999,
            Value = Math.Clamp(settings.MonsterSpawnCycle, 1, 999999),
            Width = 150
        };

        layout.Controls.Add(_spawnCycle, 1, 0);

        _showTurns = new CheckBox
        {
            Text = "冒険結果にターン数を表示する",
            AutoSize = true,
            Checked = settings.ShowAdventureTurns
        };

        layout.Controls.Add(_showTurns, 0, 1);
        layout.SetColumnSpan(_showTurns, 2);

        var note = new Label
        {
            Text =
                "注意：モンスター発生周期は現在、アプリ独自設定として保存します。\r\n" +
                "ゲーム内メモリやBINへの書き込みは行いません。\r\n" +
                "ゲーム側の保存先・データ幅・仕様が確認できるまで安全のため保留します。",
            AutoSize = true,
            ForeColor = Color.DarkRed,
            Margin = new Padding(8, 18, 8, 18)
        };

        layout.Controls.Add(note, 0, 2);
        layout.SetColumnSpan(note, 2);

        var save = new Button
        {
            Text = "設定を保存",
            Height = 40,
            Dock = DockStyle.Fill
        };

        save.Click += (_, _) =>
        {
            _settings.MonsterSpawnCycle = (int)_spawnCycle.Value;
            _settings.ShowAdventureTurns = _showTurns.Checked;

            try
            {
                _settings.Save();
                MessageBox.Show(this, "設定を保存しました。");
                Close();
            }
            catch (Exception ex)
            {
                UiFactory.Error(this, ex);
            }
        };

        layout.Controls.Add(save, 0, 3);
        layout.SetColumnSpan(save, 2);

        Controls.Add(layout);
    }
}

internal static class TornekoDataBufferLocator
{
    public static byte[] Find(object root)
    {
        if (root == null) return null;

        var flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        var visited = new System.Collections.Generic.HashSet<object>(
            new ReferenceComparer());

        return FindInternal(root, flags, visited, 0);
    }

    private static byte[] FindInternal(
        object obj,
        System.Reflection.BindingFlags flags,
        System.Collections.Generic.HashSet<object> visited,
        int depth)
    {
        if (obj == null || depth > 2) return null;
        if (!visited.Add(obj)) return null;

        if (obj is byte[] direct && direct.Length >= 524288)
            return direct;

        Type type = obj.GetType();

        foreach (var field in type.GetFields(flags))
        {
            object value = null;

            try { value = field.GetValue(obj); }
            catch { }

            if (value is byte[] bytes && bytes.Length >= 524288)
                return bytes;
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
                continue;

            object value = null;

            try { value = property.GetValue(obj, null); }
            catch { }

            if (value is byte[] bytes && bytes.Length >= 524288)
                return bytes;
        }

        if (depth < 2)
        {
            foreach (var field in type.GetFields(flags))
            {
                object value = null;

                try { value = field.GetValue(obj); }
                catch { }

                if (value == null || value is string || value.GetType().IsValueType)
                    continue;

                byte[] found = FindInternal(value, flags, visited, depth + 1);
                if (found != null) return found;
            }
        }

        return null;
    }

    private sealed class ReferenceComparer :
        System.Collections.Generic.IEqualityComparer<object>
    {
        public new bool Equals(object x, object y)
        {
            return object.ReferenceEquals(x, y);
        }

        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}

internal sealed class TornekoDataWorkbenchForm :
    System.Windows.Forms.Form
{
    private readonly byte[] _data;
    private readonly System.Windows.Forms.TabControl _tabs;

    public TornekoDataWorkbenchForm(byte[] data)
    {
        if (data == null)
            throw new System.ArgumentNullException(nameof(data));

        _data = data;

        Text = "トルネコいじり - 統合データ編集";
        Width = 1050;
        Height = 760;
        StartPosition =
            System.Windows.Forms.FormStartPosition.CenterParent;

        _tabs = new System.Windows.Forms.TabControl();
        _tabs.Dock = System.Windows.Forms.DockStyle.Fill;

        Controls.Add(_tabs);

        AddPage(
            "フロア基本データ",
            EditorKind.Bytes,
            112,
            112,
            "112 bytesの基本データ。オフセットを指定して編集します。");

        AddPage(
            "罠出現率",
            EditorKind.Traps,
            112,
            40,
            "基本データの相対オフセット60から20種類のUInt16を編集します。");

        AddPage(
            "アイテム左側14項目",
            EditorKind.Bytes,
            28,
            28,
            "アイテム左側テーブル。14項目×2 bytes＝28 bytes。");

        AddPage(
            "アイテム右側740 bytes",
            EditorKind.Bytes,
            740,
            740,
            "アイテム右側の種類別・分類別データ。開始オフセットには768-byte領域の先頭+28を指定してください。");

        AddPage(
            "モンスター出現",
            EditorKind.Spawns,
            6,
            600,
            "6 bytes／レコード。ID・累積出現値・出現数を編集します。");
    }

    private enum EditorKind
    {
        Bytes,
        Traps,
        Spawns
    }

    private void AddPage(
        string title,
        EditorKind kind,
        int recordSize,
        int displaySize,
        string description)
    {
        var page = new System.Windows.Forms.TabPage(title);

        var top = new System.Windows.Forms.Panel();
        top.Dock = System.Windows.Forms.DockStyle.Top;
        top.Height = 88;

        var descriptionLabel = new System.Windows.Forms.Label();
        descriptionLabel.Text = description;
        descriptionLabel.Left = 12;
        descriptionLabel.Top = 8;
        descriptionLabel.Width = 950;
        descriptionLabel.Height = 24;

        var offsetLabel = new System.Windows.Forms.Label();
        offsetLabel.Text = "BINオフセット（10進数または0x16進数）";
        offsetLabel.Left = 12;
        offsetLabel.Top = 42;
        offsetLabel.Width = 260;

        var offsetBox = new System.Windows.Forms.TextBox();
        offsetBox.Left = 275;
        offsetBox.Top = 38;
        offsetBox.Width = 180;
        offsetBox.Text = "0";

        var reload = new System.Windows.Forms.Button();
        reload.Text = "読み込み";
        reload.Left = 470;
        reload.Top = 36;
        reload.Width = 100;

        var grid = new System.Windows.Forms.DataGridView();
        grid.Dock = System.Windows.Forms.DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode =
            System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode =
            System.Windows.Forms.DataGridViewSelectionMode.CellSelect;

        if (kind == EditorKind.Spawns)
        {
            grid.Columns.Add("Index", "レコード");
            grid.Columns.Add("MonsterId", "モンスターID");
            grid.Columns.Add("Cumulative", "累積出現値");
            grid.Columns.Add("Quantity", "出現数");
            grid.Columns.Add("Probability", "個別確率(%)");
            grid.Columns[0].ReadOnly = true;
            grid.Columns[4].ReadOnly = true;
        }
        else
        {
            grid.Columns.Add("Index", "相対位置");
            grid.Columns.Add("Value", "値");
            grid.Columns[0].ReadOnly = true;
        }

        var status = new System.Windows.Forms.Label();
        status.Dock = System.Windows.Forms.DockStyle.Bottom;
        status.Height = 26;
        status.Text = "編集内容はBIN編集バッファへ直接反映されます。";

        top.Controls.Add(descriptionLabel);
        top.Controls.Add(offsetLabel);
        top.Controls.Add(offsetBox);
        top.Controls.Add(reload);

        page.Controls.Add(grid);
        page.Controls.Add(status);
        page.Controls.Add(top);

        _tabs.TabPages.Add(page);

        System.Action load = () =>
        {
            try
            {
                int baseOffset = ParseOffset(offsetBox.Text);

                int actualOffset = baseOffset;
                int count = displaySize;

                if (kind == EditorKind.Traps)
                {
                    actualOffset = checked(baseOffset + 60);
                    count = 20;
                }

                int bytesNeeded =
                    kind == EditorKind.Spawns ? displaySize :
                    kind == EditorKind.Traps ? 40 :
                    count;

                if (actualOffset < 0 ||
                    actualOffset >= _data.Length ||
                    bytesNeeded > _data.Length - actualOffset)
                {
                    throw new System.ArgumentOutOfRangeException(
                        "指定した範囲がBINデータの範囲外です。");
                }

                grid.Rows.Clear();

                if (kind == EditorKind.Bytes)
                {
                    for (int i = 0; i < count; i++)
                    {
                        int pos = actualOffset + i;
                        grid.Rows.Add(
                            i.ToString(),
                            _data[pos].ToString());
                    }
                }
                else if (kind == EditorKind.Traps)
                {
                    for (int i = 0; i < 20; i++)
                    {
                        int pos = actualOffset + i * 2;
                        ushort value = ReadU16(pos);

                        grid.Rows.Add(
                            i.ToString() + " " + TrapNames[i],
                            value.ToString());
                    }
                }
                else
                {
                    int records = displaySize / 6;
                    int previousCumulative = 0;

                    for (int i = 0; i < records; i++)
                    {
                        int pos = actualOffset + i * 6;

                        ushort monsterId = ReadU16(pos);
                        ushort cumulative = ReadU16(pos + 2);
                        ushort quantity = ReadU16(pos + 4);

                        int individual = cumulative - previousCumulative;
                        double probability = individual / 10.0;

                        grid.Rows.Add(
                            i.ToString(),
                            monsterId.ToString(),
                            cumulative.ToString(),
                            quantity.ToString(),
                            probability.ToString("0.0") + "%");

                        previousCumulative = cumulative;
                    }
                }

                status.Text =
                    "読み込み完了。実データ位置: " +
                    actualOffset.ToString() +
                    " / 表示件数: " + grid.Rows.Count.ToString();
            }
            catch (System.Exception ex)
            {
                grid.Rows.Clear();
                status.Text = "読み込みエラー";
                System.Windows.Forms.MessageBox.Show(
                    ex.Message,
                    title);
            }
        };

        reload.Click += (sender, e) => load();

        grid.CellEndEdit += (sender, e) =>
        {
            try
            {
                if (e.RowIndex < 0 || e.ColumnIndex <= 0)
                    return;

                int baseOffset = ParseOffset(offsetBox.Text);
                int pos;

                if (kind == EditorKind.Bytes)
                {
                    pos = checked(baseOffset + e.RowIndex);

                    byte value = ParseByte(
                        System.Convert.ToString(
                            grid.Rows[e.RowIndex].Cells[1].Value));

                    _data[pos] = value;
                }
                else if (kind == EditorKind.Traps)
                {
                    pos = checked(baseOffset + 60 + e.RowIndex * 2);

                    ushort value = ParseU16(
                        System.Convert.ToString(
                            grid.Rows[e.RowIndex].Cells[1].Value));

                    WriteU16(pos, value);
                }
                else
                {
                    pos = checked(baseOffset + e.RowIndex * 6);

                    ushort id = ParseU16(
                        System.Convert.ToString(
                            grid.Rows[e.RowIndex].Cells[1].Value));

                    ushort cumulative = ParseU16(
                        System.Convert.ToString(
                            grid.Rows[e.RowIndex].Cells[2].Value));

                    ushort quantity = ParseU16(
                        System.Convert.ToString(
                            grid.Rows[e.RowIndex].Cells[3].Value));

                    WriteU16(pos, id);
                    WriteU16(pos + 2, cumulative);
                    WriteU16(pos + 4, quantity);

                    // 累積出現値から全レコードの個別確率を再計算
                    int previousCumulative = 0;

                    for (int i = 0; i < grid.Rows.Count; i++)
                    {
                        ushort current = ParseU16(
                            System.Convert.ToString(
                                grid.Rows[i].Cells[2].Value));

                        int difference = current - previousCumulative;

                        grid.Rows[i].Cells[4].Value =
                            (difference / 10.0).ToString("0.0") + "%";

                        previousCumulative = current;
                    }
                }

                status.Text =
                    "変更反映済み。BIN編集バッファへ書き込みました。";
            }
            catch (System.Exception ex)
            {
                status.Text = "入力値エラー";
                System.Windows.Forms.MessageBox.Show(
                    ex.Message,
                    "データ編集");
                load();
            }
        };

        load();
    }

    private static readonly string[] TrapNames =
    {
        "転石", "サビ", "装外", "泥罠", "召喚",
        "落穴", "バネ", "鉄球", "回転", "睡眠",
        "鈍足", "封印", "毒矢", "地雷", "大雷",
        "弱化", "落石", "木矢", "鉄矢", "モン化"
    };

    private int ParseOffset(string value)
    {
        value = (value ?? "").Trim();

        if (value.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
        {
            return System.Convert.ToInt32(
                value.Substring(2),
                16);
        }

        return System.Int32.Parse(
            value,
            System.Globalization.NumberStyles.Integer);
    }

    private static byte ParseByte(string value)
    {
        ushort number = ParseU16(value);

        if (number > 255)
            throw new System.ArgumentOutOfRangeException(
                "byteの値は0～255で入力してください。");

        return (byte)number;
    }

    private static ushort ParseU16(string value)
    {
        value = (value ?? "").Trim();

        if (value.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
        {
            return System.Convert.ToUInt16(
                value.Substring(2),
                16);
        }

        return System.UInt16.Parse(
            value,
            System.Globalization.NumberStyles.Integer);
    }

    private ushort ReadU16(int offset)
    {
        if (offset < 0 || offset + 2 > _data.Length)
            throw new System.ArgumentOutOfRangeException(nameof(offset));

        return (ushort)(
            _data[offset] |
            (_data[offset + 1] << 8));
    }

    private void WriteU16(int offset, ushort value)
    {
        if (offset < 0 || offset + 2 > _data.Length)
            throw new System.ArgumentOutOfRangeException(nameof(offset));

        _data[offset] = (byte)(value & 0xFF);
        _data[offset + 1] = (byte)(value >> 8);
    }
}

// ============================================================
// BINファイル拡張データ保護機能
// 未確定領域を解釈・再構成せず、ファイル全体を保持する。
// ============================================================

public sealed class TornekoBinPreservationSnapshot
{
    public byte[] Data { get; private set; }
    public string SourcePath { get; private set; }
    public int OriginalLength { get; private set; }
    public string OriginalSha256 { get; private set; }

    internal TornekoBinPreservationSnapshot(
        byte[] data,
        string sourcePath)
    {
        Data = (byte[])data.Clone();
        SourcePath = sourcePath;
        OriginalLength = data.Length;
        OriginalSha256 =
            TornekoBinPreservation.ComputeSha256(data);
    }

    public byte[] GetCopy()
    {
        return (byte[])Data.Clone();
    }
}

public static class TornekoBinPreservation
{
    public const int LegacyBaseRegionSize = 524288;
    public const int VersionMarkerOffset = 355056;
    public const int ExtensionStartOffset = 355072;

    public static TornekoBinPreservationSnapshot CreateInMemorySnapshot(
        byte[] data)
    {
        if (data == null)
            throw new System.ArgumentNullException(nameof(data));

        if (data.Length != LegacyBaseRegionSize)
            throw new System.ArgumentException(
                "メモリBINは524288 bytesである必要があります。");

        // 保存時の元ファイル判定用の仮想パス。
        // このパスにファイルを作成することはない。
        string virtualPath =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "PCSX2_VanillaMemory.bin");

        return new TornekoBinPreservationSnapshot(
            data,
            virtualPath);
    }

    public static TornekoBinPreservationSnapshot Load(
        string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new System.ArgumentException("ファイルパスが空です。");

        if (!System.IO.File.Exists(path))
            throw new System.IO.FileNotFoundException(
                "BINファイルが見つかりません。",
                path);

        byte[] data = System.IO.File.ReadAllBytes(path);

        return new TornekoBinPreservationSnapshot(
            data,
            System.IO.Path.GetFullPath(path));
    }

    public static string ComputeSha256(byte[] data)
    {
        if (data == null)
            throw new System.ArgumentNullException("data");

        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(data);
            return System.BitConverter.ToString(hash)
                .Replace("-", "");
        }
    }

    private static string GetHex(
        byte[] data,
        int offset,
        int count)
    {
        if (offset < 0 || count < 0 ||
            offset > data.Length ||
            count > data.Length - offset)
        {
            return "(範囲外)";
        }

        if (count == 0)
            return "(データなし)";

        byte[] part = new byte[count];
        System.Buffer.BlockCopy(
            data, offset, part, 0, count);

        return System.BitConverter.ToString(part)
            .Replace("-", " ");
    }

    public static string CreateReport(
        TornekoBinPreservationSnapshot snapshot)
    {
        if (snapshot == null)
            throw new System.ArgumentNullException("snapshot");

        byte[] data = snapshot.Data;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("========================================");
        sb.AppendLine("BINファイル保護診断");
        sb.AppendLine("========================================");
        sb.AppendLine("ファイル: " + snapshot.SourcePath);
        sb.AppendLine("実ファイルサイズ: " + data.Length + " bytes");
        sb.AppendLine("SHA256: " + snapshot.OriginalSha256);
        sb.AppendLine("");

        sb.AppendLine(
            "524288 bytes基本領域: " +
            (data.Length >= LegacyBaseRegionSize
                ? "存在"
                : "サイズ不足"));

        sb.AppendLine(
            "基本領域サイズとの差: " +
            (data.Length - LegacyBaseRegionSize) +
            " bytes");

        sb.AppendLine(
            "ファイル末尾の追加データ: " +
            (data.Length > LegacyBaseRegionSize
                ? (data.Length - LegacyBaseRegionSize) + " bytes"
                : "なし"));

        sb.AppendLine("");
        sb.AppendLine(
            "バージョンマーカー位置: " +
            VersionMarkerOffset);

        sb.AppendLine(
            "マーカー周辺4 bytes: " +
            GetHex(data, VersionMarkerOffset, 4));

        sb.AppendLine(
            "拡張開始候補位置: " +
            ExtensionStartOffset);

        sb.AppendLine(
            "拡張開始候補周辺16 bytes: " +
            GetHex(data, ExtensionStartOffset, 16));

        sb.AppendLine("");
        sb.AppendLine(
            "注意: マーカー値や拡張領域の内部構造は未確定です。");

        sb.AppendLine(
            "この診断機能はデータの意味を推測して変更しません。");

        sb.AppendLine("========================================");

        return sb.ToString();
    }

    public static void SaveCopy(
        TornekoBinPreservationSnapshot snapshot,
        string destinationPath)
    {
        if (snapshot == null)
            throw new System.ArgumentNullException("snapshot");

        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new System.ArgumentException("保存先が空です。");

        string fullPath =
            System.IO.Path.GetFullPath(destinationPath);

        string directory =
            System.IO.Path.GetDirectoryName(fullPath);

        if (!System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);

        byte[] output = snapshot.GetCopy();

        // 元ファイルには触れず、指定先に完全なコピーを作成する。
        System.IO.File.WriteAllBytes(fullPath, output);

        byte[] check = System.IO.File.ReadAllBytes(fullPath);

        if (check.Length != snapshot.OriginalLength)
            throw new System.IO.IOException(
                "保存後のファイルサイズが一致しません。");

        string actualHash = ComputeSha256(check);

        if (!string.Equals(
            actualHash,
            snapshot.OriginalSha256,
            System.StringComparison.OrdinalIgnoreCase))
        {
            throw new System.IO.IOException(
                "保存後のSHA256が一致しません。");
        }
    }

    public static bool VerifyUnchanged(
        TornekoBinPreservationSnapshot snapshot)
    {
        if (snapshot == null || snapshot.Data == null)
            return false;

        if (snapshot.Data.Length != snapshot.OriginalLength)
            return false;

        return string.Equals(
            ComputeSha256(snapshot.Data),
            snapshot.OriginalSha256,
            System.StringComparison.OrdinalIgnoreCase);
    }
}
// ============================================================
// BIN編集保存統合補助
// 基本領域のみを置き換え、拡張領域は元データを維持する。
// 元ファイルへの上書きは禁止。
// ============================================================

public static class TornekoBinEditPreserver
{
    public const int BaseRegionSize = 524288;

    public static void SaveEditedBaseCopy(
        TornekoBinPreservationSnapshot snapshot,
        byte[] editedBaseRegion,
        string destinationPath)
    {
        if (snapshot == null)
            throw new System.ArgumentNullException("snapshot");

        if (editedBaseRegion == null)
            throw new System.ArgumentNullException("editedBaseRegion");

        if (editedBaseRegion.Length != BaseRegionSize)
            throw new System.ArgumentException(
                "編集済み基本領域は524288 bytesである必要があります。");

        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new System.ArgumentException("保存先が空です。");

        string fullDestination =
            System.IO.Path.GetFullPath(destinationPath);

        string fullSource =
            System.IO.Path.GetFullPath(snapshot.SourcePath);

        if (string.Equals(
            fullDestination,
            fullSource,
            System.StringComparison.OrdinalIgnoreCase))
        {
            throw new System.InvalidOperationException(
                "元BINファイルへの上書きは禁止されています。別名で保存してください。");
        }

        // スナップショットを複製し、元データの完全性を確認する。
        byte[] original = snapshot.GetCopy();

        if (original.Length != snapshot.OriginalLength)
            throw new System.InvalidOperationException(
                "元データのサイズが変化しています。");

        string originalHash =
            TornekoBinPreservation.ComputeSha256(original);

        if (!string.Equals(
            originalHash,
            snapshot.OriginalSha256,
            System.StringComparison.OrdinalIgnoreCase))
        {
            throw new System.InvalidOperationException(
                "元データが変更されています。安全のため保存を中止しました。");
        }

        if (original.Length < BaseRegionSize)
            throw new System.InvalidOperationException(
                "元BINが基本領域524288 bytesを満たしていません。");

        // 元BIN全体を複製して、基本領域だけ置換する。
        byte[] output = (byte[])original.Clone();

        System.Buffer.BlockCopy(
            editedBaseRegion,
            0,
            output,
            0,
            BaseRegionSize);

        string directory =
            System.IO.Path.GetDirectoryName(fullDestination);

        if (!System.IO.Directory.Exists(directory))
            System.IO.Directory.CreateDirectory(directory);

        System.IO.File.WriteAllBytes(fullDestination, output);

        // 保存結果を読み直して検査する。
        byte[] check =
            System.IO.File.ReadAllBytes(fullDestination);

        if (check.Length != original.Length)
            throw new System.IO.IOException(
                "保存後のファイルサイズが元BINと一致しません。");

        for (int i = 0; i < BaseRegionSize; i++)
        {
            if (check[i] != editedBaseRegion[i])
                throw new System.IO.IOException(
                    "基本領域の保存検査に失敗しました。位置: " + i);
        }

        // 基本領域より後ろは全バイトが元データと一致する必要がある。
        for (int i = BaseRegionSize; i < original.Length; i++)
        {
            if (check[i] != original[i])
                throw new System.IO.IOException(
                    "拡張領域の保存検査に失敗しました。位置: " + i);
        }

        string resultHash =
            TornekoBinPreservation.ComputeSha256(check);

        System.Diagnostics.Debug.WriteLine(
            "BIN編集保存成功: " + fullDestination);

        System.Diagnostics.Debug.WriteLine(
            "保存サイズ: " + check.Length);

        System.Diagnostics.Debug.WriteLine(
            "保存SHA256: " + resultHash);
    }

    public static byte[] ExtractBaseRegion(
        TornekoBinPreservationSnapshot snapshot)
    {
        if (snapshot == null)
            throw new System.ArgumentNullException("snapshot");

        byte[] original = snapshot.GetCopy();

        if (original.Length < BaseRegionSize)
            throw new System.InvalidOperationException(
                "基本領域524288 bytesが存在しません。");

        byte[] result = new byte[BaseRegionSize];

        System.Buffer.BlockCopy(
            original,
            0,
            result,
            0,
            BaseRegionSize);

        return result;
    }

    public static byte[] ExtractExtensionRegion(
        TornekoBinPreservationSnapshot snapshot)
    {
        if (snapshot == null)
            throw new System.ArgumentNullException("snapshot");

        byte[] original = snapshot.GetCopy();

        if (original.Length <= BaseRegionSize)
            return new byte[0];

        int extensionLength =
            original.Length - BaseRegionSize;

        byte[] result = new byte[extensionLength];

        System.Buffer.BlockCopy(
            original,
            BaseRegionSize,
            result,
            0,
            extensionLength);

        return result;
    }
}