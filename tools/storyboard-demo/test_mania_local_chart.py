"""Synthetic regression checks; no third-party chart or media fixtures are read."""
from contextlib import redirect_stdout
from fractions import Fraction
from io import StringIO
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

from mania_local_chart import (
    FLAC_INPUT_BYTES, FLAC_PCM_BYTES, Hit, flac_duration_for_game, make_chart, media_ms, media_time, parse_hit,
    parse_osu, print_audit, probe_audio, same_column_conflicts,
)


def stream_info(samples, *, rate=44_100, channels=2, depth=16):
    audio = (rate << 44) | ((channels - 1) << 41) | ((depth - 1) << 36) | samples
    return (b'fLaC\x80\x00\x00\x22' + b'\x10\x00' * 2 + bytes(6) + audio.to_bytes(8, 'big') + bytes(16))


class ManiaLocalChartTests(unittest.TestCase):
    def test_column_boundaries_and_object_masks(self):
        xs = [0, 127, 128, 255, 256, 383, 384, 511]
        self.assertEqual([parse_hit(f'{x},192,1001,5,0', 1).column for x in xs], [0, 0, 1, 1, 2, 2, 3, 3])
        self.assertEqual(parse_hit('384,192,1001,132,0,1049:0:0:0:0:', 3), Hit(3, 3, 1001, 1049))

    def test_reject_bad_or_unsupported_objects_with_line_number(self):
        for bad in ('512,192,0,1,0', '-1,192,0,1,0', '0,192,0.1,1,0', '0,192,0,2,0',
                    '0,192,0,129,0,10:', '0,192,1,128,0,1:', '0,192,1,128,0,3'):
            with self.subTest(bad=bad), self.assertRaisesRegex(ValueError, r'\[HitObjects\] line 9:'):
                parse_hit(bad, 9)

    def test_bar_boundaries_and_chord_roundtrip(self):
        for moment in (0, 1, 1599, 1600, 1601, 10001):
            self.assertEqual(media_ms(media_time(moment)), Fraction(moment))
        self.assertEqual(media_time(1600), {'bar': 1, 'numerator': 0, 'denominator': 1})
        chart = make_chart([Hit(1, 0, 1599, 1601), Hit(2, 3, 1599)])
        self.assertEqual(chart['notesLeft'], chart['notesRight'])
        hold, tap = chart['notesCenter']
        self.assertEqual(hold['time'], tap['time'])
        self.assertEqual([hold['center'], tap['center']], [0.625, 4.375])
        self.assertEqual(hold['nodes'][0]['time'], {'bar': 1, 'numerator': 1, 'denominator': 1600})
        self.assertTrue(hold['nodes'][0]['judge'])

    def test_same_column_pairs_separate_interiors_ends_and_chords(self):
        hits = [Hit(1, 0, 10, 20), Hit(2, 0, 10), Hit(3, 3, 10), Hit(4, 0, 12),
                Hit(5, 0, 13, 17), Hit(6, 3, 15, 25), Hit(7, 0, 20)]
        self.assertEqual([(kind, h1.line, h2.line) for kind, h1, h2 in same_column_conflicts(hits)],
                         [('duplicate-head', 1, 2), ('tap-inside-hold', 1, 4),
                          ('overlapping-holds', 1, 5), ('tail-touches-head', 1, 7)])

    def test_half_open_window_and_pending_holds(self):
        hits = [Hit(1, 0, 5, 10), Hit(2, 1, 10, 20), Hit(3, 2, 19), Hit(4, 3, 20)]
        with redirect_stdout(StringIO()) as out:
            last = print_audit(hits, (10, 20))
        self.assertEqual(last, 20)
        self.assertIn('Window [10, 20) ms: 1 taps, 1 hold heads, 1 hold tails, 3 judge units', out.getvalue())
        self.assertIn('tails exactly at start): 1; holds still pending at exclusive end: 1', out.getvalue())

    def test_game_flac_limits_use_exact_pcm_bytes_before_ffprobe(self):
        with TemporaryDirectory() as temporary:
            audio = Path(temporary) / 'example.flac'
            samples = FLAC_PCM_BYTES // 4
            audio.write_bytes(stream_info(samples))
            self.assertEqual(flac_duration_for_game(audio), Fraction(samples, 44_100))
            audio.write_bytes(stream_info(samples + 1))
            with patch('mania_local_chart.shutil.which', side_effect=AssertionError('probed oversized FLAC')):
                with self.assertRaisesRegex(ValueError, 'decoded PCM exceeds'):
                    probe_audio(audio)
            audio.write_bytes(stream_info(44_100))
            with audio.open('ab') as stream:
                stream.truncate(FLAC_INPUT_BYTES + 1)
            with self.assertRaisesRegex(ValueError, 'no larger than 64 MiB'):
                flac_duration_for_game(audio)

    def test_game_flac_rejects_unverifiable_and_unsupported_metadata(self):
        with TemporaryDirectory() as temporary:
            audio = Path(temporary) / 'example.flac'
            for data, message in [(stream_info(0), 'declare its sample count'),
                                  (stream_info(1, rate=384000), 'unsupported by game'),
                                  (stream_info(1, depth=32), 'unsupported by game')]:
                with self.subTest(message=message), self.assertRaisesRegex(ValueError, message):
                    audio.write_bytes(data)
                    flac_duration_for_game(audio)
            id3 = b'ID3\x04\x00\x10\x00\x00\x00\x00' + bytes(10)
            audio.write_bytes(id3 + stream_info(32_001, rate=32_000, channels=1, depth=24))
            self.assertEqual(flac_duration_for_game(audio), Fraction(32_001, 32_000))

    def test_bom_and_order_with_minimal_synthetic_beatmap(self):
        with TemporaryDirectory() as temporary:
            osu = Path(temporary) / 'fixture.osu'
            osu.write_text('osu file format v14\n[General]\nMode: 3\n[Difficulty]\nCircleSize:4\n'
                           '[Metadata]\nTitle:Twin Bloom\nVersion:Ramification\nBeatmapID:5893787\n'
                           '[TimingPoints]\nthis intentionally is not parsed\n'
                           '[HitObjects]\n448,192,101,1,0\n0,192,101,1,0\n', encoding='utf-8-sig')
            _, hits = parse_osu(osu)
            self.assertEqual([hit.column for hit in hits], [0, 3])
            osu.write_text('osu file format v14\n[General]\nMode:0\n[Difficulty]\nCircleSize:4\n', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'Mode:3'):
                parse_osu(osu)


if __name__ == '__main__':
    unittest.main()
