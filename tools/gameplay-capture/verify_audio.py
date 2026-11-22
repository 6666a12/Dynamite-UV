"""Check the synthetic recording's actual audio against the source waveform and frame marks."""
from __future__ import annotations

import argparse
import array
import json
import math
import subprocess
import wave
from pathlib import Path

from recording_fixture import PREVIEW_START, RATE

FPS = 60


def left_pcm(data: bytes) -> array.array:
    return array.array('h', data)[::2]


def compare(source, captured, source_sec: float, observed_sec: float) -> dict:
    start = round(source_sec * RATE)
    origin = round(observed_sec * RATE)
    span = RATE // 20  # ±50 ms accommodates the frame at which a player is first observed.
    end = RATE // 10
    step = 12
    reference = source[start:start + end:step]
    ref_energy = sum(v * v for v in reference)
    best = (-1.0, 0)
    for lag in range(-span, span + 1):
        sample = captured[origin + lag:origin + lag + end:step]
        energy = sum(v * v for v in sample)
        if energy == 0 or len(reference) != len(sample):
            continue
        similarity = sum(a * b for a, b in zip(reference, sample)) / math.sqrt(ref_energy * energy)
        if similarity > best[0]:
            best = (similarity, lag)
    if best[0] < .98:
        raise RuntimeError(f'No matching waveform at {observed_sec:.3f} s (correlation {best[0]:.3f})')
    return dict(lag_ms=round(1000 * best[1] / RATE, 3), correlation=round(best[0], 5))


def verify(run: Path) -> dict:
    metadata = json.loads((run / 'metadata.json').read_text(encoding='utf-8'))
    manifest = json.loads((run / 'manifest.json').read_text(encoding='utf-8'))
    marks = metadata['recording']['frames']
    trim_frame = metadata['trimFrame']
    source_path = Path(manifest['user']) / 'charts' / 'synthetic-pulse' / 'audio.wav'
    with wave.open(str(source_path), 'rb') as stream:
        source = left_pcm(stream.readframes(stream.getnframes()))
    captured = left_pcm(subprocess.check_output(['ffmpeg', '-nostdin', '-v', 'error', '-i', str(run / 'synthetic-pulse.mp4'),
                                                  '-map', '0:a:0', '-c:a', 'pcm_s16le', '-f', 's16le', '-']))

    def at(mark):
        return (marks[mark] - trim_frame) / FPS

    windows = [('gameplay_early', 'gameplay-music', 0, .35),
               ('gameplay_late', 'gameplay-music', 0, 16.35)]
    preview_length = at('start') - at('preview-playing') if 'preview-playing' in marks else 0
    if preview_length >= .22:
        windows.insert(0, ('preview', 'preview-playing', PREVIEW_START, min(.35, preview_length / 3)))
    matches = {name: compare(source, captured, source_sec + elapsed, at(mark) + elapsed)
               for name, mark, source_sec, elapsed in windows}
    drift = abs(matches['gameplay_early']['lag_ms'] - matches['gameplay_late']['lag_ms'])
    if drift > 2:
        raise RuntimeError(f'Gameplay music drifted by {drift:.3f} ms across the sampled windows')
    silence = {}
    handoff_start = max(0., at('start') + .20)
    for name, start, end in (
        ('handoff', handoff_start, at('gameplay-music') - .15),
        ('result', at('result-intro') + .40, at('result-held')),
    ):
        segment = captured[round(start * RATE):round(end * RATE)]
        peak = max((abs(v) for v in segment), default=32768)
        if peak > 32:
            raise RuntimeError(f'Unexpected sound leaking into {name}: peak {peak}/32768')
        silence[name] = round(20 * math.log10(max(peak, 1) / 32768), 2)
    return dict(waveform=matches, gameplay_drift_ms=round(drift, 3), quiet_peak_dbfs=silence)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('run', type=Path)
    directory = parser.parse_args().run
    evidence = json.dumps(verify(directory), indent=2) + '\n'
    (directory / 'audio-evidence.json').write_text(evidence, encoding='utf-8')
    print(evidence, end='')
