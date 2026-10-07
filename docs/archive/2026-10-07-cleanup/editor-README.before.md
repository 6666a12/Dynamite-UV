# DynaMaker UV

Godot 4.7.1 .NET desktop chart authoring surface. The editor is a clean-room
implementation of the open-source DynaMaker interaction model; it does not load
the upstream source tree or its borrowed game assets.

Before launching from a clean checkout, build the C# project once so Godot can
load the generated assembly:

```powershell
dotnet restore editor/DynaMakerUv.Editor.csproj --ignore-failed-sources
dotnet build editor/DynaMakerUv.Editor.csproj --no-restore
```

The current first pass is intentionally focused on the desktop editing loop:
fixed 1920x1080 stage, Center/Left/Right/Events sides, Normal/Chain/Hold/Select
tools, two-stage Hold tails, path-node and width editing, BPM markers, snap/grid,
transport shortcuts, and the original canvas-first right-click menu split. The
default window has no persistent tool rail or save button; lightweight status
text is kept for desktop editing feedback.
Its fixed stage is the scene-owned `ChartCanvas/SharedGameplayStage` hierarchy in
`scenes/editor_main.tscn`. That node uses the same clean-room visual classes as
the game (`GameplayBackdrop`, `GameplayStageRenderer`, `NoteView`,
`NoteVisualSpec` and `note_surface.gdshader`), rather than a separate editor
approximation. The source files under `scripts/game/` and `shaders/` are byte-for-
byte shared with the client copies.
Package
export, waveform/audio controls, background media, animation/particles, and
hitsound/music preview are not wired yet and report explicit status messages.
