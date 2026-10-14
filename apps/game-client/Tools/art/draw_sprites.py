#!/usr/bin/env python3
"""
Draws the mini-game sprites (original art for this project, flat and cute, readable on small phones).
Re-run to regenerate: python3 apps/game-client/Tools/art/draw_sprites.py   (needs Pillow)
Output: apps/game-client/Assets/_Project/Resources/Art/Minigames/*.png (256x256, transparent)
"""
import math, os
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), '..', '..', 'Assets', '_Project', 'Resources', 'Art', 'Minigames')
S = 256          # final size
K = 4            # supersampling
W = S * K
OUTLINE = (70, 50, 45, 255)

def canvas():
    return Image.new('RGBA', (W, W), (0, 0, 0, 0))

def save(img, name, shadow=True):
    if shadow:  # soft drop shadow under the shape
        a = img.split()[3]
        sh = Image.new('RGBA', img.size, (0, 0, 0, 0))
        sh.putalpha(a.point(lambda v: v * 0.28))
        sh = sh.filter(ImageFilter.GaussianBlur(K * 4))
        base = Image.new('RGBA', img.size, (0, 0, 0, 0))
        base.alpha_composite(sh, (0, K * 6))
        base.alpha_composite(img)
        img = base
    os.makedirs(OUT, exist_ok=True)
    img.resize((S, S), Image.LANCZOS).save(os.path.join(OUT, name + '.png'))

def p(v): return int(v * K)

def ellipse(d, box, fill, width=6, outline=OUTLINE):
    d.ellipse([p(x) for x in box], fill=fill, outline=outline, width=p(width) if outline else 0)

def shine(d, cx, cy, r):
    d.ellipse([p(cx - r), p(cy - r * 0.6), p(cx + r), p(cy + r * 0.6)], fill=(255, 255, 255, 170))

def face(d, cx, cy, s=1.0, color=OUTLINE):
    """Little happy face: everything in this game is friendly."""
    for dx in (-14, 14):
        d.ellipse([p(cx + dx * s - 5 * s), p(cy - 5 * s), p(cx + dx * s + 5 * s), p(cy + 5 * s)], fill=color)
    d.arc([p(cx - 12 * s), p(cy - 4 * s), p(cx + 12 * s), p(cy + 16 * s)], 20, 160, fill=color, width=p(4 * s))
    for dx in (-26, 26):  # blush
        d.ellipse([p(cx + dx * s - 7 * s), p(cy + 6 * s), p(cx + dx * s + 7 * s), p(cy + 14 * s)], fill=(255, 140, 140, 150))

def lemon():
    img = canvas(); d = ImageDraw.Draw(img)
    ellipse(d, (40, 64, 216, 196), (255, 221, 64, 255))
    d.ellipse([p(26), p(116), p(52), p(140)], fill=(255, 221, 64, 255), outline=OUTLINE, width=p(6))
    d.ellipse([p(204), p(116), p(230), p(140)], fill=(255, 221, 64, 255), outline=OUTLINE, width=p(6))
    ellipse(d, (40, 64, 216, 196), None)
    d.polygon([(p(128), p(66)), (p(160), p(38)), (p(178), p(52)), (p(140), p(72))], fill=(110, 190, 90, 255), outline=OUTLINE)
    shine(d, 92, 96, 22); face(d, 128, 132)
    save(img, 'lemon')

def ice():
    img = canvas(); d = ImageDraw.Draw(img)
    d.rounded_rectangle([p(54), p(54), p(202), p(202)], radius=p(30), fill=(190, 235, 255, 235), outline=(80, 140, 190, 255), width=p(6))
    d.rounded_rectangle([p(74), p(70), p(120), p(96)], radius=p(10), fill=(255, 255, 255, 220))
    face(d, 128, 136, 0.9, (60, 110, 160, 255))
    save(img, 'ice')

def mint():
    img = canvas(); d = ImageDraw.Draw(img)
    for ang, col in ((-25, (90, 190, 110, 255)), (25, (120, 210, 120, 255))):
        leaf = Image.new('RGBA', (W, W)); ld = ImageDraw.Draw(leaf)
        ld.ellipse([p(84), p(40), p(172), p(216)], fill=col, outline=OUTLINE, width=p(6))
        ld.line([p(128), p(60), p(128), p(200)], fill=OUTLINE, width=p(4))
        img.alpha_composite(leaf.rotate(ang, center=(W / 2, W * 0.8), resample=Image.BICUBIC))
    save(img, 'mint')

def flower(name, petal, center=(255, 205, 60, 255)):
    img = canvas(); d = ImageDraw.Draw(img)
    for k in range(6):
        a = k * math.pi / 3
        cx, cy = 128 + 58 * math.cos(a), 128 + 58 * math.sin(a)
        ellipse(d, (cx - 40, cy - 40, cx + 40, cy + 40), petal)
    ellipse(d, (84, 84, 172, 172), center)
    face(d, 128, 126, 0.8)
    save(img, name)

def dry_leaf():
    img = canvas(); d = ImageDraw.Draw(img)
    leaf = Image.new('RGBA', (W, W)); ld = ImageDraw.Draw(leaf)
    ld.polygon([(p(128), p(30)), (p(200), p(110)), (p(176), p(190)), (p(128), p(226)), (p(80), p(190)), (p(56), p(110))], fill=(176, 120, 70, 255), outline=OUTLINE)
    ld.line([p(128), p(40), p(128), p(220)], fill=OUTLINE, width=p(5))
    for y in (90, 130, 170):
        ld.line([p(128), p(y), p(170), p(y - 24)], fill=OUTLINE, width=p(4))
        ld.line([p(128), p(y), p(86), p(y - 24)], fill=OUTLINE, width=p(4))
    img.alpha_composite(leaf.rotate(-20, resample=Image.BICUBIC))
    save(img, 'dry_leaf')

def pebble():
    img = canvas(); d = ImageDraw.Draw(img)
    ellipse(d, (48, 84, 208, 196), (150, 150, 160, 255))
    d.ellipse([p(80), p(104), p(124), p(124)], fill=(200, 200, 210, 200))
    save(img, 'pebble')

def basket():
    img = canvas(); d = ImageDraw.Draw(img)
    d.arc([p(56), p(30), p(200), p(170)], 180, 360, fill=OUTLINE, width=p(14))
    d.arc([p(56), p(30), p(200), p(170)], 180, 360, fill=(190, 130, 70, 255), width=p(8))
    d.polygon([(p(20), p(100)), (p(236), p(100)), (p(206), p(226)), (p(50), p(226))], fill=(214, 156, 90, 255), outline=OUTLINE)
    for x in range(40, 230, 26):
        d.line([p(x), p(104), p(x + (128 - x) * 0.14), p(222)], fill=(170, 112, 60, 255), width=p(5))
    for y in (140, 180):
        d.line([p(28 + (y - 100) * 0.23), p(y), p(228 - (y - 100) * 0.23), p(y)], fill=(170, 112, 60, 255), width=p(5))
    d.rounded_rectangle([p(14), p(90), p(242), p(116)], radius=p(12), fill=(232, 176, 104, 255), outline=OUTLINE, width=p(6))
    save(img, 'basket')

def skewer(state):
    cols = {'raw': (236, 196, 196, 255), 'gold': (196, 120, 60, 255), 'burnt': (60, 45, 40, 255)}
    img = canvas(); d = ImageDraw.Draw(img)
    d.line([p(128), p(20), p(128), p(240)], fill=(200, 160, 110, 255), width=p(12))
    d.line([p(128), p(20), p(128), p(240)], fill=OUTLINE, width=p(3))
    for cy in (68, 128, 188):
        ellipse(d, (88, cy - 36, 168, cy + 36), cols[state])
        if state == 'gold':
            d.line([p(100), p(cy - 6), p(156), p(cy - 6)], fill=(140, 70, 30, 255), width=p(6))  # grill marks
            d.line([p(100), p(cy + 12), p(156), p(cy + 12)], fill=(140, 70, 30, 255), width=p(6))
            shine(d, 112, cy - 18, 10)
    if state == 'gold': face(d, 128, 126, 0.55, (90, 40, 20, 255))
    save(img, 'skewer_' + state)

def bread(state):
    cols = {'raw': (250, 236, 210, 255), 'gold': (226, 160, 80, 255), 'burnt': (70, 50, 40, 255)}
    img = canvas(); d = ImageDraw.Draw(img)
    d.rounded_rectangle([p(36), p(96), p(220), p(206)], radius=p(30), fill=cols[state], outline=OUTLINE, width=p(6))
    ellipse(d, (36, 52, 220, 150), cols[state])
    d.rectangle([p(42), p(100), p(214), p(130)], fill=cols[state])
    for x in (86, 128, 170):
        d.arc([p(x - 22), p(80), p(x + 22), p(120)], 200, 340, fill=OUTLINE, width=p(5))
    if state != 'burnt': face(d, 128, 156, 0.8)
    if state == 'gold': shine(d, 84, 86, 18)
    save(img, 'bread_' + state)

def smoke():
    img = canvas(); d = ImageDraw.Draw(img)
    for cx, cy, r in ((100, 170, 44), (150, 130, 50), (110, 80, 40), (160, 60, 30)):
        d.ellipse([p(cx - r), p(cy - r), p(cx + r), p(cy + r)], fill=(120, 120, 120, 150))
    save(img, 'smoke', shadow=False)

def glass():
    """Glass outline only: the tea is drawn behind it by the game, so any fill level works."""
    img = canvas(); d = ImageDraw.Draw(img)
    d.polygon([(p(44), p(20)), (p(212), p(20)), (p(190), p(236)), (p(66), p(236))], fill=(255, 255, 255, 50))
    d.line([(p(44), p(20)), (p(66), p(236)), (p(190), p(236)), (p(212), p(20))], fill=(90, 120, 150, 255), width=p(8), joint='curve')
    d.line([(p(60), p(40)), (p(76), p(200))], fill=(255, 255, 255, 190), width=p(10))
    save(img, 'glass', shadow=False)

def star(name, fill):
    img = canvas(); d = ImageDraw.Draw(img)
    pts = []
    for k in range(10):
        a = -math.pi / 2 + k * math.pi / 5
        r = 112 if k % 2 == 0 else 50
        pts.append((p(128 + r * math.cos(a)), p(136 + r * math.sin(a))))
    d.polygon(pts, fill=fill, outline=OUTLINE)
    d.line(pts + [pts[0]], fill=OUTLINE, width=p(7), joint='curve')
    if fill[0] > 200: shine(d, 104, 100, 14)
    save(img, name)

def sparkle():
    img = canvas(); d = ImageDraw.Draw(img)
    for a in (0, 90):
        rot = Image.new('RGBA', (W, W)); rd = ImageDraw.Draw(rot)
        rd.polygon([(p(128), p(10)), (p(146), p(110)), (p(128), p(246)), (p(110), p(110))], fill=(255, 250, 200, 255))
        img.alpha_composite(rot.rotate(a, resample=Image.BICUBIC))
    d.ellipse([p(104), p(104), p(152), p(152)], fill=(255, 255, 255, 255))
    save(img, 'sparkle', shadow=False)

def grill_spot():
    img = canvas(); d = ImageDraw.Draw(img)
    ellipse(d, (16, 16, 240, 240), (70, 70, 80, 255), 8)
    for y in range(52, 220, 34):
        half = math.sqrt(max(0, 108 ** 2 - (y - 128) ** 2))
        d.line([p(128 - half), p(y), p(128 + half), p(y)], fill=(150, 150, 160, 255), width=p(8))
    save(img, 'grill_spot')

def main():
    lemon(); ice(); mint()
    flower('flower_pink', (255, 150, 190, 255)); flower('flower_yellow', (255, 230, 110, 255)); flower('flower_purple', (190, 150, 240, 255))
    dry_leaf(); pebble(); basket()
    for s in ('raw', 'gold', 'burnt'): skewer(s); bread(s)
    smoke(); glass(); grill_spot(); sparkle()
    star('star_on', (255, 214, 60, 255)); star('star_off', (210, 210, 215, 255))
    print('sprites written to', os.path.relpath(OUT))

if __name__ == '__main__':
    main()
