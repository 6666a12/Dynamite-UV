# -*- coding: utf-8 -*-
"""追踪右侧绿色竖条，外推命中右线(x≈1270)时刻，与谱面对齐。"""
import subprocess, os
import numpy as np
from PIL import Image
from collections import deque

V = "gameplay_videos/[Dynamix Universe⧸谱面确认] Tablear GIGA 15.0 Ω.mp4"
TMP = "tmp_shots/green"
os.makedirs(TMP, exist_ok=True)
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", "6.3", "-to", "8.6",
                "-i", V, "-vf", "fps=15", f"{TMP}/g%04d.png"], check=True)

def detect(path):
    a = np.asarray(Image.open(path).convert("RGB").resize((720, 540))).astype(np.int16)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mask = (g > 120) & (g - r > 70) & (g - b > 30)
    mask[:150, :] = False; mask[440:, :] = False
    seen = np.zeros(mask.shape, bool)
    out = []
    ys, xs = np.nonzero(mask)
    for y0, x0 in zip(ys, xs):
        if seen[y0, x0]: continue
        q = deque([(y0, x0)]); seen[y0, x0] = True
        syy = sxx = n = 0
        minx = 10**9; maxx = -1; miny = 10**9; maxy = -1
        while q:
            y, x = q.popleft()
            syy += y; sxx += x; n += 1
            minx = min(minx, x); maxx = max(maxx, x); miny = min(miny, y); maxy = max(maxy, y)
            for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                ny, nx = y+dy, x+dx
                if 0 <= ny < 540 and 0 <= nx < 720 and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; q.append((ny, nx))
        w, h = (maxx-minx)*2, (maxy-miny)*2
        if n >= 25 and h > w * 2 and h >= 60:   # 竖条
            out.append((sxx/n*2, syy/n*2, w, h))
    return out

frames = sorted(f for f in os.listdir(TMP) if f.startswith("g"))
per_frame = []
for i, fn in enumerate(frames, start=1):
    vt = 6.3 + (i - 1) / 15.0
    per_frame.append((vt, detect(os.path.join(TMP, fn))))

tracklets = []; active = []
for vt, blobs in per_frame:
    for cx, cy, w, h in blobs:
        best, bd = None, 60.0
        for tr in active:
            lvt, lcx = tr[-1][0], tr[-1][1]
            if vt - lvt > 0.14: continue
            if cx < lcx - 15: continue      # 只向右
            d = abs(cx - lcx - 55)          # 预期每帧右移 ~850/15≈57px
            if d < bd: best, bd = tr, d
        if best is None:
            tr = [(vt, cx, cy)]; tracklets.append(tr); active.append(tr)
        else:
            best.append((vt, cx, cy))
    active = [tr for tr in active if vt - tr[-1][0] <= 0.14]

for tr in tracklets:
    if len(tr) < 3: continue
    vt = np.array([d[0] for d in tr]); cx = np.array([d[1] for d in tr]); cy = np.array([d[2] for d in tr])
    m, c = np.polyfit(vt, cx, 1)
    if m < 300: continue
    hit = (1270.0 - c) / m
    print(f"track n={len(tr)} x {cx[0]:.0f}->{cx[-1]:.0f} y~{cy.mean():.0f} speed={m:.0f}px/s -> 命中右线 vt={hit:.3f} (谱面 {hit-0.508:.3f})")
