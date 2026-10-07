> **历史归档，不是当前待办或实现规格。** 原位置：[code-review-fix-handoff.md](<../../code-review-fix-handoff.md>)。
> 2026-10-07 归档；正文保留当时审查/检查点的表述及验证结果，不代表当前重新验证。
> 当前状态以 [交接文档](<../../handoff.md>) 和 [制谱器说明](<../../dyna-maker-uv.md>) 为准。

---

# Code Review Fix Handoff

## Scope

This document records a read-only review performed on 2026-09-09. No source
files were changed during that review. The worktree is already dirty; preserve
unrelated user changes and do not reset, checkout, or delete them.

Do not launch Godot for this work. The repository instruction is to let the
user visually inspect gameplay changes. Build and non-Godot tests are enough.

## Required Fixes

### 1. Same-directory v2 saves drop replacement resources

Files:

- `shared/Chart/V2/V2PackageWriter.cs`
- `tools/chart-editor-tests/Program.cs`

`V2PackageWriter.Write` copies `ExternalResources` into staging, validates the
staged package, then takes a special same-directory path. That path invokes
`ReplaceJsonFiles`, which publishes only `meta.json` and chart JSON files.
The staging directory is then removed.

Consequences when `SourceDirectory == DestinationDirectory`:

- A replacement at an existing audio or cover path reports success but leaves
  the old resource in the package.
- A replacement that changes the resource path writes metadata pointing at a
  file that disappears with staging, leaving an invalid package.

Implement one atomic publication path that includes all changed resources as
well as JSON. Preserve the existing rollback guarantee: if any publication
step fails, restore the pre-save package state. Be deliberate about resources
that are no longer referenced by the replacement pack; do not leave an
inconsistent package merely to avoid deleting files.

Add regressions to `TestPackageWriter`:

- Same-directory save with a replacement audio file at the existing path;
  assert the destination bytes changed.
- Same-directory save that changes a referenced resource to a new package
  path; assert the new file exists and strict reopen succeeds.
- A forced publication failure that verifies JSON and resources are restored,
  and no staging directory remains.

The current test helper at `BuildWriteRequest` does not pass
`ExternalResources`, so existing passing tests do not cover this behavior.

### 2. Hold release must start the frozen grace interval

Files:

- `client/scripts/game/GameplayMain.cs`
- `shared/Judge/JudgeEngine.cs`
- `tools/core-tests/Program.cs` or a focused non-Godot regression target

`RecordHoldRelease` currently sets `ContactLostAt` but not `GraceDeadline`.
`UpdateHoldState` only calls `SustainJudgementRules.BeginHoldContactLoss` when
`ContactLostAt` is null. Therefore a release that overlaps a started Hold can
leave `ContactLostAt` set forever without a deadline.

Observed bad state transition:

1. Judge a Hold head successfully.
2. Release while overlapping the Hold before its tail.
3. Advance beyond the frozen grace duration.
4. The Hold does not break; subsequent Hold points can still receive
   `Prefect`, even though the tail uses the earlier release time.

At the first loss of contact, always create the complete
`HoldContactLoss` through `SustainJudgementRules.BeginHoldContactLoss`, using
the BPM at that exact loss time. Store both `ContactLostAt` and
`GraceDeadline`. Do not recalculate the deadline after later BPM changes.

Add a deterministic regression for the sequence above. Existing
`TestD4CFrozenGrace` only tests the pure timing rule, not the GameplayMain
state transition. Prefer extracting a small pure state-transition helper over
requiring a Godot scene test.

## Important Follow-up Work

### 3. Make the editor document the sole authoring authority

Files:

- `editor/scripts/EditorFlow.cs`
- `editor/scripts/AuthoringCanvas.cs`
- `tools/chart-editor-core/EditorDocument.cs`
- `tools/chart-editor-core/EditorCommands.cs`

The current shell copies an `EditableChart` into the canvas when opening a
chart. The canvas then mutates its own `Notes` and `BpmEvents` collections.
Those mutations do not execute document commands, update document dirty state,
participate in undo/redo, or feed `BuildAllChartSnapshots`.

This is consistent with the current memory-draft limitation, but saving must
not be connected until this boundary is fixed. Implement a view/controller
adapter where canvas actions become `EditorDocument.Execute(...)` commands and
the canvas redraws from document state. Keep `ExactBarTime` authoritative;
do not round-trip the canvas's floating second values back to v2 timestamps.

Add editor-core tests for add, move, resize, delete, BPM edits, undo/redo, and
snapshot serialization through the same command path used by the UI.

### 4. Harden legacy package resource paths

Files:

- `client/scripts/game/LegacyChartMetadata.cs`
- `client/scripts/game/ChartPack.cs`

Legacy cover paths are validated as package-relative, but chart `file` and
audio paths are accepted as arbitrary nonempty strings and are then joined to
the package directory. Player packages live in `user://charts`, so reject
empty segments, `.`, `..`, backslashes, drive/URI forms, and paths resolving
outside the package before any `FileAccess` call. Apply equivalent regular-file
and reparse-point checks where the platform APIs permit them.

The v2 loading path is already strict at actual chart loading. Apply the same
failure behavior to legacy packages and avoid exposing malformed packages as
playable cards during catalog scanning.

## Performance and Hygiene Backlog

- Cache the current v2 `ExactBarTime` and scroll speed once per gameplay frame.
  `VisualDistanceFor` currently repeats exact time conversion for every active
  note.
- Cache PCHIP slopes or polynomial coefficients for Smooth path runs in
  `V2PathEvaluator`. It currently allocates slope arrays for each evaluation.
  Benchmark a dense Smooth Hold/Mixer chart before and after, and retain exact
  output tests.
- Reuse the per-frame press and release buffers in `GameplayMain`.
- Establish one source for gameplay geometry values. `GameplayMain` and
  `GameplayStageGeometry` currently duplicate them; retain the measurement
  comments required at the top of `GameplayMain`.
- The client and editor contain near-identical gameplay renderer, note view,
  visual mapper, and shader files. Use an explicit linked-source/shared-source
  mechanism or a hash consistency test.
- Add CI for client build, editor build, core tests, chart-editor tests, and
  release tests. Document how the offline `NU1900` warning is handled.
- Keep generated captures/APKs in ignored local directories, but add a
  documented retention and cleanup command. Split future commits by runtime,
  editor, release policy, and documentation.

## Validation

Run from the repository root after code changes:

```powershell
dotnet build client/DynamiteUniverse.csproj --no-restore
dotnet build editor/DynaMakerUv.Editor.csproj --no-restore
dotnet run --project tools/core-tests/CoreTests.csproj
dotnet run --project tools/chart-editor-tests/ChartEditorTests.csproj
python -B -m unittest discover -s tools/release/tests -p 'test_*.py'
git diff --check
```

Review baseline:

- Client build: 0 warnings, 0 errors.
- Editor build: 0 errors; one offline `NU1900` vulnerability-index warning.
- Core tests: passed.
- Chart editor tests: passed.
- Release tests: 35 passed.
- `git diff --check`: no whitespace errors; Git reported CRLF conversion
  notices for existing modified files.

Do not add original game assets or charts to tracked or public-release content.

## Follow-up fixes verified (2026-09-09)

The follow-up review found five remaining defects in the first implementation. They have now
been fixed under the user's authorization:

- Same-directory publication ends its rollback boundary before backup/staging cleanup. A
  cleanup exception preserves the published package and reports the leftover temporary files.
- Canvas clicks and unchanged edits do not create history entries. Spatial-only drags retain
  the original exact BarTime, including arbitrary fractions such as `1/3`.
- Drag anchors belong to the grabbed note or path node. Center-to-left conversion is explicit;
  path previews use the same exact bar delta as the final command. Cancel restores the entire
  projection from the document.
- Placement, dragging, and grid rendering use the actual BPM timeline instead of a fixed
  `1.6 seconds/bar` grid. Grid drawing enumerates only the visible time span.
- Legacy path validation checks the package root and every parent directory for links/reparse
  points as well as the leaf file. Missing directories are not misclassified as reparse points.

Validation: client and editor build with zero errors; editor retains the offline `NU1900`
warning. Core tests, editor tests (including exact click/drag and Windows backup-cleanup
failure regressions), and all 35 release tests passed. The compiled legacy directory check
was also exercised against real Windows junctions for the package root and intermediate
directories; all six cases passed. The temporary verification project and junctions were
removed. `git diff --check` reports no whitespace errors. Godot was not launched; visual
inspection remains with the user.
