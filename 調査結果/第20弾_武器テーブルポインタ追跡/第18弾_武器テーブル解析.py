import struct, zipfile, hashlib, csv, math
from pathlib import Path
from collections import defaultdict

BASE = Path(r"C:\Users\penelope\Desktop\トルネコいじり作業環境")
OUT = BASE / "第18弾_武器テーブルと構造体解析"
WEAPON = BASE / "うみどりの爪装備してスライムベスの目の前.p2s"
BARE = BASE / "素手でスライムベスの目の前.p2s"
ELF = BASE / "Torneko3_MainCandidate.elf"

OUT.mkdir(exist_ok=True)

def say(s):
    print(s)

def sha(b):
    return hashlib.sha256(b).hexdigest()

def read_p2s(path):
    with zipfile.ZipFile(path, "r") as z:
        bad = z.testzip()
        if bad:
            raise RuntimeError(f"ZIP破損: {path}: {bad}")
        return z.read("eeMemory.bin")

def u16(b, p):
    if p < 0 or p + 2 > len(b):
        return None
    return struct.unpack_from("<H", b, p)[0]

def s16(b, p):
    v = u16(b, p)
    if v is None:
        return None
    return v - 0x10000 if v & 0x8000 else v

def u32(b, p):
    if p < 0 or p + 4 > len(b):
        return None
    return struct.unpack_from("<I", b, p)[0]

def hx(v):
    return f"0x{v:08X}"

def signed16(v):
    return v - 0x10000 if v & 0x8000 else v

def normalize_addr(v):
    if v is None:
        return None
    # KSEG0/KSEG1を物理アドレスに正規化
    if 0x80000000 <= v <= 0xBFFFFFFF:
        return v & 0x1FFFFFFF
    return v

def valid_range(addr, size, mem):
    return addr is not None and addr >= 0 and addr + size <= len(mem)

def read_elf_sections(data):
    if data[:4] != b"\x7fELF":
        raise RuntimeError("ELF magic不一致")
    if data[4] != 1 or data[5] != 1:
        raise RuntimeError("ELF32 little-endianではありません")

    shoff = u32(data, 32)
    shentsize = u16(data, 46)
    shnum = u16(data, 48)
    shstrndx = u16(data, 50)

    if not shoff or not shentsize or not shnum:
        return [], {}

    raw = []
    for i in range(shnum):
        p = shoff + i * shentsize
        if p + 40 > len(data):
            continue
        fields = struct.unpack_from("<IIIIIIIIII", data, p)
        raw.append({
            "index": i,
            "nameoff": fields[0],
            "type": fields[1],
            "flags": fields[2],
            "addr": fields[3],
            "offset": fields[4],
            "size": fields[5],
            "link": fields[6],
            "info": fields[7],
            "align": fields[8],
            "entsize": fields[9],
        })

    if shstrndx >= len(raw):
        return raw, {}

    st = raw[shstrndx]
    strings = data[st["offset"]:st["offset"]+st["size"]]

    byname = {}
    for s in raw:
        n = s["nameoff"]
        if n >= len(strings):
            name = ""
        else:
            end = strings.find(b"\0", n)
            if end < 0:
                end = len(strings)
            name = strings[n:end].decode("utf-8", "replace")
        s["name"] = name
        byname[name] = s

    return raw, byname

def read_reginfo(data, byname):
    sec = byname.get(".reginfo")
    if not sec:
        return None, "ELFに.reginfoセクションがありません"

    p = sec["offset"]
    size = sec["size"]
    if size < 24 or p + 24 > len(data):
        return None, ".reginfoサイズが24バイト未満です"

    gprmask = u32(data, p)
    cpr = struct.unpack_from("<IIII", data, p+4)
    gp = u32(data, p+20)

    return gp, (
        f".reginfo offset={hx(p)} size={hx(size)}\n"
        f"GPR mask={hx(gprmask)}\n"
        f"CPR masks={[hx(x) for x in cpr]}\n"
        f"GP value={hx(gp)}"
    )

def read_symbols(data, sections):
    rows = []
    for sec in sections:
        if sec.get("type") not in (2, 11):
            continue
        link = sec.get("link", 0)
        if link >= len(sections):
            continue
        st = sections[link]
        strings = data[st["offset"]:st["offset"]+st["size"]]
        entsize = sec.get("entsize") or 16
        if entsize < 16:
            continue
        start = sec["offset"]
        end = min(len(data), start + sec["size"])
        for p in range(start, end-15, entsize):
            nameoff, value, size, info, other, shndx = struct.unpack_from(
                "<IIIBBH", data, p
            )
            if nameoff >= len(strings):
                name = ""
            else:
                z = strings.find(b"\0", nameoff)
                if z < 0:
                    z = len(strings)
                name = strings[nameoff:z].decode("utf-8", "replace")
            if name or value:
                rows.append((name, value, size, info, shndx))
    return rows

def get_segments(data):
    phoff = u32(data, 28)
    phentsize = u16(data, 42)
    phnum = u16(data, 44)
    out = []
    for i in range(phnum):
        p = phoff + i * phentsize
        if p + 32 > len(data):
            continue
        typ, off, va, pa, fs, ms, flags, align = struct.unpack_from(
            "<IIIIIIII", data, p
        )
        if typ == 1 and fs:
            out.append({
                "index": i,
                "offset": off,
                "va": va,
                "pa": pa,
                "filesz": fs,
                "memsz": ms,
                "flags": flags,
                "exec": bool(flags & 1),
            })
    return out

REGS = [
    "$zero","$at","$v0","$v1","$a0","$a1","$a2","$a3",
    "$t0","$t1","$t2","$t3","$t4","$t5","$t6","$t7",
    "$s0","$s1","$s2","$s3","$s4","$s5","$s6","$s7",
    "$t8","$t9","$k0","$k1","$gp","$sp","$fp","$ra"
]

def decode(word, va):
    op = word >> 26
    rs = (word >> 21) & 31
    rt = (word >> 16) & 31
    rd = (word >> 11) & 31
    sa = (word >> 6) & 31
    fn = word & 63
    imm = signed16(word & 0xFFFF)
    uimm = word & 0xFFFF

    if op == 0x0F:
        return f"LUI {REGS[rt]},0x{uimm:04X}"
    if op == 0x09:
        return f"ADDIU {REGS[rt]},{REGS[rs]},{imm}"
    if op == 0x0D:
        return f"ORI {REGS[rt]},{REGS[rs]},0x{uimm:04X}"
    if op == 0x0C:
        return f"ANDI {REGS[rt]},{REGS[rs]},0x{uimm:04X}"
    if op == 0x23:
        return f"LW {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x21:
        return f"LH {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x25:
        return f"LHU {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x20:
        return f"LB {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x24:
        return f"LBU {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x2B:
        return f"SW {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x29:
        return f"SH {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x28:
        return f"SB {REGS[rt]},{imm}({REGS[rs]})"
    if op == 0x04:
        target = (va + 4 + (imm << 2)) & 0xFFFFFFFF
        return f"BEQ {REGS[rs]},{REGS[rt]},{hx(target)}"
    if op == 0x05:
        target = (va + 4 + (imm << 2)) & 0xFFFFFFFF
        return f"BNE {REGS[rs]},{REGS[rt]},{hx(target)}"
    if op == 0x03:
        target = ((va + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
        return f"JAL {hx(target)}"
    if op == 0x02:
        target = ((va + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
        return f"J {hx(target)}"
    if op == 0:
        if fn == 0:
            return f"SLL {REGS[rd]},{REGS[rt]},{sa}"
        if fn == 0x21:
            return f"ADDU {REGS[rd]},{REGS[rs]},{REGS[rt]}"
        if fn == 0x2D:
            return f"DADDU {REGS[rd]},{REGS[rs]},{REGS[rt]}"
        if fn == 0x25:
            return f"OR {REGS[rd]},{REGS[rs]},{REGS[rt]}"
        if fn == 8:
            return f"JR {REGS[rs]}"
        if fn == 0x18:
            return f"MULT {REGS[rs]},{REGS[rt]}"
    return f"RAW {word:08X}"

def get_instruction_list(elf, segments):
    out = []
    for seg in segments:
        if not seg["exec"]:
            continue
        start = seg["offset"]
        end = min(len(elf), start + seg["filesz"])
        for p in range(start, end-3, 4):
            word = u32(elf, p)
            va = seg["va"] + (p-start)
            out.append((va, word, decode(word, va)))
    return out

def write_csv(path, header, rows):
    with path.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)

def dump_region(path, a, b, start, size, title):
    with path.open("w", encoding="utf-8") as f:
        f.write(title + "\n")
        f.write(f"開始={hx(start)} サイズ={hx(size)}\n")
        for p in range(start, min(start+size, len(a)), 16):
            aa = a[p:p+16]
            bb = b[p:p+16]
            f.write(
                f"{p:08X} A:{aa.hex(' ').upper():47} "
                f"B:{bb.hex(' ').upper()}\n"
            )

def main():
    say("第18弾解析開始")
    for p in (WEAPON, BARE, ELF):
        if not p.exists():
            raise FileNotFoundError(f"ファイルがありません: {p}")

    a = read_p2s(WEAPON)
    b = read_p2s(BARE)
    elf = ELF.read_bytes()

    if len(a) != len(b):
        raise RuntimeError("EEメモリサイズ不一致")

    say(f"EEメモリ: {len(a)} bytes")
    say(f"ELF: {len(elf)} bytes")
    say("装備SHA256: " + sha(a))
    say("素手SHA256: " + sha(b))
    say("ELF SHA256: " + sha(elf))

    sections, byname = read_elf_sections(elf)
    gp, gpinfo = read_reginfo(elf, byname)
    symbols = read_symbols(elf, sections)
    segments = get_segments(elf)
    ins = get_instruction_list(elf, segments)

    # ELFセクション・シンボル情報
    with (OUT / "ELFセクションとGP情報.txt").open("w", encoding="utf-8") as f:
        f.write(gpinfo + "\n\n")
        f.write("ELF sections:\n")
        for s in sections:
            f.write(
                f'{s.get("index")} {s.get("name","")} '
                f'VA={hx(s["addr"])} OFF={hx(s["offset"])} '
                f'SIZE={hx(s["size"])} TYPE={s["type"]}\n'
            )
        f.write("\nGP関連シンボル:\n")
        for name, value, size, info, shndx in symbols:
            if "gp" in name.lower():
                f.write(
                    f"{name} value={hx(value)} size={size} "
                    f"info={info:02X} shndx={shndx}\n"
                )

    # GPベースの実メモリ参照
    gp_rows = []
    gp_candidates = []
    if gp is not None:
        gp_norm = normalize_addr(gp)
        for off in (-26340, -26212, -28508, -22304, -22300, -22296, -22292):
            va = gp_norm + off
            if valid_range(va, 4, a):
                av = u32(a, va)
                bv = u32(b, va)
                gp_rows.append([hx(gp), hx(va), off, hx(av), hx(bv)])
                if off == -26340:
                    gp_candidates.append(("gp_minus_26340", va, av, bv))
                if off == -26212:
                    gp_candidates.append(("gp_minus_26212", va, av, bv))
            else:
                gp_rows.append([hx(gp), hx(va), off, "OUT_OF_RANGE", "OUT_OF_RANGE"])

    write_csv(
        OUT / "GPグローバルポインタ参照.csv",
        ["GP","EEアドレス","GP相対オフセット","装備DWORD","素手DWORD"],
        gp_rows
    )

    # 重要関数とその周辺の逆アセンブル
    ranges = [
        (0x002AE500, 0x002AE700, "2AE574_2AE618"),
        (0x002AE900, 0x002AEA20, "2AE948_周辺"),
        (0x002AEE00, 0x002AF020, "2AEE00_2AEF64"),
        (0x00200320, 0x002003C0, "5FA3A8初期化箇所"),
    ]

    with (OUT / "重要関数逆アセンブル.txt").open("w", encoding="utf-8") as f:
        for lo, hi, label in ranges:
            f.write("\n" + "="*90 + "\n")
            f.write(f"{label} {hx(lo)} - {hx(hi)}\n")
            for va, word, text in ins:
                if lo <= va < hi:
                    f.write(f"{hx(va)} {word:08X} {text}\n")

    # JAL呼び出し元とGP相対メモリアクセス
    target_funcs = {0x002AE574,0x002AE618,0x002AE948,0x002AEEC4,0x002AEF64}
    callers = []
    gp_access = []
    for va, word, text in ins:
        op = word >> 26
        rs = (word >> 21) & 31
        imm = signed16(word & 0xFFFF)

        if op == 3:
            target = ((va+4)&0xF0000000) | ((word&0x03FFFFFF)<<2)
            if target in target_funcs:
                callers.append([hx(target), hx(va), f"{word:08X}", text])

        if rs == 28 and op in (0x20,0x21,0x23,0x24,0x25,0x28,0x29,0x2B):
            gp_access.append([hx(va),f"{word:08X}",imm,text])

    write_csv(
        OUT / "重要関数_CALLER.csv",
        ["呼出先","呼出元VA","命令WORD","命令"],
        callers
    )
    write_csv(
        OUT / "全GP相対メモリアクセス.csv",
        ["命令VA","命令WORD","GP相対オフセット","命令"],
        gp_access
    )

    # 0x1DAEB0付近のデータ領域を直接比較
    raw_base = 0x001DAEB0
    table_rows = []
    table_detail = []
    stride = 588
    max_records = 128

    # gp-26340から取得した値を、複数の解釈でテーブル基点候補にする
    base_candidates = [("固定候補_001DAEB0", raw_base)]
    for label, slot, av, bv in gp_candidates:
        for side, ptr in (("装備", av), ("素手", bv)):
            if ptr is None:
                continue
            norm = normalize_addr(ptr)
            base_candidates.append((label+"_"+side+"_値そのもの", norm))
            base_candidates.append((label+"_"+side+"_加算", normalize_addr(raw_base+ptr)))

    seen = set()
    with (OUT / "588バイトレコード候補.txt").open("w", encoding="utf-8") as f:
        f.write("588バイトレコード候補の比較\n")
        f.write("注意: これらはコード上の計算式から作った候補であり、武器テーブルと確定したものではありません。\n")
        for label, base in base_candidates:
            if base in seen:
                continue
            seen.add(base)
            if not valid_range(base, stride, a):
                continue

            f.write("\n" + "="*80 + "\n")
            f.write(f"{label} BASE={hx(base)}\n")
            valid_count = min(max_records, (len(a)-base)//stride)
            f.write(f"比較可能レコード数={valid_count}\n")

            for idx in range(valid_count):
                p = base + idx*stride
                aa = a[p:p+stride]
                bb = b[p:p+stride]
                changed = [i for i,(x,y) in enumerate(zip(aa,bb)) if x != y]
                table_rows.append([
                    label,hx(base),idx,hx(p),len(changed),
                    aa[:16].hex(" ").upper(),bb[:16].hex(" ").upper()
                ])
                if changed:
                    offsets = ",".join(f"{x:03X}" for x in changed[:120])
                    f.write(
                        f"REC {idx:03d} ADDR={hx(p)} "
                        f"DIFF={len(changed)} OFFSETS={offsets}\n"
                    )
                    for off in changed[:120]:
                        table_detail.append([
                            label,idx,hx(p),f"0x{off:03X}",
                            f"{aa[off]:02X}",f"{bb[off]:02X}",
                            int(aa[off]),int(bb[off])
                        ])

    write_csv(
        OUT / "588バイトレコード差分サマリー.csv",
        ["候補名","基点","レコード番号","レコードアドレス","差分バイト数","装備先頭16B","素手先頭16B"],
        table_rows
    )
    write_csv(
        OUT / "588バイトレコード差分詳細.csv",
        ["候補名","レコード番号","レコードアドレス","レコード内オフセット","装備HEX","素手HEX","装備10進","素手10進"],
        table_detail
    )

    # 0x1E800付近の0x80バイト構造体配列
    low_rows = []
    low_detail = []
    low_start = 0x0001E800
    low_end = 0x0001F200
    low_stride = 0x80

    with (OUT / "0x80構造体配列解析.txt").open("w", encoding="utf-8") as f:
        f.write("0x1E800から0x1F200までを0x80バイト単位で比較\n")
        for p in range(low_start, low_end, low_stride):
            if not valid_range(p, low_stride, a):
                continue
            aa = a[p:p+low_stride]
            bb = b[p:p+low_stride]
            changed = [i for i,(x,y) in enumerate(zip(aa,bb)) if x != y]
            f.write("\n" + "-"*70 + "\n")
            f.write(f"RECORD {hx(p)} 差分={len(changed)}\n")
            for off in range(0, low_stride, 16):
                x = aa[off:off+16]
                y = bb[off:off+16]
                f.write(
                    f"+{off:02X} A:{x.hex(' ').upper():47} "
                    f"B:{y.hex(' ').upper()}\n"
                )
            for off in range(0, low_stride, 4):
                av = u32(aa, off)
                bv = u32(bb, off)
                if av != bv:
                    low_rows.append([
                        hx(p),f"0x{off:02X}",hx(p+off),
                        hx(av),hx(bv),len(changed)
                    ])
            for off in changed:
                low_detail.append([
                    hx(p),f"0x{off:02X}",hx(p+off),
                    f"{aa[off]:02X}",f"{bb[off]:02X}"
                ])

    write_csv(
        OUT / "0x80構造体_DWORD差分.csv",
        ["レコード開始","レコード内オフセット","EEアドレス","装備DWORD","素手DWORD","レコード差分数"],
        low_rows
    )
    write_csv(
        OUT / "0x80構造体_バイト差分.csv",
        ["レコード開始","レコード内オフセット","EEアドレス","装備HEX","素手HEX"],
        low_detail
    )

    # 5FA3A8、3C0、730周辺を比較
    for addr, size, name in [
        (0x005FA3A8,0x100,"5FA3A8"),
        (0x005FA3C0,0x100,"5FA3C0"),
        (0x005FA700,0x100,"5FA700"),
    ]:
        dump_region(
            OUT / f"{name}_周辺.txt",a,b,addr,size,
            f"{name} 周辺の装備・素手比較"
        )

    # 重点アドレスのDWORD・WORD解釈
    special = [
        0x005FA438,0x005FA442,0x005FA454,0x005FA554,
        0x005FA3B0,0x005FA3B4,0x005FA3C0,0x005FA730
    ]
    special_rows = []
    for p in special:
        for width in (1,2,4):
            special_rows.append([
                hx(p),width,
                u32(a,p) if width==4 else int.from_bytes(a[p:p+width],"little"),
                u32(b,p) if width==4 else int.from_bytes(b[p:p+width],"little"),
                a[p:p+width].hex(" ").upper(),
                b[p:p+width].hex(" ").upper()
            ])
    write_csv(
        OUT / "5FA重点値.csv",
        ["EEアドレス","幅","装備値","素手値","装備HEX","素手HEX"],
        special_rows
    )

    # 解析概要
    with (OUT / "解析概要.txt").open("w", encoding="utf-8") as f:
        f.write("第18弾 武器データテーブル・構造体解析\n")
        f.write("="*72 + "\n")
        f.write(f"EEサイズ={len(a)}\n")
        f.write(f"ELFサイズ={len(elf)}\n")
        f.write(f"装備EE SHA256={sha(a)}\n")
        f.write(f"素手EE SHA256={sha(b)}\n")
        f.write(f"ELF SHA256={sha(elf)}\n")
        f.write(f"ELFセクション数={len(sections)}\n")
        f.write(f"命令数={len(ins)}\n")
        f.write(f"GP={hx(gp) if gp is not None else '不明'}\n")
        f.write(f"GP参照候補={len(gp_rows)}\n")
        f.write(f"GP相対命令数={len(gp_access)}\n")
        f.write(f"重要関数CALLER数={len(callers)}\n")
        f.write(f"588Bレコード候補数={len(base_candidates)}\n")
        f.write(f"588B差分詳細数={len(table_detail)}\n")
        f.write(f"0x80構造体DWORD差分数={len(low_rows)}\n")
        f.write(f"0x80構造体バイト差分数={len(low_detail)}\n")
        f.write("\nGP情報:\n" + gpinfo + "\n")
        f.write("\nGP参照値:\n")
        for row in gp_rows:
            f.write(str(row) + "\n")
        f.write("\n注意:\n")
        f.write("588バイトレコードは逆アセンブル上の計算式から得た調査候補です。\n")
        f.write("武器データ・攻撃力フィールドと確定したものではありません。\n")
        f.write("この解析は読み取り専用で、元ファイルを変更していません。\n")

    say("解析完了")
    say("出力先: " + str(OUT))
    say("GP: " + (hx(gp) if gp is not None else "不明"))
    say("GP参照候補: " + str(len(gp_rows)))
    say("GP相対命令: " + str(len(gp_access)))
    say("588B差分詳細: " + str(len(table_detail)))
    say("0x80構造体差分: " + str(len(low_detail)))

if __name__ == "__main__":
    main()