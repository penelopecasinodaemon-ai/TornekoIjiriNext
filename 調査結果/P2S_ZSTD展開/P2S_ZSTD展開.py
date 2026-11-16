import os
import sys
import hashlib
import zstandard

raw_dir = sys.argv[1]
out_dir = sys.argv[2]
report_path = sys.argv[3]

entries = [
    (2, 1327178, "PCSX2_Internal_Structures.bin"),
    (3, 33554432, "eeMemory.bin"),
    (4, 2097152, "iopMemory.bin"),
    (5, 65536, "eeHwRegs.bin"),
    (6, 65536, "iopHwRegs.bin"),
    (7, 16384, "Scratchpad.bin"),
    (8, 4096, "vu0Memory.bin"),
    (9, 16384, "vu1Memory.bin"),
    (10, 4096, "vu0MicroMem.bin"),
    (11, 16384, "vu1MicroMem.bin"),
    (12, 2178992, "SPU2.bin"),
    (13, 8491, "USB.bin"),
    (14, 228, "PAD.bin"),
    (15, 4194813, "GS.bin"),
]

dctx = zstandard.ZstdDecompressor()
lines = ["PCSX2 Zstandard展開結果", "=" * 60]

for index, expected_size, name in entries:
    raw_path = os.path.join(raw_dir, f"{index:03d}_raw.bin")
    out_path = os.path.join(out_dir, name)

    if not os.path.isfile(raw_path):
        lines.append(f"[SKIP] {name}: RAWファイルなし")
        continue

    try:
        with open(raw_path, "rb") as f:
            compressed = f.read()

        expanded = dctx.decompress(
            compressed,
            max_output_size=expected_size
        )

        if len(expanded) != expected_size:
            raise ValueError(
                f"サイズ不一致 expected={expected_size} actual={len(expanded)}"
            )

        with open(out_path, "wb") as f:
            f.write(expanded)

        sha = hashlib.sha256(expanded).hexdigest().upper()

        lines.append(f"[OK] {name}")
        lines.append(f"  圧縮サイズ: {len(compressed)}")
        lines.append(f"  展開サイズ: {len(expanded)}")
        lines.append(f"  SHA256: {sha}")

        if name == "eeMemory.bin":
            lines.append("  先頭64バイト:")
            lines.append(expanded[:64].hex(" ").upper())
            lines.append("  末尾64バイト:")
            lines.append(expanded[-64:].hex(" ").upper())

    except Exception as e:
        lines.append(f"[ERROR] {name}: {type(e).__name__}: {e}")

    lines.append("")

with open(report_path, "w", encoding="utf-8-sig") as f:
    f.write("\n".join(lines))

print("\n".join(lines))
print("展開先:", out_dir)
print("レポート:", report_path)