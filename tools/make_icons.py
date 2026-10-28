"""Draws the KwartoTV icon and writes the Windows .ico plus Android launcher PNGs. Run: python3 tools/make_icons.py"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(__file__), "..")
S = 1024

def tv_glyph(size, bg=True):
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    k = size / S
    if bg:
        grad = Image.new("RGBA", (size, size))
        gd = ImageDraw.Draw(grad)
        for y in range(size):
            t = y / size
            gd.line([(0, y), (size, y)], fill=(int(255 - 40 * t), int(140 - 60 * t), int(70 - 30 * t), 255))
        mask = Image.new("L", (size, size), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(230 * k), fill=255)
        im.paste(grad, (0, 0), mask)
    # screen
    d.rounded_rectangle([int(190 * k), int(250 * k), int(834 * k), int(700 * k)], radius=int(70 * k), fill=(18, 20, 28, 255))
    # play triangle
    d.polygon([(int(450 * k), int(360 * k)), (int(450 * k), int(590 * k)), (int(640 * k), int(475 * k))], fill=(255, 179, 107, 255))
    # stand
    d.rounded_rectangle([int(380 * k), int(745 * k), int(644 * k), int(790 * k)], radius=int(22 * k), fill=(18, 20, 28, 255))
    return im

big = tv_glyph(S)
os.makedirs(os.path.join(ROOT, "src/KwartoTV.App/Assets"), exist_ok=True)
big.save(os.path.join(ROOT, "src/KwartoTV.App/Assets/kwartotv.png"))
big.save(os.path.join(ROOT, "src/KwartoTV.App/Assets/kwartotv.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

res = os.path.join(ROOT, "android/app/src/main/res")
for name, px in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
    folder = os.path.join(res, f"mipmap-{name}")
    os.makedirs(folder, exist_ok=True)
    big.resize((px, px), Image.LANCZOS).save(os.path.join(folder, "ic_launcher.png"))
    big.resize((px, px), Image.LANCZOS).save(os.path.join(folder, "ic_launcher_round.png"))
# adaptive foreground: glyph only, inside the 66% safe zone of a 432px canvas
fg = tv_glyph(S, bg=False).resize((288, 288), Image.LANCZOS)
canvas = Image.new("RGBA", (432, 432), (0, 0, 0, 0))
canvas.paste(fg, (72, 72), fg)
os.makedirs(os.path.join(res, "drawable-nodpi"), exist_ok=True)
canvas.save(os.path.join(res, "drawable-nodpi/ic_launcher_foreground.png"))
