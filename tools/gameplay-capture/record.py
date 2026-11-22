from __future__ import annotations

import argparse
from dataclasses import dataclass
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import recording_fixture
from video_encoding import CODECS, CODEC_ALIASES, DEFAULT_CODEC, DEFAULT_PRESET, SPEEDS, VideoEncoding

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_GODOT = ROOT.parent / "godot" / "Godot_v4.7.1-stable_mono_win64" / "Godot_v4.7.1-stable_mono_win64_console.exe"

sys.path.insert(0, str(ROOT / "tools" / "client-menu-qa"))
import run as qa_stage


FPS = 60


def seconds(value: str) -> Decimal:
    try:
        duration = Decimal(value)
    except InvalidOperation as error:
        raise argparse.ArgumentTypeError('expected a duration in seconds') from error
    if not duration.is_finite() or not 0 <= duration <= 60:
        raise argparse.ArgumentTypeError('duration must be between 0 and 60 seconds')
    return duration


@dataclass(frozen=True)
class CaptureOptions:
    preview: Decimal
    result_hold: Decimal
    encoding: VideoEncoding = VideoEncoding()

    @property
    def preview_frames(self) -> int:
        return frame_count(min(self.preview, Decimal(recording_fixture.PREVIEW_DURATION)))

    @property
    def hold_frames(self) -> int:
        return frame_count(self.result_hold)


def frame_count(duration: Decimal) -> int:
    return int((duration * FPS).to_integral_value(rounding=ROUND_HALF_UP))


def start_frame(options: CaptureOptions, frames: dict) -> int:
    if options.preview_frames:
        if abs(frames['start'] - frames['preview-playing'] - options.preview_frames) > 1:
            raise RuntimeError('Actual preview duration did not match the requested frame count')
        start = max(0, frames['expand'] - 12)
    else:
        if 'expand' in frames or 'preview-playing' in frames or not frames['start'] < frames['handoff'] < frames['gameplay-music']:
            raise RuntimeError('Expected no preview and a real handoff before gameplay')
        start = frames['handoff']
    if (abs(frames['result-held'] - frames['result-revealed'] - options.hold_frames) > 1 or
            not frames['music-ended'] <= frames['result-intro'] < frames['result-revealed'] <= frames['result-held']):
        raise RuntimeError('Expected natural music end, a complete reveal and the requested result hold')
    return start


def run_capture(godot: Path, options: CaptureOptions) -> Path:
    started_at = time.perf_counter()
    ffmpeg, ffprobe = shutil.which("ffmpeg"), shutil.which("ffprobe")
    if not ffmpeg or not ffprobe:
        raise RuntimeError("ffmpeg and ffprobe are required to validate and deliver the MP4")
    options.encoding.require_encoder(ffmpeg)
    engine = godot.resolve(strict=True)
    if not engine.is_file() or "mono" not in engine.name.lower() or engine.suffix.lower() != ".exe":
        raise RuntimeError("Expected the Godot 4.7.1 Mono Windows console executable")
    output_root = ROOT / ".tmp" / "gameplay-capture" / "runs"
    run, project, user = qa_stage.stage(
        "gameplay-capture", output_root=output_root,
        driver=ROOT / "tools" / "gameplay-capture" / "capture_driver.gd",
        populate=recording_fixture.create_pack,
    )
    text = (project / "project.godot").read_text(encoding="utf-8")
    main_scene = 'run/main_scene="res://scenes/main.tscn"'
    if text.count(main_scene) != 1:
        raise RuntimeError("Could not identify the formal client entry scene")
    text = text.replace(main_scene, 'run/main_scene="res://scenes/song_select.tscn"')
    marker = 'MigrationQA="*res://qa_driver.gd"'
    if text.count(marker) != 1:
        raise RuntimeError("Could not isolate the recording driver autoload")
    text = text.replace(marker, 'CaptureDriver="*res://qa_driver.gd"')
    (project / "project.godot").write_text(text, encoding="utf-8")

    video = run / "capture.avi"
    command = [str(engine), "--path", str(project), "--windowed", "--resolution", "1920x1080",
               "--disable-vsync", "--audio-driver", "Dummy", "--fixed-fps", str(FPS),
               "--write-movie", str(video), "--log-file", str(run / "player.log"),
               "--quit-after", str(FPS * 65 + options.hold_frames), "--", "--chart-capture-prototype"]
    env = {k: v for k, v in os.environ.items()
           if not k.upper().startswith("DUV_VERIFY_") and k.upper() != "DYNAMITE_UNIVERSE_START_SEC"}
    env.update(APPDATA=str(run / "roaming"), LOCALAPPDATA=str(run / "local"),
               TMP=str(run / "temp"), TEMP=str(run / "temp"), HOME=str(run / "home"),
               USERPROFILE=str(run / "home"), DOTNET_CLI_HOME=str(run / "home"),
               DOTNET_CLI_TELEMETRY_OPTOUT="1", DU_CAPTURE_OUT=str(run), DU_CAPTURE_USER=str(user),
               DU_CAPTURE_PREVIEW_FRAMES=str(options.preview_frames),
               DU_CAPTURE_RESULT_HOLD_FRAMES=str(options.hold_frames))
    for name in ("HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"):
        env[name] = ""
    env["NO_PROXY"] = "127.0.0.1,localhost"

    render_started = time.perf_counter()
    with (run / "console.log").open("wb") as log:
        process = subprocess.Popen(command, cwd=project, env=env, stdout=log,
                                   stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
        try:
            code = process.wait(timeout=900)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)
    render_seconds = time.perf_counter() - render_started
    report_path = run / "capture-result.json"
    report = json.loads(report_path.read_text(encoding="utf-8")) if report_path.is_file() else {}
    log_path = run / "player.log"
    if code != 0 or report.get("pass") is not True or not log_path.is_file():
        raise RuntimeError(f"Prototype recording did not pass; inspect {log_path} and console.log")
    log = log_path.read_text(encoding="utf-8", errors="replace")
    results = [line for line in log.splitlines() if line.startswith("=== RESULT ===")]
    if ("ERROR:" in log or len(results) != 1 or
            "score=1000000 " not in results[0] or "newRecord=False outcome=AllPerfect" not in results[0]
            or (user / "scores.json").exists()):
        raise RuntimeError(f"Expected one real Auto all-perfect result without saved scores; inspect {log_path}")
    qa_stage.verify_capture(video)

    trim_frame = start_frame(options, report["frames"])
    mp4 = run / "synthetic-pulse.mp4"
    encode_started = time.perf_counter()
    options.encoding.encode(ffmpeg, video, mp4, trim_frame, FPS)
    encode_seconds = time.perf_counter() - encode_started
    metadata = options.encoding.verify(ffmpeg, ffprobe, mp4, FPS)
    with mp4.open("rb") as media:
        digest = hashlib.file_digest(media, "sha256").hexdigest()
    summary = {"source": str(video), "mp4": str(mp4), "sha256": digest, "probe": metadata,
               "trimFrame": trim_frame, "recording": report, "gameResult": results[0],
               "options": {"previewRequestedSeconds": str(options.preview), "previewFrames": options.preview_frames,
                           "resultHoldFrames": options.hold_frames, **options.encoding.metadata(),
                           "vsync": False, "audioDriver": "Dummy"},
               "wallSeconds": {"record": round(render_seconds, 3), "encode": round(encode_seconds, 3),
                               "totalWithValidation": round(time.perf_counter() - started_at, 3)}}
    (run / "metadata.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print("PASS:", mp4, flush=True)
    return mp4


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Record the original synthetic DynaMaker/gameplay prototype")
    parser.add_argument("--godot", type=Path, default=DEFAULT_GODOT)
    parser.add_argument('--preview-seconds', type=seconds, default='2',
                        help='time to play the package preview, clamped to its length; 0 starts at Track Handoff (default: 2)')
    parser.add_argument('--result-hold-seconds', type=seconds, default='2',
                        help='time to hold after grade reveal; 0 still shows the animation (default: 2)')
    parser.add_argument('--codec', choices=tuple(CODECS) + tuple(CODEC_ALIASES), default=DEFAULT_CODEC,
                        help='MP4 video format (default: h264)')
    parser.add_argument('--preset', choices=SPEEDS, default=DEFAULT_PRESET,
                        help='encoding speed; high is fastest (default: medium)')
    parser.add_argument('--crf', type=int, help='encoding quality; lower is higher fidelity (defaults: H.264 16, HEVC 20, AV1 24)')
    arguments = parser.parse_args()
    try:
        encoding = VideoEncoding(arguments.codec, arguments.preset, arguments.crf)
    except ValueError as error:
        parser.error(str(error))
    options = CaptureOptions(arguments.preview_seconds, arguments.result_hold_seconds, encoding)
    run_capture(arguments.godot, options)
