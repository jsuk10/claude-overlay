"""Claude Session Overlay 아이콘: 민트색 둥근 창 안에서 큰 눈으로 내다보는 '지킴이'.

python scripts/make-icon.py <out_dir>
  icon-<n>.png (16~1024), app.ico, preview.png 를 만든다. 저장소에는 그중 assets/icon/ 의
  app.ico(Windows exe·트레이)와 icon-16~1024.png(Avalonia 트레이·macOS 앱 아이콘 원본)만 둔다.
작은 크기(<=32)는 디테일을 빼고 눈을 키운 단순판으로 그린다.
"""
import sys, os
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
os.makedirs(OUT, exist_ok=True)

MINT_TOP = (88, 222, 196)
MINT_BOT = (26, 160, 140)
BAR = (20, 120, 106)
INK = (24, 38, 48)
BLUSH = (255, 138, 160)


def squircle_mask(s, r):
    m = Image.new("L", (s, s), 0)
    ImageDraw.Draw(m).rounded_rectangle([0, 0, s - 1, s - 1], radius=r, fill=255)
    return m


def gradient(s, top, bot):
    g = Image.new("RGB", (1, s))
    for y in range(s):
        t = y / (s - 1)
        g.putpixel((0, y), tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)))
    return g.resize((s, s))


def draw(size, simple):
    S = size * 8 if size <= 64 else 1024  # 크게 그려서 줄인다
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    pad = S * (0.04 if simple else 0.07)
    body = int(S - 2 * pad)
    radius = int(body * 0.30)
    tile = Image.new("RGBA", (body, body))
    tile.paste(gradient(body, MINT_TOP, MINT_BOT), (0, 0))
    d = ImageDraw.Draw(tile)

    if not simple:
        # 창 제목 줄과 점 세 개: 오버레이 창이라는 느낌
        bar_h = body * 0.17
        d.rectangle([0, 0, body, bar_h], fill=BAR + (255,))
        for i in range(3):
            cx = body * (0.16 + i * 0.09); cy = bar_h / 2; r = body * 0.025
            d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 255, 255, 200))

    # 눈: 크고 하얗고, 눈동자는 살짝 오른쪽 위를 본다(내다보는 느낌)
    eye_y = body * (0.52 if simple else 0.56)
    eye_dx = body * (0.20 if simple else 0.19)
    ew, eh = body * (0.17 if simple else 0.135), body * (0.21 if simple else 0.165)
    for sx in (-1, 1):
        cx = body / 2 + sx * eye_dx
        d.ellipse([cx - ew, eye_y - eh, cx + ew, eye_y + eh], fill=(255, 255, 255, 255))
        pr = ew * 0.62
        px, py = cx + ew * 0.22, eye_y - eh * 0.12
        d.ellipse([px - pr, py - pr * 1.12, px + pr, py + pr * 1.12], fill=INK + (255,))
        hr = pr * 0.36
        hx, hy = px - pr * 0.25, py - pr * 0.42
        d.ellipse([hx - hr, hy - hr, hx + hr, hy + hr], fill=(255, 255, 255, 255))

    if not simple:
        # 볼터치와 작은 웃는 입
        br = body * 0.065
        for sx in (-1, 1):
            cx = body / 2 + sx * body * 0.33; cy = eye_y + body * 0.17
            alpha = Image.new("L", (body, body), 0)
            ImageDraw.Draw(alpha).ellipse([cx - br * 1.3, cy - br * 0.8, cx + br * 1.3, cy + br * 0.8], fill=170)
            blush = Image.new("RGBA", (body, body), BLUSH + (0,))
            blush.putalpha(alpha.filter(ImageFilter.GaussianBlur(body * 0.012)))
            tile = Image.alpha_composite(tile, blush)
        d = ImageDraw.Draw(tile)
        mw = body * 0.07; my = eye_y + body * 0.17
        d.arc([body / 2 - mw, my - mw * 0.9, body / 2 + mw, my + mw * 0.9], start=20, end=160,
              fill=INK + (255,), width=max(2, int(body * 0.022)))

    mask = squircle_mask(body, radius)
    img.paste(tile, (int(pad), int(pad)), mask)
    if not simple and size >= 128:
        # 아래 그림자 살짝
        sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle([pad, pad + S * 0.02, S - pad, S - pad + S * 0.02], radius=radius, fill=(0, 0, 0, 70))
        sh = sh.filter(ImageFilter.GaussianBlur(S * 0.02))
        img = Image.alpha_composite(sh, img)
    return img.resize((size, size), Image.LANCZOS)


sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024]
icons = {n: draw(n, simple=n <= 32) for n in sizes}
for n, im in icons.items():
    im.save(os.path.join(OUT, f"icon-{n}.png"))
icons[256].save(os.path.join(OUT, "app.ico"), sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)],
                append_images=[icons[n] for n in (16, 20, 24, 32, 40, 48, 64, 128)])

# 미리보기: 밝은·어두운 배경 위 여러 크기
W, H = 1000, 600
prev = Image.new("RGB", (W, H), (245, 245, 245))
ImageDraw.Draw(prev).rectangle([0, H // 2, W, H], fill=(32, 32, 36))
x = 20
for n in (256, 128, 64, 48, 32, 24, 16):
    show = icons[n] if n >= 64 else icons[n].resize((n * 2, n * 2), Image.NEAREST)
    for row, y0 in ((0, 0), (1, H // 2)):
        prev.paste(show, (x, y0 + (H // 2 - show.height) // 2), show)
    x += show.width + 24
prev.save(os.path.join(OUT, "preview.png"))
print("ok", OUT)
