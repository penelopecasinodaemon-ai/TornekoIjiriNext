using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

/// <summary>
/// 味方特技ルートの二次ディスパッチ（0x002D4714）を観測する。
/// 元のJR v0をコードケーブ内で再実行し、ゲームの分岐先は変更しない。
/// </summary>
internal static class AllySkillRouteTraceService
{
    private const uint HookAddress = 0x002D4714u;
    private const uint DelayAddress = 0x002D4718u;
    private const uint OriginalInstruction = 0x00400008u; // jr v0
    private const uint OriginalDelay = 0x00000000u;       // nop

    private const uint CaveStart = 0x01EF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00010000u;
    private const uint CaveSize = 0x180u;
    private const uint TraceOffset = 0x100u;

    private const int TraceBytes = 0x40;

    private static readonly object Sync = new object();

    private static uint EncodeJ(uint instructionAddress, uint target)
    {
        if (((instructionAddress + 4u) & 0xF0000000u) !=
            (target & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "観測コードケーブがJ命令の到達範囲外です。");
        }

        return 0x08000000u | ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJ(uint instructionAddress, uint instruction)
    {
        return ((instructionAddress + 4u) & 0xF0000000u) |
            ((instruction & 0x03FFFFFFu) << 2);
    }

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

    private static uint[] BuildCode(uint cave)
    {
        uint trace = cave + TraceOffset;

        var code = new List<uint>
        {
            I(0x09, 29, 29, -0x30), // addiu sp,sp,-0x30

            I(0x3F, 29, 8, 0x00),  // sd t0,0(sp)
            I(0x3F, 29, 9, 0x08),  // sd t1,8(sp)
            I(0x3F, 29, 26, 0x10), // sd k0,16(sp)
            I(0x3F, 29, 27, 0x18), // sd k1,24(sp)

            I(0x0F, 0, 8, (int)(trace >> 16)), // lui t0,trace_hi
            I(0x0D, 8, 8, (int)(trace & 0xFFFF)), // ori t0,t0,trace_lo

            I(0x2B, 8, 22, 0x00), // sw s6,0(t0)
            I(0x2B, 8, 4, 0x04),  // sw a0,4(t0)
            I(0x2B, 8, 2, 0x08),  // sw v0,8(t0) - original JR target
            I(0x2B, 8, 3, 0x0C),  // sw v1,12(t0)

            I(0x25, 22, 9, 0x04), // lhu t1,4(s6)
            I(0x2B, 8, 9, 0x10),  // sw t1,16(t0)
            I(0x25, 22, 9, 0x06), // lhu t1,6(s6)
            I(0x2B, 8, 9, 0x14),  // sw t1,20(t0)
            I(0x24, 22, 9, 0x0A), // lbu t1,0x0A(s6)
            I(0x2B, 8, 9, 0x18),  // sw t1,24(t0)

            I(0x23, 8, 9, 0x20),  // lw t1,32(t0)
            I(0x09, 9, 9, 1),     // addiu t1,t1,1
            I(0x2B, 8, 9, 0x20),  // sw t1,32(t0)

            I(0x37, 29, 27, 0x18), // ld k1,24(sp)
            I(0x37, 29, 26, 0x10), // ld k0,16(sp)
            I(0x37, 29, 9, 0x08),  // ld t1,8(sp)
            I(0x37, 29, 8, 0x00),  // ld t0,0(sp)

            I(0x09, 29, 29, 0x30), // addiu sp,sp,0x30

            R(2, 0, 0, 0, 0x08),   // jr v0 (execute original dispatch)
            0x00000000u            // nop (original delay slot)
        };

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
            "味方分岐トレース用の空きコード領域が見つかりません。\n" +
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

        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4), code[i]);
        }

        uint trace = cave + TraceOffset;
        for (uint offset = 0; offset < TraceBytes; offset += 4u)
            ActionAutoRestoreService.WriteShared32(trace + offset, 0u);

        if (!IsCode(cave))
        {
            throw new InvalidOperationException(
                "味方分岐トレースコードの書き込み検証に失敗しました。");
        }

        for (uint offset = 0; offset < TraceBytes; offset += 4u)
        {
            if (ActionAutoRestoreService.ReadShared32(trace + offset) != 0u)
                throw new InvalidOperationException(
                    "記録領域の初期化検証に失敗しました。");
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
                    "0x002D4718の遅延スロットが想定値と異なります。\n" +
                    "現在値: 0x" + delay.ToString("X8"));
            }

            if ((current >> 26) == 2u)
            {
                uint existingCave = DecodeJ(HookAddress, current);

                if (existingCave >= CaveEnd &&
                    existingCave <= CaveStart &&
                    IsCode(existingCave))
                {
                    return
                        "味方分岐トレースは既に設置されています。\n" +
                        "フック: 0x" + HookAddress.ToString("X8") + "\n" +
                        "コードケーブ: 0x" + existingCave.ToString("X8") + "\n" +
                        "記録領域: 0x" +
                        (existingCave + TraceOffset).ToString("X8");
                }

                throw new InvalidOperationException(
                    "0x002D4714には別のJフックが存在します。上書きしません。\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            if (current != OriginalInstruction)
            {
                throw new InvalidOperationException(
                    "0x002D4714の元の命令が想定値と異なります。\n" +
                    "期待値: 0x" + OriginalInstruction.ToString("X8") + "\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            uint cave = FindCave();
            WriteCode(cave);

            uint jump = EncodeJ(HookAddress, cave);

            // フック命令だけを差し替える。遅延スロットは元のNOPを維持。
            ActionAutoRestoreService.WriteShared32(HookAddress, jump);

            uint verify =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            uint verifyDelay =
                ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (verify != jump || verifyDelay != OriginalDelay)
            {
                // フックが設置されていた場合は元のJRへ戻す。
                if (verify == jump)
                {
                    ActionAutoRestoreService.WriteShared32(
                        HookAddress, OriginalInstruction);
                }

                throw new InvalidOperationException(
                    "フック設置後の検証に失敗しました。元の命令へ戻しました。");
            }

            return
                "味方特技ルートの観測フックを設置しました。\n" +
                "Hook: 0x" + HookAddress.ToString("X8") + "\n" +
                "Original: JR v0 (0x" + OriginalInstruction.ToString("X8") + ")\n" +
                "Cave: 0x" + cave.ToString("X8") + "\n" +
                "Trace: 0x" + (cave + TraceOffset).ToString("X8") + "\n\n" +
                "PCSX2を再開し、味方スライムに特技を使わせてください。\n" +
                "その後PCSX2を一時停止して「分岐記録読み取り」を押してください。";
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
                    "味方分岐トレースのJフックが見つかりません。");
            }

            uint cave = DecodeJ(HookAddress, current);

            if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
            {
                throw new InvalidOperationException(
                    "コードケーブを検証できません。読み取りを中止しました。");
            }

            if (ActionAutoRestoreService.ReadShared32(DelayAddress) != OriginalDelay)
            {
                throw new InvalidOperationException(
                    "フック遅延スロットが変更されています。");
            }

            uint trace = cave + TraceOffset;

            uint s6 = ActionAutoRestoreService.ReadShared32(trace + 0x00);
            uint a0 = ActionAutoRestoreService.ReadShared32(trace + 0x04);
            uint v0 = ActionAutoRestoreService.ReadShared32(trace + 0x08);
            uint v1 = ActionAutoRestoreService.ReadShared32(trace + 0x0C);
            uint field04 = ActionAutoRestoreService.ReadShared32(trace + 0x10);
            uint field06 = ActionAutoRestoreService.ReadShared32(trace + 0x14);
            uint field0A = ActionAutoRestoreService.ReadShared32(trace + 0x18);
            uint count = ActionAutoRestoreService.ReadShared32(trace + 0x20);

            return string.Join(Environment.NewLine, new[]
            {
                "ALLY SKILL ROUTE TRACE",
                "Hook: 0x" + HookAddress.ToString("X8"),
                "Code cave: 0x" + cave.ToString("X8"),
                "Trace: 0x" + trace.ToString("X8"),
                "",
                "Recorded calls: " + count,
                "s6:       0x" + s6.ToString("X8"),
                "a0:       0x" + a0.ToString("X8"),
                "v0 target:0x" + v0.ToString("X8"),
                "v1:       0x" + v1.ToString("X8"),
                "LHU s6+04:0x" + field04.ToString("X4"),
                "LHU s6+06:0x" + field06.ToString("X4"),
                "LBU s6+0A:0x" + field0A.ToString("X2"),
                "",
                count == 0u
                    ? "このフックはまだ実行されていません。"
                    : "上記は最後に通過した1回分のレジスタ・構造体値です。",
                "ゲーム本来のJR v0は観測後に実行されています。"
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
                return "味方分岐トレースは既に解除されています。";

            if ((current >> 26) != 2u)
            {
                throw new InvalidOperationException(
                    "0x002D4714の命令が想定外です。復元しません。\n" +
                    "現在値: 0x" + current.ToString("X8"));
            }

            uint cave = DecodeJ(HookAddress, current);

            if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
            {
                throw new InvalidOperationException(
                    "フック先コードを検証できないため復元を中止しました。");
            }

            uint delay =
                ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (delay != OriginalDelay)
            {
                throw new InvalidOperationException(
                    "0x002D4718が元のNOPではありません。復元を中止しました。\n" +
                    "現在値: 0x" + delay.ToString("X8"));
            }

            ActionAutoRestoreService.WriteShared32(
                HookAddress, OriginalInstruction);

            if (ActionAutoRestoreService.ReadShared32(HookAddress) !=
                OriginalInstruction)
            {
                throw new InvalidOperationException(
                    "元のJR v0への復元に失敗しました。");
            }

            return
                "味方分岐トレースを解除しました。\n" +
                "0x002D4714に元のJR v0を復元しました。\n" +
                "記録データとコードケーブはそのまま残しています。";
        }
    }
}
