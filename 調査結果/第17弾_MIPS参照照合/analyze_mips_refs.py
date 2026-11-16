import zipfile
import struct
import hashlib
import csv
import math
from pathlib import Path
from collections import Counter, defaultdict

BASE = Path(r"C:\Users\penelope\Desktop\トルネコいじり作業環境")
WEAPON = BASE / "うみどりの爪装備してスライムベスの目の前.p2s"
BARE = BASE / "素手でスライムベスの目の前.p2s"
ELF = BASE / "Torneko3_MainCandidate.elf"
OUT = BASE / "第17弾_MIPS参照照合"

TARGET_START = 0x005FA000
TARGET_END   = 0x005FB000

SPECIAL = [
    0x005FA438,
    0x005FA442,
    0x005FA454,
    0x005FA554,
    0x005FA770,
    0x005FA774,
    0x005FA790,
]

def sha(data):
    return hashlib.sha256(data).hexdigest()

def read_p2s(path):
    with zipfile.ZipFile(path, "r") as z:
        bad = z.testzip()
        if bad:
            raise RuntimeError(f"ZIP破損: {path.name}: {bad}")
        return z.read("eeMemory.bin")

def hx(v):
    return f"0x{v:08X}"

def signed16(x):
    return x - 0x10000 if x & 0x8000 else x

def read_elf_segments(data):
    if data[:4] != b"\x7fELF":
        raise RuntimeError("ELF magic不一致")
    if data[4] != 1:
        raise RuntimeError("ELF32ではありません")
    if data[5] != 1:
        raise RuntimeError("Little Endian ELFではありません")

    e_phoff = struct.unpack_from("<I", data, 28)[0]
    e_phentsize = struct.unpack_from("<H", data, 42)[0]
    e_phnum = struct.unpack_from("<H", data, 44)[0]

    segments = []
    for i in range(e_phnum):
        p = e_phoff + i * e_phentsize
        if p + 32 > len(data):
            continue
        typ, off, va, pa, filesz, memsz, flags, align = struct.unpack_from(
            "<IIIIIIII", data, p
        )
        if typ == 1 and filesz:
            segments.append({
                "index": i,
                "offset": off,
                "va": va,
                "pa": pa,
                "filesz": filesz,
                "memsz": memsz,
                "flags": flags,
                "align": align,
                "executable": bool(flags & 1),
            })
    return segments

def regname(r):
    names = [
        "$zero","$at","$v0","$v1","$a0","$a1","$a2","$a3",
        "$t0","$t1","$t2","$t3","$t4","$t5","$t6","$t7",
        "$s0","$s1","$s2","$s3","$s4","$s5","$s6","$s7",
        "$t8","$t9","$k0","$k1","$gp","$sp","$fp","$ra"
    ]
    return names[r]

MEM_OPS = {
    0x20:"LB", 0x21:"LH", 0x23:"LW",
    0x24:"LBU",0x25:"LHU",
    0x28:"SB", 0x29:"SH", 0x2B:"SW",
    0x1A:"LQ", 0x1B:"SQ",
    0x31:"LWC1",0x39:"SWC1",
    0x1E:"LQ",0x1F:"SQ",
}

def analyze_code(data, segments):
    refs = []
    builds = []
    all_ins = []

    for seg in segments:
        if not seg["executable"]:
            continue

        start = seg["offset"]
        end = min(len(data), start + seg["filesz"])
        code = data[start:end]
        regs = {}

        for pos in range(0, len(code)-3, 4):
            word = struct.unpack_from("<I", code, pos)[0]
            va = seg["va"] + pos
            op = (word >> 26) & 0x3F
            rs = (word >> 21) & 31
            rt = (word >> 16) & 31
            rd = (word >> 11) & 31
            funct = word & 63
            imm = word & 0xFFFF
            simm = signed16(imm)

            desc = f"RAW={word:08X}"
            eff = None

            if op == 0x0F:  # LUI
                value = (imm << 16) & 0xFFFFFFFF
                regs[rt] = value
                desc = f"LUI {regname(rt)},0x{imm:04X}"

            elif op == 0x09:  # ADDIU
                if rs in regs:
                    value = (regs[rs] + simm) & 0xFFFFFFFF
                    regs[rt] = value
                    desc = f"ADDIU {regname(rt)},{regname(rs)},{simm}"
                    if TARGET_START <= value < TARGET_END:
                        builds.append((va, word, value, desc))
                else:
                    regs.pop(rt, None)
                    desc = f"ADDIU {regname(rt)},{regname(rs)},{simm}"

            elif op == 0x0D:  # ORI
                if rs in regs:
                    value = regs[rs] | imm
                    regs[rt] = value & 0xFFFFFFFF
                    desc = f"ORI {regname(rt)},{regname(rs)},0x{imm:04X}"
                    if TARGET_START <= value < TARGET_END:
                        builds.append((va, word, value, desc))
                else:
                    regs.pop(rt, None)
                    desc = f"ORI {regname(rt)},{regname(rs)},0x{imm:04X}"

            elif op in MEM_OPS:
                name = MEM_OPS[op]
                if rs in regs:
                    eff = (regs[rs] + simm) & 0xFFFFFFFF
                    desc = f"{name} {regname(rt)},{simm}({regname(rs)})"
                    if TARGET_START <= eff < TARGET_END:
                        refs.append((va, word, eff, name, desc))
                else:
                    desc = f"{name} {regname(rt)},{simm}({regname(rs)})"
                if op in (0x20,0x21,0x23,0x24,0x25,0x1A,0x1B,0x31):
                    regs.pop(rt, None)

            elif op == 0 and funct in (0x21,0x2D,0x25):  # ADDU/DADDU/OR
                if rs in regs and rt in regs:
                    if funct == 0x25:
                        value = regs[rs] | regs[rt]
                    else:
                        value = (regs[rs] + regs[rt]) & 0xFFFFFFFF
                    regs[rd] = value
                    desc = f"ALU {regname(rd)},{regname(rs)},{regname(rt)}"
                    if TARGET_START <= value < TARGET_END:
                        builds.append((va, word, value, desc))
                else:
                    regs.pop(rd, None)

            else:
                # レジスタの値を不確かなまま引き継がない
                if op in (0x02,0x03,0x04,0x05,0x06,0x07,0x14,0x15,0x16,0x17):
                    regs = {}
                elif op == 0 and funct == 8:
                    regs = {}

            all_ins.append((va, word, desc))

    return refs, builds, all_ins

def scan_pointer_constants(data, targets):
    rows = []
    for target in targets:
        pat = struct.pack("<I", target)
        pos = 0
        while True:
            pos = data.find(pat, pos)
            if pos < 0:
                break
            rows.append((target, pos))
            pos += 1
    return rows

def main():
    print("P2S読み込み...")
    a = read_p2s(WEAPON)
    b = read_p2s(BARE)

    if len(a) != len(b):
        raise RuntimeError("EEメモリサイズ不一致")

    diffs = [i for i,(x,y) in enumerate(zip(a,b)) if x != y]
    print("EEサイズ:",len(a))
    print("差分バイト:",len(diffs))
    print("装備SHA256:",sha(a))
    print("素手SHA256:",sha(b))

    # 5FA領域の全差分
    rows = []
    for p in diffs:
        if TARGET_START <= p < TARGET_END:
            rows.append([
                hx(p),f"{a[p]:02X}",f"{b[p]:02X}",
                a[p],b[p],b[p]-a[p]
            ])

    with (OUT/"5FA領域_全差分.csv").open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(["EEアドレス","装備HEX","素手HEX","装備10進","素手10進","差"])
        w.writerows(rows)

    # 重点アドレスの前後ダンプと数値解釈
    with (OUT/"重点アドレス_周辺解析.txt").open("w",encoding="utf-8") as f:
        for p in SPECIAL:
            f.write("\n"+"="*80+"\n")
            f.write(f"ADDRESS {hx(p)}\n")
            for q in range(p-32,p+33,16):
                aa=a[q:q+16]
                bb=b[q:q+16]
                f.write(f"{q:08X} A:{aa.hex(' ').upper():47} B:{bb.hex(' ').upper()}\n")
            for width in (1,2,4):
                av=int.from_bytes(a[p:p+width],"little")
                bv=int.from_bytes(b[p:p+width],"little")
                f.write(f"LE{width*8}: A={av} B={bv} HEX={av:X}/{bv:X}\n")
            if p+4 <= len(a):
                af=struct.unpack("<f",a[p:p+4])[0]
                bf=struct.unpack("<f",b[p:p+4])[0]
                f.write(f"FLOAT32: A={af!r} B={bf!r}\n")

    print("ELF読み込み...")
    elf=ELF.read_bytes()
    segments=read_elf_segments(elf)

    with (OUT/"ELF_LOAD_SEGMENTS.csv").open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(["Index","FileOffset","VirtualAddress","PhysicalAddress","FileSize","MemSize","Flags","Executable"])
        for s in segments:
            w.writerow([s["index"],hx(s["offset"]),hx(s["va"]),hx(s["pa"]),
                        hx(s["filesz"]),hx(s["memsz"]),hex(s["flags"]),s["executable"]])

    refs,builds,ins=analyze_code(elf,segments)

    with (OUT/"MIPS_5FA直接メモリアクセス候補.csv").open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(["命令VA","命令WORD","参照EEアドレス","命令","逆アセンブル"])
        for va,word,eff,name,desc in refs:
            w.writerow([hx(va),f"{word:08X}",hx(eff),name,desc])

    with (OUT/"MIPS_5FAアドレス構築候補.csv").open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(["命令VA","命令WORD","構築アドレス","説明"])
        for va,word,value,desc in builds:
            w.writerow([hx(va),f"{word:08X}",hx(value),desc])

    # ELF内に絶対アドレス値そのものが格納されているか
    ptrs=scan_pointer_constants(elf,SPECIAL)
    with (OUT/"ELF_絶対アドレス定数検索.csv").open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(["対象アドレス","ELFファイルオフセット"])
        for target,pos in ptrs:
            w.writerow([hx(target),hx(pos)])

    # 1E000付近の差分を周期別に調べる
    low_start=0x0001E000
    low_end=0x00020000
    low=[p for p in diffs if low_start <= p < low_end]
    period_rows=[]
    for period in (0x10,0x20,0x40,0x80,0x100,0x200,0x400):
        counts=Counter(p % period for p in low)
        common=counts.most_common(12)
        for offset,count in common:
            period_rows.append([hex(period),hex(offset),count])
    with (OUT/"1E領域_周期性.csv").open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(["周期","周期内オフセット","差分数"])
        w.writerows(period_rows)

    # 1E領域の差分位置と値を抜き出す
    with (OUT/"1E領域_差分一覧.csv").open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(["EEアドレス","装備HEX","素手HEX","装備DWORD","素手DWORD"])
        for p in low:
            av=int.from_bytes(a[p:p+4],"little")
            bv=int.from_bytes(b[p:p+4],"little")
            w.writerow([hx(p),f"{a[p]:02X}",f"{b[p]:02X}",av,bv])

    # ELF命令参照候補の周辺命令
    ref_vas=set(x[0] for x in refs)
    build_vas=set(x[0] for x in builds)
    centers=ref_vas|build_vas
    with (OUT/"MIPS_参照候補周辺.txt").open("w",encoding="utf-8") as f:
        for center in sorted(centers):
            f.write("\n"+"="*90+"\n")
            f.write(f"CENTER {hx(center)}\n")
            for va,word,desc in ins:
                if center-32 <= va <= center+32:
                    f.write(f"{hx(va)} {word:08X} {desc}\n")

    with (OUT/"解析概要.txt").open("w",encoding="utf-8") as f:
        f.write("第17弾 MIPS参照照合\n")
        f.write("="*70+"\n")
        f.write(f"装備EE SHA256: {sha(a)}\n")
        f.write(f"素手EE SHA256: {sha(b)}\n")
        f.write(f"EEサイズ: {len(a)}\n")
        f.write(f"全差分バイト: {len(diffs)}\n")
        f.write(f"5FA領域差分バイト: {len(rows)}\n")
        f.write(f"5FA領域MIPSメモリアクセス候補: {len(refs)}\n")
        f.write(f"5FA領域アドレス構築候補: {len(builds)}\n")
        f.write(f"ELF内絶対アドレス定数ヒット: {len(ptrs)}\n")
        f.write(f"1E000-20000差分バイト: {len(low)}\n")
        f.write("\n重点アドレス:\n")
        for p in SPECIAL:
            f.write(f"{hx(p)} A={a[p]:02X} B={b[p]:02X}\n")
        f.write("\n注意:\n")
        f.write("MIPSの静的な直接参照が見つからなくても、動的ポインタ経由のアクセスは否定できない。\n")
        f.write("本解析はP2S/ELFの読み取りのみで、元ファイルを変更していない。\n")

    print("解析完了")
    print("出力:",OUT)
    print("MIPS直接参照候補:",len(refs))
    print("アドレス構築候補:",len(builds))
    print("ELF内定数ヒット:",len(ptrs))
    print("5FA差分:",len(rows))
    print("1E領域差分:",len(low))

if __name__=="__main__":
    main()
