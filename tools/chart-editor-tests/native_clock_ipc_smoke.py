#!/usr/bin/env python3
"""Opt-in native editor clock smoke (stdlib only; not part of tools/verify.py).

Build editor/DynaMakerUv.Editor.csproj in Debug first; inherit the same DOTNET_ROOT /
roll-forward settings as a normal native editor run. With no other Godot running:
    python -B tools/chart-editor-tests/native_clock_ipc_smoke.py --godot PATH_TO_CONSOLE_ENGINE
"""

import argparse
import csv
import json
import math
import os
from pathlib import Path
import queue
import signal
import subprocess
import sys
import tempfile
import threading
import time
import wave
from collections import deque
from contextlib import contextmanager


EDITOR = Path(__file__).resolve().parents[2] / 'editor'
REV, OFFSET, DURATION = 7, 0.125, 8.0
PREFIX = 'DYNA_STAGE '


def check(ok, message):
    if not ok:
        raise AssertionError(message)


def seconds_at(bar):
    # First bar at 120 BPM (2 seconds per bar); 240 BPM (1 second per bar) from bar 1.
    return OFFSET + min(bar, 1) * 2 + max(0, bar - 1)


def fixture(root):
    audio = root / 'clock.wav'
    with wave.open(str(audio), 'wb') as wav:
        wav.setparams((1, 2, 22050, 0, 'NONE', 'not compressed'))
        wav.writeframes(bytes(int(DURATION * 22050) * 2))  # synthetic mono PCM silence
    def at(bar):
        return {'bar': bar, 'numerator': 0, 'denominator': 1}
    chart = root / 'clock.json'
    chart.write_text(json.dumps({
        'format': 'dynamite-uv-chart', 'formatVersion': 2, 'chartId': 'clock', 'audioOffsetSec': OFFSET,
        'timing': {'bpms': [{'time': at(0), 'bpm': 120}, {'time': at(1), 'bpm': 240}]},
        'notesLeft': [], 'notesCenter': [{'id': 'pulse', 'type': 'tap', 'time': at(1), 'center': 2.5, 'width': 1}],
        'notesRight': [],
    }), encoding='utf-8')
    return chart, audio


def refuse_other_engine(executable):
    # Fail closed if the process list is unavailable. Never stop an existing editor.
    if os.name == 'nt':
        listing = subprocess.check_output(['tasklist', '/FO', 'CSV', '/NH'], timeout=8).decode(errors='replace')
        rows = ((r[1], r[0]) for r in csv.reader(listing.splitlines()) if len(r) >= 2)
    else:
        listing = subprocess.check_output(['ps', '-A', '-o', 'pid=', '-o', 'comm='], timeout=8, text=True)
        rows = (line.strip().split(None, 1) for line in listing.splitlines() if line.strip())
    for pid, name in rows:
        base = Path(name).name.lower()
        if base.startswith(('godot', 'dynamakerstage')) or base == executable.name.lower():
            raise RuntimeError(f'{name} (PID {pid}) is already running; no engine was started')


class Stage:
    def __init__(self, process):
        self.p = process
        self.lines = queue.Queue()
        self.tail = deque(maxlen=10)
        self.last = self.previous = None
        self.loaded = False
        self.snapshot_id = 0
        # Reading a pipe directly on the test thread would make deadlines useless if the
        # engine stopped emitting events. Timestamp on receipt, not when the test consumes it.
        def pump():
            try:
                for line in process.stdout:
                    self.lines.put((time.monotonic(), line.rstrip('\r\n')))
            finally:
                self.lines.put((time.monotonic(), None))
        self.reader = threading.Thread(target=pump, daemon=True)
        self.reader.start()

    def send(self, command, **fields):
        self.p.stdin.write(json.dumps({'command': command, **fields}, allow_nan=False) + '\n')
        self.p.stdin.flush()

    def event(self, deadline):
        while True:
            left = deadline - time.monotonic()
            if left <= 0:
                raise TimeoutError('stage IPC deadline')
            try:
                received, line = self.lines.get(timeout=left)
            except queue.Empty:
                raise TimeoutError('stage IPC deadline') from None
            if line is None:
                raise RuntimeError(f'unexpected engine EOF (exit={self.p.poll()})')
            self.tail.append(line[:640])
            if not line.startswith(PREFIX):
                continue
            msg = json.loads(line[len(PREFIX):])
            if msg['event'] == 'error':
                raise RuntimeError(f'stage reported: {msg}')
            if msg['event'] == 'tick':
                for k, t in [('frame', bool), ('revision', int), ('clockRequest', int), ('playing', bool)]:
                    check(type(msg.get(k)) is t, f'tick missing or invalid {k}: {msg}')
                for k in ('second', 'bar', 'playbackRate'):
                    check(type(msg[k]) in (int, float) and math.isfinite(msg[k]), f'invalid {k}: {msg}')
                if self.loaded:
                    check(msg['revision'] == REV, f'wrong active revision: {msg}')
                    check(abs(seconds_at(msg['bar']) - msg['second']) < 1e-7, f'BPM/offset mismatch: {msg}')
                    check(msg['clockRequest'] >= self.last['clockRequest'], f'stale request after acknowledgement: {msg}')
                    check(not msg['frame'] or msg['playing'], f'idle frame with no drag in this fixture: {msg}')
                    if msg['clockRequest'] == self.last['clockRequest']:
                        check(msg['second'] >= self.last['second'], f'position regressed without seek: {msg}')
                        if not self.last['playing']:
                            check(not msg['playing'] and msg['second'] == self.last['second'],
                                  f'position changed after stop without a new transport request: {msg}')
                self.previous, self.last = self.last, msg
            return received, msg

    def wait(self, predicate, label, timeout=6):
        end = time.monotonic() + timeout
        try:
            while True:
                ts, msg = self.event(end)
                if predicate(msg):
                    return ts, msg
        except TimeoutError:
            raise TimeoutError(f'timed out waiting for {label}') from None

    def boundary(self, request, playing, **match):
        return self.wait(lambda m: m['event'] == 'tick' and not m['frame'] and
                         m['revision'] == REV and m['clockRequest'] == request and m['playing'] == playing and
                         all(m[k] == v for k, v in match.items()), f'boundary {request}, playing={playing}, {match}')[1]

    def snapshot(self):
        self.snapshot_id += 1
        self.send('audioSnapshot', requestId=self.snapshot_id)
        snap = self.wait(lambda m: m['event'] == 'audioSnapshot' and m['requestId'] == self.snapshot_id, 'audio snapshot')[1]
        check(snap['audioLoads'] == 1, f'unexpected audio reload: {snap}')
        return snap

    def advancing(self, request, rate):
        samples = []
        end = time.monotonic() + 5
        while not samples or samples[-1][0] - samples[0][0] < 1.10:
            ts, m = self.event(end)
            if m['event'] != 'tick':
                continue
            check(m['clockRequest'] == request and m['frame'] and m['playing'] and m['playbackRate'] == rate,
                  f'expected ordinary playback frames at {rate}x: {m}')
            check(not samples or m['second'] > samples[-1][1], f'duplicate or backwards playing frame: {m}')
            samples.append((ts, m['second'], m['bar']))
        wall = samples[-1][0] - samples[0][0]
        hz = (len(samples) - 1) / wall
        speed = (samples[-1][1] - samples[0][1]) / wall
        check(hz >= 3 * 4, f'only {hz:.1f} advancing ticks/s; legacy reporting was ~4 Hz')
        check(abs(speed - rate) <= rate * 0.25, f'expected {rate}x, measured {speed:.3f}x')
        print(f'PASS {rate}x: {len(samples)} real frames in {wall:.2f}s ({hz:.1f} Hz, measured {speed:.3f}x)')
        return samples

    def frozen(self, stop):
        first = self.snapshot()
        time.sleep(0.45)  # cover more than an old heartbeat; the reader still drains both streams
        second = self.snapshot()
        for snap in (first, second):
            check(snap['second'] == stop['second'] and (not snap['audioPlaying'] or snap['audioPaused']),
                  f'paused audio/clock moved: {stop} vs {snap}')
        check(first['audioRestarts'] == second['audioRestarts'], 'audio restarted while frozen')
        check(self.last['second'] == stop['second'] and self.last['bar'] == stop['bar'] and not self.last['playing'],
              f'paused authoritative tick drifted: {self.last}')


@contextmanager
def engine(executable):
    refuse_other_engine(executable)
    p = subprocess.Popen([str(executable), '--headless', '--max-fps', '60', '--path', str(EDITOR)],
                         cwd=EDITOR, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                         text=True, encoding='utf-8', errors='replace', bufsize=1, start_new_session=os.name != 'nt')
    stage = Stage(p)
    try:
        yield stage
        stage.send('quit')
        check(p.wait(timeout=6) == 0, f'nonzero Godot exit {p.returncode}')
    except BaseException:
        print('Last engine output:\n' + '\n'.join(stage.tail), file=sys.stderr)
        raise
    finally:
        if p.poll() is None:
            try:
                stage.send('quit')
                p.wait(timeout=3)
            except (OSError, subprocess.TimeoutExpired):
                if p.poll() is None:
                    # The Windows console executable can wrap a child. Limit termination
                    # to the tree rooted at OUR still-open Popen handle, never a process name.
                    if os.name == 'nt':
                        subprocess.run(['taskkill', '/PID', str(p.pid), '/T', '/F'], capture_output=True, timeout=6)
                    else:
                        os.killpg(p.pid, signal.SIGKILL)
                    p.wait(timeout=5)
        p.stdin.close()
        stage.reader.join(timeout=3)
        if not stage.reader.is_alive():
            p.stdout.close()


def smoke(stage, chart, audio):
    stage.wait(lambda m: m['event'] == 'ready', 'Godot ready', timeout=25)
    start_bar = 0.875
    stage.send('load', chart=chart.read_text(encoding='utf-8'), audioPath=str(audio), revision=REV,
               title='Clock smoke', artist='Synthetic', bar=start_bar, selection=[], difficulty='Hard', level=1)
    stage.wait(lambda m: m['event'] == 'tick' and m['revision'] == REV and not m['frame'] and
               not m['playing'] and m['bar'] == start_bar and m['second'] == seconds_at(start_bar), 'loaded clock')
    stage.loaded = True
    stage.snapshot()
    stage.send('playbackRate', multiplier=0.5)
    stage.send('play', playing=True, clockRequest=101)
    stage.boundary(101, True, playbackRate=0.5)
    started = stage.snapshot()
    check(started['audioPlaying'], f'fixture audio did not start: {started}')
    samples = stage.advancing(101, 0.5)
    check(samples[0][2] < 1 < samples[-1][2], 'fixture did not cross BPM change during playback')
    stage.send('playbackRate', multiplier=1.5)
    stage.boundary(101, True, playbackRate=1.5)
    stage.advancing(101, 1.5)
    running = stage.snapshot()
    check(running['audioPlaying'] and running['audioRestarts'] == started['audioRestarts'], 'rate change restarted audio')

    stage.send('play', playing=False, clockRequest=102)
    paused = stage.boundary(102, False)
    prev = stage.previous  # Includes frames already in transit when the pause command was sent.
    check(prev and prev['frame'] and prev['playing'] and
          (prev['second'], prev['bar']) == (paused['second'], paused['bar']), f'pause rebounded: {prev} -> {paused}')
    stage.frozen(paused)
    stage.send('seek', bar=0.25, clockRequest=103)
    back = stage.boundary(103, False)
    check(back['second'] == seconds_at(0.25) < paused['second'] and back['bar'] == 0.25, 'backward seek not authoritative')
    stage.frozen(back)

    stage.send('play', playing=True, clockRequest=104)
    stage.boundary(104, True)
    stage.wait(lambda m: m['event'] == 'tick' and m['frame'] and m['clockRequest'] == 104 and m['bar'] > 0.40,
               'playback past seek target')
    stage.send('seek', bar=0.125, clockRequest=105)
    jumped = stage.boundary(105, True)
    check(jumped['bar'] == 0.125 and jumped['second'] == seconds_at(0.125) < stage.previous['second'],
          'running backward seek did not produce an immediate discontinuity')
    stage.wait(lambda m: m['event'] == 'tick' and m['frame'] and m['clockRequest'] == 105 and m['bar'] > 0.125,
               'playback continues after running backward seek')
    near_end = DURATION - 0.5
    stage.send('seek', bar=1 + near_end - seconds_at(1), clockRequest=106)
    check(stage.boundary(106, True)['second'] == near_end, 'near-end seek did not land exactly')
    eof = stage.boundary(106, False)
    check(eof['second'] == DURATION, f'expected exact PCM end: {eof}')
    stage.frozen(eof)
    print('PASS BPM crossing, pause at last emitted position, backward seek, exact frozen EOF, single audio load')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--godot', required=True, type=Path, help='Godot .NET console executable')
    args = parser.parse_args()
    try:
        executable = args.godot.expanduser().resolve(strict=True)
        with tempfile.TemporaryDirectory(prefix='dynamaker-clock-') as tmp:
            chart, audio = fixture(Path(tmp))
            with engine(executable) as stage:
                smoke(stage, chart, audio)
    except (AssertionError, RuntimeError, ValueError, KeyError, OSError, subprocess.SubprocessError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        return 1
    print('NATIVE CLOCK IPC SMOKE PASS')
    return 0


if __name__ == '__main__':
    sys.exit(main())
