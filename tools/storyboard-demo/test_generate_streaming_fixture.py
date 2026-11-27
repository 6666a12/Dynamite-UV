"""Verify expected half-open streaming demands and byte-reproducible original pack assets."""
from fractions import Fraction
from hashlib import sha256
import json
from pathlib import Path
import re
from tempfile import TemporaryDirectory
import unittest
import wave

from PIL import Image

from generate_streaming_fixture import (
    ALIASES, ASSET_COUNT, HANDOFF, MANY, NOTE_TIMES, OVERFLOW, PRESSURE, PRESSURE_COUNT, PRESSURE_DIM, RATE, generate,
)

ASSET = re.compile(r'^asset "([^"]+)" file="([^"]+)"$', re.M)
REGION = re.compile(r'^region "([^"]+)" asset="([^"]+)" x=(\d+) y=(\d+) width=(\d+) height=(\d+)$', re.M)
DRAW = re.compile(r'^(sprite|shape) "([^"]+)"([^\n]+) \{\n    active from=(\d+) until=(\d+)$', re.M)


def uses(script, ms):
    assets = {name: (path, None) for name, path in ASSET.findall(script)}
    for name, parent, x, y, width, height in REGION.findall(script):
        assets[name] = assets[parent][0], (x, y, width, height)
    slots, draws, gradients = set(), 0, []
    for kind, name, args, start, end in DRAW.findall(script):
        if int(start) <= ms < int(end):
            draws += 1
            if kind == 'shape':
                gradients.append(name)
            else:
                slots.add(assets[re.search(r'asset="([^"]+)"', args)[1]])
    return len(slots), draws, tuple(gradients)


class StreamingFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        temporary = TemporaryDirectory()
        cls.addClassCleanup(temporary.cleanup)
        cls.pack = Path(temporary.name) / 'fixture'
        generate(cls.pack)
        root = cls.pack / 'storyboard'
        bindings = json.loads((root / 'index.json').read_text(encoding='utf-8'))
        cls.scripts = {id_: (root / script).read_text(encoding='utf-8') for id_, script in bindings['charts'].items()}

    def test_charts_follow_exact_playable_20_second_pcm(self):
        with wave.open(str(self.pack / 'audio.wav'), 'rb') as sound:
            self.assertEqual((sound.getnchannels(), sound.getsampwidth(), sound.getframerate(), sound.getnframes()),
                             (1, 2, RATE, 20 * RATE))
            self.assertTrue(any(sound.readframes(20 * RATE)))
        meta = json.loads((self.pack / 'meta.json').read_text(encoding='utf-8'))
        self.assertEqual(set(self.scripts), {HANDOFF, OVERFLOW, ALIASES, MANY, PRESSURE})
        self.assertEqual(set(self.scripts), {entry['id'] for entry in meta['charts']})
        for entry in meta['charts']:
            with self.subTest(chart=entry['id']):
                chart = json.loads((self.pack / entry['file']).read_text(encoding='utf-8'))
                self.assertEqual(chart['chartId'], entry['id'])
                self.assertEqual(chart['audioOffsetSec'], 0)
                self.assertEqual(chart['timing']['bpms'][0]['bpm'], 120)
                notes = [note['time'] for lane in ('notesLeft', 'notesCenter', 'notesRight') for note in chart[lane]]
                actual = sorted((note['bar'] + Fraction(note['numerator'], note['denominator'])) * 2000 for note in notes)
                self.assertEqual(actual, list(NOTE_TIMES))

    def test_slot_limit_handoff_and_overflow_have_exclusive_boundaries(self):
        self.assertEqual([uses(self.scripts[HANDOFF], t)[:2] for t in (1999, 2000, 7999, 8000, 10_000, 12_000, 19_000)],
                         [(0, 0), (128, 128), (128, 128), (8, 8), (0, 0), (128, 128), (0, 0)])
        self.assertEqual([uses(self.scripts[OVERFLOW], t)[:2] for t in (0, 3999, 4000, 7999, 8000, 19_000)],
                         [(2, 2), (2, 2), (129, 129), (129, 129), (2, 2), (0, 0)])

    def test_alias_crops_and_gradient_handoffs(self):
        script = self.scripts[ALIASES]
        self.assertEqual([uses(script, t) for t in (0, 4000, 8000, 12_000, 16_000, 19_000)],
                         [(1, 2, ()), (2, 4, ('gradient-a',)), (1, 2, ('gradient-a',)),
                          (0, 1, ('gradient-b',)), (1, 2, ('gradient-b',)), (0, 0, ())])
        with Image.open(self.pack / 'storyboard/assets/sheet.png') as sheet:
            self.assertNotEqual(sheet.getpixel((5, 5)), sheet.getpixel((21, 5)))

    def test_many_declarations_only_use_first_and_last_eight(self):
        script = self.scripts[MANY]
        paths = [path for _, path in ASSET.findall(script)]
        self.assertEqual(len(paths), ASSET_COUNT)
        self.assertEqual(len(set(paths)), ASSET_COUNT)
        self.assertEqual([uses(script, t)[:2] for t in (0, 7999, 8000, 16_000)], [(8, 8), (8, 8), (8, 8), (0, 0)])
        referenced = set(re.findall(r'asset="(tile-\d+)"', script))
        self.assertEqual(len(referenced), 16)
        self.assertIn('tile-0000', referenced)
        self.assertIn('tile-1023', referenced)
        digests = set()
        for path in paths:
            file = self.pack / 'storyboard' / path
            digests.add(sha256(file.read_bytes()).digest())
            with Image.open(file) as image:
                self.assertEqual((image.format, image.mode, image.size), ('PNG', 'RGBA', (16, 16)))
                image.load()
        self.assertEqual(len(digests), ASSET_COUNT)

    def test_pressure_reaches_exactly_128_mib_before_small_recovery(self):
        script = self.scripts[PRESSURE]
        assets = ASSET.findall(script)
        self.assertEqual(len(assets), PRESSURE_COUNT + 2)
        self.assertEqual([uses(script, t)[:2] for t in (1999, 2000, 7999, 8000, 18_999, 19_000)],
                         [(0, 0), (8, 8), (8, 8), (2, 2), (2, 2), (0, 0)])
        colors = set()
        byte_sum = 0
        for name, path in assets:
            file = self.pack / 'storyboard' / path
            self.assertLessEqual(file.stat().st_size, 16 << 20)
            if name.startswith('pressure-'):
                with Image.open(file) as image:
                    self.assertEqual((image.mode, image.width, image.height), ('RGBA', PRESSURE_DIM, PRESSURE_DIM))
                    self.assertEqual(image.width * image.height * 4, 16 << 20)
                    self.assertEqual(len(solid := image.getcolors(maxcolors=1)), 1)
                    colors.add(solid[0][1])
                    byte_sum += image.width * image.height * 4
        self.assertEqual(len(colors), PRESSURE_COUNT)
        self.assertEqual(byte_sum, 128 << 20)

    def test_generation_is_byte_identical_and_refuses_to_overwrite(self):
        with self.assertRaises(FileExistsError):
            generate(self.pack)
        with TemporaryDirectory() as temporary:
            copy = Path(temporary) / 'fixture'
            generate(copy)
            files = [f.relative_to(self.pack) for f in self.pack.rglob('*') if f.is_file()]
            self.assertEqual(set(files), {f.relative_to(copy) for f in copy.rglob('*') if f.is_file()})
            for path in files:
                self.assertEqual((self.pack / path).read_bytes(), (copy / path).read_bytes(), str(path))


if __name__ == '__main__':
    unittest.main()
