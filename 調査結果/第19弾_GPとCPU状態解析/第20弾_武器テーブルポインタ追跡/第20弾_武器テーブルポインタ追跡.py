import struct
import hashlib
import csv
from pathlib import Path
from collections import Counter

BASE = Path(r"C:\Users\penelope\Desktop\トルネコいじり作業環境")
OUT = BASE / "第20弾_武器テーブルポインタ追跡"
ELF = BASE / "Torneko3_MainCandidate.elf"

def u32(b,p):
    if p < 0 or p+4 > len(b):
        return None
    return struct.unpack_from("<I",b,p)[0]

def hx(v):
    return "0x%08X" % (v & 0xFFFFFFFF)

def decode(w,va):
    op=(w>>26)&63
    rs=(w>>21)&31
    rt=(w>>16)&31
    rd=(w>>11)&31
    sa=(w>>6)&31
    fn=w&63
    imm=w&0xffff
    si=imm-0x10000 if imm&0x8000 else imm

    r=["$zero","$at","$v0","$v1","$a0","$a1","$a2","$a3",
       "$t0","$t1","$t2","$t3","$t4","$t5","$t6","$t7",
       "$s0","$s1","$s2","$s3","$s4","$s5","$s6","$s7",
       "$t8","$t9","$k0","$k1","$gp","$sp","$fp","$ra"]

    if op==0:
        if fn==0x21: return f"ADDU {r[rd]},{r[rs]},{r[rt]}"
        if fn==0x23: return f"SUBU {r[rd]},{r[rs]},{r[rt]}"
        if fn==0x2d: return f"DADDU {r[rd]},{r[rs]},{r[rt]}"
        if fn==0x25: return f"OR {r[rd]},{r[rs]},{r[rt]}"
        if fn==0x24: return f"AND {r[rd]},{r[rs]},{r[rt]}"
        if fn==0x26: return f"XOR {r[rd]},{r[rs]},{r[rt]}"
        if fn==0x00: return f"SLL {r[rd]},{r[rt]},{sa}"
        if fn==0x02: return f"SRL {r[rd]},{r[rt]},{sa}"
        if fn==0x03: return f"SRA {r[rd]},{r[rt]},{sa}"
        if fn==0x08: return f"JR {r[rs]}"
        if fn==0x09: return f"JALR {r[rd]},{r[rs]}"
        if fn==0x10: return f"MFHI {r[rd]}"
        if fn==0x12: return f"MFLO {r[rd]}"
        if fn==0x18: return f"MULT {r[rs]},{r[rt]}"
        if fn==0x19: return f"MULTU {r[rs]},{r[rt]}"
        if fn==0x1a: return f"DIV {r[rs]},{r[rt]}"
        if fn==0x1b: return f"DIVU {r[rs]},{r[rt]}"
        return "SPECIAL_%02X" % fn

    if op==0x0f: return f"LUI {r[rt]},0x{imm:04X}"
    if op==0x0d: return f"ORI {r[rt]},{r[rs]},0x{imm:04X}"
    if op==0x0c: return f"ANDI {r[rt]},{r[rs]},0x{imm:04X}"
    if op==0x09: return f"ADDIU {r[rt]},{r[rs]},{si}"
    if op==0x08: return f"ADDI {r[rt]},{r[rs]},{si}"
    if op==0x0a: return f"SLTI {r[rt]},{r[rs]},{si}"
    if op==0x0b: return f"SLTIU {r[rt]},{r[rs]},{si}"
    if op==0x23: return f"LW {r[rt]},{si}({r[rs]})"
    if op==0x2b: return f"SW {r[rt]},{si}({r[rs]})"
    if op==0x21: return f"LH {r[rt]},{si}({r[rs]})"
    if op==0x25: return f"LHU {r[rt]},{si}({r[rs]})"
    if op==0x20: return f"LB {r[rt]},{si}({r[rs]})"
    if op==0x24: return f"LBU {r[rt]},{si}({r[rs]})"
    if op==0x29: return f"SH {r[rt]},{si}({r[rs]})"
    if op==0x28: return f"SB {r[rt]},{si}({r[rs]})"

    if op in (4,5,6,7,1):
        target=va+4+(si<<2)
        name={4:"BEQ",5:"BNE",6:"BLEZ",7:"BGTZ",1:"REGIMM"}[op]
        return f"{name} {r[rs]},{r[rt]}, {hx(target)}"

    if op in (2,3):
        target=((va+4)&0xf0000000)|((w&0x03ffffff)<<2)
        return ("JAL " if op==3 else "J ")+hx(target)

    return "RAW"

def load_elf():
    data=ELF.read_bytes()
    if data[:4]!=b"\x7fELF":
        raise RuntimeError("ELF magic不一致")
    h=struct.unpack_from("<HHIIIIIHHHHHH",data,16)
    entry=h[3]
    phoff=h[4]
    phentsize=h[8]
    phnum=h[9]

    segs=[]
    for i in range(phnum):
        p=phoff+i*phentsize
        typ,off,va,pa,fs,ms,flags,align=struct.unpack_from("<IIIIIIII",data,p)
        if typ==1:
            segs.append((off,va,fs,flags))

    ins=[]
    for off,va,fs,flags in segs:
        if not (flags&1):
            continue
        end=min(len(data),off+fs)
        for p in range(off,end-3,4):
            w=u32(data,p)
            address=va+(p-off)
            ins.append((address,w,decode(w,address)))

    ins.sort(key=lambda x:x[0])
    return data,entry,segs,ins

def signed16(w):
    v=w&0xffff
    return v-0x10000 if v&0x8000 else v

def get_gp_access(ins):
    rows=[]
    for va,w,txt in ins:
        op=w>>26
        rs=(w>>21)&31
        rt=(w>>16)&31
        off=signed16(w)
        if rs==28 and op in (0x20,0x21,0x23,0x24,0x25,0x28,0x29,0x2b):
            rows.append([hx(va),f"{w:08X}",off,txt])
    return rows

def write_csv(path,header,rows):
    with path.open("w",newline="",encoding="utf-8-sig") as f:
        w=csv.writer(f)
        w.writerow(header)
        w.writerows(rows)

def write_window(path,ins,centers,before=24,after=28):
    index={x[0]:i for i,x in enumerate(ins)}
    with path.open("w",encoding="utf-8") as f:
        for center in centers:
            if center not in index:
                f.write(f"\n対象命令が見つかりません: {hx(center)}\n")
                continue
            i=index[center]
            f.write("\n"+"="*100+"\n")
            f.write("CENTER "+hx(center)+"\n")
            for j in range(max(0,i-before),min(len(ins),i+after+1)):
                va,w,txt=ins[j]
                mark=">>" if va==center else "  "
                f.write(f"{mark} {hx(va)} {w:08X} {txt}\n")

def main():
    data,entry,segs,ins=load_elf()

    with (OUT/"ELFと実行領域.txt").open("w",encoding="utf-8") as f:
        f.write(f"SHA256={hashlib.sha256(data).hexdigest()}\n")
        f.write(f"SIZE={len(data)}\nENTRY={hx(entry)}\n")
        for off,va,fs,flags in segs:
            f.write(f"SEG OFF={hx(off)} VA={hx(va)} FILE={hx(fs)} FLAGS={hx(flags)} EXEC={bool(flags&1)}\n")
        f.write(f"命令数={len(ins)}\n")

    accesses=get_gp_access(ins)
    write_csv(
        OUT/"GP相対アクセス.csv",
        ["命令VA","WORD","オフセット","命令"],
        accesses
    )

    selected=[x for x in accesses if x[2] in (-26340,-26212)]
    write_csv(
        OUT/"GPマイナス26340と26212.csv",
        ["命令VA","WORD","オフセット","命令"],
        selected
    )

    # ポインタ格納命令・レコード参照命令・stride計算命令
    centers=[
        0x00200270,
        0x002003A4,
        0x0021EC54,
        0x0021EE54,
        0x00222ECC,
        0x002AE534,
        0x002AE96C,
        0x002AE970,
        0x002AE974,
        0x002AE980,
        0x002AE984,
        0x002BCC3C
    ]

    write_window(
        OUT/"重要命令周辺_前後28命令.txt",
        ins,centers,28,32
    )

    # 588バイト計算の直前にある$a0の定義候補を広く出す
    idx={x[0]:i for i,x in enumerate(ins)}
    with (OUT/"MULT直前の$a0データフロー.txt").open("w",encoding="utf-8") as f:
        for target in [0x002AE970]:
            if target not in idx:
                f.write("対象命令なし\n")
                continue
            i=idx[target]
            f.write(f"対象={hx(target)}\n")
            f.write("MULTより前の80命令:\n")
            for j in range(max(0,i-80),i):
                va,w,txt=ins[j]
                f.write(f"{hx(va)} {w:08X} {txt}\n")
            f.write("\nMULT以降の24命令:\n")
            for j in range(i,min(len(ins),i+24)):
                va,w,txt=ins[j]
                f.write(f"{hx(va)} {w:08X} {txt}\n")

    # 対象関数を呼び出すJAL
    targets={0x002AE574,0x002AE618,0x002AE948,0x002AEEC4,0x002AEF64}
    callers=[]
    for va,w,txt in ins:
        if (w>>26)==3:
            dest=((va+4)&0xf0000000)|((w&0x03ffffff)<<2)
            if dest in targets:
                callers.append([hx(va),hx(dest),f"{w:08X}"])

    write_csv(
        OUT/"重要関数_CALLER.csv",
        ["CALL命令VA","呼び出し先","WORD"],
        callers
    )

    # 588という即値がどこで構築されているかの候補を抽出
    const_rows=[]
    for i,(va,w,txt) in enumerate(ins):
        op=w>>26
        rt=(w>>16)&31
        imm=w&0xffff
        si=signed16(w)
        if op==0x0f and imm in (0,1,2,3,4,5,6,7):
            if i+1<len(ins):
                va2,w2,t2=ins[i+1]
                if ((w2>>26)==9 and ((w2>>16)&31)==rt) or ((w2>>26)==0x0d and ((w2>>16)&31)==rt):
                    const_rows.append([hx(va),txt,hx(va2),t2])
        if op in (9,0x0d) and (imm==588 or si==588):
            const_rows.append([hx(va),txt,"",""])

    write_csv(
        OUT/"定数構築候補.csv",
        ["命令1VA","命令1","命令2VA","命令2"],
        const_rows
    )

    # 全GP-26340アクセスを種類別に集計
    counts={}
    for row in selected:
        off=row[2]
        opword=int(row[1],16)
        op=opword>>26
        kind="STORE" if op in (0x28,0x29,0x2b) else "LOAD"
        counts[(off,kind)]=counts.get((off,kind),0)+1

    with (OUT/"解析概要.txt").open("w",encoding="utf-8") as f:
        f.write("第20弾 武器テーブルポインタ追跡\n")
        f.write("="*80+"\n")
        f.write(f"ELF SHA256={hashlib.sha256(data).hexdigest()}\n")
        f.write(f"ENTRY={hx(entry)}\n")
        f.write(f"命令数={len(ins)}\n")
        f.write(f"GP相対アクセス総数={len(accesses)}\n")
        f.write(f"GP-26340/-26212アクセス数={len(selected)}\n")
        f.write("\nアクセス集計:\n")
        for k,v in sorted(counts.items()):
            f.write(f"offset={k[0]} {k[1]} count={v}\n")
        f.write("\n重要な追跡対象:\n")
        for x in centers:
            f.write(hx(x)+"\n")
        f.write("\n注意:\n")
        f.write("588バイトがMULTの乗数$a0に入るかは未確定です。\n")
        f.write("この解析は命令周辺の静的調査であり、実行時レジスタ値を断定しません。\n")
        f.write("元ELFおよびP2Sは変更していません。\n")

    print("第20弾解析完了")
    print("命令数:",len(ins))
    print("GPアクセス:",len(accesses))
    print("対象GPアクセス:",len(selected))
    print("出力先:",OUT)

if __name__=="__main__":
    main()