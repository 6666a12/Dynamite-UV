# -*- coding: utf-8 -*-
"""Track cyan note blobs across hi frames, log centroid when crossing the judge bar y=665."""
import os
import numpy as np
from PIL import Image
from collections import deque

HI = "comfyui_dl/hi"
def vtime(k): return 6.5 + (k - 1) / 12.0  # hi frames: 12fps from 6.5s

frames = sorted(os.listdir(HI))

def detect(img):
    a = np.asarray(img.resize((585, 270))).astype(np.int16)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mask = (g > 180) & (b > 170) & (r < 120) & (g - r > 80) & (b - r > 60)
    mask[:20, :] = False; mask[250:, :] = False
    mask[:, :100] = False; mask[:, 487:] = False
    seen = np.zeros(mask.shape, dtype=bool)
    out = []
    ys, xs = np.nonzero(mask)
    for y0, x0 in zip(ys, xs):
        if seen[y0, x0]:
            continue
        q = deque([(y0, x0)]); seen[y0, x0] = True
        syy = sxx = n = 0
        while q:
            y, x = q.popleft()
            syy += y; sxx += x; n += 1
            for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                ny, nx = y+dy, x+dx
                if 0 <= ny < 270 and 0 <= nx < 585 and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True
                    q.append((ny, nx))
        if n >= 6:  # ~96 px full-res
            out.append((sxx / n * 4, syy / n * 4, n * 16))
    return out

for i, fn in enumerate(frames, start=1):
    img = Image.open(os.path.join(HI, fn)).convert("RGB")
    blobs = detect(img)
    print(f"f{i:04d} t={vtime(i):6.2f}s: " + "  ".join(f"({cx:6.1f},{cy:6.1f},a{ar})" for cx, cy, ar in blobs))
