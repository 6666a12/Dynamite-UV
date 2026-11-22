"""Inputs and isolation boundary for the optional standalone recording worker."""
from __future__ import annotations

from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import re
import shutil
import stat
import subprocess
import tempfile
from typing import Iterator

from capture_pck import REGISTRY, resource_path

CAPTURE_EXTENSION = 'native/video_stream/uv_video_stream.gdextension'
CAPTURE_DLL = 'uv_video_stream.dll'
CAPTURE_VERSION = '4.7.1.stable.mono.official.a13da4feb'


def sha256(path: Path) -> str:
    with path.open('rb') as handle:
        return hashlib.file_digest(handle, 'sha256').hexdigest()


def verify_engine(executable: Path) -> str:
    # Godot handles --version before project/scene initialization. Use the actual
    # GUI worker, not the console wrapper (whose forwarding is path-dependent).
    result = subprocess.run([str(executable.resolve()), '--headless', '--version'], cwd=executable.resolve().parent,
                            capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=30)
    if result.returncode or result.stdout.strip() != CAPTURE_VERSION:
        raise ValueError(f'{executable.name} does not report the pinned Godot {CAPTURE_VERSION}: '
                         f'{result.stdout.strip() or result.stderr.strip() or result.returncode}')
    return CAPTURE_VERSION


def verify_native(root: Path, native: Path, variant: str) -> str:
    descriptor = native / Path(CAPTURE_EXTENSION).name
    expected = root / 'tools/gameplay-capture/native' / descriptor.name
    if descriptor.read_text(encoding='utf-8') != expected.read_text(encoding='utf-8'):
        raise ValueError('Native capture descriptor must match the capture-only source descriptor')
    marker = (native / 'uv_video_stream.variant').read_text(encoding='ascii').splitlines()
    digest = sha256(native / CAPTURE_DLL)
    if marker != [variant, digest]:
        raise ValueError(f'Native capture variant/hash must match the linked {variant} DLL; rebuild the native target')
    return digest


def _preset(presets: str, feature: str) -> tuple[str, str]:
    groups = dict((int(number), (body, options)) for number, body, options in re.findall(
        r'^\[preset\.(\d+)\]\s*\n(.*?)^\[preset\.\1\.options\]\s*\n(.*?)(?=^\[preset\.|\Z)',
        presets, re.MULTILINE | re.DOTALL))
    matches = [(main, options) for main, options in groups.values() if f'custom_features="{feature}"' in main.splitlines()]
    if len(matches) != 1:
        raise ValueError(f'Expected a single {feature} export preset')
    return matches[0]


def _string(config: str, key: str, *, optional: bool = False) -> str:
    values = re.findall(rf'^{re.escape(key)}=(.*)$', config, re.MULTILINE)
    if optional and not values:
        return ''
    if len(values) != 1 or not re.fullmatch(r'"[^"\\\r\n]*"', values[0]):
        raise ValueError(f'Expected one plain quoted {key} field')
    return values[0][1:-1]


def _roots(config: str) -> set[str]:
    if _string(config, 'export_filter') != 'resources':
        raise ValueError('Capture export must use resource selection')
    entries, = re.findall(r'^export_files=PackedStringArray\(([^\r\n]+)\)$', config, re.MULTILINE)
    paths = entries.split(', ')
    if not all(re.fullmatch(r'"res://[^"\r\n]+"', p) for p in paths):
        raise ValueError('Unexpected capture export_files syntax')
    return {resource_path(p[1:-1]) for p in paths}


def capture_inputs(root: Path) -> tuple[str, set[str], dict[str, str]]:
    client = root / 'client'
    presets = (client / 'export_presets.cfg').read_text(encoding='utf-8')
    public, _ = _preset(presets, 'public_release')
    capture, options = _preset(presets, 'uv_video_capture')
    roots = _roots(capture)
    if (roots != _roots(public) | {'shaders/bleed_hud.gdshader', CAPTURE_EXTENSION}
            or _string(capture, 'include_filter') or _string(capture, 'exclude_filter') != 'testdata/*,build/*,builds/*'):
        raise ValueError('Capture resource allowlist drifted from the public roots and required capture files')
    if (_string(options, 'custom_template/release') or _string(options, 'custom_template/debug')
            or _string(options, 'binary_format/architecture') != 'x86_64'
            or re.findall(r'^binary_format/embed_pck=(.*)$', options, re.MULTILINE) != ['false']
            or re.findall(r'^dotnet/embed_build_outputs=(.*)$', options, re.MULTILINE) != ['false']):
        raise ValueError('Capture preset must use the installed Windows x64 template and a separate PCK')
    project_settings = (client / 'project.godot').read_text(encoding='utf-8')
    if not _string(options, 'application/icon') or not _string(project_settings, 'config/icon'):
        raise ValueError('Capture requires explicit provenance-approved application and project icons')
    implicit = {_string(options, k, optional=True) for k in ('application/icon', 'application/console_wrapper_icon')}
    implicit |= {_string(project_settings, k, optional=True) for k in ('config/icon', 'config/windows_native_icon', 'boot_splash/image')}
    implicit.discard('')
    if any(not path.startswith('res://assets/') for path in implicit):
        raise ValueError('Implicit capture images must use provenance-approved assets')
    assets = {p for p in roots if p.startswith('assets/')} | {resource_path(path) for path in implicit}
    known = {entry['path']: entry['sha256'] for entry in json.loads(
        (root / 'release/asset-provenance.json').read_text(encoding='utf-8'))['assets']}
    approved = {p: known.get(f'client/{p}', '') for p in assets}
    # Select only this preset (Godot reads preset numbering from zero).
    return f'[preset.0]\n{capture}[preset.0.options]\n{options}', roots, approved


def _check_input(root: Path, relative: str) -> Path:
    path = root
    for part in relative.split('/'):
        path /= part
        info = path.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError(f'Capture input must not use a link or junction: {path}')
    if not stat.S_ISREG(info.st_mode):
        raise ValueError(f'Capture input is not a regular file: {path}')
    return path


def _tracked(root: Path) -> set[str]:
    result = subprocess.check_output(['git', 'ls-files', '-z', '--', 'client', 'shared', 'presentation',
                                      'global.json', 'NuGet.Config', 'Directory.Build.props', 'Directory.Build.targets',
                                      'Directory.Packages.props'], cwd=root)
    return {resource_path(path.decode('utf-8')) for path in result.split(b'\0') if path}


def _include(path: str, assets: dict[str, str]) -> bool:
    if '/' not in path:
        return True  # bounded top-level managed configuration in _tracked
    scope, relative = path.split('/', 1)
    suffix = Path(relative).suffix
    if scope in ('shared', 'presentation'):
        return suffix in ('.cs', '.csproj', '.uid') and not any(part.startswith('.') for part in relative.split('/'))
    if relative.startswith('assets/'):
        return relative in assets or (relative.endswith('.import') and relative[:-7] in assets)
    return (relative in ('project.godot', 'DynamiteUniverse.csproj', 'DynamiteUniverse.sln')
            or (relative.startswith(('scenes/', 'scripts/', 'shaders/')) and suffix in ('.tscn', '.cs', '.gdshader', '.uid')))


@contextmanager
def isolated_capture(root: Path, native: Path, digest: str) -> Iterator[tuple[Path, set[str], set[str]]]:
    preset, roots, assets = capture_inputs(root)
    with tempfile.TemporaryDirectory(prefix='uv-capture-project-') as temporary:
        isolated = Path(temporary)
        project = isolated / 'client'
        mapped = set()
        for relative in sorted(p for p in _tracked(root) if _include(p, assets)):
            source = _check_input(root, relative)
            target = isolated / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            if relative.startswith('client/'):
                mapped.add(relative.removeprefix('client/'))
        for path, expected in assets.items():
            if not expected or path not in mapped or sha256(project / path) != expected:
                raise ValueError(f'Unverified staged capture asset: client/{path}')
        missing = roots - mapped - {CAPTURE_EXTENSION}
        if missing:
            raise ValueError(f'Capture roots missing from tracked staging: {sorted(missing)}')
        (project / 'export_presets.cfg').write_text(preset, encoding='utf-8')
        descriptor = project / CAPTURE_EXTENSION
        descriptor.parent.mkdir(parents=True)
        shutil.copyfile(root / 'tools/gameplay-capture/native' / descriptor.name, descriptor)
        dll = descriptor.with_name(CAPTURE_DLL)
        shutil.copyfile(native / CAPTURE_DLL, dll)
        if sha256(dll) != digest:
            raise ValueError('Native capture DLL changed while staging')
        registry = project / REGISTRY
        registry.parent.mkdir()
        registry.write_text(f'res://{CAPTURE_EXTENSION}\n', encoding='utf-8')
        # All trusted compiled code is staged; the PCK may only contain resources
        # from this source set, not build caches, presets, solution or import recipes.
        sources = {p for p in mapped if p in assets or (p.startswith(('scenes/', 'scripts/', 'shaders/'))
                   and Path(p).suffix in ('.cs', '.tscn', '.gdshader'))}
        yield project, sources | {CAPTURE_EXTENSION}, roots
