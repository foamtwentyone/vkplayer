"""Генерирует иконки расширения и приложения (запускать из корня репозитория: python scripts/make_icons.py)."""
from PIL import Image, ImageDraw

def draw(size: int) -> Image.Image:
    s = size * 4  # рисуем с запасом и уменьшаем — сглаживание
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, s - 1, s - 1), radius=s * 0.24, fill=(0, 119, 255, 255))
    w = (255, 255, 255, 255)
    # нота: две головки + штили + перемычка
    r = s * 0.11
    for cx, cy in ((s * 0.34, s * 0.70), (s * 0.66, s * 0.62)):
        d.ellipse((cx - r * 1.15, cy - r, cx + r * 1.15, cy + r), fill=w)
    st = s * 0.06
    d.rectangle((s * 0.34 + r * 1.15 - st, s * 0.26, s * 0.34 + r * 1.15, s * 0.70), fill=w)
    d.rectangle((s * 0.66 + r * 1.15 - st, s * 0.18, s * 0.66 + r * 1.15, s * 0.62), fill=w)
    d.polygon([
        (s * 0.34 + r * 1.15 - st, s * 0.26), (s * 0.66 + r * 1.15, s * 0.18),
        (s * 0.66 + r * 1.15, s * 0.30), (s * 0.34 + r * 1.15 - st, s * 0.38)], fill=w)
    return img.resize((size, size), Image.LANCZOS)

for n in (16, 32, 48, 128):
    draw(n).save(f"extension/icons/icon{n}.png")
big = draw(256)
big.save("assets/app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("ok")
