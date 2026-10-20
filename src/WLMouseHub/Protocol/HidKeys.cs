using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace WLMouseHub.Protocol;

/// <summary>HID keyboard usage ids (USB HID Usage Tables, page 0x07) and modifier bits.</summary>
public static class HidKeys
{
    public const byte LCtrl = 0x01, LShift = 0x02, LAlt = 0x04, LWin = 0x08;
    public const byte RCtrl = 0x10, RShift = 0x20, RAlt = 0x40, RWin = 0x80;

    private static readonly Dictionary<Key, byte> KeyToUsage = Build();
    private static readonly Dictionary<byte, string> UsageNames = KeyToUsage
        .GroupBy(kv => kv.Value)
        .ToDictionary(g => g.Key, g => Pretty(g.First().Key));

    private static Dictionary<Key, byte> Build()
    {
        var d = new Dictionary<Key, byte>();
        for (int i = 0; i < 26; i++) d[Key.A + i] = (byte)(0x04 + i);
        for (int i = 1; i <= 9; i++) d[Key.D1 + (i - 1)] = (byte)(0x1E + i - 1);
        d[Key.D0] = 0x27;
        d[Key.Enter] = 0x28; d[Key.Escape] = 0x29; d[Key.Back] = 0x2A; d[Key.Tab] = 0x2B; d[Key.Space] = 0x2C;
        d[Key.OemMinus] = 0x2D; d[Key.OemPlus] = 0x2E; d[Key.OemOpenBrackets] = 0x2F; d[Key.OemCloseBrackets] = 0x30;
        d[Key.OemPipe] = 0x31; d[Key.OemSemicolon] = 0x33; d[Key.OemQuotes] = 0x34; d[Key.OemTilde] = 0x35;
        d[Key.OemComma] = 0x36; d[Key.OemPeriod] = 0x37; d[Key.OemQuestion] = 0x38; d[Key.CapsLock] = 0x39;
        for (int i = 0; i < 12; i++) d[Key.F1 + i] = (byte)(0x3A + i);
        d[Key.PrintScreen] = 0x46; d[Key.Scroll] = 0x47; d[Key.Pause] = 0x48; d[Key.Insert] = 0x49;
        d[Key.Home] = 0x4A; d[Key.PageUp] = 0x4B; d[Key.Delete] = 0x4C; d[Key.End] = 0x4D; d[Key.PageDown] = 0x4E;
        d[Key.Right] = 0x4F; d[Key.Left] = 0x50; d[Key.Down] = 0x51; d[Key.Up] = 0x52; d[Key.NumLock] = 0x53;
        d[Key.Divide] = 0x54; d[Key.Multiply] = 0x55; d[Key.Subtract] = 0x56; d[Key.Add] = 0x57;
        for (int i = 1; i <= 9; i++) d[Key.NumPad1 + (i - 1)] = (byte)(0x59 + i - 1);
        d[Key.NumPad0] = 0x62; d[Key.Decimal] = 0x63; d[Key.Apps] = 0x65;
        for (int i = 0; i < 12; i++) d[Key.F13 + i] = (byte)(0x68 + i);
        return d;
    }

    public static byte? Usage(Key key) => KeyToUsage.TryGetValue(key, out var u) ? u : null;

    public static byte ModifierBit(Key key) => key switch
    {
        Key.LeftCtrl => LCtrl, Key.LeftShift => LShift, Key.LeftAlt => LAlt, Key.LWin => LWin,
        Key.RightCtrl => RCtrl, Key.RightShift => RShift, Key.RightAlt => RAlt, Key.RWin => RWin,
        _ => 0,
    };

    public static string UsageName(byte usage) => UsageNames.TryGetValue(usage, out var n) ? n : $"Key 0x{usage:X2}";

    public static string ModifierName(byte bit) => bit switch
    {
        LCtrl => "LCtrl", LShift => "LShift", LAlt => "LAlt", LWin => "LWin",
        RCtrl => "RCtrl", RShift => "RShift", RAlt => "RAlt", RWin => "RWin",
        _ => $"Mod 0x{bit:X2}",
    };

    public static string Combo(byte modifiers, byte usage)
    {
        var parts = new List<string>();
        if ((modifiers & (LCtrl | RCtrl)) != 0) parts.Add("Ctrl");
        if ((modifiers & (LAlt | RAlt)) != 0) parts.Add("Alt");
        if ((modifiers & (LShift | RShift)) != 0) parts.Add("Shift");
        if ((modifiers & (LWin | RWin)) != 0) parts.Add("Win");
        if (usage != 0) parts.Add(UsageName(usage));
        return string.Join(" + ", parts);
    }

    private static string Pretty(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(k - Key.NumPad0),
        Key.Back => "Backspace", Key.Escape => "Esc", Key.OemMinus => "-", Key.OemPlus => "=",
        Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemPipe => "\\", Key.OemSemicolon => ";",
        Key.OemQuotes => "'", Key.OemTilde => "`", Key.OemComma => ",", Key.OemPeriod => ".", Key.OemQuestion => "/",
        Key.Scroll => "ScrollLock", Key.Divide => "Num /", Key.Multiply => "Num *", Key.Subtract => "Num -",
        Key.Add => "Num +", Key.Decimal => "Num .", Key.Apps => "Menu",
        _ => k.ToString(),
    };
}
