import os
import sys
import csv
import zipfile
import hashlib
import struct
import traceback
from pathlib import Path
from collections import Counter

BASE_DIR = Path(sys.argv[1])
OUT_DIR = Path(sys.argv[2])
SOURCE_NAME = "うみどりの爪装備してスライムベスの目の前.p2s"
MEMORY_SIZE = 32 * 1024 * 1024
CHUNK_SIZE = 1024 * 1024
MAX_CANDIDATES = 50000

OUT_DIR.mkdir(parents=True, exist_ok=True)

def write_text(name, content):
    (OUT_DIR / name).write_text(content, encoding="utf-8-sig")

def sha256(data):
    return hashlib.sha256(data).hexdigest()

def locate_memory_member(zf):
    names = zf.namelist()
    exact = []
    for name in names:
        if Path(name.replace("\\", "/")).name.lower() == "eememory.bin":
            exact.append(name)
    if not exact:
        return None
    exact.sort(key=lambda x: (len(x), x.lower()))
    return exact[0]

def load_p2s(path):
    try:
        with zipfile.ZipFile(path, "r") as zf:
            member = locate_memory_member(zf)
            if member is None:
                return None, "eeMemory.bin がZIP内にありません"
            info = zf.getinfo(member)
            data = zf.read(member)
            if len(data) != MEMORY_SIZE:
                return None, f"eeMemory.bin サイズ不一致: {len(data)} bytes"
            return data, f"OK member={member} CRC={info.CRC:08X}"
    except Exception as e:
        return None, f"読み込み失敗: {type(e).__name__}: {e}"

def weapon_label(path):
    name = path.name.lower()
    labels = [
        ("うみどり", "うみどりの爪"),
        ("銀の爪", "銀の爪"),
        ("銀爪", "銀の爪"),
        ("ゴールドクロウ", "ゴールドクロウ"),
        ("エレファント", "エレファントクロウ"),
        ("クロウアンド", "クロウアンドレア"),
        ("素手", "素手"),
        ("レベル99", "レベル99"),
        ("lv99", "レベル99"),
        ("レベル1", "レベル1"),
        ("攻撃力99", "攻撃力99実験"),
    ]
    for key, value in labels:
        if key.lower() in name:
            return value
    return "名称不明"

def get_candidates():
    found = []
    for p in BASE_DIR.rglob("*.p2s"):
        try:
            if OUT_DIR in p.parents:
                continue
            if p.resolve() == (OUT_DIR / "dummy").resolve():
                continue
            found.append(p)
        except Exception:
            pass

    # 基準P2Sを必ず先頭にする
    source = BASE_DIR / SOURCE_NAME
    found = [p for p in found if p.exists()]
    found.sort(key=lambda p: (p != source, str(p).lower()))
    return found

def numpy_compare(a, b):
    try:
        import numpy as np
        aa = np.frombuffer(a, dtype=np.uint8)
        bb = np.frombuffer(b, dtype=np.uint8)
        diff = np.flatnonzero(aa != bb)
        return diff, "numpy"
    except ImportError:
        return None, "fallback"

def compare_memories(base, other, label, counts, page_stats, report):
    diff_count = 0
    changed_pages = 0
    changed_blocks = Counter()
    ranges = []
    first = None
    last = None
    method = "numpy"

    diff, method = numpy_compare(base, other)

    if diff is not None:
        diff_count = int(len(diff))
        if diff_count:
            # 変更位置ごとの変更ファイル数を集計
            counts[diff] += 1

            pages = diff // 4096
            unique_pages, page_counts = __import__("numpy").unique(
                pages, return_counts=True
            )
            changed_pages = int(len(unique_pages))
            for pg, amount in zip(unique_pages.tolist(), page_counts.tolist()):
                page_stats[pg] += 1
                changed_blocks[pg // 16] += 1

            # 連続差分範囲
            starts = [int(diff[0])]
            ends = []
            for i in range(1, len(diff)):
                if int(diff[i]) != int(diff[i-1]) + 1:
                    ends.append(int(diff[i-1]))
                    starts.append(int(diff[i]))
            ends.append(int(diff[-1]))
            for s, e in zip(starts[:20000], ends[:20000]):
                ranges.append((s, e, e-s+1))
            first = int(diff[0])
            last = int(diff[-1])
    else:
        # NumPyがない環境用。全領域をチャンク単位で比較する。
        for start in range(0, MEMORY_SIZE, CHUNK_SIZE):
            end = min(start + CHUNK_SIZE, MEMORY_SIZE)
            aa = base[start:end]
            bb = other[start:end]
            if aa == bb:
                continue
            for j, (x, y) in enumerate(zip(aa, bb)):
                if x != y:
                    pos = start + j
                    counts[pos] = min(255, counts[pos] + 1)
                    diff_count += 1
                    pg = pos // 4096
                    page_stats[pg] += 1
                    changed_blocks[pg // 16] += 1
                    if first is None:
                        first = pos
                    last = pos
                    if ranges and pos == ranges[-1][1] + 1:
                        s, _, n = ranges[-1]
                        ranges[-1] = (s, pos, n+1)
                    elif len(ranges) < 20000:
                        ranges.append((pos, pos, 1))
        changed_pages = len(set(x // 4096 for x in []))

    report.append({
        "file": str(other_path_name(label)),
        "label": label,
        "diff_bytes": diff_count,
        "changed_pages": changed_pages,
        "first": first,
        "last": last,
        "method": method,
        "ranges": ranges,
    })

def other_path_name(label):
    return label

def decode_values(data, offset):
    result = {}
    if offset + 1 <= len(data):
        result["u8"] = data[offset]
        result["s8"] = struct.unpack_from("<b", data, offset)[0]
    if offset + 2 <= len(data):
        result["u16"] = struct.unpack_from("<H", data, offset)[0]
        result["s16"] = struct.unpack_from("<h", data, offset)[0]
    if offset + 4 <= len(data):
        result["u32"] = struct.unpack_from("<I", data, offset)[0]
        result["s32"] = struct.unpack_from("<i", data, offset)[0]
        f = struct.unpack_from("<f", data, offset)[0]
        if f == f and abs(f) != float("inf"):
            result["f32"] = f
        else:
            result["f32"] = "NaN/Inf"
    return result

def main():
    print("第24弾：全EEメモリ広域差分解析")
    print("基準:", BASE_DIR / SOURCE_NAME)
    print("出力:", OUT_DIR)
    print("メモリサイズ:", MEMORY_SIZE)

    files = get_candidates()
    inventory = []
    loaded = []
    source_path = BASE_DIR / SOURCE_NAME

    print(f"\nP2S検出数: {len(files)}")

    for i, path in enumerate(files, 1):
        print(f"[読込 {i}/{len(files)}] {path.name}")
        data, status = load_p2s(path)
        inventory.append({
            "path": str(path),
            "name": path.name,
            "label": weapon_label(path),
            "status": status,
        })
        if data is not None:
            loaded.append((path, data))
            print("  ", status)
        else:
            print("  ", status)

    with (OUT_DIR / "P2Sファイル一覧.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=["path", "name", "label", "status"])
        w.writeheader()
        w.writerows(inventory)

    base_item = next((x for x in loaded if x[0].resolve() == source_path.resolve()), None)
    if base_item is None:
        raise RuntimeError("基準P2SからeeMemory.binを読み込めませんでした")

    base_path, base = base_item
    print("\n基準EEメモリ SHA256:", sha256(base))

    # NumPyがあれば高速な全メモリ比較を使用
    try:
        import numpy as np
        counts = np.zeros(MEMORY_SIZE, dtype=np.uint8)
        numpy_available = True
    except ImportError:
        counts = bytearray(MEMORY_SIZE)
        numpy_available = False

    page_count = (MEMORY_SIZE + 4095) // 4096
    page_stats = [0] * page_count
    reports = []
    value_data = []

    for i, (path, data) in enumerate(loaded, 1):
        if path.resolve() == base_path.resolve():
            continue
        print(f"\n[比較 {i}/{len(loaded)}] {path.name}")
        label = path.name
        before = len(reports)
        compare_memories(base, data, label, counts, page_stats, reports)
        reports[-1]["path"] = str(path)
        print("  差分バイト数:", reports[-1]["diff_bytes"])
        print("  変更ページ数:", reports[-1]["changed_pages"])

    # 比較概要
    with (OUT_DIR / "ファイル別差分概要.csv").open("w", newline="", encoding="utf-8-sig") as f:
        fields = ["path", "file", "diff_bytes", "changed_pages", "first", "last", "method"]
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for r in reports:
            w.writerow({k: r.get(k) for k in fields})

    # 差分範囲一覧
    with (OUT_DIR / "ファイル別連続差分範囲.csv").open("w", newline="", encoding="utf-8-sig") as f:
        fields = ["file", "start_hex", "end_hex", "length"]
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for r in reports:
            for s, e, n in r["ranges"]:
                w.writerow({
                    "file": r["file"],
                    "start_hex": f"0x{s:08X}",
                    "end_hex": f"0x{e:08X}",
                    "length": n,
                })

    # ページ単位の差分状況
    with (OUT_DIR / "4KiBページ差分集計.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(["page_index", "file_offset_start", "file_offset_end", "changed_file_count"])
        for pg, amount in enumerate(page_stats):
            if amount:
                w.writerow([pg, f"0x{pg*4096:08X}", f"0x{min((pg+1)*4096-1, MEMORY_SIZE-1):08X}", amount])

    # NumPyとbytearrayの双方に対応した候補抽出
    if numpy_available:
        import numpy as np
        idx = np.flatnonzero(counts)
        candidate_rows = []
        total_comparisons = max(1, len(reports))
        # 1～半数程度の比較だけで変化した位置を優先。
        upper = max(2, total_comparisons // 2)
        eligible = idx[(counts[idx] >= 1) & (counts[idx] <= upper)]
        # 頻度の低い位置を優先し、出力数を制限
        if len(eligible) > MAX_CANDIDATES:
            order = np.lexsort((eligible, counts[eligible]))
            eligible = eligible[order[:MAX_CANDIDATES]]
        candidate_offsets = [int(x) for x in eligible]
    else:
        upper = max(2, max(1, len(reports)) // 2)
        candidate_offsets = [
            i for i, c in enumerate(counts)
            if 1 <= c <= upper
        ][:MAX_CANDIDATES]

    with (OUT_DIR / "全メモリ差分候補.csv").open("w", newline="", encoding="utf-8-sig") as f:
        fields = [
            "file_offset_hex", "file_offset_decimal", "changed_file_count",
            "base_u8", "base_s8", "base_u16", "base_s16",
            "base_u32", "base_s32", "base_f32"
        ]
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for off in candidate_offsets:
            v = decode_values(base, off)
            w.writerow({
                "file_offset_hex": f"0x{off:08X}",
                "file_offset_decimal": off,
                "changed_file_count": int(counts[off]),
                "base_u8": v.get("u8"),
                "base_s8": v.get("s8"),
                "base_u16": v.get("u16"),
                "base_s16": v.get("s16"),
                "base_u32": v.get("u32"),
                "base_s32": v.get("s32"),
                "base_f32": v.get("f32"),
            })

    # 候補位置の全P2S値を展開。武器名と値の対応を確認するための資料。
    with (OUT_DIR / "候補位置_全P2S値.csv").open("w", newline="", encoding="utf-8-sig") as f:
        fields = ["file", "label", "offset_hex", "change_count", "u8", "s8", "u16", "s16", "u32", "s32", "f32"]
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for off in candidate_offsets:
            for path, data in loaded:
                v = decode_values(data, off)
                w.writerow({
                    "file": path.name,
                    "label": weapon_label(path),
                    "offset_hex": f"0x{off:08X}",
                    "change_count": int(counts[off]),
                    "u8": v.get("u8"),
                    "s8": v.get("s8"),
                    "u16": v.get("u16"),
                    "s16": v.get("s16"),
                    "u32": v.get("u32"),
                    "s32": v.get("s32"),
                    "f32": v.get("f32"),
                })

    # 4バイト境界の候補を別途抽出
    aligned = sorted(set((x // 4) * 4 for x in candidate_offsets))
    with (OUT_DIR / "4バイト境界候補.csv").open("w", newline="", encoding="utf-8-sig") as f:
        fields = ["offset_hex", "changed_byte_count", "base_u32", "base_s32", "base_f32"]
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for off in aligned:
            if off + 4 > MEMORY_SIZE:
                continue
            v = decode_values(base, off)
            changed = sum(int(counts[j]) for j in range(off, off+4))
            w.writerow({
                "offset_hex": f"0x{off:08X}",
                "changed_byte_count": changed,
                "base_u32": v.get("u32"),
                "base_s32": v.get("s32"),
                "base_f32": v.get("f32"),
            })

    # 各ファイルのSHA256と読み込み情報
    with (OUT_DIR / "EEメモリ検査.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f)
        w.writerow(["file", "label", "size", "sha256", "is_baseline"])
        for path, data in loaded:
            w.writerow([str(path), weapon_label(path), len(data), sha256(data), path == base_path])

    summary = []
    summary.append("第24弾 全EEメモリ広域差分解析")
    summary.append("=" * 60)
    summary.append(f"基準P2S: {base_path}")
    summary.append(f"EEメモリサイズ: {len(base):,} bytes")
    summary.append(f"基準SHA256: {sha256(base)}")
    summary.append(f"検出P2S数: {len(files)}")
    summary.append(f"読み込み成功数: {len(loaded)}")
    summary.append(f"比較数: {len(reports)}")
    summary.append(f"NumPy高速処理: {numpy_available}")
    summary.append(f"抽出候補位置数: {len(candidate_offsets)}")
    summary.append("")
    summary.append("【ファイル別差分】")
    for r in sorted(reports, key=lambda x: x["diff_bytes"], reverse=True):
        summary.append(
            f'{r["file"]}: {r["diff_bytes"]:,} bytes / '
            f'{r["changed_pages"]} pages / {r["method"]}'
        )
    summary.append("")
    summary.append("【重要な注意】")
    summary.append("・CSVのoffsetはeeMemory.bin内のファイルオフセットです。")
    summary.append("・これをそのままゲームの仮想アドレスと断定してはいけません。")
    summary.append("・セーブ間の差分には座標、乱数、アニメーション、敵配置等も含まれます。")
    summary.append("・候補位置は攻撃力と確定したものではありません。")
    summary.append("・武器名の分類はファイル名からの推定です。")
    summary.append("・候補位置の値を複数武器間で比較し、再現性を確認してください。")
    write_text("解析概要.txt", "\n".join(summary))

    print("\n" + "\n".join(summary))
    print("\n解析完了。元P2Sは変更していません。")

if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
