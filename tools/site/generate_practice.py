"""Generate the website's original 120 BPM practice beat; no external media."""
from __future__ import annotations

import argparse
import math
from pathlib import Path
import struct
import wave


def generate(path: Path) -> None:
    rate = 22050
    duration = 16
    pcm = bytearray()
    for index in range(rate * duration):
        beat, offset = divmod(index, rate // 2)
        t = offset / rate
        frequency = 1320 if beat % 4 == 0 else 880
        envelope = min(t / 0.003, 1) * math.exp(-t * 55) if t < 0.12 else 0
        value = round(0.32 * envelope * math.sin(2 * math.pi * frequency * t) * 32767)
        pcm.extend(struct.pack('<h', value))
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(pcm)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    generate(parser.parse_args().output)
