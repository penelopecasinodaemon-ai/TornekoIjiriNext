import os
import sys
import csv
import math
import struct
import hashlib
import statistics
from collections import Counter, defaultdict

FILE_A = sys.argv[1]
FILE_B = sys.argv[2]
ELF_FILE = sys.argv[3]
OUT_DIR = sys.argv[4]

os.makedirs(OUT_DIR, exist_ok=True)

def log(s):
    print(s, flush=True)

def sha(data):
    return hashlib.sha256(data).hexdigest().upper()

def out(name):
    return os.path.join(OUT_DIR, name)

def write_csv(name, header, rows):
    with open(out(name), "w", encoding="utf-8-sig", errors="replace", newline="") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)

def write_text(name, lines):
    with open(out(name), "w", encoding="utf-8-sig", errors="replace", newline="") as f:
        f.write("\n".join(str(x) for x in lines))

def read_bin(path):
    with open(path, "rb") as f:
        return f.read()

def u16(b, p):
    return int.from_bytes(b[p:p+2], "little")

def u32(b, p):
    return int.from_bytes(b[p:p+4], "little")

def s16(b, p):
    return int.from_bytes(b[p:p+2], "little", signed=True)

def s32(b, p):
    return int.from_bytes(b[p:p+4], "little", signed=True)

def hexbytes(b):
    return " ".join("%02X" % x for x in b)

def changed_positions(a, b):
    return [i for i in range(len(a)) if a[i] != b[i]]

def make_runs(pos, gap):
    if not pos:
        return []

    result = []
    start = prev = pos[0]
    count = 1

    for p in pos[1:]:
        if p - prev - 1 <= gap:
            prev = p
            count += 1
        else:
            result.append((start, prev, count))
            start = prev = p
            count = 1

    result.append((start, prev, count))
    return result

def save_runs(name, runs):
    rows = []
    for i, (s, e, count) in enumerate(runs, 1):
        rows.append([
            i, s, "0x%08X" % s,
            e, "0x%08X" % e,
            e-s+1, count
        ])
    write_csv(
        name,
        ["No", "Start_DEC", "Start_HEX", "End_DEC",
         "End_HEX", "Span_Size", "Changed_Bytes"],
        rows
    )

def main():
    log("A/Bメモリを読み込みます。")

    a = read_bin(FILE_A)
    b = read_bin(FILE_B)

    if len(a) != len(b):
        raise RuntimeError("A/Bのサイズが一致しません。")

    size = len(a)

    log("Size: %d" % size)
    log("A SHA256: " + sha(a))
    log("B SHA256: " + sha(b))

    # ------------------------------------------------------------
    # 1. 全差分位置
    # ------------------------------------------------------------

    log("")
    log("[1/12] 全差分位置を調査")

    positions = changed_positions(a, b)

    log("差分バイト数: %d" % len(positions))

    write_csv(
        "全差分バイト.csv",
        ["Offset_DEC", "Offset_HEX", "A_HEX", "B_HEX",
         "A_DEC", "B_DEC", "Delta_B_minus_A"],
        [
            [p, "0x%08X" % p, "%02X" % a[p], "%02X" % b[p],
             a[p], b[p], b[p]-a[p]]
            for p in positions
        ]
    )

    # ------------------------------------------------------------
    # 2. 差分間隔別の領域化
    # ------------------------------------------------------------

    log("[2/12] 差分領域の複数方式分類")

    run_sets = {}

    for gap in [0, 1, 2, 4, 8, 16, 32, 64]:
        runs = make_runs(positions, gap)
        run_sets[gap] = runs
        save_runs("差分領域_gap_%d.csv" % gap, runs)
        log("gap=%d regions=%d" % (gap, len(runs)))

    # ------------------------------------------------------------
    # 3. ページ単位統計
    # ------------------------------------------------------------

    log("[3/12] ページ統計")

    page_counts = Counter(p // 4096 for p in positions)

    page_rows = []

    for page, count in sorted(
        page_counts.items(), key=lambda x: (-x[1], x[0])
    ):
        start = page * 4096
        end = min(start + 4095, size - 1)

        page_rows.append([
            page, "0x%08X" % start,
            "0x%08X" % end,
            count,
            round(count / (end-start+1) * 100, 4)
        ])

    write_csv(
        "全差分ページ統計.csv",
        ["Page", "Start_HEX", "End_HEX",
         "Changed_Bytes", "Change_Percent"],
        page_rows
    )

    # ------------------------------------------------------------
    # 4. 数値解釈
    # ------------------------------------------------------------

    log("[4/12] 数値候補を大量抽出")

    numeric_rows = []

    for p in positions:
        for width in [1, 2, 4, 8]:
            start = max(0, p - width + 1)

            for off in range(start, p + 1):
                if off + width > size:
                    continue

                aa = a[off:off+width]
                bb = b[off:off+width]

                if aa == bb:
                    continue

                ua = int.from_bytes(aa, "little", signed=False)
                ub = int.from_bytes(bb, "little", signed=False)

                sa = int.from_bytes(aa, "little", signed=True)
                sb = int.from_bytes(bb, "little", signed=True)

                if width == 4:
                    try:
                        fa = struct.unpack("<f", aa)[0]
                        fb = struct.unpack("<f", bb)[0]
                    except Exception:
                        fa = float("nan")
                        fb = float("nan")
                else:
                    fa = ""
                    fb = ""

                # 大きすぎる数値は候補表から除外
                plausible = (
                    ua <= 1000000 and ub <= 1000000
                )

                if plausible:
                    numeric_rows.append([
                        "0x%08X" % off,
                        width,
                        hexbytes(aa),
                        hexbytes(bb),
                        ua, ub, ub-ua,
                        sa, sb, sb-sa,
                        fa, fb
                    ])

    write_csv(
        "数値解釈候補.csv",
        ["Offset_HEX", "Width", "A_Bytes", "B_Bytes",
         "A_UInt", "B_UInt", "Delta_UInt",
         "A_SInt", "B_SInt", "Delta_SInt",
         "A_Float32", "B_Float32"],
        numeric_rows
    )

    # ------------------------------------------------------------
    # 5. 攻撃力候補の数値変化を重点検索
    # ------------------------------------------------------------

    log("[5/12] 1→4、4→1などの数値候補検索")

    target_rows = []

    for width in [1, 2, 4]:
        for p in range(0, size-width+1):
            aa = a[p:p+width]
            bb = b[p:p+width]

            if aa == bb:
                continue

            ua = int.from_bytes(aa, "little")
            ub = int.from_bytes(bb, "little")

            # 武器攻撃力候補として小さな数値変化を拾う
            if ua <= 255 and ub <= 255:
                target_rows.append([
                    "0x%08X" % p,
                    width,
                    hexbytes(aa),
                    hexbytes(bb),
                    ua,
                    ub,
                    ub-ua,
                    "1_TO_4" if (ua, ub) == (1, 4) else
                    "4_TO_1" if (ua, ub) == (4, 1) else
                    "OTHER"
                ])

    write_csv(
        "小数値変化候補.csv",
        ["Offset_HEX", "Width", "A_HEX", "B_HEX",
         "A_Value", "B_Value", "Delta", "Pattern"],
        target_rows
    )

    # ------------------------------------------------------------
    # 6. 全差分領域の周辺ダンプ
    # ------------------------------------------------------------

    log("[6/12] 全差分領域の周辺ダンプ")

    runs = run_sets[0]
    dump_lines = []

    dump_lines.append("全差分領域 周辺ダンプ")
    dump_lines.append("A=うみどりの爪")
    dump_lines.append("B=エレファントクロウ")
    dump_lines.append("領域数=%d" % len(runs))

    for n, (s, e, count) in enumerate(runs, 1):
        left = max(0, s-32)
        right = min(size, e+33)

        dump_lines.append("")
        dump_lines.append("="*90)
        dump_lines.append(
            "REGION %d START=0x%08X END=0x%08X SIZE=%d CHANGED=%d" %
            (n, s, e, e-s+1, count)
        )

        for p in range(left, right, 16):
            q = min(p+16, right)

            aa = a[p:q]
            bb = b[p:q]

            dump_lines.append(
                "0x%08X A %s" % (p, hexbytes(aa))
            )
            dump_lines.append(
                "0x%08X B %s" % (p, hexbytes(bb))
            )

    write_text("全差分領域周辺ダンプ.txt", dump_lines)

    # ------------------------------------------------------------
    # 7. 大きな差分領域をまとめる
    # ------------------------------------------------------------

    log("[7/12] 大規模領域の集約")

    large_runs = []

    for s, e, count in run_sets[64]:
        if e-s+1 >= 128 or count >= 64:
            large_runs.append((s, e, count))

    large_rows = []

    for n, (s, e, count) in enumerate(large_runs, 1):
        left = max(0, s-64)
        right = min(size, e+65)

        large_rows.append([
            n,
            "0x%08X" % s,
            "0x%08X" % e,
            e-s+1,
            count,
            " ".join("%02X" % x for x in a[left:min(left+32,right)]),
            " ".join("%02X" % x for x in b[left:min(left+32,right)])
        ])

    write_csv(
        "大規模差分領域.csv",
        ["No", "Start", "End", "Span", "Changed",
         "A_Head", "B_Head"],
        large_rows
    )

    # ------------------------------------------------------------
    # 8. 差分の繰り返し周期
    # ------------------------------------------------------------

    log("[8/12] 繰り返し周期・規則性解析")

    gap_counter = Counter()

    for x, y in zip(positions, positions[1:]):
        gap_counter[y-x] += 1

    periodic_rows = []

    for gap, count in gap_counter.most_common(200):
        periodic_rows.append([gap, count])

    write_csv(
        "差分位置間隔頻度.csv",
        ["Gap_Bytes", "Frequency"],
        periodic_rows
    )

    # 差分位置を大きな領域ごとに分類し、各領域の間隔を集計
    clusters = []

    for s, e, count in run_sets[64]:
        if e-s+1 >= 256 or count >= 100:
            clusters.append((s, e, count))

    cluster_rows = []

    for n, (s, e, count) in enumerate(clusters, 1):
        local = [p for p in positions if s <= p <= e]

        gaps = [
            local[i+1]-local[i]
            for i in range(len(local)-1)
        ]

        freq = Counter(gaps)

        common = ";".join(
            "%d:%d" % (k, v)
            for k, v in freq.most_common(10)
        )

        cluster_rows.append([
            n,
            "0x%08X" % s,
            "0x%08X" % e,
            len(local),
            min(gaps) if gaps else "",
            max(gaps) if gaps else "",
            round(statistics.mean(gaps), 3) if gaps else "",
            common
        ])

    write_csv(
        "大規模領域内の周期.csv",
        ["No", "Start", "End", "Changed",
         "MinGap", "MaxGap", "AverageGap", "CommonGaps"],
        cluster_rows
    )

    # ------------------------------------------------------------
    # 9. 16バイト単位のブロック比較
    # ------------------------------------------------------------

    log("[9/12] 16/32/64バイトブロック解析")

    block_rows = []

    for width in [8, 16, 24, 32, 48, 64, 80, 96, 128]:
        for p in range(0, size-width+1, width):
            aa = a[p:p+width]
            bb = b[p:p+width]

            diff = sum(x != y for x, y in zip(aa, bb))

            if diff:
                block_rows.append([
                    width,
                    "0x%08X" % p,
                    diff,
                    round(diff/width*100, 2),
                    hexbytes(aa),
                    hexbytes(bb)
                ])

    write_csv(
        "固定長ブロック比較.csv",
        ["BlockWidth", "Offset", "ChangedBytes",
         "ChangedPercent", "A_Bytes", "B_Bytes"],
        block_rows
    )

    # ------------------------------------------------------------
    # 10. 文字列抽出
    # ------------------------------------------------------------

    log("[10/12] ASCII / UTF16LE文字列抽出")

    string_rows = []

    def extract_strings(data, label, encoding, min_len=5):
        if encoding == "ascii":
            run = []
            start = None

            for i, x in enumerate(data):
                if 32 <= x <= 126:
                    if start is None:
                        start = i
                    run.append(chr(x))
                else:
                    if len(run) >= min_len:
                        string_rows.append([
                            label, encoding,
                            "0x%08X" % start,
                            "".join(run)
                        ])
                    run = []
                    start = None

        elif encoding == "utf16":
            i = 0
            while i+1 < len(data):
                start = i
                chars = []

                while i+1 < len(data):
                    v = int.from_bytes(data[i:i+2], "little")

                    if 32 <= v <= 126 or v >= 0x3000:
                        try:
                            chars.append(chr(v))
                        except Exception:
                            break
                        i += 2
                    else:
                        break

                if len(chars) >= min_len:
                    string_rows.append([
                        label, encoding,
                        "0x%08X" % start,
                        "".join(chars)
                    ])

                i = max(i+2, start+2)

    extract_strings(a, "A", "ascii")
    extract_strings(b, "B", "ascii")
    extract_strings(a, "A", "utf16")
    extract_strings(b, "B", "utf16")

    write_csv(
        "メモリ内文字列.csv",
        ["Memory", "Encoding", "Offset", "String"],
        string_rows
    )

    # ------------------------------------------------------------
    # 11. ELFアドレス生成命令を検索
    # ------------------------------------------------------------

    log("[11/12] ELFのアドレス生成命令を調査")

    elf_rows = []
    elf_info = []

    if os.path.isfile(ELF_FILE):
        elf = read_bin(ELF_FILE)

        elf_info.append("ELF_SIZE=%d" % len(elf))
        elf_info.append("ELF_SHA256=" + sha(elf))

        # PH1 / PH2既知配置
        segments = [
            (0x00200000, 0x00000000, 0x0015D368),
            (0x0035D400, 0x0015D400, 0x005CC23C)
        ]

        def va_to_file(va):
            for vaddr, foff, length in segments:
                if vaddr <= va < vaddr+length:
                    return foff + (va-vaddr)
            return None

        # MIPS little endian 32-bit words
        words = []
        for p in range(0, len(elf)-3, 4):
            words.append((p, int.from_bytes(elf[p:p+4], "little")))

        # LUI rt, imm -> ADDIU/ORI same register within next 8 instructions
        targets = {
            0x00928DA8: "旧候補_00928DA8",
            0x00931720: "旧候補_00931720",
            0x00928DA8 & 0xFFFFF000: "00928xxx_page",
            0x00929000: "00929000",
            0x0039F000: "0039F000",
            0x005FA000: "005FA000",
            0x00600000: "00600000",
            0x008B7000: "008B7000"
        }

        for idx, (foff, ins) in enumerate(words):
            op = (ins >> 26) & 0x3F
            rt = (ins >> 16) & 0x1F
            imm = ins & 0xFFFF

            if op != 0x0F:
                continue

            upper = (imm << 16) & 0xFFFFFFFF

            for j in range(idx+1, min(idx+9, len(words))):
                f2, ins2 = words[j]

                op2 = (ins2 >> 26) & 0x3F
                rs2 = (ins2 >> 21) & 0x1F
                rt2 = (ins2 >> 16) & 0x1F
                imm2 = ins2 & 0xFFFF

                if rs2 != rt:
                    continue

                if op2 == 0x09:
                    signed = imm2 if imm2 < 0x8000 else imm2-0x10000
                    value = (upper + signed) & 0xFFFFFFFF
                    kind = "ADDIU"

                elif op2 == 0x0D:
                    value = upper | imm2
                    kind = "ORI"

                else:
                    continue

                for target, label in targets.items():
                    if value == target or (value & 0xFFFFF000) == (target & 0xFFFFF000):
                        elf_rows.append([
                            label,
                            "0x%08X" % target,
                            "0x%08X" % value,
                            kind,
                            "0x%08X" % foff,
                            "0x%08X" % f2,
                            "LUI_RT=%d" % rt,
                            "NEXT_RT=%d" % rt2,
                            "0x%08X" % ins,
                            "0x%08X" % ins2,
                            j-idx
                        ])

        # 0x0093xxxx の上位生成を伴う命令列を広めに抽出
        for idx, (foff, ins) in enumerate(words):
            op = (ins >> 26) & 0x3F
            rt = (ins >> 16) & 0x1F
            imm = ins & 0xFFFF

            if op != 0x0F or imm != 0x0093:
                continue

            for j in range(idx+1, min(idx+13, len(words))):
                f2, ins2 = words[j]
                op2 = (ins2 >> 26) & 0x3F
                rs2 = (ins2 >> 21) & 0x1F
                imm2 = ins2 & 0xFFFF

                if rs2 != rt:
                    continue

                if op2 in (0x09, 0x0D, 0x0B):
                    signed = imm2 if imm2 < 0x8000 else imm2-0x10000

                    if op2 == 0x09:
                        value = (0x00930000 + signed) & 0xFFFFFFFF
                        kind = "ADDIU"
                    elif op2 == 0x0D:
                        value = 0x00930000 | imm2
                        kind = "ORI"
                    else:
                        value = (0x00930000 + signed) & 0xFFFFFFFF
                        kind = "SLTIU"

                    elf_rows.append([
                        "LUI_0093_SEQUENCE",
                        "0x%08X" % value,
                        "0x%08X" % value,
                        kind,
                        "0x%08X" % foff,
                        "0x%08X" % f2,
                        "REG=%d" % rt,
                        "",
                        "0x%08X" % ins,
                        "0x%08X" % ins2,
                        j-idx
                    ])

        elf_info.append("ELF address sequences found=%d" % len(elf_rows))

    else:
        elf_info.append("ELF_FILE_NOT_FOUND")
        elf_info.append("ELF解析はスキップしました。")

    write_csv(
        "ELFアドレス生成候補.csv",
        ["Label", "Target", "GeneratedValue", "Instruction",
         "LUI_FileOffset", "Next_FileOffset", "Register1",
         "Register2", "LUI_Raw", "Next_Raw", "InstructionDistance"],
        elf_rows
    )

    # ------------------------------------------------------------
    # 12. 総合レポート
    # ------------------------------------------------------------

    log("[12/12] 総合レポート作成")

    report = []

    report += [
        "P2S 武器総合詳細解析 V2",
        "",
        "A: うみどりの爪",
        "B: エレファントクロウ",
        "",
        "EE Memory size: %d" % size,
        "A SHA256: " + sha(a),
        "B SHA256: " + sha(b),
        "Changed bytes: %d" % len(positions),
        "",
        "差分領域数:",
    ]

    for gap in sorted(run_sets):
        report.append(
            "gap=%d regions=%d" %
            (gap, len(run_sets[gap]))
        )

    report += [
        "",
        "上位差分ページ:",
    ]

    for row in page_rows[:80]:
        report.append(
            "%s count=%d percent=%s%%" %
            (row[1], row[3], row[4])
        )

    report += [
        "",
        "差分間隔の上位:",
    ]

    for gap, count in gap_counter.most_common(80):
        report.append(
            "gap=%d count=%d" % (gap, count)
        )

    report += [
        "",
        "大規模差分領域:",
    ]

    for s, e, count in clusters:
        report.append(
            "0x%08X-0x%08X span=%d changed=%d" %
            (s, e, e-s+1, count)
        )

    report += [
        "",
        "数値候補数: %d" % len(numeric_rows),
        "小数値変化候補数: %d" % len(target_rows),
        "文字列候補数: %d" % len(string_rows),
        "ELFアドレス生成候補数: %d" % len(elf_rows),
        "",
        "ELF情報:",
    ]

    report += elf_info

    report += [
        "",
        "重要な注意:",
        "武器交換時の差分は、装備データ以外のメモリ変化も含みます。",
        "攻撃力フィールドと断定するには、複数セーブでの再現性と",
        "ELF側の読み書き命令との対応確認が必要です。",
        "今回の出力は候補抽出であり、元メモリは変更していません。"
    ]

    write_text("総合解析結果.txt", report)

    log("")
    log("============================================")
    log(" 総合解析終了")
    log("============================================")
    log("差分バイト: %d" % len(positions))
    log("差分領域 gap=0: %d" % len(run_sets[0]))
    log("大規模領域: %d" % len(clusters))
    log("数値候補: %d" % len(numeric_rows))
    log("小数値候補: %d" % len(target_rows))
    log("文字列候補: %d" % len(string_rows))
    log("ELF候補: %d" % len(elf_rows))
    log("")

if __name__ == "__main__":
    main()