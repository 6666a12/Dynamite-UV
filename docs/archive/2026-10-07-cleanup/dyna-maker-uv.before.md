# DynaMaker UV

DynaMaker UV is the standalone Godot 4.7.1 .NET desktop authoring project for
strict Dynamite Universe v2 packages. Its project root is [`../editor`](../editor),
not `client/`. The game and the editor are two independent Godot projects with
separate `project.godot`, main scene, assemblies, and export presets. They share
the versioned C# chart core at build time; neither project launches the other at
runtime.

## Start

From the repository root, build and launch the editor with the Mono Godot
binary (the standard binary cannot load C# projects):

```powershell
dotnet build editor/DynaMakerUv.Editor.csproj
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path editor
```

To open a v2 package directly, pass its `meta.json` path and request the
workbench:

```powershell
& "..\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" --path editor --editor-package=C:\charts\pack\meta.json --editor-workbench
```

Before starting either Godot project, terminate any residual Godot windows as
required by [`handoff.md`](handoff.md):

```powershell
taskkill /F /IM Godot_v4.7.1-stable_mono_win64_console.exe
taskkill /F /IM Godot_v4.7.1-stable_mono_win64.exe
```

The standalone `DynaMaker UV Windows` export preset belongs to `editor/`. The
game project remains at [`../client`](../client): launch it with `--path client`
to enter normal play. The game's disabled `谱面工坊` tile is deliberately
reserved for a future server chart-library route; it is not a local-editor
launcher.

## Scope

The rebuilt shell currently keeps authoring data in memory. It does not yet read
or write legacy `.xml`/`.dy` files or v2 packages. The implemented first pass is
the fixed stage and core mouse model: three note sides plus Events, Normal/Chain/
Hold/Select tools, two-stage Hold tails, path-node and width edits, BPM markers,
Delete/Escape, grid and snap controls, transport, and the compact context menu.
Package export, audio waveform/mixer, background media, animation/particles,
and hitsound/music preview remain explicit follow-up work.

The chart canvas uses the fixed 1920x1080 desktop coordinate space. It is the
entire default window: there is no persistent tool rail or inspector. Its stage
instantiates the same game-scene visual classes and shader used by the client,
with authoring input layered above the shared renderer. Choose sides and tools
with the original right-click canvas menu or number keys.

Current native shortcuts include `1` Normal, `2` Chain, `3` Hold, `4` Select,
`Space` transport, `M` mark, `R` replay/from-mark, `Enter` reset and select,
`A/D` seek, `O/P` offset by 1 ms, `Q/E` hispeed, `S/W` preview rate, `C/V` grid,
`Z/X` temporary time/space snap overrides, arrow keys for side selection, and
`Delete`/`Backspace`.

## Reference and Asset Boundary

The interaction reference is fixed in
`third_party/dynamaker-modified-reference` at
`99a5a6049f5bc3ee69e5c8cd3a72f4d8c1e99a8d` from
<https://github.com/dynamaker-tool/dynamaker-modified>. Its `mouse.js`, `keyboard.js`,
and `playView.js` informed responsibility mapping only; none of their source is
compiled or loaded by either Godot project. Detailed attribution and the original MIT license are
in [`../THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md).

The upstream README states that its visual assets are borrowed from Dynamix.
Those images, fonts, sounds, charts, icons, UI resources, and all other
upstream assets are deliberately omitted from both `client/` and `editor/`.
Only clean-room code and project-owned/generated visual treatment may ship.

The reviewed source maps to the native implementation as follows:

| Upstream source | DynaMaker UV responsibility |
| --- | --- |
| `startMenuScene.js` | package selection and open/new entry |
| `settingsForNewMapScene.js` | new package draft flow |
| `mouse.js` | canvas placement, selection, drag state and context menu |
| `keyboard.js` | native shortcut dispatch |
| `playView.js` | shared gameplay stage renderer plus editor overlays |
| `function.js` | replaced by `shared/` v2 timing, validation and package writer |

The page lifecycle is `package <-> differential <-> chart editor <-> Storyboard`.
The differential page selects an existing Chart or creates one. Storyboard
currently binds the selected Chart context but is
explicitly marked not implemented; it exposes no fake editing controls, writes
nothing to the package and never creates dirty state.

## Release Boundary

The editor is a Windows desktop tool and is never bundled into either Android
APK. The client and editor have separate Godot project roots, so the client
export has no editor scene or editor source path to include. The Public APK
additionally uses a resource allowlist and excludes all
test data. The editor Windows preset uses the same fail-closed approach: its
resource allowlist contains the editor scene and procedural preview shader (with
the project font slots reserved for the visual pass), and it excludes
`editor/testdata/`, which is reserved for local fixtures. The Internal APK remains development-only and may
contain controlled test data, but it does not contain DynaMaker UV. See
[`releasing.md`](releasing.md) for the APK gates.
