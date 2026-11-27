from pathlib import Path
import random
from tempfile import TemporaryDirectory
import unittest

from PIL import Image

from residency_audit import Asset, audit, merge, replay, seek_sets, sweep


def asset(cost, *spans):
    return Asset(cost, 1, merge(spans))


class ResidencyAuditTests(unittest.TestCase):
    def test_half_open_and_merged_lookahead(self):
        assets = {'a': asset(4, (0, 10), (12, 20)), 'b': asset(8, (10, 12))}
        self.assertEqual(sweep(assets)['peak_count'], 1)
        self.assertEqual(sweep(assets, 2)['peak_bytes'], 12)
        self.assertEqual(merge([(2, 3), (3, 4), (2, 5)]), ((2, 5),))
        self.assertEqual(seek_sets(assets, [10], 0)[0]['current_bytes'], 8)

    def test_trigger_only_sprite_is_not_silently_omitted(self):
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'fixture.osb'
            Image.new('RGB', (1, 1)).save(root / 'ordinary.png')
            source.write_text('[Events]\nSprite,Background,Centre,ordinary.png,320,240\n F,0,0,50,1\n'
                              'Sprite,Background,Centre,trigger.png,320,240\n T,HitSound,0,100\n  F,0,0,20,1\n',
                              encoding='utf-8')
            with self.assertRaisesRegex(ValueError, 'line 5: trigger-dependent residency'):
                audit(source, 100)

    def test_sweep_matches_exhaustive_integer_timeline(self):
        rng = random.Random(1024)
        for _ in range(40):
            assets = {str(i): asset(rng.randrange(1, 32), *((s, s + rng.randrange(1, 10))
                      for s in rng.sample(range(30), 4))) for i in range(10)}
            for ahead in (0, 2, 8):
                states = seek_sets(assets, range(40), ahead)
                expected = max(a['window_bytes'] for a in states)
                self.assertEqual(sweep(assets, ahead)['peak_bytes'], expected)
                self.assertEqual(sweep(assets, ahead)['peak_count'], max(a['window_count'] for a in states))

    def test_deadline_and_reuse_with_one_slot(self):
        assets = {'a': asset(4, (0, 10), (60, 70)), 'b': asset(4, (30, 40))}
        r = replay(assets, 80, 4, 1, 20, 80, tick_ms=1)
        self.assertEqual((r['loads'], r['reloads'], r['missing_ticks']), (3, 1, 0))
        self.assertEqual((r['peak_bytes'], r['peak_slots']), (4, 1))

    def test_slot_limit_independent_of_bytes(self):
        assets = {str(i): asset(4, (400, 450)) for i in range(129)}
        r = replay(assets, 460, 8192, 128, 400, 80, tick_ms=1)
        self.assertEqual(r['peak_slots'], 128)
        self.assertGreater(r['missing_ticks'], 0)
        self.assertEqual(sweep(assets)['peak_count'], 129)

    def test_current_texture_never_evicted_for_prefetch(self):
        assets = {'active': asset(4, (0, 50)), 'next': asset(4, (50, 90))}
        r = replay(assets, 100, 4, 1, 40, 80, tick_ms=1)
        self.assertEqual(r['first_missing_ms'], 50)
        self.assertGreater(r['skipped_admission_attempts'], 0)
        self.assertEqual(r['peak_bytes'], 4)


if __name__ == '__main__':
    unittest.main()
