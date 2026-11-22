"""Parameter and stage frame-contract regressions; launches neither Godot nor FFmpeg."""
import argparse
import json
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

import record
from video_encoding import VideoEncoding


class RecordingOptionsTests(unittest.TestCase):
    @staticmethod
    def options(preview, hold):
        return record.CaptureOptions(record.seconds(preview), record.seconds(hold))

    def test_seconds_validation_and_frame_rounding(self):
        for text in ('-1', '60.0001', 'NaN', 'Infinity', 'text'):
            with self.subTest(text=text), self.assertRaises(argparse.ArgumentTypeError):
                record.seconds(text)
        options = self.options('3.5', '1.125')
        self.assertEqual((options.preview_frames, options.hold_frames), (210, 68))
        self.assertEqual(self.options('60', '60').preview_frames, 300)

    def test_configured_durations_and_trim(self):
        frames = {'expand': 90, 'preview-playing': 95, 'start': 305,
                  'music-ended': 1500, 'result-intro': 1501,
                  'result-revealed': 1700, 'result-held': 1940}
        options = self.options('3.5', '4')
        self.assertEqual(record.start_frame(options, frames), 78)
        for key, delta in [('start', 2), ('result-held', -2), ('music-ended', 2)]:
            with self.subTest(key=key), self.assertRaises(RuntimeError):
                record.start_frame(options, frames | {key: frames[key] + delta})

    def test_zero_preview_keeps_handoff_and_zero_hold_keeps_reveal(self):
        frames = {'start': 90, 'handoff': 110, 'gameplay-music': 200,
                  'music-ended': 1400, 'result-intro': 1401,
                  'result-revealed': 1600, 'result-held': 1600}
        options = self.options('0', '0')
        self.assertEqual(record.start_frame(options, frames), 110)
        for invalid in ({'preview-playing': 92}, {'handoff': 200}, {'result-revealed': 1401}):
            with self.subTest(invalid=invalid), self.assertRaises(RuntimeError):
                record.start_frame(options, frames | invalid)


class VideoEncodingTests(unittest.TestCase):
    def test_speed_mappings_and_unchanged_h264_default(self):
        expected = {
            'h264': ('libx264', 'avc1', 16, ('slow', 'fast', 'veryfast')),
            'hevc': ('libx265', 'hvc1', 20, ('slow', 'medium', 'veryfast')),
            'av1': ('libsvtav1', 'av01', 24, ('4', '7', '9')),
        }
        self.assertEqual(VideoEncoding(), VideoEncoding('x264', 'medium', 16))
        self.assertEqual(VideoEncoding('x265').codec, 'hevc')
        for codec, (encoder, tag, crf, presets) in expected.items():
            for speed, native in zip(record.SPEEDS, presets):
                with self.subTest(codec=codec, speed=speed):
                    encoding = VideoEncoding(codec, speed)
                    self.assertEqual((encoding.spec.encoder, encoding.spec.tag, encoding.crf,
                                      encoding.native_preset), (encoder, tag, crf, native))

    def test_crf_validation_is_codec_specific(self):
        self.assertEqual(VideoEncoding('av1', crf=63).crf, 63)
        for codec, crf in [('h264', 52), ('hevc', -1), ('av1', 0), ('av1', 64)]:
            with self.subTest(codec=codec, crf=crf), self.assertRaisesRegex(ValueError, '--crf'):
                VideoEncoding(codec, crf=crf)

    def test_missing_encoder_cannot_fall_back_to_different_encoder(self):
        with patch('video_encoding.subprocess.run') as run:
            run.return_value.stdout = ' V....D av1_nvenc Hardware\n V....D libsvtav1_alt Alternate\n'
            with self.assertRaisesRegex(RuntimeError, 'libsvtav1'):
                VideoEncoding('av1').require_encoder('ffmpeg')
            run.return_value.stdout += ' V..... libsvtav1 AV1\n'
            VideoEncoding('av1').require_encoder('ffmpeg')

    def test_encode_passes_native_preset_not_alias(self):
        with patch('video_encoding.subprocess.run') as run:
            VideoEncoding('av1', 'high', 27).encode('ffmpeg', Path('input.avi'), Path('output.mp4'), 30, 60)
        command = run.call_args.args[0]
        for key, value in (('-preset', '9'), ('-c:v', 'libsvtav1'), ('-tag:v', 'av01'),
                           ('-ss', '0.500000000'), ('-crf', '27'), ('-c:a', 'aac')):
            self.assertEqual(command[command.index(key) + 1], value)

    def test_validate_requires_real_codec_and_full_decode(self):
        video = dict(codec_type='video', codec_name='hevc', codec_tag_string='hvc1', width=1920, height=1080,
                     pix_fmt='yuv420p', color_range='tv', r_frame_rate='60/1', duration='30.0')
        audio = dict(codec_type='audio', codec_name='aac', sample_rate='48000', channels=2, duration='30.0')
        with patch('video_encoding.subprocess.run') as run:
            run.return_value = subprocess.CompletedProcess([], 0, stdout=json.dumps({'streams': [video, audio]}))
            VideoEncoding('hevc').verify('ffmpeg', 'ffprobe', Path('output.mp4'), 60)
            self.assertEqual(run.call_count, 2)
            self.assertIn('-xerror', run.call_args.args[0])
            with self.assertRaisesRegex(RuntimeError, 'Unexpected MP4 video format'):
                VideoEncoding('av1').verify('ffmpeg', 'ffprobe', Path('output.mp4'), 60)
            self.assertEqual(run.call_count, 3)


if __name__ == '__main__':
    unittest.main()
