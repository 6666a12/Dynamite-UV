"""Minimal bounded reader for Godot 4 ECFG custom feature strings (not arbitrary Variants)."""
import struct


def custom_features(data: bytes) -> set[str]:
    position = 0

    def take(size):
        nonlocal position
        if size < 0 or size > len(data) - position:
            raise ValueError("truncated project.binary")
        value = data[position:position + size]
        position += size
        return value

    def integer():
        return struct.unpack('<I', take(4))[0]

    if take(4) != b'ECFG':
        raise ValueError("invalid project.binary magic")
    count = integer()
    if count > len(data) // 8:
        raise ValueError("invalid project.binary property count")
    features = None
    keys = set()
    for _ in range(count):
        key = take(integer()).decode('utf-8')
        if key in keys:
            raise ValueError("duplicate project.binary property")
        keys.add(key)
        value = take(integer())
        if key != '_custom_features':
            continue
        if len(value) < 8:
            raise ValueError("truncated custom feature Variant")
        kind, length = struct.unpack_from('<II', value)
        if kind != 4 or len(value) != 8 + ((length + 3) & ~3):
            raise ValueError("custom features must be a string Variant")
        if any(value[8 + length:]):
            raise ValueError("invalid custom feature padding")
        features = set(value[8:8 + length].decode('utf-8').split(','))
    if position != len(data) or features is None:
        raise ValueError("missing custom features or trailing project.binary bytes")
    return features
