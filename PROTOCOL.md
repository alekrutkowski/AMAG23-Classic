# AMAG23 Classic: recovered configuration protocol

## Evidence and boundaries

This is an independent host implementation of the standard ps2avrGB configuration protocol, not a browser workaround, replacement firmware or a wrapper around `gbmgr.exe`.

Evidence used:

1. User-supplied `gbmgr.exe`, analyzed statically without execution. SHA-256: `d3684ee71c693a7f5523be8fc309cac136365b23768fe3d4a31224b847c0f702`. Relevant symbols include `_getKeymap` at `0x401710`, `_getMacro` at `0x401930`, `_getQuickMacro` at `0x401AC0`, `_setDeviceAction` at `0x401DA0`, `_startBootloader` at `0x40485E` and `_uploadData.constprop.4` at `0x404BF3`. These are executable virtual addresses, not USB addresses. The original program is not included in this distribution.
2. showjean's Bootmapper `GBManager.as`: client-level read/upload sequences, including its use of the existing bootloader for configuration HEX data. `https://github.com/showjean/Bootmapper/blob/master/src/GBManager.as`
3. ps2avrGB `vusb.c` and `options.h`: firmware handlers, report-length dispatch, EEPROM bank preparation and streaming. `https://github.com/showjean/ps2avrU/blob/master/firmware/ps2avrGB/vusb.c` and `https://github.com/showjean/ps2avrU/blob/master/firmware/ps2avrGB/options.h`
4. `hardwareinfo.h` and Bootmapper's board configuration JSON: memory regions, bank sizes and layer/matrix dimensions. `https://github.com/showjean/ps2avrU/blob/master/firmware/ps2avrGB/hardwareinfo.h` and `https://github.com/showjean/Bootmapper/blob/master/src/json/bootMapper.json`
5. `keymap.h`, `keyindex.c`, `custommacro.c` and `bootmapper.c`: internal key indices, row-major storage, macro execution and physical-key identification. `https://github.com/showjean/ps2avrU/tree/master/source`
6. QMK's AMAG23 board definition: physical switch-to-matrix mapping only. No firmware from this project is installed or bundled. `https://github.com/qmk/qmk_firmware/blob/master/keyboards/amag23/keyboard.json`
7. Windows HID API and Linux hidraw documentation: `https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getfeature`, `https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setfeature` and `https://www.kernel.org/doc/html/latest/hid/hidraw.html`.

Upstream code is used as a protocol reference, not bundled as this app's source. Its original authors and licenses remain applicable to those upstream projects. References were inspected for this release on 2026-10-28. Manufacturer forks and the exact physical unit remain a validation gap.

## Native transport

Normal VID/PID: `20A0:422D`. Only the vendor-specific HID collection on usage page `FF00` is selected. Windows uses SetupAPI, HID capability discovery, `HidD_GetFeature` and `HidD_SetFeature`. Linux uses `hidraw` and `HIDIOCGFEATURE`/`HIDIOCSFEATURE`. The report ID is placed in the native request buffer's first byte.

The factory firmware uses requested feature-report length as an opcode. Its configuration replies contain raw block bytes beginning at offset zero; the original executable copies them from that offset. This is an intentional nonstandard behavior, not a reason to blindly strip an apparent report ID from returned data. An OS or firmware variant that presents different framing has not been verified.

Windows `HidD_GetFeature` does not expose the actual returned byte count. The backend can test success, but its reported byte count is the requested buffer length. Repeated-read and content checks reduce some failure modes; they cannot prove that all OS/firmware framing behavior is correct.

## Read sequence

Values below are decimal except hex-byte sequences and hex addresses.

- Ready: SET report 1, 8-byte buffer `01 90 00 00 00 00 00 00`.
- Information: GET report 1, requested length 100. Modern size/version/family fields are checked; raw information bytes are retained. A legacy packet without the size field is marked as such, not assigned an invented firmware version.
- Layers: GET report 1, lengths 121, 122, 123, 124. Copy 120 bytes per layer, total 480.
- Custom Macros: GET report 1, lengths 168 through 179. Copy 168 bytes per slot, total 2,016.
- Quick Macros: GET report 1, lengths 180 through 191. Copy 48 bytes per slot, total 576.
- Extension on detected version 1.5 or later in family 0: lengths 192 through 203, 28 bytes per slot, total 336.
- Dual-action region: GET report 1, length 150, copy 128 bytes. This app backs it up and checks that it remains unchanged; it does not write it.
- Action: SET report 1, 8-byte buffer `01 91 00 00 00 00 00 00`.

Ready/Action are RAM/device-state commands, so “reading” is not literally a session with zero SET transfers. They are not configuration or firmware uploads.

Layer storage is `layer * 120 + row * 8 + column`. The app reads every cell, not only the 23 visible ones, and preserves unused cells when editing.

## Macro format

Custom slots consist of 84 two-byte events. Each event has an internal key index and a flag/delay byte. The low seven flag bits encode delay in 100 ms units. The original editor writes bit 7 for a press. Firmware execution toggles a key's held state on successive occurrences, which is what the decoder uses.

Quick slots use one key-index byte per event, also toggling held state. `00` and `FF` terminate a sequence. The optional 28-byte extension is concatenated after that slot's 48 base bytes. Macro encoding validates balanced presses/releases and capacity; unchanged raw bytes are not normalized merely by reading them.

Modifiers use ps2avrGB indices 103 through 110, not USB HID usage values E0 through E7. Custom Macro key assignments are 189 through 200; Quick Macro assignments are 201 through 212. These distinctions are covered by the codec tests.

## Uploading configuration flash pages

The original Bootmapper client invokes a bootloader transition before sending configuration HEX data. The static executable exposes the corresponding feature transfers:

1. SET normal report 1, 8 bytes: `01 00 FF 00 00 00 00 00`.
2. Wait for `16C0:05DF`, product `HIDBoot`, manufacturer `obdev.at`.
3. GET boot report 1 with a 132-byte buffer. Check returned report ID 1, little-endian page size 128 and little-endian flash size 32,768. Any mismatch stops before page writes.
4. For each changed configuration page, SET a 132-byte report: report ID `02`, three-byte little-endian address, then 128 data bytes.
5. Exit with a 7-byte SET report: `01 01 00 00 00 00 00`; wait for the normal interface to reappear.
6. Read the whole configuration back and compare it to the intended bytes.

The writable address allowlist is fixed in C#: aligned pages from `6600` up to, but not including, `7000`. The engine plans keymap bytes at `6600` with length 480 and custom macro bytes at `6800` with length 2,016. Neither a caller-supplied address nor an arbitrary firmware image is accepted by the upload API.

A changed page merges all unchanged bytes sharing it. The final partial page of either region has 32 padding bytes not returned by the normal configuration API. They are filled with `FF`, matching the original uploader's initial flash-buffer fill. This is documented behavior, not a claim that unexposed padding was backed up.

A transition request can return a USB error when the device disconnects to change mode. Only those two mode-transition errors may be followed by observation of re-enumeration. The expected identity and geometry still must pass. Errors during page writes are not ignored or automatically retried.

## Uploading Quick Macros

GET report 2 with length 180, 182, 184, 186, 188 or 190 selects the starting EEPROM slot. An empty response is expected for this address-preparation operation. Each subsequent SET report 2 has 132 bytes: `02 B4`, 96 bytes for two slots and zero padding.

The firmware streams all bytes after the initial two-byte header until the end of the bank. Padding therefore overlaps the next slots. The implementation rewrites the complete 576-byte bank in ascending order so later packets restore the overlapped bytes. It never updates just one isolated pair and ignores the padding.

The extension follows the corresponding 192, 196, 200 preparation lengths, header `02 C0`, 112 data bytes per group and complete 336-byte-bank rewrite. Firmware bounds the last packet at the bank end. Ready/Action surround the operation. The complete resulting banks are read back and checked.

## Failure handling and remaining uncertainty

Uploads require a successful previous read and matching revision. A fresh pre-upload read must still match, and a persisted backup must verify before writing. A failed write or failed comparison invalidates the in-memory connected snapshot. There is no automatic speculative rollback, factory reset, firmware installation or silent switch to demo data.

Configuration writes are not atomic. Verification can detect a mismatch but cannot prevent power-loss interruption or prove compatibility before first physical use. Neither passing simulated tests nor matching USB identifiers establishes that every manufacturer fork is supported. The implementation preserves the user's factory firmware architecture, but is not a guarantee against configuration loss or device recovery work.
