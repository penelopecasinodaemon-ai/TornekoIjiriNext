using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

/// <summary>
/// 特技判定ループ 0x002D4328 の各エントリーについて、
/// s7 / s2 / s2+0 / s2+4 / s2+8 / [s2+8]+0x1D3 を最大40件記録する。
/// 元の JAL 0x002D3C10 と遅延スロットは維持する。
/// </summary>
internal static class AllyMonsterIdentityTraceService
{
    private const uint HookAddress = 0x002D4528u;
    private const uint DelayAddress = 0x002D452Cu;
    private const uint OriginalCall = 0x0C0B4F04u;
    private const uint OriginalDelay = 0x0220282Du;
    private const uint TargetFunction = 0x002D3C10u;

    private const uint CaveStart = 0x01EF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00001000u;
    private const uint CaveSize = 0x800u;

    private const uint TraceOffset = 0x200u;
    private const uint RecordOffset = 0x204u;
    private const uint RecordSize = 0x18u;
    private const uint RecordCapacity = 40u;
    private const uint TraceBytes = 0x5C4u;

    private static readonly object Sync = new object();

    private static uint I(int op, int rs, int rt, int imm) =>
        ((uint)op << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (ushort)imm;

    private static uint R(int rs, int rt, int rd, int sh, int fn) =>
        ((uint)rs << 21) | ((uint)rt << 16) | ((uint)rd << 11) |
        ((uint)sh << 6) | (uint)fn;

    private static uint EncodeJ(uint pc, uint target)
    {
        if (((pc + 4u) & 0xF0000000u) != (target & 0xF0000000u))
            throw new InvalidOperationException("コードケーブからジャンプできないアドレスです。");
        return 0x08000000u | ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint EncodeJal(uint pc, uint target) => EncodeJ(pc, target) | 0x04000000u;
    private static uint DecodeJ(uint pc, uint ins) =>
        ((pc + 4u) & 0xF0000000u) | ((ins & 0x03FFFFFFu) << 2);

    private static uint PatchBranch(uint ins, int at, int target) =>
        (ins & 0xFFFF0000u) | ((uint)(target - (at + 1)) & 0xFFFFu);

    private static uint[] BuildCode(uint cave)
    {
        uint trace = cave + TraceOffset;
        uint records = cave + RecordOffset;
        var c = new List<uint>();

        c.Add(I(0x09,29,29,-0x30));       // addiu sp,sp,-0x30
        c.Add(I(0x3F,29,8,0x00));         // sd t0,0(sp)
        c.Add(I(0x3F,29,9,0x08));         // sd t1,8(sp)
        c.Add(I(0x3F,29,10,0x10));        // sd t2,16(sp)
        c.Add(I(0x3F,29,26,0x18));        // sd k0,24(sp)
        c.Add(I(0x3F,29,27,0x20));        // sd k1,32(sp)

        c.Add(I(0x0F,0,8,(int)(trace >> 16)));
        c.Add(I(0x0D,8,8,(int)(trace & 0xFFFF)));

        c.Add(I(0x23,8,9,0));             // lw t1,count
        c.Add(I(0x0B,0,9,0));             // sltiu t2,t1,40 -- patched below
        c[c.Count - 1] = I(0x0B,9,10,(int)RecordCapacity);
        int fullBranch = c.Count;
        c.Add(I(0x04,10,0,0));            // beq t2,zero,restore
        c.Add(0);

        c.Add(R(0,9,26,4,0x00));           // sll k0,t1,4
        c.Add(R(0,9,27,3,0x00));           // sll k1,t1,3
        c.Add(R(26,27,26,0,0x21));        // addu k0,k0,k1
        c.Add(I(0x0F,0,10,(int)(records >> 16)));
        c.Add(I(0x0D,10,10,(int)(records & 0xFFFF)));
        c.Add(R(26,10,26,0,0x21));        // addu k0,k0,t2

        c.Add(I(0x2B,26,23,0x00));        // sw s7,0(k0)
        c.Add(I(0x2B,26,18,0x04));        // sw s2,4(k0)
        c.Add(I(0x23,18,10,0x00));        // lw t2,0(s2)
        c.Add(I(0x2B,26,10,0x08));        // sw t2,8(k0)
        c.Add(I(0x23,18,10,0x04));        // lw t2,4(s2)
        c.Add(I(0x2B,26,10,0x0C));        // sw t2,12(k0)
        c.Add(I(0x23,18,10,0x08));        // lw t2,8(s2)
        c.Add(I(0x2B,26,10,0x10));        // sw t2,16(k0)

        int nullBranch = c.Count;
        c.Add(I(0x04,10,0,0));            // beq t2,zero,skip flag
        c.Add(0);
        c.Add(I(0x24,10,10,0x1D3));       // lbu t2,0x1D3(t2)
        c.Add(I(0x2B,26,10,0x14));        // sw t2,20(k0)
        int afterFlag = c.Count;
        c[nullBranch] = PatchBranch(c[nullBranch], nullBranch, afterFlag);

        c.Add(I(0x09,9,9,1));             // addiu t1,t1,1
        c.Add(I(0x2B,8,9,0));             // sw t1,count
        int restoreIndex = c.Count;
        c[fullBranch] = PatchBranch(c[fullBranch], fullBranch, restoreIndex);

        c.Add(I(0x37,29,27,0x20));        // ld k1,32(sp)
        c.Add(I(0x37,29,26,0x18));        // ld k0,24(sp)
        c.Add(I(0x37,29,10,0x10));        // ld t2,16(sp)
        c.Add(I(0x37,29,9,0x08));         // ld t1,8(sp)
        c.Add(I(0x37,29,8,0x00));         // ld t0,0(sp)
        c.Add(I(0x09,29,29,0x30));        // addiu sp,sp,0x30

        c.Add(EncodeJ(cave + (uint)(c.Count * 4), TargetFunction));
        c.Add(OriginalDelay);              // 元のJAL遅延スロットを実行
        return c.ToArray();
    }

    private static bool IsCode(uint cave)
    {
        uint[] expected = BuildCode(cave);
        for (int i = 0; i < expected.Length; i++)
            if (ActionAutoRestoreService.ReadShared32(cave + (uint)(i * 4)) != expected[i])
                return false;
        return true;
    }

    private static uint FindCave()
    {
        for (uint p = CaveStart; p >= CaveEnd; p -= CaveStep)
        {
            bool free = true;
            for (uint o = 0; o < CaveSize; o += 4)
            {
                if (ActionAutoRestoreService.ReadShared32(p + o) != 0)
                {
                    free = false;
                    break;
                }
            }
            if (free) return p;
        }
        throw new InvalidOperationException("モンスター識別トレース用の空きコード領域がありません。");
    }

    private static void RequirePaused()
    {
        if (ActionAutoRestoreService.GetSharedStatus() != 1u)
            throw new InvalidOperationException("PCSX2を一時停止してから操作してください。");
    }

    private static void WriteCode(uint cave)
    {
        uint[] code = BuildCode(cave);
        if ((uint)(code.Length * 4) >= TraceOffset)
            throw new InvalidOperationException("観測コードが記録領域に重なっています。");

        for (int i = 0; i < code.Length; i++)
            ActionAutoRestoreService.WriteShared32(cave + (uint)(i * 4), code[i]);

        for (uint o = 0; o < TraceBytes; o += 4)
            ActionAutoRestoreService.WriteShared32(cave + TraceOffset + o, 0);

        if (!IsCode(cave))
            throw new InvalidOperationException("観測コードの書き込み検証に失敗しました。");
    }

    internal static string Install()
    {
        lock (Sync)
        {
            RequirePaused();
            uint current = ActionAutoRestoreService.ReadShared32(HookAddress);
            uint delay = ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (delay != OriginalDelay)
                throw new InvalidOperationException("0x002D452Cの遅延スロットが想定値と異なります。現在値: 0x" + delay.ToString("X8"));

            if (current == OriginalCall)
            {
                // 正常な元命令。新規設置へ進む。
            }
            else if ((current >> 26) == 3u)
            {
                uint oldCave = DecodeJ(HookAddress, current);
                if (oldCave >= CaveEnd && oldCave <= CaveStart && IsCode(oldCave))
                    return "モンスター識別トレースは既に設置されています。\\nCave: 0x" + oldCave.ToString("X8");
                throw new InvalidOperationException("0x002D4528には別のJALフックがあります。上書きしません。現在値: 0x" + current.ToString("X8"));
            }
            else
            {
                throw new InvalidOperationException("0x002D4528の元命令が想定値と異なります。期待値: 0x" + OriginalCall.ToString("X8") + "\\n現在値: 0x" + current.ToString("X8"));
            }

            uint cave = FindCave();
            WriteCode(cave);
            uint jump = EncodeJal(HookAddress, cave);
            ActionAutoRestoreService.WriteShared32(HookAddress, jump);

            if (ActionAutoRestoreService.ReadShared32(HookAddress) != jump ||
                ActionAutoRestoreService.ReadShared32(DelayAddress) != OriginalDelay)
            {
                if (ActionAutoRestoreService.ReadShared32(HookAddress) == jump)
                    ActionAutoRestoreService.WriteShared32(HookAddress, OriginalCall);
                throw new InvalidOperationException("フック設置後の検証に失敗しました。元のJALへ戻しました。");
            }

            return "モンスター識別トレースを設置しました。\n\nHook: 0x" + HookAddress.ToString("X8") +
                "\nCave: 0x" + cave.ToString("X8") +
                "\n記録領域: 0x" + (cave + TraceOffset).ToString("X8") +
                "\n\nPCSX2を再開し、味方・敵が特技判定ループを通る状況にしてください。\n" +
                "最大40エントリーを記録します。その後、一時停止して読み取ってください。";
        }
    }

    internal static string Read()
    {
        lock (Sync)
        {
            RequirePaused();
            uint current = ActionAutoRestoreService.ReadShared32(HookAddress);
            if ((current >> 26) != 3u)
                throw new InvalidOperationException("モンスター識別トレースのJALフックが見つかりません。");

            uint cave = DecodeJ(HookAddress, current);
            if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
                throw new InvalidOperationException("コードケーブを検証できません。読み取りを中止しました。");
            if (ActionAutoRestoreService.ReadShared32(DelayAddress) != OriginalDelay)
                throw new InvalidOperationException("遅延スロットが変更されています。");

            uint trace = cave + TraceOffset;
            uint count = ActionAutoRestoreService.ReadShared32(trace);
            if (count > RecordCapacity)
                throw new InvalidOperationException("記録件数が不正です: " + count);

            var lines = new List<string>
            {
                "MONSTER IDENTITY TRACE",
                "Hook: 0x" + HookAddress.ToString("X8"),
                "Cave: 0x" + cave.ToString("X8"),
                "Recorded entries: " + count,
                "",
                "No.  s7   s2             [s2+0]       [s2+4]       [s2+8]       +1D3"
            };

            for (uint i = 0; i < count; i++)
            {
                uint a = cave + RecordOffset + i * RecordSize;
                uint s7 = ActionAutoRestoreService.ReadShared32(a);
                uint s2 = ActionAutoRestoreService.ReadShared32(a + 4);
                uint f0 = ActionAutoRestoreService.ReadShared32(a + 8);
                uint f4 = ActionAutoRestoreService.ReadShared32(a + 12);
                uint p8 = ActionAutoRestoreService.ReadShared32(a + 16);
                uint flag = ActionAutoRestoreService.ReadShared32(a + 20);
                lines.Add(string.Format(
                    "{0,2}   {1,2}   {2:X8}   {3:X8}   {4:X8}   {5:X8}   {6:X2}",
                    i, s7, s2, f0, f4, p8, flag));
            }

            if (count == 0)
                lines.Add("記録はまだありません。対象の処理が0x002D4528を通過していません。");

            lines.Add("");
            lines.Add("[s2+4] はID候補、[s2+0] は仲間/敵の識別候補です。実際の仲間・敵の記録を照合して確定します。");
            lines.Add("観測後も元の関数 0x002D3C10 を呼び出しています。");
            return string.Join(Environment.NewLine, lines);
        }
    }

    internal static string Restore()
    {
        lock (Sync)
        {
            RequirePaused();
            uint current = ActionAutoRestoreService.ReadShared32(HookAddress);
            if (current == OriginalCall)
                return "モンスター識別トレースは既に解除されています。";
            if ((current >> 26) != 3u)
                throw new InvalidOperationException("0x002D4528の命令が想定外です。復元しません。現在値: 0x" + current.ToString("X8"));

            uint cave = DecodeJ(HookAddress, current);
            if (cave < CaveEnd || cave > CaveStart || !IsCode(cave))
                throw new InvalidOperationException("フック先コードを検証できません。復元を中止しました。");
            if (ActionAutoRestoreService.ReadShared32(DelayAddress) != OriginalDelay)
                throw new InvalidOperationException("遅延スロットが元の命令ではありません。復元を中止しました。");

            ActionAutoRestoreService.WriteShared32(HookAddress, OriginalCall);
            if (ActionAutoRestoreService.ReadShared32(HookAddress) != OriginalCall)
                throw new InvalidOperationException("元のJAL命令への復元に失敗しました。");
            return "モンスター識別トレースを解除しました。0x002D4528に元のJALを復元しました。";
        }
    }
}
