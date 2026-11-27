from PIL import Image, ImageDraw, ImageFilter
from pathlib import Path
import math

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "カスタムモンスター" / "普通のいしにんぎょう.png"
OUTPUT = ROOT / "カスタムモンスター" / "シャコウ鬼神.png"

im = Image.open(SOURCE).convert("RGBA")
if im.size != (256, 256):
    raise SystemExit(f"Expected 256x256 UV texture, got {im.size}")
px = im.load()

# Rebuild the material palette while retaining the original UV relief and alpha.
# Dark stone -> charcoal/navy; midtones -> blue-grey; raised highlights -> turquoise/silver.
for y in range(256):
    for x in range(256):
        r,g,b,a = px[x,y]
        if a == 0:
            continue
        lum = (r*0.299 + g*0.587 + b*0.114)
        if lum < 35:
            c = (15, 22, 34)
        elif lum < 75:
            t=(lum-35)/40
            c=(int(18+18*t), int(27+23*t), int(42+28*t))
        elif lum < 125:
            t=(lum-75)/50
            c=(int(36+24*t), int(50+34*t), int(70+42*t))
        elif lum < 180:
            t=(lum-125)/55
            c=(int(60+28*t), int(84+50*t), int(112+55*t))
        else:
            t=min(1,(lum-180)/75)
            c=(int(88+75*t), int(134+75*t), int(154+85*t))
        px[x,y]=(c[0],c[1],c[2],a)

# Accent existing raised details with a luminous turquoise glaze.
for y in range(256):
    for x in range(256):
        r,g,b,a=px[x,y]
        if a and max(r,g,b)>105 and g>r*1.12 and b>r*1.15:
            px[x,y]=(min(255,r+10),min(255,g+28),min(255,b+30),a)

# The ornamental UV island at the left-middle is the existing scroll-work island.
# Replace its old thin ornament with two substantial inward-curling skirt spirals.
layer=Image.new("RGBA",(256,256),(0,0,0,0))
d=ImageDraw.Draw(layer)
def spiral(cx,cy,rx,ry,turns=1.48):
    pts=[]
    for i in range(260):
        t=i/259
        ang=-math.pi/2 + turns*2*math.pi*t
        rad=0.035+0.965*t
        pts.append((cx+math.cos(ang)*rx*rad, cy+math.sin(ang)*ry*rad))
    # dark engraved groove and bright turquoise raised edge
    d.line([(x+1.2,y+1.2) for x,y in pts], fill=(8,17,28,235), width=6, joint="curve")
    d.line(pts, fill=(46,205,207,255), width=3, joint="curve")
    d.line([(x-1.0,y-1.0) for x,y in pts], fill=(170,248,239,235), width=1, joint="curve")
    d.ellipse((cx-3,cy-3,cx+3,cy+3), fill=(115,245,230,255), outline=(15,48,62,255), width=1)

# Keep all marks inside the scroll island's 0..80 x 88..170 footprint.
spiral(22,128,17,25,1.35)
spiral(59,128,17,25,1.35)
# A crisp central V-shaped flourish ties the two curls together.
d.line([(37,153),(40,158),(43,153)], fill=(8,17,28,230), width=6)
d.line([(37,151),(40,156),(43,151)], fill=(95,235,222,255), width=3)

# Compose softly so the original island's stone shading remains visible beneath the new raised lines.
im=Image.alpha_composite(im,layer)
# Reintroduce a subtle satin finish and keep transparent atlas pixels untouched.
im.save(OUTPUT,optimize=True)
print(f"Wrote {OUTPUT} ({im.size[0]}x{im.size[1]})")
