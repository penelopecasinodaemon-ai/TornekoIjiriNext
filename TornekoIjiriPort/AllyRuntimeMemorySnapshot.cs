using System;
using System.IO;
using System.Text;

namespace TornekoIjiriPort;

internal static class AllyRuntimeMemorySnapshot
{
    private sealed class MemoryRange
    {
        internal uint Start { get; }
        internal uint End { get; }
        internal string Name { get; }
        internal bool DecodeInstructions { get; }

        internal MemoryRange(
            uint start, uint end, string name,
            bool decodeInstructions = true)
        {
            Start = start;
            End = end;
            Name = name;
            DecodeInstructions = decodeInstructions;
        }
    }

    private static readonly MemoryRange[] Ranges =
    {

        // 敵側の特技呼び出し元
        new MemoryRange(
            0x00243580u, 0x00243620u,
            "敵特技呼び出し元 0x002435C4"),

        // dgEnemyAttack本体
        new MemoryRange(
            0x0023C600u, 0x0023C900u,
            "敵特技処理 dgEnemyAttack 0x0023C750"),

        // 特技使用率の読み込み候補
        new MemoryRange(
            0x0020F880u, 0x0020F900u,
            "特技使用率読み込み候補"),

        // 使用率読み込み先の関数候補
        new MemoryRange(
            0x0021D600u, 0x0021D720u,
            "特技使用率参照関数候補"),

        new MemoryRange(
            0x002B3B00u, 0x002B4800u,
            "S4関連・間接呼び出し候補"),

        new MemoryRange(
            0x002B8800u, 0x002B8B00u,
            "追加の呼び出し元"),

        new MemoryRange(
            0x0029ABD0u, 0x0029AD20u,
            "共通ヘルパー"),

        new MemoryRange(
            0x002CF5C0u, 0x002CF800u,
            "特技ディスパッチ周辺"),

        new MemoryRange(
            0x002D0000u, 0x002D0480u,
            "特技処理周辺"),

        new MemoryRange(
            0x002D72C0u, 0x002D7420u,
            "JALR候補 0x002D72C4 / 0x002D73B4"),

        new MemoryRange(
            0x002DF7D0u, 0x002DF900u,
            "行動候補処理"),

        new MemoryRange(
            0x002F7100u, 0x002F7500u,
            "行動選択処理"),

        new MemoryRange(0x002D08A0u, 0x002D1100u, "特技処理分岐の追加候補"),

        new MemoryRange(0x002D76A0u, 0x002D8B00u, "追加ディスパッチ・特技処理候補"),

        new MemoryRange(
        0x002B3B00u, 0x002B4800u,
        "S4関連・間接呼び出し候補"),

        new MemoryRange(
        0x002B8800u, 0x002B8B00u,
        "追加の呼び出し元"),

        new MemoryRange(
        0x0029ABD0u, 0x0029AD20u,
        "共通ヘルパー"),

        new MemoryRange(
        0x002CF000u, 0x002CF5C0u,
        "特技ディスパッチャー入口・分岐条件"),

        new MemoryRange(
        0x002CF5C0u, 0x002CF800u,
        "特技ディスパッチ分岐"),

        new MemoryRange(
        0x002CF800u, 0x002CFC00u,
        "特技ディスパッチ後半"),

        new MemoryRange(
        0x002CFC00u, 0x002D0000u,
        "特技ディスパッチ先"),

        new MemoryRange(
        0x002D0000u, 0x002D0480u,
        "特技処理周辺"),

        new MemoryRange(
        0x002D0480u, 0x002D1200u,
        "追加の特技ハンドラ"),

        new MemoryRange(
        0x002D72C0u, 0x002D7420u,
        "JALR候補"),

        new MemoryRange(
        0x002DF7D0u, 0x002DF900u,
        "行動候補処理"),

        new MemoryRange(
        0x002F7100u, 0x002F7500u,
        "行動選択処理"),

        new MemoryRange(
        0x002D76A0u, 0x002D8B00u,
        "追加ディスパッチ・特技処理候補"),

        new MemoryRange(
        0x00918C70u, 0x00919670u,
        "味方特技ルート関数テーブル"),

        new MemoryRange(
    0x002D4800u, 0x002D4900u,
    "味方特技ルート候補5の処理"),

        new MemoryRange(
    0x002D4700u, 0x002D4900u,
    "味方特技ルート候補5の関数全体"),

        new MemoryRange(
    0x002D4400u, 0x002D4720u,
    "味方特技ルート候補5の呼び出し元"),

        new MemoryRange(
    0x00918EE0u, 0x00918F20u,
    "味方特技ルート候補5の分岐テーブル"),

        new MemoryRange(
    0x002D3B00u, 0x002D3D80u,
    "味方特技呼び出し先 0x002D3C10 の関数本体"),

        new MemoryRange(
            0x002D3A80u, 0x002D3B00u,
            "味方特技呼び出し先の前半ループ・初期化"),

        new MemoryRange(
            0x002D48A0u, 0x002D5530u,
            "味方特技実行パラメータの後続処理・使用箇所"),

        new MemoryRange(
            0x002D3D80u, 0x002D3E80u,
            "味方特技パラメータ計算ヘルパー 0x002D3DAC / 0x002D3DF4"),

        new MemoryRange(
            0x002D5560u, 0x002D5600u,
            "味方特技パラメータ参照ヘルパー 0x002D5560"),

        // 0x002D4D8C～0x002D4E2C が参照・更新する8バイト作業レコード候補。
        // 0x005F4C40 はコード内で比較される境界値。前後を生データとして採取する。
        new MemoryRange(
            0x005F4B40u, 0x005F4D40u,
            "味方特技8バイト作業レコード候補・境界0x005F4C40周辺（生データ）",
            false),

    };

    private static void RequirePaused()
    {
        uint status = ActionAutoRestoreService.GetSharedStatus();

        if (status != 1u)
        {
            throw new InvalidOperationException(
                "PCSX2が一時停止状態ではありません。\r\n" +
                "PCSX2を一時停止してから実行してください。\r\n" +
                "現在のPINE Status: " + status);
        }
    }

    private static string DecodeInstruction(uint address, uint word)
    {
        uint opcode = word >> 26;
        uint rs = (word >> 21) & 31u;
        uint rt = (word >> 16) & 31u;
        uint rd = (word >> 11) & 31u;
        uint funct = word & 63u;

        if (opcode == 2u || opcode == 3u)
        {
            uint target =
                ((address + 4u) & 0xF0000000u) |
                ((word & 0x03FFFFFFu) << 2);

            return (opcode == 2u ? "J" : "JAL") +
                " 0x" + target.ToString("X8");
        }

        if (opcode == 0u && funct == 8u)
        {
            return "JR " + RegisterName(rs);
        }

        if (opcode == 0u && funct == 9u)
        {
            return "JALR " + RegisterName(rd) +
                "," + RegisterName(rs);
        }

        if (opcode == 4u || opcode == 5u)
        {
            int displacement =
                unchecked((short)(word & 0xFFFFu)) * 4;

            uint target = unchecked(
                address + 4u + (uint)displacement);

            return (opcode == 4u ? "BEQ " : "BNE ") +
                RegisterName(rs) + "," +
                RegisterName(rt) +
                ",0x" + target.ToString("X8");
        }

        if (opcode == 6u || opcode == 7u)
        {
            int displacement =
                unchecked((short)(word & 0xFFFFu)) * 4;

            uint target = unchecked(
                address + 4u + (uint)displacement);

            return (opcode == 6u ? "BLEZ " : "BGTZ ") +
                RegisterName(rs) +
                ",0x" + target.ToString("X8");
        }

        if (opcode == 1u)
        {
            int displacement =
                unchecked((short)(word & 0xFFFFu)) * 4;

            uint target = unchecked(
                address + 4u + (uint)displacement);

            string branchName = rt switch
            {
                0u => "BLTZ",
                1u => "BGEZ",
                16u => "BLTZAL",
                17u => "BGEZAL",
                _ => "REGIMM"
            };

            return branchName + " " +
                RegisterName(rs) +
                ",0x" + target.ToString("X8");
        }

        return "";
    }

    private static string RegisterName(uint register)
    {
        string[] names =
        {
            "zero", "at", "v0", "v1",
            "a0", "a1", "a2", "a3",
            "t0", "t1", "t2", "t3",
            "t4", "t5", "t6", "t7",
            "s0", "s1", "s2", "s3",
            "s4", "s5", "s6", "s7",
            "t8", "t9", "k0", "k1",
            "gp", "sp", "fp", "ra"
        };

        return names[register & 31u];
    }

    internal static string Capture()
    {
        RequirePaused();

        string desktop =
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory);

        string fileName =
            "特技_実行時メモリ採取_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss") +
            ".txt";

        string path = Path.Combine(desktop, fileName);

        int totalWords = 0;
        int controlInstructions = 0;

        using (var writer = new StreamWriter(
            path,
            false,
            new UTF8Encoding(false)))
        {
            writer.WriteLine("TORNEKO SKILL RUNTIME MEMORY SNAPSHOT");
            writer.WriteLine("READ ONLY - NO MEMORY WRITES");
            writer.WriteLine(
                "Date: " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            writer.WriteLine(
                "PCSX2 Status: " +
                ActionAutoRestoreService.GetSharedStatus());

            writer.WriteLine();

            foreach (MemoryRange range in Ranges)
            {
                writer.WriteLine(
                    "==================================================");

                writer.WriteLine(
                    range.Name);

                writer.WriteLine(
                    "START: 0x" + range.Start.ToString("X8"));

                writer.WriteLine(
                    "END:   0x" + range.End.ToString("X8"));

                writer.WriteLine(
                    "==================================================");

                for (uint address = range.Start;
                     address < range.End;
                     address += 4u)
                {
                    uint word =
                        ActionAutoRestoreService.ReadShared32(address);

                    string decoded = range.DecodeInstructions
                        ? DecodeInstruction(address, word)
                        : "";

                    writer.Write(
                        "0x" + address.ToString("X8") +
                        " : 0x" + word.ToString("X8"));

                    if (!string.IsNullOrEmpty(decoded))
                    {
                        writer.Write("    ; " + decoded);
                        controlInstructions++;
                    }

                    writer.WriteLine();
                    totalWords++;
                }

                writer.WriteLine();
                writer.Flush();
            }

            uint finalStatus =
                ActionAutoRestoreService.GetSharedStatus();

            writer.WriteLine("==================================================");
            writer.WriteLine("SNAPSHOT COMPLETE");
            writer.WriteLine("Total words: " + totalWords);
            writer.WriteLine(
                "Control-flow instructions: " +
                controlInstructions);
            writer.WriteLine(
                "Final PCSX2 Status: " + finalStatus);

            if (finalStatus != 1u)
            {
                writer.WriteLine(
                    "WARNING: PCSX2 status changed during capture.");
            }
        }

        return
            "実行時メモリの採取が完了しました。\r\n\r\n" +
            "ファイル: " + path + "\r\n\r\n" +
            "読み取りワード数: " + totalWords + "\r\n" +
            "制御命令検出数: " + controlInstructions + "\r\n\r\n" +
            "メモリへの書き込みは行っていません。";
    }
}