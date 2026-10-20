using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WLMouseHub.Protocol;

namespace WLMouseHub;

/// <summary>Macro panel: four slots, recording, insert menu, save/delete.</summary>
public partial class MainWindow
{
    private readonly List<MacroSlotVm> _macros = Enumerable.Range(1, WlmMouse.MacroSlots).Select(i => new MacroSlotVm(i)).ToList();
    private readonly ObservableCollection<MacroEvent> _events = new();
    private readonly Stopwatch _recordClock = new();
    private int _macroSlot = 1;

    private void InitMacroPage()
    {
        LstMacroEvents.ItemsSource = _events;
        LstMacroEvents.ItemTemplate = BuildEventTemplate();
        _events.CollectionChanged += (_, _) => TxtMacroHint.Visibility = _events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildMacroSlots();
    }

    /// <summary>Each step is a small pill, like the hub's macro list.</summary>
    private static DataTemplate BuildEventTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetResourceReference(Border.BackgroundProperty, "B.Pill");
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 3, 10, 3));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        text.SetValue(TextBlock.FontSizeProperty, 11.5);
        border.AppendChild(text);
        return new DataTemplate { VisualTree = border };
    }

    private void RebuildMacroSlots()
    {
        if (PanelMacroSlots == null) return;
        PanelMacroSlots.Children.Clear();
        foreach (var slot in _macros)
        {
            var content = new DockPanel { LastChildFill = true };
            var edit = new TextBlock
            {
                Text = "", FontFamily = (FontFamily)Application.Current.Resources["F.Icons"], FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                ToolTip = Loc.T("Rename"),
            };
            edit.MouseLeftButtonUp += (_, ev) => { ev.Handled = true; RenameMacro(slot); };
            DockPanel.SetDock(edit, Dock.Right);
            content.Children.Add(edit);
            content.Children.Add(new TextBlock
            {
                Text = slot.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = slot.IsEmpty ? 0.55 : 1,
            });
            var rb = new RadioButton
            {
                Style = (Style)FindResource("NavRadio"), Content = content, GroupName = "macro", Tag = slot,
                IsChecked = slot.Id == _macroSlot, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 4, 12, 4), Margin = new Thickness(0, 0, 0, 6),
            };
            rb.Checked += (_, _) => { _macroSlot = slot.Id; ShowMacro(slot); };
            PanelMacroSlots.Children.Add(rb);
        }
    }

    private async Task LoadMacrosAsync()
    {
        var m = _mouse;
        if (m == null) return;
        try
        {
            var loaded = await Task.Run(() => _macros.Select(s => (s.Id, Name: m.GetMacroName(s.Id), Data: m.GetMacroData(s.Id))).ToList());
            foreach (var (id, name, data) in loaded)
            {
                var slot = _macros[id - 1];
                slot.Events = MacroCodec.Decode(data);
                slot.Name = name;
            }
            RebuildMacroSlots();
            ShowMacro(_macros[_macroSlot - 1]);
            RebuildButtonCards();
        }
        catch (WlmException ex)
        {
            Status(ex.Message);
        }
    }

    private void ShowMacro(MacroSlotVm slot)
    {
        BtnRecord.IsChecked = false;
        _events.Clear();
        foreach (var ev in slot.Events) _events.Add(ev);
    }

    private async void RenameMacro(MacroSlotVm slot)
    {
        var dlg = new InputDialog(Loc.T("Rename"), Loc.T("NewName"), slot.DisplayName);
        if (dlg.ShowDialog() != true || dlg.Value.Length == 0) return;
        string name = dlg.Value;
        if (!slot.IsEmpty && !await Run(Loc.T("Saved"), m => m.SetMacroName(slot.Id, name))) return;
        slot.Name = name;
        RebuildMacroSlots();
        RebuildButtonCards();
    }

    private int DelayInput() => int.TryParse(TxtDelayMs.Text, out int ms) && ms >= 0 ? ms : 50;

    private void InsertEvent(MacroEvent ev)
    {
        int index = LstMacroEvents.SelectedIndex;
        if (index < 0 || index >= _events.Count - 1 || BtnRecord.IsChecked == true) _events.Add(ev);
        else _events.Insert(index + 1, ev);
        LstMacroEvents.SelectedItem = ev;
        LstMacroEvents.ScrollIntoView(ev);
    }

    private void Insert_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = BtnInsert, Placement = PlacementMode.Bottom };
        MenuItem Item(string header, Action action)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += (_, _) => action();
            return mi;
        }
        menu.Items.Add(Item($"{Loc.T("Delay")} ({DelayInput()} ms)", () => InsertEvent(new MacroEvent(MacroEventKind.Delay, DelayInput()))));
        foreach (var (name, bit) in MacroEvent.MouseButtons)
        {
            var sub = new MenuItem { Header = $"{Loc.T("Mouse")}: {name}" };
            sub.Items.Add(Item(Loc.T("Click"), () =>
            {
                InsertEvent(new MacroEvent(MacroEventKind.MouseDown, bit));
                InsertEvent(new MacroEvent(MacroEventKind.Delay, DelayInput()));
                InsertEvent(new MacroEvent(MacroEventKind.MouseUp, bit));
            }));
            sub.Items.Add(Item(Loc.T("MouseDown"), () => InsertEvent(new MacroEvent(MacroEventKind.MouseDown, bit))));
            sub.Items.Add(Item(Loc.T("MouseUp"), () => InsertEvent(new MacroEvent(MacroEventKind.MouseUp, bit))));
            menu.Items.Add(sub);
        }
        menu.Items.Add(Item(Loc.T("WheelUp"), () => InsertEvent(new MacroEvent(MacroEventKind.WheelUp, 0))));
        menu.Items.Add(Item(Loc.T("WheelDown"), () => InsertEvent(new MacroEvent(MacroEventKind.WheelDown, 0))));
        menu.IsOpen = true;
    }

    private void MoveEventUp_Click(object sender, RoutedEventArgs e)
    {
        int i = LstMacroEvents.SelectedIndex;
        if (i > 0) { _events.Move(i, i - 1); LstMacroEvents.SelectedIndex = i - 1; }
    }

    private void MoveEventDown_Click(object sender, RoutedEventArgs e)
    {
        int i = LstMacroEvents.SelectedIndex;
        if (i >= 0 && i < _events.Count - 1) { _events.Move(i, i + 1); LstMacroEvents.SelectedIndex = i + 1; }
    }

    private void RemoveEvent_Click(object sender, RoutedEventArgs e)
    {
        foreach (var ev in LstMacroEvents.SelectedItems.Cast<MacroEvent>().ToList()) _events.Remove(ev);
    }

    private void MacroEvents_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && BtnRecord.IsChecked != true) RemoveEvent_Click(sender, e);
    }

    private void Record_Checked(object sender, RoutedEventArgs e)
    {
        _recordClock.Reset();
        BtnRecord.Content = Loc.T("StopRecord");
        LstMacroEvents.Focus();
    }

    private void Record_Unchecked(object sender, RoutedEventArgs e)
    {
        BtnRecord.SetResourceReference(ContentProperty, "S.Record");
        _recordClock.Stop();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e) => RecordKey(e, down: true);
    private void Window_PreviewKeyUp(object sender, KeyEventArgs e) => RecordKey(e, down: false);

    private void RecordKey(KeyEventArgs e, bool down)
    {
        if (BtnRecord.IsChecked != true || e.IsRepeat) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        byte mod = HidKeys.ModifierBit(key);
        byte? usage = mod == 0 ? HidKeys.Usage(key) : null;
        if (mod == 0 && usage == null) return;

        if (_events.Count > 0)
        {
            int delay = RbRecordDelay.IsChecked == true ? (int)_recordClock.ElapsedMilliseconds
                : RbDefaultDelay.IsChecked == true ? DelayInput() : 0;
            if (delay > 0) _events.Add(new MacroEvent(MacroEventKind.Delay, delay));
        }
        _recordClock.Restart();
        _events.Add(mod != 0
            ? new MacroEvent(down ? MacroEventKind.ModifierDown : MacroEventKind.ModifierUp, mod)
            : new MacroEvent(down ? MacroEventKind.KeyDown : MacroEventKind.KeyUp, usage!.Value));
        LstMacroEvents.ScrollIntoView(_events[^1]);
    }

    private async void SaveMacro_Click(object sender, RoutedEventArgs e)
    {
        var slot = _macros[_macroSlot - 1];
        BtnRecord.IsChecked = false;
        var events = _events.ToList();
        if (events.Count == 0)
        {
            Status(Loc.T("MacroEmpty"));
            return;
        }
        byte[] data = MacroCodec.Encode(events);
        string name = slot.DisplayName;
        if (await Run(Loc.T("Saved"), m =>
            {
                m.SetMacroData(slot.Id, data);
                m.SetMacroName(slot.Id, name);
            }))
        {
            slot.Events = events;
            slot.Name = name;
            RebuildMacroSlots();
            RebuildButtonCards();
        }
    }

    private async void DeleteMacro_Click(object sender, RoutedEventArgs e)
    {
        var slot = _macros[_macroSlot - 1];
        if (slot.IsEmpty)
        {
            _events.Clear();
            return;
        }
        if (!ThemedDialog.Confirm(Loc.T("DeleteMacro"), Loc.F("ConfirmDeleteMacro", slot.DisplayName))) return;

        var users = _buttons.Where(b => b.Action.Type is ActionType.MacroTimes or ActionType.MacroHold or ActionType.MacroToggle
                                        && b.Action.Data.Length >= 2 && ((b.Action.Data[0] << 8) | b.Action.Data[1]) == slot.Id)
                            .Select(b => (b, ButtonVm.DefaultFor(b.Id))).ToList();
        if (await Run(Loc.T("Saved"), m =>
            {
                m.DeleteMacro(slot.Id);
                foreach (var (b, a) in users) m.SetButton(_profile, b.Id, a);
            }))
        {
            foreach (var (b, a) in users) b.Action = a;
            slot.Events = new List<MacroEvent>();
            slot.Name = "";
            RebuildMacroSlots();
            ShowMacro(slot);
            RebuildButtonCards();
        }
    }
}
