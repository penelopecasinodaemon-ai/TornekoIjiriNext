using System;
using System.Text;

namespace TornekoIjiriPort;

internal static class CoromageFunctionTrace
{
    private const uint EntryAddress = 0x00252508u;
    private const uint OriginalFirst = 0x27BDFFC0u;
    private const uint OriginalSecond = 0xFFB00010u;

    private const uint CaveSearchStart = 0x01FF0000u;
    private const uint CaveSearchEnd = 0x01C00000u;
    private const uint CaveSearchStep = 0x00010000u;

    private const uint DataOffset = 0x100u;
    private const uint RecordOffset = 0x40u;
    private const uint RecordSize = 0x40u;
    private const int RecordCount = 16;
    private const int RecordWords = 13;

    private static uint _caveAddress;

    private static uint DataAddress
    {
        get { return _caveAddress + DataOffset; }
    }

    private static void RequirePaused()
    {
        uint status = ActionAutoRestoreService.GetSharedStatus();

        if (status != 1u)
        {
            throw new InvalidOperationException(
                "PCSX2を一時停止してから操作してください。");
        }
    }

    private static uint EncodeJump(uint address, uint target)
    {
        if (((address + 4u) & 0xF0000000u) != (target & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "フック先アドレスがJ命令の到達範囲外です。");
        }

        return 0x08000000u | ((target >> 2) & 0x03FFFFFFu);
    }

    private static bool IsJumpTo(uint instruction, uint address, uint target)
    {
        if ((instruction >> 26) != 2u)
        {
            return false;
        }

        uint actual =
            (((address + 4u) & 0xF0000000u) |
            ((instruction & 0x03FFFFFFu) << 2));

        return actual == target;
    }

    private static bool IsZeroRange(uint address, uint length)
    {
        for (uint offset = 0; offset < length; offset += 4u)
        {
            if (ActionAutoRestoreService.ReadShared32(address + offset) != 0u)
            {
                return false;
            }
        }

        return true;
    }

    private static uint FindCave()
    {
        for (uint address = CaveSearchStart; ; address -= CaveSearchStep)
        {
            bool candidate = true;

            for (uint offset = 0; offset < 32u; offset += 4u)
            {
                if (ActionAutoRestoreService.ReadShared32(address + offset) != 0u)
                {
                    candidate = false;
                    break;
                }
            }

            if (candidate && IsZeroRange(address, 0x600u))
            {
                return address;
            }

            if (address == CaveSearchEnd)
            {
                break;
            }
        }

        throw new InvalidOperationException(
            "0x01D00000～0x01DF0000に使用可能なコード領域がありません。");
    }

    private static uint[] BuildCode(uint caveAddress)
    {
        uint dataAddress = caveAddress + DataOffset;

        return new uint[]
        {
            0x27BDFFF0u,
            0xAFA80000u,
            0xAFA90004u,
            0xAFBA0008u,
            0xAFBB000Cu,

            0x3C1A0000u | ((dataAddress >> 16) & 0xFFFFu),
            0x375A0000u | (dataAddress & 0xFFFFu),

            0x8F5B0000u,
            0x277B0001u,
            0xAF5B0000u,

            0x3368000Fu,
            0x00084180u,
            0x0348D021u,

            0xAF440000u,
            0xAF450004u,
            0xAF460008u,
            0xAF47000Cu,

            0xAF500010u,
            0xAF510014u,
            0xAF520018u,
            0xAF53001Cu,
            0xAF540020u,
            0xAF550024u,
            0xAF560028u,
            0xAF57002Cu,

            0xAF5F0030u,

            0x8FA80000u,
            0x8FA90004u,
            0x8FBA0008u,
            0x8FBB000Cu,
            0x27BD0010u,

            OriginalFirst,
            OriginalSecond,

            EncodeJump(caveAddress + 136u, EntryAddress + 8u),
            0x00000000u
        };
    }

    private static void WriteCode(uint address, uint[] code)
    {
        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                address + (uint)(i * 4),
                code[i]);
        }

        for (int i = 0; i < code.Length; i++)
        {
            uint actual = ActionAutoRestoreService.ReadShared32(
                address + (uint)(i * 4));

            if (actual != code[i])
            {
                throw new InvalidOperationException(
                    "コード領域の書き込み検証に失敗しました。位置: " +
                    (address + (uint)(i * 4)).ToString("X8"));
            }
        }
    }

    public static string Install()
    {
        RequirePaused();

        uint currentFirst =
            ActionAutoRestoreService.ReadShared32(EntryAddress);

        uint currentSecond =
            ActionAutoRestoreService.ReadShared32(EntryAddress + 4u);

        if (_caveAddress != 0u &&
            IsJumpTo(currentFirst, EntryAddress, _caveAddress) &&
            currentSecond == 0u)
        {
            return "関数入口フックは設置済みです。\r\nコード領域: 0x" +
                _caveAddress.ToString("X8");
        }

        if (currentFirst != OriginalFirst ||
            currentSecond != OriginalSecond)
        {
            throw new InvalidOperationException(
                "関数入口の命令が想定値と異なります。\r\n" +
                "先頭: 0x" + currentFirst.ToString("X8") +
                "\r\n2番目: 0x" + currentSecond.ToString("X8") +
                "\r\n書き込みは行っていません。");
        }

        uint cave = FindCave();
        uint[] code = BuildCode(cave);

        uint dataAddress = cave + DataOffset;

        for (uint offset = 0; offset < 0x440u; offset += 4u)
        {
            ActionAutoRestoreService.WriteShared32(
                dataAddress + offset,
                0u);
        }

        WriteCode(cave, code);

        uint jump = EncodeJump(EntryAddress, cave);

        try
        {
            ActionAutoRestoreService.WriteShared32(EntryAddress, jump);
            ActionAutoRestoreService.WriteShared32(EntryAddress + 4u, 0u);

            uint verifyFirst =
                ActionAutoRestoreService.ReadShared32(EntryAddress);

            uint verifySecond =
                ActionAutoRestoreService.ReadShared32(EntryAddress + 4u);

            if (!IsJumpTo(verifyFirst, EntryAddress, cave) ||
                verifySecond != 0u)
            {
                throw new InvalidOperationException(
                    "関数入口フックの書き込み検証に失敗しました。");
            }
        }
        catch
        {
            ActionAutoRestoreService.WriteShared32(
                EntryAddress + 4u,
                OriginalSecond);

            ActionAutoRestoreService.WriteShared32(
                EntryAddress,
                OriginalFirst);

            throw;
        }

        _caveAddress = cave;

        return "関数入口フックを設置しました。\r\n" +
            "入口: 0x" + EntryAddress.ToString("X8") +
            "\r\nコード領域: 0x" + cave.ToString("X8") +
            "\r\n記録領域: 0x" + dataAddress.ToString("X8") +
            "\r\n記録件数: 最大16回";
    }

    public static string Read()
    {
        RequirePaused();

        if (_caveAddress == 0u)
        {
            throw new InvalidOperationException(
                "このアプリでフックを設置してから読み取ってください。");
        }

        uint current =
            ActionAutoRestoreService.ReadShared32(EntryAddress);

        uint delay =
            ActionAutoRestoreService.ReadShared32(EntryAddress + 4u);

        if (!IsJumpTo(current, EntryAddress, _caveAddress) ||
            delay != 0u)
        {
            throw new InvalidOperationException(
                "関数入口フックが現在設置されていません。");
        }

        uint data = DataAddress;
        uint total = ActionAutoRestoreService.ReadShared32(data);

        StringBuilder report = new StringBuilder();

        report.AppendLine("COROMAGE FUNCTION ENTRY TRACE");
        report.AppendLine("READ ONLY");
        report.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("Entry: 0x" + EntryAddress.ToString("X8"));
        report.AppendLine("Cave: 0x" + _caveAddress.ToString("X8"));
        report.AppendLine("Total calls: " + total);

        int available = (int)Math.Min(total, (uint)RecordCount);

        if (available == 0)
        {
            report.AppendLine("No calls captured.");
            return report.ToString();
        }

        int first = total > RecordCount
            ? (int)(total & 15u)
            : 0;

        string[] names =
        {
            "a0", "a1", "a2", "a3",
            "s0", "s1", "s2", "s3",
            "s4", "s5", "s6", "s7",
            "ra"
        };

        for (int i = 0; i < available; i++)
        {
            int slot = (first + i) & 15;
            uint recordAddress =
                data + RecordOffset + (uint)(slot * RecordSize);

            report.AppendLine("");
            report.AppendLine(
                "Record " + (i + 1) +
                " / Slot " + slot);

            for (int word = 0; word < RecordWords; word++)
            {
                uint value = ActionAutoRestoreService.ReadShared32(
                    recordAddress + (uint)(word * 4));

                report.AppendLine(
                    names[word] + ": 0x" + value.ToString("X8"));
            }
        }

        return report.ToString();
    }

    public static string Restore()
    {
        RequirePaused();

        if (_caveAddress == 0u)
        {
            throw new InvalidOperationException(
                "このアプリで設置したフック情報がありません。");
        }

        uint current =
            ActionAutoRestoreService.ReadShared32(EntryAddress);

        uint delay =
            ActionAutoRestoreService.ReadShared32(EntryAddress + 4u);

        if (!IsJumpTo(current, EntryAddress, _caveAddress) ||
            delay != 0u)
        {
            throw new InvalidOperationException(
                "現在の命令が設置したフックと一致しません。復元を中止しました。");
        }

        ActionAutoRestoreService.WriteShared32(
            EntryAddress + 4u,
            OriginalSecond);

        ActionAutoRestoreService.WriteShared32(
            EntryAddress,
            OriginalFirst);

        uint verifyFirst =
            ActionAutoRestoreService.ReadShared32(EntryAddress);

        uint verifySecond =
            ActionAutoRestoreService.ReadShared32(EntryAddress + 4u);

        if (verifyFirst != OriginalFirst ||
            verifySecond != OriginalSecond)
        {
            throw new InvalidOperationException(
                "元の命令への復元確認に失敗しました。");
        }

        uint restoredCave = _caveAddress;
        _caveAddress = 0u;

        return "元の命令へ復元しました。\r\n" +
            "入口: 0x" + EntryAddress.ToString("X8") +
            "\r\nコード領域: 0x" + restoredCave.ToString("X8");
    }
}