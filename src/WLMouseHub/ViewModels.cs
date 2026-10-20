using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using WLMouseHub.Protocol;

namespace WLMouseHub;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DpiStageVm : Observable
{
    private string _x = "800", _y = "800";
    private bool _isActive;
    private Visibility _yVisibility = Visibility.Collapsed;
    private Color _color = Colors.White;

    public DpiStageVm(int index, int max, int step)
    {
        Index = index;
        Max = max;
        Step = step;
    }

    public int Index { get; }
    public int Max { get; }
    public int Step { get; }

    public string X { get => _x; set { _x = value; Raise(); Raise(nameof(XValue)); } }
    public string Y { get => _y; set { _y = value; Raise(); Raise(nameof(YValue)); } }

    public double XValue
    {
        get => double.TryParse(_x, out var v) ? v : 50;
        set { _x = Snap(value).ToString(); Raise(nameof(X)); Raise(); }
    }

    public double YValue
    {
        get => double.TryParse(_y, out var v) ? v : 50;
        set { _y = Snap(value).ToString(); Raise(nameof(Y)); Raise(); }
    }

    private int Snap(double v) => Math.Clamp((int)Math.Round(v / Step) * Step, 50, Max);

    public bool IsActive { get => _isActive; set { _isActive = value; Raise(); } }
    public Visibility YVisibility { get => _yVisibility; set { _yVisibility = value; Raise(); } }

    public Color Color
    {
        get => _color;
        set { _color = value; Raise(); Raise(nameof(Brush)); }
    }

    public Brush Brush => new SolidColorBrush(_color);
}

public sealed class ButtonVm
{
    public ButtonVm(byte id, ButtonAction action)
    {
        Id = id;
        Action = action;
    }

    public byte Id { get; }
    public ButtonAction Action { get; set; }

    /// <summary>The number printed on the hub's mouse picture (1 left, 2 wheel, 3 right, 4 forward, 5 back).</summary>
    public int Number => Id switch { 1 => 1, 3 => 2, 2 => 3, 5 => 4, 4 => 5, _ => Id };

    public static ButtonAction DefaultFor(byte id) => id switch
    {
        >= 1 and <= 5 => ButtonAction.Mouse(id),
        20 => new ButtonAction(ActionType.Dpi, new byte[] { 6 }),
        _ => new ButtonAction(ActionType.Off, Array.Empty<byte>()),
    };
}

public sealed class MacroSlotVm
{
    public MacroSlotVm(int id) => Id = id;

    public int Id { get; }
    public string Name { get; set; } = "";
    public List<MacroEvent> Events { get; set; } = new();
    public bool IsEmpty => Events.Count == 0;
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Loc.F("MacroN", Id) : Name;
}

public sealed record Choice<T>(string Name, T Value)
{
    public override string ToString() => Name;
}
