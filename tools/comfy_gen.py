#!/usr/bin/env python3
"""ComfyUI 批量素材生成（纯标准库，走 ComfyUI HTTP API）。

前置：ComfyUI 已在运行（默认 http://127.0.0.1:8188），checkpoints 里有模型。

用法：
    python3 comfy_gen.py jobs.json [--server 127.0.0.1:8188] [--checkpoint sd_xl_base_1.0.safetensors]

jobs.json 是一个任务数组：
[
  {
    "name": "cover_test",
    "prompt": "anime style rhythm game song cover art, ...",
    "negative": "lowres, bad anatomy, text, watermark",
    "width": 1024, "height": 1024,
    "steps": 25, "cfg": 7.0,
    "seed": 12345,
    "count": 4,
    "out_dir": "../../local-generated/covers"
  },
  {
    "name": "img2img_example",
    "prompt": "rhythm game cover art, neon style",
    "ref_image": "ref.png",        // ComfyUI input 目录里的参考图文件名
    "denoise": 0.85,               // <1 启用 img2img；>=0.8 只取构图/色调参考
    "width": 1024, "height": 1024,
    "count": 1
  }
]

img2img 说明：ref_image 需先放入 ComfyUI 的 input 目录。denoise 越接近 1
与原图差异越大。**注意版权**：以原版素材为参考图的产出属于演绎作品，
只能内部测试用，不能发布；生成结果应留在仓库外的本地目录，不得写入
client/assets。公开曲绘由社区谱师随谱面包提供，不使用本工具生成。

每个任务生成 count 张（seed 递增），完成后从 ComfyUI output 目录
复制到 out_dir（默认留在 ComfyUI output 里，仅打印路径）。
"""

from __future__ import annotations

import argparse
import json
import shutil
import sys
import time
import urllib.request
import uuid
from pathlib import Path

DEFAULT_NEGATIVE = "lowres, bad anatomy, bad hands, text, watermark, signature, blurry"


def build_workflow(prompt: str, negative: str, width: int, height: int,
                   steps: int, cfg: float, seed: int, checkpoint: str,
                   ref_image: str | None = None, denoise: float = 1.0) -> dict:
    """标准 SDXL 文生图 / img2img workflow（API 格式）。"""
    img2img = ref_image is not None and denoise < 1.0
    sampler_latent = ["10", 0] if img2img else ["5", 0]

    wf = {
        "3": {"class_type": "KSampler", "inputs": {
            "seed": seed, "steps": steps, "cfg": cfg,
            "sampler_name": "euler", "scheduler": "normal",
            "denoise": denoise if img2img else 1.0,
            "model": ["4", 0], "positive": ["6", 0],
            "negative": ["7", 0], "latent_image": sampler_latent}},
        "4": {"class_type": "CheckpointLoaderSimple",
              "inputs": {"ckpt_name": checkpoint}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {
            "width": width, "height": height, "batch_size": 1}},
        "6": {"class_type": "CLIPTextEncode",
              "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode",
              "inputs": {"text": negative, "clip": ["4", 1]}},
        "8": {"class_type": "VAEDecode",
              "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {
            "filename_prefix": "dux", "images": ["8", 0]}},
    }
    if img2img:
        wf["10"] = {"class_type": "VAEEncode", "inputs": {
            "pixels": ["11", 0], "vae": ["4", 2]}}
        wf["11"] = {"class_type": "LoadImage", "inputs": {
            "image": ref_image, "upload": "image"}}
    return wf


def api(server: str, path: str, payload: dict | None = None):
    url = f"http://{server}{path}"
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.loads(r.read())


def wait_done(server: str, prompt_id: str, timeout_s: int = 600) -> dict:
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        hist = api(server, f"/history/{prompt_id}")
        if prompt_id in hist:
            return hist[prompt_id]
        time.sleep(1.5)
    raise TimeoutError(f"prompt {prompt_id} 超时未完成")


def run_job(server: str, job: dict, checkpoint: str, comfy_output: Path | None,
            base_dir: Path | None = None) -> None:
    name = job["name"]
    count = int(job.get("count", 1))
    base_seed = int(job.get("seed", 42))
    negative = job.get("negative", DEFAULT_NEGATIVE)
    produced: list[Path] = []

    for i in range(count):
        wf = build_workflow(
            prompt=job["prompt"], negative=negative,
            width=int(job.get("width", 1024)), height=int(job.get("height", 1024)),
            steps=int(job.get("steps", 25)), cfg=float(job.get("cfg", 7.0)),
            seed=base_seed + i, checkpoint=checkpoint,
            ref_image=job.get("ref_image"),
            denoise=float(job.get("denoise", 1.0)))
        resp = api(server, "/prompt",
                   {"prompt": wf, "client_id": str(uuid.uuid4())})
        pid = resp["prompt_id"]
        print(f"[{name}] #{i + 1}/{count} seed={base_seed + i} 已入队 ({pid})")
        result = wait_done(server, pid)
        for node_out in result.get("outputs", {}).values():
            for img in node_out.get("images", []):
                src = comfy_output / img["subfolder"] / img["filename"] \
                    if comfy_output else Path(img["filename"])
                produced.append(src)

    out_dir = job.get("out_dir")
    if out_dir and comfy_output:
        # 相对路径基于 jobs 文件所在目录解析，避免受运行 CWD 影响
        dest_dir = Path(out_dir)
        if not dest_dir.is_absolute() and base_dir is not None:
            dest_dir = (base_dir / dest_dir).resolve()
        dest_dir.mkdir(parents=True, exist_ok=True)
        for j, src in enumerate(produced):
            dst = dest_dir / f"{name}_{j + 1:02d}{src.suffix}"
            shutil.copy2(src, dst)
            print(f"[{name}] -> {dst}")
    else:
        for src in produced:
            print(f"[{name}] 输出: {src}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("jobs", help="任务 JSON 文件")
    ap.add_argument("--server", default="127.0.0.1:8188")
    ap.add_argument("--checkpoint", default="sd_xl_base_1.0.safetensors")
    ap.add_argument("--comfy-output", default=None,
                    help="ComfyUI output 目录路径（用于复制成品）")
    args = ap.parse_args()

    jobs_path = Path(args.jobs).resolve()
    jobs = json.loads(jobs_path.read_text(encoding="utf-8"))
    comfy_output = Path(args.comfy_output) if args.comfy_output else None

    # 健康检查
    try:
        stats = api(args.server, "/system_stats")
        print(f"ComfyUI 已连接: {stats['system']['os']}, "
              f"VRAM {stats['devices'][0]['vram_total'] / 2**30:.1f} GB")
    except Exception as e:
        sys.exit(f"连不上 ComfyUI ({args.server}): {e}")

    for job in jobs:
        run_job(args.server, job, args.checkpoint, comfy_output,
                base_dir=jobs_path.parent)


if __name__ == "__main__":
    main()
