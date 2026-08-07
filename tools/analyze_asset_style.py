#!/usr/bin/env python3
"""原版素材风格分析（只提炼风格参数，不复制素材本身）。

用途：从逆向提取的贴图（封面等）中提取 配色板 / 亮度 / 饱和度 / 构图统计，
生成英文 prompt 片段，供 comfy_gen.py 文生图使用。
输出不含任何原图数据——只有统计数字与颜色名，可安全入库。

用法：
    python3 analyze_asset_style.py [--category Cover] [--limit 0] \
        [--container-json PATH] [--textures DIR] [--out PATH]

默认分析全部 Cover/Raw 封面，输出 community/tools/style_refs.json。
"""

from __future__ import annotations

import argparse
import colorsys
import json
import re
from collections import Counter
from pathlib import Path

from PIL import Image

# 简化命名色表（HSV）：(名称, h, s, v)  h∈[0,360)
NAMED_COLORS = [
    ("black", 0, 0.0, 0.10), ("dark gray", 0, 0.0, 0.30),
    ("gray", 0, 0.0, 0.55), ("light gray", 0, 0.0, 0.80), ("white", 0, 0.0, 0.95),
    ("red", 0, 0.8, 0.8), ("crimson", 348, 0.85, 0.75),
    ("orange", 25, 0.9, 0.9), ("amber", 40, 0.85, 0.9),
    ("yellow", 55, 0.85, 0.9), ("lime", 80, 0.8, 0.85),
    ("green", 120, 0.7, 0.7), ("teal", 175, 0.7, 0.7),
    ("cyan", 190, 0.8, 0.85), ("sky blue", 205, 0.7, 0.9),
    ("blue", 225, 0.8, 0.8), ("navy", 230, 0.8, 0.4),
    ("purple", 275, 0.7, 0.7), ("magenta", 300, 0.8, 0.8),
    ("pink", 330, 0.6, 0.9), ("brown", 20, 0.6, 0.45),
]


def color_name(r: float, g: float, b: float) -> str:
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    h *= 360
    best, best_d = "gray", 1e9
    for name, nh, ns, nv in NAMED_COLORS:
        dh = min(abs(h - nh), 360 - abs(h - nh)) / 180.0
        d = dh * (0.3 + s) + abs(s - ns) * 0.6 + abs(v - nv) * 0.8
        if d < best_d:
            best, best_d = name, d
    return best


def analyze_image(path: Path, n_colors: int = 6) -> dict:
    im = Image.open(path).convert("RGB")
    w, h = im.size
    small = im.resize((128, max(1, int(128 * h / w))))
    q = small.quantize(colors=n_colors, method=Image.Quantize.MEDIANCUT)
    pal = q.getpalette()[: n_colors * 3]
    counts = Counter(q.getdata())
    total = sum(counts.values())

    colors = []
    for idx, cnt in counts.most_common():
        r, g, b = (pal[idx * 3 + i] / 255.0 for i in range(3))
        colors.append({
            "hex": "#{:02x}{:02x}{:02x}".format(*(int(c * 255) for c in (r, g, b))),
            "name": color_name(r, g, b),
            "share": round(cnt / total, 3),
        })

    px = list(small.getdata())
    n = len(px)
    lum = sum(0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2] for p in px) / n / 255
    sat = sum(colorsys.rgb_to_hsv(*(c / 255 for c in p))[1] for p in px) / n
    return {
        "file": path.name,
        "size": [w, h],
        "palette": colors,
        "mean_luminance": round(lum, 3),
        "mean_saturation": round(sat, 3),
    }


def prompt_fragment(entry: dict) -> str:
    """由统计生成 prompt 片段（风格描述，不含原图内容）。"""
    pal = [c for c in entry["palette"] if c["share"] >= 0.08][:4]
    colors = ", ".join(f"{c['name']} ({c['share']:.0%})" for c in pal)
    mood = "dark" if entry["mean_luminance"] < 0.35 else \
           "bright" if entry["mean_luminance"] > 0.65 else "mid-tone"
    vivid = "highly saturated" if entry["mean_saturation"] > 0.5 else \
            "muted" if entry["mean_saturation"] < 0.25 else "moderately saturated"
    return f"{mood}, {vivid} color palette dominated by {colors}"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--category", default="Cover")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--container-json",
                    default=str(Path(__file__).parents[2] / "_rev" / "extracted" / "container_paths.json"))
    ap.add_argument("--textures",
                    default=str(Path(__file__).parents[2] / "_rev" / "extracted" / "Textures"))
    ap.add_argument("--out", default=str(Path(__file__).parent / "style_refs.json"))
    args = ap.parse_args()

    containers = json.loads(Path(args.container_json).read_text(encoding="utf-8"))
    tex_dir = Path(args.textures)
    pat = re.compile(rf"{re.escape(args.category)}/.*\.png$", re.IGNORECASE)

    # container path → bundle hash 前 12 位 → Textures 文件名前缀
    targets: dict[str, list[str]] = {}
    for cpath, bundle in containers.items():
        if pat.search(cpath):
            targets.setdefault(bundle[:12], []).append(cpath)

    entries = []
    for f in sorted(tex_dir.glob("*.png")):
        prefix = f.name.split("_")[0]
        if prefix not in targets:
            continue
        try:
            e = analyze_image(f)
        except Exception as ex:
            print(f"跳过 {f.name}: {ex}")
            continue
        e["source_paths"] = targets[prefix]
        entries.append(e)
        if args.limit and len(entries) >= args.limit:
            break

    # 聚合：全部封面的颜色名频次（加权 share）
    agg = Counter()
    for e in entries:
        for c in e["palette"]:
            agg[c["name"]] += c["share"]
    overall = [{"name": n, "freq": round(f / max(1, len(entries)), 3)}
               for n, f in agg.most_common(12)]

    for e in entries:
        e["prompt_fragment"] = prompt_fragment(e)

    result = {
        "category": args.category,
        "count": len(entries),
        "overall_palette": overall,
        "overall_prompt": (
            "rhythm game cover art style: "
            + (", ".join(f"{o['name']}" for o in overall[:6]))
        ),
        "entries": entries,
    }
    out = Path(args.out)
    out.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"分析了 {len(entries)} 张 {args.category} 贴图 -> {out}")
    print("整体配色:", ", ".join(o["name"] for o in overall[:8]))


if __name__ == "__main__":
    main()
