#!/usr/bin/env python3
"""把谱面 JSON + 音频 + 封面打成一个 DUX-Community 谱面包（目录形式）。

谱面包结构（client 运行时扫描 user://charts/ 与 res://testdata/packs/）：

    <pack_id>/
        meta.json           曲名/曲师/谱师/音频/封面/难度表
        chart_<diff>.json   DynamixChartLoader 可读的谱面（diff ∈ casual/normal/hard/mega/giga/tech）
        <audio>             音频文件（保持原扩展名）
        <cover>             封面图（保持原扩展名，可选）

用法示例：
    python pack_chart.py --id tablear --title "Tablear" --artist "-" --charter "dev" \
        --diff giga --level 15 --chart chart_tablear.json --audio song_tablear.wav \
        --cover ../client/assets/covers/cover_style_navy_01.png \
        --out ../client/testdata/packs

同一个 pack_id 重复执行（不同 --diff）会把难度合并进同一个 meta.json。
"""
import argparse
import json
import shutil
import sys
from pathlib import Path

DIFFS = ["casual", "normal", "hard", "mega", "giga", "tech"]


def main() -> int:
    ap = argparse.ArgumentParser(description="DUX-Community 谱面包打包工具")
    ap.add_argument("--id", required=True, help="谱面包 ID（目录名，建议英文小写+下划线）")
    ap.add_argument("--title", required=True, help="曲名")
    ap.add_argument("--artist", default="-", help="曲师")
    ap.add_argument("--charter", default="-", help="谱师")
    ap.add_argument("--diff", required=True, choices=DIFFS, help="本次打包的难度")
    ap.add_argument("--level", type=int, required=True, help="难度等级数字")
    ap.add_argument("--chart", required=True, type=Path, help="谱面 JSON 路径")
    ap.add_argument("--audio", required=True, type=Path, help="音频文件路径")
    ap.add_argument("--cover", type=Path, default=None, help="封面图路径（可选）")
    ap.add_argument("--out", required=True, type=Path, help="谱面包输出根目录")
    args = ap.parse_args()

    for p, what in [(args.chart, "谱面"), (args.audio, "音频")]:
        if not p.is_file():
            print(f"错误：{what}文件不存在: {p}", file=sys.stderr)
            return 1
    if args.cover is not None and not args.cover.is_file():
        print(f"错误：封面文件不存在: {args.cover}", file=sys.stderr)
        return 1

    pack_dir = args.out / args.id
    pack_dir.mkdir(parents=True, exist_ok=True)

    meta_path = pack_dir / "meta.json"
    if meta_path.is_file():
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
    else:
        meta = {"id": args.id, "title": args.title, "artist": args.artist,
                "charter": args.charter, "audio": "", "cover": "", "charts": []}

    # 曲目级字段以最后一次执行为准
    meta["title"] = args.title
    meta["artist"] = args.artist
    meta["charter"] = args.charter

    chart_name = f"chart_{args.diff}.json"
    shutil.copy2(args.chart, pack_dir / chart_name)
    audio_name = args.audio.name
    shutil.copy2(args.audio, pack_dir / audio_name)
    meta["audio"] = audio_name
    if args.cover is not None:
        cover_name = args.cover.name
        shutil.copy2(args.cover, pack_dir / cover_name)
        meta["cover"] = cover_name

    charts = [c for c in meta["charts"] if c.get("diff") != args.diff]
    charts.append({"diff": args.diff, "level": args.level, "file": chart_name})
    order = {d: i for i, d in enumerate(DIFFS)}
    charts.sort(key=lambda c: order.get(c["diff"], 99))
    meta["charts"] = charts

    meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"OK: {pack_dir}  [{args.diff} Lv{args.level}] 共 {len(charts)} 个难度")
    return 0


if __name__ == "__main__":
    sys.exit(main())
