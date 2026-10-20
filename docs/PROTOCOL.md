# WLMOUSE configuration protocol

Recovered from WL MOUSE HUB (gm.wlmouse.gg, driver v1.0.47) and verified against a HUAN
(VID `36A7`, wired PID `A864`, firmware 0.1.0.11).

## Transport

* Interface: the HID collection with usage page `0xFFFF` and a 64-byte feature report
  (`MI_02` on the HUAN). Windows exposes it as a 65-byte buffer: byte 0 is the report id (0).
* One transaction = `HidD_SetFeature(request)` then `HidD_GetFeature` until the reply is ready.
* Offsets below are Windows buffer offsets (report id at `[0]`).

| Offset | Request                         | Reply                                        |
|--------|---------------------------------|----------------------------------------------|
| 0      | report id `00`                  | `00`                                         |
| 1      | `00`                            | status: `A1` OK, `< A1` busy (poll), `> A1` error (resend) |
| 2      | `00`                            | `00`                                         |
| 3      | target: `02` mouse, `00` receiver, `01` receiver info | echo                   |
| 4      | payload length field            | echo                                         |
| 5      | command class                   | echo                                         |
| 6      | command (`| 0x80` = read)       | echo                                         |
| 7..    | payload, profile index first    | payload (profile echoed at `[7]` for profile commands) |

## Commands

`p` = profile (1..3). Values are big-endian.

| Feature | Class | Get / Set | Request payload | Reply |
|---|---|---|---|---|
| Firmware version | 0 | 81 / – | – (len 16) | `[7..10]` a.b.c.d |
| Serial number | 0 | 82 / – | – (len 20) | ASCII `[9..23]` |
| Battery | 0 | 83 / – | – | `[7]` charging, `[8]` percent |
| Active profile | 0 | 85 / 05 | set: `p` | `[7]` |
| Profile count | 0 | 86 / – | – | `[7]` |
| Profile name (GBK) | 0 | 8E / 0E | get: `p` (len 58); set: `p, n, bytes` (len n+2) | `[8]` n, `[9..]` bytes |
| Reset profile | 0 | – / 0D | `p` | |
| Factory reset | 0 | – / 00 | `C0 01` (len 2), no reply | |
| Debounce (ms) | 0 | 88 / 08 | `p[, ms]` | `[8]` |
| Sleep timer (s) | 0 | 87 / 07 | `p[, s16]` (len 3); `FFFF` = never | `[8..9]` |
| Polling rate | 1 | 80 / 00 | `p[, code]` | `[8]`: 8=125 4=250 2=500 1=1000 (16=1000) 32=2000 64=4000 128=8000 |
| DPI stages | 1 | 81 / 01 | get: `p, 6` (len 10); set: `p, n, n×(x16,y16)` (len 26) | `[8]` n, then `x16 y16` from `[9]` |
| Active DPI stage | 1 | 82 / 02 | `p[, stage]` | `[8]` (1-based) |
| Max DPI | 1 | 8C / – | – | `[7..8]` |
| Lift-off distance | 1 | 88 / 08 | `p[, lod, mode]` (len 3); lod: 1/2 = mm, `0x80|tenths` below 1 mm; mode 3 | `[8]` lod, `[9]` mode |
| Angle snapping | 1 | 84 / 04 | `p[, 0/1]` | `[8]` |
| Motion sync | 1 | 89 / 09 | | |
| Ripple control | 1 | 8A / 0A | | |
| High-speed mode | 1 | 8B / 0B | | |
| Separate X/Y DPI | 1 | 8D / 0D | | |
| Tracking mode | 1 | 93 / 13 | only while high-speed is on | |
| Angle tune (°) | 1 | 94 / 14 | signed byte, −30..30 | |
| DPI stage colours | 2 | 81 / 01 | `p[, 6×RGB]` (len 19) | RGB from `[8]` |
| DPI indicator | 2 | 84 / 04 | `p[, 0/1]` | `[8]` |
| Lighting effect | 2 | 80 / 00 | target = light device; get: `01` (len 26); set: `01, 00, effect, p2, 7−speed, R, G, B` (len 8) | `[9]` effect, `[10]` p2, `[11]` raw speed, RGB `[12..14]` |
| Brightness | 2 | 82 / 02 | `01, wired?1:0[, 0..255]` (len 3) | `[9]` |
| Button combo | 3 | 81 / 01 | `p[, 0/1]` | `[8]` |
| Button mapping | 3 | 80 / 00 | `p, button, 0, type, n, data` (len 5+n; get uses type FF, n 10) | `[10]` type, `[11]` n, data from `[12]` |
| Macro allocate | 4 | – / 01 | `id16, size32` (len 6) | |
| Macro data | 4 | 83 / 03 | `id16, offset32, n[, data]` (len 7+n, ≤51 per write) | data from `[14]` |
| Macro size | 4 | 81 / – | `id16` (len 6) | size32 at `[9..12]` |
| Macro delete | 4 | – / 02 | `id16` | |
| Macro name (GBK) | 4 | 86 / 06 | get: `id, 56` (len 58); set: `id, n, bytes` | `[8]` n, `[9..]` |
| Device PID (via receiver) | 0, target 1 | 8B / – | `02` (len 6) | PID at `[11..12]` |

Lighting effects: 1 spectrum, 2 wave, 4 static, 5 breathing. Light target: `02` when wired, otherwise the
model's `LightDeviceID`.

### Button ids
1 left, 2 right, 3 middle, 4 back, 5 forward, 20 DPI button. Receiver buttons: 48 polling, 49 DPI, 50 LOD.

### Button action types
| Type | Meaning | Data |
|---|---|---|
| 0 | disabled | – |
| 1 | mouse button | code: 1 L, 2 R, 3 M, 4 back, 5 fwd, 16 wheel up, 17 wheel down |
| 2 | multi-click | `button, 1, clicks, 0, interval_ms` |
| 4 | keyboard | `modifiers, HID usage` (mods: 1 LCtrl 2 LShift 4 LAlt 8 LWin, ×16 for right) |
| 5 | consumer key | usage16 (e.g. E9 vol+, EA vol−, E2 mute, CD play/pause, B5 next, B6 prev) |
| 7 | DPI | 1 up, 2 down, 6 cycle, `5, x16, y16` lock |
| 8 | profile cycle | 3 |
| 13 | polling-rate cycle | 3 |
| 14 | LOD cycle | 3 |
| 16 | macro, run N times | `id16, N` |
| 17 | macro, repeat while held | `id16` |
| 18 | macro, toggle repeat | `id16` |

### Macro event stream
| Byte | Event | Args |
|---|---|---|
| 01 | mouse buttons | new button bitmask (1 L, 2 R, 4 M, 8 back, 16 fwd) |
| 02 / 03 | key down / up | HID usage |
| 09 / 0A | modifier down / up | modifier bit |
| 10 | wheel | 01 up, FF down |
| 20 / 21 / 22 / 23 | delay | 1 / 2 / 3 / 4 byte milliseconds |

## Not implemented on purpose
Firmware update (bootloader commands), receiver pairing / RF address, and the receiver LCD image upload
(512-byte channel). A mistake in any of these can brick or unpair the device.
