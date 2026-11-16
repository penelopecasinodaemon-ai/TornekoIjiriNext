import os
import sys
import hashlib
import csv
import heapq

memory_path = sys.argv[1]
report_path = sys.argv[2]
csv_path = sys.argv[3]

with open(memory_path, "rb") as f:
    data = f.read()

if len(data) != 33554432:
    raise ValueError(f"EE memory size mismatch: {len(data)}")

sha = hashlib.sha256(data).hexdigest().upper()

patterns = [
    ("BYTE_28", b"\x28", 1),
    ("WORD_0028", b"\x28\x00", 2),
    ("DWORD_00000028", b"\x28\x00\x00\x00", 4),
]

results = []
summary = []

def scan_pattern(name, needle, alignment):
    total = 0
    candidates = []

    pos = 0
    while True:
        pos = data.find(needle, pos)
        if pos < 0:
            break

        if pos % alignment == 0:
            total += 1

            left = max(0, pos - 16)
            right = min(len(data), pos + 17)
            window = data[left:right]

            near_left = max(0, pos - 8)
            near_right = min(len(data), pos + 9)
            near01 = data[near_left:near_right].count(1)

            near28 = window.count(0x28)

            score = near01 * 10 + near28

            context_start = max(0, pos - 16)
            context_end = min(len(data), pos + 32)
            context = data[context_start:context_end]

            item = (
                score,
                -pos,
                name,
                pos,
                near01,
                near28,
                context_start,
                context.hex(" ").upper()
            )

            candidates.append(item)

        pos += 1

    candidates.sort(reverse=True)

    summary.append(
        f"{name}: 全一致数={total}, 出力候補数={min(300, len(candidates))}"
    )

    for item in candidates[:300]:
        results.append(item)

for name, needle, alignment in patterns:
    scan_pattern(name, needle, alignment)

results.sort(key=lambda x: (-x[0], x[3]))

with open(csv_path, "w", newline="", encoding="utf-8-sig") as f:
    writer = csv.writer(f)
    writer.writerow([
        "Score",
        "OffsetHex",
        "OffsetDecimal",
        "Pattern",
        "01Within8Bytes",
        "28Within16Bytes",
        "ContextStartHex",
        "ContextBytesHex"
    ])

    for item in results:
        score, _, name, pos, near01, near28, context_start, context = item

        writer.writerow([
            score,
            f"0x{pos:08X}",
            pos,
            name,
            near01,
            near28,
            f"0x{context_start:08X}",
            context
        ])

lines = [
    "PCSX2 EE Memory 候補解析",
    "=" * 65,
    f"ファイルサイズ: {len(data)}",
    f"SHA256: {sha}",
    "",
    "検索対象:",
    "BYTE_28 = 28",
    "WORD_0028 = 28 00",
    "DWORD_00000028 = 28 00 00 00",
    "",
    "候補順位:",
    "周囲8バイト以内の01の数を重視",
    "周囲16バイト以内の28の数も加点",
    "",
    "検索結果:",
]

lines.extend(summary)

lines.extend([
    "",
    f"CSV出力件数: {len(results)}",
    "",
    "上位100候補:"
])

for rank, item in enumerate(results[:100], 1):
    score, _, name, pos, near01, near28, context_start, context = item

    lines.append(
        f"{rank:03d} | {name} | "
        f"Offset=0x{pos:08X} | "
        f"Score={score} | "
        f"01近傍={near01} | "
        f"28近傍={near28}"
    )

    lines.append(f"     Context: {context}")

with open(report_path, "w", encoding="utf-8-sig") as f:
    f.write("\n".join(lines))

print("\n".join(lines))
print("")
print("CSV:", csv_path)
print("REPORT:", report_path)