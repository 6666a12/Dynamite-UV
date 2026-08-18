#!/usr/bin/env python3
"""Record and compare deterministic legacy/v2 gameplay verification clips.

All outputs are local diagnostics. The script never embeds a chart path and defaults to
local-generated/v2-equivalence, which is ignored by Git.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_OUTPUT = ROOT / "local-generated" / "v2-equivalence"
DEFAULT_CLIENT = ROOT / "client"
DEFAULT_GODOT = ROOT.parent / "godot" / "Godot_v4.7.1-stable_mono_win64" / (
    "Godot_v4.7.1-stable_mono_win64_console.exe"
)
GODOT_PROCESSES = (
    "Godot_v4.7.1-stable_mono_win64_console.exe",
    "Godot_v4.7.1-stable_mono_win64.exe",
)


def run(command: list[str], *, check: bool = True, **kwargs: object) -> subprocess.CompletedProcess:
    return subprocess.run(command, check=check, **kwargs)


def require_tool(name: str) -> str:
    path = shutil.which(name)
    if path is None:
        raise RuntimeError(f"required executable is not available: {name}")
    return path


def stop_godot() -> None:
    if os.name != "nt":
        return
    for image in GODOT_PROCESSES:
        run(
            ["taskkill", "/F", "/IM", image],
            check=False,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )


def parse_resolution(value: str) -> tuple[int, int]:
    match = re.fullmatch(r"([1-9][0-9]*)x([1-9][0-9]*)", value)
    if match is None:
        raise argparse.ArgumentTypeError("resolution must look like 1600x900")
    width, height = (int(part) for part in match.groups())
    if width % 2 or height % 2:
        raise argparse.ArgumentTypeError("resolution dimensions must be even")
    return width, height


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_output_directory(root: Path, label: str) -> Path:
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", label):
        raise ValueError("label must be a safe ASCII identifier")
    root = root.resolve()
    output = (root / label).resolve()
    if output.parent != root:
        raise ValueError("output label escapes the output root")
    output.mkdir(parents=True, exist_ok=True)
    return output


def ffprobe(path: Path) -> dict[str, object]:
    result = run(
        [
            require_tool("ffprobe"),
            "-v",
            "error",
            "-show_entries",
            "format=duration,size:stream=codec_name,width,height,r_frame_rate,nb_frames",
            "-of",
            "json",
            str(path),
        ],
        capture_output=True,
        text=True,
    )
    return json.loads(result.stdout)


def command_record(args: argparse.Namespace) -> int:
    output = safe_output_directory(args.output_root, args.label)
    video = output / "capture.mkv"
    godot_movie = output / "godot-movie.avi"
    log = output / "godot.log"
    trace = output / "trace.jsonl"
    ready = output / "ready"
    start_gate = output / "start"
    metadata_path = output / "metadata.json"
    artifacts = (video, godot_movie, log, trace, ready, start_gate, metadata_path)
    if any(path.exists() for path in artifacts) and not args.force:
        raise RuntimeError(f"verification output already exists: {output}; pass --force to replace")
    if args.force:
        for path in artifacts:
            path.unlink(missing_ok=True)

    pack = args.pack.resolve()
    if not pack.is_dir() or not (pack / "meta.json").is_file():
        raise RuntimeError(f"pack directory has no meta.json: {pack}")
    godot = args.godot.resolve()
    client = args.client.resolve()
    if not godot.is_file():
        raise RuntimeError(f"Godot mono executable not found: {godot}")
    if not (client / "project.godot").is_file():
        raise RuntimeError(f"Godot project not found: {client}")

    width, height = args.resolution
    frame_count = round(args.duration * args.fps)
    env = os.environ.copy()
    env.update(
        {
            "DUV_VERIFY_PACK": str(pack),
            "DUV_VERIFY_CHART_ID": args.chart_id,
            "DUV_VERIFY_MODE": args.mode,
            "DUV_VERIFY_AUTO": "1",
            "DUV_VERIFY_FIXED_FPS": str(args.fps),
            "DUV_VERIFY_FRAME_COUNT": str(frame_count),
            "DUV_VERIFY_TRACE": str(trace),
            "DUV_VERIFY_READY": str(ready),
            "DUV_VERIFY_START_GATE": str(start_gate),
            "DYNAMITE_UNIVERSE_START_SEC": format(args.start_sec, ".17g"),
        }
    )

    stop_godot()
    process: subprocess.Popen[bytes] | None = None
    started_at = time.time()
    try:
        with log.open("wb") as log_file:
            process = subprocess.Popen(
                [
                    str(godot),
                    "--path",
                    str(client),
                    "--position",
                    "0,0",
                    "--resolution",
                    f"{width}x{height}",
                    "--borderless",
                    "--always-on-top",
                    *(
                        ["--write-movie", str(godot_movie),
                         "--fixed-fps", str(args.fps),
                         "--quit-after", str(frame_count)]
                        if args.capture == "godot" else []
                    ),
                    "res://scenes/gameplay.tscn",
                ],
                env=env,
                stdout=log_file,
                stderr=subprocess.STDOUT,
            )
            deadline = time.monotonic() + args.warmup
            while not ready.is_file():
                if process.poll() is not None:
                    raise RuntimeError(f"Godot exited before recording; inspect {log}")
                if time.monotonic() >= deadline:
                    raise RuntimeError(
                        f"Godot did not signal verification readiness within {args.warmup}s; "
                        f"inspect {log}"
                    )
                time.sleep(0.02)
            start_gate.write_text("start\n", encoding="utf-8")
            if args.capture == "desktop":
                run(
                    [
                        require_tool("ffmpeg"),
                        "-y",
                        "-loglevel",
                        "warning",
                        "-f",
                        "gdigrab",
                        "-offset_x",
                        "0",
                        "-offset_y",
                        "0",
                        "-video_size",
                        f"{width}x{height}",
                        "-framerate",
                        str(args.fps),
                        "-i",
                        "desktop",
                        "-frames:v",
                        str(frame_count),
                        "-c:v",
                        "ffv1",
                        "-level",
                        "3",
                        "-pix_fmt",
                        "bgr0",
                        str(video),
                    ]
                )
            else:
                try:
                    process.wait(timeout=max(30.0, args.duration * 4.0))
                except subprocess.TimeoutExpired as error:
                    raise RuntimeError("Godot Movie Writer did not finish in time") from error
                if not godot_movie.is_file():
                    raise RuntimeError(f"Godot Movie Writer did not create {godot_movie}")
                run(
                    [
                        require_tool("ffmpeg"), "-y", "-v", "error",
                        "-i", str(godot_movie), "-c:v", "ffv1", "-level", "3",
                        str(video),
                    ]
                )
    finally:
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
        stop_godot()

    metadata = {
        "label": args.label,
        "mode": args.mode,
        "capture": args.capture,
        "pack": str(pack),
        "chartId": args.chart_id,
        "startSec": args.start_sec,
        "durationSec": args.duration,
        "fps": args.fps,
        "frameCount": frame_count,
        "resolution": {"width": width, "height": height},
        "readyTimeoutSec": args.warmup,
        "elapsedWallSec": time.time() - started_at,
        "video": ffprobe(video),
        "videoSha256": sha256(video),
        "tracePresent": trace.is_file(),
    }
    metadata_path.write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
    print(metadata_path)
    return 0


def write_framemd5(source: Path, destination: Path) -> None:
    with destination.open("wb") as output:
        run(
            [require_tool("ffmpeg"), "-v", "error", "-i", str(source), "-f", "framemd5", "-"],
            stdout=output,
        )


def metric_summary(path: Path, prefix: str) -> dict[str, float | int | None]:
    values: list[float] = []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        match = re.search(rf"(?:^|\s){re.escape(prefix)}:([-+0-9.eE]+|inf)(?:\s|$)", line)
        if match is None:
            continue
        raw = match.group(1)
        values.append(float("inf") if raw == "inf" else float(raw))
    finite = [value for value in values if value != float("inf")]
    return {
        "frames": len(values),
        "minimumFinite": min(finite) if finite else None,
        "meanFinite": sum(finite) / len(finite) if finite else None,
        "infiniteFrames": len(values) - len(finite),
    }


def compare_traces(left: Path, right: Path) -> dict[str, object]:
    if not left.is_file() or not right.is_file():
        return {"available": False}
    left_lines = left.read_bytes().splitlines()
    right_lines = right.read_bytes().splitlines()
    mismatch = next(
        (index for index, pair in enumerate(zip(left_lines, right_lines)) if pair[0] != pair[1]),
        None,
    )
    if mismatch is None and len(left_lines) != len(right_lines):
        mismatch = min(len(left_lines), len(right_lines))
    return {
        "available": True,
        "equal": mismatch is None,
        "leftFrames": len(left_lines),
        "rightFrames": len(right_lines),
        "firstMismatchFrame": mismatch,
    }


def trim_video_to_trace(source: Path, trace: Path, fps: int, destination: Path) -> int | None:
    if not trace.is_file():
        return None
    frame_count = len(trace.read_bytes().splitlines())
    if frame_count <= 0:
        return 0
    run(
        [
            require_tool("ffmpeg"), "-y", "-v", "error", "-i", str(source),
            "-vf", f"select='lt(n,{frame_count})',setpts=N/({fps}*TB)",
            "-vsync", "0", "-c:v", "ffv1", "-level", "3", str(destination),
        ]
    )
    return frame_count


def command_compare(args: argparse.Namespace) -> int:
    left = args.left.resolve()
    right = args.right.resolve()
    if not left.is_file() or not right.is_file():
        raise RuntimeError("both comparison videos must exist")
    output = safe_output_directory(args.output_root, args.label)
    if args.trim_to_trace:
        left_trimmed = output / "left-trimmed.mkv"
        right_trimmed = output / "right-trimmed.mkv"
        left_count = trim_video_to_trace(left, args.left_trace, args.fps, left_trimmed)
        right_count = trim_video_to_trace(right, args.right_trace, args.fps, right_trimmed)
        if left_count is None or right_count is None:
            raise RuntimeError("--trim-to-trace requires both trace files")
        if left_count != right_count:
            raise RuntimeError(
                f"trace frame counts differ and cannot define a common video range: "
                f"{left_count} vs {right_count}"
            )
        left = left_trimmed
        right = right_trimmed
    ssim_stats = output / "ssim.log"
    psnr_stats = output / "psnr.log"
    left_md5 = output / "left.framemd5"
    right_md5 = output / "right.framemd5"
    diff_video = output / "difference.mkv"
    side_by_side = output / "side-by-side.mkv"

    write_framemd5(left, left_md5)
    write_framemd5(right, right_md5)
    ssim_filter_path = str(ssim_stats.resolve()).replace("\\", "/").replace(":", r"\:")
    psnr_filter_path = str(psnr_stats.resolve()).replace("\\", "/").replace(":", r"\:")
    run(
        [
            require_tool("ffmpeg"), "-y", "-v", "error", "-i", str(left), "-i", str(right),
            "-lavfi", f"[0:v][1:v]ssim=stats_file='{ssim_filter_path}'", "-f", "null", "-",
        ]
    )
    run(
        [
            require_tool("ffmpeg"), "-y", "-v", "error", "-i", str(left), "-i", str(right),
            "-lavfi", f"[0:v][1:v]psnr=stats_file='{psnr_filter_path}'", "-f", "null", "-",
        ]
    )
    run(
        [
            require_tool("ffmpeg"), "-y", "-v", "error", "-i", str(left), "-i", str(right),
            "-filter_complex", "[0:v][1:v]blend=all_mode=difference", "-c:v", "ffv1", "-level", "3",
            str(diff_video),
        ]
    )
    run(
        [
            require_tool("ffmpeg"), "-y", "-v", "error", "-i", str(left), "-i", str(right),
            "-filter_complex", "[0:v][1:v]hstack=inputs=2", "-c:v", "ffv1", "-level", "3",
            str(side_by_side),
        ]
    )

    left_lines = left_md5.read_text(encoding="utf-8").splitlines()
    right_lines = right_md5.read_text(encoding="utf-8").splitlines()
    first_frame_mismatch = next(
        (index for index, pair in enumerate(zip(left_lines, right_lines)) if pair[0] != pair[1]),
        None,
    )
    if first_frame_mismatch is None and len(left_lines) != len(right_lines):
        first_frame_mismatch = min(len(left_lines), len(right_lines))

    report = {
        "left": {"path": str(left), "sha256": sha256(left), "probe": ffprobe(left)},
        "right": {"path": str(right), "sha256": sha256(right), "probe": ffprobe(right)},
        "rawFrameHashesEqual": left_md5.read_bytes() == right_md5.read_bytes(),
        "firstFrameMd5MismatchLine": first_frame_mismatch,
        "ssim": metric_summary(ssim_stats, "All"),
        "psnr": metric_summary(psnr_stats, "psnr_avg"),
        "trace": compare_traces(args.left_trace, args.right_trace),
        "artifacts": {
            "difference": str(diff_video),
            "sideBySide": str(side_by_side),
        },
    }
    report_path = output / "report.json"
    report_path.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(report_path)
    trace_equal = report["trace"].get("equal", True)
    return 0 if report["rawFrameHashesEqual"] and trace_equal else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subcommands = parser.add_subparsers(dest="command", required=True)

    record = subcommands.add_parser("record", help="record one deterministic verification clip")
    record.add_argument("--label", required=True)
    record.add_argument("--pack", required=True, type=Path)
    record.add_argument("--chart-id", required=True)
    record.add_argument("--mode", required=True, choices=("legacy-direct", "v2"))
    record.add_argument("--start-sec", type=float, default=0.0)
    record.add_argument("--duration", type=float, default=10.0)
    record.add_argument("--fps", type=int, default=60)
    record.add_argument("--resolution", type=parse_resolution, default=parse_resolution("1600x900"))
    record.add_argument("--capture", choices=("godot", "desktop"), default="godot")
    record.add_argument("--warmup", type=float, default=20.0,
                        help="maximum seconds to wait for Godot readiness")
    record.add_argument("--godot", type=Path, default=DEFAULT_GODOT)
    record.add_argument("--client", type=Path, default=DEFAULT_CLIENT)
    record.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT)
    record.add_argument("--force", action="store_true")
    record.set_defaults(handler=command_record)

    compare = subcommands.add_parser("compare", help="compare two verification clips frame by frame")
    compare.add_argument("--label", required=True)
    compare.add_argument("--left", required=True, type=Path)
    compare.add_argument("--right", required=True, type=Path)
    compare.add_argument("--left-trace", type=Path, default=Path("__missing_left_trace__"))
    compare.add_argument("--right-trace", type=Path, default=Path("__missing_right_trace__"))
    compare.add_argument("--trim-to-trace", action="store_true",
                         help="compare only the number of frames present in each trace")
    compare.add_argument("--fps", type=int, default=60)
    compare.add_argument("--output-root", type=Path, default=DEFAULT_OUTPUT / "comparisons")
    compare.set_defaults(handler=command_compare)
    return parser


def main(argv: list[str] | None = None) -> int:
    try:
        args = build_parser().parse_args(argv)
        return args.handler(args)
    except (OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
