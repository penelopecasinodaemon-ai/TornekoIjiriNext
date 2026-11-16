import struct
import zipfile
import hashlib
import csv
from pathlib import Path
from collections import Counter

BASE = Path(r"C:\Users\penelope\Desktop\トルネコいじり作業環境")
OUT = BASE / "第19弾_GPとCPU状態解析"

P2S_FILES = [
    BASE / "うみどりの爪装備してスライムベスの目の前.p2s",
    BASE / "素手でスライムベスの目の前.p2s"
]

ELF = BASE / "Torneko3_MainCandidate.elf"
OUT.mkdir(exist_ok=True)

def hx(v):
    return f"0x{v & 0xFFFFFFFF:08X}"

def sha(b):
    return hashlib.sha256(b).hexdigest()

def u16(b, p):
    if p < 0 or p + 2 > len(b):
        return None
    return struct.unpack_from("<H", b, p)[0]

def u32(b, p):
    if p < 0 or p + 4 > len(b):
        return None
    return struct.unpack_from("<I", b, p)[0]

def write_csv(path, header, rows):
    with path.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(header)
        w.writerows(rows)

def read_p2s(path):
    result = {}
    with zipfile.ZipFile(path, "r") as z:
        bad = z.testzip()
        if bad:
            raise RuntimeError(f"ZIP破損: {bad}")
        for info in z.infolist():
            result[info.filename] = z.read(info.filename)
    return result

def parse_elf_header(data):
    if data[:4] != b"\x7fELF":
        raise RuntimeError("ELF magic不一致")
    if data[4] != 1:
        raise RuntimeError("ELF32ではありません")
    if data[5] != 1:
        raise RuntimeError("little-endianではありません")

    values = struct.unpack_from("<HHIIIIIHHHHHH", data, 16)
    keys = [
        "type", "machine", "version", "entry", "phoff",
        "shoff", "flags", "ehsize", "phentsize", "phnum",
        "shentsize", "shnum", "shstrndx"
    ]
    return dict(zip(keys, values))

def parse_segments(data, h):
    out = []
    for i in range(h["phnum"]):
        p = h["phoff"] + i * h["phentsize"]
        if p + 32 > len(data):
            continue
        typ, off, va, pa, fs, ms, flags, align = struct.unpack_from(
            "<IIIIIIII", data, p
        )
        if typ == 1:
            out.append({
                "index": i, "offset": off, "va": va, "pa": pa,
                "filesz": fs, "memsz": ms, "flags": flags,
                "exec": bool(flags & 1)
            })
    return out

def parse_sections(data, h):
    rows = []
    shoff = h["shoff"]
    entsize = h["shentsize"]
    count = h["shnum"]
    stridx = h["shstrndx"]

    if not shoff or not entsize or not count:
        return rows, "ELFに有効なセクションテーブルがありません"

    if entsize < 40:
        return rows, f"section header sizeが異常: {entsize}"

    for i in range(count):
        p = shoff + i * entsize
        if p + 40 > len(data):
            continue
        vals = struct.unpack_from("<IIIIIIIIII", data, p)
        rows.append({
            "index": i, "nameoff": vals[0], "type": vals[1],
            "flags": vals[2], "addr": vals[3], "offset": vals[4],
            "size": vals[5], "link": vals[6], "info": vals[7],
            "align": vals[8], "entsize": vals[9]
        })

    if stridx >= len(rows):
        return rows, "shstrndxが範囲外"

    st = rows[stridx]
    if st["offset"] + st["size"] > len(data):
        return rows, "セクション名テーブルが範囲外"

    strings = data[st["offset"]:st["offset"]+st["size"]]

    for s in rows:
        n = s["nameoff"]
        if n < len(strings):
            e = strings.find(b"\0", n)
            if e < 0:
                e = len(strings)
            s["name"] = strings[n:e].decode("utf-8", "replace")
        else:
            s["name"] = ""

    return rows, "OK"

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
    imm = word & 0xFFFF
    simm = imm - 0x10000 if imm & 0x8000 else imm

    if op == 0x0F:
        return f"LUI {REGS[rt]},0x{imm:04X}"
    if op == 0x09:
        return f"ADDIU {REGS[rt]},{REGS[rs]},{simm}"
    if op == 0x0D:
        return f"ORI {REGS[rt]},{REGS[rs]},0x{imm:04X}"
    if op == 0x08:
        return f"ADDI {REGS[rt]},{REGS[rs]},{simm}"
    if op == 0x23:
        return f"LW {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0x2B:
        return f"SW {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0x21:
        return f"LH {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0x25:
        return f"LHU {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0x20:
        return f"LB {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0x24:
        return f"LBU {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0x29:
        return f"SH {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0x28:
        return f"SB {REGS[rt]},{simm}({REGS[rs]})"
    if op == 0:
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
        if fn == 0x19:
            return f"MULTU {REGS[rs]},{REGS[rt]}"
        if fn == 0x21 and rd == 28:
            return f"ADDU $gp,{REGS[rs]},{REGS[rt]}"
    if op == 3:
        target = ((va + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
        return f"JAL {hx(target)}"
    if op == 2:
        target = ((va + 4) & 0xF0000000) | ((word & 0x03FFFFFF) << 2)
        return f"J {hx(target)}"
    return f"RAW {word:08X}"

def instructions(data, segments):
    result = []
    for seg in segments:
        if not seg["exec"]:
            continue
        start = seg["offset"]
        end = min(len(data), start + seg["filesz"])
        for p in range(start, end - 3, 4):
            word = u32(data, p)
            va = seg["va"] + p - start
            result.append((va, word, decode(word, va)))
    return result

def is_gp_writer(word):
    op = word >> 26
    rs = (word >> 21) & 31
    rt = (word >> 16) & 31
    rd = (word >> 11) & 31
    fn = word & 63

    if op in (0x0F,0x09,0x08,0x0D) and rt == 28:
        return True
    if op in (0x20,0x21,0x23,0x24,0x25) and rt == 28:
        return True
    if op == 0 and rd == 28 and fn in (0x21,0x2D,0x25):
        return True
    return False

def context_dump(path, ins, target_vas, before=12, after=12):
    idxmap = {x[0]:i for i,x in enumerate(ins)}
    with path.open("w", encoding="utf-8") as f:
        for target in target_vas:
            if target not in idxmap:
                continue
            i = idxmap[target]
            f.write("\n" + "="*90 + "\n")
            f.write(f"TARGET {hx(target)}\n")
            for j in range(max(0,i-before), min(len(ins),i+after+1)):
                va, word, text = ins[j]
                mark = ">>" if j == i else "  "
                f.write(f"{mark} {hx(va)} {word:08X} {text}\n")

def main():
    print("第19弾解析開始")

    if not ELF.exists():
        raise FileNotFoundError(f"ELFがありません: {ELF}")

    for p in P2S_FILES:
        if not p.exists():
            raise FileNotFoundError(f"P2Sがありません: {p}")

    elf = ELF.read_bytes()
    h = parse_elf_header(elf)
    segs = parse_segments(elf, h)
    sections, secmsg = parse_sections(elf, h)
    ins = instructions(elf, segs)

    with (OUT / "ELFヘッダー詳細.txt").open("w", encoding="utf-8") as f:
        f.write(f"ELF SHA256={sha(elf)}\n")
        f.write(f"ELF size={len(elf)}\n")
        for k,v in h.items():
            f.write(f"{k}={hx(v) if k in ('entry','phoff','shoff') else v}\n")
        f.write(f"\nSection status={secmsg}\n")
        f.write("\nSegments:\n")
        for s in segs:
            f.write(
                f'#{s["index"]} VA={hx(s["va"])} PA={hx(s["pa"])} '
                f'OFF={hx(s["offset"])} FILE={hx(s["filesz"])} '
                f'MEM={hx(s["memsz"])} FLAGS={hx(s["flags"])} '
                f'EXEC={s["exec"]}\n'
            )
        f.write("\nSections:\n")
        for s in sections:
            f.write(
                f'#{s["index"]} {s.get("name","")} '
                f'TYPE={s["type"]} VA={hx(s["addr"])} '
                f'OFF={hx(s["offset"])} SIZE={hx(s["size"])}\n'
            )

    # $gpを書き換える命令
    gp_writers = []
    gp_related = []
    global_stores = []

    for va, word, text in ins:
        if is_gp_writer(word):
            gp_writers.append([hx(va),f"{word:08X}",text])

        op = word >> 26
        rs = (word >> 21) & 31
        rt = (word >> 16) & 31
        imm = word & 0xFFFF
        simm = imm - 0x10000 if imm & 0x8000 else imm

        if rs == 28 and op in (0x20,0x21,0x23,0x24,0x25,0x28,0x29,0x2B):
            gp_related.append([hx(va),f"{word:08X}",simm,text])

        if rs == 28 and op == 0x2B and simm in (-26340,-26212):
            global_stores.append(va)

    write_csv(
        OUT / "GPレジスタ書き込み命令.csv",
        ["命令VA","WORD","命令"],
        gp_writers
    )
    write_csv(
        OUT / "GP相対アクセス全件.csv",
        ["命令VA","WORD","GPオフセット","命令"],
        gp_related
    )

    targets = set(global_stores)
    for va, word, text in ins:
        op = word >> 26
        rs = (word >> 21) & 31
        imm = word & 0xFFFF
        simm = imm - 0x10000 if imm & 0x8000 else imm
        if rs == 28 and op in (0x23,0x2B) and simm in (-26340,-26212):
            targets.add(va)

    # 既知の重要命令も追加
    targets.update([
        0x002003A4,0x0021EE54,0x002BCC3C,
        0x002AE534,0x002AE96C,0x002AE970,0x002AE980,
        0x002AE984
    ])

    context_dump(OUT / "GP命令前後の逆アセンブル.txt",ins,sorted(targets),18,18)

    # GP書き込み命令前後
    context_dump(
        OUT / "GPレジスタ設定候補の前後.txt",
        ins,[int(x[0],16) for x in gp_writers],8,8
    )

    # P2S内部構造体を解析
    summary = []
    state_rows = []
    state_hex = []

    for p2s in P2S_FILES:
        entries = read_p2s(p2s)
        tag = "装備" if "うみどり" in p2s.name else "素手"

        summary.append([
            tag,p2s.name,p2s.stat().st_size,
            sha(p2s.read_bytes()),len(entries)
        ])

        for name,data in entries.items():
            summary.append([
                tag,name,len(data),sha(data),"ENTRY"
            ])

            if name == "PCSX2 Internal Structures.dat":
                outhex = OUT / f"{tag}_InternalStructures先頭.hex.txt"
                with outhex.open("w",encoding="utf-8") as f:
                    f.write(f"SIZE={len(data)} SHA256={sha(data)}\n")
                    for off in range(0,min(len(data),8192),16):
                        chunk=data[off:off+16]
                        f.write(
                            f"{off:08X} "
                            f"{chunk.hex(' ').upper():47} "
                            f"{''.join(chr(x) if 32<=x<127 else '.' for x in chunk)}\n"
                        )

                needles = {
                    "005FA3A8": bytes.fromhex("A8 A3 5F 00"),
                    "005FA730": bytes.fromhex("30 A7 5F 00"),
                    "005FA3C0": bytes.fromhex("C0 A3 5F 00"),
                }
                for label,needle in needles.items():
                    start=0
                    while True:
                        pos=data.find(needle,start)
                        if pos<0:
                            break
                        state_rows.append([tag,name,label,hx(pos),needle.hex(" ").upper()])
                        start=pos+1

                # CPU状態内にありそうなEE RAMアドレス値を抽出
                freq=Counter()
                samples=[]
                for off in range(0,len(data)-3,4):
                    v=u32(data,off)
                    if v is None:
                        continue
                    if 0x00200000 <= v <= 0x01000000:
                        freq[v]+=1
                        if len(samples)<2000:
                            samples.append([tag,hx(off),hx(v)])
                for v,count in freq.most_common(100):
                    state_rows.append([
                        tag,name,"DWORD頻出候補",hx(v),count
                    ])

                write_csv(
                    OUT / f"{tag}_InternalStructures_RAM値候補.csv",
                    ["種類","オフセット","値"],
                    samples
                )

            # ほかの内部エントリーも先頭情報を保存
            if name != "PCSX2 Internal Structures.dat":
                state_hex.append([
                    tag,name,len(data),data[:32].hex(" ").upper()
                ])

    write_csv(
        OUT / "P2Sエントリー一覧.csv",
        ["区分","エントリー名","サイズ","SHA256または種別","補足"],
        summary
    )
    write_csv(
        OUT / "InternalStructures検索結果.csv",
        ["区分","エントリー","検索種別","オフセットまたは値","追加情報"],
        state_rows
    )
    write_csv(
        OUT / "P2Sその他エントリー先頭.csv",
        ["区分","エントリー","サイズ","先頭32バイト"],
        state_hex
    )

    # 0x1DAEB0固定領域の実データを再確認
    with zipfile.ZipFile(P2S_FILES[0],"r") as z:
        mem=z.read("eeMemory.bin")
    with (OUT / "1DAEB0固定領域ダンプ.txt").open("w",encoding="utf-8") as f:
        for start,size in [(0x001DAEB0,0x1000),(0x001D0000,0x1000)]:
            f.write(f"\nSTART={hx(start)} SIZE={hx(size)}\n")
            for off in range(start,start+size,16):
                chunk=mem[off:off+16]
                f.write(f"{hx(off)} {chunk.hex(' ').upper()}\n")

    with (OUT / "解析概要.txt").open("w",encoding="utf-8") as f:
        f.write("第19弾 GPレジスタ・P2S CPU状態解析\n")
        f.write("="*72+"\n")
        f.write(f"ELF SHA256={sha(elf)}\n")
        f.write(f"命令数={len(ins)}\n")
        f.write(f"GP書き込み候補={len(gp_writers)}\n")
        f.write(f"GP相対アクセス={len(gp_related)}\n")
        f.write(f"GP-26340/GP-26212へのSW={len(global_stores)}\n")
        f.write(f"セクション解析={secmsg}\n")
        f.write(f"ELF shoff={hx(h['shoff'])} shentsize={h['shentsize']} shnum={h['shnum']} shstrndx={h['shstrndx']}\n")
        f.write("\nGP書き込み命令:\n")
        for x in gp_writers:
            f.write(str(x)+"\n")
        f.write("\nGP-26340/GP-26212ストア:\n")
        for x in global_stores:
            f.write(hx(x)+"\n")
        f.write("\n注意:\n")
        f.write("CPU状態のバイナリ形式はまだ確定していません。\n")
        f.write("Internal StructuresのDWORD候補はレジスタ値と断定できません。\n")
        f.write("全ファイルは読み取り専用で処理しています。\n")

    print("解析完了")
    print("命令数:",len(ins))
    print("GP書き込み候補:",len(gp_writers))
    print("GP相対アクセス:",len(gp_related))
    print("GPグローバル変数ストア:",len(global_stores))
    print("出力先:",OUT)

if __name__=="__main__":
    main()