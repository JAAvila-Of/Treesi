"""Regenerate committed native icons from the original Treesi SVG.

Authoring-only requirements: CairoSVG and Pillow. Not used during product builds.
"""

from io import BytesIO
from pathlib import Path
import struct

import cairosvg
from PIL import Image


root = Path(__file__).resolve().parent.parent
brand = root / "assets" / "brand"
source = brand / "treesi.svg"
sizes = (16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024)
images = {}
for size in sizes:
    data = cairosvg.svg2png(url=str(source), output_width=size, output_height=size)
    images[size] = data
(brand / "treesi.png").write_bytes(images[256])
image = Image.open(BytesIO(images[256])).convert("RGBA")
image.save(brand / "treesi.ico", bitmap_format="bmp", sizes=[(s, s) for s in sizes if s <= 256],
           append_images=[Image.open(BytesIO(images[s])).convert("RGBA") for s in sizes if s < 256])
# Modern ICNS PNG members, including 16/32/64 logical sizes at 2x scale.
members = {b"icp4": 16, b"icp5": 32, b"icp6": 64, b"ic07": 128,
           b"ic08": 256, b"ic09": 512, b"ic10": 1024,
           b"ic11": 32, b"ic12": 64, b"ic13": 256, b"ic14": 512}
payload = b"".join(kind + struct.pack(">I", len(images[size]) + 8) + images[size]
                   for kind, size in members.items())
(brand / "treesi.icns").write_bytes(b"icns" + struct.pack(">I", len(payload) + 8) + payload)
