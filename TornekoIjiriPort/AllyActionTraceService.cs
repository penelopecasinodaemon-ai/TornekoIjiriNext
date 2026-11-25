using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

internal static class AllyActionTraceService
{
    private const uint HookAddress = 0x0022515Cu;
    private const uint OriginalCall = 0x0C08F1D4u;
    private const uint DelayAddress = 0x00225160u;
    private const uint OriginalDelay = 0x0200202Du;
    private const uint DispatcherAddress = 0x0023C750u;

    private const uint CaveStart = 0x01EF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00010000u;
    private const uint CaveSize = 0x180u;
    private const uint TraceOffset = 0x100u;

    private static uint _caveAddress;

    private static uint EncodeJal(uint address)
    {
        if ((address & 0xF0000000u) !=
            ((HookAddress + 4u) & 0xF0000000u))
            throw new InvalidOperationException("味方フックのJAL到達範囲外です。");

        return 0x0C000000u | ((address >> 2) & 0x03FFFFFFu);
    }

    private static bool IsJalTo(uint instruction, uint address)
    {
        return (instruction >> 26) == 3u &&
            ((instruction & 0x03FFFFFFu) << 2) == address;
    }

    private static uint[] BuildCode(uint cave)
    {
        uint trace = cave + TraceOffset;

        return new uint[]
        {
            // 元のJAL遅延スロットで a0=s0 が実行済み。
            // a0/a1とs0-s3を記録する。
            0x27BDFFD0u, // addiu sp,sp,-0x30
            0xAFA40000u, // sw a0,0(sp)
            0xAFA50004u, // sw a1,4(sp)
            0xAFB00008u, // sw s0,8(sp)
            0xAFB1000Cu, // sw s1,12(sp)
            0xAFB20010u, // sw s2,16(sp)
            0xAFB30014u, // sw s3,20(sp)
            0xAFBF0018u, // sw ra,24(sp)

            // k0 = 記録領域
            0x3C1A0000u | ((trace >> 16) & 0xFFFFu),
            0x375A0000u | (trace & 0xFFFFu),

            // 引数・保存レジスタを記録
            0xAF440000u, // sw a0,0(k0)
            0xAF450004u, // sw a1,4(k0)
            0xAF500008u, // sw s0,8(k0)
            0xAF51000Cu, // sw s1,12(k0)
            0xAF520010u, // sw s2,16(k0)
            0xAF530014u, // sw s3,20(k0)

            // 呼び出し回数を加算
            0x8F5B0018u, // lw k1,24(k0)
            0x277B0001u, // addiu k1,k1,1
            0xAF5B0018u, // sw k1,24(k0)

            // 元の共通ディスパッチャーを呼ぶ
            0x0C08F1D4u, // jal 0x0023C750
            0x00000000u, // nop

            // 元の呼び出し元へ戻る
            0x8FBF0018u, // lw ra,24(sp)
            0x27BD0030u, // addiu sp,sp,0x30
            0x03E00008u, // jr ra
            0x00000000u  // nop
        };
    }

    private static bool IsCode(uint cave)
    {
        uint[] code = BuildCode(cave);

        for (int i = 0; i < code.Length; i++)
        {
            if (ActionAutoRestoreService.ReadShared32(
                cave + (uint)(i * 4)) != code[i])
                return false;
        }

        return true;
    }

    private static uint FindCave()
    {
        for (uint candidate = CaveStart;
             candidate >= CaveEnd;
             candidate -= CaveStep)
        {
            bool free = true;

            for (uint offset = 0; offset < CaveSize; offset += 4u)
            {
                if (ActionAutoRestoreService.ReadShared32(
                    candidate + offset) != 0u)
                {
                    free = false;
                    break;
                }
            }

            if (free)
                return candidate;
        }

        throw new InvalidOperationException(
            "味方トレース用の空きコード領域がありません。");
    }

    internal static string Install()
    {
        if (ActionAutoRestoreService.GetSharedStatus() != 1u)
            throw new InvalidOperationException(
                "PCSX2を一時停止してから設置してください。");

        uint currentCall =
            ActionAutoRestoreService.ReadShared32(HookAddress);

        uint currentDelay =
            ActionAutoRestoreService.ReadShared32(DelayAddress);

        if (currentDelay != OriginalDelay)
            throw new InvalidOperationException(
                "味方側の遅延スロットが想定値と異なります: 0x" +
                currentDelay.ToString("X8"));

        if ((currentCall >> 26) == 3u && !IsJalTo(currentCall, DispatcherAddress))
        {
            uint existingCave =
                (currentCall & 0x03FFFFFFu) << 2;

            if (existingCave >= CaveEnd &&
                existingCave <= CaveStart &&
                IsCode(existingCave))
            {
                _caveAddress = existingCave;
                return "味方トレースは既に設置されています。\nコード領域: 0x" +
                    existingCave.ToString("X8");
            }

            throw new InvalidOperationException(
                "味方側呼び出し位置には別のJALが設置されています。");
        }

        if (currentCall != OriginalCall)
            throw new InvalidOperationException(
                "味方側の元のJALが見つかりません。\n現在値: 0x" +
                currentCall.ToString("X8"));

        uint cave = FindCave();
        uint[] code = BuildCode(cave);

        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4), code[i]);
        }

        uint trace = cave + TraceOffset;

        for (uint offset = 0; offset < 28u; offset += 4u)
            ActionAutoRestoreService.WriteShared32(trace + offset, 0u);

        if (!IsCode(cave))
            throw new InvalidOperationException(
                "味方トレースコードの書き込み検証に失敗しました。");

        uint jump = EncodeJal(cave);

        ActionAutoRestoreService.WriteShared32(HookAddress, jump);

        if (ActionAutoRestoreService.ReadShared32(HookAddress) != jump)
        {
            ActionAutoRestoreService.WriteShared32(
                HookAddress, OriginalCall);

            throw new InvalidOperationException(
                "味方フックの設置検証に失敗しました。");
        }

        _caveAddress = cave;

        return
            "味方側トレースを設置しました。\n" +
            "フック: 0x" + HookAddress.ToString("X8") + "\n" +
            "コード領域: 0x" + cave.ToString("X8") + "\n" +
            "記録領域: 0x" + trace.ToString("X8") + "\n\n" +
            "PCSX2を再開して味方モンスターに行動させてください。";
    }

    internal static string Read()
    {
        if (ActionAutoRestoreService.GetSharedStatus() != 1u)
            throw new InvalidOperationException(
                "記録を読む前にPCSX2を一時停止してください。");

        uint call =
            ActionAutoRestoreService.ReadShared32(HookAddress);

        if ((call >> 26) != 3u)
            throw new InvalidOperationException(
                "味方トレースフックが見つかりません。");

        uint cave = (call & 0x03FFFFFFu) << 2;

        if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
            throw new InvalidOperationException(
                "味方トレースコードを検証できません。");

        uint trace = cave + TraceOffset;

        uint a0 = ActionAutoRestoreService.ReadShared32(trace);
        uint a1 = ActionAutoRestoreService.ReadShared32(trace + 4u);
        uint s0 = ActionAutoRestoreService.ReadShared32(trace + 8u);
        uint s1 = ActionAutoRestoreService.ReadShared32(trace + 12u);
        uint s2 = ActionAutoRestoreService.ReadShared32(trace + 16u);
        uint s3 = ActionAutoRestoreService.ReadShared32(trace + 20u);
        uint count = ActionAutoRestoreService.ReadShared32(trace + 24u);

        return string.Join(Environment.NewLine, new[]
        {
            "ALLY ACTION TRACE",
            "Read only: YES",
            "Hook: 0x" + HookAddress.ToString("X8"),
            "Dispatcher: 0x" + DispatcherAddress.ToString("X8"),
            "Code cave: 0x" + cave.ToString("X8"),
            "Trace: 0x" + trace.ToString("X8"),
            "",
            "Recorded calls: " + count,
            "a0: 0x" + a0.ToString("X8"),
            "a1: 0x" + a1.ToString("X8"),
            "s0: 0x" + s0.ToString("X8"),
            "s1: 0x" + s1.ToString("X8"),
            "s2: 0x" + s2.ToString("X8"),
            "s3: 0x" + s3.ToString("X8"),
            "",
            "Latest register values before dispatcher call."
        });
    }

    internal static string Restore()
    {
        if (ActionAutoRestoreService.GetSharedStatus() != 1u)
            throw new InvalidOperationException(
                "PCSX2を一時停止してから解除してください。");

        uint current =
            ActionAutoRestoreService.ReadShared32(HookAddress);

        if (current == OriginalCall)
            return "味方トレースは既に解除されています。";

        if ((current >> 26) != 3u)
            throw new InvalidOperationException(
                "味方フック命令が想定外です: 0x" +
                current.ToString("X8"));

        uint cave = (current & 0x03FFFFFFu) << 2;

        if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
            throw new InvalidOperationException(
                "コード領域を検証できないため解除を中止しました。");

        uint delay =
            ActionAutoRestoreService.ReadShared32(DelayAddress);

        if (delay != OriginalDelay)
            throw new InvalidOperationException(
                "遅延スロットが変更されています。解除を中止しました。");

        ActionAutoRestoreService.WriteShared32(
            HookAddress, OriginalCall);

        if (ActionAutoRestoreService.ReadShared32(HookAddress) != OriginalCall)
            throw new InvalidOperationException(
                "元の味方呼び出し命令への復元に失敗しました。");

        _caveAddress = 0u;

        return
            "味方トレースを解除しました。\n" +
            "元のJAL 0x0023C750を復元しました。\n" +
            "遅延スロットは変更していません。";
    }
}

