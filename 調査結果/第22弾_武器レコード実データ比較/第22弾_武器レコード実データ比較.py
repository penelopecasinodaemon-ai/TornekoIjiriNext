import os, sys, struct, hashlib, zipfile, csv
from collections import defaultdict

elf_path, base_dir, out_dir = sys.argv[1:4]
os.makedirs(out_dir, exist_ok=True)

def save_text(name, text):
    with open(os.path.join(out_dir, name), "w", encoding="utf-8-sig") as f:
        f.write(text)

def save_csv(name, rows, fields):
    with open(os.path.join(out_dir, name), "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        w.writerows(rows)

def sha(b):
    return hashlib.sha256(b).hexdigest()

elf = open(elf_path, "rb").read()
if elf[:4] != b"\x7fELF" or elf[4] != 1 or elf[5] != 1:
    raise RuntimeError("ELF32 little-endianではありません")

eh = struct.unpack_from("<16sHHIIIIIHHHHHH", elf, 0)
phoff, phentsize, phnum = eh[5], eh[9], eh[10]

segments = []
for i in range(phnum):
    p = struct.unpack_from("<IIIIIIII", elf, phoff + i * phentsize)
    typ, off, va, pa, fs, ms, flags, align = p
    if typ == 1:
        segments.append(dict(off=off, va=va, filesz=fs,
                             memsz=ms, flags=flags))

def va_to_file(va):
    for s in segments:
        if s["va"] <= va < s["va"] + s["filesz"]:
            return s["off"] + va - s["va"]
    return None

def dump_bytes(buf, start, length):
    if start < 0 or start + length > len(buf):
        return None
    return buf[start:start+length]

def describe(buf):
    if buf is None:
        return "OUT_OF_RANGE"
    vals = []
    for i in range(0, min(len(buf)-1, 64), 2):
        u = struct.unpack_from("<H", buf, i)[0]
        s = struct.unpack_from("<h", buf, i)[0]
        vals.append(f"+{i:03X}:u16={u:5d}/0x{u:04X},s16={s:6d}")
    return " | ".join(vals)

# アイテム番号
weapons = [
    (0x29, "うみどりの爪"),
    (0x2A, "銀の爪"),
    (0x2B, "ゴールドクロウ"),
    (0x2C, "エレファントクロウ"),
    (0x2D, "クロウアンドレア"),
]

stride = 588
bases = [
    (0x0039C800, "GP基点候補_0039C800"),
    (0x005776B0, "加算後候補_005776B0"),
]

report = []
rows = []

report.append("第22弾 武器レコード実データ比較")
report.append("ELF: " + elf_path)
report.append("ELF size: " + str(len(elf)))
report.append("ELF SHA256: " + sha(elf))
report.append("stride候補: 588 (0x24C)")
report.append("")

report.append("=== ELF LOAD SEGMENTS ===")
for s in segments:
    report.append(
        f"VA=0x{s['va']:08X} FILE=0x{s['off']:08X} "
        f"FILESIZE=0x{s['filesz']:X} MEMSIZE=0x{s['memsz']:X} "
        f"FLAGS=0x{s['flags']:X}"
    )

report.append("")
report.append("=== ELF内候補レコード ===")

for base, label in bases:
    report.append(f"\n--- {label}: 0x{base:08X} ---")
    for item_id, item_name in weapons:
        for mode, index in [("IDそのまま", item_id),
                            ("ID-1", item_id-1)]:
            va = base + index * stride
            off = va_to_file(va)
            b = dump_bytes(elf, off, stride) if off is not None else None
            report.append(
                f"\n{item_name} ID=0x{item_id:02X} "
                f"{mode} VA=0x{va:08X} FILE=" +
                (f"0x{off:08X}" if off is not None else "なし")
            )
            if b is None:
                report.append("ELFファイル内にデータなし")
            else:
                report.append("SHA256=" + sha(b))
                report.append("先頭128バイト=" + b[:128].hex(" ").upper())
                report.append("末尾64バイト=" + b[-64:].hex(" ").upper())
                report.append(describe(b))

            rows.append({
                "候補基点": label,
                "アイテム名": item_name,
                "ID": f"0x{item_id:02X}",
                "添字方式": mode,
                "候補VA": f"0x{va:08X}",
                "ELFファイル位置": f"0x{off:08X}" if off is not None else "",
                "データ有無": "あり" if b is not None else "なし",
                "SHA256": sha(b) if b is not None else ""
            })

save_csv("ELF候補レコード一覧.csv", rows,
         ["候補基点","アイテム名","ID","添字方式",
          "候補VA","ELFファイル位置","データ有無","SHA256"])

# P2S内eeMemoryの調査
p2s_files = []
for root, dirs, files in os.walk(base_dir):
    dirs[:] = [d for d in dirs if d != os.path.basename(out_dir)]
    for fn in files:
        if fn.lower().endswith(".p2s"):
            p2s_files.append(os.path.join(root, fn))

p2s_report = []
p2s_rows = []

p2s_report.append("P2S files found: " + str(len(p2s_files)))
p2s_report.append("eeMemoryの候補アドレスを読み取り専用で抽出")
p2s_report.append("")

for path in p2s_files:
    rel = os.path.relpath(path, base_dir)
    try:
        with zipfile.ZipFile(path, "r") as z:
            names = z.namelist()
            if "eeMemory.bin" not in names:
                continue
            mem = z.read("eeMemory.bin")

        p2s_report.append("\n==============================")
        p2s_report.append("P2S: " + rel)
        p2s_report.append("eeMemory size: " + str(len(mem)))
        p2s_report.append("eeMemory SHA256: " + sha(mem))

        for base, label in bases:
            for item_id, item_name in weapons:
                for mode, index in [("IDそのまま", item_id),
                                    ("ID-1", item_id-1)]:
                    addr = base + index * stride
                    b = dump_bytes(mem, addr, stride)
                    if b is None:
                        continue

                    p2s_report.append(
                        f"\n{label} {item_name} {mode} "
                        f"EE=0x{addr:08X}"
                    )
                    p2s_report.append("SHA256=" + sha(b))
                    p2s_report.append("先頭128=" + b[:128].hex(" ").upper())
                    p2s_report.append(describe(b))

                    p2s_rows.append({
                        "P2S": rel,
                        "基点": label,
                        "アイテム": item_name,
                        "ID": f"0x{item_id:02X}",
                        "方式": mode,
                        "EEアドレス": f"0x{addr:08X}",
                        "SHA256": sha(b),
                        "先頭32バイト": b[:32].hex(" ").upper()
                    })

    except Exception as e:
        p2s_report.append(f"\nERROR {rel}: {e}")

save_text("P2S候補レコードダンプ.txt", "\n".join(p2s_report))
save_csv("P2S候補レコード一覧.csv", p2s_rows,
         ["P2S","基点","アイテム","ID","方式",
          "EEアドレス","SHA256","先頭32バイト"])

# 同一P2S内で5武器の候補レコードがどう変わるかを比較
grouped = defaultdict(list)
for r in p2s_rows:
    key = (r["P2S"], r["基点"], r["方式"])
    grouped[key].append(r)

compare = []
for key, items in grouped.items():
    items.sort(key=lambda x: int(x["ID"], 16))
    hashes = [x["SHA256"] for x in items]
    compare.append({
        "P2S": key[0],
        "基点": key[1],
        "方式": key[2],
        "取得レコード数": len(items),
        "5種類のSHA一致": "YES" if len(set(hashes)) == 1 else "NO",
        "武器一覧": "; ".join(x["アイテム"] for x in items)
    })

save_csv("P2S武器間比較.csv", compare,
         ["P2S","基点","方式","取得レコード数",
          "5種類のSHA一致","武器一覧"])

summary = []
summary.append("第22弾解析概要")
summary.append("ELF SHA256: " + sha(elf))
summary.append("LOAD segments: " + str(len(segments)))
summary.append("P2S files: " + str(len(p2s_files)))
summary.append("P2S candidate records: " + str(len(p2s_rows)))
summary.append("stride: 588")
summary.append("")
summary.append("注意:")
summary.append("・588バイトは命令中に存在する定数だが、添字計算への適用は未確定。")
summary.append("・候補基点とアイテムIDの対応は仮説として比較している。")
summary.append("・P2SのeeMemoryアドレスがゲームの仮想アドレスと一致するかは別途検証が必要。")
summary.append("・元ELF/P2Sは変更していない。")
save_text("解析概要.txt", "\n".join(summary))

print("\n".join(summary))
print("\n出力先:", out_dir)
