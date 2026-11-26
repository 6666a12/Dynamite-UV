"""Generate or verify the site-only OFL derivative of Adobe Source Han Sans CN."""
from __future__ import annotations

import argparse
import hashlib
from io import BytesIO
import json
from pathlib import Path

from check import CJK_FAMILY, CJK_FONT, ROOT, SITE, common_cjk, describe_points, site_cjk

SOURCE = ROOT / 'client/assets/fonts/SourceHanSansCN-VF.woff2'
SOURCE_SHA256 = 'f971e3bff46f76b49e1d5510556c2297c618ec4b491a295a4e741cdd38257799'
POSTSCRIPT_FAMILY = 'DynaUVCJK'
DEFAULT_POSTSCRIPT = POSTSCRIPT_FAMILY + '-ExtraLight'
FAMILY_FIELDS = {
    1: CJK_FAMILY, 3: f'2.005;DYNA;{DEFAULT_POSTSCRIPT}', 4: CJK_FAMILY,
    6: DEFAULT_POSTSCRIPT, 16: CJK_FAMILY, 18: CJK_FAMILY,
    20: DEFAULT_POSTSCRIPT, 21: CJK_FAMILY, 25: POSTSCRIPT_FAMILY,
}
CREDITS_FIELDS = (0, 7, 8, 9, 10, 11, 13, 14)


def rename(font) -> None:
    names = font['name']
    instances = {instance.postscriptNameID: names.getDebugName(instance.subfamilyNameID)
                 for instance in font['fvar'].instances if instance.postscriptNameID != 0xFFFF}
    for record in names.names:
        if record.nameID in FAMILY_FIELDS:
            record.string = FAMILY_FIELDS[record.nameID]
        elif record.nameID in instances:
            record.string = f'{POSTSCRIPT_FAMILY}-{instances[record.nameID]}'


def validate(font, expected: set[int]) -> None:
    points = set(font.getBestCmap())
    if missing := expected - points:
        raise ValueError(f'Missing Chinese glyphs: {describe_points(missing)}')
    if extra := points - expected:
        raise ValueError(f'Unexpected cmap entries: {describe_points(extra)}')
    if [(axis.axisTag, axis.minValue, axis.maxValue) for axis in font['fvar'].axes] != [('wght', 250, 900)]:
        raise ValueError('Expected the regional font’s original 250–900 weight axis')
    names = font['name']
    for record in names.names:
        value = record.toUnicode()
        if record.nameID in FAMILY_FIELDS and value != FAMILY_FIELDS[record.nameID]:
            raise ValueError(f'Unexpected public font name: {value}')
        if record.nameID not in CREDITS_FIELDS and ('Source' in value or '思源' in value):
            raise ValueError(f'Original font name left in derivative: {value}')
    for name_id in (1, 3, 4, 6, 0, 13, 14):
        if not names.getDebugName(name_id):
            raise ValueError(f'Missing name record {name_id}')
    if not all((names.getDebugName(instance.postscriptNameID) or '').startswith(f'{POSTSCRIPT_FAMILY}-')
               for instance in font['fvar'].instances if instance.postscriptNameID != 0xFFFF):
        raise ValueError('Named weight has an unexpected PostScript name')


def generate(source: Path, expected: set[int]) -> bytes:
    from fontTools import subset
    from fontTools.ttLib import TTFont

    original = source.read_bytes()
    if hashlib.sha256(original).hexdigest() != SOURCE_SHA256:
        raise ValueError(f'{source}: expected SHA-256 {SOURCE_SHA256} (Adobe 2.005R CN variable TTF-flavored WOFF2)')
    with TTFont(BytesIO(original), recalcTimestamp=False) as font:
        credits = [(record.nameID, record.toUnicode()) for record in font['name'].names
                   if record.nameID in CREDITS_FIELDS]
        options = subset.Options()
        options.name_IDs = ['*']
        options.name_languages = ['*']
        options.name_legacy = True
        options.ignore_missing_unicodes = False
        subsetter = subset.Subsetter(options=options)
        subsetter.populate(unicodes=expected)
        subsetter.subset(font)
        rename(font)
        if credits != [(record.nameID, record.toUnicode()) for record in font['name'].names
                       if record.nameID in CREDITS_FIELDS]:
            raise ValueError('Attribution and OFL metadata changed')
        font.flavor = 'woff2'
        output = BytesIO()
        font.save(output)
    return output.getvalue()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=SOURCE, help='byte-exact upstream WOFF2 override')
    parser.add_argument('--output', type=Path, default=SITE / CJK_FONT)
    parser.add_argument('--check', action='store_true', help='verify the existing output cmap and names without rebuilding')
    args = parser.parse_args()
    try:
        from fontTools.ttLib import TTFont

        common = common_cjk()
        required = site_cjk(SITE)
        expected = common | required
        data = args.output.read_bytes() if args.check else generate(args.source, expected)
        if len(data) > 2_000_000 or data[:4] != b'wOF2':
            raise ValueError('Expected a WOFF2 under the 2 MB website file budget')
        with TTFont(BytesIO(data)) as font:
            validate(font, expected)
        if not args.check:
            args.output.write_bytes(data)
        print(f'{args.output}: {len(data):,} bytes, {len(expected):,} mapped code points, '
              f'{len(required):,} required by the site')
        print(f'SHA-256: {hashlib.sha256(data).hexdigest()}')
        extras = ''.join(map(chr, sorted(required - common)))
        print(f'assets.json extraCharacters: {json.dumps(extras)}')
    except ImportError as error:
        parser.exit(1, f'{error}; install tools/site/requirements-fonts.txt first\n')
    except (OSError, ValueError) as error:
        parser.exit(1, f'{error}\n')


if __name__ == '__main__':
    main()
