using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TornekoIjiriPort;

internal static class CoromageCallsiteTrace
{
    private const uint HookAddress = 0x002D042Cu;
    private const uint OriginalInstruction = 0x00C0802Du;
    private const uint OriginalDelayInstruction = 0xFFB10068u;

    private const uint ReturnAddress = 0x002D0434u;

    private const uint CaveSize = 0x600u;
    private const uint CounterOffset = 0x3F0u;
    private const uint RecordOffset = 0x400u;
    private const uint RecordSize = 0x20u;
    private const uint RecordCount = 16u;

    private static string MetadataPath
    {
        get
        {
            string desktop = Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

            return Path.Combine(
                desktop,
                "CoromageActionTrace_Location.txt");
        }
    }

    private static void RequirePaused()
    {
        uint status = ActionAutoRestoreService.GetSharedStatus();

        if (status != 1u)
        {
            throw new InvalidOperationException(
                "PCSX2が一時停止中ではありません。");
        }
    }

    private static uint MakeJump(uint address)
    {
        return 0x08000000u |
            ((address >> 2) & 0x03FFFFFFu);
    }

    private static uint[] BuildCode(uint cave)
    {
        uint high = (cave >> 16) & 0xFFFFu;
        uint counterLow = (cave + CounterOffset) & 0xFFFFu;
        uint recordLow = (cave + RecordOffset) & 0xFFFFu;

        uint[] words =
        {
            0x3C080000u,
            0x35080000u,
            0x8D090000u,
            0x25290001u,
            0xAD090000u,
            0x3129000Fu,
            0x00094940u,
            0x3C080000u,
            0x35080000u,
            0x01094021u,
            0xAD040000u,
            0xAD050004u,
            0xAD060008u,
            0xAD07000Cu,
            0xAD1F0010u,
            0xAD100014u,
            0xAD1D0018u,
            0xAD09001Cu,
            0x00C0802Du,
            0x08000000u,
            0x00000000u
        };

        words[0] = 0x3C080000u | high;
        words[1] = 0x35080000u | counterLow;
        words[7] = 0x3C080000u | high;
        words[8] = 0x35080000u | recordLow;
        words[19] = MakeJump(ReturnAddress);

        return words;
    }

    private static uint FindFreeCave()
    {
        for (uint candidate = 0x01FF0000u;
             candidate >= 0x01C00000u;
             candidate -= 0x00010000u)
        {
            bool free = true;

            for (uint offset = 0; offset < CaveSize; offset += 4u)
            {
                uint value = ActionAutoRestoreService.ReadShared32(
                    candidate + offset);

                if (value != 0u)
                {
                    free = false;
                    break;
                }
            }

            if (free)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "EE RAM内に0x600バイトの空き領域がありません。");
    }

    private static uint ReadMetadataCave()
    {
        string path = MetadataPath;

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "記録先アドレスファイルがありません。");
        }

        string value = File.ReadAllText(path).Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(2);
        }

        uint cave;

        if (!uint.TryParse(
            value,
            System.Globalization.NumberStyles.HexNumber,
            null,
            out cave))
        {
            throw new InvalidOperationException(
                "記録先アドレスファイルの内容が不正です。");
        }

        return cave;
    }

    private static void VerifyCave(uint cave)
    {
        uint[] expected = BuildCode(cave);

        for (int i = 0; i < expected.Length; i++)
        {
            uint actual = ActionAutoRestoreService.ReadShared32(
                cave + (uint)(i * 4));

            if (actual != expected[i])
            {
                throw new InvalidOperationException(
                    "記録用コードの確認に失敗しました。");
            }
        }
    }

    public static string Install()
    {
        RequirePaused();

        uint original = ActionAutoRestoreService.ReadShared32(
            HookAddress);

        uint delay = ActionAutoRestoreService.ReadShared32(
            HookAddress + 4u);

        if (original != OriginalInstruction)
        {
            throw new InvalidOperationException(
                "入口命令が想定値と異なります。実測値=0x" +
                original.ToString("X8"));
        }

        if (delay != OriginalDelayInstruction)
        {
            throw new InvalidOperationException(
                "遅延スロットが想定値と異なります。実測値=0x" +
                delay.ToString("X8"));
        }

        uint cave = FindFreeCave();
        uint[] code = BuildCode(cave);

        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4),
                code[i]);
        }

        ActionAutoRestoreService.WriteShared32(
            cave + CounterOffset,
            0u);

        VerifyCave(cave);

        File.WriteAllText(
            MetadataPath,
            cave.ToString("X8"),
            new UTF8Encoding(false));

        uint hookInstruction = MakeJump(cave);

        ActionAutoRestoreService.WriteShared32(
            HookAddress,
            hookInstruction);

        uint installed = ActionAutoRestoreService.ReadShared32(
            HookAddress);

        uint delayAfter = ActionAutoRestoreService.ReadShared32(
            HookAddress + 4u);

        if (installed != hookInstruction)
        {
            throw new InvalidOperationException(
                "フック命令の設置確認に失敗しました。");
        }

        if (delayAfter != OriginalDelayInstruction)
        {
            throw new InvalidOperationException(
                "遅延スロットの維持に失敗しました。");
        }

        return
            "行動判定記録フックを設置しました。\r\n" +
            "Hook: 0x" + HookAddress.ToString("X8") + "\r\n" +
            "Cave: 0x" + cave.ToString("X8") + "\r\n" +
            "Return: 0x" + ReturnAddress.ToString("X8") + "\r\n" +
            "Record count: 16\r\n" +
            "記録先: " + MetadataPath;
    }

    public static string Read()
    {
        RequirePaused();

        uint cave = ReadMetadataCave();
        VerifyCave(cave);

        uint count = ActionAutoRestoreService.ReadShared32(
            cave + CounterOffset);

        uint available = count < RecordCount ? count : RecordCount;
        uint first = count - available;

        StringBuilder report = new StringBuilder();

        report.AppendLine("COROMAGE ACTION DISPATCH TRACE");
        report.AppendLine("READ ONLY");
        report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("Hook: 0x" + HookAddress.ToString("X8"));
        report.AppendLine("Cave: 0x" + cave.ToString("X8"));
        report.AppendLine("Total calls: " + count);
        report.AppendLine("Stored records: " + available);
        report.AppendLine();

        for (uint i = 0; i < available; i++)
        {
            uint slot = (first + i) & 15u;
            uint address = cave + RecordOffset + slot * RecordSize;

            uint a0 = ActionAutoRestoreService.ReadShared32(address);
            uint a1 = ActionAutoRestoreService.ReadShared32(address + 4u);
            uint a2 = ActionAutoRestoreService.ReadShared32(address + 8u);
            uint a3 = ActionAutoRestoreService.ReadShared32(address + 12u);
            uint ra = ActionAutoRestoreService.ReadShared32(address + 16u);
            uint s0 = ActionAutoRestoreService.ReadShared32(address + 20u);
            uint sp = ActionAutoRestoreService.ReadShared32(address + 24u);

            report.AppendLine("===== CALL " + (first + i + 1u) + " =====");
            report.AppendLine("a0 = 0x" + a0.ToString("X8"));
            report.AppendLine("a1 = 0x" + a1.ToString("X8"));
            report.AppendLine("a2 = 0x" + a2.ToString("X8"));
            report.AppendLine("a3 = 0x" + a3.ToString("X8"));
            report.AppendLine("ra = 0x" + ra.ToString("X8"));
            report.AppendLine("s0 before move = 0x" + s0.ToString("X8"));
            report.AppendLine("sp = 0x" + sp.ToString("X8"));

            if (a2 >= 0x00000000u && a2 <= 0x01FFFFC4u)
            {
                uint field38 = ActionAutoRestoreService.ReadShared32(
                    a2 + 0x38u);

                uint field10 = ActionAutoRestoreService.ReadShared32(
                    a2 + 0x10u);

                byte value3A = (byte)((field38 >> 16) & 0xFFu);
                byte value10 = (byte)(field10 & 0xFFu);

                report.AppendLine("struct +0x3A = 0x" + value3A.ToString("X2"));
                report.AppendLine("struct +0x10 = 0x" + value10.ToString("X2"));
                report.AppendLine("struct +0x38 word = 0x" + field38.ToString("X8"));
            }
            else
            {
                report.AppendLine("struct fields = invalid pointer");
            }

            report.AppendLine();
        }

        if (count == 0u)
        {
            report.AppendLine("No calls captured.");
        }

        return report.ToString();
    }

    public static string Restore()
    {
        RequirePaused();

        uint cave = ReadMetadataCave();
        uint current = ActionAutoRestoreService.ReadShared32(
            HookAddress);

        uint expectedHook = MakeJump(cave);

        if (current != expectedHook)
        {
            throw new InvalidOperationException(
                "現在の命令がこのフックと一致しません。実測値=0x" +
                current.ToString("X8"));
        }

        ActionAutoRestoreService.WriteShared32(
            HookAddress,
            OriginalInstruction);

        uint restored = ActionAutoRestoreService.ReadShared32(
            HookAddress);

        if (restored != OriginalInstruction)
        {
            throw new InvalidOperationException(
                "元の命令への復元確認に失敗しました。");
        }

        return
            "元の命令に復元しました。\r\n" +
            "Address: 0x" + HookAddress.ToString("X8") + "\r\n" +
            "Cave: 0x" + cave.ToString("X8") + "\r\n" +
            "記録データは保持しています。";
    }
}