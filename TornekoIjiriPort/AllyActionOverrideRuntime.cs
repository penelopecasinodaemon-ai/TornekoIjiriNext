using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

internal static class AllyActionOverrideRuntime
{
    private const uint HookAddress = 0x002F3E60u;
    private const uint OriginalCall = 0x0C0B4F04u;
    private const uint DelayAddress = 0x002F3E64u;
    private const uint OriginalDelay = 0x85A50004u;

    private const uint TargetFunction = 0x002D3C10u;

    private const uint CaveStart = 0x01FF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00001000u;
    private const uint CaveSize = 0x1000u;

    private const uint DataOffset = 0x200u;
    private const uint MarkerOffset = 0x1FCu;
    private const uint Marker = 0x414C594Fu;

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
            throw new InvalidOperationException(
                "味方特技フックのジャンプ先が範囲外です。");

        return 0x08000000u |
               ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint EncodeJal(uint target)
    {
        if (((HookAddress + 4u) & 0xF0000000u) !=
            (target & 0xF0000000u))
            throw new InvalidOperationException(
                "味方特技フックのコードケーブが範囲外です。");

        return 0x0C000000u |
               ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJal(uint instruction)
    {
        return (instruction & 0x03FFFFFFu) << 2;
    }

    private static int EmitBranch(
        List<uint> code,
        int opcode,
        int rs,
        int rt)
    {
        int index = code.Count;
        code.Add(I(opcode, rs, rt, 0));
        code.Add(0u);
        return index;
    }

    private static void PatchBranch(
        List<uint> code,
        int branchIndex,
        int targetIndex)
    {
        int displacement = targetIndex - (branchIndex + 1);

        if (displacement < short.MinValue ||
            displacement > short.MaxValue)
            throw new InvalidOperationException(
                "味方特技ディスパッチャーの分岐距離が不正です。");

        code[branchIndex] =
            (code[branchIndex] & 0xFFFF0000u) |
            (ushort)displacement;
    }

    private static void EmitAddress(
        List<uint> code,
        uint address)
    {
        // t3 = address
        code.Add(I(0x0F, 0, 11, (int)(address >> 16)));
        code.Add(I(0x0D, 11, 11, (int)(address & 0xFFFFu)));
    }

    private static uint[] BuildCode(
        uint cave,
        int ringCount,
        int normalCount)
    {
        uint ringData = cave + DataOffset;
        uint normalData = ringData + (uint)ringCount * 2u;

        var code = new List<uint>();

        // s2がNULLなら元の関数へ
        int contextNull = EmitBranch(code, 0x04, 18, 0);
        code.Add(0u);

        // t0 = *(s2 + 8): 個体データポインタ
        code.Add(I(0x23, 18, 8, 8));

        // 個体データポインタがNULLなら元の関数へ
        int actorNull = EmitBranch(code, 0x04, 8, 0);
        code.Add(0u);

        // +0x1D3: 0=味方、0以外=敵
        code.Add(I(0x24, 8, 9, 0x1D3));

        // 敵なら変更しない
        int enemyBranch = EmitBranch(code, 0x05, 9, 0);
        code.Add(0u);

        // t2 = 実行時種族ID（個体データ+4）
        code.Add(I(0x24, 8, 10, 4));

        // ------------------------------
        // 指輪上書きテーブルを先に検索
        // ------------------------------
        EmitAddress(code, ringData);
        code.Add(I(0x09, 0, 12, ringCount));

        int ringEmpty = EmitBranch(code, 0x04, 12, 0);
        code.Add(0u);

        int ringLoop = code.Count;

        code.Add(I(0x24, 11, 13, 0)); // lbu t5,0(t3)

        int ringMatch = EmitBranch(code, 0x04, 10, 13);
        code.Add(0u);

        code.Add(I(0x09, 11, 11, 2));  // addiu t3,t3,2
        code.Add(I(0x09, 12, 12, -1)); // addiu t4,t4,-1

        int ringRepeat = EmitBranch(code, 0x07, 12, 0);
        code.Add(0u);

        // ------------------------------
        // 通常設定テーブルを検索
        // ------------------------------
        int normalSetup = code.Count;

        EmitAddress(code, normalData);
        code.Add(I(0x09, 0, 12, normalCount));

        int noNormal = EmitBranch(code, 0x04, 12, 0);
        code.Add(0u);

        int normalLoop = code.Count;

        code.Add(I(0x24, 11, 13, 0)); // lbu t5,0(t3)

        int normalMatch = EmitBranch(code, 0x04, 10, 13);
        code.Add(0u);

        code.Add(I(0x09, 11, 11, 2));
        code.Add(I(0x09, 12, 12, -1));

        int normalRepeat = EmitBranch(code, 0x07, 12, 0);
        code.Add(0u);

        // ------------------------------
        // 一致した場合はa1を特技IDに変更
        // ------------------------------
        int applyAction = code.Count;

        code.Add(I(0x24, 11, 13, 1)); // lbu t5,1(t3)
        code.Add(R(13, 0, 5, 0, 0x21)); // addu a1,t5,zero

        // 元の関数へテールジャンプ
        int originalFunction = code.Count;
        code.Add(EncodeJ(
            cave + (uint)(originalFunction * 4),
            TargetFunction));
        code.Add(0u);

        PatchBranch(code, contextNull, originalFunction);
        PatchBranch(code, actorNull, originalFunction);
        PatchBranch(code, enemyBranch, originalFunction);

        PatchBranch(code, ringEmpty, normalSetup);
        PatchBranch(code, ringMatch, applyAction);
        PatchBranch(code, ringRepeat, ringLoop);

        PatchBranch(code, noNormal, originalFunction);
        PatchBranch(code, normalMatch, applyAction);
        PatchBranch(code, normalRepeat, normalLoop);

        return code.ToArray();
    }

    private static byte[] BuildData(
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> ring,
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> normal)
    {
        byte[] data = new byte[(ring.Count + normal.Count) * 2];

        int offset = 0;

        foreach (var entry in ring)
        {
            data[offset++] = entry.SpeciesId;
            data[offset++] = entry.ActualAction;
        }

        foreach (var entry in normal)
        {
            data[offset++] = entry.SpeciesId;
            data[offset++] = entry.ActualAction;
        }

        return data;
    }

    private static bool IsCode(
        uint cave,
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> ring,
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> normal)
    {
        if (ActionAutoRestoreService.ReadShared32(
            cave + MarkerOffset) != Marker)
            return false;

        uint[] code = BuildCode(cave, ring.Count, normal.Count);

        for (int i = 0; i < code.Length; i++)
        {
            if (ActionAutoRestoreService.ReadShared32(
                cave + (uint)(i * 4)) != code[i])
                return false;
        }

        byte[] data = BuildData(ring, normal);
        int paddedLength = (data.Length + 3) & ~3;

        for (int offset = 0; offset < paddedLength; offset += 4)
        {
            uint expected = 0u;

            for (int b = 0; b < 4; b++)
            {
                int index = offset + b;

                if (index < data.Length)
                    expected |= (uint)data[index] << (b * 8);
            }

            if (ActionAutoRestoreService.ReadShared32(
                cave + DataOffset + (uint)offset) != expected)
                return false;
        }

        return true;
    }

    private static void WriteCode(
        uint cave,
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> ring,
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> normal)
    {
        uint[] code = BuildCode(cave, ring.Count, normal.Count);

        if ((uint)(code.Length * 4) >= MarkerOffset)
            throw new InvalidOperationException(
                "味方特技ディスパッチャーのコードが大きすぎます。");

        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4), code[i]);
        }

        byte[] data = BuildData(ring, normal);
        int paddedLength = (data.Length + 3) & ~3;

        for (int offset = 0; offset < paddedLength; offset += 4)
        {
            uint value = 0u;

            for (int b = 0; b < 4; b++)
            {
                int index = offset + b;

                if (index < data.Length)
                    value |= (uint)data[index] << (b * 8);
            }

            ActionAutoRestoreService.WriteShared32(
                cave + DataOffset + (uint)offset, value);
        }

        ActionAutoRestoreService.WriteShared32(
            cave + MarkerOffset, Marker);

        if (!IsCode(cave, ring, normal))
            throw new InvalidOperationException(
                "味方特技ディスパッチャーの書き込み検証に失敗しました。");
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
            "味方特技ディスパッチャー用の空きコード領域がありません。");
    }

    private static void RestoreHook()
    {
        ActionAutoRestoreService.WriteShared32(
            HookAddress, OriginalCall);

        if (ActionAutoRestoreService.ReadShared32(HookAddress) !=
            OriginalCall)
            throw new InvalidOperationException(
                "味方特技呼び出し命令の復元に失敗しました。");
    }

    internal static void Apply(
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> normal,
        IReadOnlyList<(byte SpeciesId, byte Marker, byte ActualAction)> ring)
    {
        lock (Sync)
        {
            uint delay =
                ActionAutoRestoreService.ReadShared32(DelayAddress);

            if (delay != OriginalDelay)
                throw new InvalidOperationException(
                    "味方特技呼び出しの遅延スロットが想定値と異なります: 0x" +
                    delay.ToString("X8"));

            uint current =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            bool ours = false;
            uint existingCave = 0u;

            if (current != OriginalCall)
            {
                if ((current >> 26) != 3u)
                    throw new InvalidOperationException(
                        "味方特技呼び出し位置に想定外の命令があります: 0x" +
                        current.ToString("X8"));

                existingCave = DecodeJal(current);

                if (existingCave < CaveEnd ||
                    existingCave > CaveStart ||
                    ActionAutoRestoreService.ReadShared32(
                        existingCave + MarkerOffset) != Marker)
                    throw new InvalidOperationException(
                        "味方特技呼び出し位置に別のフックがあります。上書きしません。");

                ours = true;
            }

            if (ring.Count == 0 && normal.Count == 0)
            {
                if (ours)
                    RestoreHook();

                return;
            }

            if (ours && IsCode(existingCave, ring, normal))
                return;

            // 設定内容が変わった場合は先に元のJALへ戻す。
            if (ours)
                RestoreHook();

            uint cave = FindCave();

            WriteCode(cave, ring, normal);

            uint newCall = EncodeJal(cave);

            ActionAutoRestoreService.WriteShared32(
                HookAddress, newCall);

            if (ActionAutoRestoreService.ReadShared32(HookAddress) != newCall ||
                ActionAutoRestoreService.ReadShared32(DelayAddress) != OriginalDelay)
            {
                RestoreHook();

                throw new InvalidOperationException(
                    "味方特技フック設置後の検証に失敗しました。");
            }
        }
    }
}
