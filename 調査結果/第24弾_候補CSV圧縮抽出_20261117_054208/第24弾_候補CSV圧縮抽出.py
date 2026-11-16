import sys
import csv
import os
import traceback
from collections import defaultdict
from pathlib import Path

INPUT = Path(sys.argv[1])
OUT = Path(sys.argv[2])
OUT.mkdir(parents=True, exist_ok=True)

STATES = ["midori", "elephant", "bare", "level"]

def identify_state(row):
    name = (row.get("file", "") + " " + row.get("label", "")).lower()

    if "うみどり" in name:
        return "midori"
    if "エレファント" in name:
        return "elephant"
    if "素手" in name:
        return "bare"
    if "005fa44e" in name or "実験_11" in name:
        return "level"

    return None

def number(value):
    try:
        if value is None or value == "":
            return None
        return int(value)
    except Exception:
        return None

def float_value(value):
    try:
        if value is None or value == "":
            return None
        return float(value)
    except Exception:
        return None

def write_csv(name, rows, fields):
    path = OUT / name
    with path.open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=fields, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)
    return path

def main():
    print("入力CSV:", INPUT)
    print("出力先:", OUT)

    grouped = defaultdict(dict)
    input_rows = 0
    unknown_rows = 0

    with INPUT.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)

        required = {
            "file", "label", "offset_hex", "change_count",
            "u8", "u16", "u32", "f32"
        }
        missing = required - set(reader.fieldnames or [])
        if missing:
            raise RuntimeError("CSV列が不足しています: " + ", ".join(sorted(missing)))

        for row in reader:
            input_rows += 1
            state = identify_state(row)

            if state is None:
                unknown_rows += 1
                continue

            try:
                offset = int(row["offset_hex"], 16)
            except Exception:
                continue

            grouped[offset][state] = row

            if input_rows % 50000 == 0:
                print(f"読み込み中: {input_rows:,} 行")

    print("読み込み行数:", f"{input_rows:,}")
    print("候補アドレス数:", f"{len(grouped):,}")
    print("分類不能行数:", unknown_rows)

    result = []
    important = []

    for offset, states in grouped.items():
        if not all(s in states for s in ["midori", "elephant", "bare"]):
            continue

        m = states["midori"]
        e = states["elephant"]
        b = states["bare"]
        lv = states.get("level", {})

        row = {
            "offset_hex": f"0x{offset:08X}",
            "offset_decimal": offset,
            "change_count": m.get("change_count", ""),
        }

        score = 0
        flags = []

        # 1バイト整数候補
        vals8 = {
            "midori": number(m.get("u8")),
            "elephant": number(e.get("u8")),
            "bare": number(b.get("u8")),
            "level": number(lv.get("u8")),
        }

        valid8 = all(
            vals8[s] is not None and 0 <= vals8[s] <= 255
            for s in ["midori", "elephant", "bare"]
        )

        diff8 = valid8 and len({
            vals8["midori"], vals8["elephant"], vals8["bare"]
        }) > 1

        row["u8_midori"] = vals8["midori"]
        row["u8_elephant"] = vals8["elephant"]
        row["u8_bare"] = vals8["bare"]
        row["u8_level"] = vals8["level"]
        row["u8_diff"] = diff8

        if diff8:
            score += 1
            flags.append("1byte")

        # 2バイト整数候補。偶数アドレスのみ。
        vals16 = {
            "midori": number(m.get("u16")),
            "elephant": number(e.get("u16")),
            "bare": number(b.get("u16")),
            "level": number(lv.get("u16")),
        }

        valid16 = all(
            vals16[s] is not None and 0 <= vals16[s] <= 9999
            for s in ["midori", "elephant", "bare"]
        )

        diff16 = (
            offset % 2 == 0
            and valid16
            and len({
                vals16["midori"], vals16["elephant"], vals16["bare"]
            }) > 1
        )

        row["u16_midori"] = vals16["midori"]
        row["u16_elephant"] = vals16["elephant"]
        row["u16_bare"] = vals16["bare"]
        row["u16_level"] = vals16["level"]
        row["u16_diff"] = diff16

        if diff16:
            score += 2
            flags.append("2byte")

        # 4バイト整数候補。4バイト境界のみ。
        vals32 = {
            "midori": number(m.get("u32")),
            "elephant": number(e.get("u32")),
            "bare": number(b.get("u32")),
            "level": number(lv.get("u32")),
        }

        valid32 = all(
            vals32[s] is not None and 0 <= vals32[s] <= 9999
            for s in ["midori", "elephant", "bare"]
        )

        diff32 = (
            offset % 4 == 0
            and valid32
            and len({
                vals32["midori"], vals32["elephant"], vals32["bare"]
            }) > 1
        )

        row["u32_midori"] = vals32["midori"]
        row["u32_elephant"] = vals32["elephant"]
        row["u32_bare"] = vals32["bare"]
        row["u32_level"] = vals32["level"]
        row["u32_diff"] = diff32

        if diff32:
            score += 3
            flags.append("4byte")

        row["candidate_score"] = score
        row["candidate_types"] = ",".join(flags)

        # 元データのfloat値も参考として残す
        row["f32_midori"] = m.get("f32", "")
        row["f32_elephant"] = e.get("f32", "")
        row["f32_bare"] = b.get("f32", "")

        if score > 0:
            result.append(row)

            # 既知の注目領域
            if (
                0x0039C000 <= offset < 0x003AB000
                or 0x005FA000 <= offset < 0x00609000
                or 0x0064B000 <= offset < 0x006AE000
                or 0x006F1000 <= offset < 0x006F4000
            ):
                important.append(row)

    fields = [
        "offset_hex", "offset_decimal", "change_count",
        "candidate_score", "candidate_types",
        "u8_midori", "u8_elephant", "u8_bare", "u8_level", "u8_diff",
        "u16_midori", "u16_elephant", "u16_bare", "u16_level", "u16_diff",
        "u32_midori", "u32_elephant", "u32_bare", "u32_level", "u32_diff",
        "f32_midori", "f32_elephant", "f32_bare"
    ]

    result.sort(
        key=lambda r: (
            -r["candidate_score"],
            -int(r["change_count"] or 0),
            r["offset_decimal"]
        )
    )

    important.sort(
        key=lambda r: (
            -r["candidate_score"],
            r["offset_decimal"]
        )
    )

    all_path = write_csv(
        "武器3状態_小整数候補.csv",
        result,
        fields
    )

    top_path = write_csv(
        "武器3状態_上位1000候補.csv",
        result[:1000],
        fields
    )

    important_path = write_csv(
        "既知重要領域_小整数候補.csv",
        important,
        fields
    )

    summary = []
    summary.append("第24弾 候補CSV圧縮抽出")
    summary.append("=" * 60)
    summary.append(f"入力ファイル: {INPUT}")
    summary.append(f"入力行数: {input_rows:,}")
    summary.append(f"候補アドレス数: {len(grouped):,}")
    summary.append(f"分類不能行数: {unknown_rows:,}")
    summary.append("")
    summary.append("【抽出結果】")
    summary.append(f"武器3状態の小整数候補: {len(result):,}")
    summary.append(f"既知重要領域の小整数候補: {len(important):,}")
    summary.append("")
    summary.append("【出力ファイル】")
    summary.append(str(all_path))
    summary.append(str(top_path))
    summary.append(str(important_path))
    summary.append("")
    summary.append("【判定方法】")
    summary.append("・うみどりの爪、エレファントクロウ、素手の3状態を比較")
    summary.append("・1バイト値は0～255")
    summary.append("・2バイト値は偶数アドレス、0～9999")
    summary.append("・4バイト値は4バイト境界、0～9999")
    summary.append("・3状態の値が異なる候補のみ抽出")
    summary.append("・レベル変更実験の値も参考列として記録")
    summary.append("・スコアは候補の優先順位であり、攻撃力の確定を意味しない")
    summary.append("")
    summary.append("【注意】")
    summary.append("候補アドレスはeeMemory.bin内のファイルオフセットです。")
    summary.append("ゲームの仮想アドレスとはまだ同一視できません。")
    summary.append("装備状態の変化はモデル・座標・アニメーション等にも影響します。")

    (OUT / "抽出概要.txt").write_text(
        "\n".join(summary),
        encoding="utf-8-sig"
    )

    print("\n" + "\n".join(summary))
    print("\n抽出処理が終了しました。")

if __name__ == "__main__":
    try:
        main()
    except Exception:
        traceback.print_exc()
        sys.exit(1)
