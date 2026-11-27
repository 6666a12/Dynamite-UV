"""Run rendered gameplay on Windows using the generated Prism Relay fixture (no preset/export changes)."""
from __future__ import annotations

import argparse
import math
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
GODOT = ROOT.parent / 'godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe'
CHARTS = ('title-only', 'demo-keep', 'demo-replace', 'stress-100', 'stress-500', 'stress-1500')


def validate_bench(text: str, chart: str, off: bool, expect_batch: bool | None = None) -> None:
    rows = [line for line in text.splitlines() if line.startswith('STORYBOARD_BENCH ')]
    if len(rows) != 1:
        raise ValueError('expected exactly one timing summary')
    fields = dict(field.split('=', 1) for field in rows[0].split()[1:])
    count = 0 if off else int(chart.rsplit('-', 1)[1])
    try:
        if (fields['chart'] != chart or fields['loaded'] != str(not off) or fields['forced_off'] != str(off)
                or int(fields['max_items']) != count or float(fields['average_items']) != count):
            raise ValueError(f'expected {count} active sprites throughout the measured window')
        if expect_batch is not None and int(fields['max_batches']) != int(expect_batch):
            raise ValueError(f'expected {int(expect_batch)} instanced draw batch(es)')
        if int(fields['frames']) < 30 or any(not math.isfinite(float(fields[key])) or float(fields[key]) <= 0
                                             for key in ('fps', 'frame_p50_ms', 'frame_p95_ms', 'frame_p99_ms')):
            raise ValueError('empty or invalid frame timing sample')
    except KeyError as error:
        raise ValueError(f'missing benchmark field: {error}') from error


def main() -> int:
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument('mode', choices=('bench', 'capture'))
    args.add_argument('--godot', type=Path, default=GODOT)
    args.add_argument('--pack', type=Path, required=True)
    args.add_argument('--output', type=Path, required=True, help='new or empty evidence directory')
    args.add_argument('--rounds', type=int, default=3)
    args.add_argument('--chart', choices=CHARTS, default='demo-replace')
    args.add_argument('--frames', type=int, default=1218, help='capture: number of frames at 60 fps')
    args.add_argument('--off', action='store_true', help='capture: disable storyboard for visual A/B')
    args.add_argument('--gpu-index', type=int, help='optional explicit Vulkan GPU device')
    comparison = args.add_mutually_exclusive_group()
    comparison.add_argument('--no-batch', action='store_true', help='use the pre-instancing draw path')
    comparison.add_argument('--compare-batch', action='store_true', help='bench both draw paths in alternating order')
    options = args.parse_args()
    if options.compare_batch and options.mode != 'bench':
        args.error('--compare-batch requires bench mode')
    godot, pack, output = (p.resolve() for p in (options.godot, options.pack, options.output))
    if not godot.is_file() or not (pack / 'storyboard/index.json').is_file():
        args.error('cannot find Godot Mono or the generated Storyboard pack')
    if not (1 <= options.rounds <= 8 and 1 <= options.frames <= 1800):
        args.error('use 1–8 bench rounds or 1–1800 capture frames')
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        args.error('output must be new or empty')
    output.mkdir(parents=True, exist_ok=True)
    env = {key: value for key, value in os.environ.items() if not key.startswith(('DUV_VERIFY_', 'DUV_STORYBOARD_'))}
    env.pop('DYNAMITE_UNIVERSE_START_SEC', None)
    env.update(DUV_VERIFY_PACK=str(pack), DUV_VERIFY_MODE='v2', DUV_VERIFY_AUTO='1')
    base = [str(godot), '--path', 'client', '--resolution', '1280x720', '--position', '40,40', '--disable-vsync']
    if options.gpu_index is not None:
        base.extend(('--gpu-index', str(options.gpu_index)))
    scene = 'res://scenes/gameplay.tscn'
    if options.mode == 'capture':
        cases = [(options.chart, options.off, 1, options.no_batch)]
    else:
        # Alternate ordering to reduce warming bias. The off case uses the exact same selected chart as 1500.
        workloads = [('stress-1500', True), ('stress-100', False), ('stress-500', False), ('stress-1500', False)]
        cases = [(chart, off, round_, no_batch) for round_ in range(1, options.rounds + 1)
                 for chart, off in (workloads if round_ % 2 else reversed(workloads))
                 for no_batch in ((True, False) if round_ % 2 else (False, True))
                 ] if options.compare_batch else [(chart, off, round_, options.no_batch)
                    for round_ in range(1, options.rounds + 1)
                    for chart, off in (workloads if round_ % 2 else reversed(workloads))]
    for chart, off, round_, no_batch in cases:
        if options.compare_batch and off and no_batch:
            continue  # Off has no Storyboard to batch; one control per round is enough.
        state = 'off' if off else ('direct' if no_batch else 'instanced') if options.compare_batch else 'on'
        label = f'{chart}-{state}-{round_}'
        path = output / f'{label}.log'
        child_env = dict(env, DUV_VERIFY_CHART_ID=chart, DUV_STORYBOARD_OFF='1' if off else '0',
                         DUV_STORYBOARD_NO_BATCH='1' if no_batch else '0', DUV_STORYBOARD_BRIGHTNESS='100')
        if options.mode == 'bench':
            child_env['DUV_STORYBOARD_BENCH'] = '1'
            command = [*base, '--max-fps', '0', scene]
        else:
            command = [*base, '--write-movie', str(output / f'{label}.avi'), '--fixed-fps', '60',
                       '--quit-after', str(options.frames), scene, '--', '--chart-capture-prototype']
        print(f'Running {label}', flush=True)
        with path.open('xb') as log:
            # A regular file avoids buffering a second process's output in memory.
            result = subprocess.run(command, cwd=ROOT, env=child_env, stdout=log, stderr=subprocess.STDOUT,
                                    check=False, timeout=58 if options.mode == 'bench' else 480)
        text = path.read_text(encoding='utf-8', errors='replace')
        if result.returncode or ('STORYBOARD_LOAD' in text) == off or 'ERROR:' in text:
            raise RuntimeError(f'{label}: rendering/load expectation failed; see {path}')
        if options.mode == 'bench':
            try:
                validate_bench(text, chart, off, expect_batch=not off and not no_batch)
            except ValueError as error:
                raise RuntimeError(f'{label}: {error}; see {path}') from error
        if options.mode == 'capture' and 'Done recording movie' not in text:
            raise RuntimeError(f'{label}: no completed movie; see {path}')
        for line in text.splitlines():
            if line.startswith(('Vulkan ', 'STORYBOARD_', 'Done recording movie')):
                print(line, flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
