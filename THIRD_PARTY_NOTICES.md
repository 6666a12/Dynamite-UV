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

## Chart editor audio dependencies

The desktop chart editor under `tools/chart-editor/` redistributes the following
NuGet libraries. They are editor-only dependencies and are not included in the
game APK.

- **NAudio 2.2.1** and component packages — Copyright 2008-2026 Mark Heath;
  MIT; <https://github.com/naudio/NAudio>
- **NLayer 2.0.1** — Copyright 2018 Mark Heath, Andrew Ward & Contributors;
  MIT; <https://github.com/naudio/NLayer>
- **NAudio.Vorbis 1.5.0 / NVorbis 0.10.4** — Copyright Andrew Ward; MIT;
  <https://github.com/naudio/Vorbis> and <https://github.com/NVorbis/NVorbis>
- **Concentus.Oggfile 1.0.7** — Copyright 2020 Andrew Ward and Logan Stromberg;
  MIT; <https://github.com/lostromb/concentus.oggfile>
- **Concentus 2.2.2** — copyright held by the Opus contributors listed by the
  package; 3-clause BSD-style Opus license;
  <https://github.com/lostromb/concentus>
- **BunLabs.NAudio.Flac 2.0.1** — authorship attributed to Vivelin by the NuGet
  package; Microsoft Public License (Ms-PL);
  <https://github.com/BunLabs/NAudio.Flac>

The dependency packages carry their license metadata/text in NuGet. Windows
Media Foundation is used through the operating system for M4A/AAC and is not
redistributed by this repository.

## Research tooling boundary

Files under `tools/` that support behavioral research, inspection or local
asset production are not part of the game APK. External programs used by
those workflows (for example Apktool, Frida, ffmpeg and ComfyUI) must be
installed separately under their respective upstream licenses. Their
executables, signing keys and generated outputs must not be added to this
repository or a public game package.
