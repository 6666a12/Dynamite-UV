# Windows editor test-package baseline

As of 2026-11-12, the user confirmed `windows-x64-test-v1` as the current test
baseline. Package identity, hashes and acceptance scope are maintained only in
[verification.md](../../docs/verification.md#当前-windows-编辑器测试包基线).
Use the explicit profile for subsequent test builds; the no-profile CLI default
is still the conservative full-runtime path, not the agreed slim baseline.

`build.py` creates a **new local candidate directory**, never publishes it. Default
behavior retains the full bundled .NET 10 runtime. Existing packages are not edited.

## Experimental size profiles

- `--trimmed-fallback`: ordinary framework-dependent `shell/` still uses compatible
  system .NET first. If that probe fails, the launcher probes and starts a separate
  self-contained `shell-bundled/` app. This is an application-specific ILLink output,
  **not** a shared .NET installation. Genuine application failures are not retried.
- `--trimmed-stage`: root the complete GodotSharp and project assemblies, preserve
  native hosting/JSON/COM, and trim the reachable .NET 9 runtime. Never hand-delete
  framework DLLs. `stage-trim.props` isolates intermediates per project;
  `stage-trim.targets` documents native entry-point roots.
- `--stage-from <prior-candidate/stage>`: explicitly reuse a known candidate's stage
  resources when the official exporter cannot complete. The manifest records the
  source path and PCK hash; this is not a fresh export. Combining it with
  `--trimmed-stage` republishes managed code from current source.
- `--stage-template <exe>`: replace the exported native engine with a matching
  4.7.1/a13da4feb template. The PCK and managed payload still come from the official
  exporter. Native HUD/resource regression is mandatory for customized stages.
  Replacing the EXE currently does not apply the export preset's icon/version
  resources to that custom binary; the public entry remains DynaMakerUV.exe.

Both trimming profiles preserve reflection used by localization, preferences,
Dock layout and IPC. Third-party trim-analysis warnings remain; passing the covered
regressions is not proof that every reflection/native/file-dialog path is safe.
The profiles remain opt-in, not the default release configuration.

## Fixed test profile and uninstall

User-approved interim test profile: `--profile windows-x64-test-v1`, defined in
`test-profile.json`. It enables both trimming modes and pins runtime versions and
the tested template SHA-256. Supply `--stage-template` explicitly; mismatches fail.
PCK reuse still requires explicit `--stage-from` and is recorded, never automatic.
No further size cuts are implicit in this profile. Version/security updates require
reviewing and refreshing the pinned profile, not keeping old runtimes forever.

Each package includes `Uninstall.cmd` and `uninstall.ps1`. Close the app, double-click
Uninstall.cmd and confirm removal. Only manifest-listed, hash-matching files are
removed, then only empty known subdirectories. Extra charts, modified files, the
package root and `%LOCALAPPDATA%/DynaMakerUV` preferences are preserved. No elevation,
registry changes or recursive root deletion. The script rejects path traversal and
reparse points and preflights file locks. System PowerShell execution policy is
respected; policy-blocked scripts require administrator-approved handling, not a
bundled bypass. `-CheckOnly` is non-destructive; `-ConfirmRemoval` is for disposable
automated fixtures. Interrupted uninstalls may leave files and need manual cleanup.

## Custom native template

Fetch upstream Godot **4.7.1-stable** into a new empty source directory. Keep helper
scripts and the build-tool Python environment outside that checkout. Install SCons
in that isolated environment and make MinGW-w64 available on PATH. Then run:

```powershell
python -I tools/editor-release/build_template.py <source> --python <venv>/Scripts/python.exe
```

The recipe pins the source revision and rejects tracked modifications. It disables
Vulkan/D3D12 and unused navigation/import modules, but **retains 3D core APIs**:
`disable_3d=yes` breaks the official GodotSharp Viewport static bindings even in a
2D app. It keeps OpenGL Compatibility,
Mono, 2D GPU particles, font shaping and audio. The first local build lacks optional
ANGLE, WinRT TTS and AccessKit SDKs; upstream disables those drivers with warnings.
That narrows GPU fallback/accessibility coverage versus an official template and
must be evaluated before making this a default. It uses size optimization without LTO
for the first experiment. It does not change the game's renderer or official global
templates. A matching managed/native API and actual HWND/GPU smoke remain mandatory.

## Optional chart video export

Add `--with-video-export --capture-native <native-build>` to a **new** local candidate to include `capture/`, a
self-contained Windows export of the official game renderer with a streaming MovieWriter.
Build the three native inputs with the [pinned Windows SDK recipe](<../gameplay-capture/native/README.md>).
Rebuild older native outputs: the variant file now contains two lines (variant and
linked DLL SHA-256). `--capture-qa` accepts a matching delay-injection DLL only under
`.tmp/`; do not distribute it.

The capture worker exports from a temporary project with only tracked source files,
approved artwork (including executable and project icons), and fresh imports. The
normal client tree and extension registry are not modified. The capture PCK is then
audited entry by entry against staged inputs and remap targets. Both the editor and
the finished capture EXE must report the pinned engine build via `--headless --version`
(before scene initialization). This validates identity, not actual MovieWriter load or
GPU runtime compatibility. The candidate records the worker version/hashes and external
FFmpeg policy in `distribution.json`; it does not change Android packages or the pinned
test profile.

FFmpeg is not redistributed or installed automatically. Discovery checks the last used
path, `DYNA_FFMPEG`, the process/system/user PATH, then common WinGet, Scoop,
Chocolatey and portable layouts (including `Program Files\ffmpeg`, `C:\ffmpeg` on
systems where Windows lives on C:, and `ffmpeg.exe` beside the Shell). Incompatible
candidates are skipped. To install on a machine with WinGet, one option is
`winget install --id Gyan.FFmpeg -e`; a build extracted elsewhere can be found via
Locate FFmpeg in File → Export Chart Video. The selected codec needs `libx264`,
`libx265` or `libsvtav1` respectively, plus the AAC encoder. Discovery also checks
for `-fps_mode` and `-enc_time_base` support. If using a shared FFmpeg build, keep
its own dependent DLLs beside it. End users
do not need Python, the Godot editor or a developer checkout. Capture opens a
GPU-backed game window; silent device output does not disable the MP4 audio mix.

## Verification and comparison

```powershell
python -I tools/editor-release/verify_launcher.py <candidate>
python -I tools/editor-release/measure.py <baseline> <candidate>
```

The launcher helper verifies the system-first and forced-bundled selections against
`distribution.json` and runs the Shell headless regression. It requires compatible
system .NET 10 x64 outside the candidate and does **not** establish clean-machine
behavior with no .NET installed. The measurement helper reports uncompressed bytes
and actual DEFLATE level-6 payload bytes, excluding ZIP headers; it writes no archive.

Real-window persistence smoke uses the existing workflow documented in
`docs/verification.md`; use a new screenshot/preferences directory. Run against the
final candidate, not the source stage. Preserve failed logs separately.

Public distribution still requires the project's clean-worktree, provenance,
license/notice and release checks. These scripts do not approve redistribution,
sign a package or implement updates.
