"""Focused packaging checks; never launches Godot or builds a package."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import capture_build as build
import capture_pck as pck

ROOT = Path(__file__).resolve().parents[2]


def pck_fixture(path: Path, members: list[tuple[str, bytes]]) -> None:
    payloads = bytearray()
    entries = bytearray(struct.pack('<I', len(members)))
    for name, payload in members:
        raw = name.encode()
        raw += b'\0' * (-len(raw) % 4)
        entries.extend(struct.pack('<I', len(raw)) + raw)
        entries.extend(pck.ENTRY.pack(len(payloads), len(payload), hashlib.md5(payload, usedforsecurity=False).digest(), 0))
        payloads.extend(payload)
    base = pck.HEADER.size
    path.write_bytes(pck.HEADER.pack(b'GDPC', 4, 4, 7, 1, 2, base, base + len(payloads)) + payloads + entries)


class PackagingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def put(self, name: str, data: bytes) -> Path:
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def test_version_uses_finished_gui_executable_without_project_arguments(self) -> None:
        exe = self.root / 'with spaces' / 'DynaMakerCapture.exe'
        with patch.object(build.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, build.CAPTURE_VERSION+'\n', '')) as run:
            self.assertEqual(build.verify_engine(exe), build.CAPTURE_VERSION)
            self.assertEqual(run.call_args.args[0], [str(exe.resolve()), '--headless', '--version'])
            self.assertEqual(run.call_args.kwargs['cwd'], exe.parent.resolve())
            run.return_value = subprocess.CompletedProcess([], 0, '4.7.1.stable.official.wrong', '')
            with self.assertRaisesRegex(ValueError, 'pinned Godot'):
                build.verify_engine(exe)
            run.return_value = subprocess.CompletedProcess([], 1, build.CAPTURE_VERSION, 'Failed')
            with self.assertRaises(ValueError):
                build.verify_engine(exe)

    def test_cmake_generated_variant_is_bound_to_dll_bytes(self) -> None:
        cmake = shutil.which('cmake')
        if cmake is None:
            self.skipTest('CMake executable is not installed')
        dll = self.put(f'native/{build.CAPTURE_DLL}', b'linked QA payload')
        descriptor = Path(build.CAPTURE_EXTENSION).name
        self.put(f'native/{descriptor}', b'descriptor')
        self.put(f'tools/gameplay-capture/native/{descriptor}', b'descriptor')
        writer = ROOT / 'tools/gameplay-capture/native/write-variant.cmake'
        subprocess.run([cmake, f'-DUV_DLL={dll}', '-DUV_VARIANT=qa', '-P', str(writer)], check=True)
        self.assertEqual(build.verify_native(self.root, dll.parent, 'qa'), build.sha256(dll))
        with self.assertRaises(ValueError):
            build.verify_native(self.root, dll.parent, 'release')
        dll.write_bytes(b'changed after linking')
        with self.assertRaises(ValueError):
            build.verify_native(self.root, dll.parent, 'qa')

    def test_isolation_and_implicit_icon_provenance(self) -> None:
        client = self.root / 'client'
        project = (ROOT / 'client/project.godot').read_bytes()
        presets = (ROOT / 'client/export_presets.cfg').read_bytes()
        self.put('client/project.godot', project)
        config = self.put('client/export_presets.cfg', presets)
        self.put('release/asset-provenance.json', (ROOT / 'release/asset-provenance.json').read_bytes())
        registry = self.put('client/'+pck.REGISTRY, b'normal client registry')
        self.put('client/testdata/never-copy.wav', b'synthetic sentinel')
        self.put('client/.godot/imported/never-copy.ctex', b'old cached bytes')
        self.put('client/assets/never-copy.wav', b'unapproved bytes')
        rewritten, roots, assets = build.capture_inputs(self.root)
        self.assertEqual(rewritten.count('[preset.0]'), 1)
        self.assertNotIn('Android', rewritten)
        self.assertIn('assets/icons/app-icon.png', assets)
        for name in (roots | set(assets)) - {build.CAPTURE_EXTENSION}:
            self.put(f'client/{name}', (ROOT / 'client' / name).read_bytes())
        self.put('shared/Fake.cs', b'// trusted source')
        self.put('tools/gameplay-capture/native/'+Path(build.CAPTURE_EXTENSION).name, b'descriptor')
        dll = self.put(f'native/{build.CAPTURE_DLL}', b'linked')
        tracked = {p.relative_to(self.root).as_posix() for p in self.root.rglob('*') if p.is_file()}
        with patch.object(build, '_tracked', return_value=tracked):
            with self.assertRaisesRegex(RuntimeError, 'simulate export failure'):
                with build.isolated_capture(self.root, dll.parent, build.sha256(dll)) as (stage, _, _):
                    self.assertNotEqual(stage, client)
                    self.assertTrue((stage.parent / 'shared/Fake.cs').is_file())
                    self.assertFalse((stage / 'testdata').exists())
                    self.assertFalse((stage / 'assets/never-copy.wav').exists())
                    self.assertFalse((stage / '.godot/imported').exists())
                    self.assertEqual((stage / pck.REGISTRY).read_text(), f'res://{build.CAPTURE_EXTENSION}\n')
                    raise RuntimeError('simulate export failure')
            self.assertFalse(stage.exists())
            self.assertEqual(registry.read_bytes(), b'normal client registry')
            self.put('client/assets/icons/app-icon.png', b'substituted icon')
            with self.assertRaisesRegex(ValueError, 'Unverified staged capture asset'):
                with build.isolated_capture(self.root, dll.parent, build.sha256(dll)):
                    self.fail('mutated icon passed')
        config.write_bytes(presets.replace(b'application/icon="res://assets/icons/app-icon.png"',
                                            b'application/icon="D:/private/icon.ico"'))
        with self.assertRaisesRegex(ValueError, 'provenance-approved'):
            build.capture_inputs(self.root)

    def test_final_pck_rejects_unknown_members_and_substituted_import(self) -> None:
        extension = build.CAPTURE_EXTENSION
        raw = self.put(extension, b'descriptor')
        imported = '.godot/imported/icon.png-generated.ctex'
        sidecar = f'[remap]\npath="res://{imported}"\n'.encode()
        self.put('assets/icon.png.import', sidecar)
        self.put(imported, b'known converted bytes')
        source = {'assets/icon.png', extension}
        pack = self.root / 'capture.pck'
        members = [(n, b'engine metadata') for n in sorted(pck.PACK_META)] + [
            (pck.REGISTRY, f'res://{extension}\n'.encode()),
            (extension, raw.read_bytes()),
            ('assets/icon.png.import', sidecar),
            (imported, b'known converted bytes'),
        ]
        pck_fixture(pack, members)
        pck.verify_capture_pck(pack, self.root, source, source, extension)
        for extra in ('testdata/private.wav', '.godot/imported/private.ctex', 'unapproved.svg'):
            with self.subTest(extra=extra):
                pck_fixture(pack, members + [(extra, b'extra data')])
                with self.assertRaises(ValueError):
                    pck.verify_capture_pck(pack, self.root, source, source, extension)
        pck_fixture(pack, members[:-1] + [(imported, b'substituted image')])
        with self.assertRaisesRegex(ValueError, 'staged generated resource'):
            pck.verify_capture_pck(pack, self.root, source, source, extension)
        pck_fixture(pack, members + [('Assets/ICON.PNG.IMPORT', b'duplicate')])
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            pck.read_pck(pack)
        pck_fixture(pack, members + [('../escape', b'bad path')])
        with self.assertRaisesRegex(ValueError, 'Unsafe resource path'):
            pck.read_pck(pack)


if __name__ == '__main__':
    unittest.main()
