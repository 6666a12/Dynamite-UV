"""Private, offline bounded Twin Bloom storyboard converter (see twin_bloom_local_osb.md)."""

import argparse
from bisect import bisect_right
from collections import Counter, defaultdict
import csv
from dataclasses import dataclass, replace
from fractions import Fraction
from hashlib import sha256
import json
import math
from pathlib import Path, PurePosixPath
import shutil
from tempfile import TemporaryDirectory

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
PRIVATE = REPO / ".tmp"
DEFAULT_SOURCE = PRIVATE / "osu-twin-bloom-research/organizer-target/Gardens - Twin Bloom (elexire).osb"
DEFAULT_CHART = PRIVATE / "storyboard-demo/twin-bloom-ramification"
LAYER = {"Background": 0, "Fail": 1, "Pass": 2, "Foreground": 3, "Overlay": 4}
PIVOT = {"TopLeft": (0, 0), "TopCentre": (.5, 0), "TopRight": (1, 0),
         "CentreLeft": (0, .5), "Centre": (.5, .5), "CentreRight": (1, .5),
         "BottomLeft": (0, 1), "BottomCentre": (.5, 1), "BottomRight": (1, 1)}
AXES = {"M": ("position.x", "position.y"), "MX": ("position.x",), "MY": ("position.y",),
        "S": ("scale.x",), "V": ("scale.x", "scale.y"), "R": ("rotation",),
        "C": ("tint",), "F": ("opacity",)}


@dataclass(frozen=True)
class Command:
    kind: str
    ease: int
    start: int
    end: int
    first: tuple
    last: tuple
    line: int


@dataclass
class Sprite:
    line: int
    layer: str
    pivot: str
    path: str
    x: float
    y: float
    frames: int
    delay: float
    mode: str
    entries: list


def require(condition, message):
    if not condition:
        raise ValueError(message)


class WorkBudget:
    def __init__(self, limit, label):
        self.remaining = limit
        self.label = label

    def take(self, cost, source):
        require(cost <= self.remaining, f"{source}: {self.label} work budget exceeded")
        self.remaining -= cost


def parse(source):
    sprites, ignored = [], Counter()
    sprite, block = None, None
    for line, text in enumerate(source.splitlines(), 1):
        if not text.strip() or text.lstrip().startswith("//"):
            continue
        trimmed = text.lstrip(" _")
        indent = len(text) - len(trimmed)
        if trimmed.startswith("["):
            require(trimmed == "[Events]", f"line {line}: expected standalone .osb [Events]")
            continue
        fields = next(csv.reader([trimmed], strict=True))
        kind = fields[0]
        if indent == 0:
            block = None
            if kind not in ("Sprite", "Animation"):
                require(kind in ("0", "1", "5", "Video", "Sample", "Background", "Break", "2"),
                        f"line {line}: unknown event {kind}")
                ignored[kind] += 1
                sprite = None
                continue
            _, layer, pivot, path, x, y, *animation = fields
            require(layer in LAYER and pivot in PIVOT, f"line {line}: unknown layer/origin")
            if kind == "Animation":
                count, delay, *mode = animation
                count, delay, mode = int(count), float(delay), mode[0] if mode else "LoopForever"
                require(count > 0 and math.isfinite(delay) and delay > 0 and mode in ("LoopOnce", "LoopForever"),
                        f"line {line}: invalid animation")
            else:
                count, delay, mode = 1, 1, "Sprite"
            sprite = Sprite(line, layer, pivot, path.replace("\\", "/"), float(x), float(y), count, delay, mode, [])
            sprites.append(sprite)
            continue
        require(sprite is not None, f"line {line}: command outside sprite")
        if kind == "L":
            require(indent == 1, f"line {line}: nested loop")
            _, start, count = fields
            block = (kind, int(start), int(count), [])
            require(block[2] > 0, f"line {line}: invalid loop count")
            sprite.entries.append(block)
        elif kind == "T":
            require(indent == 1, f"line {line}: nested trigger")
            block = (kind, fields[1:], line, [])
            sprite.entries.append(block)
        else:
            if indent == 1:
                block = None
            require(indent == 1 or (indent == 2 and block is not None), f"line {line}: invalid indentation")
            require(kind in AXES or kind == "P", f"line {line}: unknown command {kind}")
            _, ease, start, end, *values = fields
            start, end, ease = int(start), int(end or start), int(ease)
            require(end >= start, f"line {line}: reversed command interval")
            arity = 3 if kind == "C" else 2 if kind in ("M", "V") else 1
            require(len(values) in (arity, arity * 2), f"line {line}: wrong command arity")
            vals = tuple(values) if kind == "P" else tuple(map(float, values))
            cmd = Command(kind, ease, start, end, vals[:arity], vals[-arity:], line)
            (block[3] if block else sprite.entries).append(cmd)
    return sprites, ignored


def trigger_lines(sprite):
    return [entry[2] for entry in sprite.entries if not isinstance(entry, Command) and entry[0] == "T"]


def unconditional_bounds(sprite):
    first = last = None
    for entry in sprite.entries:
        if isinstance(entry, Command):
            start, end = entry.start, entry.end
        else:
            kind, base, count, children = entry
            if kind == "T" or not children:
                continue
            start = base + min(c.start for c in children)
            end = base + max(c.end for c in children) * count
        first = start if first is None else min(first, start)
        last = end if last is None else max(last, end)
    return first, last


def expand(sprite, begin, end, budget=None):
    """Keep one preceding loop iteration per track, the active ones, and the first future one."""
    first, last = unconditional_bounds(sprite)
    if first is None or first >= end or last < begin:
        return None
    if budget is None:
        budget = WorkBudget(MAX_EXPANDED_COMMANDS, "expanded command")
    commands = []
    for entry in sprite.entries:
        if isinstance(entry, Command):
            budget.take(1, f"line {entry.line}")
            commands.append(entry)
            continue
        kind, start, count, children = entry
        if kind == "T" or not children:
            continue
        period = max(c.end for c in children)
        if period == 0:
            iterations = range(1)
        else:
            lo = min(count - 1, max(0, (begin - start) // period - 1))
            hi = min(count - 1, max(0, (end - start) // period + 1))
            iterations = range(lo, hi + 1)
        budget.take(len(iterations) * len(children), f"line {children[0].line} (loop child)")
        for iteration in iterations:
            shift = start + iteration * period
            commands.extend(replace(c, start=c.start + shift, end=c.end + shift) for c in children)
    # osu includes the final instant; callers retain it up to the next 1-ms boundary.
    return first, last, commands, trigger_lines(sprite)


def asset_path(root, name):
    path = PurePosixPath(name)
    require(not path.is_absolute() and ".." not in path.parts and ":" not in name,
            f"unsafe asset path: {name}")
    target = (root / Path(*path.parts)).resolve(strict=True)
    require(target.is_relative_to(root), f"asset outside source tree: {name}")
    return target


def private_path(path):
    path = path.resolve()
    require(path != PRIVATE and path.is_relative_to(PRIVATE), "input and output must stay under repository .tmp/")
    return path


NATIVE = {0: "linear", 1: "quad-out", 2: "quad-in", 3: "quad-in", 4: "quad-out",
          5: "quad-in-out", 15: "sine-in", 16: "sine-out", 17: "sine-in-out"}
STAGE_SCALE = 1080 / 480
MAX_COUNT, MAX_DIMENSION = 1024, 4096
MAX_NODES, MAX_SCRIPT = 12_000, 2 << 20
MAX_PNG = 16 << 20
PREVIEW_COUNT, PREVIEW_PIXELS, PREVIEW_PNG_TOTAL = 128, 25_165_824, 48 << 20
MAX_EXPANDED_COMMANDS, MAX_FRAME_STEPS = 100_000, 100_000
# Limits offline decode/deduplication work, not the size of a streamed declaration.
MAX_SOURCE_DECODE_BYTES = 1 << 30
LINE_FRAMES = {f"sb/s/9/lines/line{i}.png" for i in range(6)}
CHART_ID = "ramification"
SCRIPT = "official-excerpt.uvsb"


def easing(code, u):
    if u <= 0 or u >= 1:
        return min(1, max(0, u))
    match code:
        case 0: return u
        case 1 | 4: return 1 - (1 - u) ** 2
        case 2 | 3: return u ** 2
        case 5: return 2 * u ** 2 if u < .5 else 1 - (-2 * u + 2) ** 2 / 2
        case 6: return u ** 3
        case 7: return 1 - (1 - u) ** 3
        case 8: return 4 * u ** 3 if u < .5 else 1 - (-2 * u + 2) ** 3 / 2
        case 15: return 1 - math.cos(math.pi * u / 2)
        case 16: return math.sin(math.pi * u / 2)
        case 17: return (1 - math.cos(math.pi * u)) / 2
        case 18: return 2 ** (10 * u - 10)
        case 19: return 1 - 2 ** (-10 * u)
        case 20: return 2 ** (20 * u - 10) / 2 if u < .5 else (2 - 2 ** (-20 * u + 10)) / 2
        case 30: return 1 + 2.70158 * (u - 1) ** 3 + 1.70158 * (u - 1) ** 2
        case _: raise ValueError(f"Unsupported selected easing {code}; choose another window or implement its curve")


def frame_spans(sprite, source_start, start, end, budget=None):
    """Slice the frame clock at represented (integer millisecond) boundaries."""
    if start >= end:
        return []
    if budget is None:
        budget = WorkBudget(MAX_FRAME_STEPS, "animation span")
    line = f"line {sprite.line}"
    if sprite.mode == "Sprite":
        budget.take(1, line)
        return [(sprite.path, end - start)]
    path = PurePosixPath(sprite.path)
    delay = Fraction(str(sprite.delay))
    num, den = delay.numerator, delay.denominator
    t, spans = start, []
    while t < end:
        budget.take(1, line)
        # ceil(i * delay) <= t - source_start iff i * delay <= t - source_start.
        # Jump over all unrepresented sub-millisecond frames without even making their names.
        i = (t - source_start) * den // num
        last = sprite.mode == "LoopOnce" and i >= sprite.frames - 1
        if last or sprite.frames == 1:
            index, boundary = sprite.frames - 1, end
        else:
            index = i % sprite.frames
            boundary = min(end, source_start + ((i + 1) * num + den - 1) // den)
        name = str(path.with_name(f"{path.stem}{index}{path.suffix}"))
        if spans and spans[-1][0] == name:
            spans[-1] = name, spans[-1][1] + boundary - t
        else:
            spans.append((name, boundary - t))
        t = boundary
    return spans


@dataclass
class Texture:
    id: str
    source: Path
    format: str
    original: tuple
    size: tuple
    alpha: tuple
    aliases: list


def inspect_textures(root, names, line_width):
    by_path, unique = {}, {}
    budget = WorkBudget(MAX_SOURCE_DECODE_BYTES, "source RGBA decode")
    for name in sorted(names):
        source = asset_path(root, name)
        with Image.open(source) as image:
            detected = image.format
            require(detected in ("PNG", "JPEG") and getattr(image, "n_frames", 1) == 1,
                    f"not a single-frame PNG or JPEG: {name}")
            original = image.size
            require(all(0 < dim <= MAX_DIMENSION for dim in original), f"texture dimension exceeds {MAX_DIMENSION}: {name}")
            target = (line_width, line_width * 9 // 16) if line_width and name in LINE_FRAMES else original
            require(target == original or original == (1920, 1080), f"unexpected size for resized line frame: {name}")
            budget.take(math.prod(original) * 4, name)  # charge every source, even if it deduplicates
            with image.convert("RGBA") as rgba, rgba.getchannel("A") as channel:
                alpha = channel.histogram()
                digest = (original, target, sha256(rgba.tobytes()).digest())
        if digest not in unique:
            unique[digest] = Texture(f"texture-{len(unique):03d}", source, detected, original, target,
                                     (alpha[0], sum(alpha[1:255]), alpha[255]), [])
        texture = by_path[name] = unique[digest]
        texture.aliases.append(name)
    return by_path, list(unique.values())


def write_texture(texture, out):
    if texture.original == texture.size and texture.format == "PNG":
        require(texture.source.stat().st_size <= MAX_PNG, f"{texture.source.name}: exceeds one-PNG byte limit")
        shutil.copyfile(texture.source, out)  # original alpha, dimensions and pixels, byte-for-byte
        return
    with Image.open(texture.source) as src:
        image = src.convert("RGB" if src.format == "JPEG" else "RGBA")
        if image.size != texture.size:
            image = image.resize(texture.size, Image.Resampling.LANCZOS)
        image.save(out, format="PNG")
    with Image.open(out) as check:
        check.load()
        require(check.size == texture.size, f"unexpected PNG size: {out}")


def command_tracks(commands):
    tracks, flags = defaultdict(list), defaultdict(list)
    for c in commands:
        if c.kind == "P":
            require(c.first == c.last and c.first[0] in "AHV" and len(c.first[0]) == 1,
                    f"line {c.line}: unsupported parameter")
            flags[c.first[0]].append(c)
            continue
        for i, prop in enumerate(AXES[c.kind]):
            pair = (c.first, c.last) if prop == "tint" else ((c.first[i],), (c.last[i],))
            tracks[prop].append(replace(c, first=pair[0], last=pair[1]))
        if c.kind == "S":
            tracks["scale.y"].append(c)
    return tracks, flags


def flag_track(commands, start, until):
    spans = [(c.start, c.end if c.end > c.start else until) for c in commands]
    cuts = sorted({start, *(at for span in spans for at in span if start < at < until)})
    result = []
    for at in cuts:
        value = any(a <= at < b for a, b in spans)
        if not result or result[-1][1] != (value,):
            result.append((at, (value,), "step"))
    return result


def srgb_to_linear(c):
    u = c / 255
    return u / 12.92 if u <= .04045 else ((u + .055) / 1.055) ** 2.4


def linear_to_srgb(c):
    return 255 * (12.92 * c if c <= .0031308 else 1.055 * (c ** (1 / 2.4)) - .055)


def fmt(v):
    return f"{v:.10f}".rstrip("0").rstrip(".") if v else "0"


def literal(prop, values):
    if prop == "tint":
        return '"#' + ''.join(f"{round(linear_to_srgb(v)):02X}" for v in values) + '"'
    if prop.startswith("flip-"):
        return "#true" if values[0] else "#false"
    return fmt(values[0])


def fit(sample, a, b, left, right, tolerance):
    """Return outgoing linear keys, inspecting multiple interior points before bisecting."""
    if b <= a:
        return []
    if b - a <= 1 or all(
        all(abs(v - (x + (y - x) * j / 8)) <= tolerance for v, x, y in zip(sample(a + (b - a) * j / 8), left, right))
        for j in range(1, 8)
    ):
        return [(a, left, "linear" if left != right else "step")]
    m = (a + b) // 2
    at_m = sample(m)
    return fit(sample, a, m, left, at_m, tolerance) + fit(sample, m, b, at_m, right, tolerance)


def numeric_keys(commands, start, until, convert, tolerance, report, color=False):
    if not commands:
        return []
    # Instantaneous setup precedes nonzero-duration commands at the same start.
    ordered = sorted(commands, key=lambda c: (c.start, c.end, c.line))
    starts = [c.start for c in ordered]
    breaks = sorted({start, until, *(at for c in ordered for at in (c.start, c.end) if start < at < until)})

    def select(at):
        return ordered[max(0, bisect_right(starts, at) - 1)]

    def sample(c, at):
        if at < c.start:
            return convert(c.first)
        if at >= c.end or c.first == c.last:
            return convert(c.last)
        try:
            u = easing(c.ease, (at - c.start) / (c.end - c.start))
        except ValueError as exc:
            raise ValueError(f"line {c.line}: {exc}") from exc
        return convert(tuple(x + (y - x) * u for x, y in zip(c.first, c.last)))

    keys, approximated = [], set()
    for a, b in zip(breaks, breaks[1:]):
        c = select(a)
        left, right = sample(c, a), sample(c, b)
        discontinuous = b < until and right != sample(select(b), b)
        r = b - 1 if discontinuous else b
        if discontinuous:
            report["submillisecond_jump_guards"] += int(left != right)
        if r > a:
            end_value = sample(c, r)
            if left == end_value:
                keys.append((a, left, "step"))
            elif not color and (c.ease == 0 or c.ease in NATIVE and a == c.start and r == c.end):
                keys.append((a, left, NATIVE[c.ease]))
            else:
                approximated.add((c.line, c.start, c.ease))
                keys += fit(lambda t: sample(c, t), a, r, left, end_value, tolerance)
        if discontinuous:
            keys.append((r, sample(c, r), "step"))
    keys.append((until, sample(select(breaks[-2]), until), "step"))
    for _, _, ease in approximated:
        report[f"fit_ease_{ease}"] += 1
    # Same-value consecutive holds are redundant; retain curve endpoints.
    slim = []
    for key in keys:
        if slim and slim[-1][1] == key[1] and slim[-1][2] == key[2] == "step":
            continue
        slim.append(key)
    return slim


def emit_property(out, prop, keys, default):
    if len(keys) == 1:
        if keys[0][1] != default:
            out.append(f'    base "{prop}" {literal(prop, keys[0][1])}')
    elif keys:
        out.append(f'    track "{prop}" {{')
        for i, (t, values, ease) in enumerate(keys):
            suffix = f' ease="{ease}"' if i < len(keys) - 1 and ease != "linear" and not prop.startswith("flip-") else ""
            out.append(f'        key at={t} value={literal(prop, values)}{suffix}')
        out.append("    }")


def emit_sprite(sprite, start, end, commands, spans, textures, report, order):
    tracks, flags = command_tracks(commands)
    require(sprite.layer not in ("Pass", "Fail"), f"line {sprite.line}: conditional Pass/Fail layer unsupported")
    texture = textures[spans[0][0]]
    require(all(textures[name].size == texture.size and textures[name].original == texture.original for name, _ in spans),
            f"line {sprite.line}: mixed animation dimensions unsupported")
    px, py = (f / t for f, t in zip(texture.original, texture.size))
    # One osu! pixel is 2.25 stage pixels, with the native 4:3 field centered in the 16:9 stage.
    x = lambda v: 960 + (v - 320) * STAGE_SCALE
    y = lambda v: v * STAGE_SCALE
    fields = [("position.x", (x(sprite.x),), (0,), x, .33), ("position.y", (y(sprite.y),), (0,), y, .33),
              ("scale.x", (STAGE_SCALE * px,), (1,), lambda v: v * STAGE_SCALE * px, .35 / texture.size[0]),
              ("scale.y", (STAGE_SCALE * py,), (1,), lambda v: v * STAGE_SCALE * py, .35 / texture.size[1]),
              ("rotation", (0,), (0,), math.degrees, .15),
              ("opacity", (1,), (1,), lambda v: min(1, max(0, v)), .003),
              ("tint", (1, 1, 1), (1, 1, 1), lambda v: srgb_to_linear(min(255, max(0, v))), .0015)]
    unique = {textures[name].id for name, _ in spans}
    asset_arg = f' asset="{texture.id}"' if len(unique) == 1 else ""
    out = [f'sprite "osb-{sprite.line:05d}"{asset_arg} clock="media-ms" order={order} {{',
           f"    active from={start} until={end}"]
    pivot = PIVOT[sprite.pivot]
    if pivot != (.5, .5):
        out.append(f'    base "pivot" {fmt(pivot[0])} {fmt(pivot[1])}')
    blend = flag_track(flags["A"], start, end)
    require(len(blend) == 1, f"line {sprite.line}: time-varying additive blend unsupported by a single UVSB sprite")
    if blend[0][1][0]:
        out.append('    base "blend" "add"')
    for flag, prop in (("H", "flip-x"), ("V", "flip-y")):
        emit_property(out, prop, flag_track(flags[flag], start, end), (False,))
    for prop, base, default, transform, tolerance in fields:
        keys = numeric_keys(tracks[prop], start, end, lambda vs: tuple(map(transform, vs)), tolerance, report, prop == "tint")
        if prop.startswith("scale."):
            require(all(key[1][0] >= 0 for key in keys), f"line {sprite.line}: negative scale unsupported")
        emit_property(out, prop, keys or [(start, base, "step")], default)
    if len(unique) > 1:
        out.append('    sequence mode="once-hold" {')
        out.extend(f'        frame "{textures[name].id}" duration-ms={duration}' for name, duration in spans)
        out.append("    }")
    return out + ["}"]


def chart_files(chart):
    meta_path = asset_path(chart, "meta.json")
    meta = json.loads(meta_path.read_text(encoding="utf-8-sig"))
    require(meta["format"] == "dynamite-uv-pack" and meta["formatVersion"] == 2 and len(meta["charts"]) == 1
            and meta["charts"][0]["id"] == CHART_ID, "expected strict single-difficulty Ramification v2 pack")
    require(not (chart / "storyboard").exists(), "expected chart-only source pack")
    names = ["meta.json", meta["audio"], meta["charts"][0]["file"]]
    return [(name, asset_path(chart, name)) for name in names]


def build(source, chart, output, begin, end, line_width, audit_only, preview_budget=False):
    require(0 <= begin < end and end - begin <= 20_000, "select a positive excerpt of at most 20 seconds")
    require(line_width is None or (16 <= line_width < 1920 and line_width % 16 == 0),
            "--line-width must be a positive multiple of 16 below 1920")
    require(audit_only or not output.exists(), f"refusing to overwrite {output}")
    sprites, ignored = parse(source.read_text(encoding="utf-8-sig"))
    selected, selected_triggers = [], []
    commands_budget = WorkBudget(MAX_EXPANDED_COMMANDS, "expanded command")
    span_budget = WorkBudget(MAX_FRAME_STEPS, "animation span")
    for sprite in sorted(sprites, key=lambda sp: (LAYER[sp.layer], sp.line)):
        if data := expand(sprite, begin, end, commands_budget):
            first, last, commands, triggers = data
            start, until = max(begin, first), min(end, last + 1)
            selected.append((sprite, start, until, commands, frame_spans(sprite, first, start, until, span_budget)))
            selected_triggers.extend(triggers)
    require(selected, "no drawable source objects in the excerpt")
    textures, unique = inspect_textures(source.parent, {name for *_, spans in selected for name, _ in spans}, line_width)
    pixels = sum(math.prod(tx.size) for tx in unique)
    native_pixels = sum(math.prod(tx.original) for tx in unique)
    report = Counter()
    script = ['storyboard 0', 'backdrop mode="replace" color="#000000"']
    script.extend(f'asset "{tx.id}" file="assets/{tx.id}.png"' for tx in unique)
    for order, (sp, start, until, commands, spans) in enumerate(selected):
        script += emit_sprite(sp, start, until, commands, spans, textures, report, order)
    nodes = sum(line != "}" for line in map(str.strip, script))
    encoded = ("\n".join(script) + "\n").encode("utf-8")
    over = []
    asset_limit = PREVIEW_COUNT if preview_budget else MAX_COUNT
    pixel_limit = PREVIEW_PIXELS if preview_budget else None
    for value, limit, label in ((len(unique), asset_limit, "assets"), (pixels, pixel_limit, "texture pixels"),
                                (max(max(tx.size) for tx in unique), MAX_DIMENSION, "texture dimension"),
                                (nodes, MAX_NODES, "KDL nodes"), (len(encoded), MAX_SCRIPT, "script bytes")):
        if limit is not None and value > limit:
            over.append(f"{label} {value:,} > {limit:,}")
    result = {
        "range_ms": [begin, end], "source_sha256": sha256(source.read_bytes()).hexdigest(),
        "objects": len(selected), "animations": sum(sp.mode != "Sprite" for sp, *_ in selected),
        "source_layers": Counter(sp.layer for sp, *_ in selected),
        "source_paths": len(textures), "unique_textures": len(unique), "native_pixels": native_pixels,
        "texture_pixels": pixels, "texture_pixel_limit": pixel_limit, "texture_asset_limit": asset_limit,
        "script_bytes": len(encoded), "kdl_nodes": nodes, "over_limit": over,
        "track_fit": report, "ignored_other_events": ignored, "ignored_trigger_lines_in_selected_sprites": selected_triggers,
        "fractional_frame_delay_lines": [sp.line for sp, *_ in selected if sp.delay % 1],
        "textures": [dict(id=tx.id, sources=tx.aliases, original=tx.original, stored=tx.size,
                          original_alpha_zero_partial_opaque=tx.alpha) for tx in unique],
        "object_windows": [[sp.line, start, until] for sp, start, until, *_ in selected],
    }
    print(f"[{begin}, {end}) ms: {len(selected)} objects, {len(unique)} textures/{pixels:,} pixels "
          f"({native_pixels:,} native), {nodes:,} KDL nodes/{len(encoded):,} bytes; fit {dict(report)}")
    if audit_only:
        print(json.dumps(result, indent=2))
        return
    require(not over, "; ".join(over) + "; narrow the window or explicitly use --line-width for section 9")
    files = chart_files(chart)
    output.parent.mkdir(parents=True, exist_ok=True)
    # Build privately beside the final pack; failed preflight leaves no partial output.
    with TemporaryDirectory(prefix=".official-osb-", dir=output.parent) as work:
        pack = Path(work) / "pack"
        board = pack / "storyboard"
        (board / "assets").mkdir(parents=True)
        for name, src in files:
            dst = pack / name
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)
        (board / SCRIPT).write_bytes(encoded)
        (board / "index.json").write_text(json.dumps({"version": 0, "charts": {CHART_ID: SCRIPT}}) + "\n", encoding="utf-8")
        png_bytes = 0
        for tx in unique:
            out = board / "assets" / f"{tx.id}.png"
            write_texture(tx, out)
            size = out.stat().st_size
            require(size <= MAX_PNG, f"{out.name}: exceeds one-PNG byte limit")
            png_bytes += size
            require(not preview_budget or png_bytes <= PREVIEW_PNG_TOTAL, "combined preview PNG byte limit exceeded")
        result["png_bytes"] = png_bytes
        (board / "conversion-report.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        pack.rename(output)
    print(f"Wrote private pack {output} ({png_bytes:,} compressed texture bytes), chart {CHART_ID}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--osb", type=Path, default=DEFAULT_SOURCE)
    parser.add_argument("--chart-pack", type=Path, default=DEFAULT_CHART)
    parser.add_argument("--output", type=Path, default=PRIVATE / "storyboard-demo/twin-bloom-official-192-204")
    parser.add_argument("--start-ms", type=int, default=189_000)
    parser.add_argument("--end-ms", type=int, default=204_000)
    parser.add_argument("--line-width", type=int, help="explicitly resize only the six 1920×1080 section-9 line frames")
    parser.add_argument("--preview-budget", action="store_true",
                        help="require compatibility with the historical eager 96 MiB RGBA8 local preview")
    parser.add_argument("--audit-only", action="store_true", help="emit diagnostics and limits; do not write anything")
    args = parser.parse_args()
    try:
        build(private_path(args.osb), private_path(args.chart_pack), private_path(args.output),
              args.start_ms, args.end_ms, args.line_width, args.audit_only, args.preview_budget)
    except (OSError, ValueError, csv.Error) as exc:
        parser.exit(1, f"Conversion failed: {exc}\n")


if __name__ == "__main__":
    main()
