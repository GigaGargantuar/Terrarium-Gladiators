"""Generate the Terrarium Gladiators desktop icons from the vector-style mark."""

from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
S = 4


def pt(value: int) -> int:
    return value * S


image = Image.new("RGBA", (pt(256), pt(256)), "#081319")
draw = ImageDraw.Draw(image)

# Rounded app tile and glass terrarium.
draw.rounded_rectangle((0, 0, pt(256) - 1, pt(256) - 1), radius=pt(54), fill="#081319")
draw.rounded_rectangle((pt(52), pt(34), pt(204), pt(208)), radius=pt(75), fill="#10282d", outline="#43d6c4", width=pt(8))
draw.rectangle((pt(37), pt(191), pt(219), pt(222)), fill="#173b3a", outline="#43d6c4", width=pt(8))

# Gladiator helmet, crest, and face opening.
draw.polygon([(pt(86), pt(172)), (pt(94), pt(111)), (pt(122), pt(87)), (pt(160), pt(88)), (pt(183), pt(116)), (pt(154), pt(124)), (pt(154), pt(172))], fill="#f0ede2")
draw.polygon([(pt(113), pt(90)), (pt(106), pt(59)), (pt(124), pt(42)), (pt(164), pt(39)), (pt(143), pt(57)), (pt(143), pt(88))], fill="#ffc75a")
draw.rectangle((pt(79), pt(171), pt(160), pt(191)), fill="#f0ede2")
draw.rectangle((pt(105), pt(112), pt(119), pt(158)), fill="#081319")
draw.rectangle((pt(105), pt(143), pt(154), pt(158)), fill="#081319")

# Small organic arcs imply plants inside the glass arena.
draw.arc((pt(60), pt(117), pt(104), pt(198)), 120, 265, fill="#43d6c4", width=pt(7))
draw.arc((pt(157), pt(124), pt(198), pt(198)), 270, 62, fill="#43d6c4", width=pt(7))

image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.convert("RGB").save(ROOT / "Icon.bmp")
image.save(ROOT / "Icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
