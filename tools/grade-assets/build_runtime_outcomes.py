"""Package the seven SURGE result wordmarks as Godot runtime textures.

Source of truth is the ignored local design directory
``design/result-outcomes/surge/`` (1600x400 transparent sheets rendered from the
clean-room geometric glyph generator ``build_surge.py``; no original-game pixels).
The client shows each sheet at 80% (1280x320) — the sheet keeps its 1600x400
resolution in ``res://`` so the display scale is the only place the 80% rule lives.

This script owns ``client/assets/results/outcomes/`` only. The Blender/Penpot grade
pipeline (``build_runtime_results.py``) skips that subdirectory on purpose so these
files never inherit a wrong Blender source line.

Run: python -B tools/grade-assets/build_runtime_outcomes.py
"""
from pathlib import Path
import hashlib
import json
import shutil

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'design/result-outcomes/surge'
DEST = ROOT / 'client/assets/results/outcomes'
SLUGS = ('track-complete', 'all-perfect', 'full-combo', 'one-miss', 'one-good', 'double-one', 'stage-failure')
WIDTH, HEIGHT = 1600, 400
BASE36 = '0123456789abcdefghijklmnopqrstuvwxyz'
PROVENANCE_PREFIX = 'client/assets/results/outcomes/'

IMPORT_PARAMS = '''
[params]

compress/mode=0
compress/high_quality=false
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=0
compress/channel_pack=0
mipmaps/generate=false
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=true
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=1
'''


def read_text(path):
    """Decode UTF-8 and report the file's own EOL so rewriting keeps the diff local."""
    raw = path.read_bytes()
    eol = '\r\n' if b'\r\n' in raw else '\n'
    return raw.decode('utf-8').replace('\r\n', '\n'), eol


def write_text(path, text, eol):
    path.write_bytes(text.replace('\n', eol).encode('utf-8'))


def resource_path(slug):
    return f'res://assets/results/outcomes/{slug}.png'


def uid_of(slug):
    """Stable base-36 uid so re-running never renumbers already referenced assets."""
    digest = hashlib.sha256(resource_path(slug).encode('utf-8')).digest()
    value = int.from_bytes(digest[:8], 'big') & ((1 << 63) - 1)
    digits = ''
    while value:
        value, remainder = divmod(value, 36)
        digits = BASE36[remainder] + digits
    return 'uid://' + (digits or '1')


def import_text(slug):
    resource = resource_path(slug)
    cached = ('res://.godot/imported/' + slug + '.png-'
              + hashlib.md5(resource.encode('utf-8')).hexdigest() + '.ctex')
    return (f'[remap]\n\nimporter="texture"\ntype="CompressedTexture2D"\nuid="{uid_of(slug)}"\n'
            f'path="{cached}"\nmetadata={{\n"vram_texture": false\n}}\n\n[deps]\n\n'
            f'source_file="{resource}"\ndest_files=["{cached}"]\n' + IMPORT_PARAMS)


def copy_sheets():
    DEST.mkdir(parents=True, exist_ok=True)
    for slug in SLUGS:
        source = SOURCE / f'{slug}.png'
        with Image.open(source) as image:
            assert image.size == (WIDTH, HEIGHT), (slug, image.size)
            assert image.mode == 'RGBA', (slug, image.mode)
        shutil.copyfile(source, DEST / f'{slug}.png')
        (DEST / f'{slug}.png.import').write_bytes(import_text(slug).encode('utf-8'))
    for stray in sorted(DEST.glob('*.png')):
        if stray.stem not in SLUGS:
            raise SystemExit(f'unexpected outcome sheet: {stray.name}')


WORDMARK_SHADER = 'res://shaders/wordmark_diffuse.gdshader'


def update_export_files():
    presets = ROOT / 'client/export_presets.cfg'
    data, eol = read_text(presets)
    marker = 'export_files=PackedStringArray('
    start = data.index(marker) + len(marker)
    end = data.index(')', start)
    files = [value.strip().strip('"') for value in data[start:end].split(',')]
    for path in [WORDMARK_SHADER] + ['res://' + sheet.relative_to(ROOT / 'client').as_posix()
                                     for sheet in sorted(DEST.glob('*.png'))]:
        if path not in files:
            files.append(path)
    write_text(presets, data[:start] + ', '.join(json.dumps(v) for v in files) + data[end:], eol)


def update_provenance():
    path = ROOT / 'release/asset-provenance.json'
    data, eol = read_text(path)
    provenance = json.loads(data)
    provenance['assets'] = [entry for entry in provenance['assets']
                            if not entry['path'].startswith(PROVENANCE_PREFIX)]
    generator = Path(__file__).resolve()
    for sheet in sorted(DEST.glob('*.png')):
        provenance['assets'].append({
            'path': sheet.relative_to(ROOT).as_posix(),
            'sha256': hashlib.sha256(sheet.read_bytes()).hexdigest(),
            'kind': 'project-generated-ui-asset',
            'source': 'Clean-room SURGE result wordmarks: original geometric glyph SVG/PNG generated by '
                      'design/result-outcomes/surge/build_surge.py; no original-game pixels',
            'generator': generator.relative_to(ROOT).as_posix(),
            'generatorSha256': hashlib.sha256(generator.read_bytes()).hexdigest(),
        })
    write_text(path, json.dumps(provenance, ensure_ascii=False, indent=2) + '\n', eol)


def main():
    copy_sheets()
    update_export_files()
    update_provenance()
    print(f'Packaged {len(SLUGS)} outcome sheets ({WIDTH}x{HEIGHT}) + .import stubs; '
          'Public export list and provenance updated.')


if __name__ == '__main__':
    main()
