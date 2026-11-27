"""Generate the Prism Relay fixture pack; never replace files in an existing pack."""

import argparse
from fractions import Fraction
import json
from pathlib import Path
import stat

from artwork import write_art
from music import DURATION_MS, FINAL_BEAT, OFFSET, SWITCH_BEAT, TEMPOS, media_time, write_music
from storyboard import STRESS_COUNTS, demo_script, stress_script, title_script

DEFAULT_OUTPUT = Path(__file__).resolve().parents[2] / '.tmp' / 'storyboard-demo' / 'prism-relay'
LANES = ('Left', 'Center', 'Right')


def bartime(beat):
    t = Fraction(beat) / 4
    bar, numerator = divmod(t.numerator, t.denominator)
    return {'bar': bar, 'numerator': numerator, 'denominator': t.denominator}


def chart_data():
    notes = {lane: [] for lane in LANES}
    held = {lane: [] for lane in LANES}
    slots = set()

    def point(lane, beat, center, width):
        # All authored times land on the sixteenth grid. IDs only encode this
        # grid for convenient inspection, not to imply a different time model.
        step = int(beat * 4)
        return {'id': f'{lane.lower()}-{step:03}', 'time': bartime(beat), 'center': center, 'width': width}

    def note(lane, beat, center, kind='tap', width=0.92):
        if (lane, beat) in slots or any(start <= beat <= end for start, end in held[lane]):
            return
        notes[lane].append((beat, {**point(lane, beat, center, width), 'type': kind}))
        slots.add((lane, beat))

    def sustain(kind, lane, start, values, curves):
        # values: (relative beat, center, width); all paths are on just one lane.
        points = [point(lane, start + offset, center, width) for offset, center, width in values]
        for p, curve in zip(points, curves):
            p['curveToNext'] = curve
        head, *nodes = points
        if kind == 'hold':
            for i, node in enumerate(nodes):
                node['judge'] = i % 2 == 1 or i == len(nodes) - 1
        notes[lane].append((start, {**head, 'type': kind, 'nodes': nodes}))
        held[lane].append((start, start + values[-1][0]))

    sustain('hold', 'Center', 9, ((0, 1.23, .95), (1, 1.5, .89), (2, 2.1, 1.06), (3, 2.55, 1.13)),
            ('smooth', 'smooth', 'smooth'))
    sustain('mixer', 'Right', 20, ((0, 1.38, .88), (1, 2.58, .80), (2, 3.55, .92),
                                (3, 2.61, .82), (4, 1.42, .96), (5, 2.25, 1.02)), ('smooth',) * 5)
    sustain('mixer', 'Left', 32, ((0, 3.47, .87), (1, 2.48, .86), (2, 1.44, .86),
                               (3, 3.03, .90), (4, 2.24, 1.08)), ('smooth',) * 4)
    sustain('hold', 'Center', 37, ((0, 3.1, 1), (1, 3.7, .97), (2, 2.9, .94), (3, 2.6, 1.12)),
            ('easeInOutSine', 'easeInOutSine', 'easeOutQuad'))

    opening = (('Center', 2.5), ('Left', 1.8), ('Right', 2.25), ('Center', 1.35),
               ('Right', 2.9), ('Center', 3.55), ('Left', 2.82), ('Center', 2.2))
    for i, beat in enumerate((3, 4, 5, 6, 7, 8, Fraction(17, 2), 9, 10, 11,
                              12, Fraction(25, 2), 13, 14, Fraction(29, 2), 15, 16, Fraction(33, 2))):
        lane, x = opening[i % len(opening)]
        if any(start <= beat <= end for start, end in held[lane]):
            lane = 'Left' if i % 2 else 'Right'
        note(lane, beat, x, 'exTap' if i in (4, 15) else 'tap')
    note('Left', 8, 1.65)
    note('Right', 13, 2.67)

    # Alternating eighths; short sixteenth fan-outs and sync chords mark the accents.
    # During a sustain the active lane is reserved, so dense runs don't require
    # releasing the mixer or a scored hold to press a second note on that rail.
    centers = (1.25, 2.5, 3.45, 2.0, 3.02, 1.72, 3.62, 2.63)
    order = ('Left', 'Center', 'Right', 'Center', 'Left', 'Right', 'Center', 'Right')
    for step in range(SWITCH_BEAT * 4, FINAL_BEAT * 4, 2):
        i = (step - SWITCH_BEAT * 4) // 2
        beat = Fraction(step, 4)
        lane = order[i % len(order)]
        available = [l for l in (lane, *LANES) if not any(a <= beat <= z for a, z in held[l])]
        lane = available[0]
        note(lane, beat, centers[(i * 3) % len(centers)],
             'exTap' if i % 8 == 0 else 'tap')
        if i % 8 in (0, 7):
            for other in LANES:
                if other != lane:
                    note(other, beat, 2.1 if other == 'Left' else 2.65)
        if 18 <= beat < 38 and i % 8 in (1, 4, 5):
            other = next(l for l in available if l != lane)
            note(other, beat + Fraction(1, 4), centers[(i + 2) % len(centers)], width=.83)
    for lane, x in zip(LANES, (2.3, 2.5, 2.3)):
        note(lane, FINAL_BEAT, x, width=1.1)

    return {
        'audioOffsetSec': float(OFFSET),
        'timing': {'bpms': [{'time': bartime(beat), 'bpm': tempo}
                            for beat, tempo in zip((0, SWITCH_BEAT), TEMPOS)]},
        **{f'notes{lane}': [n for _, n in sorted(notes[lane], key=lambda item: (item[0], item[1]['id']))]
           for lane in LANES},
    }


def put_text(path: Path, text):
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(text)


def put_json(path: Path, value):
    put_text(path, json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n')


def prepare_output(path: Path):
    path.mkdir(parents=True, exist_ok=True)
    entry = path.lstat()
    if (not stat.S_ISDIR(entry.st_mode)
            or getattr(entry, 'st_file_attributes', 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT):
        raise ValueError(f'output must be a real directory, not a link: {path}')
    if any(path.iterdir()):
        raise ValueError(f'output directory is not empty; no files written: {path}')


def generate(path: Path):
    prepare_output(path)
    scripts = path / 'storyboard'
    charts = path / 'charts'
    scripts.mkdir()
    charts.mkdir()
    write_art(path)
    write_music(path / 'audio.wav')

    variants = [('demo-keep', 'DEMO KEEP', demo_script(False)),
                ('demo-replace', 'DEMO REPLACE', demo_script(True)),
                ('title-only', 'A1 TITLE ONLY', title_script())]
    variants.extend((f'stress-{n}', f'STRESS {n}', stress_script(n)) for n in STRESS_COUNTS)
    gameplay = chart_data()
    entries, bindings = [], {}
    for chart_id, label, script in variants:
        entry = f'charts/{chart_id}.json'
        script_name = f'{chart_id}.uvsb'
        put_text(scripts / script_name, script)
        bindings[chart_id] = script_name
        put_json(path / entry, {'format': 'dynamite-uv-chart', 'formatVersion': 2,
                                'chartId': chart_id, **gameplay})
        entries.append({'id': chart_id, 'difficulty': 'custom', 'difficultyKey': label,
                        'level': 10, 'charters': ['Atlas Lab Fixture'], 'file': entry})

    put_json(scripts / 'index.json', {'version': 0, 'charts': bindings})
    put_json(path / 'meta.json', {'format': 'dynamite-uv-pack', 'formatVersion': 2,
                                 'id': 'fixture.prism-relay', 'revision': 1,
                                 'title': 'Prism Relay', 'artist': 'Atlas Lab Fixture',
                                 'audio': 'audio.wav', 'cover': 'cover.png',
                                 'preview': {'startSec': 8.35, 'durationSec': 9.0}, 'charts': entries})
    print(f'Generated strict v2 fixture: {path.resolve()}')
    print(f'Audio: {DURATION_MS / 1000:.3f}s; tempo switch at {float(media_time(SWITCH_BEAT)):.3f}s; '
          f'last hit at {float(media_time(FINAL_BEAT)):.3f}s')
    print('Chart IDs: ' + ', '.join(bindings))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=DEFAULT_OUTPUT, metavar='EMPTY_PACK_DIR',
                        help=f'create the pack in a new or empty directory (default: {DEFAULT_OUTPUT})')
    args = parser.parse_args()
    try:
        generate(args.output)
    except (OSError, ValueError) as error:
        parser.exit(1, f'error: {error}\n')


if __name__ == '__main__':
    main()
