from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent
PREVIEW = Path(r'C:\Users\Public\PakRatSourceStyleV13.png')

OUT = 256
WORK = 1400


def rgba(h, a=255):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def rr(draw, xy, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(xy, radius=radius, fill=fill, outline=outline, width=width)


BG = rgba('171A20')
PANEL = rgba('20252D')
BORDER = rgba('4B5563')
TOP = rgba('DB9B43')
LEFT = rgba('5B6470')
RIGHT = rgba('2D333D')
EDGE = rgba('F3F4F6')
SOFT = rgba('CDD4DD')

img = Image.new('RGBA', (WORK, WORK), (0, 0, 0, 0))
shadow = Image.new('RGBA', (WORK, WORK), (0, 0, 0, 0))
sd = ImageDraw.Draw(shadow)
rr(sd, (160, 175, 1240, 1255), 255, (0, 0, 0, 118))
shadow = shadow.filter(ImageFilter.GaussianBlur(72))
img.alpha_composite(shadow)

d = ImageDraw.Draw(img)
rr(d, (110, 110, 1290, 1290), 280, BG, outline=BORDER, width=10)
rr(d, (275, 275, 1125, 1125), 220, PANEL, outline=(255, 255, 255, 10), width=2)

# cube geometry
x = 700
y = 710
w = 250
h = 145

left_top = (x - w, y - h)
top_mid = (x, y - 2 * h)
right_top = (x + w, y - h)
center = (x, y)
left_bottom = (x - w, y + h)
right_bottom = (x + w, y + h)
bottom_mid = (x, y + 2 * h)

left_face = [left_top, center, bottom_mid, left_bottom]
right_face = [right_top, right_bottom, bottom_mid, center]
top_face = [left_top, top_mid, right_top, center]

# soft cube shadow
cube_shadow = Image.new('RGBA', (WORK, WORK), (0, 0, 0, 0))
cd = ImageDraw.Draw(cube_shadow, 'RGBA')
for poly in (left_face, right_face, top_face):
    shifted = [(px + 16, py + 22) for px, py in poly]
    cd.polygon(shifted, fill=(0, 0, 0, 68))
cube_shadow = cube_shadow.filter(ImageFilter.GaussianBlur(18))
img.alpha_composite(cube_shadow)

# filled faces
d.polygon(left_face, fill=LEFT)
d.polygon(right_face, fill=RIGHT)
d.polygon(top_face, fill=TOP)

# face lighting
hi = Image.new('RGBA', (WORK, WORK), (0, 0, 0, 0))
hd = ImageDraw.Draw(hi, 'RGBA')
hd.polygon([
    (left_top[0] + 24, left_top[1] + 16),
    (center[0] - 14, center[1] + 8),
    (bottom_mid[0] - 16, bottom_mid[1] - 30),
    (left_bottom[0] + 28, left_bottom[1] - 10),
], fill=(255, 255, 255, 26))
hd.polygon([
    (top_mid[0], top_mid[1] + 12),
    (right_top[0] - 18, right_top[1] + 12),
    (center[0] + 6, center[1] - 2),
    (left_top[0] + 26, left_top[1] + 12),
], fill=(255, 255, 255, 22))
hi = hi.filter(ImageFilter.GaussianBlur(2))
img.alpha_composite(hi)

# clean edges
stroke = 22
for pts in [
    [left_top, top_mid, right_top, center, left_top],
    [left_top, left_bottom, bottom_mid, right_bottom, right_top],
    [center, bottom_mid],
]:
    d.line(pts, fill=EDGE, width=stroke, joint='curve')

# very subtle source cue only as warm rim on top edge
accent = Image.new('RGBA', (WORK, WORK), (0, 0, 0, 0))
ad = ImageDraw.Draw(accent)
ad.line([(x + 4, y - 2 * h + 10), (right_top[0] - 22, right_top[1] + 8)], fill=(255, 214, 140, 140), width=10)
accent = accent.filter(ImageFilter.GaussianBlur(1.0))
img.alpha_composite(accent)

out = img.resize((OUT, OUT), Image.Resampling.LANCZOS)
png = ROOT / 'pakrat_modern_icon.png'
ico = ROOT / 'pakrat_modern.ico'
out.save(png)
out.save(ico, sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)])
out.save(PREVIEW)
print(PREVIEW)
