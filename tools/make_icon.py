"""Draws DDL/Assets/ddl.ico (a white clock on a blue rounded square).

Run (needs Pillow): python tools/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

S = 1024  # draw large, then let Pillow downsample for each icon size
OUT = Path(__file__).resolve().parent.parent / "DDL" / "Assets" / "ddl.ico"

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

d.rounded_rectangle([40, 40, S - 40, S - 40], radius=220, fill=(52, 92, 230, 255))

c, r, w = S / 2, 300, 84  # clock face
d.ellipse([c - r, c - r, c + r, c + r], outline=(255, 255, 255, 255), width=w)
d.line([(c, c), (c, c - 190)], fill=(255, 255, 255, 255), width=w)        # minute hand -> 12
d.line([(c, c), (c + 140, c)], fill=(255, 255, 255, 255), width=w)        # hour hand -> 3
d.ellipse([c - w / 2, c - w / 2, c + w / 2, c + w / 2], fill=(255, 255, 255, 255))

OUT.parent.mkdir(parents=True, exist_ok=True)
img.save(OUT, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(OUT)
