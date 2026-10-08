#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Build a multi-resolution .ico for clipboard-tool with super-ellipse (squircle)
alpha crop — same approach as WindowTinter (WindowTinter-proto-shadow/DEV.md
section 17 "超椭圆图标").

Source : D:/download/生成液态玻璃风格icon.png  (2048x2048, RGB, fully opaque
         square canvas — the liquid-glass art is centered on a flat pink
         background, so plain resize -> sharp square corners at every size).
Output : clipboard-tool/clipboard-exe/Assets/app.ico
Sizes  : 16/24/32/48/64/128/256.

Crop formula:
    |x/a|^n + |y/b|^n <= 1   (centered, x,y in [-1,1], a=b=1-inset)
    n=4 (squircle, mild rounding)  for 48/64/128/256
    n=8 (near-circle, more rounding) for 16/24/32  (cleaner silhouette at
        small sizes where subtle corners look noisy)

Why two exponents? At 16x16 / 24x24 a squircle silhouette has only 1 px of
rounding on each corner — visually indistinguishable from a square, defeating
the purpose. A near-circle exponent pushes more pixels into the corner
arc, so even at 16x16 the icon reads as "rounded".

Result is RGBA per frame; Pillow's ICO writer will encode alpha and Windows
will use it for Begin Menu / taskbar / tray surfaces.
"""
from __future__ import annotations

import numpy as np
from pathlib import Path
from PIL import Image

SRC = Path(r"D:\download\生成液态玻璃风格icon.png")
DST = Path(r"D:\workbuddy\2026-09-04-13-26-37\clipboard-tool\clipboard-exe\Assets\app.ico")

# WindowTinter-aligned size set; the 256 frame uses PNG-in-ICO encoding (Vista+).
SIZES: list[int] = [16, 24, 32, 48, 64, 128, 256]
# Threshold between "small" (n=8) and "large" (n=4). 32 and below get n=8.
SMALL_THRESHOLD = 32

# Inset shrinks the mask's bounding box to leave a small transparent margin
# inside the canvas. Without inset, the squircle touches the very edge; with
# it, the shape "breathes" and looks proportional. 4 % matches what the
# modern Windows 11 icon set uses (radius ~ 22% of side after rounding).
INSET_PCT = 0.04


def superellipse_mask(side: int, n: float, inset: float) -> np.ndarray:
    """Return a (side, side) bool mask: True inside the superellipse."""
    t = np.linspace(-1.0, 1.0, side, dtype=np.float32)
    xv, yv = np.meshgrid(t, t)
    s = 1.0 - inset
    return (np.abs(xv / s) ** n + np.abs(yv / s) ** n) <= 1.0


def render_frame(rgb_src: np.ndarray, side: int) -> Image.Image:
    """Downscale RGB source to (side, side) with a superellipse alpha
    applied AFTER scaling (so the mask is generated at the target pixel
    grid — keeps edges crisp at every size)."""
    n = 8 if side <= SMALL_THRESHOLD else 4
    # 1) high-quality downscale (preserve the glass highlights)
    rgb = Image.fromarray(rgb_src, "RGB").resize((side, side), Image.LANCZOS)
    arr = np.asarray(rgb, dtype=np.uint8)            # H,W,3
    # 2) build mask at this target size, feather 1 px to avoid aliased
    #    fringe where the superellipse edge sits between pixels.
    m = superellipse_mask(side, n, INSET_PCT)
    m8 = m.astype(np.uint8) * 255                    # 0 / 255
    # 1-px 3x3 box blur gives the boundary a 1-px antialiased ring; cheap
    # and effective for icon-sized frames.
    pad = np.pad(m8, 1, mode="edge")
    blurred = (
        pad[0:-2, 0:-2].astype(np.uint16)
        + pad[0:-2, 1:-1].astype(np.uint16)
        + pad[0:-2, 2:].astype(np.uint16)
        + pad[1:-1, 0:-2].astype(np.uint16)
        + pad[1:-1, 1:-1].astype(np.uint16)
        + pad[1:-1, 2:].astype(np.uint16)
        + pad[2:, 0:-2].astype(np.uint16)
        + pad[2:, 1:-1].astype(np.uint16)
        + pad[2:, 2:].astype(np.uint16)
    ) // 9
    alpha = blurred.astype(np.uint8)
    rgba = np.dstack([arr, alpha])
    return Image.fromarray(rgba, "RGBA")


def main() -> None:
    if not SRC.exists():
        raise SystemExit(f"source PNG not found: {SRC}")
    img = Image.open(SRC)
    if img.size != (2048, 2048):
        raise SystemExit(f"unexpected source size {img.size}; expected 2048x2048")
    rgb_src = np.asarray(img.convert("RGB"))
    DST.parent.mkdir(parents=True, exist_ok=True)

    frames: list[Image.Image] = [render_frame(rgb_src, s) for s in SIZES]
    DST.unlink(missing_ok=True)
    # Primary = 256; the rest are appended at the requested sizes. Pillow
    # matches `sizes` to the frames in `append_images` order.
    frames[-1].save(
        DST,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        append_images=frames[:-1],
    )
    print(f"wrote {DST} ({DST.stat().st_size} bytes), sizes={SIZES}, "
          f"crop=n<=32->8 else 4, inset={INSET_PCT*100:.0f}%")

    # Sanity reopen + per-frame alpha check. Pillow's IcoImageFile.ico.getimage
    # returns the closest available frame for the requested size, so we ask for
    # the exact size on disk and re-build the matching mask at that size.
    chk = Image.open(DST)
    print(f"  reopened: size={chk.size}, format={chk.format}, "
          f"sizes={sorted(chk.ico.sizes())}")
    for s in SIZES:
        frm = chk.ico.getimage((s, s))
        a = np.asarray(frm)[:, :, 3]
        n = 8 if s <= SMALL_THRESHOLD else 4
        m = superellipse_mask(s, n, INSET_PCT)
        missing = int((m & (a < 8)).sum())     # mask says "in" but alpha is transparent
        overcrop = int(((~m) & (a > 200)).sum())  # mask says "out" but alpha is opaque
        print(f"  {s:>3}: mode={frm.mode}  inside-but-transparent={missing}  "
              f"outside-but-opaque={overcrop}")


if __name__ == "__main__":
    main()
