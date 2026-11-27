"""Synthetic benchmark logs; no Godot, graphics adapter, or real pack needed."""
from decimal import Decimal
import unittest

from profile_windows import NVIDIA_QUERY, validate


def log(*, mode='flat', backdrop='Replace', window_mode='Fullscreen', video_start='0.000',
        duration='12.000000', updates=15, active='True'):
    off = mode == 'off' or mode == 'cached-stage'
    visible = mode in ('on', 'atlas', 'redraw-hits')
    text = f"""chart 'example' notes=311 speed=14
STORYBOARD_BENCH chart=fixture loaded={not off} forced_off={off} max_items={int(visible)} frames=120 fps=120 frame_p95_ms=10
STORYBOARD_GPU chart=fixture target=1920x1080 window=1920x1080 vsync=Disabled renderer=mobile max_fps=0 from_s=4.000 until_s=10.000 gpu_valid=18 viewport_gpu_p50_ms=4 viewport_gpu_p95_ms=6
STORYBOARD_LOGIC from_unix_ms=90000 until_unix_ms=96000 main_p95_ms=5
STORYBOARD_LOAD chart=fixture backdrop={backdrop}
PLAYFIELD_BENCH mode={window_mode} cache=1920x1090
"""
    if mode in ('video', 'video-low'):
        text += f"""STORYBOARD_VIDEO_LOAD start_s={video_start} duration_s={duration}
STORYBOARD_VIDEO_PLAY video_s=0.050 frame_updates=1
STORYBOARD_VIDEO_STATS reason=eos frame_updates=900 window_frame_updates=900 window_active=True
STORYBOARD_VIDEO_STATS reason=bench frame_updates=900 window_frame_updates={updates} window_active={active}
"""
    return text


def check(mode, text, *, fullscreen=False):
    validate(text, 'fixture', mode, '1920x1080', 0, 14, Decimal(4), Decimal(10), fullscreen=fullscreen)


class ProfileWindowTests(unittest.TestCase):
    def test_require_real_fullscreen_in_standard_and_playfield_cases(self):
        for mode in ('off', 'flat', 'on', 'cached-stage', 'video'):
            with self.subTest(mode=mode):
                check(mode, log(mode=mode, window_mode='ExclusiveFullscreen'), fullscreen=True)
                check(mode, log(mode=mode, window_mode='Windowed'))
                with self.assertRaisesRegex(ValueError, 'did not report fullscreen'):
                    check(mode, log(mode=mode, window_mode='Windowed'), fullscreen=True)

    def test_flat_and_both_video_controls_require_replacement(self):
        for mode in ('flat', 'video', 'video-low'):
            with self.subTest(mode=mode):
                check(mode, log(mode=mode))
                with self.assertRaisesRegex(ValueError, 'replaced stock backdrop'):
                    check(mode, log(mode=mode, backdrop='Keep'))
        check('on', log(mode='on', backdrop='Keep'))

    def test_video_submissions_must_be_inside_an_active_covered_window(self):
        for arguments, error in [
            (dict(updates=14), 'inside the measurement window'),
            (dict(active='False'), 'both measurement boundaries'),
            (dict(duration='9.999999'), 'does not cover'),
            (dict(video_start='4.001'), 'does not cover'),
        ]:
            with self.subTest(arguments=arguments), self.assertRaisesRegex(ValueError, error):
                check('video', log(mode='video', **arguments))
        valid = log(mode='video')
        check('video', valid)
        with self.assertRaisesRegex(ValueError, 'exactly one STORYBOARD_VIDEO_STATS'):
            check('video', valid + valid.splitlines()[-1] + '\n')

    def test_atlas_requires_the_historical_backend_to_report_packed_textures(self):
        text = log(mode='atlas')
        with self.assertRaisesRegex(ValueError, 'pre-streaming build'):
            check('atlas', text)
        check('atlas', text + 'STORYBOARD_ATLAS textures=3\n')

    def test_nvidia_csv_rows_have_a_stable_adapter_identifier(self):
        self.assertEqual(NVIDIA_QUERY.partition('=')[2].split(',')[:2], ['timestamp', 'uuid'])


if __name__ == '__main__':
    unittest.main()
