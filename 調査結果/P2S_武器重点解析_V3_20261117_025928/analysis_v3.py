import os
import sys
import csv
import struct
import hashlib
from collections import Counter

A_PATH, B_PATH, ELF_PATH, OUT = sys.argv[1:5]
os.makedirs(OUT, exist_ok=True)

def read(p):
    with open(p, "rb") as f:
        return f.read()

def sha(b):
    return hashlib.sha256(b).hexdigest().upper()

def hx(b):
    return " ".join("%02X" % x for x in b)

def write_csv(name, header, rows):
    with open(os.path.join(OUT, name), "w",
              encoding="utf-8-sig", errors="replace",
              newline="") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)

def write_text(name, lines):
    with open(os.path.join(OUT, name), "w",
              encoding="utf-8-sig", errors="replace") as f:
        f.write("\n".join(str(x) for x in lines))

def u32(b, p):
    return int.from_bytes(b[p:p+4], "little")

def s16(v):
    return v if v < 0x8000 else v - 0x10000

def reg(n):
    names = [
        "zero","at","v0","v1","a0","a1","a2","a3",
        "t0","t1","t2","t3","t4","t5","t6","t7",
        "s0","s1","s2","s3","s4","s5","s6","s7",
        "t8","t9","k0","k1","gp","sp","fp","ra"
    ]
    return "$" + names[n]

def decode(ins):
    op = (ins >> 26) & 63
    rs = (ins >> 21) & 31
    rt = (ins >> 16) & 31
    rd = (ins >> 11) & 31
    imm = ins & 0xFFFF
    simm = s16(imm)

    if op == 0x0F:
        return "lui %s,0x%04X" % (reg(rt), imm)
    if op == 0x09:
        return "addiu %s,%s,%d" % (reg(rt), reg(rs), simm)
    if op == 0x0D:
        return "ori %s,%s,0x%04X" % (reg(rt), reg(rs), imm)
    if op == 0x20:
        return "lb %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x21:
        return "lh %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x23:
        return "lw %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x24:
        return "lbu %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x25:
        return "lhu %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x28:
        return "sb %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x29:
        return "sh %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x2B:
        return "sw %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x2F:
        return "cache %s,%d(%s)" % (reg(rt), simm, reg(rs))
    if op == 0x02:
        return "j 0x%08X" % ((ins & 0x03FFFFFF) << 2)
    if op == 0x03:
        return "jal 0x%08X" % ((ins & 0x03FFFFFF) << 2)
    if op == 0:
        fn = ins & 63
        if fn == 0x21:
            return "addu %s,%s,%s" % (reg(rd), reg(rs), reg(rt))
        if fn == 0x2D:
            return "daddu %s,%s,%s" % (reg(rd), reg(rs), reg(rt))
        if fn == 0x08:
            return "jr %s" % reg(rs)
        if fn == 0x0D:
            return "break"
    return "UNKNOWN 0x%08X" % ins

def main():
    print("A/B/ELF読み込み")
    a = read(A_PATH)
    b = read(B_PATH)
    elf = read(ELF_PATH)

    if len(a) != len(b):
        raise RuntimeError("A/Bサイズ不一致")

    print("A size:", len(a))
    print("B size:", len(b))
    print("A SHA256:", sha(a))
    print("B SHA256:", sha(b))
    print("ELF size:", len(elf))
    print("ELF SHA256:", sha(elf))

    # --------------------------------------------------
    # 1. 1->4 / 4->1候補
    # --------------------------------------------------
    print("[1/7] 1->4 / 4->1全候補")

    positions_14 = []
    positions_41 = []

    for p in range(len(a)):
        if a[p] == 1 and b[p] == 4:
            positions_14.append(p)
        elif a[p] == 4 and b[p] == 1:
            positions_41.append(p)

    rows = []

    for label, positions in [
        ("1_TO_4", positions_14),
        ("4_TO_1", positions_41)
    ]:
        for p in positions:
            rows.append([
                label, "0x%08X" % p,
                a[p], b[p],
                hx(a[max(0,p-8):p+9]),
                hx(b[max(0,p-8):p+9])
            ])

    write_csv(
        "1_TO_4_4_TO_1全候補.csv",
        ["Pattern","Offset","A_Value","B_Value",
         "A_Neighborhood","B_Neighborhood"],
        rows
    )

    # --------------------------------------------------
    # 2. 注目アドレス周辺
    # --------------------------------------------------
    print("[2/7] 注目アドレス周辺ダンプ")

    targets = [
        0x003A65F4,
        0x00601DA8,
        0x003A61F8,
        0x003A64DC,
        0x00601A60,
        0x00601B14,
        0x00928DA8,
        0x00931720
    ]

    dump = []

    for target in targets:
        if target >= len(a):
            continue

        left = max(0, target-512)
        right = min(len(a), target+513)

        dump += [
            "",
            "="*100,
            "TARGET 0x%08X" % target,
            "A=うみどりの爪 / B=エレファントクロウ",
            "A中心16進:",
        ]

        for p in range(left, right, 16):
            q = min(p+16, right)
            aa = a[p:q]
            bb = b[p:q]
            marks = "".join(
                "^^" if aa[i] != bb[i] else "  "
                for i in range(len(aa))
            )

            dump.append(
                "0x%08X A %s" % (p, hx(aa))
            )
            dump.append(
                "0x%08X B %s" % (p, hx(bb))
            )
            dump.append("           D " + marks)

        dump.append("")
        dump.append("数値解釈:")

        for p in range(max(left, target-16),
                       min(right-8, target+17)):
            if p % 2:
                continue

            vals = []
            for width in [1,2,4,8]:
                if p+width <= len(a):
                    av = int.from_bytes(a[p:p+width], "little")
                    bv = int.from_bytes(b[p:p+width], "little")
                    if av != bv:
                        vals.append(
                            "u%d:%d->%d" % (width*8,av,bv)
                        )

            if vals:
                dump.append(
                    "0x%08X %s" % (p, " / ".join(vals))
                )

    write_text("注目アドレス周辺ダンプ.txt", dump)

    # --------------------------------------------------
    # 3. 連続・周期候補
    # --------------------------------------------------
    print("[3/7] 1->4候補と4->1候補の近接関係")

    relation_rows = []

    for p in positions_14:
        nearest = sorted(
            [(abs(q-p), q) for q in positions_41]
        )[:20]

        for distance, q in nearest:
            relation_rows.append([
                "0x%08X" % p,
                "0x%08X" % q,
                distance,
                "0x%X" % (q-p)
            ])

    write_csv(
        "1_TO_4から近い4_TO_1.csv",
        ["OneToFour","FourToOne","Distance","SignedDelta"],
        relation_rows
    )

    # --------------------------------------------------
    # 4. ELF仮想アドレス対応
    # --------------------------------------------------
    print("[4/7] ELF同一アドレス領域を確認")

    segments = [
        (0x00200000, 0x00000000, 0x0015D368),
        (0x0035D400, 0x0015D400, 0x005CC23C)
    ]

    def va_to_file(va):
        for vaddr, foff, length in segments:
            if vaddr <= va < vaddr+length:
                return foff + va-vaddr
        return None

    elf_dump = []

    for target in targets:
        foff = va_to_file(target)

        elf_dump += [
            "",
            "="*80,
            "VA 0x%08X" % target,
            "ELF file offset: " + (
                "0x%08X" % foff if foff is not None else "SEGMENT外"
            )
        ]

        if foff is not None:
            left = max(0, foff-128)
            right = min(len(elf), foff+129)

            for p in range(left, right, 16):
                elf_dump.append(
                    "0x%08X %s" % (p, hx(elf[p:p+16]))
                )

    write_text("ELF同一仮想アドレス周辺.txt", elf_dump)

    # --------------------------------------------------
    # 5. ELF内の正確なアドレス生成命令
    # --------------------------------------------------
    print("[5/7] ELFアドレス生成命令を正確に検索")

    elf_targets = [
        0x003A65F4,
        0x00601DA8,
        0x003A61F8,
        0x00601A60,
        0x00601B14,
        0x00928DA8,
        0x00931720
    ]

    instructions = []
    for p in range(0, len(elf)-3, 4):
        instructions.append((p, u32(elf,p)))

    exact_rows = []
    nearby_rows = []

    for idx, (foff, ins) in enumerate(instructions):
        op = (ins >> 26) & 63
        rt = (ins >> 16) & 31
        imm = ins & 0xFFFF

        if op != 0x0F:
            continue

        upper = (imm << 16) & 0xFFFFFFFF

        for j in range(idx+1, min(idx+9, len(instructions))):
            f2, ins2 = instructions[j]
            op2 = (ins2 >> 26) & 63
            rs2 = (ins2 >> 21) & 31
            rt2 = (ins2 >> 16) & 31
            imm2 = ins2 & 0xFFFF

            if rs2 != rt:
                continue

            if op2 == 0x09:
                value = (upper + s16(imm2)) & 0xFFFFFFFF
                kind = "ADDIU"
            elif op2 == 0x0D:
                value = upper | imm2
                kind = "ORI"
            else:
                continue

            for target in elf_targets:
                row = [
                    "0x%08X" % target,
                    "0x%08X" % value,
                    kind,
                    "0x%08X" % foff,
                    "0x%08X" % f2,
                    reg(rt),
                    reg(rt2),
                    "0x%08X" % ins,
                    "0x%08X" % ins2,
                    j-idx
                ]

                if value == target:
                    exact_rows.append(row)
                elif abs(value-target) <= 0x100:
                    nearby_rows.append(row)

    header = [
        "Target","Generated","Kind","LUI_FileOffset",
        "Next_FileOffset","LUI_Register","Destination",
        "LUI_Raw","Next_Raw","InstructionDistance"
    ]

    write_csv("ELF正確アドレス生成.csv", header, exact_rows)
    write_csv("ELF近接アドレス生成.csv", header, nearby_rows)

    # --------------------------------------------------
    # 6. 既知命令列の前後を逆アセンブル
    # --------------------------------------------------
    print("[6/7] 既知参照命令周辺を逆アセンブル")

    code_offsets = [
        0x00129C3C,
        0x0012ABC8,
        0x00129964,
        0x00129B08,
        0x0012A46C
    ]

    dis = []

    for center in code_offsets:
        dis += ["", "="*90, "CENTER FILE 0x%08X" % center]

        start = max(0, center-0x60)
        end = min(len(elf)-3, center+0x64)

        start -= start % 4

        for p in range(start, end, 4):
            ins = u32(elf,p)
            marker = " >>> " if p == center else "     "
            dis.append(
                marker + "0x%08X 0x%08X %s" %
                (p, ins, decode(ins))
            )

    write_text("ELF既知参照命令逆アセンブル.txt", dis)

    # --------------------------------------------------
    # 7. 総合結果
    # --------------------------------------------------
    print("[7/7] 総合結果")

    report = [
        "P2S 武器重点解析 V3",
        "",
        "A SHA256: "+sha(a),
        "B SHA256: "+sha(b),
        "ELF SHA256: "+sha(elf),
        "",
        "1->4候補数: %d" % len(positions_14),
        "4->1候補数: %d" % len(positions_41),
        "",
        "1->4候補:"
    ]

    for p in positions_14:
        report.append("0x%08X" % p)

    report += ["", "4->1候補の先頭100件:"]

    for p in positions_41[:100]:
        report.append("0x%08X" % p)

    report += [
        "",
        "ELF正確アドレス生成候補数: %d" % len(exact_rows),
        "ELF近接アドレス生成候補数: %d" % len(nearby_rows),
        "",
        "注意:",
        "メモリ差分だけでは攻撃力と確定できない。",
        "ELFの命令参照と実行時の構造体・配列の対応確認が必要。",
        "元BIN、ELF、P2Sは変更していない。"
    ]

    write_text("重点解析結果.txt", report)

    print("")
    print("===================================")
    print(" V3解析終了")
    print("===================================")
    print("1->4:", len(positions_14))
    print("4->1:", len(positions_41))
    print("ELF exact:", len(exact_rows))
    print("ELF nearby:", len(nearby_rows))
    print("出力:", OUT)

if __name__ == "__main__":
    main()