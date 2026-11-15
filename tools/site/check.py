"""Validate the curated Pages payload without starting apps or accessing the network."""
from __future__ import annotations

import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit
import wave

ROOT = Path(__file__).resolve().parents[2]
SITE = ROOT / 'site'


class Page(HTMLParser):
    def __init__(self, path: Path):
        super().__init__(convert_charrefs=True)
        self.path = path
        self.ids: set[str] = set()
        self.links: list[str] = []
        self.errors: list[str] = []
        self.has_lang = False
        self.has_viewport = False
        self.feed(path.read_text(encoding='utf-8'))

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if 'id' in values:
            identity = values['id']
            if identity in self.ids:
                self.errors.append(f'duplicate id: {identity}')
            self.ids.add(identity)
        if tag == 'html':
            self.has_lang = values.get('lang') == 'zh-CN'
        if tag == 'meta' and values.get('name') == 'viewport':
            self.has_viewport = True
        if tag == 'img' and (not values.get('alt') and values.get('alt') != ''):
            self.errors.append('image lacks alt text')
        if tag == 'img' and not all(values.get(key) for key in ('width', 'height')):
            self.errors.append('image lacks explicit dimensions')
        if tag == 'script' and not values.get('src'):
            self.errors.append('inline scripts are not allowed')
        for key, value in attrs:
            if key.startswith('on'):
                self.errors.append('inline event handlers are not allowed')
            if key in ('href', 'src') and value:
                self.links.append(value)


def asset_bytes(path: Path) -> bytes:
    data = path.read_bytes()
    # Git normalizes text on check-in; Windows checkouts may restore CRLF.
    # Only license text is normalized. Fonts, audio and images stay byte-exact.
    return data.replace(b'\r\n', b'\n') if path.suffix == '.txt' else data


def check() -> list[str]:
    errors: list[str] = []
    manifest = json.loads((ROOT / 'tools/site/assets.json').read_text(encoding='utf-8'))
    expected = {'index.html', 'guide.html', 'assets/site.css', 'assets/site.js', 'assets/mark.svg'}
    expected.update(asset['path'] for asset in manifest['assets'])
    actual = {path.relative_to(SITE).as_posix() for path in SITE.rglob('*') if path.is_file()}
    if expected != actual:
        errors.append(f'payload differs: missing={sorted(expected-actual)}, unexpected={sorted(actual-expected)}')
    for path in SITE.rglob('*'):
        if path.is_symlink():
            errors.append(f'symlink forbidden: {path}')
        if path.is_file() and path.stat().st_size > 2_000_000:
            errors.append(f'file exceeds website budget: {path}')
    for asset in manifest['assets']:
        path = SITE / asset['path']
        if not path.is_file():
            continue
        if hashlib.sha256(asset_bytes(path)).hexdigest() != asset['sha256']:
            errors.append(f'asset hash differs: {asset["path"]}')
        if source := asset.get('source'):
            original = ROOT / source
            if not original.is_file() or asset_bytes(original) != asset_bytes(path):
                errors.append(f'licensed asset differs from source: {source}')
    pages = {path.resolve(): Page(path) for path in SITE.glob('*.html')}
    for path, page in pages.items():
        errors.extend(f'{path.name}: {error}' for error in page.errors)
        if not page.has_lang or not page.has_viewport:
            errors.append(f'{path.name}: missing language or viewport')
    references = [(page.path, url) for page in pages.values() for url in page.links]
    css = SITE / 'assets/site.css'
    references.extend((css, match) for match in re.findall(r'url\([\'"]?([^\)\'\"]+)', css.read_text(encoding='utf-8')))
    for source, reference in references:
        url = urlsplit(reference)
        if url.scheme or url.netloc:
            if url.scheme != 'https' or url.netloc != 'github.com':
                errors.append(f'unexpected external URL: {reference}')
            if source.suffix == '.css':
                errors.append('external CSS assets are not allowed')
            continue
        if url.path.startswith('/'):
            errors.append(f'root-relative URL breaks project Pages: {reference}')
            continue
        target = (source.parent / unquote(url.path)).resolve() if url.path else source.resolve()
        if target.is_dir():
            target /= 'index.html'
        if not target.is_relative_to(SITE.resolve()) or not target.is_file():
            errors.append(f'{source.name}: missing/outside target {reference}')
        elif url.fragment and (target not in pages or unquote(url.fragment) not in pages[target].ids):
            errors.append(f'{source.name}: missing fragment {reference}')
    guide = pages.get((SITE / 'guide.html').resolve())
    if guide is None or len(guide.ids) < 12:
        errors.append('guide is missing chapters')
    audio = SITE / 'assets/practice-120bpm.wav'
    if audio.is_file():
        with wave.open(str(audio)) as wav:
            if (wav.getnchannels(), wav.getsampwidth(), wav.getframerate(), wav.getnframes()) != (1, 2, 22050, 352800):
                errors.append('practice WAV format/duration differs')
    return errors


if __name__ == '__main__':
    issues = check()
    if issues:
        print('\n'.join(issues), file=sys.stderr)
        raise SystemExit(1)
    print('SITE CHECK PASS: curated payload, asset hashes, links, anchors, metadata and practice audio')
