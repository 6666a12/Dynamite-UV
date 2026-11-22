"""MP4 encoding policy and verification for the offline chart recorder."""
from __future__ import annotations

from dataclasses import dataclass
import json
from pathlib import Path
import subprocess

DEFAULT_CODEC = 'h264'
DEFAULT_PRESET = 'medium'
SPEEDS = ('slow', 'medium', 'high')
CODEC_ALIASES = {'x264': 'h264', 'x265': 'hevc', 'h265': 'hevc'}


@dataclass(frozen=True)
class Codec:
    encoder: str
    tag: str
    crf_range: tuple[int, int]
    default_crf: int
    presets: tuple[str, str, str]


CODECS = {
    'h264': Codec('libx264', 'avc1', (0, 51), 16, ('slow', 'fast', 'veryfast')),
    'hevc': Codec('libx265', 'hvc1', (0, 51), 20, ('slow', 'medium', 'veryfast')),
    # SVT-AV1 warns that non-realtime presets 10+ target automation and can show artifacts.
    'av1': Codec('libsvtav1', 'av01', (1, 63), 24, ('4', '7', '9')),
}


@dataclass(frozen=True)
class VideoEncoding:
    codec: str = DEFAULT_CODEC
    preset: str = DEFAULT_PRESET
    crf: int | None = None

    def __post_init__(self) -> None:
        canonical = CODEC_ALIASES.get(self.codec, self.codec)
        if canonical not in CODECS:
            raise ValueError(f'Unknown video codec: {self.codec}')
        if self.preset not in SPEEDS:
            raise ValueError(f'Unknown encoding speed: {self.preset}')
        spec = CODECS[canonical]
        crf = spec.default_crf if self.crf is None else self.crf
        low, high = spec.crf_range
        if not low <= crf <= high:
            raise ValueError(f'--crf for {canonical} must be in {low}..{high}')
        object.__setattr__(self, 'codec', canonical)
        object.__setattr__(self, 'crf', crf)

    @property
    def spec(self) -> Codec:
        return CODECS[self.codec]

    @property
    def native_preset(self) -> str:
        return self.spec.presets[SPEEDS.index(self.preset)]

    def metadata(self) -> dict:
        return {'codec': self.codec, 'encoder': self.spec.encoder, 'codecTag': self.spec.tag,
                'preset': self.preset, 'encoderPreset': self.native_preset, 'crf': self.crf}

    def require_encoder(self, ffmpeg: str) -> None:
        output = subprocess.run([ffmpeg, '-hide_banner', '-encoders'], check=True, capture_output=True,
                                text=True, encoding='utf-8', errors='replace').stdout
        for line in output.splitlines():
            fields = line.split(maxsplit=2)
            if len(fields) >= 2 and len(fields[0]) == 6 and fields[0].startswith('V') and fields[1] == self.spec.encoder:
                return
        raise RuntimeError(f'FFmpeg does not provide {self.spec.encoder} for {self.codec}; install a build with that encoder')

    def encode(self, ffmpeg: str, source: Path, output: Path, trim_frame: int, fps: int) -> None:
        subprocess.run([ffmpeg, '-nostdin', '-y', '-v', 'error', '-i', str(source),
                        '-ss', f'{trim_frame / fps:.9f}', '-map', '0:v:0', '-map', '0:a:0',
                        '-c:v', self.spec.encoder, '-tag:v', self.spec.tag,
                        '-preset', self.native_preset, '-crf', str(self.crf),
                        '-vf', 'scale=out_range=tv', '-pix_fmt', 'yuv420p', '-color_range', 'tv',
                        '-c:a', 'aac', '-b:a', '192k', '-movflags', '+faststart', str(output)], check=True)

    def verify(self, ffmpeg: str, ffprobe: str, output: Path, fps: int) -> dict:
        probe = subprocess.run([ffprobe, '-v', 'error', '-show_entries',
                                'format=duration,size:stream=codec_type,codec_name,codec_tag_string,width,height,pix_fmt,color_range,r_frame_rate,sample_rate,channels,duration,nb_frames',
                                '-of', 'json', str(output)], capture_output=True, text=True, check=True)
        metadata = json.loads(probe.stdout)
        streams = metadata.get('streams', [])
        video = next((s for s in streams if s.get('codec_type') == 'video'), {})
        audio = next((s for s in streams if s.get('codec_type') == 'audio'), {})
        video_format = tuple(video.get(key) for key in ('codec_name', 'codec_tag_string', 'width', 'height', 'pix_fmt', 'color_range', 'r_frame_rate'))
        if video_format != (self.codec, self.spec.tag, 1920, 1080, 'yuv420p', 'tv', f'{fps}/1'):
            raise RuntimeError(f'Unexpected MP4 video format for {self.codec}: {video_format}')
        if (audio.get('codec_name'), audio.get('sample_rate'), audio.get('channels')) != ('aac', '48000', 2):
            raise RuntimeError('MP4 is missing the expected stereo 48 kHz AAC game mix')
        if abs(float(video['duration']) - float(audio['duration'])) > 1 / fps:
            raise RuntimeError('Audio and video duration differ by more than one frame')
        subprocess.run([ffmpeg, '-nostdin', '-v', 'error', '-xerror', '-i', str(output), '-f', 'null', '-'], check=True)
        return metadata
