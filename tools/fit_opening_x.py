# -*- coding: utf-8 -*-
"""追踪谱面确认视频开头段(1.5~7s)青色 tap 轨迹，按 x 恒定性分组，
外推命中时刻匹配谱面 note，拟合 x(P) 映射。"""
import json, subprocess, os
import numpy as np
from PIL import Image
from collections import deque

V = "gameplay_videos/[Dynamix Universe⧸谱面确认] Tablear GIGA 15.0 Ω.mp4"
OFF = 0.508
LINE_CY = 850.0      # 命中时 blob 中心 y（线体 858-864，条高约 20）
TMP = "tmp_shots/fit"
os.makedirs(TMP, exist_ok=True)

# 1) 抽帧 15fps
subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", "1.5", "-to", "7.2",
                "-i", V, "-vf", "fps=15", f"{TMP}/f%04d.png"], check=True)

def detect(path):
    a = np.asarray(Image.open(path).convert("RGB").resize((720, 540))).astype(np.int16)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mask = (g > 170) & (b > 160) & (r < 130) & (g - r > 70) & (b - r > 50)
    mask[:150, :] = False
    mask[425:, :] = False           # >850 排除线体
    mask[:, :80] = False; mask[:, 640:] = False
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
        if n >= 30 and 120 <= w <= 320 and h <= 80:   # tap 条 ~196x20 @1440
            out.append((sxx/n*2, syy/n*2, w))
    return out

dets = []  # (vt, cx, cy)
frames = sorted(f for f in os.listdir(TMP) if f.startswith("f"))
per_frame = []
for i, fn in enumerate(frames, start=1):
    vt = 1.5 + (i - 1) / 15.0
    per_frame.append((vt, detect(os.path.join(TMP, fn))))

# 2) 帧间链接成轨迹：cx 接近(±35)、cy 递增、帧差≤2
tracklets = []   # each: list of (vt, cx, cy)
active = []
for vt, blobs in per_frame:
    used = set()
    for cx, cy, w in blobs:
        best, bd = None, 35.0
        for tr in active:
            lvt, lcx, lcy = tr[-1]
            if vt - lvt > 0.14: continue
            if cy < lcy - 10: continue
            d = abs(cx - lcx)
            if d < bd: best, bd = tr, d
        if best is None:
            tr = [(vt, cx, cy)]
            tracklets.append(tr); active.append(tr)
        else:
            best.append((vt, cx, cy)); used.add(id(best))
    active = [tr for tr in active if vt - tr[-1][0] <= 0.14]

# 3) 轨迹>=3帧、累计下落>100px，外推命中匹配谱面
chart = json.load(open("community/client/testdata/packs/tablear/chart_giga.json", encoding="utf-8"))
notes = sorted([(n["Baked_Second"], n["Position"], n["Width"]) for n in chart["NotesCenter"]
                if n["Baked_Second"] < 6.5 and n["Type"] == 1])
hits = np.array([t + OFF for t, _, _ in notes])

rows = []
used_notes = set()
for tr in tracklets:
    if len(tr) < 3: continue
    vt = np.array([d[0] for d in tr]); cx = np.array([d[1] for d in tr]); cy = np.array([d[2] for d in tr])
    if cx.std() > 15 or cy[-1] - cy[0] < 100: continue
    m, c = np.polyfit(vt, cy, 1)
    if m < 200: continue
    hit_vt = (LINE_CY - c) / m
    j = int(np.argmin(np.abs(hits - hit_vt)))
    if abs(hits[j] - hit_vt) > 0.09 or j in used_notes: continue
    used_notes.add(j)
    t, P, W = notes[j]
    rows.append((P, W, cx.mean(), len(tr)))
    print(f"P={P:5.2f} W={W:.1f} -> cx={cx.mean():7.1f} (n={len(tr)}, hit_vt={hit_vt:.3f} vs chart {hits[j]:.3f})")

Ps = np.array([r[0] for r in rows]); Xs = np.array([r[2] for r in rows])
for name, Z in (("P", Ps), ("P+W/2", Ps + np.array([r[1] for r in rows])/2)):
    A = np.vstack([np.ones_like(Z), Z]).T
    (a, bb), *_ = np.linalg.lstsq(A, Xs, rcond=None)
    pred = a + bb * Z
    ss = 1 - ((Xs - pred) ** 2).sum() / max(((Xs - Xs.mean()) ** 2).sum(), 1e-9)
    print(f"fit cx = {a:.1f} + {bb:.1f}*({name})  r2={ss:.4f}  maxres={abs(Xs-pred).max():.1f}  n={len(rows)}")
