using System;
using System.Collections.Generic;
using System.Threading;

namespace TornekoIjiriPort;

/// <summary>
/// スライムの指輪を装備した状態で、スライム系の仲間がレベルアップした場合に
/// 現在HP・最大HP・攻撃力・防御力へそれぞれ+1するEE側フック。
/// </summary>
internal sealed class SlimeRingLevelUpService : IDisposable
{
    private const uint HookAddress = 0x002C1198u;
    private const uint OriginalCallInstruction = 0x0C0B1D11u; // jal 0x002C7444
    private const uint OriginalDelayInstruction = 0xACC30014u; // sw v1,20(a2)
    private const uint OriginalCallTarget = 0x002C7444u;
    private const uint ContinueAddress = 0x002C11A0u;

    private const uint RingStateAddress = 0x00650112u;
    private const uint RingItemAddress = 0x0065011Cu;
    private const byte RingEquippedMask = 0x10;
    private const ushort SlimeRingItemId = 0x00A4;
    private const byte SlimeFamilyMask = 0x20;

    private const int MonsterRecordSize = 24;
    private const int MonsterRecordCount = 199;
    private const uint EeAddressMask = 0x1FFFFFFFu;
    private const uint EeMemoryBaseAddress = 0x00180000u;

    // 既存のアクション・呪文コードケーブ領域と重ならない領域を使用する。
    private const uint CaveSearchStart = 0x01DF0000u;
    private const uint CaveSearchEnd = 0x01D00000u;
    private const uint CaveSearchStep = 0x00001000u;
    private const uint CaveSize = 0x200u;
    private const uint CallCounterOffset = 0x1F0u;
    private const uint BonusCounterOffset = 0x1F4u;

    private readonly System.Threading.Timer _timer;
    private int _busy;
    private bool _disposed;
    private bool _installed;
    private int _processId;
    private uint _caveAddress;
    private uint _tableBaseAddress;
    private string _lastStatus = "";

    internal event Action<string>? StatusChanged;

    internal SlimeRingLevelUpService()
    {
        Pcsx2LiveSession.StateChanged += OnSessionChanged;
        _timer = new System.Threading.Timer(Poll, null, 1000, 1000);
    }

    private void OnSessionChanged()
    {
        Poll(null);
    }

    private void Poll(object? state)
    {
        if (_disposed || Interlocked.Exchange(ref _busy, 1) != 0)
            return;

        try
        {
            if (!Pcsx2LiveSession.IsConnected ||
                Pcsx2LiveSession.ProcessId <= 0 ||
                !Pcsx2LiveSession.VanillaDataLoaded ||
                Pcsx2LiveSession.VanillaBin == null)
            {
                return;
            }

            int processId = Pcsx2LiveSession.ProcessId;
            if (_processId != processId)
            {
                _processId = processId;
                _installed = false;
                _caveAddress = 0;
                _tableBaseAddress = 0;
            }

            if (!_installed)
            {
                uint status = ActionAutoRestoreService.GetSharedStatus();
                if (status != 0)
                {
                    RaiseStatus("スライムの指輪効果：PCSX2をゲーム実行状態にすると自動設置します。");
                    return;
                }

                Install(Pcsx2LiveSession.VanillaBin);
            }

            if (_installed)
            {
                uint currentHook = ActionAutoRestoreService.ReadShared32(HookAddress);
                uint expectedHook = EncodeJal(HookAddress, _caveAddress);

                if (currentHook != expectedHook)
                {
                    if (currentHook == OriginalCallInstruction)
                    {
                        _installed = false;
                        _caveAddress = 0;
                        RaiseStatus("スライムの指輪効果：フックが解除されたため再設置します。");
                        return;
                    }

                    throw new InvalidOperationException(
                        "レベルアップ処理のフック命令が外部から変更されています。安全のため停止しました。");
                }

                uint calls = ActionAutoRestoreService.ReadShared32(_caveAddress + CallCounterOffset);
                uint bonuses = ActionAutoRestoreService.ReadShared32(_caveAddress + BonusCounterOffset);
                RaiseStatus(
                    "スライムの指輪効果：有効 / レベルアップ処理検出 " +
                    calls.ToString() + "回 / ボーナス適用 " + bonuses.ToString() + "回");
            }
        }
        catch (Exception ex)
        {
            RaiseStatus("スライムの指輪効果：設置・監視エラー：" + ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void Install(byte[] bin)
    {
        uint tableBase = ResolveAndValidateMonsterTable(bin);
        uint currentInstruction = ActionAutoRestoreService.ReadShared32(HookAddress);
        uint currentDelay = ActionAutoRestoreService.ReadShared32(HookAddress + 4u);

        if (currentDelay != OriginalDelayInstruction)
        {
            throw new InvalidOperationException(
                "レベルアップ処理の遅延スロットが想定外です。現在値: 0x" +
                currentDelay.ToString("X8"));
        }

        if (currentInstruction != OriginalCallInstruction)
        {
            if (IsJal(currentInstruction))
            {
                uint existingCave = DecodeJal(currentInstruction);
                if (IsOurCode(existingCave, tableBase))
                {
                    _caveAddress = existingCave;
                    _tableBaseAddress = tableBase;
                    _installed = true;
                    return;
                }
            }

            throw new InvalidOperationException(
                "レベルアップ処理の命令が想定外です。現在値: 0x" +
                currentInstruction.ToString("X8"));
        }

        uint cave = FindCodeCave();
        uint[] code = BuildCode(cave, tableBase);

        for (int i = 0; i < code.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4),
                code[i]);
        }

        ActionAutoRestoreService.WriteShared32(cave + CallCounterOffset, 0);
        ActionAutoRestoreService.WriteShared32(cave + BonusCounterOffset, 0);

        if (!IsOurCode(cave, tableBase))
        {
            throw new InvalidOperationException(
                "スライムの指輪フックコードの書き込み検証に失敗しました。");
        }

        uint hook = EncodeJal(HookAddress, cave);
        ActionAutoRestoreService.WriteShared32(HookAddress, hook);

        if (ActionAutoRestoreService.ReadShared32(HookAddress) != hook)
        {
            ActionAutoRestoreService.WriteShared32(HookAddress, OriginalCallInstruction);
            throw new InvalidOperationException(
                "レベルアップ処理へのフック設置に失敗しました。");
        }

        _caveAddress = cave;
        _tableBaseAddress = tableBase;
        _installed = true;

        RaiseStatus(
            "スライムの指輪効果：フック設置完了。スライム系のレベルアップ時に4能力値を各+1します。");
    }

    private static uint ResolveAndValidateMonsterTable(byte[] bin)
    {
        if (bin.Length != 524288)
            throw new InvalidOperationException("取得済みBINのサイズが524288 bytesではありません。");

        uint rawPointer = BitConverter.ToUInt32(bin, 16);
        uint correction = (uint)bin[3] << 24;
        uint pointer = unchecked(rawPointer + correction);
        uint physicalAddress = pointer & EeAddressMask;

        if (physicalAddress < EeMemoryBaseAddress)
            throw new InvalidOperationException("モンスターテーブルのBINポインタが不正です。");

        long offset = (long)physicalAddress - EeMemoryBaseAddress + MonsterRecordSize;
        long requiredEnd = offset + (long)MonsterRecordCount * MonsterRecordSize;

        if (offset < 0 || requiredEnd > bin.Length)
            throw new InvalidOperationException("モンスターテーブルが取得済みBINの範囲外です。");

        // 種族IDは1始まり。ID 0の未使用レコードを含む先頭アドレスを返す。
        uint tableBase = physicalAddress;

        // BIN上の199レコードとPCSX2 RAM上の同じ位置を照合し、
        // テーブル基点の取り違えがあればフックを設置しない。
        for (int id = 1; id <= MonsterRecordCount; id++)
        {
            uint recordAddress = tableBase + (uint)(id * MonsterRecordSize);
            uint flagsWord = ActionAutoRestoreService.ReadShared32(recordAddress + 4u);
            byte liveFlags = (byte)(flagsWord >> 16);
            byte binFlags = bin[checked((int)offset) + (id - 1) * MonsterRecordSize + 6];

            if (liveFlags != binFlags)
            {
                throw new InvalidOperationException(
                    "モンスターテーブル照合に失敗しました。種族ID " + id +
                    " / BINフラグ 0x" + binFlags.ToString("X2") +
                    " / RAMフラグ 0x" + liveFlags.ToString("X2"));
            }
        }

        return tableBase;
    }

    private uint FindCodeCave()
    {
        for (uint candidate = CaveSearchStart;
             candidate >= CaveSearchEnd;
             candidate -= CaveSearchStep)
        {
            if (ActionAutoRestoreService.ReadShared32(candidate) != 0)
                continue;

            bool free = true;
            for (uint offset = 4; offset < CaveSize; offset += 4)
            {
                if (ActionAutoRestoreService.ReadShared32(candidate + offset) != 0)
                {
                    free = false;
                    break;
                }
            }

            if (free)
                return candidate;
        }

        throw new InvalidOperationException(
            "スライムの指輪フック用の空きコード領域が見つかりません。");
    }

    private static uint[] BuildCode(uint caveAddress, uint tableBase)
    {
        var code = new List<uint>();

        // 呼び出し元へ戻るアドレス、対象能力値ポインタ、s4を退避。
        code.Add(0x27BDFFF0u); // addiu sp,sp,-16
        code.Add(0xAFBF000Cu); // sw ra,12(sp)
        code.Add(0xAFA60008u); // sw a2,8(sp)
        code.Add(0xAFB40004u); // sw s4,4(sp)

        // フックがレベルアップ処理から呼ばれた回数を記録。
        AddAddress(code, 8, caveAddress + CallCounterOffset);
        code.Add(0x8D090000u); // lw t1,0(t0)
        code.Add(0x25290001u); // addiu t1,t1,1
        code.Add(0xAD090000u); // sw t1,0(t0)

        // 元の jal 0x002C7444 を実行する。
        code.Add(EncodeJal(caveAddress + (uint)(code.Count * 4), OriginalCallTarget));
        code.Add(0x00000000u); // nop

        var skipBranches = new List<(int Index, int Opcode, int Rs, int Rt)>();

        // スライムの指輪を装備しているか確認。
        code.Add(0x3C080065u); // lui t0,0x0065
        code.Add(0x91090112u); // lbu t1,0x0112(t0)
        code.Add(0x31290010u); // andi t1,t1,0x10
        AddBranchPlaceholder(code, skipBranches, 4, 9, 0);

        code.Add(0x9509011Cu); // lhu t1,0x011C(t0)
        code.Add(0x240A00A4u); // addiu t2,zero,0x00A4
        AddBranchPlaceholder(code, skipBranches, 5, 9, 10);

        // s4+8 が対象モンスター情報、+4 が1始まりの種族ID。
        code.Add(0x8E880008u); // lw t0,8(s4)
        code.Add(0x95090004u); // lhu t1,4(t0)
        code.Add(0x2D2A0001u); // sltiu t2,t1,1
        AddBranchPlaceholder(code, skipBranches, 5, 10, 0);
        code.Add(0x2D2A00C8u); // sltiu t2,t1,200
        AddBranchPlaceholder(code, skipBranches, 4, 10, 0);

        // レコードアドレス = テーブル基点 + (種族ID-1)*24。
        code.Add(0x00095080u); // sll t2,t1,4
        code.Add(0x000958C0u); // sll t3,t1,3
        code.Add(0x014B5021u); // addu t2,t2,t3
        AddAddress(code, 11, tableBase);
        code.Add(0x014B5021u); // addu t2,t2,t3
        code.Add(0x914A0006u); // lbu t2,6(t2)
        code.Add(0x314A0020u); // andi t2,t2,0x20
        AddBranchPlaceholder(code, skipBranches, 4, 10, 0);

        // a2に保存した能力値構造体の4項目をそれぞれ+1。
        // レベルアップ解析で確認した配置：HP +16、最大HP +20、
        // 攻撃力 +32、防御力 +36。
        code.Add(0x8FAC0008u); // lw t4,8(sp)
        code.Add(0x8D880010u); // lw t0,16(t4)
        code.Add(0x25080001u); // addiu t0,t0,1
        code.Add(0xAD880010u); // sw t0,16(t4)
        code.Add(0x8D880014u); // lw t0,20(t4)
        code.Add(0x25080001u); // addiu t0,t0,1
        code.Add(0xAD880014u); // sw t0,20(t4)
        code.Add(0x8D880020u); // lw t0,32(t4)
        code.Add(0x25080001u); // addiu t0,t0,1
        code.Add(0xAD880020u); // sw t0,32(t4)
        code.Add(0x8D880024u); // lw t0,36(t4)
        code.Add(0x25080001u); // addiu t0,t0,1
        code.Add(0xAD880024u); // sw t0,36(t4)

        // 実際にボーナスを適用した回数を記録。
        AddAddress(code, 8, caveAddress + BonusCounterOffset);
        code.Add(0x8D090000u); // lw t1,0(t0)
        code.Add(0x25290001u); // addiu t1,t1,1
        code.Add(0xAD090000u); // sw t1,0(t0)

        int exitIndex = code.Count;

        foreach (var branch in skipBranches)
        {
            int immediate = exitIndex - branch.Index - 1;
            code[branch.Index] =
                ((uint)branch.Opcode << 26) |
                ((uint)branch.Rs << 21) |
                ((uint)branch.Rt << 16) |
                (ushort)immediate;
        }

        // 元の関数の戻りアドレスとスタックを復元して続行。
        code.Add(0x8FB40004u); // lw s4,4(sp)
        code.Add(0x8FBF000Cu); // lw ra,12(sp)
        code.Add(0x27BD0010u); // addiu sp,sp,16
        code.Add(EncodeJump(caveAddress + (uint)(code.Count * 4), ContinueAddress));
        code.Add(0x00000000u); // nop

        if ((uint)(code.Count * 4) >= CallCounterOffset)
            throw new InvalidOperationException("フックコードがコード領域を超過しました。");

        return code.ToArray();
    }

    private static void AddBranchPlaceholder(
        List<uint> code,
        List<(int Index, int Opcode, int Rs, int Rt)> branches,
        int opcode,
        int rs,
        int rt)
    {
        int index = code.Count;
        code.Add(0);
        code.Add(0); // delay slot
        branches.Add((index, opcode, rs, rt));
    }

    private static void AddAddress(List<uint> code, int register, uint address)
    {
        code.Add(0x3C000000u | ((uint)register << 16) | ((address >> 16) & 0xFFFFu));
        code.Add(0x34000000u | ((uint)register << 21) | ((uint)register << 16) | (address & 0xFFFFu));
    }

    private static uint EncodeJal(uint fromAddress, uint targetAddress)
    {
        if (((fromAddress + 4u) & 0xF0000000u) != (targetAddress & 0xF0000000u))
            throw new InvalidOperationException("JALのジャンプ先が範囲外です。");

        return 0x0C000000u | ((targetAddress >> 2) & 0x03FFFFFFu);
    }

    private static uint EncodeJump(uint fromAddress, uint targetAddress)
    {
        if (((fromAddress + 4u) & 0xF0000000u) != (targetAddress & 0xF0000000u))
            throw new InvalidOperationException("J命令のジャンプ先が範囲外です。");

        return 0x08000000u | ((targetAddress >> 2) & 0x03FFFFFFu);
    }

    private static bool IsJal(uint instruction) =>
        (instruction & 0xFC000000u) == 0x0C000000u;

    private static uint DecodeJal(uint instruction) =>
        (instruction & 0x03FFFFFFu) << 2;

    private bool IsOurCode(uint cave, uint tableBase)
    {
        uint[] expected;
        try
        {
            expected = BuildCode(cave, tableBase);
        }
        catch
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (ActionAutoRestoreService.ReadShared32(cave + (uint)(i * 4)) != expected[i])
                return false;
        }

        return true;
    }

    private void RaiseStatus(string message)
    {
        if (_lastStatus == message)
            return;

        _lastStatus = message;
        StatusChanged?.Invoke(message);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Pcsx2LiveSession.StateChanged -= OnSessionChanged;
        _timer.Dispose();

        try
        {
            if (_installed &&
                Pcsx2LiveSession.IsConnected &&
                _caveAddress != 0)
            {
                uint expectedHook = EncodeJal(HookAddress, _caveAddress);
                uint current = ActionAutoRestoreService.ReadShared32(HookAddress);

                if (current == expectedHook)
                {
                    ActionAutoRestoreService.WriteShared32(
                        HookAddress,
                        OriginalCallInstruction);

                    if (ActionAutoRestoreService.ReadShared32(HookAddress) != OriginalCallInstruction)
                        throw new InvalidOperationException("フック命令の復元に失敗しました。");
                }
            }
        }
        catch
        {
            // PCSX2終了時など、接続できない場合は復元を試みられない。
        }
    }
}
