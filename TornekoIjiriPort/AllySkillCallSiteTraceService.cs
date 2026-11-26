
using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

/// <summary>
/// 0x002F3E60 の JAL 直前にあるレジスタと候補データを記録する。
/// 元の JAL の戻り先を維持し、観測後に 0x002D3C10 を実行する。
/// </summary>
internal static class AllySkillCallSiteTraceService
{
    private const uint HookAddress = 0x002F3E60u;
    private const uint DelayAddress = 0x002F3E64u;

    private const uint OriginalCall = 0x0C0B4F04u;
    private const uint OriginalDelay = 0x85A50004u;

    private const uint TargetFunction = 0x002D3C10u;

    private const uint CaveStart = 0x01EF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00001000u;

    private const uint CaveSize = 0x180u;
    private const uint TraceOffset = 0x100u;
    private const uint TraceBytes = 0x40u;

    private static readonly object Sync = new object();

    private static uint I(int opcode, int rs, int rt, int immediate)
    {
        return ((uint)opcode << 26) |
               ((uint)rs << 21) |
               ((uint)rt << 16) |
               (ushort)immediate;
    }

    private static uint R(int rs, int rt, int rd, int shamt, int funct)
    {
        return ((uint)rs << 21) |
               ((uint)rt << 16) |
               ((uint)rd << 11) |
               ((uint)shamt << 6) |
               (uint)funct;
    }

    private static uint EncodeJ(uint instructionAddress, uint target)
    {
        if (((instructionAddress + 4u) & 0xF0000000u) !=
            (target & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "コードケーブからジャンプできないアドレスです。");
        }

        return 0x08000000u |
               ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint EncodeJal(uint instructionAddress, uint target)
    {
        return EncodeJ(instructionAddress, target) | 0x04000000u;
    }

    private static uint DecodeJ(uint instructionAddress, uint instruction)
    {
        return ((instructionAddress + 4u) & 0xF0000000u) |
               ((instruction & 0x03FFFFFFu) << 2);
    }

    private static uint PatchBranch(
        uint instruction,
        int branchIndex,
        int targetIndex)
    {
        int offset = targetIndex - (branchIndex + 1);

        return (instruction & 0xFFFF0000u) |
               ((uint)offset & 0xFFFFu);
    }

    private static uint[] BuildCode(uint cave)
    {
        uint trace = cave + TraceOffset;
        var code = new List<uint>();

        // スタック確保
        code.Add(I(0x09, 29, 29, -0x40)); // addiu sp,sp,-0x40

        // 使用する一時レジスタを退避
        code.Add(I(0x3F, 29, 8, 0x00));   // sd t0,0(sp)
        code.Add(I(0x3F, 29, 9, 0x08));   // sd t1,8(sp)
        code.Add(I(0x3F, 29, 10, 0x10));  // sd t2,16(sp)
        code.Add(I(0x3F, 29, 26, 0x18));  // sd k0,24(sp)
        code.Add(I(0x3F, 29, 27, 0x20));  // sd k1,32(sp)

        // 記録領域アドレス
        code.Add(I(0x0F, 0, 8, (int)(trace >> 16))); // lui t0,hi
        code.Add(I(0x0D, 8, 8, (int)(trace & 0xFFFF))); // ori t0,t0,lo

        // 呼び出し時のレジスタ
        code.Add(I(0x2B, 8, 18, 0x04)); // sw s2,4(t0)
        code.Add(I(0x2B, 8, 16, 0x08)); // sw s0,8(t0)
        code.Add(I(0x2B, 8, 5, 0x0C));  // sw a1,12(t0)
        code.Add(I(0x2B, 8, 13, 0x10)); // sw t5,16(t0)

        // *(s2+8) を取得
        code.Add(I(0x23, 18, 9, 0x08)); // lw t1,8(s2)
        code.Add(I(0x2B, 8, 9, 0x14));  // sw t1,20(t0)

        // ポインタがNULLでなければ +0x1D3 の1バイトを取得
        int nullBranch = code.Count;
        code.Add(I(0x04, 9, 0, 0));     // beq t1,zero,skip
        code.Add(0x00000000u);          // nop

        code.Add(I(0x24, 9, 10, 0x1D3)); // lbu t2,0x1D3(t1)
        code.Add(I(0x2B, 8, 10, 0x18));  // sw t2,24(t0)

        int afterByteRead = code.Count;
        code[nullBranch] = PatchBranch(
            code[nullBranch], nullBranch, afterByteRead);

        // 呼び出し回数
        code.Add(I(0x23, 8, 9, 0x00)); // lw t1,0(t0)
        code.Add(I(0x09, 9, 9, 1));    // addiu t1,t1,1
        code.Add(I(0x2B, 8, 9, 0x00)); // sw t1,0(t0)

        // 一時レジスタを復元
        code.Add(I(0x37, 29, 27, 0x20)); // ld k1,32(sp)
        code.Add(I(0x37, 29, 26, 0x18)); // ld k0,24(sp)
        code.Add(I(0x37, 29, 10, 0x10)); // ld t2,16(sp)
        code.Add(I(0x37, 29, 9, 0x08));  // ld t1,8(sp)
        code.Add(I(0x37, 29, 8, 0x00));  // ld t0,0(sp)

        code.Add(I(0x09, 29, 29, 0x40)); // addiu sp,sp,0x40

        // 元のJALが設定したRAは変更しない。
        // J命令で本来の関数へ移動し、元の呼び出しから戻る。
        code.Add(EncodeJ(cave + (uint)(code.Count * 4), TargetFunction));
        code.Add(0x00000000u); // nop

        return code.ToArray();
    }

    private static bool IsCode(uint cave)
    {
        uint[] expected = BuildCode(cave);

        for (int i = 0; i < expected.Length; i++)
        {
            if (ActionAutoRestoreService.ReadShared32(
                cave + (uint)(i * 4)) != expected[i])
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
            "呼び出し地点トレース用の空きコード領域がありません。\n" +
            "安全のためフックを設置しませんでした。");
    }

    private static void RequirePaused()
    {
        if (ActionAutoRestoreService.GetSharedStatus() != 1u)
        {
            throw new InvalidOperationException(
                "PCSX2を一時停止してから操作してください。");
        }
    }

    private static void WriteCode(uint cave)
    {
        uint[] code = BuildCode(cave);

        if ((uint)(code.Length * 4) >= TraceOffset)
        {
            throw new InvalidOperationException(
                "観測コードが記録領域に重なっています。");
        }

        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4), code[i]);
        }

        uint trace = cave + TraceOffset;

        for (uint offset = 0; offset < TraceBytes; offset += 4u)
        {
            ActionAutoRestoreService.WriteShared32(
                trace + offset, 0u);
        }

        if (!IsCode(cave))
        {
            throw new InvalidOperationException(
                "観測コードの書き込み検証に失敗しました。");
        }
    }

    internal static string Install()
    {
        lock (Sync)
        {
            RequirePaused();

            uint current =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            uint delay =
                ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (delay != OriginalDelay)
            {
                throw new InvalidOperationException(
                    "0x002F3E64の遅延スロットが想定値と異なります。\n" +
                    "現在値: 0x" + delay.ToString("X8"));
            }

            if ((current >> 26) == 3u)
            {
                uint existingCave = DecodeJ(HookAddress, current);

                if (existingCave >= CaveEnd &&
                    existingCave <= CaveStart &&
                    IsCode(existingCave))
                {
                    return
                        "呼び出し地点トレースは既に設置されています。\n" +
                        "Hook: 0x" + HookAddress.ToString("X8") + "\n" +
                        "Cave: 0x" + existingCave.ToString("X8");
                }

                throw new InvalidOperationException(
                    "0x002F3E60には別のJALフックがあります。\n" +
                    "上書きしません。\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            if (current != OriginalCall)
            {
                throw new InvalidOperationException(
                    "0x002F3E60の元の命令が想定値と異なります。\n" +
                    "期待値: 0x" + OriginalCall.ToString("X8") + "\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            uint cave = FindCave();
            WriteCode(cave);

            uint jump = EncodeJal(HookAddress, cave);

            // JALだけを差し替える。遅延スロットは元のLHを維持。
            ActionAutoRestoreService.WriteShared32(HookAddress, jump);

            uint verify =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            uint verifyDelay =
                ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (verify != jump || verifyDelay != OriginalDelay)
            {
                if (verify == jump)
                {
                    ActionAutoRestoreService.WriteShared32(
                        HookAddress, OriginalCall);
                }

                throw new InvalidOperationException(
                    "フック設置後の検証に失敗しました。元の命令へ戻しました。");
            }

            return
                "呼び出し地点トレースを設置しました。\n\n" +
                "Hook: 0x" + HookAddress.ToString("X8") + "\n" +
                "Original JAL: 0x" + OriginalCall.ToString("X8") + "\n" +
                "Delay: 0x" + OriginalDelay.ToString("X8") + "\n" +
                "Cave: 0x" + cave.ToString("X8") + "\n" +
                "Trace: 0x" + (cave + TraceOffset).ToString("X8") + "\n\n" +
                "PCSX2を再開して味方に特技を使わせてください。\n" +
                "その後、一時停止して記録を読み取ってください。";
        }
    }

    internal static string Read()
    {
        lock (Sync)
        {
            RequirePaused();

            uint current =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            if ((current >> 26) != 3u)
            {
                throw new InvalidOperationException(
                    "呼び出し地点トレースのJALフックが見つかりません。");
            }

            uint cave = DecodeJ(HookAddress, current);

            if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
            {
                throw new InvalidOperationException(
                    "コードケーブを検証できません。読み取りを中止しました。");
            }

            if (ActionAutoRestoreService.ReadShared32(DelayAddress) !=
                OriginalDelay)
            {
                throw new InvalidOperationException(
                    "遅延スロットが変更されています。");
            }

            uint trace = cave + TraceOffset;

            uint count = ActionAutoRestoreService.ReadShared32(trace + 0x00);
            uint s2 = ActionAutoRestoreService.ReadShared32(trace + 0x04);
            uint s0 = ActionAutoRestoreService.ReadShared32(trace + 0x08);
            uint a1 = ActionAutoRestoreService.ReadShared32(trace + 0x0C);
            uint t5 = ActionAutoRestoreService.ReadShared32(trace + 0x10);
            uint pointer = ActionAutoRestoreService.ReadShared32(trace + 0x14);
            uint field = ActionAutoRestoreService.ReadShared32(trace + 0x18);

            return string.Join(Environment.NewLine, new[]
            {
                "ALLY SKILL CALL-SITE TRACE",
                "Hook: 0x" + HookAddress.ToString("X8"),
                "Code cave: 0x" + cave.ToString("X8"),
                "Trace: 0x" + trace.ToString("X8"),
                "",
                "Recorded calls: " + count,
                "",
                "s2                  : 0x" + s2.ToString("X8"),
                "s2 + 8 pointer      : 0x" + pointer.ToString("X8"),
                "pointer + 0x1D3     : 0x" + field.ToString("X2"),
                "s0                  : 0x" + s0.ToString("X8"),
                "a1 (skill ID)       : 0x" + a1.ToString("X8"),
                "t5                  : 0x" + t5.ToString("X8"),
                "",
                count == 0u
                    ? "まだフックは実行されていません。"
                    : "最後に通過した呼び出し1回分を記録しています。",
                "",
                "観測後、元の関数 0x002D3C10 を実行しています。"
            });
        }
    }

    internal static string Restore()
    {
        lock (Sync)
        {
            RequirePaused();

            uint current =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            if (current == OriginalCall)
                return "呼び出し地点トレースは既に解除されています。";

            if ((current >> 26) != 3u)
            {
                throw new InvalidOperationException(
                    "0x002F3E60の命令が想定外です。復元しません。\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            uint cave = DecodeJ(HookAddress, current);

            if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
            {
                throw new InvalidOperationException(
                    "フック先コードを検証できません。復元を中止しました。");
            }

            if (ActionAutoRestoreService.ReadShared32(DelayAddress) !=
                OriginalDelay)
            {
                throw new InvalidOperationException(
                    "遅延スロットが元の命令ではありません。復元を中止しました。");
            }

            ActionAutoRestoreService.WriteShared32(
                HookAddress, OriginalCall);

            if (ActionAutoRestoreService.ReadShared32(HookAddress) !=
                OriginalCall)
            {
                throw new InvalidOperationException(
                    "元のJAL命令への復元に失敗しました。");
            }

            return
                "呼び出し地点トレースを解除しました。\n" +
                "0x002F3E60に元のJAL 0x002D3C10を復元しました。\n" +
                "記録データとコードケーブは残しています。";
        }
    }
}
