"""Measure the real Windows gameplay framebuffer (local developer packs only)."""
from __future__ import annotations

import argparse
from decimal import Decimal, InvalidOperation
from pathlib import Path
import math
import os
import re
import shutil
import subprocess

PLAYFIELD_CASES = {'legacy-inputs', 'retained-links', 'cached-stage', 'flat-stage'}
VIDEO_CASES = {'video', 'video-low'}
FLAT_CASES = {'flat', *VIDEO_CASES}
NVIDIA_QUERY = '--query-gpu=timestamp,uuid,utilization.gpu,clocks.current.graphics,pstate,power.draw,memory.used'

ROOT = Path(__file__).resolve().parents[2]
GODOT = ROOT.parent / 'godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'


def timestamp(value: str) -> Decimal:
    try:
        seconds = Decimal(value)
    except InvalidOperation as exc:
        raise argparse.ArgumentTypeError('expected media seconds') from exc
    if not seconds.is_finite() or not 0 <= seconds <= 3600:
        raise argparse.ArgumentTypeError('media seconds must be between 0 and 3600')
    return seconds


def fields(text: str, prefix: str) -> dict[str, str]:
    lines = [row for row in text.splitlines() if row.startswith(prefix + ' ')]
    if len(lines) != 1:
        raise ValueError(f'expected exactly one {prefix} record')
    return dict(word.split('=', 1) for word in lines[0].split()[1:])


def validate_video(text: str, start: Decimal, end: Decimal) -> None:
    load = fields(text, 'STORYBOARD_VIDEO_LOAD')
    if not any(line.startswith('STORYBOARD_VIDEO_PLAY ') for line in text.splitlines()):
        raise ValueError('video needs an actual first frame log')
    try:
        clip_start, duration = Decimal(load['start_s']), Decimal(load['duration_s'])
    except InvalidOperation as error:
        raise ValueError('invalid video clip timing') from error
    if (not clip_start.is_finite() or not duration.is_finite() or duration <= 0 or
            clip_start > start or clip_start + duration < end):
        raise ValueError('video clip does not cover the measurement window')
    bench = '\n'.join(line for line in text.splitlines() if line.startswith('STORYBOARD_VIDEO_STATS reason=bench '))
    stats = fields(bench, 'STORYBOARD_VIDEO_STATS')
    if stats['window_active'] != 'True':
        raise ValueError('video was not active at both measurement boundaries')
    if int(stats['window_frame_updates']) < 15:
        raise ValueError('video needs at least 15 submitted frames inside the measurement window')


def validate(text: str, chart: str, mode: str, size: str, cap: int, fall_speed: int, start: Decimal, end: Decimal,
             *, fullscreen: bool = False) -> None:
    frame = fields(text, 'STORYBOARD_BENCH')
    gpu = fields(text, 'STORYBOARD_GPU')
    logic = fields(text, 'STORYBOARD_LOGIC')
    if not re.search(rf"^chart '.+' notes=.* speed={fall_speed}$", text, re.MULTILINE):
        raise ValueError('wrong note fall speed')
    if int(logic['from_unix_ms']) >= int(logic['until_unix_ms']) or float(logic['main_p95_ms']) <= 0:
        raise ValueError('invalid logic timing result')
    for record in (frame, gpu):
        if record['chart'] != chart:
            raise ValueError('wrong chart loaded')
    if ('ERROR:' in text or gpu['target'] != size or gpu['window'] != size or gpu['vsync'] != 'Disabled'
            or gpu['renderer'] != 'mobile' or int(gpu['max_fps']) != cap):
        raise ValueError(f'invalid render target/mode: {gpu}, expected {size}, Forward Mobile, VSync off, cap {cap}')
    if abs(Decimal(gpu['from_s']) - start) > Decimal('.00051') or abs(Decimal(gpu['until_s']) - end) > Decimal('.00051'):
        raise ValueError('wrong measurement window')
    storyboard_off = mode == 'off' or mode in PLAYFIELD_CASES
    max_items = int(frame['max_items'])
    if (frame['loaded'] != str(not storyboard_off) or frame['forced_off'] != str(storyboard_off)
            or max_items < 0 or (max_items > 0) != (mode in ('on', 'atlas', 'redraw-hits'))):
        raise ValueError('wrong storyboard state or no visible performance sampled')
    if mode in FLAT_CASES and fields(text, 'STORYBOARD_LOAD')['backdrop'] != 'Replace':
        raise ValueError('flat and video cases require a replaced stock backdrop')
    if fullscreen or mode in PLAYFIELD_CASES:
        playfield = fields(text, 'PLAYFIELD_BENCH')
        if fullscreen and playfield['mode'] not in ('Fullscreen', 'ExclusiveFullscreen'):
            raise ValueError('requested fullscreen but game window did not report fullscreen')
        if mode == 'cached-stage':
            width, height = map(int, size.split('x'))
            cache_width, cache_height = map(int, playfield['cache'].split('x'))
            if cache_width != width or not height <= cache_height <= height * 1.05 + 1:
                raise ValueError(f'background cache was not active at expected resolution (with top padding): {playfield}')
        if mode == 'retained-links' and int(playfield['links_retained']) <= 0:
            raise ValueError('retained link case did not retain geometry')
    if mode == 'atlas':
        if not any(line.startswith('STORYBOARD_ATLAS ') for line in text.splitlines()):
            raise ValueError('atlas case requires a pre-streaming build with atlas telemetry')
        if int(fields(text, 'STORYBOARD_ATLAS')['textures']) <= 0:
            raise ValueError('atlas path requested but no textures were packed')
    if mode in VIDEO_CASES:
        validate_video(text, start, end)
    if int(gpu['gpu_valid']) < 15 or int(frame['frames']) < 120:
        raise ValueError('insufficient frame or GPU samples')
    if any(not math.isfinite(float(k)) or float(k) <= 0 for k in (
            frame['fps'], frame['frame_p95_ms'], gpu['viewport_gpu_p50_ms'], gpu['viewport_gpu_p95_ms'])):
        raise ValueError('invalid timing result')


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--godot', type=Path, default=GODOT)
    ap.add_argument('--pack', type=Path, required=True, help='local stage with valid Storyboard binding')
    ap.add_argument('--flat', type=Path, help='optional same-chart/same-flat-backdrop zero-sprite pack')
    ap.add_argument('--chart', required=True)
    ap.add_argument('--seek', type=timestamp, required=True)
    ap.add_argument('--from', dest='start', type=timestamp, required=True)
    ap.add_argument('--until', type=timestamp, required=True)
    ap.add_argument('--resolution', default='2560x1440')
    ap.add_argument('--screen', type=int, default=0)
    ap.add_argument('--gpu-index', type=int)
    ap.add_argument('--fullscreen', action='store_true', help='assert fullscreen framebuffer matches requested size')
    ap.add_argument('--cap', type=int, default=0, help='0 is uncapped; VSync disabled in all runs')
    ap.add_argument('--render-thread', choices=('safe', 'separate'))
    ap.add_argument('--phases', action='store_true', help='record synchronous renderer wall time (requires --render-thread safe)')
    ap.add_argument('--case', choices=('off', 'flat', 'on', 'atlas', 'redraw-hits', 'video', 'video-low', *sorted(PLAYFIELD_CASES)),
                    action='append', help='explicit ordered case list; repeat as needed (atlas requires a pre-streaming build)')
    ap.add_argument('--video-file', help='local filename under --flat/storyboard for the video case')
    ap.add_argument('--video-low-file', help='local filename under --flat/storyboard for the video-low case')
    ap.add_argument('--fall-speed-level', type=int, default=14, choices=range(1, 21), metavar='1..20',
                    help='visual note speed in tenths (default 14 = 1.4x)')
    ap.add_argument('--rounds', type=int, default=3)
    ap.add_argument('--output', type=Path, required=True)
    ap.add_argument('--nvidia-monitor', action='store_true', help='collect separate 1Hz device-wide nvidia-smi CSV in a background process')
    a = ap.parse_args()
    if not 1 <= a.rounds <= 8 or not 0 <= a.cap <= 1000 or not a.seek <= a.start < a.until <= a.start + 60:
        ap.error('expected 1–8 rounds, non-negative cap, seek <= start < end, and duration <=60s')
    if a.phases and a.render_thread != 'safe':
        ap.error('--phases requires --render-thread safe; threaded post-draw is deferred')
    if a.case and (len(set(a.case)) != len(a.case) or
                   (set(a.case) & {'flat', 'video', 'video-low'} and a.flat is None) or
                   ('video' in a.case and not a.video_file) or ('video-low' in a.case and not a.video_low_file)):
        ap.error('cases must be distinct; flat/video need --flat and video cases need their corresponding filename')
    if not re.fullmatch(r'[1-9][0-9]{2,3}x[1-9][0-9]{2,3}', a.resolution):
        ap.error('resolution must be e.g. 2560x1440')
    a.godot, a.pack, a.output = a.godot.resolve(), a.pack.resolve(), a.output.resolve()
    if a.flat: a.flat = a.flat.resolve()
    if not a.godot.is_file() or any(not pack.is_dir() for pack in [a.pack, *([a.flat] if a.flat else [])]):
        ap.error('Godot Mono or pack path does not exist')
    if a.output.exists():
        ap.error('output must not exist')
    monitor = shutil.which('nvidia-smi') if a.nvidia_monitor else None
    if a.nvidia_monitor and not monitor:
        ap.error('nvidia-smi is not on PATH')
    a.output.mkdir(parents=True)
    env = {key: val for key, val in os.environ.items() if not key.startswith(('DUV_VERIFY_', 'DUV_STORYBOARD_'))}
    env.update(DUV_VERIFY_MODE='v2', DUV_VERIFY_CHART_ID=a.chart, DUV_VERIFY_AUTO='1',
               DUV_STORYBOARD_BENCH='1', DUV_STORYBOARD_GPU_PROFILE='1', DUV_STORYBOARD_BRIGHTNESS='100',
               DUV_STORYBOARD_BENCH_FROM=str(a.start), DUV_STORYBOARD_BENCH_UNTIL=str(a.until),
               DUV_VERIFY_FALL_SPEED_LEVEL=str(a.fall_speed_level), DYNAMITE_UNIVERSE_START_SEC=str(a.seek))
    cmd = [str(a.godot), '--path', 'client', '--resolution', a.resolution, '--screen', str(a.screen),
           '--disable-vsync', '--max-fps', str(a.cap)]
    if not a.fullscreen:
        cmd += ['--borderless']
    else:
        cmd += ['--fullscreen']
    if a.gpu_index is not None: cmd += ['--gpu-index', str(a.gpu_index)]
    cmd += ['res://scenes/gameplay.tscn']
    if a.render_thread:
        cmd[1:1] = ['--render-thread', a.render_thread]
    if a.phases:
        env['DUV_STORYBOARD_PHASES'] = '1'
    modes = a.case or ['off', *(['flat'] if a.flat else []), 'on']
    cases = [(mode, a.flat if mode in ('flat', 'video', 'video-low') else a.pack) for mode in modes]
    run_identity = None
    for round_ in range(1, a.rounds + 1):
        for mode, pack in (cases if round_ % 2 else reversed(cases)):
            name = f'{mode}-{round_}'
            path = a.output / f'{name}.log'
            print(f'Running {a.resolution} cap={a.cap} {name}', flush=True)
            video_name = a.video_file if mode == 'video' else a.video_low_file if mode == 'video-low' else ''
            current = dict(env, DUV_VERIFY_PACK=str(pack),
                           DUV_STORYBOARD_OFF='1' if mode == 'off' or mode in PLAYFIELD_CASES else '0',
                           DUV_STORYBOARD_BACKDROP={'cached-stage': 'cache', 'flat-stage': 'flat'}.get(mode, ''),
                           DUV_VERIFY_VISUAL_INPUTS='0' if mode == 'legacy-inputs' else '1',
                           DUV_VERIFY_RETAIN_LINKS='1' if mode == 'retained-links' else '0',
                           DUV_STORYBOARD_ATLAS='1' if mode == 'atlas' else '0',
                           DUV_VERIFY_REDRAW_HITS='1' if mode == 'redraw-hits' else '0',
                           DUV_STORYBOARD_VIDEO_FILE=video_name if mode in ('video', 'video-low') else '')
            with path.open('xb') as log:
                process = None
                try:
                    if monitor:
                        with (a.output / f'{name}.nv.csv').open('xb') as nv:
                            # Keep every row identifiable; Godot's adapter indices may not match nvidia-smi's.
                            process = subprocess.Popen([monitor, NVIDIA_QUERY, '--format=csv,noheader,nounits', '-l', '1'],
                                                       stdout=nv, stderr=subprocess.STDOUT)
                    result = subprocess.run(cmd, cwd=ROOT, env=current, stdout=log, stderr=subprocess.STDOUT,
                                            check=False, timeout=float(a.until - a.seek) + 35)
                finally:
                    if process is not None:
                        process.terminate()
                        process.wait(timeout=5)
            text = path.read_text(encoding='utf-8', errors='replace')
            if result.returncode:
                raise RuntimeError(f'{name} exited {result.returncode}: {path}')
            try:
                validate(text, a.chart, mode, a.resolution, a.cap, a.fall_speed_level, a.start, a.until,
                         fullscreen=a.fullscreen)
                identity = fields(text, 'identity')
                identity = (identity['chart'], identity['ruleset'], identity['digest'])
                if run_identity is not None and identity != run_identity:
                    raise ValueError(f'chart/ruleset/digest changed from {run_identity} to {identity}')
                run_identity = identity
            except (ValueError, KeyError) as error:
                raise RuntimeError(f'{name}: {error}; see {path}') from error
            for line in text.splitlines():
                if line.startswith(('Vulkan ', 'STORYBOARD_')):
                    print(line, flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
