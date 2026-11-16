import os
import sys
import hashlib
import csv
import struct

memory_path = sys.argv[1]
report_path = sys.argv[2]
csv_path = sys.argv[3]

with open(memory_path, "rb") as f:
    data = f.read()

if len(data) != 33554432:
    raise ValueError("eeMemory.binのサイズが一致しません")

sha = hashlib.sha256(data).hexdigest().upper()

targets = [
    ("ELF_PH2候補_00928DA8", 0x00928DA8),
    ("検索候補_00928BF0", 0x00928BF0),
    ("動的領域候補_00931720", 0x00931720),
    ("WORD候補_0018E4F0", 0x0018E4F0),
    ("DWORD候補_005DFD74", 0x005DFD74),
    ("構造候補_00363460", 0x00363460),
]

lines = [
    "PCSX2 EE Memory 詳細候補調査",
    "=" * 70,
    f"Size: {len(data)}",
    f"SHA256: {sha}",
    "",
    "注意: アドレスとファイルオフセットが同一であるとの仮定で抽出。",
    "ゲーム内構造・攻撃力フィールドの確定結果ではない。",
    ""
]

csv_rows = []

for name, address in targets:
    start = max(0, address - 128)
    end = min(len(data), address + 128)
    block = data[start:end]

    lines.append("")
    lines.append("=" * 70)
    lines.append(f"{name}")
    lines.append(f"Target: 0x{address:08X}")
    lines.append(f"Range : 0x{start:08X} - 0x{end-1:08X}")
    lines.append("-" * 70)

    target_index = address - start

    for line_start in range(0, len(block), 16):
        absolute = start + line_start
        chunk = block[line_start:line_start+16]
        hex_text = " ".join(f"{b:02X}" for b in chunk)

        marker = ""
        if absolute <= address < absolute + len(chunk):
            marker = "  <--- TARGET"

        lines.append(f"{absolute:08X}: {hex_text}{marker}")

    lines.append("")
    lines.append("Target周辺の数値解釈:")

    for delta in range(-16, 17, 4):
        pos = address + delta
        if pos < 0 or pos + 4 > len(data):
            continue

        raw = data[pos:pos+4]
        u32 = struct.unpack("<I", raw)[0]
        s32 = struct.unpack("<i", raw)[0]
        u16 = struct.unpack("<H", raw[:2])[0]
        u8 = raw[0]

        lines.append(
            f"Offset 0x{pos:08X} ({delta:+d}) "
            f"Bytes={raw.hex(' ').upper()} "
            f"U8={u8} U16={u16} U32={u32} S32={s32}"
        )

        csv_rows.append([
            name,
            f"0x{address:08X}",
            f"0x{pos:08X}",
            delta,
            raw.hex(" ").upper(),
            u8,
            u16,
            u32,
            s32
        ])

with open(csv_path, "w", newline="", encoding="utf-8-sig") as f:
    writer = csv.writer(f)
    writer.writerow([
        "TargetName",
        "TargetAddress",
        "ReadAddress",
        "Delta",
        "Bytes",
        "U8",
        "U16",
        "U32",
        "S32"
    ])
    writer.writerows(csv_rows)

with open(report_path, "w", encoding="utf-8-sig") as f:
    f.write("\n".join(lines))

print("\n".join(lines))
print("")
print("CSV:", csv_path)
print("REPORT:", report_path)