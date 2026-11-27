"""Original 4/4 electronic cue, synthesized directly to stereo PCM (no samples)."""

from array import array
from fractions import Fraction
from math import exp, sin, tau
from pathlib import Path
from random import Random
import sys
import wave

RATE = 44_100
DURATION_MS = 20_400
OFFSET = Fraction(3, 5)
SWITCH_BEAT = 17
TEMPOS = (120, 160)
FINAL_BEAT = 42


def media_time(beat):
    """Exact map shared by the score and synth; the tempo change is off-bar."""
    beat = Fraction(beat)
    return (OFFSET + min(beat, SWITCH_BEAT) * Fraction(60, TEMPOS[0])
            + max(0, beat - SWITCH_BEAT) * Fraction(60, TEMPOS[1]))


def hz(midi):
    return 440 * 2 ** ((midi - 69) / 12)


def write_music(path: Path):
    frames = RATE * DURATION_MS // 1000
    left = array('d', [0]) * frames
    right = array('d', [0]) * frames
    rng = Random(0xA71A5)

    def layer(beat, length, gain, pan, voice):
        start = round(media_time(beat) * RATE)
        gl, gr = gain * (1 - pan) ** 0.5, gain * pan ** 0.5
        for i in range(min(round(length * RATE), frames - start)):
            sample = voice(i / RATE, length)
            left[start + i] += sample * gl
            right[start + i] += sample * gr

    def kick(t, end):
        phase = tau * (47 * t + 4.0 * (1 - exp(-t * 28)))
        return (sin(phase) + .12 * sin(2 * phase) * exp(-t * 24)) * min(1, t * 700) * exp(-t * 14)

    def clap(t, end):
        n = rng.random() * 2 - 1
        env = sum(exp(-(t - delay) * 44) for delay in (0, .011, .023) if t >= delay)
        return n * env * .40 + .34 * sin(tau * 174 * t) * exp(-t * 48)

    def hat(t, end):
        return ((rng.random() * 2 - 1) * .70 + sin(tau * 8393 * t) * .30) * exp(-t * 5.2 / end)

    def synth(midi, style):
        f = hz(midi)

        def voice(t, end):
            a = tau * f * t
            if style == 'bass':
                amp = min(1, t * 180, (end - t) * 16) * exp(-t * 5)
                return amp * (sin(a) + .35 * sin(a * 2) + .17 * sin(a * 3))
            if style == 'pad':
                amp = min(1, t * 1.9, (end - t) * 1.8)
                return amp * (.62 * sin(a) + .26 * sin(a * 1.002) + .2 * sin(a * 2))
            amp = min(1, t * 310, (end - t) * 18) * exp(-t * 5.5)
            return amp * (sin(a + sin(a * 2.002) * exp(-t * 11) * 1.3) + .22 * sin(a * 3))

        return voice

    # Chord voicings and two hand-authored melodic/rhythmic phrases. The bass,
    # kick and sharper arpeggio enter together exactly at the tempo switch.
    chords = ((42, 54, 57, 61, 68), (38, 54, 57, 62, 64),
              (45, 52, 57, 59, 61), (40, 52, 56, 59, 66))
    opening = ((0, 2), (3, 0), (6, 1), (9, 3), (12, 2), (15, 1))
    relay = ((0, 1), (2, 2), (3, 3), (5, 0), (8, 2), (10, 1), (13, 3), (14, 2))
    sections = (0, 4, 8, 12, 16, 17, 21, 25, 29, 33, 37, 41)
    for i, start in enumerate(sections):
        end = sections[i + 1] if i + 1 < len(sections) else FINAL_BEAT
        root, *voicing = chords[(i if i < 5 else i - 5) % len(chords)]
        for j, pitch in enumerate(voicing):
            layer(start, float(media_time(end) - media_time(start)) + .35,
                  .057, (.18, .66, .38, .83)[j], synth(pitch, 'pad'))
        for beat in range(start, end):
            layer(beat, .39 if beat < SWITCH_BEAT else .28, .18, .5,
                  synth(root + (12 if (beat - start) % 4 == 3 else 0), 'bass'))
        for step, voice in opening if start < SWITCH_BEAT else relay:
            beat = start + Fraction(step, 4)
            if beat >= end:
                break
            tone = synth(voicing[voice] + 12 + (12 if step == 13 else 0), 'bell')
            pan = (.33, .58, .74, .43)[voice]
            layer(beat, .27, .115, pan, tone)
            layer(beat + Fraction(3, 8), .43, .027, 1 - pan, tone)

    for beat in range(FINAL_BEAT):
        section_beat = beat if beat < SWITCH_BEAT else beat - SWITCH_BEAT
        if beat >= 4 and (beat >= SWITCH_BEAT or section_beat % 4 in (0, 2)):
            layer(beat, .36, .49, .5, kick)
        if beat >= 5 and section_beat % 2:
            layer(beat, .21, .19, .51, clap)
        if beat >= 2:
            layer(beat + Fraction(1, 2), .095, .075, .73, hat)
        if beat >= SWITCH_BEAT:
            layer(beat, .057, .043, .26, hat)
            if section_beat % 4 in (1, 3):
                layer(beat + Fraction(3, 4), .045, .05, .39, hat)

    layer(SWITCH_BEAT - Fraction(1, 2), .23, .12, .44, clap)
    layer(FINAL_BEAT, .55, .44, .5, kick)
    for j, pitch in enumerate((42, 54, 57, 61, 68, 73)):
        pan = (j + 1) / 7
        layer(FINAL_BEAT, 1.86, .079, pan, synth(pitch, 'pad'))
        layer(FINAL_BEAT + Fraction(j, 8), .64, .07, 1 - pan, synth(pitch + 12, 'bell'))

    # Normalize the single deterministic mix, protect the final PCM samples.
    peak = max(max(map(abs, buffer)) for buffer in (left, right))
    gain = .88 * 32767 / peak
    pcm = array('h')
    for i, (l, r) in enumerate(zip(left, right)):
        g = gain * min(1, (frames - 1 - i) / (RATE * .09))
        pcm.extend((round(l * g), round(r * g)))
    if sys.byteorder != 'little':
        pcm.byteswap()
    with path.open('xb') as stream, wave.open(stream, 'wb') as wav:
        wav.setparams((2, 2, RATE, frames, 'NONE', 'not compressed'))
        wav.writeframes(pcm.tobytes())

    # A second pass exercises actual RIFF/WAVE readback without external tools.
    with wave.open(str(path), 'rb') as wav:
        if (wav.getnchannels(), wav.getsampwidth(), wav.getframerate(), wav.getnframes()) != (2, 2, RATE, frames):
            raise ValueError('generated WAV does not match the expected PCM layout')
        if len(wav.readframes(frames)) != frames * 4:
            raise ValueError('generated WAV data was truncated')
