# Keydial Mini (K20) Bluetooth LE protocol

What Open Keydial relies on, as observed on a Huion Keydial Mini K20 (firmware model string
`HUION_T21h_230628`) paired with Windows 11. Contributions for other firmware versions or devices
are welcome.

## Device

| | |
|---|---|
| Bluetooth LE HID device | Huion VID `256C`, PID `8251` |
| Proprietary GATT service | `0xFFE0` |

In its default ("factory") mode the Keydial behaves as a plain Bluetooth keyboard: every key sends a fixed
letter or shortcut (T2 = `G`, T3 = `L`...). After the driver-mode command below, it stops sending keyboard
input and reports raw button and dial events on the `FFE0` service instead.

## Characteristics

### `FFE1` — data (notify)

Frames start with `55 54`, then a type byte. The last byte is a checksum: the sum of bytes 2 .. n-2, modulo 256.

| Type | Meaning | Payload |
|---|---|---|
| `E0` | button state | bytes 5..7 = 24-bit little-endian mask. Bit *i* = key T(*i*+1) for *i* = 0..17, bit 18 = the dial button. A frame is sent on every press and release. |
| `F1` | dial step | byte 6 = `01` one direction, `02` the other. One frame per detent. |

### `FFE2` — commands (write without response + notify)

Commands are 8 bytes: `CD <cmd> 00 00 00 00 00 00`. Replies arrive as notifications on the same
characteristic: `<length> <cmd> <data...>`.

| Command | Effect | Reply |
|---|---|---|
| `CD C9 ...` | ask for the model name | ASCII model string, e.g. `HUION_T21h_230628` |
| `CD C8 ...` | switch to driver mode (the Bluetooth twin of USB string descriptor 200 on Huion tablets) | `... C8 01 ...` |

Without a command, a status notification `06 D1 xx <battery %> 00 00` arrives about every three minutes.

## Key layout

Seen from above, dial at the top:

```
 T1   T2   T3   T4
 T5   T6   T7   T8
 T9   T10  T11  T12
 T13  T14  T15  T16 (tall)
 T17 (wide)  T18
```

## Notes for implementers

- Driver mode is lost when the Keydial sleeps or reconnects: Open Keydial sends `C9` then `C8` (300 ms apart)
  each time it connects, and repeats `C8` every minute while connected (it has no effect when the mode is on).
- On Windows, writing the CCCD descriptor of a sleeping device blocks for about 8 seconds; Open Keydial
  only writes it when the device is known to be connected.
- The Windows API used is the Win32 `BluetoothGATT*` family on the service's device interface
  (`\\?\BTHLEDevice#{0000ffe0-...}...`), no WinRT.
