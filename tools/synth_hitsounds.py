#!/usr/bin/env python3
"""程序合成打击音效（Dynamite Universe 原创，对标原版 7 种 HitSound 类别）。

用法：
    python3 synth_hitsounds.py [--out DIR]

输出 7 个 44.1kHz 16bit 单声道 WAV：
    GlassShard / Keyboard / Mechanic / Metal / PistolTrigger / Polymer / Polymer2

只依赖 numpy；WAV 写出用标准库 wave。所有声音为程序合成的原创音效，
不采样、不复制任何原游戏音频。
"""

from __future__ import annotations

import argparse
import wave
from pathlib import Path

import numpy as np

SR = 44100


# ---------- 基础构件 ----------

def env_exp(n: int, tau_ms: float) -> np.ndarray:
    """指数衰减包络，tau 为时间常数（ms）。"""
    t = np.arange(n) / SR
    return np.exp(-t / (tau_ms / 1000.0))


def env_ad(n: int, attack_ms: float, tau_ms: float) -> np.ndarray:
    """线性起音 + 指数衰减。"""
    a = max(1, int(attack_ms / 1000.0 * SR))
    env = env_exp(n, tau_ms)
    env[:a] *= np.linspace(0.0, 1.0, a)
    return env


def sine(freq: float, n: int, phase: float = 0.0) -> np.ndarray:
    t = np.arange(n) / SR
    return np.sin(2 * np.pi * freq * t + phase)


def noise(n: int, seed: int = 0) -> np.ndarray:
    return np.random.default_rng(seed).standard_normal(n)


def lowpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    """单极点低通。"""
    a = 1.0 - np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a * (x[i] - acc)
        y[i] = acc
    return y


def highpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    return x - lowpass(x, cutoff)


def resonator(x: np.ndarray, freq: float, decay_ms: float) -> np.ndarray:
    """双极点谐振器：把信号灌进 freq 处的窄带共鸣。"""
    r = np.exp(-1.0 / (decay_ms / 1000.0 * SR))
    w = 2 * np.pi * freq / SR
    b0 = 1.0 - r
    a1 = -2 * r * np.cos(w)
    a2 = r * r
    y = np.zeros_like(x)
    for i in range(2, len(x)):
        y[i] = b0 * x[i] - a1 * y[i - 1] - a2 * y[i - 2]
    return y


def partials(freqs_amps: list[tuple[float, float]], n: int) -> np.ndarray:
    out = np.zeros(n)
    for f, a in freqs_amps:
        out += a * sine(f, n)
    return out


def finalize(x: np.ndarray, peak: float = 0.7) -> np.ndarray:
    """软饱和 + 峰值归一。"""
    x = np.tanh(x)
    m = np.max(np.abs(x))
    if m > 0:
        x = x / m * peak
    return x


def save_wav(path: Path, x: np.ndarray) -> None:
    pcm = (x * 32767).astype(np.int16)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def ms(duration_ms: float) -> int:
    return int(duration_ms / 1000.0 * SR)


# ---------- 7 种打击音（原创合成） ----------

def glass_shard() -> np.ndarray:
    """玻璃碎片：高频非谐波泛音簇 + 极短高通噪声，清脆明亮。"""
    n = ms(180)
    body = partials([(3520, 1.0), (4970, 0.7), (7230, 0.5), (9110, 0.25)], n)
    body *= env_ad(n, 0.5, 28)
    burst = highpass(noise(n, 1), 6000) * env_ad(n, 0.3, 6) * 0.8
    return finalize(body + burst)


def keyboard() -> np.ndarray:
    """键盘敲击：2kHz 附近的短促 click + 轻微低频 thock，极干。"""
    n = ms(60)
    click = resonator(noise(n, 2), 2100, 4) * env_ad(n, 0.2, 3.5) * 3.0
    thock = sine(160, n) * env_ad(n, 0.5, 9) * 0.4
    return finalize(click + thock)


def mechanic() -> np.ndarray:
    """机械：中频金属 partials 中等衰减 + 攻击噪声，像扳手敲击。"""
    n = ms(150)
    body = partials([(310, 1.0), (845, 0.8), (1620, 0.6), (2380, 0.35)], n)
    body *= env_ad(n, 0.4, 22)
    attack = highpass(noise(n, 3), 3000) * env_ad(n, 0.2, 4) * 0.7
    return finalize(body + attack)


def metal() -> np.ndarray:
    """金属：棒状非谐波泛音（1, 2.76, 5.4, 8.9），长尾共鸣。"""
    n = ms(320)
    f0 = 220
    body = partials([(f0 * r, a) for r, a in
                     [(1.0, 1.0), (2.76, 0.7), (5.40, 0.5), (8.93, 0.3)]], n)
    body *= env_ad(n, 0.4, 65)
    attack = highpass(noise(n, 4), 4000) * env_ad(n, 0.2, 5) * 0.6
    return finalize(body + attack)


def pistol_trigger() -> np.ndarray:
    """扳机：极快的宽带 snap + 低频短 thump，干脆利落。"""
    n = ms(80)
    snap = resonator(noise(n, 5), 2800, 2.5) * env_ad(n, 0.1, 2.2) * 3.5
    thump = sine(95, n) * env_ad(n, 0.8, 14) * 0.9
    return finalize(snap + thump)


def polymer() -> np.ndarray:
    """聚合物：低通噪声 + 180Hz 圆润 body，软塑料拍击感。"""
    n = ms(100)
    body = sine(185, n) * env_ad(n, 1.2, 16) * 1.0
    tap = lowpass(noise(n, 6), 900) * env_ad(n, 0.6, 7) * 1.4
    return finalize(body + tap)


def polymer2() -> np.ndarray:
    """聚合物 2：更低更圆的变体，略长。"""
    n = ms(120)
    body = (sine(140, n) * 1.0 + sine(283, n) * 0.3) * env_ad(n, 1.5, 20)
    tap = lowpass(noise(n, 7), 650) * env_ad(n, 0.8, 9) * 1.2
    return finalize(body + tap)


SOUNDS = {
    "HitSound_0001_GlassShard": glass_shard,
    "HitSound_0002_Keyboard": keyboard,
    "HitSound_0003_Mechanic": mechanic,
    "HitSound_0004_Metal": metal,
    "HitSound_0005_PistolTrigger": pistol_trigger,
    "HitSound_0006_Polymer": polymer,
    "HitSound_0007_Polymer2": polymer2,
}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(Path(__file__).parent.parent
                                         / "client" / "assets" / "audio" / "hitsounds"))
    args = ap.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    for name, fn in SOUNDS.items():
        x = fn()
        path = out / f"{name}.wav"
        save_wav(path, x)
        print(f"{path.name}: {len(x) / SR * 1000:.0f} ms, "
              f"peak={np.max(np.abs(x)):.2f}")


if __name__ == "__main__":
    main()
