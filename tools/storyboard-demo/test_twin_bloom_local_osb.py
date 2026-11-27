"""Synthetic regression only; no licensed fixture data."""

from collections import Counter
from contextlib import redirect_stdout
from io import StringIO
import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from PIL import Image

from twin_bloom_local_osb import (
    Command, Sprite, WorkBudget, build, easing, expand, frame_spans, inspect_textures, numeric_keys, parse, write_texture,
)


class LocalStoryboardTests(unittest.TestCase):
    def test_loop_uses_max_local_end_and_preserves_preceding_values(self):
        text = """[Events]
Video,0,"unused.mp4"
Sprite,Background,Centre,"test.png",320,240
 L,100,5
  F,0,10,20,0,1
  M,0,20,40,10,20,30,40
"""
        (sprite,), ignored = parse(text)
        first, last, commands, triggers = expand(sprite, 225, 267)
        self.assertEqual((first, last), (110, 300))
        self.assertEqual(ignored, {"Video": 1})
        self.assertEqual(triggers, [])
        self.assertEqual([c.start for c in commands if c.kind == "F"], [190, 230, 270])
        fades = [c for c in commands if c.kind == "F"]
        keys = numeric_keys(fades, 225, 267, tuple, .001, Counter())
        self.assertEqual(keys, [(225, (1,), "step"), (230, (0,), "linear"), (240, (1,), "step")])

    def test_same_time_setup_and_boundary_jump_are_not_blended_away(self):
        fades = [Command("F", 0, 100, 100, (.2,), (.2,), 1),
                 Command("F", 0, 100, 200, (1,), (0,), 2),
                 Command("F", 0, 200, 250, (.5,), (1,), 3)]
        report = Counter()
        keys = numeric_keys(fades, 100, 260, tuple, .001, report)
        self.assertEqual([(t, ease) for t, _, ease in keys], [(100, "linear"), (199, "step"),
                                                               (200, "linear"), (250, "step")])
        self.assertAlmostEqual(keys[1][1][0], .01)
        self.assertEqual(report["submillisecond_jump_guards"], 1)

    def test_expo_fit_retains_partial_curve_not_a_restarted_ease(self):
        expo = Command("M", 19, 1000, 2000, (0,), (100,), 1)
        report = Counter()
        keys = numeric_keys([expo], 1300, 1880, tuple, .03, report)
        self.assertEqual(report["fit_ease_19"], 1)
        self.assertAlmostEqual(keys[0][1][0], 87.5)
        self.assertAlmostEqual(keys[-1][1][0], 100 * easing(19, .88))
        for (a, (v,), _), (b, (w,), _) in zip(keys, keys[1:]):
            for ms in range(a, b):
                self.assertAlmostEqual(v + (w - v) * (ms - a) / (b - a),
                                       100 * easing(19, (ms - 1000) / 1000), delta=.031)

    def test_animation_retains_phase_and_once_holds(self):
        sprite = Sprite(1, "Background", "Centre", "frames/f.png", 0, 0, 3, 200, "LoopForever", [])
        self.assertEqual(frame_spans(sprite, 1000, 1553, 1870),
                         [("frames/f2.png", 47), ("frames/f0.png", 200), ("frames/f1.png", 70)])
        sprite.mode = "LoopOnce"
        self.assertEqual(frame_spans(sprite, 1000, 1375, 1999), [("frames/f1.png", 25), ("frames/f2.png", 599)])

    def test_animation_only_enumerates_representable_frames(self):
        sprite = Sprite(1, "Background", "Centre", "f.png", 0, 0, 10**30 + 7, 1e-12, "LoopForever", [])
        self.assertEqual(frame_spans(sprite, 10, 10, 13),
                         [("f0.png", 1), ("f1000000000000.png", 1), ("f2000000000000.png", 1)])
        with self.assertRaisesRegex(ValueError, "animation span work budget"):
            frame_spans(sprite, 10, 10, 13, WorkBudget(2, "animation span"))
        sprite.mode = "LoopOnce"
        sprite.frames = 10**12
        self.assertEqual(frame_spans(sprite, 10, 10, 13), [("f0.png", 1), ("f999999999999.png", 2)])
        sprite.delay, sprite.frames, sprite.mode = .1, 20, "LoopForever"
        self.assertEqual(frame_spans(sprite, 10, 11, 13), [("f10.png", 1), ("f0.png", 1)])
        with self.assertRaisesRegex(ValueError, "invalid animation"):
            parse('[Events]\nAnimation,Background,Centre,"f.png",320,240,2,inf')

    def test_loop_budget_checked_before_expansion_and_shared_between_sprites(self):
        (sprite,), _ = parse('[Events]\nSprite,Background,Centre,a.png,0,0\n L,0,9000000\n'
                             + '  F,0,0,1,0,1\n' * 6)
        with patch('twin_bloom_local_osb.replace', side_effect=AssertionError('copied loop child')):
            with self.assertRaisesRegex(ValueError, 'expanded command work budget'):
                expand(sprite, 0, 20_000)
            self.assertIsNone(expand(sprite, 10_000_000, 10_020_000))
        (one, two), _ = parse('[Events]\n' + ('Sprite,Background,Centre,a.png,0,0\n F,0,0,1,0,1\n' * 2))
        budget = WorkBudget(1, 'expanded command')
        self.assertIsNotNone(expand(one, 0, 2, budget))
        with self.assertRaisesRegex(ValueError, 'expanded command work budget'):
            expand(two, 0, 2, budget)

    def test_dimension_and_cumulative_decode_checks_precede_rgba_allocation(self):
        with TemporaryDirectory() as work:
            root = Path(work).resolve()
            Image.new('RGB', (4097, 1)).save(root / 'wide.png')
            with patch.object(Image.Image, 'convert', side_effect=AssertionError('decoded oversized file')):
                with self.assertRaisesRegex(ValueError, 'texture dimension exceeds 4096'):
                    inspect_textures(root, ['wide.png'], None)
            Image.new('RGB', (2, 2)).save(root / 'a.png')
            (root / 'b.png').write_bytes((root / 'a.png').read_bytes())
            with patch('twin_bloom_local_osb.MAX_SOURCE_DECODE_BYTES', 16):
                with self.assertRaisesRegex(ValueError, 'b.png: source RGBA decode work budget'):
                    inspect_textures(root, ['a.png', 'b.png'], None)

    def test_jpeg_named_png_is_transcoded_and_real_png_named_jpeg_is_preserved(self):
        with TemporaryDirectory() as work:
            root = Path(work).resolve()
            Image.new('RGB', (2, 2), 'red').save(root / 'jpg.png', format='JPEG')
            Image.new('RGBA', (2, 2), (255, 255, 255, 15)).save(root / 'png.jpg', format='PNG')
            by_path, _ = inspect_textures(root, ['jpg.png', 'png.jpg'], None)
            jpg_output, png_output = root / 'jpeg-generated.png', root / 'png-generated.png'
            write_texture(by_path['jpg.png'], jpg_output)
            write_texture(by_path['png.jpg'], png_output)
            with Image.open(jpg_output) as image:
                self.assertEqual(image.format, 'PNG')
                self.assertEqual(image.mode, 'RGB')
            self.assertEqual(png_output.read_bytes(), (root / 'png.jpg').read_bytes())

    def test_converter_default_does_not_apply_the_historical_preview_count(self):
        with TemporaryDirectory() as work, redirect_stdout(StringIO()):
            root = Path(work).resolve()
            chart = root / 'base'
            chart.mkdir()
            (chart / 'meta.json').write_text(json.dumps({
                'format': 'dynamite-uv-pack', 'formatVersion': 2, 'audio': 'audio.mp3',
                'charts': [{'id': 'ramification', 'file': 'chart.json'}],
            }), encoding='utf-8')
            (chart / 'audio.mp3').write_bytes(b'placeholder audio bytes')
            (chart / 'chart.json').write_text('{}', encoding='utf-8')
            lines = ['[Events]']
            for i in range(129):
                name = f'frame-{i:03d}.png'
                Image.new('RGB', (1, 1), (i, 0, 0)).save(root / name)
                lines.extend([f'Sprite,Background,Centre,{name},320,240', f' F,0,{i},{i},1'])
            source = root / 'fixture.osb'
            source.write_text('\n'.join(lines), encoding='utf-8')
            output = root / 'streaming'
            build(source, chart, output, 0, 129, None, False)
            report = json.loads((output / 'storyboard/conversion-report.json').read_text(encoding='utf-8'))
            self.assertEqual(report['unique_textures'], 129)
            self.assertEqual(report['texture_asset_limit'], 1024)
            self.assertIsNone(report['texture_pixel_limit'])
            preview = root / 'preview'
            with self.assertRaisesRegex(ValueError, 'assets 129 > 128'):
                build(source, chart, preview, 0, 129, None, False, preview_budget=True)
            self.assertFalse(preview.exists())

    def test_matching_assets_share_one_texture_and_copy_native_png_alpha(self):
        with TemporaryDirectory() as work:
            root = Path(work).resolve()
            image = Image.new("RGBA", (3, 1))
            image.putdata([(251, 125, 25, 0), (251, 125, 25, 17), (12, 3, 4, 255)])
            image.save(root / "a.png")
            image.save(root / "b.png", compress_level=1)
            by_path, unique = inspect_textures(root, ["a.png", "b.png"], None)
            self.assertIs(by_path["a.png"], by_path["b.png"])
            self.assertEqual(unique[0].alpha, (1, 1, 1))
            output = root / "stored.png"
            write_texture(unique[0], output)
            self.assertEqual(output.read_bytes(), (root / "a.png").read_bytes())


if __name__ == "__main__":
    unittest.main()
