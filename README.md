<p align="center"><img src="docs/icon.png" width="96" alt="WLMouse Hub icon"></p>

# WLMouse Hub

> Unofficial. Not affiliated with or endorsed by WLMOUSE. Use at your own risk, especially firmware updates.

**Download:** grab `WLMouseHub-win-x64.exe` from the [latest release](../../releases/latest). It is a single file that
runs on Windows 10/11 x64 without installing anything.

A native Windows app for WLMOUSE mice. It looks and works like the official WL MOUSE HUB (gm.wlmouse.gg),
but it runs on its own and talks to the mouse directly over HID: no browser, no driver, no admin rights.

* **All models** in the hub's database: HUAN, HUAN M, BEAST MIAO, STRIDER, YING, SWORD X, BEAST MINI, BEAST MINI PRO,
  BEAST MAX, BEAST X, BEAST X PRO, BEAST G, BEAST X V2. Wired or through the receiver. On start the app refreshes the
  model list from the hub, so new models appear without a rebuild. A built-in copy is used when offline.
* **Dark and light themes**, the same colours as the hub.
* **Languages:** English, Русский, 中文 (EN / RU / 中文 button in the header).
* Model pictures and the logo come from the hub on first use and are cached in `%LocalAppData%\WLMouseHub\cache`.

## What it does

| Page | Contents |
|---|---|
| Main | Mouse picture, DPI, polling rate, battery, LOD, firmware versions; app options (start with Windows, keep in tray) |
| Setting | Buttons 1–5 around the mouse picture with their actions; info card with Reset Profile and Factory Reset; profile tabs with rename |
| ↳ Performance | Polling rate, LOD, debounce (0–15 ms), angle tune (±30°), sleep time, High-Speed, Turbo, Motion Sync, Button Combination, Ripple Control, Angle Snap |
| ↳ DPI | 1–6 stages with sliders, separate X/Y, per-stage colours, active stage |
| ↳ Macro | 4 slots: record keys (recorded, default or no delays), insert clicks / wheel / delays, rename, save, delete |
| ↳ Wheel Lighting | Effect, brightness, speed, colour picker, preset and recent colours |
| ↳ Dongle Screen (HUAN, HUAN M) | The receiver display: WLmouse GIF library, your own pictures (PNG/JPG → 360×360, or 224×224 GIF), Main + 5 Random slots, STATUS / SCREEN EFFECT / VOICE, Update, Delete |
| Firmware | Installed and latest versions of the receiver and the mouse, release notes, one-click update |

### Firmware updates
The app uses the same firmware files as the hub (`gm.wlmouse.gg/Config/fwfiles/…`) and the same procedure
(bootloader → erase → program → verify → restart). The packets it sends were checked to be byte-identical
to the hub's loader for all 40 firmware files of all 13 models (`--fwcheck`). See [docs/FIRMWARE.md](docs/FIRMWARE.md).

* Mouse: connect it by cable. Updating resets the mouse settings to defaults (the hub does the same).
* Receiver: plug the receiver in (on its own or with a Type-C cable, as the hub's instructions say).
* Keep the device plugged in and the app open until it says the update is complete.
* Not supported here, use the official hub: BEAST X V2 receiver (needs a different 512-byte channel) and 1K receivers.

Not included: receiver pairing, and the BEAST X V2 receiver screen (it uses a different 512-byte channel).

## Build and run

Needs the .NET 9 SDK (or newer).

```bash
dotnet build src/WLMouseHub/WLMouseHub.csproj -c Release
```

The app is `src/WLMouseHub/bin/Release/net9.0-windows/WLMouseHub.exe`.

Options: `--lang en|ru|zh`, `--theme dark|light`, `--minimized` (start in the tray).

## Diagnostics

* `WLMouseHub.exe --dump out.txt` – everything the app reads from the mouse (read commands only).
* `WLMouseHub.exe --roundtrip 3 out.txt` – writes every setting of profile 3 back unchanged and checks it, plus a
  test macro in an empty slot that is deleted again.
* `WLMouseHub.exe --screen out.txt` – receiver screen switches and stored pictures (read only, pictures saved next to the report).
* `WLMouseHub.exe --screen-roundtrip out.txt` – writes the stored screen pictures back unchanged and verifies them.
* `WLMouseHub.exe --fwcheck out.txt` – downloads and parses every firmware file and prints packet fingerprints.
* `tools/wlm-read.ps1` – the original read-only PowerShell probe.

## Layout

* `src/WLMouseHub/Hid` – Windows HID access (SetupAPI, hid.dll)
* `src/WLMouseHub/Protocol` – command protocol, device models, button actions, macros, firmware image and updater
* `src/WLMouseHub/Themes` – dark/light palettes and control styles
* `src/WLMouseHub/MainWindow*.cs` – pages: core/home, settings, macro, lighting, firmware
* `src/WLMouseHub/Loc.cs` – EN / RU / ZH strings
* `docs/PROTOCOL.md`, `docs/FIRMWARE.md` – protocol references
