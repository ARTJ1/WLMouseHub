using System;
using System.Collections.Generic;
using System.Linq;

namespace WLMouseHub.Protocol;

public enum ActionType : byte
{
    Off = 0, MouseButton = 1, MultiClick = 2, Keyboard = 4, Consumer = 5, Dpi = 7, Profile = 8,
    PollingRate = 13, Lod = 14, MacroTimes = 16, MacroHold = 17, MacroToggle = 18,
}

public enum ActionGroup { Mouse, Dpi, Media, System, Other }

/// <summary>What a physical button does: an action type plus its data bytes.</summary>
public sealed record ButtonAction(ActionType Type, byte[] Data)
{
    public static ButtonAction Mouse(byte code) => new(ActionType.MouseButton, new[] { code });
    public static ButtonAction Consumer(ushort usage) => new(ActionType.Consumer, new[] { (byte)(usage >> 8), (byte)usage });
    public static ButtonAction Key(byte modifiers, byte usage) => new(ActionType.Keyboard, new[] { modifiers, usage });
    public static ButtonAction DpiLock(int dpi) =>
        new(ActionType.Dpi, new byte[] { 5, (byte)(dpi >> 8), (byte)dpi, (byte)(dpi >> 8), (byte)dpi });
    public static ButtonAction Macro(ActionType mode, int id, int repeat) => mode == ActionType.MacroTimes
        ? new(mode, new byte[] { (byte)(id >> 8), (byte)id, (byte)Math.Clamp(repeat, 1, 255) })
        : new(mode, new byte[] { (byte)(id >> 8), (byte)id });

    /// <summary>Fixed actions offered in the button menu: translation key, group, action.</summary>
    public static IReadOnlyList<(string Key, ActionGroup Group, ButtonAction Action)> Presets { get; } =
        new List<(string, ActionGroup, ButtonAction)>
        {
            ("LeftClick", ActionGroup.Mouse, Mouse(1)),
            ("RightClick", ActionGroup.Mouse, Mouse(2)),
            ("WheelClick", ActionGroup.Mouse, Mouse(3)),
            ("Backward", ActionGroup.Mouse, Mouse(4)),
            ("Forward", ActionGroup.Mouse, Mouse(5)),
            ("ScrollUp", ActionGroup.Mouse, Mouse(16)),
            ("ScrollDown", ActionGroup.Mouse, Mouse(17)),
            ("DoubleClick", ActionGroup.Mouse, new(ActionType.MultiClick, new byte[] { 1, 1, 2, 0, 100 })),
            ("DpiCycle", ActionGroup.Dpi, new(ActionType.Dpi, new byte[] { 6 })),
            ("DpiUp", ActionGroup.Dpi, new(ActionType.Dpi, new byte[] { 1 })),
            ("DpiDown", ActionGroup.Dpi, new(ActionType.Dpi, new byte[] { 2 })),
            ("ProfileCycle", ActionGroup.Dpi, new(ActionType.Profile, new byte[] { 3 })),
            ("PollingCycle", ActionGroup.Dpi, new(ActionType.PollingRate, new byte[] { 3 })),
            ("LodCycle", ActionGroup.Dpi, new(ActionType.Lod, new byte[] { 3 })),
            ("PlayPause", ActionGroup.Media, Consumer(0xCD)),
            ("NextTrack", ActionGroup.Media, Consumer(0xB5)),
            ("PrevTrack", ActionGroup.Media, Consumer(0xB6)),
            ("Stop", ActionGroup.Media, Consumer(0xB7)),
            ("VolUp", ActionGroup.Media, Consumer(0xE9)),
            ("VolDown", ActionGroup.Media, Consumer(0xEA)),
            ("Mute", ActionGroup.Media, Consumer(0xE2)),
            ("MediaPlayer", ActionGroup.Media, Consumer(0x183)),
            ("Calculator", ActionGroup.System, Consumer(0x192)),
            ("MyComputer", ActionGroup.System, Consumer(0x194)),
            ("Browser", ActionGroup.System, Consumer(0x196)),
            ("Email", ActionGroup.System, Consumer(0x18A)),
            ("WebHome", ActionGroup.System, Consumer(0x223)),
            ("WebRefresh", ActionGroup.System, Consumer(0x227)),
            ("Disabled", ActionGroup.Other, new(ActionType.Off, Array.Empty<byte>())),
        };

    public bool SameAs(ButtonAction other) => Type == other.Type && Data.SequenceEqual(other.Data);

    /// <summary>Human-readable name; <paramref name="translate"/> maps preset keys to text.</summary>
    public string Describe(Func<string, string>? translate = null, Func<int, string>? macroName = null)
    {
        translate ??= k => k;
        foreach (var (key, _, action) in Presets)
            if (SameAs(action)) return translate(key);

        int U16(int i) => Data.Length > i + 1 ? (Data[i] << 8) | Data[i + 1] : 0;
        string Macro() => macroName?.Invoke(U16(0)) ?? "Macro " + U16(0);
        return Type switch
        {
            ActionType.Off => translate("Disabled"),
            ActionType.MouseButton => $"Button {Data.FirstOrDefault()}",
            ActionType.MultiClick => $"{Data.ElementAtOrDefault(2)}× click {Data.FirstOrDefault()}",
            ActionType.Keyboard => HidKeys.Combo(Data.ElementAtOrDefault(0), Data.ElementAtOrDefault(1)),
            ActionType.Consumer => $"Media 0x{U16(0):X3}",
            ActionType.Dpi when Data.FirstOrDefault() == 5 => $"{translate("DpiLock")} {U16(1)}",
            ActionType.Dpi => "DPI",
            ActionType.MacroTimes => $"{Macro()} ×{Data.ElementAtOrDefault(2)}",
            ActionType.MacroHold => $"{Macro()} ⟳",
            ActionType.MacroToggle => $"{Macro()} ⇄",
            _ => $"Type {(byte)Type}",
        };
    }
}
