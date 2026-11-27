#!/usr/bin/env python3
"""Generate an ornamental Shakou Kishin texture from the Ishiningyo UV atlas."""
from pathlib import Path
from PIL import Image, ImageDraw
import math

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/"カスタムモンスター"/"普通のいしにんぎょう.png"
OUTPUT=ROOT/"カスタムモンスター"/"シャコウ鬼神.png"

def clamp(v): return max(0,min(255,int(v)))
def ramp(v,stops):
    for (x0,c0),(x1,c1) in zip(stops,stops[1:]):
        if v<=x1:
            t=(v-x0)/max(1,x1-x0)
            return tuple(clamp(a+(b-a)*t) for a,b in zip(c0,c1))
    return stops[-1][1]

def spiral(draw,cx,cy,rx,ry,phase=0):
    pts=[]
    for n in range(220):
        t=n/219
        a=phase+t*math.pi*3.2
        r=1-0.91*t
        pts.append((cx+math.cos(a)*rx*r,cy+math.sin(a)*ry*r))
    draw.line([(x+0.8,y+1.0) for x,y in pts],fill=(5,19,31),width=3)
    draw.line(pts,fill=(69,211,204),width=2)
    draw.ellipse((cx-1.4,cy-1.4,cx+1.4,cy+1.4),fill=(177,255,245))

def main():
    src=Image.open(SOURCE).convert("RGB")
    if src.size!=(256,256): raise ValueError(f"Expected 256x256, got {src.size}")
    stone=[(0,(7,10,18)),(48,(18,25,37)),(96,(39,51,66)),(144,(75,91,105)),(192,(132,146,153)),(255,(218,225,225))]
    enamel=[(0,(5,15,24)),(48,(9,49,60)),(96,(15,100,112)),(144,(25,155,162)),(192,(95,211,207)),(255,(220,255,244))]
    sp=src.load()
    out=Image.new("RGB",src.size); op=out.load()
    # Rebuild the complete material palette while retaining embossed light/shadow.
    for y in range(256):
        for x in range(256):
            r,g,b=sp[x,y]; lum=clamp(.299*r+.587*g+.114*b)
            base=ramp(lum,stone)
            # Raised decorative islands: large scroll panel, top ornamental plate,
            # and narrow hem/edge strips identified in the supplied atlas.
            accent=((x<82 and 104<=y<174) or (54<=x<113 and y<62)
                    or (x<256 and 190<=y<256) or (205<=x<251 and 75<=y<190))
            if accent:
                teal=ramp(lum,enamel); m=.78 if lum>65 else .48
                base=tuple(clamp(base[i]*(1-m)+teal[i]*m) for i in range(3))
            op[x,y]=base

    # The broad left-hand ornamental UV island is the skirt-scroll panel.
    # Replace the old generic curling relief with a paired, inward-facing spiral motif.
    d=ImageDraw.Draw(out)
    spiral(d,23,139,12,17,0.1)
    spiral(d,59,139,12,17,math.pi+0.1)
    d.line([(7,159),(18,166),(40,170),(64,166),(76,159)],fill=(8,24,36),width=4)
    d.line([(7,157),(18,164),(40,168),(64,164),(76,157)],fill=(96,230,220),width=2)
    # Small inner highlights give the curls the polished enamel look from the reference.
    d.ellipse((38,165,42,169),fill=(180,255,246))

    # Raised turquoise piping and rivets along the lower horizontal hem UV strips.
    for y in (193,232):
        d.line([(2,y),(253,y)],fill=(8,25,38),width=3)
        d.line([(2,y-1),(253,y-1)],fill=(92,224,216),width=1)
    for x in range(8,253,12):
        d.ellipse((x-1,211,x+1,213),fill=(133,245,234))

    # Decorative medallion details on the upper central panel.
    for cx in (70,99):
        d.ellipse((cx-7,86,cx+7,100),fill=(10,35,49),outline=(109,239,229),width=2)
        d.ellipse((cx-4,89,cx+4,97),outline=(57,178,181),width=2)
        d.ellipse((cx-1,92,cx+1,94),fill=(207,255,248))
    d.line([(77,82),(85,76),(92,82)],fill=(111,241,230),width=2)
    d.ellipse((81,119,91,129),fill=(17,72,83),outline=(135,249,238),width=2)
    for x,y in [(75,135),(81,132),(87,131),(93,132),(99,135)]:
        d.ellipse((x,y,x+2,y+2),fill=(150,250,239))

    OUTPUT.parent.mkdir(parents=True,exist_ok=True)
    out.save(OUTPUT,format="PNG",optimize=True)
    print(f"Generated {OUTPUT.relative_to(ROOT)} ({out.width}x{out.height})")

if __name__=="__main__": main()
