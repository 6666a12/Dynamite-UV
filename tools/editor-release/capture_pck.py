"""Audit the standalone PCK emitted by the pinned Godot 4.7.1 capture preset."""
from __future__ import annotations

import hashlib
from pathlib import Path
import re
import struct

HEADER = struct.Struct('<4s5I2Q')
ENTRY = struct.Struct('<QQ16sI')
PACK_META = {'.godot/global_script_class_cache.cfg', '.godot/uid_cache.bin', 'project.binary'}
REGISTRY = '.godot/extension_list.cfg'


def resource_path(value: str) -> str:
    relative = value.removeprefix('res://')
    if ('\\' in relative or ':' in relative or any(ord(c) < 32 for c in relative) or
            any(not part or part in ('.', '..') for part in relative.split('/'))):
        raise ValueError(f'Unsafe resource path: {value!r}')
    return relative


def read_pck(pack: Path) -> dict[str, memoryview]:
    data = memoryview(pack.read_bytes())
    if len(data) < HEADER.size:
        raise ValueError('Truncated capture PCK header')
    magic, format_version, major, minor, patch, flags, base, directory = HEADER.unpack_from(data)
    # PACK_REL_FILEBASE only: no encryption, sparse bundle, patches or embedded PCK.
    if (magic, format_version, major, minor, patch, flags) != (b'GDPC', 4, 4, 7, 1, 2):
        raise ValueError('Unexpected capture PCK format, version or flags')
    if not HEADER.size <= base <= directory <= len(data) - 4:
        raise ValueError('Capture PCK has invalid directory or data offsets')
    count, = struct.unpack_from('<I', data, directory)
    pos = directory + 4
    if count > (len(data) - pos) // (4 + 4 + ENTRY.size):
        raise ValueError('Capture PCK has an invalid entry count')
    files: dict[str, memoryview] = {}
    folded = set()
    for _ in range(count):
        if pos + 4 > len(data):
            raise ValueError('Truncated capture PCK entry')
        name_size, = struct.unpack_from('<I', data, pos)
        pos += 4
        if not 0 < name_size <= 4096 or name_size % 4 or pos + name_size + ENTRY.size > len(data):
            raise ValueError('Capture PCK has a malformed entry name')
        path = resource_path(bytes(data[pos:pos + name_size]).rstrip(b'\0').decode('utf-8'))
        pos += name_size
        offset, size, digest, entry_flags = ENTRY.unpack_from(data, pos)
        pos += ENTRY.size
        if path.casefold() in folded or entry_flags or base + offset + size > directory:
            raise ValueError(f'Duplicate or unsupported capture PCK entry: {path}')
        payload = data[base + offset:base + offset + size]
        if hashlib.md5(payload, usedforsecurity=False).digest() != digest:
            raise ValueError(f'Capture PCK payload checksum mismatch: {path}')
        files[path] = payload
        folded.add(path.casefold())
    if pos != len(data):
        raise ValueError('Unexpected trailing data in capture PCK directory')
    return files


def _remap_targets(payload: bytes) -> set[str]:
    section = re.search(r'^\[remap\][^\S\n]*\n(.*?)(?=^\[|\Z)', payload.rstrip(b'\0').decode('utf-8'), re.MULTILINE | re.DOTALL)
    if section is None:
        return set()
    return {resource_path(path) for path in re.findall(r'^path(?:\.[\w.]+)?="(res://[^"\r\n]+)"\r?$', section[1], re.MULTILINE)}


def verify_capture_pck(pack: Path, project: Path, sources: set[str], roots: set[str], extension: str) -> None:
    files = read_pck(pack)
    if set(files) & PACK_META != PACK_META or REGISTRY not in files:
        raise ValueError('Capture PCK is missing Godot metadata')
    if bytes(files[REGISTRY]).split() != [f'res://{extension}'.encode('utf-8')]:
        raise ValueError('Capture PCK has an unexpected extension registry')
    for root in roots:
        if not any(root + suffix in files for suffix in ('', '.import', '.remap')):
            raise ValueError(f'Capture PCK is missing a declared export root: {root}')

    generated = set()
    for name, payload in files.items():
        if name in sources:
            # Godot's Mono exporter replaces .cs sources with one newline; the
            # actual assembly is not embedded in this PCK.
            expected = b'\n' if name.endswith('.cs') else (project / name).read_bytes()
            if payload != expected:
                raise ValueError(f'Capture PCK bytes do not match staged source or Mono placeholder: {name}')
        elif name.endswith('.import') and name[:-7] in sources:
            targets = _remap_targets(bytes(payload))
            sidecar = project / name
            if (not sidecar.is_file() or not targets or not targets <= _remap_targets(sidecar.read_bytes()) or
                    any(not path.startswith('.godot/imported/') for path in targets)):
                raise ValueError(f'Capture PCK has an unexpected import target: {name}')
            generated.update(targets)
        elif name.endswith('.tscn.remap') and name[:-6] in sources:
            source = name[:-6]
            digest = hashlib.md5(f'res://{source}'.encode(), usedforsecurity=False).hexdigest()
            suffix = re.escape(Path(source).with_suffix('.scn').name)
            targets = _remap_targets(bytes(payload))
            if len(targets) != 1 or not all(re.fullmatch(rf'\.godot/exported/\d+/export-{digest}-{suffix}', p) for p in targets):
                raise ValueError(f'Capture PCK has an unexpected scene remap: {name}')
            generated.update(targets)
        elif name not in PACK_META | {REGISTRY} and not name.startswith(('.godot/imported/', '.godot/exported/')):
            raise ValueError(f'Unexpected capture PCK member: {name}')

    actual_generated = {p for p in files if p.startswith(('.godot/imported/', '.godot/exported/'))}
    if actual_generated != generated:
        raise ValueError(f'Capture PCK generated resource closure mismatch: {sorted(actual_generated ^ generated)}')
    for path in sorted(generated):
        staged = project / path
        if not staged.is_file() or bytes(files[path]) != staged.read_bytes():
            raise ValueError(f'Capture PCK does not match staged generated resource: {path}')
