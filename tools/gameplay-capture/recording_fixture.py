"""Original cover, synthetic audio and strict v2 chart; no external assets."""
from __future__ import annotations

import array
import json
import math
import struct
import wave
import zlib
from pathlib import Path

TITLE = 'Synthetic Pulse'
PACK_ID = 'community.prototype.synthetic-pulse'
OFFSET = 0.125
DURATION = 18.25
PREVIEW_START = 8.125
PREVIEW_DURATION = 5
RATE = 48_000


def write_audio(path: Path) -> None:
    samples = array.array('h')
    roots = (41.20, 49.00, 55.00, 36.71)
    ratios = (4, 6, 8, 12, 10, 8, 6, 9)
    for frame in range(round(RATE * DURATION)):
        t = frame / RATE - OFFSET
        if t < 0:
            samples.extend((0, 0))
            continue
        beat = int(t * 2)
        p = t % .5
        q = t % .25
        root = roots[beat // 4 % len(roots)]
        fade = min(1., (DURATION - OFFSET - t) * 8.)
        attack = min(1., p * 800.)
        kick = .33 * attack * math.exp(-p * 22.) * math.sin(math.tau * (49. * p + 42. * (1 - math.exp(-p * 15.)) / 15.))
        bass = .17 * attack * math.exp(-p * 5.) * (math.sin(math.tau * root * p) + .21 * math.sin(math.tau * root * 2 * p))
        tone = math.tau * root * ratios[int(t * 4) % len(ratios)] * q
        arp = .14 * min(1., q * 240.) * math.exp(-q * 12.) * (math.sin(tone) + .22 * math.sin(2 * tone))
        tick = .055 * min(1., q * 1000.) * math.exp(-q * 105.) * math.sin(42177. * t + 7. * math.sin(33219. * t))
        pan = .25 if int(t * 4) % 2 else -.25
        for side in (-1, 1):
            samples.append(round(32767 * fade * (kick + bass + arp * (1 + side * pan) + tick)))
    with wave.open(str(path), 'wb') as stream:
        stream.setparams((2, 2, RATE, 0, 'NONE', 'not compressed'))
        stream.writeframes(samples.tobytes())


def write_cover(path: Path) -> None:
    size = 384
    raw = bytearray()
    for row in range(size):
        raw.append(0)
        for col in range(size):
            x, y = (col - 192) / 192, (row - 192) / 192
            radius, angle = math.hypot(x, y), math.atan2(y, x)
            orbit = abs(radius - (.62 + .10 * math.cos(angle * 3)))
            echo = abs(radius - (.78 + .05 * math.cos(angle * 3)))
            glow = .89 * math.exp(-orbit * 95) + .15 * math.exp(-orbit * 12) + .30 * math.exp(-echo * 140)
            diamond = math.exp(-abs(abs(x) + abs(y) - .30) * 92)
            grid = (row % 24 == 0 or col % 24 == 0) * max(0., 1. - radius)
            mix = (1 + math.sin(angle + .8)) / 2
            rgb = (9 + glow * (61 + 130 * mix) + 31 * diamond,
                   14 + glow * (185 - 59 * mix) + 164 * diamond + 10 * grid,
                   29 + glow * 204 + 176 * diamond + 18 * grid)
            raw.extend(min(255, round(v)) for v in rgb)

    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 2, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b''))


def at(tick: int) -> dict[str, int]:
    bar, step = divmod(tick, 8)
    divisor = math.gcd(step, 8)
    return dict(bar=bar, numerator=step // divisor, denominator=8 // divisor)


def create_pack(user: Path) -> None:
    directory = user / 'charts' / 'synthetic-pulse'
    directory.mkdir(parents=True, exist_ok=False)
    write_audio(directory / 'audio.wav')
    write_cover(directory / 'cover.png')
    tracks = {key: [] for key in ('Left', 'Center', 'Right')}

    def note(track, kind, tick, center, width, **options):
        tracks[track].append(dict(id=f'{track.lower()}-{kind}-{tick:02}', type=kind,
                                  time=at(tick), center=center, width=width, **options))

    def sustain(track, kind, tick, center, points):
        nodes = [dict(id=f'{kind}-{tick}-{offset}', time=at(tick + offset), center=position, width=.94)
                 for offset, position in points]
        for node in nodes[:-1]:
            node['curveToNext'] = 'smooth'
        if kind == 'hold':
            nodes[-1]['judge'] = True
        note(track, kind, tick, center, .94, curveToNext='smooth', nodes=nodes)

    for bar in range(8):
        k = bar * 8
        for j, (track, center) in enumerate((('Center', 1.35), ('Left', 2.65), ('Right', 3.6), ('Center', 3.15))):
            tick = k + 2 * j
            if tick == 0 or (track == 'Left' and bar in (2, 3)) or (track == 'Right' and bar in (4, 5)):
                continue
            kind = 'exTap' if (bar + j) % 5 == 3 else 'tap'
            note(track, kind, tick, 5 - center if bar % 2 else center, .86)
        if bar in (1, 3, 5, 7):
            note('Center', 'drag', k + 5, 2.1, .69)
        if bar in (2, 4, 6):
            note('Center', 'mine', k + 7, 3.9, .70)
    sustain('Center', 'hold', 5, 2.4, [(4, 3.5), (7, 1.35)])
    sustain('Left', 'mixer', 16, 1.2, [(3, 3.65), (7, 1.45), (12, 2.8)])
    sustain('Right', 'mixer', 32, 3.7, [(4, 1.45), (8, 3.6), (12, 2.35)])
    sustain('Left', 'hold', 44, 1.7, [(5, 3.6), (9, 2.2)])
    note('Left', 'tap', 62, 2.7, 1.04)
    note('Right', 'tap', 62, 2.7, 1.04)

    chart = dict(format='dynamite-uv-chart', formatVersion=2, chartId='showcase', audioOffsetSec=OFFSET,
                 timing={'bpms': [dict(time=at(0), bpm=120)]}, scrollSpeeds=[],
                 **{'notes' + track: sorted(notes, key=lambda n: (n['time']['bar'], n['time']['numerator'] / n['time']['denominator']))
                    for track, notes in tracks.items()})
    pack = dict(format='dynamite-uv-pack', formatVersion=2, id=PACK_ID, revision=1, title=TITLE,
                artist='DynaMaker UV · Original synth', audio='audio.wav', cover='cover.png',
                preview=dict(startSec=PREVIEW_START, durationSec=PREVIEW_DURATION),
                charts=[dict(id='showcase', difficulty='hard', level=8, charters=['UV Recorder'], file='chart.json')])
    for path, value in [(directory / 'chart.json', chart), (directory / 'meta.json', pack),
                        (user / 'settings.json', {'autoEnabled': True, 'motionMode': 'full', 'musicVolume': 90})]:
        path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
