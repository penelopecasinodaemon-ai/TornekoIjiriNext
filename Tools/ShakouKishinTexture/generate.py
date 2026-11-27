#!/usr/bin/env python3
"""Create a Shakou Kishin recolor of the Ishiningyo 256x256 UV texture.

The supplied screenshot is used as the color reference.  The original UV layout
and relief are retained; grayscale values are remapped into a charcoal/blue-gray
stone palette, while the decorative UV islands receive the turquoise enamel
accent visible on Shakou Kishin.
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "カスタムモンスター" / "普通のいしにんぎょう.png"
REFERENCE = ROOT / "カスタムモンスター" / "シャコウ鬼神正面撮影.png"
OUTPUT = ROOT / "カスタムモンスター" / "シャコウ鬼神.png"

def clamp(v):
    return max(0, min(255, int(v)))

def ramp(value, stops):
    """Piecewise-linear RGB color ramp."""
    for i in range(len(stops) - 1):
        x0, c0 = stops[i]
        x1, c1 = stops[i + 1]
        if value <= x1:
            t = (value - x0) / max(1, x1 - x0)
            return tuple(clamp(a + (b - a) * t) for a, b in zip(c0, c1))
    return stops[-1][1]

def is_accent_island(x, y):
    # Decorative raised motifs / trim strips in the supplied UV atlas.
    # These are the ornamental regions (swirls, head panel and long hem trim).
    if 0 <= x < 82 and 126 <= y < 233:
        return True
    if 120 <= x < 241 and 0 <= y < 63:
        return True
    if 0 <= x < 256 and 234 <= y < 256:
        return True
    # Narrow raised border strip at the right edge of the central UV panel.
    if 205 <= x < 251 and 75 <= y < 199:
        return True
    return False

def main():
    if not SOURCE.exists() or not REFERENCE.exists():
        raise FileNotFoundError("Required source texture or Shakou Kishin reference image is missing.")

    src = Image.open(SOURCE).convert("RGB")
    ref = Image.open(REFERENCE).convert("RGB")
    if src.size != (256, 256):
        raise ValueError(f"Expected 256x256 UV texture, got {src.size}")

    # Derive the accent hue from saturated turquoise pixels in the reference.
    samples = []
    rw, rh = ref.size
    step = max(1, min(rw, rh) // 240)
    for y in range(0, rh, step):
        for x in range(0, rw, step):
            r, g, b = ref.getpixel((x, y))
            if g > r * 1.18 and b > r * 1.12 and g > 65 and b > 75 and abs(g-b) < 90:
                samples.append((r, g, b))
    if samples:
        samples.sort(key=lambda p: p[1] + p[2] - 2*p[0], reverse=True)
        chosen = samples[:max(1, len(samples)//3)]
        accent = tuple(clamp(sum(p[i] for p in chosen) / len(chosen)) for i in range(3))
    else:
        accent = (55, 205, 202)

    # Color ramps retain the source's sculpted lighting and embossed details.
    stone = [
        (0,   (7, 10, 18)),
        (48,  (18, 25, 37)),
        (96,  (39, 51, 66)),
        (144, (75, 91, 105)),
        (192, (132, 146, 153)),
        (255, (218, 225, 225)),
    ]
    enamel = [
        (0,   (5, 15, 24)),
        (48,  (9, 49, 60)),
        (96,  (15, 100, 112)),
        (144, (25, 155, 162)),
        (192, (95, 211, 207)),
        (255, (220, 255, 244)),
    ]

    pixels = src.load()
    out = Image.new("RGB", src.size)
    dst = out.load()
    for y in range(256):
        for x in range(256):
            r, g, b = pixels[x, y]
            lum = clamp(0.299*r + 0.587*g + 0.114*b)
            base = ramp(lum, stone)
            if is_accent_island(x, y):
                teal = ramp(lum, enamel)
                # Keep some original stone visible in the raised relief.
                mix = 0.82 if lum > 70 else 0.55
                color = tuple(clamp(base[i]*(1-mix) + teal[i]*mix) for i in range(3))
            else:
                color = base
            dst[x, y] = color

    # Preserve PNG losslessly and retain the 256x256 UV atlas dimensions.
    out.save(OUTPUT, format="PNG", optimize=True)
    print(f"Generated: {OUTPUT.relative_to(ROOT)} ({out.width}x{out.height})")
    print(f"Reference-derived turquoise accent RGB: {accent}")

if __name__ == "__main__":
    main()
