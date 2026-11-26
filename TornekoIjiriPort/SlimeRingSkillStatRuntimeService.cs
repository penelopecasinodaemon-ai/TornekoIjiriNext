using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

/// <summary>
/// スライムの指輪の特技使用率・範囲を、実際の種族データ取得時に
/// 呼び出し元のAI個体に応じて切り替える。
///
/// 0x0021D6C8 は種族レコード (24 bytes) の取得関数。
/// 0x0021D6CC を J でフックし、$ra が確認済みのAI判定呼び出し元である場合だけ
/// 個体データ +0x1D3 (0=味方、非0=敵) を見て共通種族データを書き換える。
/// </summary>
internal static class SlimeRingSkillStatRuntimeService
{
    // 種族レコード取得関数の2命令目。
    private const uint HookAddress = 0x0021D6CCu;
    private const uint DelayAddress = 0x0021D6D0u;
    private const uint OriginalInstruction = 0x8D080010u; // LW t0,16(t0)
    private const uint OriginalDelay = 0x00040900u;        // SLL at,a0,4

    // 旧方式のフック。所有マーカーが確認できた場合だけ元に戻す。
    private const uint LegacyHookAddress = 0x002D4528u;
    private const uint LegacyDelayAddress = 0x002D452Cu;
    private const uint LegacyOriginalCall = 0x0C0B4F04u;
    private const uint LegacyOriginalDelay = 0x0220282Du;

    private const uint CaveStart = 0x01FF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00001000u;
    private const uint CaveSize = 0x1000u;

    private const uint MarkerOffset = 0x3F0u;
    private const uint DataOffset = 0x400u;
    private const uint Marker = 0x534C5254u;

    private const int SkillRangeDataOffset = 382206;
    private const int MonsterRecordSize = 24;
    private const uint EeMemoryBaseAddress = 0x00180000u;

    // [id, padding x3, allyRate, allyRange, baseRate, baseRange, rangeAddress]
    private const int EntrySize = 16;

    private static readonly object Sync = new object();

    private sealed class Entry
    {
        public byte RuntimeSpeciesId;
        public byte AllyRate;
        public ushort AllyRange;
        public byte BaseRate;
        public ushort BaseRange;
        public uint RangeAddress;
    }

    private static uint I(int op, int rs, int rt, int imm)
    {
        return ((uint)op << 26) |
               ((uint)rs << 21) |
               ((uint)rt << 16) |
               (ushort)imm;
    }

    private static uint R(int rs, int rt, int rd, int sh, int fn)
    {
        return ((uint)rs << 21) |
               ((uint)rt << 16) |
               ((uint)rd << 11) |
               ((uint)sh << 6) |
               (uint)fn;
    }

    private static void EmitAddress(List<uint> code, int register, uint value)
    {
        code.Add(I(0x0F, 0, register, (int)(value >> 16)));
        code.Add(I(0x0D, register, register, (int)(value & 0xFFFF)));
    }

    private static uint EncodeJ(uint pc, uint target)
    {
        if (((pc + 4u) & 0xF0000000u) !=
            (target & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "コードケーブから種族データ取得関数へジャンプできません。");
        }

        return 0x08000000u |
               ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJ(uint pc, uint instruction)
    {
        return ((pc + 4u) & 0xF0000000u) |
               ((instruction & 0x03FFFFFFu) << 2);
    }

    private static int EmitBranch(List<uint> code, uint opcode)
    {
        int index = code.Count;
        code.Add(opcode);
        return index;
    }

    private static void PatchBranch(
        List<uint> code,
        int branchIndex,
        int targetIndex,
        uint opcode)
    {
        int displacement = targetIndex - (branchIndex + 1);
        code[branchIndex] = opcode | (uint)(ushort)displacement;
    }

    private static uint[] BuildCode(uint cave, int entryCount)
    {
        uint dataAddress = cave + DataOffset;
        var code = new List<uint>();

        void Emit(uint instruction) => code.Add(instruction);

        // 元の関数が使用しない一時レジスタを保存する。
        // t0/t1/at は元関数自身が破壊するため保存不要。
        Emit(I(0x09, 29, 29, -0x50)); // addiu sp,sp,-0x50
        Emit(I(0x3F, 29, 10, 0x00));  // sd t2,0(sp)
        Emit(I(0x3F, 29, 11, 0x08));  // sd t3,8(sp)
        Emit(I(0x3F, 29, 12, 0x10));  // sd t4,16(sp)
        Emit(I(0x3F, 29, 13, 0x18));  // sd t5,24(sp)
        Emit(I(0x3F, 29, 14, 0x20));  // sd t6,32(sp)
        Emit(I(0x3F, 29, 15, 0x28));  // sd t7,40(sp)
        Emit(I(0x3F, 29, 24, 0x30));  // sd t8,48(sp)
        Emit(I(0x3F, 29, 25, 0x38));  // sd t9,56(sp)
        Emit(I(0x3F, 29, 26, 0x40));  // sd k0,64(sp)
        Emit(I(0x3F, 29, 27, 0x48));  // sd k1,72(sp)

        // 0x0021D6CC の元命令と、その後続3命令を再現。
        // LUI t0,0x0018 と SLL at,a0,4 はフックの遅延スロットまでに実行済み。
        Emit(I(0x23, 8, 8, 16));      // lw t0,16(t0)
        Emit(R(0, 4, 9, 3, 0));      // sll t1,a0,3
        Emit(R(1, 8, 2, 0, 0x21));   // addu v0,at,t0
        Emit(R(2, 9, 2, 0, 0x21));   // addu v0,v0,t1

        // $ra は呼び出し元の JAL が設定した値のまま保持されている。
        // 対象AI判定経路だけ個体ポインタを取得する。
        var s2Branches = new List<int>();
        var s1Branches = new List<int>();

        void AddReturnAddressBranch(uint returnAddress, bool useS2)
        {
            EmitAddress(code, 10, returnAddress); // t2 = return address
            int branch = EmitBranch(
                code,
                I(0x04, 31, 10, 0)); // beq ra,t2,label
            Emit(0u);
            if (useS2)
                s2Branches.Add(branch);
            else
                s1Branches.Add(branch);
        }

        // 呼び出し元はアップロードされた ELF の命令列から確認した8箇所。
        AddReturnAddressBranch(0x0020A3A4u, true);
        AddReturnAddressBranch(0x0020D8A0u, true);
        AddReturnAddressBranch(0x002303A4u, false);
        AddReturnAddressBranch(0x00234B24u, false);
        AddReturnAddressBranch(0x0023BE34u, false);
        AddReturnAddressBranch(0x0023D24Cu, false);
        AddReturnAddressBranch(0x0023D544u, false);
        AddReturnAddressBranch(0x0023DA18u, true);

        int unrecognizedBranch =
            EmitBranch(code, I(0x04, 0, 0, 0)); // beq zero,zero,restore
        Emit(0u);

        int actorS1Label = code.Count;
        Emit(I(0x23, 17, 11, 8)); // lw t3,8(s1)
        int actorS1NullBranch =
            EmitBranch(code, I(0x04, 11, 0, 0));
        Emit(0u);
        int actorS1CommonBranch =
            EmitBranch(code, I(0x04, 0, 0, 0));
        Emit(0u);

        int actorS2Label = code.Count;
        Emit(I(0x23, 18, 11, 8)); // lw t3,8(s2)
        int actorS2NullBranch =
            EmitBranch(code, I(0x04, 11, 0, 0));
        Emit(0u);
        int actorS2CommonBranch =
            EmitBranch(code, I(0x04, 0, 0, 0));
        Emit(0u);

        int commonLabel = code.Count;

        Emit(I(0x24, 11, 12, 0x1D3)); // lbu t4,0x1D3(t3): faction
        Emit(I(0x24, 11, 13, 4));     // lbu t5,4(t3): runtime species ID

        // 個体データの種族IDと getter 引数 a0 が一致する場合だけ変更。
        int speciesMismatchBranch =
            EmitBranch(code, I(0x05, 13, 4, 0)); // bne t5,a0,restore
        Emit(0u);

        EmitAddress(code, 14, dataAddress);       // t6 = entry table
        Emit(I(0x09, 0, 15, entryCount));         // t7 = entry count

        int loopLabel = code.Count;
        Emit(I(0x24, 14, 26, 0));                 // lbu k0,0(t6)

        int foundBranch =
            EmitBranch(code, I(0x04, 26, 13, 0)); // beq k0,t5,found
        Emit(0u);

        Emit(I(0x09, 14, 14, EntrySize));         // addiu t6,t6,EntrySize
        Emit(I(0x09, 15, 15, -1));                // addiu t7,t7,-1

        int loopBranch =
            EmitBranch(code, I(0x05, 15, 0, 0));  // bne t7,zero,loop
        Emit(0u);

        int notFoundBranch =
            EmitBranch(code, I(0x04, 0, 0, 0));   // beq zero,zero,restore
        Emit(0u);

        int foundLabel = code.Count;

        int allyBranch =
            EmitBranch(code, I(0x04, 12, 0, 0));  // beq t4,zero,ally
        Emit(0u);

        // 敵: BIN基準値。レートは1 byte、範囲は2 bytesだけを書き換える。
        Emit(I(0x24, 14, 24, 8));                 // lbu t8,8(t6): baseRate
        Emit(I(0x28, 2, 24, 22));                 // sb t8,22(v0)
        Emit(I(0x25, 14, 24, 10));                // lhu t8,10(t6): baseRange
        Emit(I(0x23, 14, 27, 12));                // lw k1,12(t6): range address
        Emit(I(0x29, 27, 24, 0));                 // sh t8,0(k1)

        int enemyDoneBranch =
            EmitBranch(code, I(0x04, 0, 0, 0));   // beq zero,zero,restore
        Emit(0u);

        int allyLabel = code.Count;

        // 味方: スライムの指輪設定。
        Emit(I(0x24, 14, 24, 4));                 // lbu t8,4(t6): allyRate
        Emit(I(0x28, 2, 24, 22));                 // sb t8,22(v0)
        Emit(I(0x25, 14, 24, 6));                // lhu t8,6(t6): allyRange
        Emit(I(0x23, 14, 27, 12));                // lw k1,12(t6): range address
        Emit(I(0x29, 27, 24, 0));                 // sh t8,0(k1)

        int restoreLabel = code.Count;

        foreach (int branch in s2Branches)
            PatchBranch(code, branch, actorS2Label, I(0x04, 31, 10, 0));

        foreach (int branch in s1Branches)
            PatchBranch(code, branch, actorS1Label, I(0x04, 31, 10, 0));

        PatchBranch(code, unrecognizedBranch, restoreLabel, I(0x04, 0, 0, 0));
        PatchBranch(code, actorS1NullBranch, restoreLabel, I(0x04, 11, 0, 0));
        PatchBranch(code, actorS1CommonBranch, commonLabel, I(0x04, 0, 0, 0));
        PatchBranch(code, actorS2NullBranch, restoreLabel, I(0x04, 11, 0, 0));
        PatchBranch(code, actorS2CommonBranch, commonLabel, I(0x04, 0, 0, 0));
        PatchBranch(code, speciesMismatchBranch, restoreLabel, I(0x05, 13, 4, 0));
        PatchBranch(code, foundBranch, foundLabel, I(0x04, 26, 13, 0));
        PatchBranch(code, loopBranch, loopLabel, I(0x05, 15, 0, 0));
        PatchBranch(code, notFoundBranch, restoreLabel, I(0x04, 0, 0, 0));
        PatchBranch(code, allyBranch, allyLabel, I(0x04, 12, 0, 0));
        PatchBranch(code, enemyDoneBranch, restoreLabel, I(0x04, 0, 0, 0));

        // 元関数が変更しない一時レジスタを元に戻し、元の呼び出し元へ戻る。
        Emit(I(0x37, 29, 27, 0x48)); // ld k1,72(sp)
        Emit(I(0x37, 29, 26, 0x40)); // ld k0,64(sp)
        Emit(I(0x37, 29, 25, 0x38)); // ld t9,56(sp)
        Emit(I(0x37, 29, 24, 0x30)); // ld t8,48(sp)
        Emit(I(0x37, 29, 15, 0x28)); // ld t7,40(sp)
        Emit(I(0x37, 29, 14, 0x20)); // ld t6,32(sp)
        Emit(I(0x37, 29, 13, 0x18)); // ld t5,24(sp)
        Emit(I(0x37, 29, 12, 0x10)); // ld t4,16(sp)
        Emit(I(0x37, 29, 11, 0x08)); // ld t3,8(sp)
        Emit(I(0x37, 29, 10, 0x00)); // ld t2,0(sp)
        Emit(I(0x09, 29, 29, 0x50)); // addiu sp,sp,0x50
        Emit(0x03E00008u);           // jr ra
        Emit(0u);                    // nop

        if (code.Count * 4 >= MarkerOffset)
            throw new InvalidOperationException(
                "陣営別切り替えコードがコードケーブ領域を超えました。");

        return code.ToArray();
    }

    private static List<Entry> BuildEntries(
        byte[] source,
        int recordTableOffset,
        SlimeRingSettings settings,
        ISet<int> slimeFamilyIds)
    {
        var result = new List<Entry>();

        foreach (int speciesId in SlimeRingEditorForm.TargetMonsterIds)
        {
            int recordOffset =
                checked(recordTableOffset + speciesId * MonsterRecordSize);

            int rangeOffset =
                checked(SkillRangeDataOffset + (speciesId + 1) * 20);

            if (recordOffset < 0 ||
                recordOffset + MonsterRecordSize > source.Length ||
                rangeOffset < 0 ||
                rangeOffset + 8 > source.Length)
            {
                throw new InvalidOperationException(
                    "特技データのBIN範囲が不正です。種族ID=" + speciesId);
            }

            byte baseRate = source[recordOffset + 22];

            ushort baseRange =
                BitConverter.ToUInt16(source, rangeOffset + 6);

            SlimeRingMonsterSettings item =
                settings.GetMonster(speciesId);

            bool eligible =
                slimeFamilyIds.Contains(speciesId);

            byte allyRate = eligible
                ? (byte)Math.Clamp(item.SkillRate, 0, 255)
                : baseRate;

            ushort allyRange = eligible
                ? (ushort)Math.Clamp(item.SkillRange, 0, 7)
                : baseRange;

            uint rangeAddress =
                EeMemoryBaseAddress +
                (uint)rangeOffset + 6u;

            result.Add(new Entry
            {
                RuntimeSpeciesId = (byte)(speciesId + 1),
                AllyRate = allyRate,
                AllyRange = allyRange,
                BaseRate = baseRate,
                BaseRange = baseRange,
                RangeAddress = rangeAddress
            });
        }

        return result;
    }

    private static byte[] BuildData(List<Entry> entries)
    {
        byte[] data = new byte[entries.Count * EntrySize];

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            int p = i * EntrySize;

            data[p] = e.RuntimeSpeciesId;
            data[p + 4] = e.AllyRate;
            BitConverter.GetBytes(e.AllyRange).CopyTo(data, p + 6);

            data[p + 8] = e.BaseRate;
            BitConverter.GetBytes(e.BaseRange).CopyTo(data, p + 10);

            BitConverter.GetBytes(e.RangeAddress).CopyTo(data, p + 12);
        }

        return data;
    }

    private static void WriteBytes(uint address, byte[] data)
    {
        for (int offset = 0; offset < data.Length; offset += 4)
        {
            byte[] word = new byte[4];
            int count = Math.Min(4, data.Length - offset);
            Array.Copy(data, offset, word, 0, count);

            ActionAutoRestoreService.WriteShared32(
                address + (uint)offset,
                BitConverter.ToUInt32(word, 0));
        }
    }

    private static byte[] ReadBytes(uint address, int length)
    {
        byte[] data = new byte[length];

        for (int offset = 0; offset < length; offset += 4)
        {
            uint value =
                ActionAutoRestoreService.ReadShared32(
                    address + (uint)offset);

            byte[] word = BitConverter.GetBytes(value);
            int count = Math.Min(4, length - offset);
            Array.Copy(word, 0, data, offset, count);
        }

        return data;
    }

    private static uint FindMarkedCave()
    {
        for (uint p = CaveStart; p >= CaveEnd; p -= CaveStep)
        {
            if (ActionAutoRestoreService.ReadShared32(p + MarkerOffset) == Marker)
                return p;
        }

        return 0;
    }

    private static uint FindFreeCave()
    {
        for (uint p = CaveStart; p >= CaveEnd; p -= CaveStep)
        {
            // 先頭が使用中なら、残り4KBを読む必要はない。
            if (ActionAutoRestoreService.ReadShared32(p) != 0)
                continue;

            bool empty = true;

            for (uint offset = 4; offset < CaveSize; offset += 4)
            {
                if (ActionAutoRestoreService.ReadShared32(p + offset) != 0)
                {
                    empty = false;
                    break;
                }
            }

            if (empty)
                return p;
        }

        throw new InvalidOperationException(
            "スライムの指輪・陣営別特技判定用の空きコード領域がありません。");
    }

    private static uint GetOwnedCaveAt(
        uint hookAddress,
        uint currentInstruction,
        uint originalInstruction,
        uint expectedOpcode,
        string hookName)
    {
        if (currentInstruction == originalInstruction)
            return 0;

        if ((currentInstruction & 0xFC000000u) != expectedOpcode)
        {
            throw new InvalidOperationException(
                hookName + " に想定外の命令があります。別のフックを保護するため中止しました。");
        }

        uint cave = DecodeJ(hookAddress, currentInstruction);

        if (cave < CaveEnd ||
            cave > CaveStart ||
            (cave & (CaveStep - 1u)) != 0 ||
            ActionAutoRestoreService.ReadShared32(cave + MarkerOffset) != Marker)
        {
            throw new InvalidOperationException(
                hookName + " のジャンプ先に当サービスの識別マーカーがありません。");
        }

        return cave;
    }

    private static void VerifyCode(uint cave, int entryCount)
    {
        uint[] expected = BuildCode(cave, entryCount);

        for (int i = 0; i < expected.Length; i++)
        {
            uint actual =
                ActionAutoRestoreService.ReadShared32(
                    cave + (uint)(i * 4));

            if (actual != expected[i])
            {
                throw new InvalidOperationException(
                    "陣営別切り替えコードの検証に失敗しました。" +
                    "位置=0x" + (cave + (uint)(i * 4)).ToString("X8"));
            }
        }
    }

    private static void VerifyData(uint address, byte[] expected)
    {
        byte[] actual = ReadBytes(address, expected.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
            {
                throw new InvalidOperationException(
                    "陣営別設定テーブルの検証に失敗しました。" +
                    "位置=0x" + (address + (uint)i).ToString("X8"));
            }
        }
    }

    internal static string Apply(
        byte[] source,
        int recordTableOffset,
        SlimeRingSettings settings,
        ISet<int> slimeFamilyIds)
    {
        lock (Sync)
        {
            if (source == null || source.Length != 524288)
                throw new InvalidOperationException("反映元BINが不正です。");

            if (ActionAutoRestoreService.ReadShared32(DelayAddress)
                != OriginalDelay)
            {
                throw new InvalidOperationException(
                    "0x0021D6D0の遅延スロットが想定値と異なります。");
            }

            if (ActionAutoRestoreService.ReadShared32(LegacyDelayAddress)
                != LegacyOriginalDelay)
            {
                throw new InvalidOperationException(
                    "旧フック 0x002D452C の遅延スロットが想定値と異なります。");
            }

            List<Entry> entries =
                BuildEntries(source, recordTableOffset, settings, slimeFamilyIds);

            byte[] data = BuildData(entries);

            uint currentHook =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            uint activeNewCave = GetOwnedCaveAt(
                HookAddress,
                currentHook,
                OriginalInstruction,
                0x08000000u,
                "0x0021D6CC");

            uint currentLegacy =
                ActionAutoRestoreService.ReadShared32(LegacyHookAddress);

            uint legacyCave = GetOwnedCaveAt(
                LegacyHookAddress,
                currentLegacy,
                LegacyOriginalCall,
                0x0C000000u,
                "旧フック 0x002D4528");

            // 旧方式のフックは、当サービス所有と確認できた場合だけ解除。
            if (legacyCave != 0)
            {
                ActionAutoRestoreService.WriteShared32(
                    LegacyHookAddress,
                    LegacyOriginalCall);

                if (ActionAutoRestoreService.ReadShared32(LegacyHookAddress)
                    != LegacyOriginalCall)
                {
                    throw new InvalidOperationException(
                        "旧フック 0x002D4528 を元のJALへ戻せませんでした。");
                }
            }

            uint cave = activeNewCave;

            if (cave == 0)
                cave = legacyCave;

            // フック解除済みでも旧マーカーが残っていればコードケーブを再利用する。
            if (cave == 0)
                cave = FindMarkedCave();

            if (cave == 0)
                cave = FindFreeCave();

            uint[] code = BuildCode(cave, entries.Count);

            for (int i = 0; i < code.Length; i++)
            {
                ActionAutoRestoreService.WriteShared32(
                    cave + (uint)(i * 4),
                    code[i]);
            }

            WriteBytes(cave + DataOffset, data);

            ActionAutoRestoreService.WriteShared32(
                cave + MarkerOffset,
                Marker);

            VerifyCode(cave, entries.Count);
            VerifyData(cave + DataOffset, data);

            uint expectedHook = EncodeJ(HookAddress, cave);

            if (currentHook == OriginalInstruction)
            {
                ActionAutoRestoreService.WriteShared32(
                    HookAddress,
                    expectedHook);
            }
            else if (currentHook != expectedHook)
            {
                throw new InvalidOperationException(
                    "0x0021D6CC のフック先が更新後のコードケーブと一致しません。");
            }

            if (ActionAutoRestoreService.ReadShared32(HookAddress)
                != expectedHook)
            {
                throw new InvalidOperationException(
                    "0x0021D6CC のフック設置・検証に失敗しました。");
            }

            if (ActionAutoRestoreService.ReadShared32(LegacyHookAddress)
                != LegacyOriginalCall)
            {
                throw new InvalidOperationException(
                    "旧フック 0x002D4528 が復元されていません。");
            }

            return
                "特技使用率・範囲の陣営別切り替えを適用しました。" +
                Environment.NewLine +
                "対象種族数: " + entries.Count +
                Environment.NewLine +
                "味方: 指輪設定 / 敵: BIN基準値" +
                Environment.NewLine +
                "判定位置: 種族データ取得 0x0021D6CC" +
                Environment.NewLine +
                "対象AI呼び出し元: 8箇所 ($ra 判定)" +
                Environment.NewLine +
                "旧フック 0x002D4528: 元のJALへ復元";
        }
    }

    internal static void RestoreHook()
    {
        lock (Sync)
        {
            uint current =
                ActionAutoRestoreService.ReadShared32(HookAddress);

            if (current != OriginalInstruction)
            {
                GetOwnedCaveAt(
                    HookAddress,
                    current,
                    OriginalInstruction,
                    0x08000000u,
                    "0x0021D6CC");

                ActionAutoRestoreService.WriteShared32(
                    HookAddress,
                    OriginalInstruction);

                if (ActionAutoRestoreService.ReadShared32(HookAddress)
                    != OriginalInstruction)
                {
                    throw new InvalidOperationException(
                        "0x0021D6CC を元のLW命令へ戻せませんでした。");
                }
            }

            uint legacy =
                ActionAutoRestoreService.ReadShared32(LegacyHookAddress);

            if (legacy != LegacyOriginalCall)
            {
                GetOwnedCaveAt(
                    LegacyHookAddress,
                    legacy,
                    LegacyOriginalCall,
                    0x0C000000u,
                    "旧フック 0x002D4528");

                ActionAutoRestoreService.WriteShared32(
                    LegacyHookAddress,
                    LegacyOriginalCall);

                if (ActionAutoRestoreService.ReadShared32(LegacyHookAddress)
                    != LegacyOriginalCall)
                {
                    throw new InvalidOperationException(
                        "旧フック 0x002D4528 を元のJALへ戻せませんでした。");
                }
            }
        }
    }
}
