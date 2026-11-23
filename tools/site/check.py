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
# List every public route explicitly; an extra HTML file still fails the payload check.
EDITIONS = {'zh-CN': Path(), 'en': Path('en')}
ROUTES = ('index.html', 'guide.html')
GUIDE_ANCHORS = {
    'prepare', 'workspace', 'panel-layout', 'tools-panel', 'new-package',
    'first-note', 'edit', 'inspector-panel', 'batch-panel', 'sustain',
    'timing', 'timing-panel', 'preview', 'export-video', 'save', 'cover-studio',
    'shortcuts', 'faq',
}


class Page(HTMLParser):
    def __init__(self, path: Path):
        super().__init__(convert_charrefs=True)
        self.path = path
        self.ids: set[str] = set()
        self.links: list[str] = []
        self.errors: list[str] = []
        self.lang = ''
        self.alternates: list[tuple[str, str]] = []
        self.language_links: list[tuple[str, str]] = []
        self.navigation: list[str] = []
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
            self.lang = values.get('lang', '')
        if tag == 'link' and 'alternate' in (values.get('rel') or '').split():
            self.alternates.append((values.get('hreflang', ''), values.get('href', '')))
        if tag == 'a' and 'data-language-link' in values:
            self.language_links.append((values.get('hreflang', ''), values.get('href', '')))
        elif tag == 'a' and values.get('href'):
            self.navigation.append(values['href'])
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


def local_target(source: Path, url_path: str) -> Path:
    target = (source.parent / unquote(url_path)).resolve() if url_path else source.resolve()
    return target / 'index.html' if target.is_dir() else target


def edition_errors(pages: dict[Path, Page]) -> list[str]:
    errors = []
    for lang, directory in EDITIONS.items():
        for route in ROUTES:
            path = (SITE / directory / route).resolve()
            if (page := pages.get(path)) is None:
                continue  # Already reported by the payload check.
            name = path.relative_to(SITE).as_posix()
            peers = {(locale, (SITE / folder / route).resolve()) for locale, folder in EDITIONS.items()}

            def destinations(links: list[tuple[str, str]]) -> list[tuple[str, Path]]:
                return sorted((locale, local_target(path, urlsplit(link).path)) for locale, link in links)

            if page.lang != lang or not page.has_viewport:
                errors.append(f'{name}: expected lang={lang} and viewport')
            if destinations(page.alternates) != sorted(peers):
                errors.append(f'{name}: wrong hreflang alternates')
            if destinations(page.language_links) != sorted(pair for pair in peers if pair[0] != lang):
                errors.append(f'{name}: incorrect language switch')
            urls = map(urlsplit, page.navigation)
            internal = {local_target(path, url.path) for url in urls if not url.netloc and not url.scheme}
            for target in internal:
                if target in pages and pages[target].lang != lang:
                    errors.append(f'{name}: navigation leaves the selected language: {target.name}')
            sibling = 'guide.html' if route == 'index.html' else 'index.html'
            if (path.parent / sibling) not in internal:
                errors.append(f'{name}: no link to {sibling} in this language')
            if route == 'guide.html' and (missing := GUIDE_ANCHORS - page.ids):
                errors.append(f'{name}: missing guide sections: {sorted(missing)}')
            for _, peer in peers:
                if peer in pages and (missing := page.ids - pages[peer].ids):
                    errors.append(f'{name}: anchors missing from alternate: {sorted(missing)}')
    return errors


def check() -> list[str]:
    errors: list[str] = []
    manifest = json.loads((ROOT / 'tools/site/assets.json').read_text(encoding='utf-8'))
    expected = {(directory / route).as_posix() for directory in EDITIONS.values() for route in ROUTES}
    expected.update(('assets/site.css', 'assets/site.js', 'assets/mark.svg'))
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
    pages = {path.resolve(): Page(path) for path in SITE.rglob('*.html')}
    for path, page in pages.items():
        errors.extend(f'{path.relative_to(SITE)}: {error}' for error in page.errors)
    errors.extend(edition_errors(pages))
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
        target = local_target(source, url.path)
        if not target.is_relative_to(SITE.resolve()) or not target.is_file():
            errors.append(f'{source.name}: missing/outside target {reference}')
        elif url.fragment and (target not in pages or unquote(url.fragment) not in pages[target].ids):
            errors.append(f'{source.name}: missing fragment {reference}')
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
