# Third-Party Notices

This file lists third-party content that is intentionally redistributed by the
current repository or public client build. It does not grant a license for the
Dynamite Universe project's own code or assets.

## Orbitron

- Files: `client/assets/fonts/Orbitron.woff2` and
  `docs/ui-mock/fonts/Orbitron.woff2`
- Copyright: Copyright 2018 The Orbitron Project Authors
  (<https://github.com/theleagueof/orbitron>)
- Reserved Font Name: `Orbitron`
- License: SIL Open Font License, Version 1.1
- Source: <https://github.com/google/fonts/tree/main/ofl/orbitron>
- License text: [`third_party/orbitron/OFL-1.1.txt`](third_party/orbitron/OFL-1.1.txt)
- Public APK copy: `client/assets/licenses/Orbitron-OFL-1.1.txt`

The font is redistributed unmodified. The project's own code and assets are
not licensed under the OFL.

## Space Grotesk

- Files: `client/assets/fonts/SpaceGrotesk-Regular.woff2`,
  `client/assets/fonts/SpaceGrotesk-Bold.woff2` and matching files under
  `docs/ui-mock/fonts/`
- Copyright: Copyright 2018 The Space Grotesk Project Authors
  (<https://github.com/floriankarsten/space-grotesk>)
- License: SIL Open Font License, Version 1.1
- Source: <https://github.com/google/fonts/tree/main/ofl/spacegrotesk>
- License text: [`third_party/space-grotesk/OFL-1.1.txt`](third_party/space-grotesk/OFL-1.1.txt)
- Public APK copy: `client/assets/licenses/SpaceGrotesk-OFL-1.1.txt`

The Regular and Bold fonts are redistributed unmodified. The project's own
code and assets are not licensed under the OFL.

## Godot Engine

- Version used by this project: Godot Engine 4.7.1 .NET
- Copyright: Copyright (c) 2014-present Godot Engine contributors; Copyright
  (c) 2007-2014 Juan Linietsky, Ariel Manzur
- License: MIT
- Source: <https://github.com/godotengine/godot/tree/4.7>
- License text: [`third_party/godot/LICENSE.txt`](third_party/godot/LICENSE.txt)
- Public APK copy: `client/assets/licenses/Godot-LICENSE.txt`

## .NET Runtime

- Runtime family: .NET 9 (`net9.0`; exported runtime version is determined by the
  installed Godot 4.7.1 .NET export template)
- Copyright: Copyright (c) .NET Foundation and Contributors
- License: MIT, with upstream third-party notices
- Source: <https://github.com/dotnet/runtime/tree/v9.0.18>
- License text: [`third_party/dotnet/LICENSE.TXT`](third_party/dotnet/LICENSE.TXT)
- Third-party notices:
  [`third_party/dotnet/THIRD-PARTY-NOTICES.TXT`](third_party/dotnet/THIRD-PARTY-NOTICES.TXT)
- Public APK copies: `client/assets/licenses/DotNet-LICENSE.txt` and
  `client/assets/licenses/DotNet-THIRD-PARTY-NOTICES.txt`

The Public export policy includes all of the above license and notice files,
and the final APK gate verifies their SHA-256 values.

## Research tooling boundary

Files under `tools/` that support behavioral research, inspection or local
asset production are not part of the game APK. External programs used by
those workflows (for example Apktool, Frida, ffmpeg and ComfyUI) must be
installed separately under their respective upstream licenses. Their
executables, signing keys and generated outputs must not be added to this
repository or a public game package.

## DynaMaker Modified reference

- Optional local research checkout (Git-ignored, not included in a clean clone): `third_party/dynamaker-modified-reference`
- Fixed source: <https://github.com/dynamaker-tool/dynamaker-modified>
- Fixed commit: `99a5a6049f5bc3ee69e5c8cd3a72f4d8c1e99a8d`
- Optional local snapshot manifest: `third_party/dynamaker-modified-reference/ORIGIN.md`
- Upstream copyright: Copyright (c) 2021 jmakxd
- License: MIT; [upstream license at the fixed commit](<https://github.com/dynamaker-tool/dynamaker-modified/blob/99a5a6049f5bc3ee69e5c8cd3a72f4d8c1e99a8d/LICENSE>); an optional local copy resides in the snapshot's `LICENSE`.

The source snapshot is an optional, local-only development/reference input. It is
not committed and may be removed once editor development no longer needs it.
Neither building, running nor exporting either Godot project requires it.
DynaMaker UV does not compile, embed, or load upstream JavaScript, HTML, CSS, or JSON.
When installed, the local copy supports these behavioral reference mappings:

- `app/src/Script/mouse.js` -> native editor canvas input
- `app/src/Script/keyboard.js` -> native editor shortcut dispatch
- `app/src/Script/playView.js` -> shared gameplay visual mapping plus editor overlay
- `app/src/Script/startMenuScene.js` -> package selection page
- `app/src/Script/settingsForNewMapScene.js` -> new package flow
- `app/src/Script/function.js` -> replaced by the shared v2 core and writer

Upstream documentation identifies its artwork as borrowed Dynamix material. No
upstream Dynamix artwork, fonts, sounds, charts, icons, UI files, or other
assets are imported into `client/` or `editor/`, and none may be added to an
export. Persisted v2 package behavior is owned by this repository's shared
chart core.
