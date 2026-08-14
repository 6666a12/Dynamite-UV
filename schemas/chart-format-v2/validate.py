from __future__ import annotations

import hashlib
import json
import math
import wave
from copy import deepcopy
from fractions import Fraction
from pathlib import Path

from jsonschema import Draft202012Validator

ROOT = Path(__file__).resolve().parent
EXAMPLES = ROOT / "examples" / "golden-pack"
VECTOR_PATH = ROOT / "vectors" / "gameplay-digest-v1.json"
TYPE_ORDER = {
    name: index
    for index, name in enumerate(
        ("tap", "drag", "exTap", "hold", "mixer", "mine", "barLine")
    )
}


def load_json(path: Path) -> object:
    def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
        result: dict[str, object] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"{path}: duplicate JSON key {key!r}")
            result[key] = value
        return result

    return json.loads(
        path.read_text(encoding="utf-8"),
        object_pairs_hook=reject_duplicate_keys,
    )


def bar_value(value: dict[str, int]) -> Fraction:
    numerator = value["numerator"]
    denominator = value["denominator"]
    assert numerator < denominator
    assert math.gcd(numerator, denominator) == 1
    if numerator == 0:
        assert denominator == 1
    return Fraction(value["bar"] * denominator + numerator, denominator)


def canonical_json(value: object) -> str:
    # This vector uses ASCII keys/strings and simple finite binary64 numbers, for which
    # this serialization is byte-identical to its RFC 8785 JCS representation.
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def project_note(note: dict[str, object]) -> dict[str, object]:
    result = {key: note[key] for key in ("type", "time", "center", "width")}
    if note["type"] not in ("hold", "mixer"):
        return result

    result["curveToNext"] = note.get("curveToNext", "linear")
    source_nodes = note["nodes"]
    assert isinstance(source_nodes, list)
    nodes = []
    for index, node in enumerate(source_nodes):
        assert isinstance(node, dict)
        projected = {key: node[key] for key in ("time", "center", "width")}
        if index + 1 < len(source_nodes):
            projected["curveToNext"] = node.get("curveToNext", "linear")
        if note["type"] == "hold":
            projected["judge"] = node.get("judge", True)
        nodes.append(projected)
    result["nodes"] = nodes
    return result


def project_lane(notes: list[dict[str, object]]) -> list[dict[str, object]]:
    result = [project_note(note) for note in notes]
    result.sort(
        key=lambda note: (
            bar_value(note["time"]),
            TYPE_ORDER[note["type"]],
            canonical_json(note).encode("utf-8"),
        )
    )
    return result


def build_projection(
    chart: dict[str, object], audio_sha256: str
) -> dict[str, object]:
    scroll_speeds = chart.get("scrollSpeeds") or [
        {
            "time": {"bar": 0, "numerator": 0, "denominator": 1},
            "value": 1,
        }
    ]
    projected_scroll = []
    for index, event in enumerate(scroll_speeds):
        projected = {"time": event["time"], "value": event["value"]}
        if index + 1 < len(scroll_speeds):
            projected["curveToNext"] = event.get("curveToNext", "linear")
        projected_scroll.append(projected)

    return {
        "rulesetId": "dynamite-uv-ruleset-2.0",
        "judgePreset": "hard",
        "audio": {"sha256": audio_sha256},
        "timing": {
            "audioOffsetSec": chart["audioOffsetSec"],
            "bpms": chart["timing"]["bpms"],
        },
        "scrollSpeeds": projected_scroll,
        "notes": {
            "left": project_lane(chart["notesLeft"]),
            "center": project_lane(chart["notesCenter"]),
            "right": project_lane(chart["notesRight"]),
        },
    }


def expect_rejected(
    name: str, validator: Draft202012Validator, instance: dict[str, object]
) -> None:
    assert not validator.is_valid(instance), f"negative case was accepted: {name}"


def validate_schema_and_examples() -> None:
    pack_schema = load_json(ROOT / "pack.schema.json")
    chart_schema = load_json(ROOT / "chart.schema.json")
    meta = load_json(EXAMPLES / "meta.json")
    hard = load_json(EXAMPLES / "chart_hard.json")
    custom = load_json(EXAMPLES / "chart_custom.json")
    assert isinstance(pack_schema, dict)
    assert isinstance(chart_schema, dict)
    assert isinstance(meta, dict)
    assert isinstance(hard, dict)
    assert isinstance(custom, dict)

    Draft202012Validator.check_schema(pack_schema)
    Draft202012Validator.check_schema(chart_schema)
    pack_validator = Draft202012Validator(pack_schema)
    chart_validator = Draft202012Validator(chart_schema)
    pack_validator.validate(meta)
    chart_validator.validate(hard)
    chart_validator.validate(custom)

    case = deepcopy(meta)
    case["charts"][0]["difficulty"] = "tutorial"
    expect_rejected("tutorial", pack_validator, case)
    case = deepcopy(meta)
    del case["charts"][1]["difficultyKey"]
    expect_rejected("custom without difficultyKey", pack_validator, case)
    case = deepcopy(meta)
    case["charts"][0]["difficultyKey"] = "HARDER"
    expect_rejected("standard with difficultyKey", pack_validator, case)
    case = deepcopy(meta)
    case["charts"][0]["unrated"] = True
    expect_rejected("level and unrated", pack_validator, case)
    case = deepcopy(meta)
    del case["audio"]
    expect_rejected("missing audio fallback", pack_validator, case)

    for value in (0, -1):
        case = deepcopy(hard)
        case["scrollSpeeds"][0]["value"] = value
        expect_rejected(f"scroll {value}", chart_validator, case)
    case = deepcopy(hard)
    case["scrollSpeeds"][0]["curveToNext"] = "smooth"
    expect_rejected("smooth scroll", chart_validator, case)
    case = deepcopy(hard)
    case["notesCenter"].append(deepcopy(case["notesLeft"][0]))
    expect_rejected("center mixer", chart_validator, case)
    case = deepcopy(hard)
    case["notesCenter"][1]["nodes"] = []
    expect_rejected("empty Hold", chart_validator, case)
    case = deepcopy(hard)
    case["unknown"] = True
    expect_rejected("unknown field", chart_validator, case)
    case = deepcopy(hard)
    case["notesLeft"][0]["nodes"][0]["judge"] = True
    expect_rejected("Mixer judge", chart_validator, case)


def validate_semantics() -> None:
    meta = load_json(EXAMPLES / "meta.json")
    charts = [
        load_json(EXAMPLES / "chart_hard.json"),
        load_json(EXAMPLES / "chart_custom.json"),
    ]
    assert isinstance(meta, dict)
    assert all(isinstance(chart, dict) for chart in charts)

    with wave.open(str(EXAMPLES / "audio.wav"), "rb") as source:
        duration = source.getnframes() / source.getframerate()
        assert source.getnchannels() == 1
        assert source.getsampwidth() == 2
        assert source.getframerate() == 8000
    assert duration == 5
    preview = meta["preview"]
    assert preview["startSec"] + preview["durationSec"] <= duration

    entries = meta["charts"]
    assert len({entry["id"] for entry in entries}) == len(entries)
    assert len({entry["file"].casefold() for entry in entries}) == len(entries)

    for chart in charts:
        entry = next(item for item in entries if item["id"] == chart["chartId"])
        assert load_json(EXAMPLES / entry["file"])["chartId"] == entry["id"]
        bpms = chart["timing"]["bpms"]
        assert bar_value(bpms[0]["time"]) == 0
        assert all(
            bar_value(left["time"]) < bar_value(right["time"])
            for left, right in zip(bpms, bpms[1:])
        )

        scroll = chart.get("scrollSpeeds", [])
        if scroll:
            assert bar_value(scroll[0]["time"]) == 0
            assert all(
                bar_value(left["time"]) < bar_value(right["time"])
                for left, right in zip(scroll, scroll[1:])
            )
            assert "curveToNext" not in scroll[-1]

        ids = []
        main_times = []
        for lane_name in ("notesLeft", "notesCenter", "notesRight"):
            lane = chart[lane_name]
            lane_times = [bar_value(note["time"]) for note in lane]
            assert all(left <= right for left, right in zip(lane_times, lane_times[1:]))
            for note in lane:
                ids.append(note["id"])
                assert note["width"] / 2 <= note["center"] <= 5 - note["width"] / 2
                if note["type"] != "barLine":
                    main_times.append(bar_value(note["time"]))
                if note["type"] not in ("hold", "mixer"):
                    continue
                points = [note, *note["nodes"]]
                assert all(
                    bar_value(left["time"]) < bar_value(right["time"])
                    for left, right in zip(points, points[1:])
                )
                assert "curveToNext" not in note["nodes"][-1]
                for node in note["nodes"]:
                    ids.append(node["id"])
                    assert node["width"] / 2 <= node["center"] <= 5 - node["width"] / 2
                if note["type"] == "hold":
                    assert note["nodes"][-1].get("judge") is True
                    main_times.extend(
                        bar_value(node["time"])
                        for node in note["nodes"]
                        if node.get("judge", True)
                    )
                else:
                    assert lane_name != "notesCenter"
                    head = bar_value(note["time"])
                    end = bar_value(note["nodes"][-1]["time"])
                    main_times.extend(
                        head + Fraction(index, 8)
                        for index in range(1, math.floor((end - head) * 8) + 1)
                    )
        assert len(ids) == len(set(ids))
        assert main_times

        segments = []
        accumulated = chart["audioOffsetSec"]
        for index, event in enumerate(bpms):
            if index:
                previous = bpms[index - 1]
                accumulated += float(
                    bar_value(event["time"]) - bar_value(previous["time"])
                ) * 240 / previous["bpm"]
            segments.append((bar_value(event["time"]), accumulated, event["bpm"]))

        def seconds(time: Fraction) -> float:
            start, start_sec, bpm = max(
                segment for segment in segments if segment[0] <= time
            )
            return start_sec + float(time - start) * 240 / bpm

        judged_seconds = [seconds(time) for time in main_times]
        assert min(judged_seconds) >= 0
        assert max(judged_seconds) <= duration + 0.050


def validate_digest() -> tuple[str, str]:
    chart = load_json(EXAMPLES / "chart_hard.json")
    vector = load_json(VECTOR_PATH)
    assert isinstance(chart, dict)
    assert isinstance(vector, dict)

    audio_sha256 = hashlib.sha256((EXAMPLES / "audio.wav").read_bytes()).hexdigest()
    projection = build_projection(chart, audio_sha256)
    canonical = canonical_json(projection)
    digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()

    assert all(ord(character) < 128 for character in canonical)
    assert vector["audioSha256"] == audio_sha256
    assert vector["projection"] == projection
    assert vector["canonicalJson"] == canonical
    assert vector["expectedSha256"] == digest
    return audio_sha256, digest


def main() -> None:
    for path in sorted(ROOT.rglob("*.json")):
        load_json(path)
    validate_schema_and_examples()
    validate_semantics()
    audio_sha256, digest = validate_digest()
    print("v2 format fixtures: PASS")
    print(f"audio SHA-256: {audio_sha256}")
    print(f"gameplay digest: {digest}")


if __name__ == "__main__":
    main()
