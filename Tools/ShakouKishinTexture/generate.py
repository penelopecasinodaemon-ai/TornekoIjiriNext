#!/usr/bin/env python3
"""Create a fresh Shakou Kishin-inspired 256x256 UV texture.

The source image is used ONLY as a UV-island coverage mask. None of its stone
colors, relief, ornaments, or painted details are copied into the result.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
import math

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "カスタムモンスター" / "普通のいしにんぎょう.png"
OUTPUT = ROOT / "カスタムモンスター" / "シャコウ鬼神.png"
S = 4
W = H = 256
SW = SH = W * S

# Shakou Kishin palette: graphite armor, cold silver edges, luminous turquoise.
INK = (7, 17, 27)
DARK = (16, 29, 43)
STONE = (43, 57, 72)
MID = (73, 91, 105)
SILVER = (156, 174, 181)
PALE = (220, 237, 232)
TEAL_DARK = (8, 58, 69)
TEAL = (22, 151, 158)
AQUA = (73, 214, 207)
GLOW = (190, 255, 242)

def xy(v):
    return int(round(v * S))

def line(d, pts, fill, width=1):
    d.line([(xy(x), xy(y)) for x, y in pts], fill=fill, width=max(1, xy(width)), joint="curve")

def ellipse(d, box, fill=None, outline=None, width=1):
    b = tuple(xy(v) for v in box)
    d.ellipse(b, fill=fill, outline=outline, width=max(1, xy(width)) if outline else 1)

def polygon(d, pts, fill):
    d.polygon([(xy(x), xy(y)) for x, y in pts], fill=fill)

def arc(d, box, start, end, fill, width=1):
    d.arc(tuple(xy(v) for v in box), start, end, fill=fill, width=max(1, xy(width)))

def spiral(d, cx, cy, rx, ry, color=AQUA, width=1.5, turns=1.65, phase=0):
    pts=[]
    for i in range(160):
        t=i/159
        a=phase+t*math.tau*turns
        r=1-0.91*t
        pts.append((cx+math.cos(a)*rx*r, cy+math.sin(a)*ry*r))
    line(d, pts, INK, width+1.6)
    line(d, pts, color, width)
    ellipse(d,(cx-.9,cy-.9,cx+.9,cy+.9),GLOW)

def gem(d, cx, cy, rx, ry):
    pts=[(cx,cy-ry),(cx+rx*.78,cy-ry*.35),(cx+rx,cy+ry*.25),
         (cx,cy+ry),(cx-rx,cy+ry*.25),(cx-rx*.78,cy-ry*.35)]
    polygon(d,pts,INK)
    polygon(d,[(cx,cy-ry+1),(cx+rx*.7,cy-ry*.2),(cx,cy+ry*.68),(cx-rx*.7,cy-ry*.2)],TEAL)
    polygon(d,[(cx,cy-ry+1),(cx+rx*.7,cy-ry*.2),(cx,cy)],AQUA)
    ellipse(d,(cx-1,cy-ry*.25,cx+1,cy-ry*.25+2),GLOW)

def main():
    src=Image.open(SOURCE).convert("RGB")
    if src.size != (W,H):
        raise ValueError(f"Expected {W}x{H} UV guide, got {src.size}")

    # White is the atlas background. The binary mask keeps only the actual
    # mapped UV islands; the source's grayscale artwork is never sampled.
    gray=src.convert("L")
    mask=gray.point(lambda p: 255 if p < 247 else 0).resize((SW,SH),Image.Resampling.NEAREST)

    # Fresh procedural material: independent of the source's brightness/relief.
    base=Image.new("RGB",(SW,SH),(255,255,255))
    bp=base.load()
    for y in range(SH):
        yy=y/S
        for x in range(SW):
            if mask.getpixel((x,y)) == 0:
                continue
            xx=x/S
            # A restrained cool-metal gradient across the entire UV layout.
            light=0.5+0.5*math.cos((xx-128)/180*math.pi)
            vertical=0.5+0.5*math.cos((yy-112)/210*math.pi)
            t=max(0,min(1,0.20+0.30*light+0.20*vertical))
            c=tuple(int(DARK[i]*(1-t)+MID[i]*t) for i in range(3))
            # Subtle turquoise bounce light near edges, not a flat recolor.
            glow=max(0,1-abs(xx-128)/155)*max(0,1-abs(yy-128)/190)*0.10
            c=tuple(min(255,int(c[i]*(1-glow)+TEAL[i]*glow)) for i in range(3))
            bp[x,y]=c

    # Draw details at 4x resolution, then clip everything to UV coverage.
    art=Image.new("RGBA",(SW,SH),(0,0,0,0))
    d=ImageDraw.Draw(art)

    # A. Main head/face scroll island (confirmed by the in-game UV test:
    # left-middle curved ornament island). Create a new face-side ornament.
    # The paired curls are intentionally placed away from the eye-strip UV.
    spiral(d,22,145,10.5,16,AQUA,1.8,1.55,0.15)
    spiral(d,58,145,10.5,16,AQUA,1.8,1.55,math.pi+0.15)
    line(d,[(5,125),(12,116),(25,111),(40,108),(55,111),(70,119),(77,128)],SILVER,1.5)
    line(d,[(5,127),(12,118),(25,113),(40,110),(55,113),(70,121),(77,130)],TEAL,1.2)
    # Small temple studs and a central brow flourish.
    for x,y in [(8,139),(12,132),(68,132),(72,139)]:
        ellipse(d,(x-1.5,y-1.5,x+1.5,y+1.5),GLOW,outline=TEAL,width=.7)
    line(d,[(29,119),(36,114),(40,118),(44,114),(51,119)],AQUA,1.5)

    # B. Small horizontal UV island at upper-left: eye lens.
    # Build a dark eye socket with turquoise glass and a bright pinpoint.
    ellipse(d,(3,8,42,25),INK,outline=SILVER,width=1.2)
    ellipse(d,(7,10,38,23),TEAL_DARK,outline=AQUA,width=1)
    ellipse(d,(17,12,27,22),AQUA,outline=PALE,width=.8)
    ellipse(d,(20,14,24,18),GLOW)

    # C. Upper-center crown/forehead panel: silver crest and V-shaped enamel.
    line(d,[(57,26),(65,13),(78,8),(91,13),(103,26)],INK,3)
    line(d,[(59,25),(67,15),(78,11),(89,15),(101,25)],SILVER,1.8)
    line(d,[(65,28),(78,17),(91,28)],AQUA,2.2)
    line(d,[(69,30),(78,23),(87,30)],GLOW,.8)
    gem(d,78,37,5,5)

    # D. Large upper-right trapezoid island: primary armor plate with crown rim.
    line(d,[(125,15),(136,7),(221,7),(234,17)],INK,3)
    line(d,[(127,15),(138,9),(219,9),(232,17)],SILVER,1.4)
    line(d,[(132,21),(145,14),(215,14),(228,22)],AQUA,1.8)
    # Repeating rivets along the lower arc.
    for x in range(145,218,9):
        y=40-0.004*(x-181)**2
        ellipse(d,(x-1,y-1,x+1,y+1),GLOW,outline=TEAL,width=.5)
    # Center crest motif.
    polygon(d,[(181,17),(190,27),(181,36),(172,27)],TEAL_DARK)
    polygon(d,[(181,19),(187,27),(181,33),(175,27)],AQUA)
    ellipse(d,(179,24,183,28),GLOW)

    # E. Mid-left narrow ornament island: collar/diadem details.
    arc(d,(4,66,75,101),195,345,SILVER,2)
    arc(d,(9,70,70,97),195,345,AQUA,1.5)
    for x in (17,29,41,53,65):
        ellipse(d,(x-1.3,88,x+1.3,90.6),GLOW)

    # F. Large right-hand rounded panel: chest/torso armor plate.
    # A raised central breastplate, necklace, and jewel.
    line(d,[(166,78),(174,69),(214,69),(228,82)],INK,3)
    line(d,[(168,78),(176,72),(212,72),(226,83)],SILVER,1.5)
    line(d,[(172,87),(181,80),(207,80),(220,89)],AQUA,1.6)
    gem(d,195,111,10,12)
    arc(d,(169,119,221,159),15,165,SILVER,1.2)
    arc(d,(174,122,216,155),15,165,AQUA,1.3)
    for x,y in [(178,132),(185,137),(195,139),(205,137),(212,132)]:
        ellipse(d,(x-1,y-1,x+1,y+1),GLOW,outline=TEAL,width=.5)
    # Two shoulder medallions.
    for cx in (177,213):
        ellipse(d,(cx-8,151,cx+8,168),INK,outline=SILVER,width=1.5)
        ellipse(d,(cx-6,153,cx+6,166),TEAL_DARK,outline=AQUA,width=1)
        ellipse(d,(cx-2,157,cx+2,162),GLOW)

    # G. Thin vertical strip on far right: repeating turquoise piping.
    line(d,[(243,77),(243,187)],INK,3)
    line(d,[(245,77),(245,187)],AQUA,1)
    for y in range(83,185,8):
        ellipse(d,(240,y-1,247,y+2),TEAL,outline=GLOW,width=.45)

    # H. Lower UV bands: layered skirt hem, turquoise piping, and rivets.
    # The atlas has several independent horizontal strips; the motifs are
    # repeated so each strip reads as a matching trim from any orientation.
    for y in (194,205,216,228,239,250):
        line(d,[(2,y),(254,y)],INK,2.2)
        line(d,[(2,y-.8),(254,y-.8)],SILVER,.75)
        line(d,[(2,y+1),(254,y+1)],AQUA,.9)
    for x in range(7,253,12):
        for y in (199,222,244):
            ellipse(d,(x-1,y-1,x+1,y+1),GLOW,outline=TEAL,width=.45)

    # Paired skirt-scroll medallions on the broad lower-left island where
    # the in-game mapping can be refined independently from the face curls.
    spiral(d,21,178,7,8,AQUA,1.2,1.45)
    spiral(d,59,178,7,8,AQUA,1.2,1.45,math.pi)
    line(d,[(5,187),(19,184),(40,186),(62,184),(77,187)],SILVER,1.2)

    # Leg/boot panel accents on the lower central region.
    for cx in (91,132,174):
        line(d,[(cx-11,254),(cx-8,246),(cx+8,246),(cx+11,254)],INK,2)
        line(d,[(cx-9,252),(cx-6,248),(cx+6,248),(cx+9,252)],AQUA,1)

    # Clip all newly created paint to the actual atlas islands.
    clipped=Image.new("RGBA",(SW,SH),(0,0,0,0))
    clipped.paste(art,(0,0),mask)
    composed=Image.alpha_composite(base.convert("RGBA"),clipped).convert("RGB")
    # Preserve exact 256x256 atlas dimensions.
    result=composed.resize((W,H),Image.Resampling.LANCZOS)
    OUTPUT.parent.mkdir(parents=True,exist_ok=True)
    result.save(OUTPUT,format="PNG",optimize=True)
    print(f"Generated fresh UV artwork: {OUTPUT.relative_to(ROOT)} ({W}x{H})")

if __name__ == "__main__":
    main()
