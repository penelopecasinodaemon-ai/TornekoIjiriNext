import zipfile
import hashlib
import csv
import struct
from pathlib import Path
from collections import Counter

weapon_path = Path(r"C:\Users\penelope\Desktop\トルネコいじり作業環境\うみどりの爪装備してスライムベスの目の前.p2s")
bare_path = Path(r"C:\Users\penelope\Desktop\トルネコいじり作業環境\素手でスライムベスの目の前.p2s")
outdir = Path(r"C:\Users\penelope\Desktop\トルネコいじり作業環境\第16弾_武器装備_素手_全メモリ比較")

def sha256(data):
    return hashlib.sha256(data).hexdigest()

def read_p2s(path):
    result = {}
    with zipfile.ZipFile(path, "r") as z:
        bad = z.testzip()
        if bad:
            raise RuntimeError(f"ZIP破損: {path.name} / {bad}")
        for info in z.infolist():
            result[info.filename] = z.read(info.filename)
    return result

print("P2Sを読み込んでいます...")
weapon = read_p2s(weapon_path)
bare = read_p2s(bare_path)

if "eeMemory.bin" not in weapon or "eeMemory.bin" not in bare:
    raise RuntimeError("eeMemory.binが見つかりません")

a = weapon["eeMemory.bin"]
b = bare["eeMemory.bin"]

if len(a) != len(b):
    raise RuntimeError(f"EEメモリサイズ不一致: 装備={len(a)} 素手={len(b)}")

if len(a) != 33554432:
    raise RuntimeError(f"EEメモリサイズが想定外: {len(a)}")

print("装備EE SHA256:", sha256(a))
print("素手EE SHA256:", sha256(b))
print("EEメモリサイズ:", len(a))
print()

# ZIPエントリーごとの差分を記録
entry_rows = []
for name in sorted(set(weapon) | set(bare)):
    if name not in weapon:
        entry_rows.append([name, "装備側なし", 0, len(bare[name]), ""])
    elif name not in bare:
        entry_rows.append([name, "素手側なし", len(weapon[name]), 0, ""])
    else:
        da, db = weapon[name], bare[name]
        entry_rows.append([
            name,
            "一致" if da == db else "差分あり",
            len(da),
            len(db),
            sha256(da) if da == db else ""
        ])

with (outdir / "ZIPエントリー比較.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["エントリー名", "比較結果", "装備サイズ", "素手サイズ", "一致時SHA256"])
    w.writerows(entry_rows)

# EE全体の差分位置
diffs = [i for i, (x, y) in enumerate(zip(a, b)) if x != y]
print("EE差分バイト数:", len(diffs))

# 差分を連続した範囲にまとめる
groups = []
if diffs:
    start = prev = diffs[0]
    for pos in diffs[1:]:
        if pos == prev + 1:
            prev = pos
        else:
            groups.append((start, prev))
            start = prev = pos
    groups.append((start, prev))

with (outdir / "差分範囲一覧.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow([
        "開始アドレス", "終了アドレス", "範囲長",
        "装備側HEX", "素手側HEX"
    ])
    for s, e in groups:
        w.writerow([
            f"0x{s:08X}", f"0x{e:08X}", e-s+1,
            a[s:e+1].hex(" ").upper(),
            b[s:e+1].hex(" ").upper()
        ])

# 全差分バイトCSV
with (outdir / "全差分バイト.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["EEアドレス", "装備値HEX", "素手値HEX", "装備10進", "素手10進"])
    for pos in diffs:
        w.writerow([
            f"0x{pos:08X}",
            f"{a[pos]:02X}",
            f"{b[pos]:02X}",
            a[pos],
            b[pos]
        ])

# 変化した領域の前後を含むダンプ
with (outdir / "差分周辺ダンプ.txt").open("w", encoding="utf-8") as f:
    f.write("装備状態と素手状態の差分周辺ダンプ\n")
    f.write("装備側と素手側を同じアドレスで比較\n\n")
    for idx, (s, e) in enumerate(groups, 1):
        lo = max(0, s - 32)
        hi = min(len(a), e + 33)
        f.write("=" * 90 + "\n")
        f.write(f"GROUP {idx}  0x{s:08X} - 0x{e:08X}  ({e-s+1} bytes)\n")
        f.write(f"範囲全体の差分バイト数: {sum(1 for p in range(s,e+1) if a[p] != b[p])}\n")
        for p in range(lo, hi, 16):
            aa = a[p:min(p+16, hi)]
            bb = b[p:min(p+16, hi)]
            f.write(f"{p:08X}  A:{aa.hex(' ').upper():47}  B:{bb.hex(' ').upper()}\n")
        f.write("\n")

# 差分範囲に重なる1/2/4バイト数値候補を作成
# 全オフセットからの読み取りと、自然アラインメント読み取りの両方を出力
candidate_rows = []
changed_set = set(diffs)

for pos in diffs:
    for width in (1, 2, 4):
        start = pos
        if start + width > len(a):
            continue

        # この数値範囲に差分が含まれているか
        if not any((start + j) in changed_set for j in range(width)):
            continue

        av = int.from_bytes(a[start:start+width], "little")
        bv = int.from_bytes(b[start:start+width], "little")
        if av == bv:
            continue

        aligned = (start % width == 0)
        candidate_rows.append([
            f"0x{start:08X}",
            width,
            "YES" if aligned else "NO",
            a[start:start+width].hex(" ").upper(),
            b[start:start+width].hex(" ").upper(),
            av,
            bv,
            bv-av
        ])

# 重複を除去
unique = {}
for row in candidate_rows:
    key = (row[0], row[1])
    unique[key] = row

candidate_rows = sorted(
    unique.values(),
    key=lambda r: (int(r[0], 16), r[1])
)

with (outdir / "数値候補_1_2_4バイト.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow([
        "開始アドレス", "幅(byte)", "自然アラインメント",
        "装備HEX", "素手HEX",
        "装備LE整数", "素手LE整数", "差分"
    ])
    w.writerows(candidate_rows)

# 重点領域の差分を個別に表示
focus_ranges = [
    (0x005F9000, 0x005FB000),
    (0x005FB000, 0x00600000),
    (0x00600000, 0x00610000),
    (0x00640000, 0x00650000),
    (0x00920000, 0x00932000),
]

focus_rows = []
for lo, hi in focus_ranges:
    for pos in diffs:
        if lo <= pos < hi:
            focus_rows.append([
                f"0x{pos:08X}", f"0x{lo:08X}-0x{hi:08X}",
                f"{a[pos]:02X}", f"{b[pos]:02X}"
            ])

with (outdir / "重点領域差分.csv").open("w", newline="", encoding="utf-8-sig") as f:
    w = csv.writer(f)
    w.writerow(["アドレス", "重点範囲", "装備値", "素手値"])
    w.writerows(focus_rows)

# 差分グループの統計
length_counts = Counter(e-s+1 for s, e in groups)
entry_diffs = [r for r in entry_rows if r[1] != "一致"]

with (outdir / "解析概要.txt").open("w", encoding="utf-8") as f:
    f.write("第16弾：武器装備・素手の全メモリ比較\n")
    f.write("=" * 60 + "\n\n")
    f.write(f"装備ファイル: {weapon_path.name}\n")
    f.write(f"素手ファイル: {bare_path.name}\n\n")
    f.write(f"装備EE SHA256: {sha256(a)}\n")
    f.write(f"素手EE SHA256: {sha256(b)}\n")
    f.write(f"EEサイズ: {len(a)} bytes\n")
    f.write(f"差分バイト数: {len(diffs)}\n")
    f.write(f"連続差分グループ数: {len(groups)}\n")
    f.write(f"数値候補数: {len(candidate_rows)}\n\n")
    f.write("ZIPエントリー差分:\n")
    for r in entry_diffs:
        f.write(f"  {r[0]} : {r[1]} / 装備={r[2]} / 素手={r[3]}\n")
    f.write("\n差分グループ長の頻度:\n")
    for length, count in sorted(length_counts.items()):
        f.write(f"  {length} byte: {count} groups\n")
    f.write("\n差分グループ上位200件:\n")
    for s, e in groups[:200]:
        f.write(
            f"  0x{s:08X}-0x{e:08X} "
            f"長さ={e-s+1} "
            f"装備={a[s:e+1].hex(' ').upper()} "
            f"素手={b[s:e+1].hex(' ').upper()}\n"
        )

print()
print("解析完了")
print("差分グループ数:", len(groups))
print("数値候補数:", len(candidate_rows))
print("ZIPエントリー差分数:", len(entry_diffs))
print("出力先:", outdir)
print()
print("生成ファイル:")
for p in sorted(outdir.iterdir()):
    if p.is_file() and p.name != "compare_p2s.py":
        print(" ", p.name, p.stat().st_size, "bytes")
