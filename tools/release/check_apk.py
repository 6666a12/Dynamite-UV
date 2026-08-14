#!/usr/bin/env python3
"""Fail-closed release gate for DUX-Community Android APKs.

The checker deliberately uses only Python's standard library. Android manifest
identity is delegated to the Android SDK's ``apkanalyzer``; if the tool or any
critical identity value cannot be obtained, the gate fails closed.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from dataclasses import dataclass, field
from pathlib import Path, PurePosixPath
from typing import Any, Iterable

POLICY_SCHEMA_VERSION = 1
DEFAULT_POLICY = Path(__file__).resolve().parents[2] / "release" / "apk-policy.json"
IMPORT_TEXT_LIMIT = 8 * 1024 * 1024
QUOTED_VALUE_RE = re.compile(r'"((?:\\.|[^"\\])*)"')
SOURCE_FILE_RE = re.compile(r"(?m)^\s*source_file\s*=\s*(\"(?:\\.|[^\"\\])*\")")
DEST_FILES_RE = re.compile(r"(?ms)^\s*dest_files\s*=\s*\[(.*?)\]")
DRIVE_PATH_RE = re.compile(r"^[A-Za-z]:")


class GateConfigurationError(Exception):
    """Raised for an unusable policy or command-line configuration."""


@dataclass
class ImportMetadata:
    entry_name: str
    source_files: list[str] = field(default_factory=list)
    dest_files: list[str] = field(default_factory=list)


@dataclass
class GateResult:
    sha256: str | None = None
    size: int | None = None
    entry_count: int = 0
    identity: dict[str, str] = field(default_factory=dict)
    errors: list[str] = field(default_factory=list)

    @property
    def passed(self) -> bool:
        return not self.errors


def _require_mapping(value: Any, field_name: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise GateConfigurationError(f"policy field {field_name!r} must be an object")
    return value


def _require_string(value: Any, field_name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise GateConfigurationError(f"policy field {field_name!r} must be a non-empty string")
    return value


def _require_string_list(value: Any, field_name: str) -> list[str]:
    if not isinstance(value, list) or any(not isinstance(item, str) or not item for item in value):
        raise GateConfigurationError(f"policy field {field_name!r} must be a string array")
    return value


def load_policy(path: Path) -> dict[str, Any]:
    try:
        with path.open("r", encoding="utf-8") as stream:
            policy = json.load(stream)
    except (OSError, json.JSONDecodeError) as exc:
        raise GateConfigurationError(f"cannot load policy {path}: {exc}") from exc

    root = _require_mapping(policy, "<root>")
    if root.get("schema_version") != POLICY_SCHEMA_VERSION:
        raise GateConfigurationError(
            f"unsupported policy schema_version {root.get('schema_version')!r}; "
            f"expected {POLICY_SCHEMA_VERSION}"
        )
    _require_string(root.get("policy_id"), "policy_id")
    _require_string(root.get("policy_revision"), "policy_revision")

    identity = _require_mapping(root.get("identity"), "identity")
    label_resource = _require_mapping(identity.get("label_resource"), "identity.label_resource")
    for key in ("config", "type", "name"):
        _require_string(label_resource.get(key), f"identity.label_resource.{key}")

    modes = _require_mapping(root.get("modes"), "modes")
    for mode in ("public", "internal"):
        mode_policy = _require_mapping(modes.get(mode), f"modes.{mode}")
        for key in (
            "application_id", "application_label", "version_name",
            "version_code", "debuggable", "required_custom_feature", "filename",
        ):
            _require_string(mode_policy.get(key), f"modes.{mode}.{key}")
        requirement = mode_policy.get("testdata_requirement")
        expected = "forbid" if mode == "public" else "require"
        if requirement != expected:
            raise GateConfigurationError(
                f"modes.{mode}.testdata_requirement must be {expected!r}"
            )
        required_suffixes = mode_policy.get("required_entry_suffixes", [])
        _require_string_list(required_suffixes, f"modes.{mode}.required_entry_suffixes")
        allowed_payloads = mode_policy.get("allowed_imported_payloads", {})
        _require_mapping(allowed_payloads, f"modes.{mode}.allowed_imported_payloads")
        for payload, digest in allowed_payloads.items():
            _require_string(payload, f"modes.{mode}.allowed_imported_payloads key")
            digest = _require_string(
                digest, f"modes.{mode}.allowed_imported_payloads.{payload}"
            )
            if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
                raise GateConfigurationError(
                    f"modes.{mode}.allowed_imported_payloads.{payload} must be SHA-256 hex"
                )
        required_hashes = mode_policy.get("required_entry_sha256", {})
        _require_mapping(required_hashes, f"modes.{mode}.required_entry_sha256")
        for suffix, digest in required_hashes.items():
            _require_string(suffix, f"modes.{mode}.required_entry_sha256 key")
            digest = _require_string(digest, f"modes.{mode}.required_entry_sha256.{suffix}")
            if not re.fullmatch(r"[0-9a-fA-F]{64}", digest):
                raise GateConfigurationError(
                    f"modes.{mode}.required_entry_sha256.{suffix} must be SHA-256 hex"
                )
        required_nonempty = mode_policy.get("required_nonempty_entry_suffixes", [])
        _require_string_list(required_nonempty, f"modes.{mode}.required_nonempty_entry_suffixes")
        forbidden_suffixes = mode_policy.get("forbidden_entry_suffixes", [])
        _require_string_list(forbidden_suffixes, f"modes.{mode}.forbidden_entry_suffixes")
        allowed_abis = mode_policy.get("allowed_abis", [])
        _require_string_list(allowed_abis, f"modes.{mode}.allowed_abis")
        signing = mode_policy.get("signing", {})
        if signing:
            signing = _require_mapping(signing, f"modes.{mode}.signing")
            if not isinstance(signing.get("require_valid"), bool):
                raise GateConfigurationError(
                    f"modes.{mode}.signing.require_valid must be a boolean"
                )
            _require_string_list(
                signing.get("required_schemes", []),
                f"modes.{mode}.signing.required_schemes",
            )
            certificate_env = signing.get("certificate_sha256_env")
            if certificate_env is not None:
                _require_string(
                    certificate_env, f"modes.{mode}.signing.certificate_sha256_env"
                )
    _require_string(modes["internal"].get("warning"), "modes.internal.warning")

    rules = _require_mapping(root.get("content_rules"), "content_rules")
    testdata = _require_mapping(rules.get("testdata"), "content_rules.testdata")
    _require_string(testdata.get("path_component"), "content_rules.testdata.path_component")
    _require_string_list(
        testdata.get("quarantined_imported_payloads"),
        "content_rules.testdata.quarantined_imported_payloads",
    )
    legacy = _require_mapping(rules.get("legacy_ai_cover"), "content_rules.legacy_ai_cover")
    _require_string_list(
        legacy.get("resource_path_prefixes"),
        "content_rules.legacy_ai_cover.resource_path_prefixes",
    )
    _require_string_list(
        legacy.get("quarantined_imported_payloads"),
        "content_rules.legacy_ai_cover.quarantined_imported_payloads",
    )
    return root


def sha256_file(path: Path) -> tuple[str, int]:
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
            size += len(chunk)
    return digest.hexdigest(), size


def normalize_zip_name(name: str) -> str:
    """Return a slash-normalized ZIP name, rejecting traversal-like names."""
    if not name or "\x00" in name:
        raise ValueError("empty name or embedded NUL")
    normalized = name.replace("\\", "/")
    if normalized.startswith("/") or DRIVE_PATH_RE.match(normalized):
        raise ValueError("absolute path")
    components = normalized.split("/")
    if any(component in ("", ".", "..") for component in components[:-1]):
        raise ValueError("empty, dot, or parent path component")
    if components[-1] in (".", ".."):
        raise ValueError("dot or parent path component")
    return "/".join(components)


def project_resource_path(path: str) -> str:
    """Map an APK entry or Godot res:// value to a project-relative path."""
    normalized = path.replace("\\", "/").strip()
    is_godot_path = normalized.lower().startswith("res://")
    if is_godot_path:
        normalized = normalized[6:]
    normalized = normalized.lstrip("/")
    # Godot's project files live under the APK's top-level Android assets/ dir.
    # A res://assets/... source already is project-relative and must retain its
    # first component; an APK assets/assets/... entry must lose only the outer.
    if not is_godot_path and normalized.casefold().startswith("assets/"):
        normalized = normalized[7:]
    return normalized


def path_has_component(path: str, component: str) -> bool:
    wanted = component.casefold()
    normalized = path.replace("\\", "/")
    if normalized.lower().startswith("res://"):
        normalized = normalized[6:]
    return any(part.casefold() == wanted for part in normalized.split("/") if part)


def _unquote(token: str) -> str:
    match = QUOTED_VALUE_RE.fullmatch(token.strip())
    if not match:
        raise ValueError(f"invalid quoted value {token!r}")
    raw = match.group(1)
    # Godot paths only need the common string escapes here. Avoid interpreting
    # arbitrary Python escapes or accepting malformed trailing backslashes.
    return re.sub(r'\\([\\"])', r"\1", raw)


def parse_import_metadata(entry_name: str, data: bytes) -> ImportMetadata:
    if len(data) > IMPORT_TEXT_LIMIT:
        raise ValueError(f"metadata exceeds {IMPORT_TEXT_LIMIT} bytes")
    try:
        text = data.rstrip(b"\x00").decode("utf-8-sig", errors="strict")
    except UnicodeDecodeError as exc:
        raise ValueError(f"metadata is not valid UTF-8: {exc}") from exc

    metadata = ImportMetadata(entry_name=entry_name)
    for match in SOURCE_FILE_RE.finditer(text):
        metadata.source_files.append(_unquote(match.group(1)))
    for assignment in DEST_FILES_RE.finditer(text):
        for quoted in QUOTED_VALUE_RE.finditer(assignment.group(1)):
            metadata.dest_files.append(_unquote(quoted.group(0)))
    return metadata


def _matches_resource_prefix(path: str, prefixes: Iterable[str]) -> bool:
    resource = project_resource_path(path).casefold()
    return any(resource.startswith(prefix.replace("\\", "/").lstrip("/").casefold()) for prefix in prefixes)


def _is_imported_payload(entry_name: str) -> bool:
    resource = project_resource_path(entry_name)
    return resource.casefold().startswith(".godot/imported/")


def _is_quarantined_payload(entry_name: str, payload_names: Iterable[str]) -> bool:
    resource = project_resource_path(entry_name)
    if not resource.casefold().startswith(".godot/imported/"):
        return False
    payload_name = PurePosixPath(resource).name.casefold()
    return any(payload_name == expected.casefold() for expected in payload_names)


def _find_entry_by_suffix(
    normalized_entries: dict[str, zipfile.ZipInfo], suffix: str
) -> zipfile.ZipInfo | None:
    folded_suffix = suffix.replace("\\", "/").lstrip("/").casefold()
    matches = [
        info for info in normalized_entries.values()
        if not info.is_dir()
        and info.filename.replace("\\", "/").casefold().endswith(folded_suffix)
    ]
    return matches[0] if len(matches) == 1 else None


def inspect_apk(apk_path: Path, mode: str, policy: dict[str, Any], result: GateResult) -> None:
    mode_policy = policy["modes"][mode]
    expected_filename = mode_policy["filename"]
    if apk_path.name.casefold() != expected_filename.casefold():
        result.errors.append(
            f"filename mismatch for {mode}: expected {expected_filename!r}, got {apk_path.name!r}"
        )

    try:
        result.sha256, result.size = sha256_file(apk_path)
    except OSError as exc:
        result.errors.append(f"cannot read APK {apk_path}: {exc}")
        return

    testdata_rule = policy["content_rules"]["testdata"]
    testdata_component = testdata_rule["path_component"]
    legacy_rule = policy["content_rules"]["legacy_ai_cover"]
    normalized_entries: dict[str, zipfile.ZipInfo] = {}
    invalid_entries: set[int] = set()
    imports: list[ImportMetadata] = []
    entry_sha256: dict[str, str] = {}
    testdata_files: list[str] = []

    try:
        with zipfile.ZipFile(apk_path, "r") as archive:
            infos = archive.infolist()
            result.entry_count = len(infos)
            if not infos:
                result.errors.append("APK ZIP is empty")

            for info in infos:
                try:
                    normalized = normalize_zip_name(info.filename)
                except ValueError as exc:
                    result.errors.append(f"unsafe ZIP entry {info.filename!r}: {exc}")
                    invalid_entries.add(id(info))
                    continue
                folded = normalized.casefold()
                if folded in normalized_entries:
                    result.errors.append(
                        f"duplicate ZIP entry after case/path normalization: {normalized!r}"
                    )
                    continue
                normalized_entries[folded] = info
                if info.flag_bits & 0x1:
                    result.errors.append(f"encrypted ZIP entry cannot be verified: {normalized!r}")
                if not info.is_dir() and path_has_component(normalized, testdata_component):
                    testdata_files.append(normalized)

            try:
                corrupt = archive.testzip()
                if corrupt is not None:
                    result.errors.append(f"ZIP CRC/integrity failure at entry {corrupt!r}")
            except (OSError, RuntimeError, zipfile.BadZipFile) as exc:
                result.errors.append(f"ZIP integrity verification failed: {exc}")

            has_manifest = any(
                info.filename.replace("\\", "/").casefold() == "androidmanifest.xml"
                and not info.is_dir()
                for info in normalized_entries.values()
            )
            if not has_manifest:
                result.errors.append("APK ZIP is missing root AndroidManifest.xml")

            for info in infos:
                if id(info) in invalid_entries or info.is_dir():
                    continue
                raw_name = info.filename.replace("\\", "/")
                if not raw_name.casefold().endswith(".import"):
                    continue
                try:
                    normalized = normalize_zip_name(info.filename)
                    data = archive.read(info)
                    entry_sha256[normalized.casefold()] = hashlib.sha256(data).hexdigest()
                except (OSError, RuntimeError, zipfile.BadZipFile, ValueError) as exc:
                    result.errors.append(f"cannot read APK entry {info.filename!r}: {exc}")

            for info in infos:
                try:
                    normalized = normalize_zip_name(info.filename)
                except ValueError:
                    # The unsafe entry was already reported during the first pass.
                    continue
                try:
                    data = archive.read(info)
                    imports.append(parse_import_metadata(normalized, data))
                    entry_sha256[normalized.casefold()] = hashlib.sha256(data).hexdigest()
                except (OSError, RuntimeError, zipfile.BadZipFile, ValueError) as exc:
                    result.errors.append(f"cannot parse import metadata {normalized!r}: {exc}")
    except (OSError, zipfile.BadZipFile) as exc:
        result.errors.append(f"not a valid APK ZIP: {exc}")
        return

    if mode_policy["testdata_requirement"] == "forbid" and testdata_files:
        for entry in testdata_files:
            result.errors.append(f"public APK contains testdata path: {entry!r}")
    elif mode_policy["testdata_requirement"] == "require" and not testdata_files:
        result.errors.append("internal APK must contain at least one file under a testdata path")

    required_suffixes = mode_policy.get("required_entry_suffixes", [])
    for suffix in required_suffixes:
        if _find_entry_by_suffix(normalized_entries, suffix) is None:
            result.errors.append(
                f"{mode} APK is missing required entry suffix: {suffix!r}"
            )

    for suffix in mode_policy.get("required_nonempty_entry_suffixes", []):
        entry = _find_entry_by_suffix(normalized_entries, suffix)
        if entry is None:
            result.errors.append(
                f"{mode} APK is missing required non-empty entry suffix: {suffix!r}"
            )
        elif entry.file_size == 0:
            result.errors.append(
                f"{mode} APK required entry is empty: {entry.filename!r}"
            )

    for suffix, expected_hash in mode_policy.get("required_entry_sha256", {}).items():
        entry = _find_entry_by_suffix(normalized_entries, suffix)
        if entry is None:
            result.errors.append(
                f"{mode} APK is missing hashed required entry suffix: {suffix!r}"
            )
            continue
        actual_hash = entry_sha256.get(entry.filename.replace("\\", "/").casefold())
        if actual_hash != expected_hash.casefold():
            result.errors.append(
                f"{mode} APK required entry hash mismatch: {entry.filename!r}"
            )

    for suffix in mode_policy.get("forbidden_entry_suffixes", []):
        normalized_suffix = suffix.replace("\\", "/").lstrip("/").casefold()
        for info in normalized_entries.values():
            if (not info.is_dir() and
                    info.filename.replace("\\", "/").casefold().endswith(normalized_suffix)):
                result.errors.append(f"{mode} APK contains forbidden entry: {info.filename!r}")

    actual_abis = {
        PurePosixPath(info.filename.replace("\\", "/")).parts[1]
        for info in normalized_entries.values()
        if not info.is_dir()
        and len(PurePosixPath(info.filename.replace("\\", "/")).parts) >= 3
        and PurePosixPath(info.filename.replace("\\", "/")).parts[0].casefold() == "lib"
    }
    allowed_abis = set(mode_policy.get("allowed_abis", []))
    if allowed_abis:
        if not actual_abis:
            result.errors.append(f"{mode} APK contains no native ABI libraries")
        for abi in sorted(actual_abis - allowed_abis):
            result.errors.append(f"{mode} APK contains unapproved ABI directory: {abi!r}")
        for abi in sorted(allowed_abis - actual_abis):
            result.errors.append(f"{mode} APK is missing required ABI directory: {abi!r}")

    project_entry = _find_entry_by_suffix(normalized_entries, "project.binary")
    if project_entry is not None:
        project_bytes = b""
        try:
            with zipfile.ZipFile(apk_path, "r") as archive:
                project_bytes = archive.read(project_entry)
        except (OSError, RuntimeError, zipfile.BadZipFile) as exc:
            result.errors.append(f"cannot read project.binary for feature check: {exc}")
        required_feature = mode_policy.get("required_custom_feature", "").encode("utf-8")
        if required_feature and required_feature not in project_bytes:
            result.errors.append(
                f"{mode} APK project.binary is missing custom feature "
                f"{mode_policy['required_custom_feature']!r}"
            )

    sparse_pck = _find_entry_by_suffix(normalized_entries, "assets.sparsepck")
    if sparse_pck is not None:
        try:
            with zipfile.ZipFile(apk_path, "r") as archive:
                magic = archive.read(sparse_pck)[:4]
            if magic != b"GDPC":
                result.errors.append("APK assets.sparsepck has invalid Godot pack magic")
        except (OSError, RuntimeError, zipfile.BadZipFile) as exc:
            result.errors.append(f"cannot verify assets.sparsepck: {exc}")

    deps_entry = _find_entry_by_suffix(
        normalized_entries, ".godot/mono/publish/arm64/DuxCommunity.deps.json"
    )
    if deps_entry is not None:
        try:
            with zipfile.ZipFile(apk_path, "r") as archive:
                deps = json.loads(archive.read(deps_entry).decode("utf-8"))
            runtime_name = deps.get("runtimeTarget", {}).get("name", "")
            if not runtime_name.endswith("/android-arm64"):
                result.errors.append(
                    f"DuxCommunity.deps.json has unexpected runtime target: {runtime_name!r}"
                )
            target = deps.get("targets", {}).get(runtime_name, {})
            project = next(
                (value for key, value in target.items() if key.startswith("DuxCommunity/")),
                None,
            )
            dependencies = project.get("dependencies", {}) if isinstance(project, dict) else {}
            if "DuxShared" not in dependencies or "GodotSharp" not in dependencies:
                result.errors.append(
                    "DuxCommunity.deps.json is missing DuxShared/GodotSharp dependencies"
                )
        except (OSError, RuntimeError, zipfile.BadZipFile, UnicodeDecodeError,
                json.JSONDecodeError) as exc:
            result.errors.append(f"cannot verify DuxCommunity.deps.json: {exc}")

    entry_names = list(normalized_entries)
    allowed_imported_payloads = {
        item.casefold(): digest.casefold()
        for item, digest in mode_policy.get("allowed_imported_payloads", {}).items()
    }
    for folded in entry_names:
        info = normalized_entries[folded]
        if info.is_dir():
            continue
        name = info.filename.replace("\\", "/")
        if mode == "public" and _is_imported_payload(name):
            payload_name = PurePosixPath(project_resource_path(name)).name.casefold()
            if payload_name not in allowed_imported_payloads:
                result.errors.append(
                    f"public APK contains unapproved imported payload: {name!r}"
                )
            else:
                actual_hash = entry_sha256.get(name.casefold())
                if actual_hash != allowed_imported_payloads[payload_name]:
                    result.errors.append(
                        f"public APK approved imported payload hash mismatch: {name!r}"
                    )
        if _matches_resource_prefix(name, legacy_rule["resource_path_prefixes"]):
            result.errors.append(f"APK contains forbidden legacy AI cover path: {name!r}")
        if _is_quarantined_payload(
            name, legacy_rule["quarantined_imported_payloads"]
        ):
            result.errors.append(f"APK contains forbidden legacy AI cover imported payload: {name!r}")
        if mode == "public" and _is_quarantined_payload(
            name, testdata_rule["quarantined_imported_payloads"]
        ):
            result.errors.append(f"public APK contains quarantined testdata imported payload: {name!r}")

    for metadata in imports:
        forbidden_dests: set[str] = set()
        for source in metadata.source_files:
            if mode == "public" and path_has_component(source, testdata_component):
                result.errors.append(
                    f"public APK import {metadata.entry_name!r} maps testdata source {source!r}"
                )
                forbidden_dests.update(dest.casefold() for dest in metadata.dest_files)
            if _matches_resource_prefix(source, legacy_rule["resource_path_prefixes"]):
                result.errors.append(
                    f"APK import {metadata.entry_name!r} maps forbidden legacy AI cover source {source!r}"
                )
                forbidden_dests.update(dest.casefold() for dest in metadata.dest_files)
        if forbidden_dests:
            for dest in metadata.dest_files:
                resource_dest = project_resource_path(dest).casefold()
                candidates = {resource_dest, f"assets/{resource_dest}"}
                if any(candidate in normalized_entries for candidate in candidates):
                    result.errors.append(
                        f"forbidden import payload from {metadata.entry_name!r} is present: {dest!r}"
                    )


def _version_key(path: Path) -> tuple[tuple[int, Any], ...]:
    parts: list[tuple[int, Any]] = []
    for part in re.split(r"([0-9]+)", path.as_posix().casefold()):
        parts.append((0, int(part)) if part.isdigit() else (1, part))
    return tuple(parts)


def _tool_names() -> tuple[str, ...]:
    return ("apkanalyzer.bat", "apkanalyzer.exe", "apkanalyzer") if os.name == "nt" else (
        "apkanalyzer",
        "apkanalyzer.bat",
    )


def _sdk_roots() -> list[Path]:
    roots: list[Path] = []
    for variable in ("ANDROID_SDK_ROOT", "ANDROID_HOME"):
        if os.environ.get(variable):
            roots.append(Path(os.environ[variable]).expanduser())
    home = Path.home()
    if os.name == "nt":
        local_app_data = os.environ.get("LOCALAPPDATA")
        if local_app_data:
            roots.append(Path(local_app_data) / "Android" / "Sdk")
        roots.append(home / "AppData" / "Local" / "Android" / "Sdk")
    elif sys.platform == "darwin":
        roots.append(home / "Library" / "Android" / "sdk")
    else:
        roots.extend((home / "Android" / "Sdk", home / "Android" / "sdk"))

    unique: list[Path] = []
    seen: set[str] = set()
    for root in roots:
        key = str(root.resolve(strict=False)).casefold()
        if key not in seen:
            seen.add(key)
            unique.append(root)
    return unique


def find_apkanalyzer(explicit: str | None) -> Path:
    direct_candidates: list[Path] = []
    if explicit:
        direct_candidates.append(Path(explicit).expanduser())
    elif os.environ.get("APKANALYZER"):
        direct_candidates.append(Path(os.environ["APKANALYZER"]).expanduser())

    for candidate in direct_candidates:
        if candidate.is_file():
            return candidate.resolve()
        resolved = shutil.which(str(candidate))
        if resolved:
            return Path(resolved).resolve()
        raise GateConfigurationError(f"apkanalyzer not found: {candidate}")

    for name in _tool_names():
        resolved = shutil.which(name)
        if resolved:
            return Path(resolved).resolve()

    sdk_candidates: list[Path] = []
    for root in _sdk_roots():
        for name in _tool_names():
            sdk_candidates.append(root / "cmdline-tools" / "latest" / "bin" / name)
            sdk_candidates.append(root / "tools" / "bin" / name)
        cmdline_tools = root / "cmdline-tools"
        if cmdline_tools.is_dir():
            for bin_dir in cmdline_tools.glob("*/bin"):
                for name in _tool_names():
                    sdk_candidates.append(bin_dir / name)

    existing = [path for path in sdk_candidates if path.is_file()]
    if existing:
        existing.sort(key=_version_key, reverse=True)
        return existing[0].resolve()
    raise GateConfigurationError(
        "apkanalyzer not found; pass --apkanalyzer, set APKANALYZER/ANDROID_SDK_ROOT/"
        "ANDROID_HOME, or install Android SDK command-line tools"
    )


def find_android_tool(tool_name: str) -> Path:
    names = (
        f"{tool_name}.bat", f"{tool_name}.exe", tool_name,
    ) if os.name == "nt" else (tool_name, f"{tool_name}.bat")
    env_override = os.environ.get(f"DUX_{tool_name.upper()}_PATH")
    if env_override:
        candidate = Path(env_override).expanduser()
        if candidate.is_file():
            return candidate.resolve()
        raise GateConfigurationError(
            f"Android SDK tool override does not exist: {candidate}"
        )
    for name in names:
        resolved = shutil.which(name)
        if resolved:
            return Path(resolved).resolve()
    candidates: list[Path] = []
    for root in _sdk_roots():
        build_tools = root / "build-tools"
        if not build_tools.is_dir():
            continue
        for version_dir in build_tools.iterdir():
            if not version_dir.is_dir():
                continue
            for name in names:
                candidates.append(version_dir / name)
    existing = [path for path in candidates if path.is_file()]
    if existing:
        existing.sort(key=_version_key, reverse=True)
        return existing[0].resolve()
    raise GateConfigurationError(f"Android SDK tool not found: {tool_name}")


def inspect_signature(
    apk_path: Path, mode: str, policy: dict[str, Any], result: GateResult
) -> None:
    signing = policy["modes"][mode].get("signing", {})
    if not signing or not signing.get("require_valid", False):
        return
    try:
        tool = find_android_tool("apksigner")
        completed = subprocess.run(
            [str(tool), "verify", "--verbose", "--print-certs", str(apk_path)],
            check=False, capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=60,
        )
    except (GateConfigurationError, OSError, subprocess.TimeoutExpired) as exc:
        result.errors.append(f"critical APK signature verification failed: {exc}")
        return
    output = completed.stdout + "\n" + completed.stderr
    if completed.returncode != 0 or not re.search(r"(?m)^Verifies\s*$", output):
        result.errors.append("APK signature verification failed")
        return
    for scheme in signing.get("required_schemes", []):
        pattern = rf"(?mi)^Verified using {re.escape(scheme)}(?:\.\d+)? scheme .*:\s*true\s*$"
        if not re.search(pattern, output):
            result.errors.append(f"APK signature is missing required {scheme} scheme")
    digests = re.findall(
        r"(?mi)certificate SHA-256 digest:\s*([0-9a-f]{64})\s*$", output
    )
    if len({digest.casefold() for digest in digests}) != 1:
        result.errors.append("APK must have exactly one verifiable signer certificate")
        return
    actual_digest = digests[0].casefold()
    result.identity["signing_cert_sha256"] = actual_digest
    env_name = signing.get("certificate_sha256_env")
    if env_name:
        expected_digest = os.environ.get(env_name, "").strip().replace(":", "").casefold()
        if not re.fullmatch(r"[0-9a-f]{64}", expected_digest):
            result.errors.append(
                f"critical signer pin is missing or invalid in environment variable {env_name}"
            )
        elif actual_digest != expected_digest:
            result.errors.append(
                f"APK signer certificate mismatch for environment pin {env_name}"
            )


def _run_apkanalyzer(tool: Path, arguments: list[str], apk_path: Path) -> str:
    command = [str(tool), *arguments, str(apk_path)]
    try:
        completed = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=60,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise GateConfigurationError(
            f"cannot run apkanalyzer command {' '.join(arguments)}: {exc}"
        ) from exc
    value = completed.stdout.strip()
    if completed.returncode != 0:
        details = completed.stderr.strip() or value or f"exit code {completed.returncode}"
        raise GateConfigurationError(
            f"apkanalyzer {' '.join(arguments)} failed: {details}"
        )
    if not value or "\n" in value or "\r" in value:
        raise GateConfigurationError(
            f"apkanalyzer {' '.join(arguments)} returned no single verifiable value"
        )
    return value


def inspect_identity(
    apk_path: Path,
    mode: str,
    policy: dict[str, Any],
    apkanalyzer: Path,
    result: GateResult,
) -> None:
    label = policy["identity"]["label_resource"]
    commands = {
        "application_id": ["manifest", "application-id"],
        "application_label": [
            "resources",
            "value",
            "--config",
            label["config"],
            "--type",
            label["type"],
            "--name",
            label["name"],
        ],
        "version_name": ["manifest", "version-name"],
        "version_code": ["manifest", "version-code"],
        "debuggable": ["manifest", "debuggable"],
    }
    expected = policy["modes"][mode]
    for field_name, command in commands.items():
        try:
            actual = _run_apkanalyzer(apkanalyzer, command, apk_path)
        except GateConfigurationError as exc:
            result.errors.append(f"critical identity verification failed: {exc}")
            continue
        result.identity[field_name] = actual
        if actual != expected[field_name]:
            result.errors.append(
                f"identity mismatch for {field_name}: expected {expected[field_name]!r}, got {actual!r}"
            )


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Validate a DUX-Community APK against the final public/internal release policy."
    )
    parser.add_argument("--mode", required=True, choices=("public", "internal"))
    parser.add_argument("--apk", required=True, type=Path, help="APK file to validate")
    parser.add_argument(
        "--policy",
        type=Path,
        default=DEFAULT_POLICY,
        help=f"policy JSON (default: {DEFAULT_POLICY})",
    )
    parser.add_argument(
        "--apkanalyzer",
        default=None,
        help="Android SDK apkanalyzer path (otherwise environment/PATH/SDK discovery is used)",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_argument_parser().parse_args(argv)
    apk_path = args.apk.expanduser().resolve(strict=False)
    policy_path = args.policy.expanduser().resolve(strict=False)

    try:
        policy = load_policy(policy_path)
    except GateConfigurationError as exc:
        print(f"APK gate FAILED: {exc}", file=sys.stderr)
        return 1

    if args.mode == "internal":
        warning = policy["modes"]["internal"]["warning"]
        print(f"*** INTERNAL APK: {warning} ***", file=sys.stderr)

    result = GateResult()
    inspect_apk(apk_path, args.mode, policy, result)

    try:
        apkanalyzer = find_apkanalyzer(args.apkanalyzer)
    except GateConfigurationError as exc:
        result.errors.append(f"critical identity verification failed: {exc}")
        apkanalyzer = None
    if apkanalyzer is not None and result.sha256 is not None:
        inspect_identity(apk_path, args.mode, policy, apkanalyzer, result)
    if result.sha256 is not None:
        inspect_signature(apk_path, args.mode, policy, result)

    if result.passed:
        print("APK gate PASS")
        print(f"mode: {args.mode}")
        print(f"apk: {apk_path}")
        print(f"sha256: {result.sha256}")
        print(f"size: {result.size} bytes")
        print(f"zip_entries: {result.entry_count}")
        print(f"application_id: {result.identity['application_id']}")
        print(f"application_label: {result.identity['application_label']}")
        print(f"version_name: {result.identity['version_name']}")
        print(f"version_code: {result.identity['version_code']}")
        print(f"debuggable: {result.identity['debuggable']}")
        if "signing_cert_sha256" in result.identity:
            print(f"signing_cert_sha256: {result.identity['signing_cert_sha256']}")
        return 0

    print("APK gate FAILED", file=sys.stderr)
    print(f"mode: {args.mode}", file=sys.stderr)
    print(f"apk: {apk_path}", file=sys.stderr)
    if result.sha256 is not None:
        print(f"sha256: {result.sha256}", file=sys.stderr)
    for error in result.errors:
        print(f"- {error}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
