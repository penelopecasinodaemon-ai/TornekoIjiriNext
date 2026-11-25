using System;
using System.IO;
using System.Text;
using System.Globalization;

namespace TornekoIjiriPort;

internal static class CoromageCallsiteTrace
{
    private const uint HookAddress = 0x002D042Cu;
    private const uint OriginalInstruction = 0x00C0802Du;
    private const uint OriginalDelayInstruction = 0xFFB10068u;
    private const uint ReturnAddress = 0x002D0434u;

    private const uint CaveStart = 0x01FF0000u;
    private const uint CaveEnd = 0x01C00000u;
    private const uint CaveStep = 0x00010000u;
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
        if (ActionAutoRestoreService.GetSharedStatus() != 1u)
        {
            throw new InvalidOperationException(
                "PCSX2を一時停止してから操作してください。");
        }
    }

    private static uint EncodeJump(
        uint instructionAddress,
        uint target)
    {
        if (((instructionAddress + 4u) & 0xF0000000u) !=
            (target & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "J命令の到達範囲外です。");
        }

        return 0x08000000u |
            ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJump(
        uint instructionAddress,
        uint instruction)
    {
        return
            (((instructionAddress + 4u) & 0xF0000000u) |
            ((instruction & 0x03FFFFFFu) << 2));
    }

    private static bool IsJumpTo(
        uint instruction,
        uint instructionAddress,
        uint target)
    {
        if ((instruction >> 26) != 2u)
            return false;

        return DecodeJump(
            instructionAddress,
            instruction) == target;
    }

    private static uint[] BuildCode(uint cave)
    {
        uint high = (cave >> 16) & 0xFFFFu;

        uint counterLow =
            (cave + CounterOffset) & 0xFFFFu;

        uint recordLow =
            (cave + RecordOffset) & 0xFFFFu;

        uint[] words =
        {
            // SPを32バイト確保
            0x27BDFFE0u,

            // 元の一時レジスタを退避
            0xFFA80000u, // sd t0,0(sp)
            0xFFA90008u, // sd t1,8(sp)
            0xFFBA0010u, // sd k0,16(sp)
            0xFFBB0018u, // sd k1,24(sp)

            // カウンターアドレス
            0x3C1A0000u,
            0x375A0000u,

            // カウンター加算
            0x8F5B0000u,
            0x277B0001u,
            0xAF5B0000u,

            // 16件リングバッファ
            0x337B000Fu,
            0x001BD940u,

            // 記録領域アドレス
            0x3C1A0000u,
            0x375A0000u,
            0x035BD021u,

            // 引数と保存レジスタを記録
            0xAF440000u, // sw a0,0(k0)
            0xAF450004u, // sw a1,4(k0)
            0xAF460008u, // sw a2,8(k0)
            0xAF47000Cu, // sw a3,12(k0)
            0xAF5F0010u, // sw ra,16(k0)
            0xAF500014u, // sw s0,20(k0)

            // 元のSPを記録
            0x27BB0020u,
            0xAF5B0018u,

            // 元のt0を記録
            0xDFBB0000u,
            0xAF5B001Cu,

            // 全レジスタを復元
            0xDFA80000u,
            0xDFA90008u,
            0xDFBA0010u,
            0xDFBB0018u,

            // SPを復元
            0x27BD0020u,

            // 元の命令を実行
            0x00C0802Du,

            // 元の処理へ戻る
            0x08000000u,
            0x00000000u
        };

        words[5] =
            0x3C1A0000u | high;

        words[6] =
            0x375A0000u | counterLow;

        words[12] =
            0x3C1A0000u | high;

        words[13] =
            0x375A0000u | recordLow;

        words[31] = EncodeJump(
            cave + 31u * 4u,
            ReturnAddress);

        return words;
    }

    private static bool IsZeroRange(
        uint address,
        uint length)
    {
        for (uint offset = 0; offset < length; offset += 4u)
        {
            if (ActionAutoRestoreService.ReadShared32(
                address + offset) != 0u)
            {
                return false;
            }
        }

        return true;
    }

    private static uint FindFreeCave()
    {
        for (uint candidate = CaveStart;
             candidate >= CaveEnd;
             candidate -= CaveStep)
        {
            if (IsZeroRange(candidate, CaveSize))
                return candidate;
        }

        throw new InvalidOperationException(
            "EE RAM内に0x600バイトの空き領域がありません。");
    }

    private static bool IsCode(uint cave)
    {
        uint[] expected = BuildCode(cave);

        for (int i = 0; i < expected.Length; i++)
        {
            uint actual =
                ActionAutoRestoreService.ReadShared32(
                    cave + (uint)(i * 4));

            if (actual != expected[i])
                return false;
        }

        return true;
    }

    private static void WriteCode(uint cave)
    {
        uint[] code = BuildCode(cave);

        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4),
                code[i]);
        }

        if (!IsCode(cave))
        {
            throw new InvalidOperationException(
                "トレースコードの書き込み検証に失敗しました。");
        }
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

        if (value.StartsWith(
            "0x",
            StringComparison.OrdinalIgnoreCase))
        {
            value = value.Substring(2);
        }

        if (!uint.TryParse(
            value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out uint cave))
        {
            throw new InvalidOperationException(
                "記録先アドレスファイルの内容が不正です。");
        }

        return cave;
    }

    private static void WriteMetadataCave(uint cave)
    {
        File.WriteAllText(
            MetadataPath,
            cave.ToString("X8"),
            new UTF8Encoding(false));
    }

    public static string Install()
    {
        RequirePaused();

        uint current =
            ActionAutoRestoreService.ReadShared32(HookAddress);

        uint delay =
            ActionAutoRestoreService.ReadShared32(HookAddress + 4u);

        // 既に設置されている場合
        if ((current >> 26) == 2u)
        {
            uint existing =
                DecodeJump(HookAddress, current);

            if (existing >= CaveEnd &&
                existing <= CaveStart &&
                delay == OriginalDelayInstruction &&
                IsCode(existing))
            {
                WriteMetadataCave(existing);

                return
                    "実行入口トレースは既に設置されています。\n" +
                    "コード領域: 0x" + existing.ToString("X8");
            }

            throw new InvalidOperationException(
                "既存のJフックを検証できません。");
        }

        if (current != OriginalInstruction)
        {
            throw new InvalidOperationException(
                "0x002D042Cの元の命令が想定値と異なります。\n" +
                "現在値: 0x" + current.ToString("X8"));
        }

        if (delay != OriginalDelayInstruction)
        {
            throw new InvalidOperationException(
                "0x002D0430の遅延スロットが想定値と異なります。\n" +
                "現在値: 0x" + delay.ToString("X8"));
        }

        uint cave = FindFreeCave();

        WriteCode(cave);

        ActionAutoRestoreService.WriteShared32(
            cave + CounterOffset,
            0u);

        WriteMetadataCave(cave);

        uint jump = EncodeJump(HookAddress, cave);

        ActionAutoRestoreService.WriteShared32(
            HookAddress,
            jump);

        uint verify =
            ActionAutoRestoreService.ReadShared32(HookAddress);

        uint verifyDelay =
            ActionAutoRestoreService.ReadShared32(
                HookAddress + 4u);

        if (verify != jump ||
            verifyDelay != OriginalDelayInstruction)
        {
            ActionAutoRestoreService.WriteShared32(
                HookAddress,
                OriginalInstruction);

            throw new InvalidOperationException(
                "フックの設置確認に失敗しました。元の命令へ戻しました。");
        }

        return
            "実行入口トレースを設置しました。\n" +
            "Hook: 0x" + HookAddress.ToString("X8") + "\n" +
            "Cave: 0x" + cave.ToString("X8") + "\n" +
            "Return: 0x" + ReturnAddress.ToString("X8") + "\n" +
            "Record count: 16\n\n" +
            "PCSX2を再開して味方モンスターに特技を使わせてください。";
    }

    public static string Read()
    {
        RequirePaused();

        uint cave = ReadMetadataCave();

        if (cave < CaveEnd ||
            cave > CaveStart ||
            !IsCode(cave))
        {
            throw new InvalidOperationException(
                "記録用コード領域を検証できません。");
        }

        uint current =
            ActionAutoRestoreService.ReadShared32(HookAddress);

        uint delay =
            ActionAutoRestoreService.ReadShared32(
                HookAddress + 4u);

        if (current != OriginalInstruction &&
            !IsJumpTo(current, HookAddress, cave))
        {
            throw new InvalidOperationException(
                "現在のフック命令が記録先と一致しません。");
        }

        if (delay != OriginalDelayInstruction)
        {
            throw new InvalidOperationException(
                "遅延スロットが変更されています。");
        }

        uint count =
            ActionAutoRestoreService.ReadShared32(
                cave + CounterOffset);

        uint available =
            count < RecordCount ? count : RecordCount;

        uint first = count - available;

        StringBuilder report = new StringBuilder();

        report.AppendLine("RUNTIME ACTION ENTRY TRACE");
        report.AppendLine("READ ONLY");
        report.AppendLine(
            "Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine(
            "Hook: 0x" + HookAddress.ToString("X8"));
        report.AppendLine(
            "Cave: 0x" + cave.ToString("X8"));
        report.AppendLine("Total calls: " + count);
        report.AppendLine("Stored records: " + available);
        report.AppendLine();

        for (uint i = 0; i < available; i++)
        {
            uint slot = (first + i) & 15u;

            uint address =
                cave + RecordOffset + slot * RecordSize;

            uint a0 =
                ActionAutoRestoreService.ReadShared32(address);

            uint a1 =
                ActionAutoRestoreService.ReadShared32(address + 4u);

            uint a2 =
                ActionAutoRestoreService.ReadShared32(address + 8u);

            uint a3 =
                ActionAutoRestoreService.ReadShared32(address + 12u);

            uint ra =
                ActionAutoRestoreService.ReadShared32(address + 16u);

            uint s0 =
                ActionAutoRestoreService.ReadShared32(address + 20u);

            uint sp =
                ActionAutoRestoreService.ReadShared32(address + 24u);

            uint t0 =
                ActionAutoRestoreService.ReadShared32(address + 28u);

            report.AppendLine(
                "===== CALL " + (first + i + 1u) + " =====");

            report.AppendLine("a0 = 0x" + a0.ToString("X8"));
            report.AppendLine("a1 = 0x" + a1.ToString("X8"));
            report.AppendLine("a2 = 0x" + a2.ToString("X8"));
            report.AppendLine("a3 = 0x" + a3.ToString("X8"));
            report.AppendLine("t0 = 0x" + t0.ToString("X8"));
            report.AppendLine("ra = 0x" + ra.ToString("X8"));
            report.AppendLine("s0 before move = 0x" + s0.ToString("X8"));
            report.AppendLine("sp = 0x" + sp.ToString("X8"));

            if (a2 <= 0x01FFFFC4u)
            {
                uint field38 =
                    ActionAutoRestoreService.ReadShared32(
                        a2 + 0x38u);

                uint field10 =
                    ActionAutoRestoreService.ReadShared32(
                        a2 + 0x10u);

                byte value3A =
                    (byte)((field38 >> 16) & 0xFFu);

                byte value10 =
                    (byte)(field10 & 0xFFu);

                report.AppendLine(
                    "struct +0x3A = 0x" + value3A.ToString("X2"));

                report.AppendLine(
                    "struct +0x10 = 0x" + value10.ToString("X2"));

                report.AppendLine(
                    "struct +0x38 = 0x" + field38.ToString("X8"));
            }
            else
            {
                report.AppendLine("struct fields = invalid pointer");
            }

            report.AppendLine();
        }

        if (count == 0u)
            report.AppendLine("No calls captured.");

        return report.ToString();
    }

    public static string Restore()
    {
        RequirePaused();

        uint current =
            ActionAutoRestoreService.ReadShared32(HookAddress);

        if (current == OriginalInstruction)
        {
            return "実行入口トレースは既に解除されています。";
        }

        uint cave = ReadMetadataCave();

        if (cave < CaveEnd ||
            cave > CaveStart ||
            !IsCode(cave))
        {
            throw new InvalidOperationException(
                "コード領域を検証できないため解除を中止しました。");
        }

        uint delay =
            ActionAutoRestoreService.ReadShared32(
                HookAddress + 4u);

        if (!IsJumpTo(current, HookAddress, cave))
        {
            throw new InvalidOperationException(
                "現在のフック命令が一致しません。");
        }

        if (delay != OriginalDelayInstruction)
        {
            throw new InvalidOperationException(
                "遅延スロットが変更されています。解除を中止しました。");
        }

        ActionAutoRestoreService.WriteShared32(
            HookAddress,
            OriginalInstruction);

        if (ActionAutoRestoreService.ReadShared32(HookAddress) !=
            OriginalInstruction)
        {
            throw new InvalidOperationException(
                "元の命令への復元に失敗しました。");
        }

        return
            "実行入口トレースを解除しました。\n" +
            "元の命令を復元しました。\n" +
            "記録データは保持しています。";
    }
}