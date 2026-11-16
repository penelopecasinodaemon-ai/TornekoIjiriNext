import sys, zipfile, hashlib, csv
from pathlib import Path
from collections import Counter

pa, pb, out = map(Path, sys.argv[1:4])
out.mkdir(parents=True, exist_ok=True)

TARGET = "eeMemory.bin"
START, END = 0x005FA000, 0x005FB000
FOCUS = 0x005FA44E

def sha(d):
    return hashlib.sha256(d).hexdigest().upper()

def read(path):
    with zipfile.ZipFile(path) as z:
        bad = z.testzip()
        if bad:
            raise RuntimeError(f"CRC error: {bad}")
        return {i.filename: z.read(i.filename) for i in z.infolist()}

print("A読み込み:", pa.name)
a = read(pa)
print("B読み込み:", pb.name)
b = read(pb)

if TARGET not in a or TARGET not in b:
    raise RuntimeError("eeMemory.binがありません")

ea, eb = a[TARGET], b[TARGET]

if len(ea) != 33554432 or len(eb) != 33554432:
    raise RuntimeError("EEメモリサイズが不正です")

print("A対象値:", ea[FOCUS:FOCUS+2].hex(" ").upper())
print("B対象値:", eb[FOCUS:FOCUS+2].hex(" ").upper())

changes = []
pages = Counter()

for i, (x, y) in enumerate(zip(ea, eb)):
    if x != y:
        changes.append((i, x, y))
        pages[i // 0x1000] += 1

focus = [x for x in changes if START <= x[0] < END]

with (out / "全EEメモリ差分.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["Offset", "A", "B", "A_HEX", "B_HEX"])
    for i, x, y in changes:
        w.writerow([f"0x{i:08X}", x, y, f"{x:02X}", f"{y:02X}"])

with (out / "重点範囲差分.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["Offset", "A", "B", "A_HEX", "B_HEX"])
    for i, x, y in focus:
        w.writerow([f"0x{i:08X}", x, y, f"{x:02X}", f"{y:02X}"])

with (out / "差分ページ一覧.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["Page", "Start", "ChangedBytes"])
    for p, n in pages.most_common():
        w.writerow([f"0x{p:05X}", f"0x{p*0x1000:08X}", n])

runs = []
for item in focus:
    if not runs or item[0] != runs[-1][-1][0] + 1:
        runs.append([item])
    else:
        runs[-1].append(item)

with (out / "重点範囲差分ブロック.txt").open("w", encoding="utf-8-sig") as f:
    for n, run in enumerate(runs, 1):
        f.write(f"BLOCK {n}: 0x{run[0][0]:08X}-0x{run[-1][0]:08X} ({len(run)} bytes)\n")
        f.write("A: " + " ".join(f"{x:02X}" for _, x, _ in run) + "\n")
        f.write("B: " + " ".join(f"{y:02X}" for _, _, y in run) + "\n\n")

other = []
for name in sorted(set(a) | set(b)):
    if name not in a or name not in b:
        other.append(name + " [missing]")
    elif name != TARGET and a[name] != b[name]:
        other.append(name + " [changed]")

with (out / "重点範囲前後ダンプ.txt").open("w", encoding="utf-8-sig") as f:
    for center in [START, FOCUS, END]:
        lo, hi = max(0, center-64), min(len(ea), center+128)
        f.write(f"\n--- {lo:08X} - {hi:08X} ---\n")
        for off in range(lo, hi, 16):
            f.write(f"{off:08X} A: {ea[off:off+16].hex(' ').upper()}\n")
            f.write(f"{off:08X} B: {eb[off:off+16].hex(' ').upper()}\n")

with (out / "解析結果.txt").open("w", encoding="utf-8-sig") as f:
    f.write("装備変更前後 EEメモリ差分解析\n")
    f.write(f"A: {pa}\nB: {pb}\n")
    f.write(f"A SHA256: {sha(ea)}\nB SHA256: {sha(eb)}\n")
    f.write(f"A 0x{FOCUS:08X}: {ea[FOCUS:FOCUS+2].hex(' ').upper()}\n")
    f.write(f"B 0x{FOCUS:08X}: {eb[FOCUS:FOCUS+2].hex(' ').upper()}\n")
    f.write(f"全体差分バイト数: {len(changes)}\n")
    f.write(f"重点範囲差分バイト数: {len(focus)}\n")
    f.write(f"重点範囲差分ブロック数: {len(runs)}\n")
    f.write("他ZIPエントリーの差分:\n")
    f.write("\n".join(other) if other else "なし")
    f.write("\n\n差分ページ上位40:\n")
    for p, n in pages.most_common(40):
        f.write(f"0x{p*0x1000:08X}: {n}\n")

print()
print("解析完了")
print("全EE差分:", len(changes))
print("重点範囲差分:", len(focus))
print("重点範囲ブロック:", len(runs))
print("他エントリー差分:", len(other))
print("出力:", out)
