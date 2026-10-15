#!/usr/bin/env python3
# Bakes a bitmap font strike into an atlas for Chicago95's pixel text: every glyph rendered aliased (1 bit), white
# on transparent, packed in rows, with a metrics file: one line per character
#   code x y w h xoff yoff advance        (y down; xoff/yoff place the glyph box relative to the pen at the line top)
# and a first line:  font ascent descent
#   tools/make_font.py <font.otb> <pixel size> <out base name>
import sys
from PIL import Image, ImageDraw, ImageFont

path, size, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
font = ImageFont.truetype(path, size)
ascent, descent = font.getmetrics()
chars = [chr(c) for c in range(32, 127)] + list("×·…°±²³µ«»–—‘’“”•")
glyphs = []
for ch in chars:
    try:
        mask = font.getmask(ch, mode="1")
    except Exception:
        continue
    if ch != " " and mask.size[0] == 0:
        continue
    bbox = font.getbbox(ch, mode="1")          # relative to the pen at the line top (anchor "la")
    adv = round(font.getlength(ch, mode="1"))
    img = Image.new("L", mask.size)
    img.putdata([255 if v else 0 for v in mask])
    glyphs.append((ch, img, bbox, adv))

W = 256
x = y = row = 0
places = []
for ch, img, bbox, adv in glyphs:
    w, h = img.size
    if x + w + 1 > W: x = 0; y += row + 1; row = 0
    places.append((ch, x, y, img, bbox, adv))
    x += w + 1; row = max(row, h)
H = y + row + 1
atlas = Image.new("RGBA", (W, H), (255, 255, 255, 0))
lines = [f"font {ascent} {descent}"]
for ch, gx, gy, img, bbox, adv in places:
    w, h = img.size
    if w and h:
        atlas.paste(Image.new("RGBA", (w, h), (255, 255, 255, 255)), (gx, gy), img)
    lines.append(f"{ord(ch)} {gx} {gy} {w} {h} {bbox[0]} {bbox[1]} {adv}")
atlas.save(out + ".png")
open(out + ".txt", "w").write("\n".join(lines) + "\n")
print(out, len(places), "glyphs", W, "x", H, "ascent", ascent, "descent", descent)
