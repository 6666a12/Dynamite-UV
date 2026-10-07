> **历史归档，不是当前待办或实现规格。** 原位置：[handoff-editor-rebuild.md](<../../handoff-editor-rebuild.md>)。
> 2026-10-07 归档；正文保留当时审查/检查点的表述及验证结果，不代表当前重新验证。
> 当前状态以 [交接文档](<../../handoff.md>) 和 [制谱器说明](<../../dyna-maker-uv.md>) 为准。

---

# Editor rebuild checkpoint (2026-08-24)

The former Avalonia UI projects (`tools/chart-editor`,
`tools/chart-editor-audio-check`, and `tools/chart-editor-resource-check`) were
removed. `tools/chart-editor-core` and its tests remain as UI-independent v2
document infrastructure.

The replacement desktop project is `editor/`, a clean-room Godot 4.7.1 .NET
implementation of the original DynaMaker PC interaction model. It currently
provides a fixed 1920x1080 stage, Center/Left/Right/Events sides, Normal/Chain/
Hold (two-stage tail)/Select tools, mouse placement and dragging, path-node edits,
width resize, BPM events, delete/escape, snap/grid controls, keyboard transport
shortcuts, and separate blank-canvas versus selected-object context menus. The
default window is canvas-only apart from non-interactive status labels: no
permanent side rail, inspector, transport controls, menu button, or save button.

XML/DY export, audio waveform/mixer, background assets, animation/particles, and
hitsound/music preview are explicit placeholders. No upstream source, chart, or
borrowed asset is copied into the new editor.

Static Roslyn compilation of both editor scripts completed with 0 errors (one
cross-target `System.Runtime` warning). The Godot project now restores and builds
with the locally cached SDK packages; the only build warning is NuGet audit data
being unavailable offline. A console Godot launch with `--quit-after` loads the
scene and both scripts successfully.
