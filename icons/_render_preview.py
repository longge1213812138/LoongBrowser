#!/usr/bin/env python3
# 生成图标方案对比图：读取已渲染好的 512px PNG，拼成带小尺寸预览的对比图
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = r"C:\Users\91533\Desktop\LoongBrowser-master"
ICON_DIR = os.path.join(ROOT, "icons")
SRC = os.path.join(os.environ["TEMP"], "iconrender")
OUT = os.path.join(ICON_DIR, "preview.png")

CANVAS_W, CANVAS_H = 1200, 780
BG = (246, 248, 251)
CARD = (255, 255, 255)

schemes = [
    ("A-globe.png",   "A  地球 / 冷蓝底",     "最像浏览器，辨识成本最低"),
    ("B-orbit.png",   "B  行星环 / 连接世界",  "独特，任意尺寸都清晰"),
    ("C-page.png",    "C  书页折角 / 极简",   "强调阅读，偏文档类"),
    ("D-compass.png", "D  罗盘环 / 纯几何",   "小尺寸最清晰，无行业暗示"),
    ("E-letter.png",  "E  字母 L / 硬朗",     "字母标，好记"),
    ("F-tabs.png",    "F  标签+地球 / 多标签", "点出多标签特性"),
]


def font(size, bold=False):
    names = ["msyhbd.ttc", "segoeuib.ttf", "arialbd.ttf"] if bold else ["msyh.ttc", "segoeui.ttf", "arial.ttf"]
    for n in names:
        p = os.path.join(r"C:\Windows\Fonts", n)
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def main():
    canvas = Image.new("RGB", (CANVAS_W, CANVAS_H), BG)
    d = ImageDraw.Draw(canvas)
    f_title = font(32, True)
    f_sub = font(16)
    f_name = font(21, True)
    f_desc = font(15)

    d.text((56, 32), "LoongBrowser 应用图标方案", font=f_title, fill=(18, 30, 46))
    d.text((56, 76), "扁平化 / 圆角 112（约 22%）/ 512×512 基准 · SVG 源文件在 icons/ · 每张下方为 48px 与 32px 实际观感",
           font=f_sub, fill=(112, 124, 140))

    cols, rows = 3, 2
    card_w, card_h = 352, 286
    gap_x, gap_y = 24, 26
    x0, y0 = 56, 120

    for i, (fname, name, desc) in enumerate(schemes):
        c, r = i % cols, i // cols
        x = x0 + c * (card_w + gap_x)
        y = y0 + r * (card_h + gap_y)
        d.rounded_rectangle([x, y, x + card_w, y + card_h], radius=14, fill=CARD, outline=(226, 232, 240))

        src = os.path.join(SRC, fname)
        if not os.path.exists(src):
            continue
        im = Image.open(src).convert("RGBA")

        # 主图 176px
        big = im.resize((176, 176), Image.LANCZOS)
        canvas.paste(big, (x + 20, y + 18), big)

        # 小尺寸：48 / 32，都按实际像素渲染后放大 1 倍展示（不插值放大，保留真实锐利度）
        s48 = im.resize((48, 48), Image.LANCZOS)
        s32 = im.resize((32, 32), Image.LANCZOS)
        canvas.paste(s48, (x + 224, y + 34), s48)
        canvas.paste(s32, (x + 224, y + 104), s32)
        d.text((x + 224, y + 86), "48", font=f_desc, fill=(150, 160, 174))
        d.text((x + 224, y + 142), "32", font=f_desc, fill=(150, 160, 174))

        d.text((x + 20, y + 206), name, font=f_name, fill=(18, 30, 46))
        d.text((x + 20, y + 240), desc, font=f_desc, fill=(110, 122, 138))

    canvas.save(OUT, quality=95)
    print("saved:", OUT, os.path.getsize(OUT))


if __name__ == "__main__":
    main()
