import sys
import os
import zipfile
import hashlib
import struct
import csv
from datetime import datetime

FILE_A = sys.argv[1]
FILE_B = sys.argv[2]
OUT_DIR = sys.argv[3]

os.makedirs(OUT_DIR, exist_ok=True)

def log(message):
    print(message, flush=True)

def sha256(data):
    return hashlib.sha256(data).hexdigest().upper()

def extract_ee_memory(path, label):
    log("")
    log("[" + label + "] ZIPを確認中: " + os.path.basename(path))

    with zipfile.ZipFile(path, "r") as zf:
        names = zf.namelist()

        if "eeMemory.bin" not in names:
            raise RuntimeError("eeMemory.bin がZIP内にありません: " + path)

        info = zf.getinfo("eeMemory.bin")

        log("圧縮方式: " + str(info.compress_type))
        log("圧縮サイズ: " + str(info.compress_size))
        log("展開サイズ: " + str(info.file_size))

        try:
            data = zf.read(info)
        except Exception as first_error:
            log("通常のZIP展開に失敗。Zstandard直接展開を試します。")
            log(str(first_error))

            try:
                import zstandard
            except ImportError:
                raise RuntimeError(
                    "Zstandard展開に失敗し、Pythonのzstandardモジュールもありません。"
                )

            with open(path, "rb") as f:
                f.seek(info.header_offset)
                header = f.read(30)

                if len(header) != 30:
                    raise RuntimeError("ZIPローカルヘッダーを読み取れません。")

                values = struct.unpack("<IHHHHHIIIHH", header)

                signature = values[0]
                filename_length = values[9]
                extra_length = values[10]

                if signature != 0x04034B50:
                    raise RuntimeError("ZIPローカルヘッダーが不正です。")

                f.seek(info.header_offset + 30 + filename_length + extra_length)
                compressed = f.read(info.compress_size)

            data = zstandard.ZstdDecompressor().decompress(
                compressed,
                max_output_size=info.file_size
            )

    if len(data) != info.file_size:
        raise RuntimeError(
            "展開サイズが一致しません。 expected=" +
            str(info.file_size) + " actual=" + str(len(data))
        )

    log("展開成功: " + str(len(data)) + " bytes")
    log("SHA256: " + sha256(data))

    return data

def write_binary(path, data):
    with open(path, "wb") as f:
        f.write(data)

def write_text(path, text):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(text)

def main():
    log("")
    log("========================================")
    log(" P2S EE Memory 差分比較")
    log("========================================")

    # ------------------------------------------------------------
    # 1. EEメモリ抽出
    # ------------------------------------------------------------

    a = extract_ee_memory(FILE_A, "A: うみどりの爪")
    b = extract_ee_memory(FILE_B, "B: エレファントクロウ")

    if len(a) != len(b):
        raise RuntimeError(
            "EEメモリのサイズが異なります。比較を中止します。"
        )

    if len(a) != 33554432:
        log("警告: EEメモリが通常の32MiBではありません。")

    log("")
    log("[2/4] 展開済みEEメモリを保存")

    path_a = os.path.join(OUT_DIR, "A_うみどりの爪_eeMemory.bin")
    path_b = os.path.join(OUT_DIR, "B_エレファントクロウ_eeMemory.bin")

    write_binary(path_a, a)
    write_binary(path_b, b)

    # ------------------------------------------------------------
    # 2. バイト単位の比較
    # ------------------------------------------------------------

    log("")
    log("[3/4] 32MiBをバイト単位で比較中...")
    log("差分の集計には少し時間がかかります。")

    size = len(a)
    changed_total = 0

    page_size = 4096
    page_counts = {}

    regions = []

    region_start = None
    last_changed = None
    region_changed_count = 0

    # 連続差分をまとめる際、最大16バイトの空白を許容
    merge_gap = 16

    for i in range(size):
        if a[i] != b[i]:
            changed_total += 1

            page = i // page_size
            page_counts[page] = page_counts.get(page, 0) + 1

            if region_start is None:
                region_start = i
                last_changed = i
                region_changed_count = 1

            elif i - last_changed - 1 <= merge_gap:
                last_changed = i
                region_changed_count += 1

            else:
                regions.append((
                    region_start,
                    last_changed,
                    region_changed_count
                ))

                region_start = i
                last_changed = i
                region_changed_count = 1

    if region_start is not None:
        regions.append((
            region_start,
            last_changed,
            region_changed_count
        ))

    log("比較完了")
    log("EEメモリサイズ: " + str(size))
    log("差分バイト数: " + str(changed_total))
    log("差分領域数: " + str(len(regions)))
    log("差分ページ数: " + str(len(page_counts)))

    # ------------------------------------------------------------
    # 3. 差分領域CSV
    # ------------------------------------------------------------

    csv_regions = os.path.join(OUT_DIR, "差分領域.csv")

    with open(csv_regions, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)

        writer.writerow([
            "No",
            "開始オフセット_DEC",
            "開始オフセット_HEX",
            "終了オフセット_DEC",
            "終了オフセット_HEX",
            "領域サイズ",
            "実際の差分バイト数"
        ])

        for n, (start, end, count) in enumerate(regions, 1):
            writer.writerow([
                n,
                start,
                "0x%08X" % start,
                end,
                "0x%08X" % end,
                end - start + 1,
                count
            ])

    # ------------------------------------------------------------
    # 4. 差分ページCSV
    # ------------------------------------------------------------

    csv_pages = os.path.join(OUT_DIR, "差分ページ.csv")

    sorted_pages = sorted(
        page_counts.items(),
        key=lambda x: (-x[1], x[0])
    )

    with open(csv_pages, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)

        writer.writerow([
            "順位",
            "ページ番号",
            "開始オフセット_HEX",
            "終了オフセット_HEX",
            "差分バイト数"
        ])

        for rank, (page, count) in enumerate(sorted_pages, 1):
            start = page * page_size
            end = min(start + page_size - 1, size - 1)

            writer.writerow([
                rank,
                page,
                "0x%08X" % start,
                "0x%08X" % end,
                count
            ])

    # ------------------------------------------------------------
    # 5. 差分領域周辺のバイナリダンプ
    # ------------------------------------------------------------

    dump_path = os.path.join(OUT_DIR, "差分領域周辺ダンプ.txt")

    context = 128
    max_dump_regions = 3000

    with open(dump_path, "w", encoding="utf-8-sig") as f:
        f.write("P2S EE Memory 差分領域周辺ダンプ\n")
        f.write("A = うみどりの爪\n")
        f.write("B = エレファントクロウ\n")
        f.write("前後128バイトを表示\n")
        f.write("========================================\n\n")

        for n, (start, end, count) in enumerate(
            regions[:max_dump_regions], 1
        ):
            left = max(0, start - context)
            right = min(size, end + context + 1)

            f.write("\n")
            f.write("=" * 80 + "\n")
            f.write("領域番号: %d\n" % n)
            f.write("開始: 0x%08X\n" % start)
            f.write("終了: 0x%08X\n" % end)
            f.write("領域サイズ: %d\n" % (end - start + 1))
            f.write("差分バイト数: %d\n" % count)
            f.write("表示範囲: 0x%08X - 0x%08X\n" % (left, right - 1))
            f.write("-" * 80 + "\n")

            for pos in range(left, right, 16):
                chunk_end = min(pos + 16, right)

                aa = a[pos:chunk_end]
                bb = b[pos:chunk_end]

                hex_a = " ".join("%02X" % x for x in aa)
                hex_b = " ".join("%02X" % x for x in bb)

                f.write("0x%08X A: %s\n" % (pos, hex_a))
                f.write("0x%08X B: %s\n" % (pos, hex_b))

        if len(regions) > max_dump_regions:
            f.write("\n")
            f.write(
                "注意: ダンプは先頭%d領域までです。\n" %
                max_dump_regions
            )
            f.write(
                "差分領域.csvには全領域を出力しています。\n"
            )

    # ------------------------------------------------------------
    # 6. 総合レポート
    # ------------------------------------------------------------

    report_path = os.path.join(OUT_DIR, "解析結果.txt")

    report = []

    report.append("P2S EE Memory 武器差分比較結果")
    report.append("生成日時: " + datetime.now().isoformat())
    report.append("")
    report.append("A: " + os.path.basename(FILE_A))
    report.append("B: " + os.path.basename(FILE_B))
    report.append("")
    report.append("A EE Memory SHA256: " + sha256(a))
    report.append("B EE Memory SHA256: " + sha256(b))
    report.append("")
    report.append("EEメモリサイズ: %d bytes" % size)
    report.append("差分バイト数: %d" % changed_total)
    report.append("差分領域数: %d" % len(regions))
    report.append("差分ページ数: %d" % len(page_counts))
    report.append("差分割合: %.8f%%" % (changed_total * 100.0 / size))
    report.append("")
    report.append("差分領域の定義:")
    report.append("差分の間に16バイト以下の同一領域がある場合、同じ領域に統合。")
    report.append("")
    report.append("上位100差分領域:")
    report.append("")

    for n, (start, end, count) in enumerate(regions[:100], 1):
        report.append(
            "%4d  0x%08X - 0x%08X  size=%d  changed=%d" %
            (n, start, end, end - start + 1, count)
        )

    report.append("")
    report.append("差分バイト数が多い上位100ページ:")
    report.append("")

    for rank, (page, count) in enumerate(sorted_pages[:100], 1):
        start = page * page_size
        end = min(start + page_size - 1, size - 1)

        report.append(
            "%4d  page=%d  0x%08X-0x%08X  changed=%d" %
            (rank, page, start, end, count)
        )

    report.append("")
    report.append("注意:")
    report.append("この比較だけでは攻撃力フィールドとは確定できません。")
    report.append("武器交換に伴う差分と、ゲーム内の他の変化を区別する必要があります。")
    report.append("候補領域はELF側の参照・書き込み処理と照合して判断してください。")

    write_text(report_path, "\n".join(report))

    # ------------------------------------------------------------
    # 完了
    # ------------------------------------------------------------

    log("")
    log("========================================")
    log(" 比較処理完了")
    log("========================================")
    log("差分バイト数: " + str(changed_total))
    log("差分領域数: " + str(len(regions)))
    log("")
    log("出力フォルダ: " + OUT_DIR)
    log("")
    log("主な出力ファイル:")
    log("解析結果.txt")
    log("差分領域.csv")
    log("差分ページ.csv")
    log("差分領域周辺ダンプ.txt")
    log("A_うみどりの爪_eeMemory.bin")
    log("B_エレファントクロウ_eeMemory.bin")

if __name__ == "__main__":
    main()