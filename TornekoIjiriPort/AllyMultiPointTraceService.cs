using System;
using System.Collections.Generic;
using System.Text;

namespace TornekoIjiriPort;

/// <summary>
/// 味方側既存16地点と敵側候補6地点、共通ディスパッチャーを観測する。
/// 16個のJAL呼び出し地点と、共通ディスパッチャーのJR v0を記録する。
/// 各地点は独立したコードケーブと最終レジスタ記録を持つ。
/// </summary>
internal static class AllyMultiPointTraceService
{
    private const uint CaveStart = 0x01EF0000u;
    private const uint CaveEnd = 0x01E00000u;
    private const uint CaveStep = 0x00001000u;
    private const uint TraceOffset = 0x00000400u;
    private const uint MetadataOffset = 0x00000210u;
    private const uint MetadataMagic = 0x41545243u; // "ATRC"

    private const uint DispatcherSite = 0x002CF54Cu;
    private const uint DispatcherOriginal = 0x00400008u; // jr v0
    private const uint DispatcherDelay = 0x00000000u;

    private sealed class Site
    {
        internal uint Address;
        internal uint ExpectedTarget;
        internal string Name;
        internal bool IsDispatcher;
        internal uint OriginalInstruction;
        internal uint OriginalDelay;
        internal uint Target;
        internal uint Cave;

        internal Site(uint address, uint target, string name)
        {
            Address = address;
            ExpectedTarget = target;
            Name = name;
            IsDispatcher = false;
        }

        internal Site()
        {
            Address = DispatcherSite;
            ExpectedTarget = 0;
            Name = "共通ディスパッチャー JR v0";
            IsDispatcher = true;
            OriginalInstruction = DispatcherOriginal;
            OriginalDelay = DispatcherDelay;
        }
    }

    private static readonly object Sync = new object();

    private static readonly Site[] CallSites =
    {
        new Site(0x002CF5C0u, 0x002CFC20u, "handler-00"),
        new Site(0x002CF5D4u, 0x002D02E8u, "handler-01"),
        new Site(0x002CF5ECu, 0x002D0074u, "handler-02"),
        new Site(0x002CF600u, 0x002CFC58u, "handler-03"),
        new Site(0x002CF614u, 0x002CFCE8u, "handler-04"),
        new Site(0x002CF628u, 0x002CFD80u, "handler-05"),
        new Site(0x002CF648u, 0x002CFE1Cu, "handler-06"),
        new Site(0x002CF65Cu, 0x002D02BCu, "handler-07"),
        new Site(0x002CF678u, 0x002D0C58u, "handler-08"),
        new Site(0x002CF694u, 0x002D0CA0u, "handler-09"),
        new Site(0x002CF6B0u, 0x002D0C7Cu, "handler-10"),
        new Site(0x002CF6CCu, 0x002D0CC4u, "handler-11"),
        new Site(0x002CF6ECu, 0x002D0424u, "handler-12"),
        new Site(0x002CF70Cu, 0x002D08A4u, "handler-13"),
        new Site(0x002CF728u, 0x002D1070u, "handler-14"),
        new Site(0x002CF73Cu, 0x002D0320u, "handler-15"),
        new Site(0x002D4528u, 0x002D3C10u, "skill-call-4528"),
        new Site(0x002D46E0u, 0x002D3C10u, "skill-call-46E0"),

        // 敵側特技処理候補
        new Site(0x0023C7ACu, 0x0021E644u, "enemy-call-01"),
        new Site(0x0020F8C4u, 0x002CE924u, "enemy-call-02"),
        new Site(0x002D016Cu, 0x0021D680u, "enemy-call-03"),
        new Site(0x002D01F4u, 0x0021D680u, "enemy-call-04"),
        new Site(0x002D0B8Cu, 0x002CE924u, "enemy-call-05")
    };

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

    private static uint EncodeJ(uint fromAddress, uint target)
    {
        if (((fromAddress + 4u) & 0xF0000000u) !=
            (target & 0xF0000000u))
            throw new InvalidOperationException(
                "コードケーブがJ命令の到達範囲外です。");

        return 0x08000000u | ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJ(uint fromAddress, uint instruction)
    {
        return ((fromAddress + 4u) & 0xF0000000u) |
               ((instruction & 0x03FFFFFFu) << 2);
    }

    private static uint DecodeJalTarget(uint address, uint instruction)
    {
        return ((address + 4u) & 0xF0000000u) |
               ((instruction & 0x03FFFFFFu) << 2);
    }

    private static bool IsJ(uint instruction) =>
        (instruction >> 26) == 2u;

    private static bool IsControlTransfer(uint instruction)
    {
        uint op = instruction >> 26;
        if (op == 1u || op == 2u || op == 3u ||
            (op >= 4u && op <= 7u))
            return true;

        if (op >= 0x10u && op <= 0x13u)
        {
            uint rs = (instruction >> 21) & 31u;
            if (rs == 8u)
                return true;
        }

        if (op == 0u)
        {
            uint funct = instruction & 63u;
            if (funct == 8u || funct == 9u)
                return true;
        }

        return false;
    }

    private static uint[] BuildCode(Site site, uint cave)
    {
        uint trace = cave + TraceOffset;
        var code = new List<uint>(180);

        // For a JAL callsite, reproduce link-register assignment and the original
        // delay-slot instruction first. The subsequent snapshot is therefore the
        // exact register state seen by the called handler.
        if (!site.IsDispatcher)
        {
            uint returnAddress = site.Address + 8u;
            code.Add(I(0x0F, 0, 31, (int)(returnAddress >> 16))); // lui ra,hi
            code.Add(I(0x0D, 31, 31, (int)(returnAddress & 0xFFFF))); // ori ra,ra,lo
            code.Add(site.OriginalDelay);
        }

        // Preserve the complete 128-bit GPR state in a private aligned stack frame.
        code.Add(I(0x19, 29, 29, -0x400)); // daddiu sp,sp,-0x400

        for (int reg = 1; reg <= 31; reg++)
        {
            if (reg == 29)
                continue;

            code.Add(I(0x1F, 29, reg, reg * 16)); // sq rN,offset(sp)
        }

        // Save the original SP value in slot 32.
        code.Add(I(0x19, 29, 26, 0x400)); // daddiu k0,sp,0x400
        code.Add(I(0x1F, 29, 26, 0x200)); // sq k0,0x200(sp)

        // k0 = trace record; count is incremented for every hit.
        code.Add(I(0x0F, 0, 26, (int)(trace >> 16))); // lui k0,hi
        code.Add(I(0x0D, 26, 26, (int)(trace & 0xFFFF))); // ori k0,k0,lo
        code.Add(I(0x23, 26, 27, 0)); // lw k1,0(k0)
        code.Add(I(0x09, 27, 27, 1)); // addiu k1,k1,1
        code.Add(I(0x2B, 26, 27, 0)); // sw k1,0(k0)

        // Store all 32 GPRs as aligned 128-bit values. Slot 0 is $zero; slots 1..31 are GPRs.
        code.Add(I(0x1F, 26, 0, 0x10)); // sq zero,0x10(k0)
        for (int reg = 1; reg <= 31; reg++)
        {
            int stackOffset = reg == 29 ? 0x200 : reg * 16;
            code.Add(I(0x1E, 29, 27, stackOffset)); // lq k1,stack(sp)
            code.Add(I(0x1F, 26, 27, 0x10 + reg * 16)); // sq k1,record(k0)
        }

        // Restore every GPR except k0/k1, which are restored last.
        for (int reg = 1; reg <= 31; reg++)
        {
            if (reg == 26 || reg == 27 || reg == 29)
                continue;

            code.Add(I(0x1E, 29, reg, reg * 16)); // lq rN,offset(sp)
        }

        code.Add(I(0x1E, 29, 27, 27 * 16)); // restore k1
        code.Add(I(0x1E, 29, 26, 26 * 16)); // restore k0
        code.Add(I(0x19, 29, 29, 0x400)); // restore sp

        if (site.IsDispatcher)
        {
            // Original instruction at 0x002CF54C: jr v0; delay slot is nop.
            code.Add(R(2, 0, 0, 0, 8)); // jr v0
            code.Add(0u);               // nop
        }
        else
        {
            // The JAL's target is called with the original RA and post-delay state.
            uint jumpAddress = cave + (uint)(code.Count * 4);
            code.Add(EncodeJ(jumpAddress, site.Target));
            code.Add(0u);
        }

        if ((uint)(code.Count * 4) >= TraceOffset)
            throw new InvalidOperationException(
                "観測コードが記録領域に重なっています。");

        return code.ToArray();
    }

    private static void WriteWord(uint address, uint value) =>
        ActionAutoRestoreService.WriteShared32(address, value);

    private static uint ReadWord(uint address) =>
        ActionAutoRestoreService.ReadShared32(address);

    private static string ReadVector128(uint address)
    {
        uint w0 = ReadWord(address);
        uint w1 = ReadWord(address + 4u);
        uint w2 = ReadWord(address + 8u);
        uint w3 = ReadWord(address + 12u);
        return w3.ToString("X8") + w2.ToString("X8") +
               w1.ToString("X8") + w0.ToString("X8");
    }

    private static void RequirePaused()
    {
        if (ActionAutoRestoreService.GetSharedStatus() != 1u)
            throw new InvalidOperationException(
                "PCSX2を一時停止してから操作してください。");
    }

    private static uint FindCave(HashSet<uint> reserved)
    {
        for (uint candidate = CaveStart;
             candidate >= CaveEnd;
             candidate -= CaveStep)
        {
            if (reserved.Contains(candidate))
                continue;

            // Fast reject: most occupied pages have a nonzero first word.
            if (ReadWord(candidate) != 0u)
                continue;

            bool free = true;
            for (uint offset = 4u; offset < TraceOffset + 0x224u; offset += 4u)
            {
                if (ReadWord(candidate + offset) != 0u)
                {
                    free = false;
                    break;
                }
            }

            if (free)
                return candidate;
        }

        throw new InvalidOperationException(
            "24地点分の空きコード領域を確保できませんでした。\n" +
            "ゲームメモリは変更せずに中止しました。");
    }

    private static void WriteSite(Site site)
    {
        uint[] code = BuildCode(site, site.Cave);
        for (int i = 0; i < code.Length; i++)
            WriteWord(site.Cave + (uint)(i * 4), code[i]);

        uint trace = site.Cave + TraceOffset;
        for (uint offset = 0; offset < 0x224u; offset += 4u)
            WriteWord(trace + offset, 0u);

        WriteWord(trace + MetadataOffset, site.OriginalInstruction);
        WriteWord(trace + MetadataOffset + 4u, site.OriginalDelay);
        WriteWord(trace + MetadataOffset + 8u, site.Target);
        WriteWord(trace + MetadataOffset + 12u, site.Address);
        WriteWord(trace + MetadataOffset + 16u, MetadataMagic);

        // Verify the generated code and metadata before patching the game site.
        for (int i = 0; i < code.Length; i++)
        {
            if (ReadWord(site.Cave + (uint)(i * 4)) != code[i])
                throw new InvalidOperationException(
                    "コードケーブ書き込み検証失敗: 0x" +
                    site.Cave.ToString("X8"));
        }

        if (ReadWord(trace + MetadataOffset + 16u) != MetadataMagic)
            throw new InvalidOperationException("観測メタデータの検証に失敗しました。");
    }

    private static Site[] CreateAllSites()
    {
        var list = new List<Site>();
        foreach (Site template in CallSites)
        {
            list.Add(new Site(
                template.Address,
                template.ExpectedTarget,
                template.Name));
        }
        list.Add(new Site());
        return list.ToArray();
    }

    internal static string Install()
    {
        lock (Sync)
        {
            RequirePaused();

            Site[] sites = CreateAllSites();
            var reserved = new HashSet<uint>();

            // Read and validate every original instruction before writing anything.
            foreach (Site site in sites)
            {
                uint current = ReadWord(site.Address);

                if (IsJ(current))
                    throw new InvalidOperationException(
                        "既にJフックまたは別のJ命令があります: 0x" +
                        site.Address.ToString("X8"));

                if (site.IsDispatcher)
                {
                    if (current != DispatcherOriginal)
                        throw new InvalidOperationException(
                            "共通ディスパッチャー命令が想定外です。\n" +
                            "Address: 0x" + site.Address.ToString("X8") +
                            "\nExpected: 0x" + DispatcherOriginal.ToString("X8") +
                            "\nActual: 0x" + current.ToString("X8"));

                    uint delay = ReadWord(site.Address + 4u);
                    if (delay != DispatcherDelay)
                        throw new InvalidOperationException(
                            "共通ディスパッチャーの遅延スロットが想定外です。");
                    site.OriginalInstruction = current;
                    site.OriginalDelay = delay;
                    site.Target = 0;
                }
                else
                {
                    if ((current >> 26) != 3u)
                        throw new InvalidOperationException(
                            "JAL命令ではありません: 0x" +
                            site.Address.ToString("X8") +
                            " 現在値=0x" + current.ToString("X8"));

                    uint target = DecodeJalTarget(site.Address, current);
                    if (target != site.ExpectedTarget)
                        throw new InvalidOperationException(
                            "JALの飛び先が実行時解析と一致しません。\n" +
                            "Address: 0x" + site.Address.ToString("X8") +
                            "\nExpected: 0x" + site.ExpectedTarget.ToString("X8") +
                            "\nActual: 0x" + target.ToString("X8"));

                    uint delay = ReadWord(site.Address + 4u);
                    if (IsControlTransfer(delay))
                        throw new InvalidOperationException(
                            "遅延スロットが制御転送命令のため安全に移設できません。\n" +
                            "Address: 0x" + (site.Address + 4u).ToString("X8"));

                    site.OriginalInstruction = current;
                    site.OriginalDelay = delay;
                    site.Target = target;
                }
            }

            // Reserve 24 independent 4-KiB pages.
            foreach (Site site in sites)
            {
                site.Cave = FindCave(reserved);
                reserved.Add(site.Cave);
            }

            // Populate and verify every cave before changing any game instruction.
            foreach (Site site in sites)
                WriteSite(site);

            var patched = new List<Site>();
            try
            {
                foreach (Site site in sites)
                {
                    // Add first so even a partial write of this site is rolled back.
                    patched.Add(site);

                    if (!site.IsDispatcher)
                        WriteWord(site.Address + 4u, 0u);

                    uint jump = EncodeJ(site.Address, site.Cave);
                    WriteWord(site.Address, jump);

                    if (ReadWord(site.Address) != jump ||
                        ReadWord(site.Address + 4u) != 0u)
                        throw new InvalidOperationException(
                            "フック設置後の検証に失敗: 0x" +
                            site.Address.ToString("X8"));
                }
            }
            catch
            {
                foreach (Site site in patched)
                {
                    WriteWord(site.Address, site.OriginalInstruction);
                    if (!site.IsDispatcher)
                        WriteWord(site.Address + 4u, site.OriginalDelay);
                }
                throw;
            }

            var sb = new StringBuilder();
            sb.AppendLine("特技処理候補24地点の同時観測フックを設置しました。");
            sb.AppendLine("観測地点: 23 JAL + 1 JR v0");
            sb.AppendLine("各地点で全32 GPRを記録し、呼び出し回数を別々に集計します。");
            sb.AppendLine("記録は各地点の最新状態を保持します。");
            sb.AppendLine();
            foreach (Site site in sites)
            {
                sb.Append("0x").Append(site.Address.ToString("X8"))
                  .Append(" -> cave 0x").Append(site.Cave.ToString("X8"))
                  .Append(" / trace 0x").Append((site.Cave + TraceOffset).ToString("X8"))
                  .Append(" / ").AppendLine(site.Name);
            }
            sb.AppendLine();
            sb.AppendLine("PCSX2を再開して敵スライムに特技を使わせ、停止後に記録を読み取ってください。");
            return sb.ToString();
        }
    }

    private static bool TryGetInstalledSite(
        uint address,
        string name,
        bool dispatcher,
        out Site site)
    {
        site = null;
        uint instruction = ReadWord(address);
        if (!IsJ(instruction))
            return false;

        uint cave = DecodeJ(address, instruction);
        if (cave < CaveEnd || cave > CaveStart ||
            (cave & 0xFFFu) != 0u)
            return false;

        uint trace = cave + TraceOffset;
        if (ReadWord(trace + MetadataOffset + 16u) != MetadataMagic)
            return false;

        uint storedSite = ReadWord(trace + MetadataOffset + 12u);
        if (storedSite != address)
            return false;

        var found = new Site
        {
            Address = address,
            Name = name,
            IsDispatcher = dispatcher,
            Cave = cave,
            OriginalInstruction = ReadWord(trace + MetadataOffset),
            OriginalDelay = ReadWord(trace + MetadataOffset + 4u),
            Target = ReadWord(trace + MetadataOffset + 8u)
        };

        uint[] expected = BuildCode(found, cave);
        int[] probes =
        {
            0, 1, 2, 30, 31, 32,
            expected.Length / 2,
            expected.Length - 2,
            expected.Length - 1
        };
        foreach (int index in probes)
        {
            if (index < 0 || index >= expected.Length)
                continue;
            if (ReadWord(cave + (uint)(index * 4)) != expected[index])
                return false;
        }

        site = found;
        return true;
    }

    private static Site[] GetInstalledSites()
    {
        var found = new List<Site>();
        foreach (Site template in CallSites)
        {
            if (TryGetInstalledSite(
                template.Address, template.Name, false, out Site site))
                found.Add(site);
        }

        if (TryGetInstalledSite(
            DispatcherSite, "共通ディスパッチャー JR v0", true, out Site dispatch))
            found.Add(dispatch);

        return found.ToArray();
    }

    internal static string Read()
    {
        lock (Sync)
        {
            RequirePaused();

            Site[] sites = GetInstalledSites();
            if (sites.Length == 0)
                throw new InvalidOperationException(
                    "多点観測フックが見つかりません。先に設置してください。");

            var sb = new StringBuilder();
            sb.AppendLine("ENEMY SKILL CANDIDATE MULTI-POINT TRACE");
            sb.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Installed points: " + sites.Length + " / 24");
            sb.AppendLine("Read only: YES");
            sb.AppendLine();

            foreach (Site site in sites)
            {
                uint trace = site.Cave + TraceOffset;
                uint count = ReadWord(trace);
                sb.AppendLine("==================================================");
                sb.Append("0x").Append(site.Address.ToString("X8"))
                  .Append("  ").AppendLine(site.Name);
                sb.Append("Call count: ").AppendLine(count.ToString());
                sb.Append("Original instruction: 0x")
                  .AppendLine(site.OriginalInstruction.ToString("X8"));
                if (!site.IsDispatcher)
                    sb.Append("Original delay: 0x")
                      .AppendLine(site.OriginalDelay.ToString("X8"));
                sb.Append("Target: ")
                  .AppendLine(site.IsDispatcher
                      ? "dynamic JR v0 (see $v0)"
                      : "0x" + site.Target.ToString("X8"));

                if (count == 0u)
                {
                    sb.AppendLine("No execution observed.");
                    sb.AppendLine();
                    continue;
                }

                string[] names =
                {
                    "zero","at","v0","v1","a0","a1","a2","a3",
                    "t0","t1","t2","t3","t4","t5","t6","t7",
                    "s0","s1","s2","s3","s4","s5","s6","s7",
                    "t8","t9","k0","k1","gp","sp","fp","ra"
                };

                for (int reg = 0; reg < 32; reg++)
                {
                    string value = ReadVector128(trace + 0x10u + (uint)(reg * 16));
                    sb.Append(names[reg].PadRight(5))
                      .Append(" 0x").AppendLine(value);
                }

                if (site.IsDispatcher)
                {
                    uint v0 = ReadWord(trace + 0x10u + 2u * 16u);
                    sb.Append("Resolved JR target: 0x")
                      .AppendLine(v0.ToString("X8"));
                }

                sb.AppendLine();
            }

            sb.AppendLine("注意: 各地点は独立して記録しています。Call countが増えた地点が実際に通過した候補です。");
            return sb.ToString();
        }
    }

    internal static string Restore()
    {
        lock (Sync)
        {
            RequirePaused();

            Site[] sites = GetInstalledSites();
            if (sites.Length == 0)
                return "解除対象の多点観測フックは見つかりませんでした。";

            // Validate every target before restoring any of them.
            foreach (Site site in sites)
            {
                uint current = ReadWord(site.Address);
                uint expectedJump = EncodeJ(site.Address, site.Cave);
                if (current != expectedJump)
                    throw new InvalidOperationException(
                        "フック命令が変更されているため一括解除を中止しました。\n" +
                        "Address: 0x" + site.Address.ToString("X8"));
                if (!site.IsDispatcher &&
                    ReadWord(site.Address + 4u) != 0u)
                    throw new InvalidOperationException(
                        "遅延スロットが変更されているため一括解除を中止しました。\n" +
                        "Address: 0x" + (site.Address + 4u).ToString("X8"));
            }

            foreach (Site site in sites)
            {
                WriteWord(site.Address, site.OriginalInstruction);
                if (!site.IsDispatcher)
                    WriteWord(site.Address + 4u, site.OriginalDelay);
            }

            foreach (Site site in sites)
            {
                if (ReadWord(site.Address) != site.OriginalInstruction ||
                    (!site.IsDispatcher &&
                     ReadWord(site.Address + 4u) != site.OriginalDelay))
                    throw new InvalidOperationException(
                        "元の命令への復元検証に失敗しました: 0x" +
                        site.Address.ToString("X8"));
            }

            return
                "多点観測フックを " + sites.Length + " 地点解除しました。\n" +
                "23か所のJALと共通ディスパッチャーの元の命令・遅延スロットを復元しました。\n" +
                "コードケーブは記録確認用に残しています。";
        }
    }
}
