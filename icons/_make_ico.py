#!/usr/bin/env python3
"""从 1024px 主渲染图生成多尺寸 ICO。

小尺寸不是简单缩小：
- 16px：去掉圆环缺口（缺口在这个尺寸只会变成噪点），描边加粗，指针放大占满内圈
- 20/24px：保留缺口但加大指针，描边适度加粗
- 32px 及以上：直接用主渲染图降采样（缺口与指针都清晰）
"""
import os
from PIL import Image, ImageDraw

ROOT = r"C:\Users\91533\Desktop\LoongBrowser-master"
SRC = os.path.join(os.environ["TEMP"], "iconsmall", "d1024.png")
OUT_ICO = os.path.join(ROOT, "icons", "LoongBrowser.ico")
OUT_PNG = os.path.join(ROOT, "icons", "LoongBrowser-256.png")

# ICO 内嵌尺寸（Windows 常用档位）
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

BLUE = (30, 99, 168, 255)
WHITE = (255, 255, 255, 255)


def draw_small(size, *, keep_gap, ring_scale, needle_scale):
    """按 512 基准等比重绘，参数用于小尺寸补偿。

    D-compass.svg 的几何（512 基准）：
      底板圆角 112 · 圆环 r=128 描边 40 · 缺口白弧 56 · 指针菱形 88×176 · 中心白点 r=22
    """
    S = 512.0
    k = size / S
    ss = 8                      # 超采样倍数
    W = size * ss
    im = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    def sc(v):
        return v * k * ss

    d.rounded_rectangle([0, 0, W - 1, W - 1], radius=sc(112), fill=WHITE)

    cx = cy = W / 2.0
    r = sc(128)
    ring_w = sc(40) * ring_scale

    # 圆环
    d.ellipse([cx - r - ring_w / 2, cy - r - ring_w / 2,
               cx + r + ring_w / 2, cy + r + ring_w / 2],
              outline=BLUE, width=int(round(ring_w)))

    # 缺口：只用白色盖掉一段（16/20px 不留缺口，避免读成字母 C）
    if keep_gap:
        gap_w = sc(56)
        # 对应 SVG 的 M256 84 A172 172 0 0 1 384 128：从正上方到右上 45°，
        # PIL 角度以 3 点钟为 0、顺时针为正，故为 -90 → -45。
        d.arc([cx - r - gap_w / 2, cy - r - gap_w / 2,
               cx + r + gap_w / 2, cy + r + gap_w / 2],
              start=-90, end=-45, fill=WHITE, width=int(round(gap_w)))

    # 指针：菱形，上下顶点 + 左右腰点
    hw = sc(44) * needle_scale      # 半宽
    hh = sc(88) * needle_scale      # 半高
    d.polygon([(cx, cy - hh), (cx + hw, cy), (cx, cy + hh), (cx - hw, cy)], fill=BLUE)

    # 中心白点
    cr = sc(22) * (1.0 if size >= 24 else 1.15)
    d.ellipse([cx - cr, cy - cr, cx + cr, cy + cr], fill=WHITE)

    return im.resize((size, size), Image.LANCZOS)


SMALL = {
    # 24px 以下一律不留缺口：这个尺寸里缺口会被描边吞噬、读成字母 C。
    #       保留缺口  描边倍率  指针倍率
    16: dict(keep_gap=False, ring_scale=1.40, needle_scale=1.28),
    20: dict(keep_gap=False, ring_scale=1.26, needle_scale=1.16),
    24: dict(keep_gap=False, ring_scale=1.14, needle_scale=1.06),
}


def main():
    src = Image.open(SRC).convert("RGBA")
    bbox = src.getbbox()
    if bbox:
        src = src.crop(bbox)
    src = src.resize((1024, 1024), Image.LANCZOS)

    frames = []
    for s in SIZES:
        if s in SMALL:
            im = draw_small(s, **SMALL[s])
        else:
            im = src.resize((s, s), Image.LANCZOS)
        frames.append(im)

    frames[-1].save(OUT_ICO, format="ICO",
                    sizes=[(f.width, f.height) for f in frames])
    frames[-1].save(OUT_PNG, format="PNG")

    print("ICO:", OUT_ICO, os.path.getsize(OUT_ICO), "bytes")
    print("PNG:", OUT_PNG, os.path.getsize(OUT_PNG), "bytes")
    print("sizes:", ", ".join(str(f.width) for f in frames))

    # 预览：把小尺寸按像素放大，检查真实观感
    prev = os.path.join(ROOT, "icons", "_ico_preview.png")
    pad, cell = 14, 264
    picks = [16, 20, 24, 32]
    canvas = Image.new("RGBA", ((cell + pad) * len(picks) + pad, cell + pad * 2), (246, 248, 251, 255))
    for i, s in enumerate(picks):
        im = next((f for f in frames if f.width == s), None)
        if im is None:
            continue
        big = im.resize((cell, cell), Image.NEAREST)
        canvas.alpha_composite(big, (pad + i * (cell + pad), pad))
    canvas.convert("RGB").save(prev)
    print("preview:", prev)


if __name__ == "__main__":
    main()
