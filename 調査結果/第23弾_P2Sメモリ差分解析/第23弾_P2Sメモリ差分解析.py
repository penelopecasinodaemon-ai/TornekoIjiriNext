import os, sys, zipfile, hashlib, csv, struct
from itertools import combinations

base_dir, out_dir = sys.argv[1:3]
os.makedirs(out_dir, exist_ok=True)

def save_csv(name, rows, fields):
    with open(os.path.join(out_dir, name), "w",
              newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)

def save_text(name, text):
    with open(os.path.join(out_dir, name), "w",
              encoding="utf-8-sig") as f:
        f.write(text)

files = []
for root, dirs, names in os.walk(base_dir):
    dirs[:] = [d for d in dirs
               if d not in (os.path.basename(out_dir),)]
    for name in names:
        if name.lower().endswith(".p2s"):
            files.append(os.path.join(root, name))

snapshots = []
errors = []

for path in files:
    try:
        with zipfile.ZipFile(path, "r") as z:
            if "eeMemory.bin" not in z.namelist():
                continue
            mem = z.read("eeMemory.bin")
        snapshots.append({
            "name": os.path.relpath(path, base_dir),
            "data": mem,
            "size": len(mem),
            "sha": hashlib.sha256(mem).hexdigest()
        })
    except Exception as e:
        errors.append(f"{path}: {e}")

summary = [
    "第23弾 P2Sメモリ差分解析",
    f"P2S files found: {len(files)}",
    f"Valid eeMemory snapshots: {len(snapshots)}",
    ""
]

for s in snapshots:
    summary.append(
        f'{s["name"]} | size={s["size"]} | SHA256={s["sha"]}'
    )

if len(snapshots) < 2:
    save_text("解析概要.txt", "\n".join(summary + errors))
    raise RuntimeError("比較可能なP2Sが2個未満です")

# 全スナップショットの共通サイズ
common_size = min(s["size"] for s in snapshots)
summary += ["", f"Common memory size: {common_size}"]

# ページ単位の差分
page_size = 0x1000
page_rows = []

for a, b in combinations(snapshots, 2):
    da, db = a["data"], b["data"]
    limit = min(len(da), len(db))
    changed_pages = 0

    for start in range(0, limit, page_size):
        end = min(start + page_size, limit)
        x, y = da[start:end], db[start:end]
        diff = sum(p != q for p, q in zip(x, y))
        if diff:
            changed_pages += 1
            page_rows.append({
                "P2S_A": a["name"],
                "P2S_B": b["name"],
                "ページ開始": f"0x{start:08X}",
                "ページ終了": f"0x{end-1:08X}",
                "異なるバイト数": diff,
                "ページサイズ": end-start
            })

save_csv("ページ単位差分.csv", page_rows, [
    "P2S_A", "P2S_B", "ページ開始",
    "ページ終了", "異なるバイト数", "ページサイズ"
])

# 候補領域のバイト差分
candidate_start = 0x0039C000
candidate_end = 0x003A5000
candidate_rows = []

for a, b in combinations(snapshots, 2):
    da, db = a["data"], b["data"]
    end = min(candidate_end, len(da), len(db))
    start = min(candidate_start, end)

    for pos in range(start, end):
        if da[pos] != db[pos]:
            candidate_rows.append({
                "P2S_A": a["name"],
                "P2S_B": b["name"],
                "EEアドレス": f"0x{pos:08X}",
                "A": f"{da[pos]:02X}",
                "B": f"{db[pos]:02X}"
            })

save_csv("候補領域バイト差分.csv", candidate_rows, [
    "P2S_A", "P2S_B", "EEアドレス", "A", "B"
])

# 588バイト候補レコードの比較
weapons = [
    (0x29, "うみどりの爪"),
    (0x2A, "銀の爪"),
    (0x2B, "ゴールドクロウ"),
    (0x2C, "エレファントクロウ"),
    (0x2D, "クロウアンドレア")
]
record_rows = []
base = 0x0039C800
stride = 588

for a, b in combinations(snapshots, 2):
    da, db = a["data"], b["data"]
    for item_id, name in weapons:
        for mode, index in [
            ("IDそのまま", item_id),
            ("ID-1", item_id-1)
        ]:
            addr = base + index * stride
            if addr + stride > min(len(da), len(db)):
                continue

            x = da[addr:addr+stride]
            y = db[addr:addr+stride]
            diffs = [i for i, (p, q) in enumerate(zip(x, y))
                     if p != q]

            record_rows.append({
                "P2S_A": a["name"],
                "P2S_B": b["name"],
                "武器": name,
                "方式": mode,
                "候補アドレス": f"0x{addr:08X}",
                "異なるバイト数": len(diffs),
                "最初の差分": (
                    f"+0x{diffs[0]:03X}" if diffs else "なし"
                ),
                "差分位置一覧": ";".join(
                    f"+0x{i:03X}" for i in diffs[:100]
                )
            })

save_csv("588バイト候補レコード差分.csv", record_rows, [
    "P2S_A", "P2S_B", "武器", "方式",
    "候補アドレス", "異なるバイト数",
    "最初の差分", "差分位置一覧"
])

# 候補領域の連続差分をまとめたレポート
report = []
for a, b in combinations(snapshots, 2):
    da, db = a["data"], b["data"]
    end = min(candidate_end, len(da), len(db))
    start = min(candidate_start, end)

    report.append("=" * 70)
    report.append("A: " + a["name"])
    report.append("B: " + b["name"])
    report.append(f"比較範囲: 0x{start:08X} - 0x{end:08X}")

    runs = []
    run_start = None
    last = None

    for pos in range(start, end):
        if da[pos] != db[pos]:
            if run_start is None:
                run_start = pos
            last = pos
        elif run_start is not None:
            if last - run_start >= 2:
                runs.append((run_start, last))
            run_start = None
            last = None

    if run_start is not None and last - run_start >= 2:
        runs.append((run_start, last))

    report.append(f"連続差分範囲数: {len(runs)}")
    for x, y in runs[:500]:
        report.append(
            f"0x{x:08X}-0x{y:08X} "
            f"長さ={y-x+1}"
        )
        report.append(
            " A: " + da[x:min(y+1, x+64)].hex(" ").upper()
        )
        report.append(
            " B: " + db[x:min(y+1, x+64)].hex(" ").upper()
        )

save_text("候補領域差分詳細.txt", "\n".join(report))
save_text("解析概要.txt", "\n".join(summary + [
    "",
    f"ページ差分行数: {len(page_rows)}",
    f"候補領域バイト差分数: {len(candidate_rows)}",
    f"588候補レコード比較数: {len(record_rows)}",
    "",
    "注意:",
    "・P2S間でゲーム内の状況が完全一致するとは限りません。",
    "・差分は装備以外の状態変化も含みます。",
    "・差分があるだけでは攻撃力フィールドとは断定できません。",
    "・元のP2Sファイルは変更していません。",
    "",
    *errors
]))

print("\n".join(summary))
print("ページ差分:", len(page_rows))
print("候補領域差分:", len(candidate_rows))
print("588候補比較:", len(record_rows))
print("出力先:", out_dir)
