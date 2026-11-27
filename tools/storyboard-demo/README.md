# Prism Relay — original Storyboard fixture

This tool generates one **local strict v2.0** pack for visual and Windows load testing. It reads no charts, images, audio, fonts, or templates. The title and graphic plates are drawn in code, including the lettering; the music is a hand-composed and synthesized stereo cue. Nothing here is a shipped game asset.

## Generate and select a variant

Requires Python 3.10+ and Pillow (tested with Pillow 13.0.0). No Godot, ffmpeg, asset service, network access or system font is used by the generator. From the repo root:

```powershell
# Install Pillow in your Python environment if it is not already available.
python -m pip install Pillow==13.0.0
python -B tools/storyboard-demo/generate.py
# Or supply a different new/empty pack directory:
python -B tools/storyboard-demo/generate.py --output .tmp/storyboard-demo/another-run
```

The default pack root is `.tmp/storyboard-demo/prism-relay/`, relative to the repo rather than to the shell's working directory. `--output` is the **pack root** itself. A nonempty destination or destination symlink/junction is an error; files are created exclusively, never updated in place. If a run is interrupted, choose a new directory rather than reusing a partial pack. On the same Python/Pillow build, repeated runs to empty directories produce identical bytes.

Open the pack via the normal local chart library, or select its absolute root and exact chart ID with the existing Editor/Internal test harness (`DUV_VERIFY_PACK`, `DUV_VERIFY_MODE=v2`, `DUV_VERIFY_CHART_ID`). `DUV_VERIFY_AUTO=1` is useful for comparable recordings. For intended runs, turn on **Settings → Display → Storyboard** and choose full UI motion; reduced/off falls back to the stock background in this prototype. All entries share the same 20.400-second, 44.1 kHz stereo PCM WAV, identical chart notes and timing, and the same judgement preset and Gameplay Digest. The chart has three populated lanes, two holds, two mixers, and a denser post-switch phrase; BarTime zero is 0.600 s, and 120 BPM changes to 160 at BarTime `4+1/4` (9.100 s media time).

| Exact chart ID | Scenario |
| --- | --- |
| `demo-keep` | Full visual sequence, original gameplay background remains behind it. |
| `demo-replace` | Same sequence, except the gameplay background is replaced with `#10131A`. |
| `title-only` | A1 only: entrance, hold, fade; default Keep backdrop. |
| `stress-100` / `stress-500` / `stress-1500` | Exactly 100 / 500 / 1,500 separate, simultaneous sprites with interpolated rotation from 4.000 s inclusive to 16.000 s exclusive. Zero Storyboard sprites outside that window; there are no extra scene sprites. |

The stress levels share the demo's 18×18 PNG, the same replacement backdrop, and the same first *N* sprite positions and angle tracks. Compare 6.000, 10.000 and 14.000 s, or record the steady 5–15 s window; check 3.999/4.000 and 15.999/16.000 s for the draw-count transitions. Use the **same stress chart** with Storyboard **Off** as the ordinary gameplay baseline; Off restores the stock backdrop, so compare the three Full levels directly when isolating scaling under a fixed backdrop. A fixed media clock helps visual frame comparisons, but elapsed-time performance measurements should use the benchmark's intended real-time or per-frame-timing mode.

The pack includes `meta.json`, six strict chart JSONs in `charts/`, `audio.wav`, `cover.png`, and `storyboard/index.json`. The index only has `version: 0` and the `charts` mapping. Its paths are relative to `storyboard/`; those scripts in turn resolve their generated PNGs in `assets/`. No renames or index edits are needed to select a variant.

## Visual recording cues

Times are **raw media seconds**, not countdown or calibrated judgement time. Include the gameplay HUD and all three lanes in the crop so the recording shows whether notes remain legible.

| Time (s) | Capture |
| --- | --- |
| 0.58 → 1.70 → 3.10 | Title slides in, holds at its anchor, then fades without moving; absent at 3.540 s. Record both backdrop modes around 1.70 s. |
| 3.75 / 4.15 / 4.90 | Left curved-flight atlas token progresses cyan → amber → violet; the right token loops using nearest sampling. |
| 5.55 / 7.10 | Left token follows the curved path, holds its last atlas frame, and keeps its angle; the right rotated crop clips the sweeping beam against the angled gradient. The nearest token is flipped between 5.200 and 7.770 s. |
| 8.85 → 9.10 → 9.51 → 9.85 | First additive clip begins, peaks at the tempo switch, has a real gap, then peaks again on the new tempo after 0.750 s. The carrier label is locked to the media clock. |
| 11.2–12.1 / 15.1 | Dense taps with right / left Mixer, plus the rotated crop and full graphics. Useful for checking real judgement-line and note contrast. |
| 19.3 | Quiet coda title after the final 18.475 s chord, before the audio ends at 20.400 s. |

For a non-graphical strict pack check, run:

```powershell
dotnet run --project tools/chart-tool -- validate-v2 .tmp/storyboard-demo/prism-relay
ffprobe -v error -show_entries stream=codec_name,sample_rate,channels,duration .tmp/storyboard-demo/prism-relay/audio.wav
```

The generator itself checks every PNG and reads its generated RIFF/PCM WAV back. These checks do not replace in-game review of clipping, compositing, readability, or frame performance.

To compare direct rendering and instancing on Windows Forward Mobile, build the Debug client and run both paths in the same session (with no other visible Godot instance):

```powershell
python -B tools/storyboard-demo/verify_windows.py bench --compare-batch --rounds 3 --pack .tmp/storyboard-demo/prism-relay --output .tmp/storyboard-batch-ab
```

The runner fixes brightness to 100%, verifies the active sprite counts and actual instanced batches, and records separate logs. See [measured results and limitations](../../docs/storyboard-runtime.md#优化后的-ab).

For a private original-asset workload rather than synthetic Prism Relay sprites, see the [Twin Bloom profile and reproducible settings](../../docs/storyboard-twin-bloom-profile.md) and the two offline conversion guides: [4K chart](mania_local_chart.md) and [storyboard excerpts](twin_bloom_local_osb.md). The third-party inputs and all generated media must stay in ignored local storage.

## Streaming regression fixture

[The streaming generator](<generate_streaming_fixture.py>) writes a different, original 20-second pack to a new ignored directory:

```powershell
python -B tools/storyboard-demo/generate_streaming_fixture.py --output .tmp/storyboard-demo/streaming-fixture
$env:DUV_VERIFY_PACK = (Resolve-Path .tmp/storyboard-demo/streaming-fixture).Path
$env:DUV_STORYBOARD_MEMORY_MIB = '128' # repeat with '256'
<Godot-Mono> --path client --render-thread safe res://scenes/verification/storyboard_residency.tscn
```

The five charts cover 128-slot reuse and retirement, 129-slot fallback/recovery, aliases and separate crops, 1024 declarations with a small active set, and eight simultaneous 16 MiB textures. Run with `--headless` for scheduler behavior; use Forward Mobile/Vulkan to verify backend retirement. Follow the [Godot run convention](<../../docs/verification.md#运行截图和录屏>) before launching. These checks do not certify real-time presentation or production video.
