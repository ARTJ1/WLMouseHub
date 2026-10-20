using System;
using System.Collections.Generic;

namespace WLMouseHub.Protocol;

public enum MacroEventKind { KeyDown, KeyUp, ModifierDown, ModifierUp, MouseDown, MouseUp, WheelUp, WheelDown, Delay }

/// <summary>One macro step. Value is a HID usage, modifier bit, mouse button bit or delay in ms.</summary>
public sealed record MacroEvent(MacroEventKind Kind, int Value)
{
    public static readonly (string Name, byte Bit)[] MouseButtons =
        { ("Left", 1), ("Right", 2), ("Middle", 4), ("Back", 8), ("Forward", 16) };

    public override string ToString() => Kind switch
    {
        MacroEventKind.KeyDown => $"↓ {HidKeys.UsageName((byte)Value)}",
        MacroEventKind.KeyUp => $"↑ {HidKeys.UsageName((byte)Value)}",
        MacroEventKind.ModifierDown => $"↓ {HidKeys.ModifierName((byte)Value)}",
        MacroEventKind.ModifierUp => $"↑ {HidKeys.ModifierName((byte)Value)}",
        MacroEventKind.MouseDown => $"↓ Mouse {MouseName(Value)}",
        MacroEventKind.MouseUp => $"↑ Mouse {MouseName(Value)}",
        MacroEventKind.WheelUp => "Wheel up",
        MacroEventKind.WheelDown => "Wheel down",
        MacroEventKind.Delay => $"Wait {Value} ms",
        _ => Kind.ToString(),
    };

    private static string MouseName(int bit)
    {
        foreach (var (name, b) in MouseButtons) if (b == bit) return name;
        return bit.ToString();
    }
}

/// <summary>Encodes macros to and from the device's byte stream.</summary>
public static class MacroCodec
{
    public static byte[] Encode(IReadOnlyList<MacroEvent> events)
    {
        var bytes = new List<byte>();
        int mouseState = 0;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case MacroEventKind.KeyDown: bytes.Add(0x02); bytes.Add((byte)e.Value); break;
                case MacroEventKind.KeyUp: bytes.Add(0x03); bytes.Add((byte)e.Value); break;
                case MacroEventKind.ModifierDown: bytes.Add(0x09); bytes.Add((byte)e.Value); break;
                case MacroEventKind.ModifierUp: bytes.Add(0x0A); bytes.Add((byte)e.Value); break;
                case MacroEventKind.MouseDown: mouseState |= e.Value; bytes.Add(0x01); bytes.Add((byte)mouseState); break;
                case MacroEventKind.MouseUp: mouseState &= ~e.Value; bytes.Add(0x01); bytes.Add((byte)mouseState); break;
                case MacroEventKind.WheelUp: bytes.Add(0x10); bytes.Add(0x01); break;
                case MacroEventKind.WheelDown: bytes.Add(0x10); bytes.Add(0xFF); break;
                case MacroEventKind.Delay:
                    uint ms = (uint)Math.Max(0, e.Value);
                    if (ms <= 0xFF) { bytes.Add(0x20); bytes.Add((byte)ms); }
                    else if (ms <= 0xFFFF) { bytes.Add(0x21); bytes.Add((byte)(ms >> 8)); bytes.Add((byte)ms); }
                    else if (ms <= 0xFFFFFF) { bytes.Add(0x22); bytes.Add((byte)(ms >> 16)); bytes.Add((byte)(ms >> 8)); bytes.Add((byte)ms); }
                    else { bytes.Add(0x23); bytes.Add((byte)(ms >> 24)); bytes.Add((byte)(ms >> 16)); bytes.Add((byte)(ms >> 8)); bytes.Add((byte)ms); }
                    break;
            }
        }
        return bytes.ToArray();
    }

    public static List<MacroEvent> Decode(byte[] data)
    {
        var events = new List<MacroEvent>();
        int mouseState = 0;
        int i = 0;
        while (i + 1 < data.Length)
        {
            byte op = data[i];
            switch (op)
            {
                case 0x02: events.Add(new(MacroEventKind.KeyDown, data[i + 1])); i += 2; break;
                case 0x03: events.Add(new(MacroEventKind.KeyUp, data[i + 1])); i += 2; break;
                case 0x09: events.Add(new(MacroEventKind.ModifierDown, data[i + 1])); i += 2; break;
                case 0x0A: events.Add(new(MacroEventKind.ModifierUp, data[i + 1])); i += 2; break;
                case 0x01:
                    int next = data[i + 1];
                    int pressed = next & ~mouseState, released = mouseState & ~next;
                    foreach (var (_, bit) in MacroEvent.MouseButtons)
                    {
                        if ((pressed & bit) != 0) events.Add(new(MacroEventKind.MouseDown, bit));
                        if ((released & bit) != 0) events.Add(new(MacroEventKind.MouseUp, bit));
                    }
                    mouseState = next; i += 2; break;
                case 0x10: events.Add(new(data[i + 1] == 1 ? MacroEventKind.WheelUp : MacroEventKind.WheelDown, 0)); i += 2; break;
                case >= 0x20 and <= 0x23:
                    int n = op - 0x1F;
                    if (i + n >= data.Length) return events;
                    int v = 0;
                    for (int k = 1; k <= n; k++) v = (v << 8) | data[i + k];
                    events.Add(new(MacroEventKind.Delay, v)); i += n + 1; break;
                default:
                    return events; // padding or unknown data: stop
            }
        }
        return events;
    }
}
