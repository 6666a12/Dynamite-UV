"""Offline texture residency audit for a private standalone osu! storyboard.

No assets are emitted and this does not measure Godot, driver memory or GPU fences.
"""
from __future__ import annotations

import argparse
from bisect import bisect_right
from collections import defaultdict
from dataclasses import dataclass
import json
from pathlib import Path
from statistics import median
from time import perf_counter

from PIL import Image
from twin_bloom_local_osb import (
    DEFAULT_SOURCE, WorkBudget, asset_path, frame_spans, parse, private_path, trigger_lines, unconditional_bounds,
)

MIB = 1 << 20
MAX_AUDIT_FRAME_STEPS = 1_000_000


@dataclass(frozen=True)
class Asset:
    image_bytes: int
    file_bytes: int
    spans: tuple[tuple[int, int], ...]
    width: int = 0
    height: int = 0


def merge(spans):
    result = []
    for start, end in sorted(spans):
        if start >= end:
            raise ValueError('empty or inverted asset interval')
        if result and start <= result[-1][1]:
            result[-1] = result[-1][0], max(end, result[-1][1])
        else:
            result.append((start, end))
    return tuple(result)


def audit(source: Path, duration: int):
    sprites, ignored = parse(source.read_text(encoding='utf-8-sig'))
    spans = defaultdict(list)
    budget = WorkBudget(MAX_AUDIT_FRAME_STEPS, 'residency span')
    for sprite in sprites:
        if triggers := trigger_lines(sprite):
            raise ValueError(f'line {triggers[0]}: trigger-dependent residency is not modeled')
        first, last = unconditional_bounds(sprite)
        if first is None or first >= duration or last < 0:
            continue
        start, end = max(0, first), min(duration, last + 1)
        t = start
        for asset, width in frame_spans(sprite, first, start, end, budget):
            spans[asset].append((t, t + width))
            t += width
        if t != end:
            raise ValueError('animation coverage did not match active interval')
    assets = {}
    paths = {}
    for name, ranges in sorted(spans.items()):
        path = asset_path(source.parent, name)
        with Image.open(path) as image:
            w, h = image.size
        assets[name] = Asset(w * h * 4, path.stat().st_size, merge(ranges), w, h)
        paths[name] = path
    return len(sprites), dict(ignored), assets, paths


def events_for(assets, ahead=0):
    events = defaultdict(list)
    for name, asset in assets.items():
        for start, end in merge((max(0, start - ahead), end) for start, end in asset.spans):
            events[start].append((name, True))
            events[end].append((name, False))
    return events


def sweep(assets, ahead=0):
    events = events_for(assets, ahead)
    active = set()
    resident = 0
    peak_bytes = peak_count = t_bytes = t_count = 0
    for t, batch in sorted(events.items()):
        for name, add in batch:
            if add:
                active.add(name)
                resident += assets[name].image_bytes
            else:
                active.remove(name)
                resident -= assets[name].image_bytes
        if resident > peak_bytes:
            peak_bytes, t_bytes = resident, t
        if len(active) > peak_count:
            peak_count, t_count = len(active), t
    return dict(peak_bytes=peak_bytes, at_bytes_ms=t_bytes, peak_count=peak_count, at_count_ms=t_count)


def replay(assets, duration, cap_bytes, cap_count, horizon, budget_rate, overhead_ms=1, tick_ms=7):
    """One conservative serialized load lane; decimal milliseconds, quantized completion.

    The admission clock reserves decoded bytes immediately. Demand pinned until last use.
    Other residents with a later next use can be evicted for an earlier deadline. No real I/O,
    GPU upload/retirement, video, simultaneous producers or pressure callbacks are included.
    """
    events = sorted(events_for(assets).items())
    starts = {n: tuple(t for t, _ in a.spans) for n, a in assets.items()}
    future = sorted((t, n) for n, a in assets.items() for t, _ in a.spans)
    first = {name: t for t, entries in events if t == 0 for name, add in entries if add}
    loaded = set(first) if len(first) <= cap_count and sum(assets[n].image_bytes for n in first) <= cap_bytes else set()
    used = prime_bytes = sum(assets[n].image_bytes for n in loaded)
    pending = None
    peak_bytes, peak_count = used, len(loaded)
    seen = set(loaded)
    reads, read_bytes, reloads = len(loaded), prime_bytes, 0
    misses, missing_ms, skipped_admissions, first_miss = 0, 0, 0, None
    cursor = lo = hi = 0
    active = set()
    for t in range(0, duration, tick_ms):
        while cursor < len(events) and events[cursor][0] <= t:
            for name, add in events[cursor][1]:
                if add:
                    active.add(name)
                else:
                    active.remove(name)
            cursor += 1
        if pending is not None and pending[0] <= t:
            loaded.add(pending[1])
            pending = None
        if active - loaded:
            misses += 1
            missing_ms += min(duration - t, tick_ms)
            if first_miss is None:
                first_miss = t
        while lo < len(future) and future[lo][0] <= t:
            lo += 1
        hi = max(lo, hi)
        while hi < len(future) and future[hi][0] <= t + horizon:
            hi += 1
        if pending is not None:
            continue
        candidates = {name: t for name in active - loaded}
        for deadline, name in future[lo:hi]:
            if name not in loaded:
                candidates.setdefault(name, deadline)
        def next_use(name):
            seq = starts[name]
            i = bisect_right(seq, t)
            return seq[i] if i < len(seq) else float('inf')
        for name, deadline in sorted(candidates.items(), key=lambda item: (item[1], item[0])):
            cost = assets[name].image_bytes
            if used + cost > cap_bytes or len(loaded) >= cap_count:
                victims, recovered = [], 0
                for other in sorted(loaded - active, key=lambda n: (next_use(n), assets[n].image_bytes, n), reverse=True):
                    if next_use(other) <= deadline:
                        break
                    victims.append(other)
                    recovered += assets[other].image_bytes
                    if used - recovered + cost <= cap_bytes and len(loaded) - len(victims) < cap_count:
                        break
                if used - recovered + cost > cap_bytes or len(loaded) - len(victims) >= cap_count:
                    skipped_admissions += 1
                    continue
                loaded.difference_update(victims)
                used -= recovered
            pending = (t + overhead_ms + 1000 * cost / (budget_rate * MIB), name)
            reads += 1
            reloads += name in seen
            seen.add(name)
            read_bytes += cost
            used += cost
            peak_bytes = max(peak_bytes, used)
            peak_count = max(peak_count, len(loaded) + 1)
            break
    return dict(peak_bytes=peak_bytes, peak_slots=peak_count, prime_bytes=prime_bytes, loads=reads,
                reloads=reloads, bytes_processed=read_bytes, missing_ticks=misses, missing_ms=missing_ms,
                first_missing_ms=first_miss, skipped_admission_attempts=skipped_admissions)


def seek_sets(assets, timestamps, ahead):
    result = []
    for t in timestamps:
        required = [a for a in assets.values() if any(s <= t < e for s, e in a.spans)]
        desired = [a for a in assets.values() if any(s <= t + ahead and e > t for s, e in a.spans)]
        result.append(dict(at_ms=t, current_count=len(required), current_bytes=sum(a.image_bytes for a in required),
                           window_count=len(desired), window_bytes=sum(a.image_bytes for a in desired)))
    return result


def decode(paths, assets):
    counts = []
    start = perf_counter()
    for name, path in paths.items():
        begin = perf_counter()
        with Image.open(path) as file, file.convert('RGBA') as pixels:
            if pixels.width * pixels.height * 4 != assets[name].image_bytes:
                raise ValueError('decoded size mismatch')
        counts.append((perf_counter() - begin) * 1000)
    import math
    return dict(p50_ms=median(counts), p95_ms=sorted(counts)[math.ceil(len(counts) * .95) - 1],
                worst_ms=max(counts), elapsed_s=perf_counter() - start)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--osb', type=Path, default=DEFAULT_SOURCE)
    p.add_argument('--duration-ms', type=int, default=424872)
    p.add_argument('--decode', action='store_true', help='add sequential local Pillow decode timings, no GPU')
    o = p.parse_args()
    if not 1 <= o.duration_ms <= 3_600_000:
        p.error('duration-ms must be between 1 and 3,600,000')
    objects, ignored, assets, paths = audit(private_path(o.osb), o.duration_ms)
    if not assets:
        p.error('no assets in the measurement window')
    eps = dict(objects=objects, referenced_paths=len(assets), ignored=ignored,
               eager_bytes=sum(a.image_bytes for a in assets.values()), file_bytes=sum(a.file_bytes for a in assets.values()),
               largest_bytes=max(a.image_bytes for a in assets.values()),
               windows={t: sweep(assets, t) for t in (0, 500, 1000, 2000, 4000, 8000)},
               seek=seek_sets(assets, [t for t in (0, 64800, 89900, 197200, 267276, 297833, 312931, 379318)
                                       if t < o.duration_ms], 2000), simulations=[])
    # 112/96 MiB model 16/32 MiB carved out of a nominal low 128-MiB envelope.
    for count, size in ((128, 96), (128, 112), (128, 128), (128, 256), (128, 512), (256, 512), (256, 1024)):
        for horizon, rate in ((500, 80), (2000, 80), (2000, 160), (4000, 80), (4000, 160), (8000, 80)):
            eps['simulations'].append(dict(slots=count, mib=size, horizon_ms=horizon, assumed_mib_s=rate,
                                           **replay(assets, o.duration_ms, size * MIB, count, horizon, rate)))
    eps['low_dimension_model'] = []
    for max_edge in (2048, 1536):
        resized = {}
        for name, a in assets.items():
            ratio = min(1, max_edge / max(a.width, a.height))
            w, h = max(1, round(a.width * ratio)), max(1, round(a.height * ratio))
            resized[name] = Asset(w * h * 4, a.file_bytes, a.spans, w, h)
        eps['low_dimension_model'].append(dict(max_edge=max_edge,
            count_changed=sum(a.image_bytes != assets[n].image_bytes for n, a in resized.items()),
            active=sweep(resized), lookahead_2s=sweep(resized, 2000),
            replay_96mib_80mibs=replay(resized, o.duration_ms, 96 * MIB, 128, 2000, 80),
            replay_96mib_40mibs=replay(resized, o.duration_ms, 96 * MIB, 128, 2000, 40)))
    if o.decode:
        eps['pillow_decode'] = decode(paths, assets)
    print(json.dumps(eps, indent=2))


if __name__ == '__main__':
    main()
