# Third-Party Notices

This file lists third-party content that is intentionally redistributed by the
current repository or public client build. It does not grant a license for the
DUX-Community project's own code or assets.

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
