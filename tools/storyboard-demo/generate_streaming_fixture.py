"""Generate a synthetic 20-second, strict v2 pack for local Storyboard streaming tests."""
from __future__ import annotations

import argparse
from fractions import Fraction
import json
from pathlib import Path
import struct
import wave
import zlib

DEFAULT_OUTPUT = Path(__file__).resolve().parents[2] / '.tmp' / 'storyboard-demo' / 'streaming-fixture'
DURATION_MS, RATE = 20_000, 24_000
TILE, ASSET_COUNT = 16, 1024
PRESSURE_DIM, PRESSURE_COUNT = 2048, 8
HANDOFF, OVERFLOW = 'stream-handoff-128', 'stream-overflow-129'
ALIASES, MANY, PRESSURE = 'stream-alias-crop', 'stream-many-1024', 'stream-byte-pressure'
NOTE_TIMES = range(1000, 19_001, 500)


def write_text(path: Path, text: str) -> None:
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(text)


def write_json(path: Path, content: dict) -> None:
    write_text(path, json.dumps(content, indent=2, allow_nan=False) + '\n')


def write_audio(path: Path) -> None:
    # Sparse, low-volume original clicks locate the playhead. Integer synthesis
    # and explicit little-endian PCM make the output repeatable.
    pcm = bytearray(RATE * DURATION_MS // 1000 * 2)
    click_frames, period = 720, 60
    for ms in NOTE_TIMES:
        offset = ms * RATE // 1000
        for i in range(click_frames):
            triangle = period - 4 * abs(i % period - period // 2)
            sample = triangle * (click_frames - i) // 24
            struct.pack_into('<h', pcm, 2 * (offset + i), sample)
    with path.open('xb') as stream, wave.open(stream, 'wb') as sound:
        sound.setparams((1, 2, RATE, 0, 'NONE', 'not compressed'))
        sound.writeframes(pcm)


def png_chunk(kind: bytes, payload: bytes) -> bytes:
    return struct.pack('>I', len(payload)) + kind + payload + struct.pack('>I', zlib.crc32(kind + payload))


def write_png_rows(path: Path, width: int, height: int, rows: bytes) -> None:
    with path.open('xb') as output:
        output.write(b'\x89PNG\r\n\x1a\n' + png_chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)) +
                     png_chunk(b'IDAT', zlib.compress(rows)) + png_chunk(b'IEND', b''))


def write_png(path: Path, width: int, height: int, pixel) -> None:
    rows = b''.join(b'\0' + b''.join(bytes(pixel(x, y)) for x in range(width)) for y in range(height))
    write_png_rows(path, width, height, rows)


def pressure_asset(i: int) -> str:
    return f'pressure-{i:02d}'


def write_pressure_png(path: Path, i: int) -> None:
    color = bytes((48 + 26 * i, 218 - 20 * i, 240 - 16 * i, 255))
    row = b'\0' + color * PRESSURE_DIM
    write_png_rows(path, PRESSURE_DIM, PRESSURE_DIM, row * PRESSURE_DIM)


def tile_pixel(index: int, x: int, y: int) -> tuple[int, int, int, int]:
    if x in (0, TILE - 1) or y in (0, TILE - 1):
        return 236, 244, 255, 255
    if x == y or x + y == TILE - 1:
        return 12, 18, 32, 255
    # Unique colors within the square preserve distinct content as well as paths.
    return 48 + 3 * (index % 64), 76 + 10 * (index // 64), 237 if index < 128 else 100, 255


def sheet_pixel(x: int, y: int) -> tuple[int, int, int, int]:
    if y in (0, 15) or x in (0, 15, 16, 31):
        return 244, 250, 255, 255
    if x < 16:
        return 242, 77 + (24 if (x + y) % 5 == 0 else 0), 49, 255
    return 35, 182 + (44 if (x - y) % 5 == 0 else 0), 239, 255


def asset(i: int) -> str:
    return f'tile-{i:04d}'


def header(indices=()) -> str:
    return ('storyboard 0\nbackdrop mode="replace" color="#101522"\n' +
            ''.join(f'asset "{asset(i)}" file="assets/{asset(i)}.png"\n' for i in indices))


def sprite(name: str, image: str, start: int, end: int, x: int, y: int, scale: float = 4) -> str:
    return (f'sprite "{name}" asset="{image}" clock="media-ms" {{\n'
            f'    active from={start} until={end}\n'
            f'    base "position.x" {x}\n    base "position.y" {y}\n'
            f'    base "scale.x" {scale}\n    base "scale.y" {scale}\n'
            '    base "sampling" "nearest"\n}\n')


def bank(label: str, indices, start: int, end: int) -> str:
    return ''.join(sprite(f'{label}-{j:03d}', asset(i), start, end, 172 + (j % 16) * 105, 145 + (j // 16) * 104)
                   for j, i in enumerate(indices))


def gradient(name: str, start: int, end: int, first: str, last: str) -> str:
    return (f'shape "{name}" kind="rect" width=1160 height=156 clock="media-ms" {{\n'
            f'    active from={start} until={end}\n'
            '    base "position.x" 960\n    base "position.y" 645\n'
            '    linear-gradient from-x=0 from-y=0 to-x=1 to-y=0 {\n'
            f'        stop at=0 color="{first}"\n        stop at=1 color="{last}"\n'
            '    }\n}\n')


def charts_and_scripts():
    first = range(128)
    handoff = (header(range(136)) + bank('initial', first, 2000, 8000) +
               bank('second', range(128, 136), 8000, 10_000) + bank('return', first, 12_000, 19_000))
    overflow = (header(range(129)) + bank('before', range(2), 0, 4000) +
                bank('overflow', range(129), 4000, 8000) + bank('after', range(2), 8000, 19_000))
    aliases = (header() + '''asset "sheet" file="assets/sheet.png"
asset "sheet-alias" file="assets/sheet.png"
region "left" asset="sheet" x=0 y=0 width=16 height=16
region "left-alias" asset="sheet-alias" x=0 y=0 width=16 height=16
region "right" asset="sheet-alias" x=16 y=0 width=16 height=16
''' + sprite('whole-a', 'sheet', 0, 4000, 700, 318, 12) +
               sprite('whole-b', 'sheet-alias', 0, 4000, 1220, 318, 12) +
               sprite('crop-a', 'left', 4000, 8000, 580, 318, 12) +
               sprite('crop-b', 'left-alias', 4000, 8000, 960, 318, 12) +
               sprite('crop-c', 'right', 4000, 8000, 1340, 318, 12) +
               sprite('whole-return', 'sheet-alias', 8000, 12_000, 960, 318, 12) +
               gradient('gradient-a', 4000, 12_000, '#F24D31', '#23B6EF') +
               gradient('gradient-b', 12_000, 19_000, '#63F5C0', '#8038D0') +
               sprite('after-gradient', 'sheet', 16_000, 19_000, 960, 318, 12))
    many = (header(range(ASSET_COUNT)) + bank('head', range(8), 0, 8000) +
            bank('tail', range(ASSET_COUNT - 8, ASSET_COUNT), 8000, 16_000))
    pressure = header(range(2))
    pressure_names = [pressure_asset(i) for i in range(PRESSURE_COUNT)]
    pressure += ''.join(f'asset "{name}" file="assets/{name}.png"\n' for name in pressure_names)
    pressure += ''.join(sprite(f'large-{i}', name, 2000, 8000, 330 + (i % 4) * 420, 300 + (i // 4) * 320, .125)
                        for i, name in enumerate(pressure_names))
    pressure += bank('small-recovery', range(2), 8000, 19_000)
    return ((HANDOFF, 'STREAM 128 HANDOFF', handoff), (OVERFLOW, 'STREAM 129 OVERFLOW', overflow),
            (ALIASES, 'STREAM ALIAS CROP', aliases), (MANY, 'STREAM 1024 DECLARATIONS', many),
            (PRESSURE, 'STREAM BYTE PRESSURE', pressure))


def chart_data(chart_id: str) -> dict:
    notes = {lane: [] for lane in ('notesLeft', 'notesCenter', 'notesRight')}
    for i, ms in enumerate(NOTE_TIMES):
        fraction = Fraction(ms, 2000)  # 120 BPM, 4 beats per bar; offset is zero.
        bar, numerator = divmod(fraction.numerator, fraction.denominator)
        notes[tuple(notes)[i % 3]].append({
            'id': f'tap-{i:02d}', 'type': 'tap', 'center': 2.5, 'width': 1,
            'time': {'bar': bar, 'numerator': numerator, 'denominator': fraction.denominator},
        })
    return {'format': 'dynamite-uv-chart', 'formatVersion': 2, 'chartId': chart_id, 'audioOffsetSec': 0,
            'timing': {'bpms': [{'time': {'bar': 0, 'numerator': 0, 'denominator': 1}, 'bpm': 120}]}, **notes}


def generate(output: Path) -> None:
    output.mkdir(parents=True, exist_ok=False)
    charts, storyboard = output / 'charts', output / 'storyboard'
    charts.mkdir()
    textures = storyboard / 'assets'
    textures.mkdir(parents=True)
    write_audio(output / 'audio.wav')
    for i in range(ASSET_COUNT):
        write_png(textures / f'{asset(i)}.png', TILE, TILE, lambda x, y: tile_pixel(i, x, y))
    write_png(textures / 'sheet.png', 32, 16, sheet_pixel)
    for i in range(PRESSURE_COUNT):
        write_pressure_png(textures / f'{pressure_asset(i)}.png', i)
    entries, bindings = [], {}
    for chart_id, label, script in charts_and_scripts():
        chart_file, script_file = f'charts/{chart_id}.json', f'{chart_id}.uvsb'
        entries.append({'id': chart_id, 'difficulty': 'custom', 'difficultyKey': label, 'unrated': True,
                        'charters': ['Local Synthetic Fixture'], 'file': chart_file})
        bindings[chart_id] = script_file
        write_json(output / chart_file, chart_data(chart_id))
        write_text(storyboard / script_file, script)
    write_json(storyboard / 'index.json', {'version': 0, 'charts': bindings})
    write_json(output / 'meta.json', {'format': 'dynamite-uv-pack', 'formatVersion': 2,
                                    'id': 'fixture.storyboard-streaming', 'revision': 1,
                                    'title': 'Streaming Boundary Lab', 'artist': 'Local Synthetic Fixture',
                                    'audio': 'audio.wav', 'preview': {'startSec': 2, 'durationSec': 8}, 'charts': entries})


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=DEFAULT_OUTPUT, metavar='NEW_PACK_DIR')
    options = parser.parse_args()
    try:
        generate(options.output)
    except (OSError, ValueError) as error:
        parser.exit(1, f'error: {error}\n')
    print(f'Generated 20.000 s synthetic streaming fixture: {options.output.resolve()}')
    print(f'Chart IDs: {HANDOFF}, {OVERFLOW}, {ALIASES}, {MANY}, {PRESSURE}')


if __name__ == '__main__':
    main()
