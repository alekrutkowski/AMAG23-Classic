# Contributing

Thank you for helping improve AMAG23 Classic. Changes should stay focused on the original `20A0:422D` ps2avrGB keypad family and must not introduce firmware flashing or unverified arbitrary USB writes.

## Local checks

Install PowerShell 7.4+ (7.6 LTS recommended), Node.js, and optionally Python with Playwright and Chromium for the graphical tests.

```sh
node tests/test-codec.js
pwsh -NoProfile -File ./Start-AMAG23.ps1 -SelfTest
python tests/test-ui.py  # needs playwright + chromium
```

Tests use simulated configurations or a simulated USB transport; successful tests do not prove physical compatibility.

Start a simulated GUI without a keypad:

```sh
pwsh -NoProfile -File ./Start-AMAG23.ps1 -Demo
```

Do not attach raw personal configuration backups to issue reports or pull requests. Macro files can contain secrets.

## Design principles

- Keep the user-facing controls straightforward, with **actual device state** distinguished from unsaved changes.
- Treat every configuration upload as sensitive: validate devices and memory ranges, save a backup, and verify read-back.
- Keep the app fully local, without network dependencies, telemetry, or bundled manufacturer binaries.
- Add tests for protocol or UI changes. Use a simulated transport for automation; hardware testing must be explicitly described as such.
- For theme changes, maintain readability and keyboard focus indicators in **both themes**. System mode must adapt to OS appearance changes.
- Use the version in `VERSION` as the single source of truth.

## Releases

The GitHub workflows test the code and create a release archive when a matching `vX.Y.Z` tag is pushed. See `scripts/Package-Release.ps1` for the exact packaged file list. The repository includes development tests; the release archive includes only the files needed to launch and document the app, plus its PowerShell self-tests.

Project: MIT license. Copyright (c) 2026 Alek Rutkowski <alek.rutkowski@gmail.com>. Third-party acknowledgments: [ACKNOWLEDGMENTS.md](ACKNOWLEDGMENTS.md).
