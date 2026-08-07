#!/usr/bin/env python3
"""简单并行分段下载器（对单连接限速的 CDN 有效）。

用法：
    python3 pardl.py URL OUT [--size N] [--workers 12] [--chunk-mb 32]

分段临时文件放在 OUT.parts/，全部完成后合并为 OUT 并删除临时目录。
支持断点续传：已完成的段会跳过。只依赖标准库。
"""

from __future__ import annotations

import argparse
import math
import sys
import threading
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

UA = {"User-Agent": "pardl/1.0"}


def fetch_size(url: str) -> int:
    req = urllib.request.Request(url, method="HEAD", headers=UA)
    with urllib.request.urlopen(req, timeout=30) as r:
        return int(r.headers["Content-Length"])


def fetch_range(url: str, start: int, end: int, dest: Path,
                progress: list[int], idx: int) -> None:
    """下载 [start, end] 到 dest；已存在且大小正确则跳过。失败重试 5 次。"""
    want = end - start + 1
    if dest.exists() and dest.stat().st_size == want:
        progress[idx] = want
        return
    for attempt in range(5):
        try:
            req = urllib.request.Request(
                url, headers={**UA, "Range": f"bytes={start}-{end}"})
            with urllib.request.urlopen(req, timeout=60) as r, \
                    open(dest, "wb") as f:
                got = 0
                while True:
                    buf = r.read(1 << 16)
                    if not buf:
                        break
                    f.write(buf)
                    got += len(buf)
                    progress[idx] = got
            if dest.stat().st_size == want:
                return
        except Exception as e:
            print(f"[seg {idx}] 第 {attempt + 1} 次失败: {e}", flush=True)
            time.sleep(2 * (attempt + 1))
    raise RuntimeError(f"分段 {idx} 多次失败")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("url")
    ap.add_argument("out")
    ap.add_argument("--workers", type=int, default=12)
    ap.add_argument("--chunk-mb", type=int, default=32)
    args = ap.parse_args()

    out = Path(args.out)
    total = fetch_size(args.url)
    chunk = args.chunk_mb << 20
    nseg = math.ceil(total / chunk)
    parts = out.parent / (out.name + ".parts")
    parts.mkdir(parents=True, exist_ok=True)
    print(f"总大小 {total / 2**20:.0f} MB，{nseg} 段 × {args.chunk_mb} MB，"
          f"{args.workers} 线程", flush=True)

    progress = [0] * nseg
    stop = threading.Event()

    def report() -> None:
        t0 = time.time()
        while not stop.is_set():
            time.sleep(5)
            got = sum(progress)
            rate = got / max(1e-9, time.time() - t0)
            print(f"\r{got / 2**20:.0f}/{total / 2**20:.0f} MB "
                  f"({got * 100 // total}%) 平均 {rate / 2**20:.1f} MB/s   ",
                  end="", flush=True)

    t = threading.Thread(target=report, daemon=True)
    t.start()

    with ThreadPoolExecutor(max_workers=args.workers) as ex:
        futs = []
        for i in range(nseg):
            start = i * chunk
            end = min(total - 1, start + chunk - 1)
            futs.append(ex.submit(fetch_range, args.url, start, end,
                                  parts / f"{i:05d}.part", progress, i))
        for f in futs:
            f.result()

    stop.set()
    print(f"\n合并 {nseg} 段 -> {out}", flush=True)
    with open(out, "wb") as w:
        for i in range(nseg):
            w.write((parts / f"{i:05d}.part").read_bytes())
    if out.stat().st_size != total:
        sys.exit(f"合并后大小不符: {out.stat().st_size} != {total}")
    for p in parts.glob("*.part"):
        p.unlink()
    parts.rmdir()
    print("完成", flush=True)


if __name__ == "__main__":
    main()
