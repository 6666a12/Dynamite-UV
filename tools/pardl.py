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
import hashlib
import json
import re
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
                progress: list[int], idx: int, validator: str | None = None) -> None:
    """Download an exact range. Resume only with matching source, validator and content hash."""
    want = end - start + 1
    receipt = dest.with_suffix(dest.suffix + ".json")
    identity = {"source": hashlib.sha256(url.encode()).hexdigest(), "start": start,
                "end": end, "validator": validator}
    try:
        saved = json.loads(receipt.read_text(encoding="utf-8"))
        if validator and saved.get("identity") == identity and dest.stat().st_size == want and \
                saved.get("sha256") == hashlib.sha256(dest.read_bytes()).hexdigest():
            progress[idx] = want
            return
    except (OSError, ValueError, AttributeError):
        pass
    for attempt in range(5):
        try:
            headers = {**UA, "Range": f"bytes={start}-{end}"}
            if validator:
                headers["If-Range"] = validator
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req, timeout=60) as r:
                content_range = re.fullmatch(r"bytes (\d+)-(\d+)/(\d+)", r.headers.get("Content-Range", ""))
                if r.status != 206 or not content_range or tuple(map(int, content_range.groups()[:2])) != (start, end) \
                        or int(content_range.group(3)) <= end:
                    raise ValueError("server did not return the requested byte range")
                got = 0
                with open(dest, "wb") as f:
                    while buf := r.read(min(1 << 16, want - got + 1)):
                        got += len(buf)
                        if got > want:
                            raise ValueError("range response exceeds requested size")
                        f.write(buf)
                        progress[idx] = got
            if got != want:
                raise ValueError("short range response")
            receipt.write_text(json.dumps({"identity": identity,
                "sha256": hashlib.sha256(dest.read_bytes()).hexdigest()}), encoding="utf-8")
            return
        except Exception as e:
            print(f"[seg {idx}] 第 {attempt + 1} 次失败: {type(e).__name__}", flush=True)
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
    with urllib.request.urlopen(urllib.request.Request(args.url, method="HEAD", headers=UA), timeout=30) as response:
        total = int(response.headers["Content-Length"])
        validator = response.headers.get("ETag")
        if validator and validator.startswith("W/"):
            validator = None
    if args.workers <= 0 or args.chunk_mb <= 0 or total <= 0:
        ap.error("workers, chunk-mb and resource size must be positive")
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
                                  parts / f"{i:05d}.part", progress, i, validator))
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
        p.with_suffix(p.suffix + ".json").unlink(missing_ok=True)
    parts.rmdir()
    print("完成", flush=True)


if __name__ == "__main__":
    main()
