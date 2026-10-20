# WLMOUSE firmware update procedure

Recovered from WL MOUSE HUB v1.0.47. Offsets are Windows feature-report offsets (report id at `[0]`).
Firmware files: `https://gm.wlmouse.gg/Config/fwfiles/<FWFolder>/<file>.hex` (names in `env-models.json`).
The version is the `_v…` part of the file name (`v01.00.02.10` → `1.0.2.10`).

## Devices

| Target | Normal mode | Bootloader mode | Files |
|---|---|---|---|
| Mouse (cable only) | `VIDWired:PIDWired` | `DeviceBLVID:DeviceBLPID` | `DeviceFWFile_00` (chip 0), `DeviceFWFile_01` (chip 1) |
| 8K receiver | `VIDWired:_8KDongle` | `Receiver4K8KBLVID:Receiver4K8KBLPID` | `Receiver4K8KFWFile_00` (chip 0), `Receiver4K8KFWFile_01` (chip 1) |

`ReceiverOneMcuMultiHex = 1` means both files go to one MCU: no `exitBL`/`enterBL` between them.

## Commands (64-byte channel)

| Command | Request | Reply |
|---|---|---|
| enter bootloader | `[3]=chip [4]=1 [7]=B0` | none, device re-enumerates with the BL PID |
| BL version | `[3]=chip [4]=6 [5]=B0 [6]=80` | `[1]` = A1/02, hex digits `[9..12]`; contains `B` in BL mode |
| erase | `[3]=chip [4]=8 [5]=B0 [6]=01` | `[1]` = A1/02 (poll) |
| program | packet below | echo of address `[8..11]` and data `[12..]`, status A1/02 |
| verify (read back) | `[3]=chip [4]=20 [5]=B0 [6]=83 [7]=20 [8..11]=address` | data `[12..]` (XOR 0x55) |
| exit bootloader | `[3]=chip [4]=1 [5]=B0 [6]=04 [7]=B0` | none |
| exit to normal (HUAN M, BEAST X V2 mouse) | `… [7]=01` | none |

### Program packet
Built from the Intel HEX image: data is laid out contiguously from the first address (`ext<<16 | addr`), with gaps padded
with zeros. It is split into 16-byte rows, and two rows go in one 32-byte packet:

```
[3]=chip [4]=size+5 [5]=B0 [6]=02 [7]=size [8..11]=address (BE) [12..12+size]=data XOR 0x55
```

Timing: after each packet wait `programDelay` ms; every 4 KB (128 packets) wait `programDelay4K` ms instead.
Chip 0: 1 / 1 / verify 1 ms. Chip 1: 4 / 5 / verify 5 ms. Each packet is retried up to 5 times with +0.5 ms per try.

## Sequence (mouse)
1. In normal mode: `enterBL(0)`, wait 0.5 s. The device comes back as the BL PID.
2. Poll BL version until it contains `B` (50 × 0.5 s).
3. Chip 0: erase → program all packets → verify every packet against the image.
4. If there is no chip 1 file: `exitBL(0)` (HUAN M / BEAST X V2: exit-to-normal), wait for the normal PID, read the
   version, then the hub sends the factory-defaults command (`[3]=02 [4]=02 [7]=C0 [8]=01`).
5. Chip 1 (if any): `enterBL(1)` unless one-MCU, poll BL version on chip 1 (re-send `enterBL(1)` every 4 polls),
   erase → program → verify, `exitBL(1)`, wait for the normal PID.

## Sequence (8K receiver)
Same as above with the receiver IDs. After chip 0, `exitBL(0)` only when the receiver is not one-MCU. BEAST X V2's
receiver uses a 512-byte channel for chip 1 (index 3); this app does not support that path.
