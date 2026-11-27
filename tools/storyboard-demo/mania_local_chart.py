"""Convert an approved local Twin Bloom 4K Ramification .osu into a strict UV v2 test pack."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from dataclasses import dataclass
from fractions import Fraction
import json
from pathlib import Path
import re
import shutil
import subprocess
import unicodedata

ROOT = Path(__file__).resolve().parents[2]
LOCAL = ROOT / '.tmp'
DEFAULT_OUTPUT = LOCAL / 'storyboard-demo' / 'twin-bloom-ramification'
CHART_ID = 'ramification'
BPM = 150
BAR_MS = 240_000 // BPM  # four synthetic beats; not osu! tempo/scroll interpretation
MAX_SAFE_INTEGER = (1 << 53) - 1  # exact serialized BarTime limit in shared's strict v2 decoder
CENTERS = (0.625, 1.875, 3.125, 4.375)
WIDTH = 1.0
AUDIO_FORMATS = {'.wav': ('wav', {'pcm_s16le', 'pcm_u8'}), '.mp3': ('mp3', {'mp3'}),
                 '.ogg': ('ogg', {'vorbis'}), '.flac': ('flac', {'flac'})}
# Mirror shared FlacDecoderOptions.GamePlayback; PCM is always 16-bit interleaved.
FLAC_INPUT_BYTES = FLAC_PCM_BYTES = 64 << 20


@dataclass(frozen=True)
class Hit:
    line: int
    column: int  # zero-based, left to right
    head: int
    tail: int | None = None

    def describe(self) -> str:
        interval = str(self.head) if self.tail is None else f'{self.head}–{self.tail}'
        return f'line {self.line} @{interval} ms'


def local_path(path: Path, *, source: bool) -> Path:
    if LOCAL.resolve() != LOCAL:
        raise ValueError('repository .tmp must not redirect outside itself')
    resolved = path.resolve(strict=source)
    if LOCAL not in resolved.parents:
        raise ValueError(f'chart input and pack output must be below the repo’s ignored .tmp/: {path}')
    return resolved


def integer(token: str) -> int:
    if not re.fullmatch(r'[0-9]+', token.strip()):
        raise ValueError(f'expected a nonnegative integer, got {token!r}')
    return int(token)


def parse_hit(text: str, number: int) -> Hit:
    try:
        fields = text.split(',', 5)
        if len(fields) < 5:
            raise ValueError('expected x,y,time,type,hitSound')
        x, _y, head, type_bits, _sound = map(integer, fields[:5])
        if x >= 512:
            raise ValueError(f'x={x} is outside [0, 512)')
        column = x * 4 // 512
        # The new-combo/colour-skip bits are not object types.
        kind = type_bits & ~(4 | 16 | 32 | 64)
        if kind == 1:
            return Hit(number, column, head)
        if kind != 128:
            raise ValueError(f'unsupported or mixed hit-object type {type_bits}; expected Tap or Hold')
        if len(fields) != 6 or ':' not in fields[5]:
            raise ValueError('expected Hold end time followed by a colon')
        tail = integer(fields[5].split(':', 1)[0])
        if tail <= head:
            raise ValueError(f'Hold tail {tail} must be later than head {head}')
        return Hit(number, column, head, tail)
    except ValueError as error:
        raise ValueError(f'[HitObjects] line {number}: {error}') from error


def parse_osu(path: Path) -> tuple[dict[str, str], list[Hit]]:
    fields: dict[tuple[str, str], str] = {}
    hits: list[Hit] = []
    section = ''
    with path.open(encoding='utf-8-sig') as stream:
        if not re.fullmatch(r'osu file format v[0-9]+', next(stream, '').strip()):
            raise ValueError(f'not an osu! beatmap: {path}')
        for number, raw in enumerate(stream, 2):
            text = raw.strip()
            if not text or text.startswith('//'):
                continue
            if text.startswith('[') and text.endswith(']'):
                section = text[1:-1]
            elif section == 'HitObjects':
                hits.append(parse_hit(text, number))
            elif section in {'General', 'Difficulty', 'Metadata'} and ':' in text:
                key, value = (part.strip() for part in text.split(':', 1))
                if key not in {'Mode', 'CircleSize', 'Title', 'TitleUnicode', 'Artist', 'ArtistUnicode',
                               'Version', 'Creator', 'AudioFilename', 'BeatmapID'}:
                    continue
                if (section, key) in fields:
                    raise ValueError(f'line {number}: duplicate [{section}] {key}')
                fields[section, key] = value
    if fields.get(('General', 'Mode')) != '3' or fields.get(('Difficulty', 'CircleSize')) not in {'4', '4.0'}:
        raise ValueError('expected osu!mania Mode:3 and 4K CircleSize:4')
    metadata = {key: value for (section, key), value in fields.items() if section == 'Metadata'}
    if (metadata.get('Title'), metadata.get('Version'), metadata.get('BeatmapID')) != (
            'Twin Bloom', 'Ramification', '5893787'):
        raise ValueError('expected Twin Bloom [Ramification], BeatmapID 5893787')
    if not hits:
        raise ValueError('missing or empty [HitObjects]')
    metadata['SourceAudio'] = fields.get(('General', 'AudioFilename'), '')
    return metadata, sorted(hits, key=lambda hit: (hit.head, hit.column, hit.line))


def media_time(ms: int) -> dict[str, int]:
    time = Fraction(ms, BAR_MS)
    bar, numerator = divmod(time.numerator, time.denominator)
    if not 0 <= bar <= MAX_SAFE_INTEGER:
        raise ValueError(f'time {ms} ms falls outside strict v2 BarTime range')
    return {'bar': bar, 'numerator': numerator, 'denominator': time.denominator}


def media_ms(time: dict[str, int]) -> Fraction:
    return BAR_MS * (time['bar'] + Fraction(time['numerator'], time['denominator']))


def point(id_: str, hit: Hit, time: int) -> dict:
    return {'id': id_, 'time': media_time(time), 'center': CENTERS[hit.column], 'width': WIDTH}


def make_chart(hits: list[Hit]) -> dict:
    notes = []
    for index, hit in enumerate(hits, 1):
        id_ = f'hit-{index:05}'
        head = point(id_, hit, hit.head)
        if hit.tail is None:
            head['type'] = 'tap'
        else:
            tail = point(f'{id_}-tail', hit, hit.tail)
            head.update(type='hold', curveToNext='linear', nodes=[{**tail, 'judge': True}])
        notes.append(head)
    # Audit the actual serialized structures, including column, head, tail and tie ordering.
    rendered = [(CENTERS.index(note['center']), media_ms(note['time']),
                 media_ms(note['nodes'][0]['time']) if note['type'] == 'hold' else None) for note in notes]
    if rendered != [(hit.column, hit.head, hit.tail) for hit in hits]:
        raise ValueError('internal error: output chart does not round-trip every source hit exactly')
    return {'format': 'dynamite-uv-chart', 'formatVersion': 2, 'chartId': CHART_ID,
            'audioOffsetSec': 0, 'timing': {'bpms': [{'time': media_time(0), 'bpm': BPM}]},
            'notesLeft': [], 'notesCenter': notes, 'notesRight': []}


def same_column_conflicts(hits: list[Hit]):
    active: list[list[Hit]] = [[] for _ in CENTERS]
    for hit in hits:
        preceding = active[hit.column]
        preceding[:] = [p for p in preceding if (p.tail if p.tail is not None else p.head) >= hit.head]
        for prior in preceding:
            if prior.head == hit.head:
                kind = 'duplicate-head'
            elif prior.tail == hit.head:
                kind = 'tail-touches-head'
            elif hit.tail is None:
                kind = 'tap-inside-hold'
            else:
                kind = 'overlapping-holds'
            yield kind, prior, hit
        preceding.append(hit)


def print_counts(label: str, hits: list[Hit], start: int, end: int) -> None:
    columns = [Counter() for _ in CENTERS]
    by_head = defaultdict(set)
    by_event = Counter()
    for hit in hits:
        if start <= hit.head < end:
            columns[hit.column]['taps' if hit.tail is None else 'hold_heads'] += 1
            by_head[hit.head].add(hit.column)
            by_event[hit.head] += 1
        if hit.tail is not None and start <= hit.tail < end:
            columns[hit.column]['tails'] += 1
            by_event[hit.tail] += 1
    taps, holds, tails = (sum(c[key] for c in columns) for key in ('taps', 'hold_heads', 'tails'))
    chords = Counter(len(cols) for cols in by_head.values())
    print(f'{label}: {taps} taps, {holds} hold heads, {tails} hold tails, {taps + holds + tails} judge units')
    print('  C1–C4 taps/hold heads/hold tails: ' + '  '.join(
        f'{c["taps"]}/{c["hold_heads"]}/{c["tails"]}' for c in columns))
    print(f'  Head instants: {len(by_head)}; multi-column chords 2K={chords[2]}, 3K={chords[3]}, 4K={chords[4]}; '
          f'max simultaneous judge units (including tails)={max(by_event.values(), default=0)}')


def print_audit(hits: list[Hit], window: tuple[int, int] | None) -> int:
    last = max(hit.tail if hit.tail is not None else hit.head for hit in hits)
    print(f'Full source: first head {hits[0].head} ms, last judge unit {last} ms')
    print_counts('Full chart', hits, 0, last + 1)
    kinds = ('duplicate-head', 'tap-inside-hold', 'overlapping-holds', 'tail-touches-head')
    counts = Counter()
    samples: dict[str, list[str]] = defaultdict(list)
    for kind, prior, hit in same_column_conflicts(hits):
        counts[kind] += 1
        if len(samples[kind]) < 5:
            samples[kind].append(f'C{hit.column + 1}: {prior.describe()} vs {hit.describe()}')
    print('Same-column source pairs: ' + ', '.join(f'{kind}={counts[kind]}' for kind in kinds))
    for kind in kinds:
        for sample in samples[kind]:
            print(f'  {kind}: {sample}')
    if any(counts.values()):
        print('  Conflicts are reported, not moved or removed; judge/playability may differ from osu!mania.')
    if window is not None:
        start, end = window
        print_counts(f'Window [{start}, {end}) ms', hits, start, end)
        carry = [hit for hit in hits if hit.tail is not None and hit.head < start <= hit.tail]
        cut = [hit for hit in hits if hit.tail is not None and hit.head < end <= hit.tail]
        print(f'  Holds entering at seek (including tails exactly at start): {len(carry)}; '
              f'holds still pending at exclusive end: {len(cut)}')
        for boundary, objects in (('start', carry), ('end', cut)):
            for hit in objects:
                print(f'    {boundary} C{hit.column + 1}: {hit.describe()}')
        print(f'  Seek: $env:DYNAMITE_UNIVERSE_START_SEC = \'{start // 1000}.{start % 1000:03}\'; '
              f'measure to {end // 1000}.{end % 1000:03} s media time in the caller')
        if carry:
            print('  For manual testing, choose a clean start before those Hold heads; seeking does not replay earlier input.')
    return last


def flac_duration_for_game(path: Path) -> Fraction:
    length = path.stat().st_size
    if not 0 < length <= FLAC_INPUT_BYTES:
        raise ValueError('FLAC input must be nonempty and no larger than 64 MiB for game playback')
    with path.open('rb') as stream:
        lead = stream.read(10)
        offset = 0
        if lead.startswith(b'ID3'):
            if len(lead) != 10 or any(byte >= 128 for byte in lead[6:]):
                raise ValueError('invalid FLAC leading ID3v2 tag')
            for byte in lead[6:]:
                offset = (offset << 7) | byte
            offset += 10 + (10 if lead[5] & 0x10 else 0)
        stream.seek(offset)
        header = stream.read(42)
    if len(header) != 42 or header[:4] != b'fLaC' or header[4] & 0x7f or header[5:8] != b'\x00\x00\x22':
        raise ValueError('invalid FLAC STREAMINFO header')
    min_block, max_block = int.from_bytes(header[8:10], 'big'), int.from_bytes(header[10:12], 'big')
    fields = int.from_bytes(header[18:26], 'big')
    rate, channels, depth = fields >> 44, ((fields >> 41) & 7) + 1, ((fields >> 36) & 31) + 1
    samples = fields & ((1 << 36) - 1)
    if not (16 <= min_block <= max_block and 8000 <= rate <= 192000 and channels in (1, 2) and depth in (8, 16, 24)):
        raise ValueError('FLAC STREAMINFO has a block size, sample rate, channel count, or bit depth unsupported by game playback')
    if not samples:
        raise ValueError('FLAC STREAMINFO must declare its sample count to check the game playback budget')
    if samples * channels * 2 > FLAC_PCM_BYTES:
        raise ValueError('FLAC decoded PCM exceeds the 64 MiB game playback limit; use another supported format')
    return Fraction(samples, rate)


def probe_audio(path: Path) -> tuple[str, Fraction]:
    extension = path.suffix.lower()
    if extension not in AUDIO_FORMATS or not path.is_file():
        raise ValueError('expected a user-provided regular .mp3, PCM .wav, Vorbis .ogg, or .flac file')
    flac_duration = flac_duration_for_game(path) if extension == '.flac' else None
    ffprobe = shutil.which('ffprobe')
    if ffprobe is None:
        raise ValueError('ffprobe is required on PATH to check local audio format and duration (not used in --audit-only)')
    process = subprocess.run([ffprobe, '-v', 'error', '-select_streams', 'a', '-show_entries',
                              'format=format_name,duration:stream=codec_name,channels', '-of', 'json', str(path)],
                             capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=30)
    if process.returncode != 0 or process.stderr.strip():
        raise ValueError(f'ffprobe could not inspect local audio: {process.stderr.strip()}')
    info = json.loads(process.stdout)
    streams = info.get('streams', [])
    media = info.get('format', {})
    format_name, codecs = AUDIO_FORMATS[extension]
    if (len(streams) != 1 or media.get('format_name') != format_name
            or streams[0].get('codec_name') not in codecs or streams[0].get('channels') not in {1, 2}):
        raise ValueError(f'{extension} does not contain one supported mono/stereo {format_name} audio stream')
    if flac_duration is not None:
        return 'audio' + extension, flac_duration  # ffprobe formats seconds to a finite number of decimals
    try:
        duration = Fraction(media['duration'])
    except (KeyError, ValueError, ZeroDivisionError) as error:
        raise ValueError('ffprobe did not report a valid audio duration') from error
    if duration <= 0:
        raise ValueError('audio duration must be positive')
    return 'audio' + extension, duration


def write_json(path: Path, value: dict) -> None:
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.write('\n')


def write_pack(output: Path, chart: dict, metadata: dict[str, str], audio: Path, audio_name: str) -> None:
    chart_file = f'charts/{CHART_ID}.json'
    display = lambda key: unicodedata.normalize('NFC', metadata.get(key + 'Unicode') or metadata.get(key, ''))
    title, artist, creator = display('Title'), display('Artist'), display('Creator')
    if any(not value or any(unicodedata.category(c) == 'Cc' for c in value) for value in (title, artist, creator)):
        raise ValueError('source metadata must supply a valid Title, Artist and Creator')
    meta = {'format': 'dynamite-uv-pack', 'formatVersion': 2, 'id': 'local.mania-twin-bloom', 'revision': 1,
            'title': title, 'artist': artist, 'audio': audio_name,
            'charts': [{'id': CHART_ID, 'difficulty': 'custom', 'difficultyKey': 'Ramification',
                        'unrated': True, 'charters': [creator], 'file': chart_file}]}
    # The destination directory must be new; never replace an existing pack or clear a partial run.
    output.mkdir(parents=True, exist_ok=False)
    (output / 'charts').mkdir()
    with audio.open('rb') as source, (output / audio_name).open('xb') as target:
        shutil.copyfileobj(source, target)
    write_json(output / chart_file, chart)
    write_json(output / 'meta.json', meta)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('osu', type=Path, help='.osu source below this repository’s ignored .tmp/')
    parser.add_argument('--audio', type=Path, help='user-approved local full-song audio; copied byte for byte')
    parser.add_argument('--output', type=Path, help=f'new pack directory below .tmp/ (default: {DEFAULT_OUTPUT})')
    parser.add_argument('--start-ms', type=int, help='optional included benchmark start in raw media ms')
    parser.add_argument('--end-ms', type=int, help='optional excluded benchmark end in raw media ms')
    parser.add_argument('--audit-only', action='store_true', help='audit the source without requiring audio or writing files')
    args = parser.parse_args()
    if (args.start_ms is None) != (args.end_ms is None) or (
            args.start_ms is not None and not 0 <= args.start_ms < args.end_ms):
        parser.error('provide both window boundaries with 0 <= --start-ms < --end-ms')
    if args.audit_only and (args.audio is not None or args.output is not None):
        parser.error('--audit-only cannot be combined with --audio or --output')
    if not args.audit_only and args.audio is None:
        parser.error('--audio is required for a playable pack (or use --audit-only)')
    window = None if args.start_ms is None else (args.start_ms, args.end_ms)
    try:
        source = local_path(args.osu, source=True)
        if source.suffix.lower() != '.osu' or not source.is_file():
            raise ValueError('expected a regular .osu source file')
        metadata, hits = parse_osu(source)
        chart = make_chart(hits)
        last = print_audit(hits, window)
        print(f'Exact rational round trip verified for all {len(hits)} parents and their tails, synthetic 150 BPM.')
        if args.audit_only:
            return 0
        output = local_path(args.output or DEFAULT_OUTPUT, source=False)
        audio = args.audio.resolve(strict=True)
        audio_name, duration = probe_audio(audio)
        if last > duration * 1000 or (window is not None and window[1] > duration * 1000):
            raise ValueError('audio duration does not cover the final judge unit and selected window')
        if audio.name.casefold() != metadata['SourceAudio'].casefold():
            print(f'  Audio filename differs from [General] AudioFilename={metadata["SourceAudio"]!r}; '
                  'confirm it is the same unshifted, full cue.')
        write_pack(output, chart, metadata, audio, audio_name)
        print(f'Local pack: {output}\nChart ID: {CHART_ID}\nAudio: {audio_name}, {float(duration):.3f} s (full source copy)')
        print(f'Strict validator: dotnet run --project tools/chart-tool -- validate-v2 "{output}"')
    except (OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
        parser.exit(1, f'error: {error}\n')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
