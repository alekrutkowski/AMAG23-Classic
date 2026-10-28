# Acknowledgments and third-party notices

AMAG23 Classic is a new, independent implementation. **None of the projects below is presented as the author or publisher of this application.** The names and trademarks belong to their respective owners.

## Hardware, protocol, and earlier software

- **Koolertron:** manufacturer of the [AE-AMAG23 keypad](https://www.koolertron.com/koolertron-one-handed-macro-mechanical-keyboard-with-23-fully-programmable-keys.html), and publisher of the [original programming software and instructions](https://www.koolertron.com/user-manual-software-download). The product photo embedded in `README.md` is fetched from Koolertron's website; copyright in that photo remains with its owner. The photo is **not stored or redistributed** in this repository or in release archives.
- **showjean and contributors**, [ps2avrU / ps2avrGB firmware](https://github.com/showjean/ps2avrU): source for the device's custom firmware protocol, macro events, memory layout, and HID feature-report behavior. Original work includes components and historical influences credited in that repository (e.g. ps2avr and Dulcimer). Follow the upstream project's notices and licensing terms when using its files.
- **showjean and contributors**, [Bootmapper](https://github.com/showjean/Bootmapper): original ps2avrGB configuration client's command flow and expected device behavior. This implementation does not bundle the Bootmapper client or its ActionScript source. Bootmapper's build environment includes [Apache Flex](https://flex.apache.org/) and Adobe AIR, neither required by this project.
- **Original Koolertron/AMAG `gbmgr.exe`**: a user-provided copy was examined **statically** as evidence of how the original vendor utility communicates with the device. It is **not included, called, executed or redistributed**.
- **[QMK Firmware project](https://github.com/qmk/qmk_firmware)**: its [AMAG23 keyboard definition](https://github.com/qmk/qmk_firmware/tree/master/keyboards/amag23) served as a cross-reference for the switch/matrix layout, **not as installed or bundled firmware**. QMK is a separate open-source project with its own licensing, chiefly GPL-2.0-or-later; see the [QMK license file](https://github.com/qmk/qmk_firmware/blob/master/LICENSE).
- **[Objective Development / V-USB](https://www.obdev.at/products/vusb/index.html)**: prior work on the USB software stack and HID bootloader family from which the hardware's `obdev.at` / `HIDBoot` identifiers originate. No V-USB source or binary is bundled.

The protocol description, address safeguards, and remaining uncertainties are documented in [PROTOCOL.md](PROTOCOL.md). These sources are cited for technical background. Their licenses are **not replaced** by the MIT license applying to this project's original source. In particular, a publicly accessible GitHub repository does not itself grant permission to copy its code without regard to its original license.

## Application runtime and operating-system APIs

No external JavaScript library, CSS framework, native HID helper executable, package manager, or remote dependency is bundled or required by the application.

- **[PowerShell](https://github.com/PowerShell/PowerShell)**: the local application host. PowerShell is [MIT licensed](https://github.com/PowerShell/PowerShell/blob/master/LICENSE.txt).
- **[.NET](https://dotnet.microsoft.com/)**: runtime used by PowerShell and the embedded C# interop layer. Licensing and notices: [dotnet/runtime](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT).
- **Microsoft Windows HID and SetupAPI**: system APIs used for device discovery and feature reports. Documentation: [HidD_GetFeature](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getfeature), [HidD_SetFeature](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setfeature), [SetupAPI](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/setupapi).
- **Linux kernel hidraw**: system interface used for HID reports. Documentation: [Linux HID raw API](https://www.kernel.org/doc/html/latest/hid/hidraw.html). Its kernel-side implementation remains part of the Linux kernel; no kernel code is bundled.
- **The user's own Chromium, Chrome, Edge, or other modern browser**: renders the local GUI. The project does not redistribute a browser.

## Test-only tooling

These are **not runtime dependencies** and are **not required by end users**:

- [Node.js](https://nodejs.org/) (JavaScript codec unit tests; [license](https://github.com/nodejs/node/blob/main/LICENSE)).
- [Python](https://www.python.org/) (browser test harness; [license](https://docs.python.org/3/license.html)).
- [Playwright](https://playwright.dev/python/) (browser-driven test automation; [license](https://github.com/microsoft/playwright/blob/main/LICENSE)).
- [Chromium](https://www.chromium.org/) (test browser; [license and notices](https://chromium.googlesource.com/chromium/src/+/main/LICENSE)).
- [GitHub Actions](https://docs.github.com/en/actions) with the official checkout and Python setup actions, for CI and tagged release packaging.

## License of this repository

Copyright (c) 2026 Alek Rutkowski <alek.rutkowski@gmail.com>.

AMAG23 Classic's own source is provided under the [MIT License](LICENSE). No proprietary manufacturer executable, copyrighted upstream source, firmware image, or manufacturer photo is redistributed in this repository.
