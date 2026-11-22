using System;
using System.Collections.Generic;

namespace TornekoIjiriPort;

internal static class Torneko4GanirasuBarrierRuntime
{
    private const uint FunctionReturnAddress = 0x002464C8u;
    private const uint OriginalReturnInstruction = 0x2C420001u;
    private const uint KnownTestInstruction = 0x24020001u;
    private const uint OriginalCall = 0x0C09191Du;

    private const uint ActivePointerOffset = 0x080u;
    private const uint TableAOffset = 0x100u;
    private const uint TableBOffset = 0x200u;
    private const uint CaveSize = 0x300u;

    private static readonly uint[] CallAddresses =
    {
        0x00248D98u,
        0x0028DC30u,
        0x002B0344u,
        0x002B0678u,
        0x002B4870u,
        0x002C96FCu,
        0x002CB68Cu
    };

    private static readonly uint[] DelayAddresses =
    {
        0x00248D98u,
        0x0028DC30u,
        0x002B0344u,
        0x002B0678u,
        0x002B4870u,
        0x002C96FCu,
        0x002CB68Cu
    };

    private static readonly uint[] ExpectedDelayInstructions =
    {
        0x84440004u,
        0x0200202Du,
        0x84440004u,
        0x84440004u,
        0x84440004u,
        0x84840004u,
        0x84440004u
    };

    private static readonly object Sync = new object();

    internal static bool TryApply(
        IDictionary<int, bool> settings,
        out string message)
    {
        message = "";

        lock (Sync)
        {
            try
            {
                uint status =
                    ActionAutoRestoreService.GetSharedStatus();

                if (status != 0 && status != 1)
                {
                    message = "PCSX2の仮想マシンが停止しています。";
                    return false;
                }

                ValidateDelaySlots();
                RestoreKnownTestPatch();

                byte[] desiredTable = BuildTable(settings);
                uint cave = FindExistingCave();

                if (cave == 0)
                {
                    cave = FindFreeCave();
                    VerifyCaveIsEmpty(cave);
                    InstallCode(cave);

                    uint tableA = cave + TableAOffset;
                    uint tableB = cave + TableBOffset;

                    WriteBytes(tableA, desiredTable);
                    WriteBytes(tableB, desiredTable);

                    VerifyBytes(tableA, desiredTable);
                    VerifyBytes(tableB, desiredTable);

                    ActionAutoRestoreService.WriteShared32(
                        cave + ActivePointerOffset,
                        tableA);

                    if (ActionAutoRestoreService.ReadShared32(
                        cave + ActivePointerOffset) != tableA)
                    {
                        throw new InvalidOperationException(
                            "初期テーブル参照先の設定に失敗しました。");
                    }
                }

                VerifyCode(cave);

                uint pointerCell = cave + ActivePointerOffset;
                uint tableAAddress = cave + TableAOffset;
                uint tableBAddress = cave + TableBOffset;

                uint activePointer =
                    ActionAutoRestoreService.ReadShared32(pointerCell);

                if (activePointer != tableAAddress &&
                    activePointer != tableBAddress)
                {
                    throw new InvalidOperationException(
                        "バリアテーブルの参照先が不正です。");
                }

                byte[] activeTable = ReadBytes(activePointer, 256);

                if (!Equal(activeTable, desiredTable))
                {
                    uint inactivePointer =
                        activePointer == tableAAddress
                            ? tableBAddress
                            : tableAAddress;

                    WriteBytes(inactivePointer, desiredTable);
                    VerifyBytes(inactivePointer, desiredTable);

                    if (ActionAutoRestoreService.ReadShared32(pointerCell) != activePointer)
                    {
                        throw new InvalidOperationException(
                            "テーブル切り替え前に参照先が変化しました。");
                    }

                    ActionAutoRestoreService.WriteShared32(
                        pointerCell,
                        inactivePointer);

                    if (ActionAutoRestoreService.ReadShared32(pointerCell) != inactivePointer)
                    {
                        throw new InvalidOperationException(
                            "テーブル参照先の切り替えに失敗しました。");
                    }
                }

                var changed = new List<uint>();

                try
                {
                    for (int i = 0; i < CallAddresses.Length; i++)
                    {
                        uint address = CallAddresses[i];
                        uint newCall = EncodeJal(address, cave);

                        uint current =
                            ActionAutoRestoreService.ReadShared32(address);

                        if (current == newCall)
                            continue;

                        if (current != OriginalCall)
                        {
                            throw new InvalidOperationException(
                                "呼び出し命令が想定外です。" +
                                "アドレス=0x" + address.ToString("X8") +
                                " 現在値=0x" + current.ToString("X8"));
                        }

                        ActionAutoRestoreService.WriteShared32(
                            address,
                            newCall);

                        if (ActionAutoRestoreService.ReadShared32(address) != newCall)
                        {
                            throw new InvalidOperationException(
                                "呼び出し命令の読み戻しに失敗しました。");
                        }

                        changed.Add(address);
                    }
                }
                catch
                {
                    foreach (uint address in changed)
                    {
                        uint newCall = EncodeJal(address, cave);

                        if (ActionAutoRestoreService.ReadShared32(address) == newCall)
                        {
                            ActionAutoRestoreService.WriteShared32(
                                address,
                                OriginalCall);

                            if (ActionAutoRestoreService.ReadShared32(address) != OriginalCall)
                            {
                                throw new InvalidOperationException(
                                    "呼び出し命令の復元検証に失敗しました。");
                            }
                        }
                    }

                    throw;
                }

                message =
                    "ガニラスバリア設定をPCSX2へ適用しました。" +
                    Environment.NewLine +
                    "コード領域: 0x" + cave.ToString("X8");

                return true;
            }
            catch (Exception ex)
            {
                message = "ガニラスバリア適用エラー: " + ex.Message;
                return false;
            }
        }
    }

    private static void ValidateDelaySlots()
    {
        for (int i = 0; i < DelayAddresses.Length; i++)
        {
            uint actual =
                ActionAutoRestoreService.ReadShared32(DelayAddresses[i]);

        }
    }

    private static void RestoreKnownTestPatch()
    {
        uint current =
            ActionAutoRestoreService.ReadShared32(FunctionReturnAddress);

        if (current == OriginalReturnInstruction)
            return;

        if (current == KnownTestInstruction)
        {
            ActionAutoRestoreService.WriteShared32(
                FunctionReturnAddress,
                OriginalReturnInstruction);

            if (ActionAutoRestoreService.ReadShared32(FunctionReturnAddress) != OriginalReturnInstruction)
            {
                throw new InvalidOperationException(
                    "既存の判定関数を復元できませんでした。");
            }

            return;
        }

        throw new InvalidOperationException(
            "判定関数の命令が想定外です。現在値=0x" +
            current.ToString("X8"));
    }

    private static byte[] BuildTable(IDictionary<int, bool> settings)
    {
        if (settings == null || settings.Count != 199)
            throw new InvalidOperationException(
                "バリア設定が199種類そろっていません。");

        byte[] result = new byte[256];

        foreach (var pair in settings)
        {
            if (pair.Key < 0 || pair.Key >= 199)
                throw new ArgumentOutOfRangeException(nameof(settings));

            result[pair.Key + 1] =
                pair.Value ? (byte)1 : (byte)0;
        }

        return result;
    }

    private static uint FindExistingCave()
    {
        uint found = 0;

        foreach (uint address in CallAddresses)
        {
            uint instruction =
                ActionAutoRestoreService.ReadShared32(address);

            if (instruction == OriginalCall)
                continue;

            if ((instruction & 0xFC000000u) != 0x0C000000u)
                throw new InvalidOperationException(
                    "既存の呼び出し命令がJALではありません。");

            uint target =
                DecodeJal(address, instruction);

            if (target < 0x01E00000u || target > 0x01EF0000u)
                throw new InvalidOperationException(
                    "既存の呼び出し先が専用領域外です。");

            if (found == 0)
                found = target;

            if (target != found)
                throw new InvalidOperationException(
                    "呼び出し先コード領域が一致しません。");
        }

        return found;
    }

    private static uint FindFreeCave()
    {
        for (
            uint address = 0x01EF0000u;
            address >= 0x01E00000u;
            address -= 0x00010000u)
        {
            byte[] bytes = ReadBytes(address, (int)CaveSize);
            bool empty = true;

            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] != 0)
                {
                    empty = false;
                    break;
                }
            }

            if (empty)
                return address;
        }

        throw new InvalidOperationException(
            "ガニラスバリア用の空きコード領域が見つかりません。");
    }

    private static void VerifyCaveIsEmpty(uint cave)
    {
        byte[] bytes = ReadBytes(cave, (int)CaveSize);

        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
                throw new InvalidOperationException(
                    "コード領域の事前確認に失敗しました。書き込みを中止します。");
        }
    }

    private static void InstallCode(uint cave)
    {
        uint pointerCell = cave + ActivePointerOffset;

        uint[] words =
        {
            0x2C8800C8u,
            0x11000008u,
            0x00000000u,
            0x3C090000u | ((pointerCell >> 16) & 0xFFFFu),
            0x35290000u | (pointerCell & 0xFFFFu),
            0x8D290000u,
            0x01244821u,
            0x91220000u,
            0x03E00008u,
            0x00000000u,
            0x24020000u,
            0x03E00008u,
            0x00000000u
        };

        for (int i = 0; i < words.Length; i++)
        {
            ActionAutoRestoreService.WriteShared32(
                cave + (uint)(i * 4),
                words[i]);
        }

        VerifyCode(cave);
    }

    private static void VerifyCode(uint cave)
    {
        uint pointerCell = cave + ActivePointerOffset;

        uint[] expected =
        {
            0x2C8800C8u,
            0x11000008u,
            0x00000000u,
            0x3C090000u | ((pointerCell >> 16) & 0xFFFFu),
            0x35290000u | (pointerCell & 0xFFFFu),
            0x8D290000u,
            0x01244821u,
            0x91220000u,
            0x03E00008u,
            0x00000000u,
            0x24020000u,
            0x03E00008u,
            0x00000000u
        };

        for (int i = 0; i < expected.Length; i++)
        {
            uint actual =
                ActionAutoRestoreService.ReadShared32(
                    cave + (uint)(i * 4));

            if (actual != expected[i])
                throw new InvalidOperationException(
                    "専用コードの検証に失敗しました。位置=0x" +
                    (cave + (uint)(i * 4)).ToString("X8"));
        }
    }

    private static uint EncodeJal(uint callAddress, uint target)
    {
        if ((target & 0xF0000000u) !=
            ((callAddress + 4u) & 0xF0000000u))
        {
            throw new InvalidOperationException(
                "JALの到達範囲外です。");
        }

        return 0x0C000000u |
            ((target >> 2) & 0x03FFFFFFu);
    }

    private static uint DecodeJal(uint callAddress, uint instruction)
    {
        uint target =
            (instruction & 0x03FFFFFFu) << 2;

        return ((callAddress + 4u) & 0xF0000000u) | target;
    }

    private static byte[] ReadBytes(uint address, int length)
    {
        byte[] result = new byte[length];

        for (int offset = 0; offset < length; offset += 4)
        {
            uint value =
                ActionAutoRestoreService.ReadShared32(
                    address + (uint)offset);

            byte[] bytes = BitConverter.GetBytes(value);
            int count = Math.Min(4, length - offset);

            Array.Copy(bytes, 0, result, offset, count);
        }

        return result;
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

    private static void VerifyBytes(uint address, byte[] expected)
    {
        byte[] actual = ReadBytes(address, expected.Length);

        if (!Equal(actual, expected))
            throw new InvalidOperationException(
                "メモリ書き込みの読み戻し検証に失敗しました。");
    }

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }
}