using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

/// <summary>
/// 味方特技処理関数 0x002D3C10 の呼び出し直後を観測する。
///
/// 0x002D46E8 の BEQ v0,zero をフックする。
/// 元の遅延スロット 0x002D46EC はそのまま実行される。
///
/// v0 == 0 なら 0x002D48A4
/// v0 != 0 なら 0x002D46F0
///
/// 元の分岐動作を維持しながら、呼び出し結果を記録する。
/// </summary>
internal static class AllySkillEntryTraceService
{
    private const uint HookAddress = 0x002D46E8u;
    private const uint DelayAddress = 0x002D46ECu;

    private const uint OriginalInstruction = 0x1040006Eu;
    private const uint OriginalDelay = 0x8FAE004Cu;

    private const uint FallThroughAddress = 0x002D46F0u;
    private const uint ZeroTargetAddress = 0x002D48A4u;

    private const uint CaveStart = 0x01EF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00001000u;

    private const uint CaveSize = 0x180u;
    private const uint TraceOffset = 0x100u;
    private const uint TraceBytes = 0x40u;

    private static readonly object Sync = new object();

    private static uint I(
        int opcode,
        int rs,
        int rt,
        int immediate)
    {
        return ((uint)opcode << 26) |
               ((uint)rs << 21) |
               ((uint)rt << 16) |
               (ushort)immediate;
    }

    private static uint EncodeJ(uint address, uint target)
    {
        if (((address + 4u) & 0xF0000000u) !=
            (target & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "コードケーブからジャンプできないアドレスです。");
        }

        return 0x08000000u |
               ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJ(uint address, uint instruction)
    {
        return ((address + 4u) & 0xF0000000u) |
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

        // -------------------------------------------------
        // スタック確保
        // -------------------------------------------------

        code.Add(I(0x09, 29, 29, -0x20));
        // addiu sp,sp,-0x20

        code.Add(I(0x2B, 29, 8, 0x00));
        // sw t0,0(sp)

        code.Add(I(0x2B, 29, 9, 0x04));
        // sw t1,4(sp)

        code.Add(I(0x2B, 29, 26, 0x08));
        // sw k0,8(sp)

        code.Add(I(0x2B, 29, 27, 0x0C));
        // sw k1,12(sp)

        // -------------------------------------------------
        // 記録領域アドレス
        // -------------------------------------------------

        code.Add(I(0x0F, 0, 8, (int)(trace >> 16)));
        // lui t0,trace_hi

        code.Add(I(0x0D, 8, 8, (int)(trace & 0xFFFF)));
        // ori t0,t0,trace_lo

        // -------------------------------------------------
        // 呼び出し回数
        // -------------------------------------------------

        code.Add(I(0x23, 8, 9, 0x00));
        // lw t1,0(t0)

        code.Add(I(0x09, 9, 9, 1));
        // addiu t1,t1,1

        code.Add(I(0x2B, 8, 9, 0x00));
        // sw t1,0(t0)

        // -------------------------------------------------
        // 呼び出し直後のレジスタを記録
        // -------------------------------------------------

        code.Add(I(0x2B, 8, 2, 0x04));
        // sw v0,4(t0)

        code.Add(I(0x2B, 8, 4, 0x08));
        // sw a0,8(t0)

        code.Add(I(0x2B, 8, 5, 0x0C));
        // sw a1,12(t0)

        code.Add(I(0x2B, 8, 13, 0x10));
        // sw t5,16(t0)

        code.Add(I(0x2B, 8, 14, 0x14));
        // sw t6,20(t0)

        code.Add(I(0x2B, 8, 16, 0x18));
        // sw s0,24(t0)

        // 元のSPを記録
        code.Add(I(0x09, 29, 9, 0x20));
        // addiu t1,sp,0x20

        code.Add(I(0x2B, 8, 9, 0x1C));
        // sw t1,28(t0)

        // -------------------------------------------------
        // v0 != 0 の場合だけ、次に参照される値を記録
        //
        // 元の処理では v0 == 0 の場合、
        // t6+6 を読み込まずに 0x002D48A4 へ進む。
        // その動作を維持する。
        // -------------------------------------------------

        int skipOptionalBranch = code.Count;

        code.Add(I(0x04, 2, 0, 0));
        // beq v0,zero,skip

        code.Add(0x00000000u);
        // nop

        code.Add(I(0x21, 14, 9, 0x06));
        // lh t1,6(t6)

        code.Add(I(0x2B, 8, 9, 0x20));
        // sw t1,32(t0)

        code.Add(I(0x25, 14, 9, 0x04));
        // lhu t1,4(t6)

        code.Add(I(0x2B, 8, 9, 0x24));
        // sw t1,36(t0)

        int optionalEnd = code.Count;

        code[skipOptionalBranch] =
            PatchBranch(
                code[skipOptionalBranch],
                skipOptionalBranch,
                optionalEnd);

        // -------------------------------------------------
        // 使用レジスタ復元
        // -------------------------------------------------

        code.Add(I(0x23, 29, 27, 0x0C));
        // lw k1,12(sp)

        code.Add(I(0x23, 29, 26, 0x08));
        // lw k0,8(sp)

        code.Add(I(0x23, 29, 9, 0x04));
        // lw t1,4(sp)

        code.Add(I(0x23, 29, 8, 0x00));
        // lw t0,0(sp)

        code.Add(I(0x09, 29, 29, 0x20));
        // addiu sp,sp,0x20

        // -------------------------------------------------
        // 元のBEQと同じ分岐を再現
        // -------------------------------------------------

        int originalBranch = code.Count;

        code.Add(I(0x04, 2, 0, 0));
        // beq v0,zero,zeroTarget

        code.Add(0x00000000u);
        // nop

        // v0 != 0
        int fallThroughJump = code.Count;

        code.Add(EncodeJ(
            cave + (uint)(fallThroughJump * 4),
            FallThroughAddress));

        code.Add(0x00000000u);

        // v0 == 0
        int zeroTargetJump = code.Count;

        code.Add(EncodeJ(
            cave + (uint)(zeroTargetJump * 4),
            ZeroTargetAddress));

        code.Add(0x00000000u);

        code[originalBranch] =
            PatchBranch(
                code[originalBranch],
                originalBranch,
                zeroTargetJump);

        return code.ToArray();
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

    private static uint FindCave()
    {
        for (uint candidate = CaveStart;
             candidate >= CaveEnd;
             candidate -= CaveStep)
        {
            bool free = true;

            for (uint offset = 0;
                 offset < CaveSize;
                 offset += 4u)
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
            "味方特技入口トレース用の空きコード領域がありません。\n" +
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
                cave + (uint)(i * 4),
                code[i]);
        }

        uint trace = cave + TraceOffset;

        for (uint offset = 0;
             offset < TraceBytes;
             offset += 4u)
        {
            ActionAutoRestoreService.WriteShared32(
                trace + offset, 0u);
        }

        if (!IsCode(cave))
        {
            throw new InvalidOperationException(
                "観測コードの書き込み検証に失敗しました。");
        }

        for (uint offset = 0;
             offset < TraceBytes;
             offset += 4u)
        {
            if (ActionAutoRestoreService.ReadShared32(
                trace + offset) != 0u)
            {
                throw new InvalidOperationException(
                    "記録領域の初期化検証に失敗しました。");
            }
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
                    "0x002D46ECの遅延スロットが想定値と異なります。\n" +
                    "現在値: 0x" + delay.ToString("X8"));
            }

            if ((current >> 26) == 2u)
            {
                uint existingCave =
                    DecodeJ(HookAddress, current);

                if (existingCave >= CaveEnd &&
                    existingCave <= CaveStart &&
                    IsCode(existingCave))
                {
                    return
                        "味方特技入口フックは既に設置されています。\n" +
                        "Hook: 0x" + HookAddress.ToString("X8") + "\n" +
                        "Cave: 0x" + existingCave.ToString("X8") + "\n" +
                        "Trace: 0x" +
                        (existingCave + TraceOffset).ToString("X8");
                }

                throw new InvalidOperationException(
                    "0x002D46E8には別のJフックがあります。\n" +
                    "上書きしません。\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            if (current != OriginalInstruction)
            {
                throw new InvalidOperationException(
                    "0x002D46E8の元の命令が想定値と異なります。\n" +
                    "期待値: 0x" + OriginalInstruction.ToString("X8") + "\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            uint cave = FindCave();

            WriteCode(cave);

            uint jump = EncodeJ(HookAddress, cave);

            // フック命令だけを差し替える。
            // 元の遅延スロットは変更しない。
            ActionAutoRestoreService.WriteShared32(
                HookAddress, jump);

            uint verify =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            uint verifyDelay =
                ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (verify != jump || verifyDelay != OriginalDelay)
            {
                if (verify == jump)
                {
                    ActionAutoRestoreService.WriteShared32(
                        HookAddress, OriginalInstruction);
                }

                throw new InvalidOperationException(
                    "フック設置後の検証に失敗しました。\n" +
                    "元の命令へ戻しました。");
            }

            return
                "味方特技入口フックを設置しました。\n\n" +
                "Hook: 0x" + HookAddress.ToString("X8") + "\n" +
                "Original: 0x" + OriginalInstruction.ToString("X8") + "\n" +
                "Delay: 0x" + OriginalDelay.ToString("X8") + "\n" +
                "Cave: 0x" + cave.ToString("X8") + "\n" +
                "Trace: 0x" +
                (cave + TraceOffset).ToString("X8") + "\n\n" +
                "PCSX2を再開して味方スライムに特技を使わせてください。\n" +
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

            if ((current >> 26) != 2u)
            {
                throw new InvalidOperationException(
                    "味方特技入口フックが見つかりません。");
            }

            uint cave = DecodeJ(HookAddress, current);

            if (cave < CaveEnd ||
                cave > CaveStart ||
                !IsCode(cave))
            {
                throw new InvalidOperationException(
                    "コードケーブを検証できません。\n" +
                    "読み取りを中止しました。");
            }

            if (ActionAutoRestoreService.ReadShared32(
                DelayAddress) != OriginalDelay)
            {
                throw new InvalidOperationException(
                    "遅延スロットが変更されています。");
            }

            uint trace = cave + TraceOffset;

            uint count = ActionAutoRestoreService.ReadShared32(
                trace + 0x00);

            uint result = ActionAutoRestoreService.ReadShared32(
                trace + 0x04);

            uint a0 = ActionAutoRestoreService.ReadShared32(
                trace + 0x08);

            uint a1 = ActionAutoRestoreService.ReadShared32(
                trace + 0x0C);

            uint t5 = ActionAutoRestoreService.ReadShared32(
                trace + 0x10);

            uint t6 = ActionAutoRestoreService.ReadShared32(
                trace + 0x14);

            uint s0 = ActionAutoRestoreService.ReadShared32(
                trace + 0x18);

            uint originalSp = ActionAutoRestoreService.ReadShared32(
                trace + 0x1C);

            uint skillIndex = ActionAutoRestoreService.ReadShared32(
                trace + 0x20);

            uint tableValue = ActionAutoRestoreService.ReadShared32(
                trace + 0x24);

            return string.Join(Environment.NewLine, new[]
            {
                "ALLY SKILL ENTRY TRACE",
                "Hook: 0x" + HookAddress.ToString("X8"),
                "Code cave: 0x" + cave.ToString("X8"),
                "Trace: 0x" + trace.ToString("X8"),
                "",
                "Recorded calls: " + count,
                "",
                "呼び出し結果 v0 : 0x" + result.ToString("X8"),
                "a0              : 0x" + a0.ToString("X8"),
                "a1              : 0x" + a1.ToString("X8"),
                "t5              : 0x" + t5.ToString("X8"),
                "t6              : 0x" + t6.ToString("X8"),
                "s0              : 0x" + s0.ToString("X8"),
                "Original SP     : 0x" + originalSp.ToString("X8"),
                "",
                "LH t6+6         : 0x" + skillIndex.ToString("X8"),
                "LHU t6+4        : 0x" + tableValue.ToString("X8"),
                "",
                count == 0u
                    ? "まだフックは実行されていません。"
                    : "最後に通過した呼び出し結果を記録しています。",
                "",
                "元の分岐先:",
                result == 0u
                    ? "0x002D48A4"
                    : "0x002D46F0"
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

            if (current == OriginalInstruction)
            {
                return "味方特技入口フックは既に解除されています。";
            }

            if ((current >> 26) != 2u)
            {
                throw new InvalidOperationException(
                    "0x002D46E8の命令が想定外です。\n" +
                    "復元しません。\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            uint cave = DecodeJ(HookAddress, current);

            if (cave < CaveEnd ||
                cave > CaveStart ||
                !IsCode(cave))
            {
                throw new InvalidOperationException(
                    "フック先コードを検証できません。\n" +
                    "復元を中止しました。");
            }

            uint delay =
                ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (delay != OriginalDelay)
            {
                throw new InvalidOperationException(
                    "遅延スロットが元の命令ではありません。\n" +
                    "復元を中止しました。\n" +
                    "現在値: 0x" + delay.ToString("X8"));
            }

            ActionAutoRestoreService.WriteShared32(
                HookAddress, OriginalInstruction);

            if (ActionAutoRestoreService.ReadShared32(HookAddress) !=
                OriginalInstruction)
            {
                throw new InvalidOperationException(
                    "元のBEQ命令への復元に失敗しました。");
            }

            return
                "味方特技入口フックを解除しました。\n" +
                "0x002D46E8に元のBEQ命令を復元しました。\n" +
                "記録データとコードケーブは残しています。";
        }
    }
}