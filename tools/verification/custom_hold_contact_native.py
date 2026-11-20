"""Opt-in real-client replay: a parked finger, a rebounding Hold, and a straight control.
Build the Debug client first. Uses synthetic audio, a headless viewport and isolated data.
Run: python -B tools/verification/custom_hold_contact_native.py NEW_OUTPUT --ready
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import wave

from custom_curve_native import (DEFAULT_ENGINE, DRIVER, ROOT, bar, clean_native_log,
                                 environment, guard, need, runtime_stamp, write_json)


def fixture(pack: Path, formula: str) -> None:
    pack.mkdir()
    head = dict(id='head', type='hold', time=bar(0), center=2, width=.16,
                curveToNext='custom', customExpr=formula,
                nodes=[dict(id='tail', time=bar(0, 17, 96), center=2.2, width=.16, judge=True)])
    common = dict(formatVersion=2, formatMinor=1)
    write_json(pack/'hold.json', dict(format='dynamite-uv-chart', **common, chartId='hold', audioOffsetSec=.5,
               timing=dict(bpms=[dict(time=bar(0), bpm=240)]), notesLeft=[], notesRight=[], notesCenter=[head]))
    write_json(pack/'meta.json', dict(format='dynamite-uv-pack', **common, id='probe.hold-contact', revision=1,
               title='Hold contact', artist='Verification', audio='silence.wav', charts=[dict(id='hold',
               difficulty='hard', level=1, charters=['Verification'], file='hold.json')]))
    with wave.open(str(pack/'silence.wav'), 'wb') as audio:
        audio.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
        audio.writeframes(bytes(2 * 2 * 16000))


def replay(engine, pack, work, fps):
    guard()
    work.mkdir()
    trace = work/'frames.jsonl'
    env = environment(work)
    env.update(DUV_VERIFY_PACK=str(pack), DUV_VERIFY_CHART_ID='hold', DUV_VERIFY_MODE='v2', DUV_VERIFY_AUTO='0',
               DUV_VERIFY_FIXED_FPS=str(fps), DUV_VERIFY_FRAME_COUNT=str(2*fps), DUV_VERIFY_TRACE=str(trace), DUV_VERIFY_HOLD_PROBE='1',
               DUV_VERIFY_READY=str(work/'ready'))
    args = [str(engine), '--headless', '--audio-driver', 'Dummy', '--path', str(ROOT/'client'), '--resolution',
            '1920x1080', '--fixed-fps', str(fps), '--quit-after', str(fps+180), '--log-file', str(work/'engine.log'),
            '--script', str(DRIVER)]
    log = work/'native.log'
    with log.open('xb') as stream:
        proc = subprocess.run(args, stdout=stream, stderr=subprocess.STDOUT, env=env, timeout=60,
                              creationflags=subprocess.CREATE_NO_WINDOW)
    text = log.read_text(encoding='utf-8', errors='replace')
    clean_native_log(text, f'Hold {fps} FPS')
    need(proc.returncode == 0 and (work/'ready').is_file(), 'native client failed: '+str(log))
    touches = [json.loads(line[13:]) for line in text.splitlines() if line.startswith('CUSTOM_TOUCH ')]
    need(len(touches) == 2 and touches[0]['pressed'] and not touches[1]['pressed'] and
         abs(touches[0]['time']-.5) < 1e-12 and abs(touches[1]['time']-1) < 1e-12,
         'expected the same head input and no movement or release before the tail: '+str(touches))
    frames = [json.loads(line) for line in trace.read_text(encoding='utf-8').splitlines()]
    need(len(frames) == 2*fps and abs(frames[-1]['time']-2) < 1e-10, 'incomplete replay')
    # At 4 FPS the queued press and tail are settled in one client frame. The
    # finer traces must also show the Perfect head by itself, before settlement.
    if fps > 4:
        need(any(row['judgement']['resolved'] == 1 and row['judgement']['perfect'] == 1 for row in frames),
             'head was not hit before tail settlement')
    end = frames[-1]
    need(end['judgement']['resolved'] == end['judgement']['total'] == 2, 'missing judgement')
    return dict(fps=fps, touches=touches, judgement=end['judgement'], score=end['score'])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    parser.add_argument('--ready', action='store_true', required=True)
    parser.add_argument('--godot', type=Path, default=DEFAULT_ENGINE)
    args = parser.parse_args()
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    engine = args.godot.resolve(); stamp = runtime_stamp('client', 'DynamiteUniverse.dll')
    failures = []
    # With .16-wide Hold and .4-wide finger, the rebound exits when p reaches
    # 1.4. That is ~10.516 ms after the head. Its 150 ms grace expires before
    # the 177.083 ms tail, and that tail is >162.5 ms from the frozen loss
    # (Hard Good window). For the straight control the finger never loses contact.
    for label, formula, perfect, miss in [('rebound', 't+24*t*(1-t)', 1, 1), ('straight', 't', 2, 0)]:
        case = output/label; case.mkdir(); pack = case/'pack'; fixture(pack, formula)
        rows = [replay(engine, pack, case/str(fps), fps) for fps in (4, 30, 60, 64)]
        write_json(case/'result.json', rows)
        for row in rows:
            j = row['judgement']; print(label, row['fps'], j, row['score'], flush=True)
            if (j['perfect'], j['miss']) != (perfect, miss) or row['score'] != rows[0]['score']:
                failures.append(f"{label} at {row['fps']} FPS")
    need(stamp == runtime_stamp('client', 'DynamiteUniverse.dll'), 'client changed during probe')
    write_json(output/'status.json', dict(failures=failures, assemblies=stamp))
    need(not failures, 'frame-dependent or incorrect grades: '+', '.join(failures))
    print('PASS native parked-finger Hold, 4/30/60/64 FPS')


if __name__ == '__main__':
    main()
