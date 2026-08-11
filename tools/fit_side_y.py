# -*- coding: utf-8 -*-
"""手元视频(realplay)侧轨星爆拟合：裁出左右侧线条带（原生分辨率），
检测命中星爆（高亮度低色度大片）的屏幕 y，配对谱面侧轨 note（已知 P/W），
拟合 y_game = a + b*(P+W/2)。
游戏画面四角（见 video-geometry-analysis）：TL(473,196) TR(1531,194) BR(1535,999) BL(474,1000)。
用法: python fit_side_y.py [T0 T1]
"""
import json, subprocess, os, sys, shutil
import numpy as np
from PIL import Image
from collections import deque

# 视频 profile：realplay=手元（透视矫正）；charter=谱面确认（iPad 直出 1440x1080，恒等映射）
PROFILE = sys.argv[3] if len(sys.argv) > 3 else "realplay"
if PROFILE == "charter":
    V = "gameplay_videos/[Dynamix Universe⧸谱面确认] Tablear GIGA 15.0 Ω.mp4"
    OFF = 0.508
    BANDS = {'L': (100, 60, 200, 960), 'R': (1140, 60, 200, 960)}  # 侧线 x≈165/1275
else:
    V = "gameplay_videos/tablear_realplay.mp4"
    OFF = 3.811
    BANDS = {'L': (470, 150, 240, 850), 'R': (1320, 150, 240, 850)}  # 侧线 x≈575/1434
TMP = f"tmp_shots/sidefit_{PROFILE}"
TL, TR, BR, BL = (473, 196), (1531, 194), (1535, 999), (474, 1000)

def to_game_y(x, y):
    if PROFILE == "charter":
        return y
    l = TL[1] + (TR[1] - TL[1]) * ((x - TL[0]) / max(1e-9, TR[0] - TL[0]))
    b = BL[1] + (BR[1] - BL[1]) * ((x - BL[0]) / max(1e-9, BR[0] - BL[0]))
    return (y - l) / max(1e-9, b - l) * 1080.0

# 谱面侧轨命中时刻表
d = json.load(open('community/client/testdata/packs/tablear/chart_giga.json', encoding='utf-8'))
secs = d['TimeLine']['BakedBarSections']
def b2s(bar):
    s = secs[0]
    for x in secs:
        if x['BarTime'] <= bar: s = x
        else: break
    return s['Seconds'] + (bar - s['BarTime']) * 240.0 / s['BPM']

hits = {'L': [], 'R': []}
for tr, key in (('NotesLeft', 'L'), ('NotesRight', 'R')):
    for n in d[tr]:
        if n['Type'] in (1, 2, 3, 5, 6):
            hits[key].append((b2s(n['BarTime']), n['Position'] + n['Width'] / 2.0))
for k in hits: hits[k].sort()

T0 = float(sys.argv[1]) if len(sys.argv) > 1 else 2.0
T1 = float(sys.argv[2]) if len(sys.argv) > 2 else 190.0
for k in BANDS:
    os.makedirs(f"{TMP}/{k}", exist_ok=True)
    x, y, w, h = BANDS[k]
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", str(T0), "-to", str(T1),
                    "-i", V, "-vf", f"crop={w}:{h}:{x}:{y},fps=15",
                    f"{TMP}/{k}/f%05d.png"], check=True)

def bright_mask(path):
    a = np.asarray(Image.open(path).convert("RGB")).astype(np.int16)
    mx = a.max(axis=2); mn = a.min(axis=2); mean = a.mean(axis=2)
    return (mean > 175) & ((mx - mn) < 65)

def detect(mask):
    """亮白掩膜内的星爆 → (y_orig, 面积)。星爆=高亮度低色度大片。"""
    seen = np.zeros(mask.shape, bool)
    out = []
    ys, xs = np.nonzero(mask)
    for y0, x0 in zip(ys, xs):
        if seen[y0, x0]: continue
        q = deque([(y0, x0)]); seen[y0, x0] = True
        syy = sxx = n = 0
        miny = 10**9; maxy = -1; minx = 10**9; maxx = -1
        while q:
            y, x = q.popleft()
            syy += y; sxx += x; n += 1
            miny = min(miny, y); maxy = max(maxy, y); minx = min(minx, x); maxx = max(maxx, x)
            for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                ny, nx = y+dy, x+dx
                if 0 <= ny < mask.shape[0] and 0 <= nx < mask.shape[1] and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; q.append((ny, nx))
        w, h = maxx-minx, maxy-miny
        # 星爆：大片（≥400px）、不要过细长（排除白线/文字/高光条）
        if n >= 400 and w >= 25 and h >= 25 and w <= 4*h and h <= 4*w:
            out.append((syy/n, n))
    return out

samples = []
for side, (bx, by, bw, bh) in BANDS.items():
    frames = sorted(f for f in os.listdir(f"{TMP}/{side}") if f.startswith("f"))
    # 静态像素图：抽样帧中 >40% 时间都为亮白的像素（UI 文字/线体辉光），检测时排除
    sub = frames[::10]
    freq = np.zeros((bh, bw), np.float32)
    for fn in sub:
        freq += bright_mask(f"{TMP}/{side}/{fn}")
    static = freq > len(sub) * 0.4
    prev = np.zeros((bh, bw), bool)
    for i, fn in enumerate(frames, start=1):
        vt = T0 + (i - 1) / 15.0
        ct = vt - OFF
        cur = bright_mask(f"{TMP}/{side}/{fn}") & ~static
        new_only = cur & ~prev  # 星爆是突现的；持续亮斑（含漏网静态）剔除
        prev = cur
        for yrel, npx in detect(new_only):
            y_orig = yrel + by
            best, bd = None, 0.09
            for hs, cp in hits[side]:
                if abs(hs - ct) < bd: bd = abs(hs - ct); best = cp
            if best is None: continue
            x_line = (575 if side == 'L' else 1434) if PROFILE == "realplay" else (165 if side == 'L' else 1275)
            samples.append((best, to_game_y(x_line, y_orig), side, ct))

if not samples:
    print("no samples"); sys.exit(1)
cp = np.array([s[0] for s in samples]); gy = np.array([s[1] for s in samples])
A = np.vstack([np.ones_like(cp), cp]).T
coef, *_ = np.linalg.lstsq(A, gy, rcond=None)
pred = A @ coef
ss = 1 - ((gy - pred) ** 2).sum() / max(1e-9, ((gy - gy.mean()) ** 2).sum())
r = gy - pred
print(f"n={len(samples)}  y_game = {coef[0]:.1f} + {coef[1]:.1f} * (P+W/2)   r2={ss:.4f}")
print(f"resid std={r.std():.1f}px  p16/p50/p84 = {np.percentile(r,16):.0f}/{np.percentile(r,50):.0f}/{np.percentile(r,84):.0f}")
# 二次拟合（透视曲率检验）
A2 = np.vstack([np.ones_like(cp), cp, cp * cp]).T
c2, *_ = np.linalg.lstsq(A2, gy, rcond=None)
p2 = A2 @ c2
ss2 = 1 - ((gy - p2) ** 2).sum() / max(1e-9, ((gy - gy.mean()) ** 2).sum())
print(f"quad: y = {c2[0]:.1f} + {c2[1]:.1f}*cP + {c2[2]:.1f}*cP^2   r2={ss2:.4f}")
# 残差随时间（演出/漂移检验）：每 30s 一档的均值
ts = np.array([s[3] for s in samples])
for lo in range(0, 190, 30):
    m = (ts >= lo) & (ts < lo + 30)
    if m.sum() > 10:
        print(f"  t={lo:3d}-{lo+30:3d}s  n={m.sum():4d}  resid mean={r[m].mean():+6.1f} std={r[m].std():5.1f}")
for side in ('L', 'R'):
    m = np.array([s[2] == side for s in samples])
    if m.sum() > 4:
        c, *_ = np.linalg.lstsq(A[m], gy[m], rcond=None)
        print(f"  {side}: n={m.sum()}  a={c[0]:.1f} b={c[1]:.1f}")
