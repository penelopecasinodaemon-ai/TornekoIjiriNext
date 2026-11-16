import os, sys, struct, csv, hashlib
from collections import defaultdict

elf_path = sys.argv[1]
out_dir = sys.argv[2]
os.makedirs(out_dir, exist_ok=True)

def write_text(name, text):
    with open(os.path.join(out_dir, name), "w", encoding="utf-8-sig") as f:
        f.write(text)

def write_csv(name, rows, fields):
    with open(os.path.join(out_dir, name), "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)

data = open(elf_path, "rb").read()
if data[:4] != b"\x7fELF":
    raise RuntimeError("ELF magicが一致しません")
if data[4] != 1 or data[5] != 1:
    raise RuntimeError("ELF32 little-endianではありません")

eh = struct.unpack_from("<16sHHIIIIIHHHHHH", data, 0)
phoff, phentsize, phnum = eh[5], eh[9], eh[10]

segments = []
for i in range(phnum):
    p = struct.unpack_from("<IIIIIIII", data, phoff + i * phentsize)
    p_type, off, va, pa, filesz, memsz, flags, align = p
    if p_type == 1:
        segments.append({
            "off": off, "va": va, "filesz": filesz,
            "memsz": memsz, "flags": flags
        })

exec_segments = [s for s in segments if s["flags"] & 1]
if not exec_segments:
    raise RuntimeError("実行可能セグメントがありません")

# VA -> instruction map
ins = {}
for seg in exec_segments:
    start = seg["off"]
    end = start + seg["filesz"]
    for off in range(start, end - 3, 4):
        word = struct.unpack_from("<I", data, off)[0]
        va = seg["va"] + off - start
        ins[va] = word

regs = [
    "zero","at","v0","v1","a0","a1","a2","a3",
    "t0","t1","t2","t3","t4","t5","t6","t7",
    "s0","s1","s2","s3","s4","s5","s6","s7",
    "t8","t9","k0","k1","gp","sp","fp","ra"
]

def rn(n):
    return "$" + regs[n & 31]

def sx(v, bits=16):
    return v - (1 << bits) if v & (1 << (bits-1)) else v

def decode(va, w):
    op = w >> 26
    rs = (w >> 21) & 31
    rt = (w >> 16) & 31
    rd = (w >> 11) & 31
    sa = (w >> 6) & 31
    fn = w & 63
    imm = sx(w & 0xffff)
    target = ((va + 4) & 0xf0000000) | ((w & 0x03ffffff) << 2)

    if op == 0:
        names = {
            0x00:"SLL",0x02:"SRL",0x03:"SRA",
            0x08:"JR",0x09:"JALR",
            0x10:"MFHI",0x11:"MTHI",0x12:"MFLO",0x13:"MTLO",
            0x18:"MULT",0x19:"MULTU",0x1A:"DIV",0x1B:"DIVU",
            0x20:"ADD",0x21:"ADDU",0x22:"SUB",0x23:"SUBU",
            0x24:"AND",0x25:"OR",0x26:"XOR",0x27:"NOR",
            0x2A:"SLT",0x2B:"SLTU",0x2D:"DADDU"
        }
        name = names.get(fn, "SPECIAL_%02X" % fn)
        if fn in (0x18,0x19,0x1A,0x1B):
            return f"{name} {rn(rs)},{rn(rt)}"
        if fn in (0x10,0x12):
            return f"{name} {rn(rd)}"
        if fn in (0x11,0x13):
            return f"{name} {rn(rs)}"
        if fn in (0x08,):
            return f"JR {rn(rs)}"
        if fn in (0x09,):
            return f"JALR {rn(rd)},{rn(rs)}"
        if fn in (0x00,0x02,0x03):
            return f"{name} {rn(rd)},{rn(rt)},{sa}"
        return f"{name} {rn(rd)},{rn(rs)},{rn(rt)}"

    if op in (2,3):
        return ("J" if op == 2 else "JAL") + f" 0x{target:08X}"

    if op in (4,5,6,7,20,21,22,23):
        names = {4:"BEQ",5:"BNE",6:"BLEZ",7:"BGTZ",
                 20:"BEQL",21:"BNEL",22:"BLEZL",23:"BGTZL"}
        dest = (va + 4 + (imm << 2)) & 0xffffffff
        return f"{names[op]} {rn(rs)},{rn(rt)},0x{dest:08X}"

    if op in (8,9,10,11,12,13,14,15,24,25):
        names = {8:"ADDI",9:"ADDIU",10:"SLTI",11:"SLTIU",
                 12:"ANDI",13:"ORI",14:"XORI",15:"LUI",
                 24:"DADDI",25:"DADDIU"}
        if op == 15:
            return f"LUI {rn(rt)},0x{w & 0xffff:04X}"
        return f"{names[op]} {rn(rt)},{rn(rs)},{imm if op not in (12,13,14) else hex(w & 0xffff)}"

    loads = {32:"LB",33:"LH",34:"LWL",35:"LW",36:"LBU",
             37:"LHU",38:"LWR",39:"LWU",40:"SB",41:"SH",
             42:"SWL",43:"SW",46:"SWR",55:"LD",63:"SD"}
    if op in loads:
        return f"{loads[op]} {rn(rt)},{imm}({rn(rs)})"

    if op == 1:
        return f"REGIMM {rn(rs)},{rn(rt)},imm={imm}"

    return f"OP{op:02X} rs={rn(rs)} rt={rn(rt)} rd={rn(rd)} imm={imm}"

def line(va):
    w = ins.get(va)
    if w is None:
        return f"0x{va:08X} <範囲外>"
    return f"0x{va:08X} {w:08X} {decode(va,w)}"

addrs = sorted(ins)
index = {a:i for i,a in enumerate(addrs)}

# 1. 全乗算命令と近傍のHI/LO処理を調査
mult_rows = []
mult_report = []
for va in addrs:
    w = ins[va]
    op = w >> 26
    fn = w & 63
    is_mult = op == 0 and fn in (0x18,0x19,0x1A,0x1B)
    if not is_mult:
        continue
    i = index[va]
    nearby = addrs[max(0,i-8):min(len(addrs),i+17)]
    body = [line(x) for x in nearby]
    mflo = [x for x in nearby if (ins[x] >> 26) == 0 and (ins[x] & 63) == 0x12]
    mfhi = [x for x in nearby if (ins[x] >> 26) == 0 and (ins[x] & 63) == 0x10]
    mult_rows.append({
        "VA":f"0x{va:08X}",
        "命令":decode(va,w),
        "近傍MFLO": ";".join(f"0x{x:08X}" for x in mflo),
        "近傍MFHI": ";".join(f"0x{x:08X}" for x in mfhi)
    })
    mult_report.append("\n".join(body) + "\n" + "="*100)

write_csv("全MULT_DIV命令.csv", mult_rows,
          ["VA","命令","近傍MFLO","近傍MFHI"])
write_text("全MULT_DIV命令_前後命令.txt", "\n".join(mult_report))

# 2. MFLO/MFHI全件と前後命令
hi_lo_rows = []
hi_lo_report = []
for va in addrs:
    w = ins[va]
    if (w >> 26) == 0 and (w & 63) in (0x10,0x11,0x12,0x13):
        i = index[va]
        near = addrs[max(0,i-10):min(len(addrs),i+11)]
        hi_lo_rows.append({
            "VA":f"0x{va:08X}",
            "命令":decode(va,w),
            "前後": " | ".join(line(x) for x in near)
        })
        hi_lo_report.append("\n".join(line(x) for x in near) + "\n" + "="*80)

write_csv("全HI_LO命令.csv", hi_lo_rows, ["VA","命令","前後"])
write_text("全HI_LO命令_周辺.txt", "\n".join(hi_lo_report))

# 3. 588および関連定数の構築候補
const_rows = []
for va in addrs:
    w = ins[va]
    op = w >> 26
    rt = (w >> 16) & 31
    imm = w & 0xffff
    if op in (8,9,10,11,12,13,14,15) and imm in (588,0x024c,0xc800,0xaeb0,0x0039,0x001d):
        const_rows.append({
            "VA":f"0x{va:08X}",
            "命令":decode(va,w),
            "前命令":line(va-4),
            "次命令":line(va+4)
        })
write_csv("定数構築候補.csv", const_rows,
          ["VA","命令","前命令","次命令"])

# 4. 既知アドレスへの直接参照と近傍
targets = [0x0039C800,0x001DAEB0,0x005776B0,0x005FA438,
           0x005FA44E,0x005FA000,0x003A65F4]
target_report = []
target_rows = []
for va in addrs:
    w = ins[va]
    op = w >> 26
    rt = (w >> 16) & 31
    imm = sx(w & 0xffff)
    # LUI followed by ADDIU/ORI is reconstructed
    if op == 15 and va+4 in ins:
        w2 = ins[va+4]
        op2 = w2 >> 26
        rt2 = (w2 >> 16) & 31
        rs2 = (w2 >> 21) & 31
        imm2 = w2 & 0xffff
        if rt == rs2 and rt == rt2 and op2 in (9,13):
            high = (w & 0xffff) << 16
            value = (high + sx(imm2)) & 0xffffffff if op2 == 9 else (high | imm2)
            if value in targets or any(abs(value-t) < 0x1000 for t in targets):
                target_rows.append({
                    "VA":f"0x{va:08X}",
                    "構築値":f"0x{value:08X}",
                    "命令1":decode(va,w),
                    "命令2":decode(va+4,w2)
                })
                target_report.extend([line(va-8),line(va-4),line(va),line(va+4),line(va+8),""])

write_csv("既知アドレス構築.csv", target_rows,
          ["VA","構築値","命令1","命令2"])
write_text("既知アドレス構築_周辺.txt", "\n".join(target_report))

# 5. GP-26340/26212へのアクセスと関数近傍
gp_rows = []
gp_report = []
for va in addrs:
    w = ins[va]
    op = w >> 26
    rs = (w >> 21) & 31
    imm = sx(w & 0xffff)
    if rs == 28 and imm in (-26340,-26212):
        gp_rows.append({
            "VA":f"0x{va:08X}",
            "命令":decode(va,w),
            "種類":"LOAD" if op in (32,33,35,36,37,39) else "STORE/OTHER"
        })
        i = index[va]
        near = addrs[max(0,i-12):min(len(addrs),i+13)]
        gp_report.append("\n".join(line(x) for x in near) + "\n" + "="*90)

write_csv("GP重要変数アクセス.csv", gp_rows, ["VA","命令","種類"])
write_text("GP重要変数アクセス_周辺.txt", "\n".join(gp_report))

# 6. 0x0039C800/0x005776B0周辺のデータダンプ（ELF内のVA対応）
def va_to_file(va):
    for s in segments:
        if s["va"] <= va < s["va"] + s["filesz"]:
            return s["off"] + (va - s["va"])
    return None

dump_report = []
for center in [0x0039C800,0x005776B0,0x005776B0+588,0x005776B0+1176]:
    dump_report.append(f"\nCENTER 0x{center:08X}")
    for delta in range(-64, 129, 16):
        va = center + delta
        off = va_to_file(va)
        if off is None or off+16 > len(data):
            dump_report.append(f"0x{va:08X} <ELFファイル内に対応なし>")
            continue
        b = data[off:off+16]
        dwords = [struct.unpack_from("<I",b,j)[0] for j in (0,4,8,12)]
        dump_report.append(
            f"0x{va:08X} {b.hex(' ').upper()} | " +
            " ".join(f"{x:08X}" for x in dwords)
        )
write_text("基点候補データダンプ.txt", "\n".join(dump_report))

# 7. 対象関数の詳細逆アセンブル
ranges = [
    (0x0021EC00,0x0021EF00),
    (0x002AE4E0,0x002AE600),
    (0x002AE940,0x002AEA00),
    (0x002BCC00,0x002BCCE0),
    (0x0021AD00,0x0021AE00),
]
report = []
for start,end in ranges:
    report.append(f"\nRANGE 0x{start:08X}-0x{end:08X}")
    for va in range(start,end,4):
        if va in ins:
            report.append(line(va))
write_text("重要関数詳細逆アセンブル.txt", "\n".join(report))

# 8. 588の前後にある命令を含む関数候補
stride_report = []
for va in addrs:
    w = ins[va]
    if (w >> 26) in (8,9,10,11,12,13,14) and (w & 0xffff) == 588:
        i = index[va]
        start = max(0,i-20)
        end = min(len(addrs),i+45)
        stride_report.append(
            f"\n588候補 @ 0x{va:08X}\n" +
            "\n".join(line(x) for x in addrs[start:end]) +
            "\n" + "="*100
        )
write_text("588定数使用箇所_全件.txt", "\n".join(stride_report))

# 9. ELF概要とSHA256
summary = []
summary.append("第21弾 乗算結果と武器データ領域解析")
summary.append("ELF: " + elf_path)
summary.append("Size: " + str(len(data)))
summary.append("SHA256: " + hashlib.sha256(data).hexdigest())
summary.append(f"Program headers: {phnum}")
summary.append(f"Executable segments: {len(exec_segments)}")
for s in exec_segments:
    summary.append(
        f"EXEC VA=0x{s['va']:08X} FILESIZE=0x{s['filesz']:X} "
        f"MEMSIZE=0x{s['memsz']:X} OFFSET=0x{s['off']:X}"
    )
summary.append(f"Decoded instructions: {len(ins)}")
summary.append(f"MULT/DIV count: {len(mult_rows)}")
summary.append(f"MFHI/MFLO/MTHI/MTLO count: {len(hi_lo_rows)}")
summary.append(f"GP important accesses: {len(gp_rows)}")
summary.append(f"Constant construction candidates: {len(const_rows)}")
summary.append("")
summary.append("注意: ELF上の値は実行時RAMの値と一致するとは限りません。")
summary.append("ポインタの実体・データ構造はゲーム内実測と照合して確定してください。")
write_text("解析概要.txt", "\n".join(summary))

print("\n".join(summary))
print("\n出力先:", out_dir)
