using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace PulseMouse.App.Controls;

/// <summary>Five-slot rhythm editor with short/long buttons and a conflict message.</summary>
public sealed class PatternComposer : Border
{
    public const int MaxPulses = 5;
    private readonly PulseSlot[] _slots = new PulseSlot[MaxPulses];
    private readonly Icon _msgIcon = new() { Size = 13, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _msgText = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private string _pattern = "";

    /// <summary>Returns the name of another binding using the pattern, or null when free.</summary>
    public Func<string, string?>? FindConflict { get; set; }
    public event Action<string>? PatternChanged;
    /// <summary>Raised when the user tries to add a sixth pulse.</summary>
    public event Action? LimitReached;

    public string Pattern
    {
        get => _pattern;
        set { _pattern = value ?? ""; Refresh(); }
    }

    public bool HasConflict => _pattern.Length > 0 && FindConflict?.Invoke(_pattern) is not null;

    public PatternComposer()
    {
        CornerRadius = new CornerRadius(12);
        BorderThickness = new Thickness(1);
        BorderBrush = Ui.Brush("Line2Brush");
        Background = Ui.Brush("Shade22Brush");
        Padding = new Thickness(10);

        var slots = new UniformGrid { Rows = 1, Margin = new Thickness(-3, 0, -3, 10) };
        for (int i = 0; i < MaxPulses; i++)
        {
            _slots[i] = new PulseSlot { Height = 40, Radius = 9, GlyphSize = GlyphSize.Medium, Shaded = false, Margin = new Thickness(3, 0, 3, 0) };
            slots.Children.Add(_slots[i]);
        }

        var buttons = new DockPanel { LastChildFill = false };
        buttons.Children.Add(PulseButton(".", "短按"));
        buttons.Children.Add(PulseButton("-", "长按"));
        var clear = new System.Windows.Controls.Button { Content = "清空", Style = (Style)FindResource("BtnSmGhost") };
        clear.Click += (_, _) => Set("");
        var back = new System.Windows.Controls.Button { Style = (Style)FindResource("BtnSmIconGhost"), ToolTip = "退格", Margin = new Thickness(0, 0, 6, 0) };
        Ui.SetIcon(back, IconKind.Back);
        Ui.SetIconSize(back, 15);
        back.Click += (_, _) => { if (_pattern.Length > 0) Set(_pattern[..^1]); };
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(back, Dock.Right);
        buttons.Children.Add(clear);
        buttons.Children.Add(back);

        var message = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        message.Children.Add(_msgIcon);
        message.Children.Add(_msgText);

        var root = new StackPanel();
        root.Children.Add(slots);
        root.Children.Add(buttons);
        root.Children.Add(message);
        Child = root;
        Refresh();
    }

    private System.Windows.Controls.Button PulseButton(string pulse, string label)
    {
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(new PatternGlyph { Pattern = pulse, Foreground = Ui.Brush("MintBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = new System.Windows.Controls.Button { Content = content, Style = (Style)FindResource("BtnSm"), Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(button, Dock.Left);
        button.Click += (_, _) =>
        {
            if (_pattern.Length >= MaxPulses) { LimitReached?.Invoke(); return; }
            Set(_pattern + pulse);
        };
        return button;
    }

    private void Set(string pattern)
    {
        _pattern = pattern;
        Refresh();
        PatternChanged?.Invoke(_pattern);
    }

    public void Refresh()
    {
        string? conflict = _pattern.Length > 0 ? FindConflict?.Invoke(_pattern) : null;
        for (int i = 0; i < MaxPulses; i++)
        {
            bool filled = i < _pattern.Length;
            _slots[i].Pulse = filled ? _pattern[i].ToString() : "";
            _slots[i].State = !filled ? SlotState.Empty : conflict is not null ? SlotState.Conflict : SlotState.Filled;
        }
        var (brush, icon, text) = _pattern.Length == 0
            ? ("Text3Brush", IconKind.Info, "点击添加短按 · 或长按 —，最多 5 位")
            : conflict is not null
                ? ("CoralBrush", IconKind.Alert, $"该节奏已被「{conflict}」占用")
                : ("MintBrush", IconKind.Check, "节奏可用");
        _msgIcon.Kind = icon;
        _msgIcon.Foreground = _msgText.Foreground = Ui.Brush(brush);
        _msgText.Text = text;
    }
}
