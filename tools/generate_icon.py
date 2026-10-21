"""Generate the Production Transcription Windows icon. Requires Pillow.
Run: python tools/generate_icon.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

SIZE = 512
SCALE = 4
s = SIZE * SCALE
im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
d = ImageDraw.Draw(im)
def p(v): return round(v * SCALE)
def box(xy, **kw): d.rounded_rectangle(tuple(p(v) for v in xy), **kw)
navy = "#0B1521"
teal = "#36E4DF"
cyan = "#12BBD8"
silver = "#EAF3F7"
box((12, 12, 500, 500), radius=p(105), fill=navy, outline=teal, width=p(14))
# Waveform: readable at 16px, no text.
for x, h, color in [(83, 58, cyan), (115, 111, teal), (149, 177, cyan),
                     (363, 177, cyan), (397, 111, teal), (429, 58, cyan)]:
    box((x-9, 256-h//2, x+9, 256+h//2), radius=p(9), fill=color)
# Microphone capsule with horizontal grille marks.
box((194, 91, 318, 330), radius=p(60), fill=silver)
for y in (162, 211, 260):
    box((209, y, 242, y+12), radius=p(6), fill=navy)
    box((270, y, 303, y+12), radius=p(6), fill=navy)
# Cradle and stand.
d.arc((p(170), p(186), p(342), p(389)), 0, 180, fill=teal, width=p(17))
d.line((p(256), p(385), p(256), p(421)), fill=teal, width=p(18))
box((188, 418, 324, 436), radius=p(9), fill=teal)
im = im.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
out = Path(__file__).resolve().parents[1] / "src" / "ProductionTranscription.App" / "Assets"
out.mkdir(parents=True, exist_ok=True)
im.save(out / "ProductionTranscription.ico", format="ICO", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
im.save(out / "ProductionTranscription.png")
print(out / "ProductionTranscription.ico")
