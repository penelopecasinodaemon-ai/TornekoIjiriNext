import sys
import zipfile
import hashlib
import csv
import struct
from pathlib import Path
from collections import Counter

pa, pb, pc, outdir = map(Path, sys.argv[1:5])
outdir.mkdir(parents=True, exist_ok=True)

FOCUS = 0x005FA44E
START = 0x005FA000
END   = 0x005FB000

CHECKS = [
    0x005FA438,
    0x005FA442,
    0x005FA44E,
    0x005FA454,
    0x005FA554,
]

def sha(data):
    return hashlib.sha256(data).hexdigest().upper()

def load(path):
    print("読み込み:", path.name, flush=True)
    with zipfile.ZipFile(path, "r") as z:
        names = z.namelist()
        if "eeMemory.bin" not in names:
            raise RuntimeError("eeMemory.binがありません: " + path.name)
        data = z.read("eeMemory.bin")
    if len(data) != 33554432:
        raise RuntimeError(
            f"EEメモリサイズ異常: {path.name} {len(data)}"
        )
    return data

a = load(pa)
b = load(pb)
c = load(pc)

print()
print("=== ファイル情報 ===")
for name, data in [("A",a),("B",b),("C",c)]:
    print(name, "SHA256:", sha(data))
    print(name, "0x005FA44E:", data[FOCUS:FOCUS+2].hex(" ").upper())

print()
print("=== 重要アドレスの3状態比較 ===")

with (outdir / "重要アドレス比較.csv").open(
    "w", newline="", encoding="utf-8-sig"
) as f:
    w = csv.writer(f)
    w.writerow(["Address","A_HEX","B_HEX","C_HEX","A=C","Bだけ異なる"])
    for off in CHECKS:
        av = a[off:off+4]
        bv = b[off:off+4]
        cv = c[off:off+4]
        reversible = av == cv and av != bv
        w.writerow([
            f"0x{off:08X}",
            av.hex(" ").upper(),
            bv.hex(" ").upper(),
            cv.hex(" ").upper(),
            av == cv,
            reversible
        ])
        print(
            f"0x{off:08X}  "
            f"A={av.hex(' ').upper()}  "
            f"B={bv.hex(' ').upper()}  "
            f"C={cv.hex(' ').upper()}  "
            f"可逆={reversible}"
        )

print()
print("=== EEメモリ全体を3状態比較 ===", flush=True)

ab = 0
bc = 0
ac = 0
reversible_all = []
pages = Counter()

for i in range(len(a)):
    av, bv, cv = a[i], b[i], c[i]

    if av != bv:
        ab += 1
    if bv != cv:
        bc += 1
    if av != cv:
        ac += 1

    if av == cv and av != bv:
        reversible_all.append((i,av,bv,cv))
        pages[i // 0x1000] += 1

print("A-B差分:", ab)
print("B-C差分:", bc)
print("A-C差分:", ac)
print("A=CかつBのみ異なるバイト:", len(reversible_all))

with (outdir / "全メモリ可逆候補.csv").open(
    "w", newline="", encoding="utf-8-sig"
) as f:
    w = csv.writer(f)
    w.writerow(["Offset","A","B","C","A_HEX","B_HEX","C_HEX"])
    for i,av,bv,cv in reversible_all:
        w.writerow([
            f"0x{i:08X}",av,bv,cv,
            f"{av:02X}",f"{bv:02X}",f"{cv:02X}"
        ])

print()
print("=== 重点領域 0x005FA000-0x005FB000 ===")

focus = [
    x for x in reversible_all
    if START <= x[0] < END
]

print("可逆候補バイト数:", len(focus))

with (outdir / "重点領域_可逆候補.csv").open(
    "w", newline="", encoding="utf-8-sig"
) as f:
    w = csv.writer(f)
    w.writerow(["Offset","A","B","C","A_HEX","B_HEX","C_HEX"])
    for i,av,bv,cv in focus:
        w.writerow([
            f"0x{i:08X}",av,bv,cv,
            f"{av:02X}",f"{bv:02X}",f"{cv:02X}"
        ])

runs = []
for item in focus:
    if not runs or item[0] != runs[-1][-1][0] + 1:
        runs.append([item])
    else:
        runs[-1].append(item)

with (outdir / "重点領域_可逆ブロック.txt").open(
    "w", encoding="utf-8-sig"
) as f:
    for n, run in enumerate(runs,1):
        f.write(
            f"BLOCK {n}: 0x{run[0][0]:08X}"
            f"-0x{run[-1][0]:08X} "
            f"({len(run)} bytes)\n"
        )
        f.write("A: " + " ".join(f"{x[1]:02X}" for x in run) + "\n")
        f.write("B: " + " ".join(f"{x[2]:02X}" for x in run) + "\n")
        f.write("C: " + " ".join(f"{x[3]:02X}" for x in run) + "\n\n")

with (outdir / "可逆候補ページ一覧.csv").open(
    "w", newline="", encoding="utf-8-sig"
) as f:
    w = csv.writer(f)
    w.writerow(["Page","Start","ReversibleBytes"])
    for p,n in pages.most_common():
        w.writerow([f"0x{p:05X}",f"0x{p*0x1000:08X}",n])

print()
print("=== 重点領域の全差分（可逆以外も含む） ===")

with (outdir / "重点領域_全3状態比較.csv").open(
    "w", newline="", encoding="utf-8-sig"
) as f:
    w = csv.writer(f)
    w.writerow([
        "Offset","A","B","C",
        "A_HEX","B_HEX","C_HEX",
        "A_EQ_C","B_ONLY"
    ])
    for i in range(START,END):
        av,bv,cv = a[i],b[i],c[i]
        if av != bv or bv != cv or av != cv:
            w.writerow([
                f"0x{i:08X}",av,bv,cv,
                f"{av:02X}",f"{bv:02X}",f"{cv:02X}",
                av == cv,
                av == cv and av != bv
            ])

with (outdir / "解析結果.txt").open(
    "w", encoding="utf-8-sig"
) as f:
    f.write("3状態 装備可逆差分解析\n")
    f.write("====================\n\n")
    f.write(f"A: {pa}\n")
    f.write(f"B: {pb}\n")
    f.write(f"C: {pc}\n\n")
    f.write(f"A SHA256: {sha(a)}\n")
    f.write(f"B SHA256: {sha(b)}\n")
    f.write(f"C SHA256: {sha(c)}\n\n")
    f.write(f"A-B差分: {ab}\n")
    f.write(f"B-C差分: {bc}\n")
    f.write(f"A-C差分: {ac}\n")
    f.write(f"A=CかつBのみ異なる全候補: {len(reversible_all)}\n")
    f.write(f"重点領域の可逆候補: {len(focus)}\n")
    f.write(f"重点領域の可逆ブロック: {len(runs)}\n\n")
    f.write("重要アドレス:\n")
    for off in CHECKS:
        f.write(
            f"0x{off:08X}: "
            f"A={a[off:off+4].hex(' ').upper()} "
            f"B={b[off:off+4].hex(' ').upper()} "
            f"C={c[off:off+4].hex(' ').upper()}\n"
        )
    f.write("\n可逆候補ページ上位50:\n")
    for p,n in pages.most_common(50):
        f.write(f"0x{p*0x1000:08X}: {n}\n")

print()
print("解析完了")
print("A-B差分:",ab)
print("B-C差分:",bc)
print("A-C差分:",ac)
print("全体の可逆候補:",len(reversible_all))
print("重点領域の可逆候補:",len(focus))
print("重点領域の可逆ブロック:",len(runs))
print("出力先:",outdir)