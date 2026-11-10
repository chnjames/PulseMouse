using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PulseMouse.App.Controls;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using KeyInterop = System.Windows.Input.KeyInterop;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using PulseMouse.App.Services;
using PulseMouse.Core;
using PulseMouse.Windows;

namespace PulseMouse.App.Views;

public partial class CommandsView : System.Windows.Controls.UserControl, IPageView
{
    private readonly MainWindow _shell;
    private readonly PulseController _c;
    private CommandBinding? _editing;
    private bool _syncing, _pendingNew, _pinManual;
    private string _draftScope = "";
    private string? _mapFocusPattern;
    private string? _mapFocusScope;

    public CommandsView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _c = shell.Controller;
        Composer.FindConflict = _ => null;
        Composer.PatternChanged += _ =>
        {
            PatternLength.Text = $"{Composer.Pattern.Length} / 5";
            if (!_syncing) RefreshPatternHint();
        };
        Composer.LimitReached += () => _shell.Toast("节奏最多 5 位", IconKind.Alert);
        _c.BindingsChanged += () => { if (IsLoaded) Rebuild(); };
        foreach (var (name, keys) in TargetField.ShortcutPresets)
        {
            var preset = new Button { Style = (Style)FindResource("BtnSmGhost"), Content = name, ToolTip = keys, Margin = new Thickness(0, 0, 6, 6) };
            preset.Click += (_, _) => ApplyPreset(name, keys);
            ShortcutPresets.Children.Add(preset);
        }
        Relocalize();
    }

    public void OnShown()
    {
        if (_pendingNew) { _pendingNew = false; Rebuild(); LoadDraft(null); NameBox.Focus(); return; }
        var target = _editing is not null && _c.Settings.Commands.Contains(_editing) ? _editing : _c.Settings.Commands.FirstOrDefault(c => c.Scope.Length == 0);
        _editing = target;
        Rebuild();
        LoadDraft(target);
    }

    public void OnHidden() { }
    public void Relocalize()
    {
        Header.Eyebrow = Loc.Eyebrow(Page.Commands);
        Header.Heading = Loc.Nav(Page.Commands);
        Header.Description = Loc.T("全局定义默认含义；每个程序可以把非固定的节奏改成自己的含义。",
            "Globals set the default meaning; each app can override unpinned rhythms.");
        if (IsLoaded) Rebuild();
    }
    public void Reload() { _editing = null; if (IsLoaded) OnShown(); }
    public void BeginNew() { _editing = null; _pendingNew = true; }

    private bool MapMode => ViewSeg.SelectedIndex == 0;
    private ActionKind DraftKind => TargetField.KindAt(KindSeg.SelectedIndex);
    private string DraftScope => ScopeSeg.SelectedIndex == 0 ? "" : ScopeAppBox.Text.Trim().ToLowerInvariant().Replace(".exe", "");

    private void Rebuild()
    {
        ApplyViewChrome();
        ContextOffBanner.Visibility = _c.Settings.ContextEnabled ? Visibility.Collapsed : Visibility.Visible;
        UpdateRecentBanner();
        if (MapMode) RebuildMap();
        else RebuildList();
        RefreshInspector();
    }

    private void ViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyViewChrome();
        Rebuild();
    }

    private void ApplyViewChrome()
    {
        bool map = MapMode;
        MapHost.Visibility = MapToolbar.Visibility = map ? Visibility.Visible : Visibility.Collapsed;
        ListHost.Visibility = ListToolbar.Visibility = map ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateRecentBanner()
    {
        string recent = _c.RecentProcess;
        bool show = recent.Length > 0
            && !_c.Settings.ContextApps.Any(a => a.Process.Equals(recent, StringComparison.OrdinalIgnoreCase));
        RecentBanner.Visibility = show && MapMode ? Visibility.Visible : Visibility.Collapsed;
        if (show) RecentBannerText.Text = Loc.T($"刚刚在 {recent} 中输入 · 添加为程序？", $"Just used in {recent} · add as app?");
    }

    // ---------- map ----------

    private void RebuildMap()
    {
        MapRoot.Children.Clear();
        var settings = _c.Settings;
        var apps = settings.ContextApps.ToList();
        var patterns = PatternOrder.All.Where(p => p.Length <= 2)
            .Concat(settings.Commands.Select(c => c.Pattern).Where(p => p.Length > 2))
            .Distinct()
            .OrderBy(PatternOrder.Rank)
            .ToList();

        var grid = new Grid { MinWidth = 160 + 160 + apps.Count * 160 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160), MinWidth = 150 });
        foreach (var _ in apps) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160), MinWidth = 150 });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < patterns.Count; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void Header(int col, string title, string? sub, string? process, bool recent)
        {
            var stack = new StackPanel { Margin = new Thickness(6, 0, 6, 10) };
            var top = new DockPanel();
            if (process is not null)
            {
                var remove = new Button { Style = (Style)FindResource("BtnSmGhost"), Content = "×", Padding = new Thickness(6, 0, 6, 0), ToolTip = "移除程序" };
                string proc = process;
                remove.Click += async (_, _) => await RemoveAppAsync(proc);
                DockPanel.SetDock(remove, Dock.Right);
                top.Children.Add(remove);
            }
            top.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12.5 });
            stack.Children.Add(top);
            if (sub is not null)
                stack.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Ui.Brush(recent ? "MintBrush" : "Text3Brush"), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(stack, col);
            grid.Children.Add(stack);
        }

        Header(0, "节奏", null, null, false);
        Header(1, "全局", "默认含义", null, false);
        for (int i = 0; i < apps.Count; i++)
        {
            bool recent = apps[i].Process.Equals(_c.RecentProcess, StringComparison.OrdinalIgnoreCase);
            Header(i + 2, apps[i].DisplayName.Length > 0 ? apps[i].DisplayName : apps[i].Process,
                recent ? "● 刚刚用过" : apps[i].Process + ".exe", apps[i].Process, recent);
        }

        for (int r = 0; r < patterns.Count; r++)
        {
            string pattern = patterns[r];
            var glyph = new Border
            {
                Style = (Style)FindResource("PatternChip"), Margin = new Thickness(0, 3, 6, 3),
                Child = new PatternGlyph { Pattern = pattern, GlyphSize = GlyphSize.Medium, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetRow(glyph, r + 1);
            grid.Children.Add(glyph);

            grid.Children.Add(MapCell(pattern, "", 1, r + 1));
            for (int i = 0; i < apps.Count; i++)
                grid.Children.Add(MapCell(pattern, apps[i].Process, i + 2, r + 1));
        }

        // Card style targets ContentControl, not Border — assigning it to Border crashes the page.
        MapRoot.Children.Add(new ContentControl
        {
            Style = (Style)FindResource("Card"),
            Padding = new Thickness(12, 14, 12, 10),
            Content = grid,
            Opacity = settings.ContextEnabled ? 1 : 0.85
        });
    }

    private UIElement MapCell(string pattern, string scope, int col, int row)
    {
        var commands = _c.Settings.Commands;
        var own = scope.Length == 0 ? commands.Global(pattern) : commands.Own(scope, pattern);
        var global = commands.Global(pattern);
        bool focused = pattern == _mapFocusPattern && scope == (_mapFocusScope ?? "");
        var border = new Border
        {
            Margin = new Thickness(4, 3, 4, 3), Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(10),
            Background = Ui.Brush("Shade16Brush"), BorderThickness = new Thickness(focused ? 1.5 : 1),
            BorderBrush = Ui.Brush(focused ? "MintBrush" : "LineBrush"), Cursor = System.Windows.Input.Cursors.Hand,
            Opacity = scope.Length > 0 && !_c.Settings.ContextEnabled ? 0.45 : 1
        };
        Grid.SetColumn(border, col);
        Grid.SetRow(border, row);

        var stack = new StackPanel();
        border.Child = stack;

        if (scope.Length == 0)
        {
            if (own is null)
            {
                stack.Children.Add(Muted("+ 空闲"));
                border.MouseLeftButtonUp += (s, e) => { e.Handled = true; NewAt(pattern, ""); };
            }
            else
            {
                stack.Children.Add(NameLine(own.Name, own.Pinned));
                stack.Children.Add(Sub(PulseController.TargetText(own)));
                stack.Children.Add(HeatChip(own));
                border.MouseLeftButtonUp += (s, e) => { e.Handled = true; Edit(own); };
            }
        }
        else if (own is not null)
        {
            bool replaces = global is not null;
            border.BorderBrush = Ui.Brush(replaces ? "AmberBrush" : "MintBrush");
            border.BorderThickness = new Thickness(replaces || focused ? 1.5 : 1);
            border.Padding = new Thickness(replaces ? 8 : 10, 8, 10, 8);
            if (replaces) border.BorderThickness = new Thickness(3, 1, 1, 1);
            stack.Children.Add(NameLine(own.Name, false));
            if (replaces) stack.Children.Add(Sub(Loc.T($"替换「{global!.Name}」", $"replaces “{global!.Name}”"), "AmberBrush"));
            else stack.Children.Add(Sub(PulseController.TargetText(own)));
            stack.Children.Add(HeatChip(own));
            border.MouseLeftButtonUp += (s, e) => { e.Handled = true; Edit(own); };
        }
        else if (global is { Pinned: true })
        {
            border.Cursor = System.Windows.Input.Cursors.No;
            stack.Children.Add(Muted(Loc.T($"固定 · {global.Name}", $"Pinned · {global.Name}")));
            border.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                _shell.Toast(Loc.T($"「{global.Name}」已固定，可在全局指令里取消固定", $"“{global.Name}” is pinned — unpin it in the global binding"), IconKind.Info);
            };
        }
        else if (global is not null)
        {
            stack.Children.Add(Muted(Loc.T($"沿用 · {global.Name}", $"Inherit · {global.Name}")));
            stack.Children.Add(Sub(Loc.T($"点击改成 {scope} 的含义", $"Click to override for {scope}")));
            border.MouseLeftButtonUp += (s, e) => { e.Handled = true; NewAt(pattern, scope, global); };
        }
        else
        {
            stack.Children.Add(Muted("—"));
            border.MouseLeftButtonUp += (s, e) => { e.Handled = true; NewAt(pattern, scope); };
        }

        return border;
    }

    private static TextBlock NameLine(string name, bool pinned) => new()
    {
        Text = pinned ? "🔒 " + name : name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private static TextBlock Sub(string text, string brush = "Text3Brush") => new()
    {
        Text = text, FontSize = 11, Foreground = Ui.Brush(brush), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
    };
    private static TextBlock Muted(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = Ui.Brush("Text3Brush"), FontStyle = FontStyles.Italic
    };
    private UIElement HeatChip(CommandBinding command)
    {
        int total = CommandHeat.Total(CommandHeat.Series(_c.Settings, command.Id, DateTime.Today));
        return new TextBlock
        {
            Text = total > 0 ? $"{total}× / 7d" : Loc.T("7 天未用", "Unused 7d"),
            FontSize = 10.5, Foreground = Ui.Brush("Text3Brush"), Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        };
    }

    private void Edit(CommandBinding command)
    {
        _editing = command;
        _mapFocusPattern = command.Pattern;
        _mapFocusScope = command.Scope;
        LoadDraft(command);
        if (MapMode) RebuildMap();
        else RebuildList();
    }

    private void NewAt(string pattern, string scope, CommandBinding? inherit = null)
    {
        _editing = null;
        _mapFocusPattern = pattern;
        _mapFocusScope = scope;
        _syncing = true;
        NameBox.Text = inherit?.Name ?? "";
        TargetBox.Text = inherit is { ActionKind: not ActionKind.Workflow and not ActionKind.Repeat } ? inherit.Target : "";
        KindSeg.SelectedIndex = (int)(inherit?.ActionKind ?? ActionKind.Shortcut);
        Composer.Pattern = pattern;
        PatternLength.Text = $"{pattern.Length} / 5";
        ScopeSeg.SelectedIndex = scope.Length == 0 ? 0 : 1;
        if (scope.Length > 0) ScopeAppBox.Text = scope;
        _pinManual = false;
        PinSwitch.IsChecked = false;
        _syncing = false;
        RefreshInspector();
        if (MapMode) RebuildMap();
        NameBox.Focus();
    }

    // ---------- list ----------

    private IEnumerable<CommandBinding> Filtered()
    {
        var kind = FilterSeg.SelectedIndex;
        var q = SearchBox.Text.Trim();
        var qPattern = q.Replace('·', '.').Replace('—', '-');
        return _c.Settings.Commands.Where(c =>
            (kind <= 0 || (int)c.ActionKind == kind - 1) &&
            (q.Length == 0
             || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
             || PulseController.TargetText(c).Contains(q, StringComparison.OrdinalIgnoreCase)
             || c.Pattern.Contains(qPattern, StringComparison.Ordinal)
             || c.Scope.Contains(q, StringComparison.OrdinalIgnoreCase)));
    }

    private void RebuildList()
    {
        _syncing = true;
        List.Items.Clear();
        var items = Filtered().OrderBy(c => c.Scope).ThenBy(c => PatternOrder.Rank(c.Pattern)).ToList();
        foreach (var command in items)
        {
            var item = new ListBoxItem { Content = ItemContent(command), Tag = command };
            List.Items.Add(item);
            if (ReferenceEquals(command, _editing)) item.IsSelected = true;
        }
        ListMeta.Text = Loc.T($"{items.Count} 条指令 · 名称不必唯一，匹配看节奏 + 作用范围",
            $"{items.Count} commands · names need not be unique");
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _syncing = false;
    }

    private UIElement ItemContent(CommandBinding command)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = new Border
        {
            Style = (Style)System.Windows.Application.Current.Resources["PatternChip"],
            Child = new PatternGlyph { Pattern = command.Pattern, GlyphSize = GlyphSize.Medium, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var texts = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        string title = command.Pinned && command.Scope.Length == 0 ? "🔒 " + command.Name : command.Name;
        texts.Children.Add(new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        string scopeLabel = command.Scope.Length == 0 ? Loc.T("全局", "Global") : command.Scope;
        var global = command.Scope.Length > 0 ? _c.Settings.Commands.Global(command.Pattern) : null;
        string note = global is not null ? Loc.T($"替换「{global.Name}」", $"replaces “{global.Name}”") : PulseController.TargetText(command);
        texts.Children.Add(new TextBlock
        {
            Text = $"{scopeLabel} · {note}", FontSize = 11.5, Foreground = Ui.Brush("Text3Brush"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0)
        });
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        int heat = CommandHeat.Total(CommandHeat.Series(_c.Settings, command.Id, DateTime.Today));
        if (heat > 0) right.Children.Add(new KindChip { Kind = ChipKind.Muted, Text = $"{heat}×", Margin = new Thickness(0, 0, 6, 0) });
        right.Children.Add(new KindChip { Kind = KindChip.From(command.ActionKind) });
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(right, 2);
        grid.Children.Add(chip);
        grid.Children.Add(texts);
        grid.Children.Add(right);
        return grid;
    }

    private void ListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || List.SelectedItem is not ListBoxItem { Tag: CommandBinding command }) return;
        Edit(command);
    }

    private void FilterChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) RebuildList(); }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) RebuildList(); }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        NewAt("", "");
        Composer.Pattern = "";
        PatternLength.Text = "0 / 5";
        NameBox.Focus();
    }

    // ---------- inspector ----------

    private void LoadDraft(CommandBinding? command)
    {
        _syncing = true;
        NameBox.Text = command?.Name ?? "";
        TargetBox.Text = command is { ActionKind: not ActionKind.Workflow and not ActionKind.Repeat } ? command.Target : "";
        KindSeg.SelectedIndex = (int)(command?.ActionKind ?? ActionKind.Application);
        Composer.Pattern = command?.Pattern ?? "";
        PatternLength.Text = $"{Composer.Pattern.Length} / 5";
        _draftScope = command?.Scope ?? "";
        ScopeSeg.SelectedIndex = _draftScope.Length == 0 ? 0 : 1;
        ScopeAppBox.Text = _draftScope;
        FillScopeApps();
        _pinManual = command?.PinnedManually ?? false;
        PinSwitch.IsChecked = command?.Pinned ?? false;
        _syncing = false;
        RefreshInspector();
    }

    private void FillScopeApps()
    {
        // ScopeAppBox is a free-text process name; candidates live in ContextApps / RecentProcess for the picker.
    }

    private void ScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncing) return;
        if (DraftKind is ActionKind.Workflow or ActionKind.Repeat && ScopeSeg.SelectedIndex != 0)
        {
            _syncing = true;
            ScopeSeg.SelectedIndex = 0;
            _syncing = false;
            _shell.Toast(Loc.T("工作流与「重复上一次」只能是全局", "Workflow and Repeat last must be global"), IconKind.Alert);
        }
        RefreshInspector();
        RefreshPatternHint();
    }

    private void KindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncing) return;
        if (DraftKind is ActionKind.Workflow or ActionKind.Repeat)
        {
            _syncing = true;
            ScopeSeg.SelectedIndex = 0;
            _syncing = false;
        }
        bool isShortcut = DraftKind == ActionKind.Shortcut;
        if (isShortcut != KeyChord.TryParse(TargetBox.Text, out _) && DraftKind != ActionKind.Repeat) TargetBox.Text = "";
        RefreshInspector();
        RefreshPatternHint();
    }

    private void PinClick(object sender, RoutedEventArgs e)
    {
        _pinManual = true;
        RefreshPatternHint();
    }

    private void TargetKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DraftKind != ActionKind.Shortcut) return;
        var key = e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, _ => e.Key };
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Tab && modifiers == ModifierKeys.None) return;
        e.Handled = true;
        if (key == Key.Back && modifiers == ModifierKeys.None) { TargetBox.Text = ""; return; }
        ushort vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
        if (KeyChord.IsModifier(vk)) return;
        if (!KeyChord.IsNamedKey(vk)) { _shell.Toast("暂不支持这个按键", IconKind.Alert); return; }
        var held = new List<ushort>();
        if (modifiers.HasFlag(ModifierKeys.Control)) held.Add(KeyChord.Ctrl);
        if (modifiers.HasFlag(ModifierKeys.Shift)) held.Add(KeyChord.Shift);
        if (modifiers.HasFlag(ModifierKeys.Alt)) held.Add(KeyChord.Alt);
        if (modifiers.HasFlag(ModifierKeys.Windows)) held.Add(KeyChord.Win);
        TargetBox.Text = KeyChord.Format(held, vk);
        TargetBox.CaretIndex = TargetBox.Text.Length;
    }

    private void ApplyPreset(string name, string keys)
    {
        TargetBox.Text = keys;
        if (NameBox.Text.Trim().Length == 0 || TargetField.ShortcutPresets.Any(p => p.Name == NameBox.Text.Trim())) NameBox.Text = name;
    }

    private void RefreshInspector()
    {
        bool editing = _editing is not null;
        var kind = DraftKind;
        string scope = DraftScope;
        InspectorEyebrow.Text = editing ? "EDIT COMMAND" : "NEW COMMAND";
        InspectorTitle.Text = editing ? Loc.T("编辑指令", "Edit command") : Loc.T("新建指令", "New command");
        InspectorChip.Visibility = Visibility.Visible;
        InspectorChip.Kind = KindChip.From(kind);
        InspectorBadge.Text = scope.Length == 0
            ? (PinSwitch.IsChecked == true ? Loc.T("全局 · 固定", "Global · pinned") : Loc.T("全局", "Global"))
            : Loc.T($"{scope} 中的含义", $"Meaning in {scope}");
        DeleteButton.Visibility = TestButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (editing)
            DeleteButton.Content = scope.Length > 0 && _c.Settings.Commands.Global(_editing!.Pattern) is not null
                ? Loc.T("恢复为全局含义", "Revert to global")
                : Loc.T("删除", "Delete");

        ScopeAppPanel.Visibility = ScopeSeg.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ScopeHint.Text = scope.Length == 0
            ? Loc.T("所有程序的默认含义；可固定以免被程序覆盖", "Default for all apps; pin to prevent overrides")
            : Loc.T($"光标在 {scope}.exe 上时使用这个含义", $"Used when the cursor is over {scope}.exe");

        bool workflow = kind == ActionKind.Workflow;
        bool repeat = kind == ActionKind.Repeat;
        bool hasSteps = _editing is { ActionKind: ActionKind.Workflow };
        TargetPanel.Visibility = workflow || repeat ? Visibility.Collapsed : Visibility.Visible;
        WorkflowNote.Visibility = workflow ? Visibility.Visible : Visibility.Collapsed;
        RepeatNote.Visibility = repeat ? Visibility.Visible : Visibility.Collapsed;
        if (workflow)
        {
            WorkflowNoteText.Text = Loc.T("工作流的步骤在「工作流」页编辑。", "Edit workflow steps on the Workflows page.")
                + (hasSteps ? "" : Loc.T("保存前请先添加至少 1 个步骤。", " Add at least one step before saving."));
            WorkflowNoteButton.Content = hasSteps ? Loc.T("打开工作流编辑器", "Open workflow editor") : Loc.T("前往工作流页创建", "Create on Workflows page");
        }
        if (repeat)
        {
            var last = _c.Executor.Last;
            RepeatNoteText.Text = last is null
                ? Loc.T("还没有可重复的动作。成功执行一条节奏指令或手势后会出现在这里。", "Nothing to repeat yet. A successful rhythm or gesture will show here.")
                : Loc.T($"当前会重复：{last.Command.Name}（{PulseController.TargetText(last.Command)}）",
                    $"Will repeat: {last.Command.Name} ({PulseController.TargetText(last.Command)})");
        }
        SaveButton.IsEnabled = !workflow || hasSteps;
        if (!workflow && !repeat)
        {
            var (label, placeholder, hint, browse) = TargetField.Config(kind);
            TargetLabel.Text = label;
            Ui.SetPlaceholder(TargetBox, placeholder);
            TargetHint.Text = hint;
            BrowseButton.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
        }
        ShortcutPresets.Visibility = kind == ActionKind.Shortcut ? Visibility.Visible : Visibility.Collapsed;
        PinPanel.Visibility = scope.Length == 0 && kind is not ActionKind.Workflow and not ActionKind.Repeat ? Visibility.Visible : Visibility.Collapsed;
        var replacers = scope.Length == 0
            ? _c.Settings.Commands.Where(c => c.Scope.Length > 0 && c.Pattern == Composer.Pattern).Select(c => c.Scope).Distinct().ToList()
            : new List<string>();
        PinHint.Text = PinSwitch.IsChecked == true
            ? Loc.T("各程序不能占用这个节奏", "Apps cannot override this rhythm")
            : replacers.Count > 0
                ? Loc.T($"当前被 {string.Join("、", replacers)} 替换", $"Currently overridden in {string.Join(", ", replacers)}")
                : Loc.T("程序可以给这个节奏定义自己的含义", "Apps may define their own meaning for this rhythm");

        HeatPanel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        if (editing && _editing is not null) RefreshHeat(_editing);
        RefreshPatternHint();
    }

    private void RefreshHeat(CommandBinding command)
    {
        var week = CommandHeat.Series(_c.Settings, command.Id, DateTime.Today);
        int total = CommandHeat.Total(week);
        HeatTotal.Text = total > 0 ? $"{total}" : Loc.T("7 天未用", "Unused");
        HeatBars.Children.Clear();
        int max = Math.Max(1, week.Max());
        foreach (int v in week)
        {
            HeatBars.Children.Add(new Border
            {
                Width = 10, Height = Math.Max(2, 24.0 * v / max), CornerRadius = new CornerRadius(3),
                Background = Ui.Brush(v > 0 ? "MintBrush" : "Line2Brush"),
                Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Bottom, Opacity = v > 0 ? 1 : 0.4
            });
        }
        LastUsedText.Text = command.LastUsed.Length == 0
            ? Loc.T("尚未执行过", "Not used yet")
            : Loc.T($"上次 {command.LastUsed}{(command.LastContext.Length > 0 && command.LastContext != ContextNames.Desktop ? $" 在 {command.LastContext}" : "")}",
                $"Last {command.LastUsed}{(command.LastContext.Length > 0 && command.LastContext != ContextNames.Desktop ? $" in {command.LastContext}" : "")}");
    }

    private void RefreshPatternHint()
    {
        if (!IsLoaded) return;
        string pattern = Composer.Pattern;
        string scope = DraftScope;
        bool pinned = PinSwitch.IsChecked == true;
        var check = _c.Settings.Commands.Check(pattern, scope, _editing?.Id, pinned, DraftKind);
        PatternHint.Foreground = Ui.Brush(check.SaveBlocked ? "CoralBrush" : check.Issue == PatternIssue.ReplacesGlobal ? "AmberBrush" : "Text3Brush");
        PatternHint.Text = check.Issue switch
        {
            PatternIssue.Empty => Loc.T("点击添加短按 · 或长按 —，最多 5 位", "Add · or — , up to 5 pulses"),
            PatternIssue.WorkflowOrRepeatScoped => Loc.T("工作流与「重复上一次」只能是全局", "Workflow / Repeat must be global"),
            PatternIssue.DuplicateInScope => Loc.T($"{(scope.Length == 0 ? "全局" : scope)} 中这个节奏已经是「{check.Message}」", $"Already “{check.Message}” here"),
            PatternIssue.PinnedGlobal => Loc.T($"「{check.Message}」已固定，在所有程序中保持不变", $"“{check.Message}” is pinned globally"),
            PatternIssue.ReplacesGlobal => Loc.T($"在 {(scope.Length == 0 ? "?" : scope)} 中替换全局「{check.Message}」；其他程序不受影响", $"Replaces global “{check.Message}” here"),
            PatternIssue.GlobalTaken => Loc.T($"已被全局「{check.Message}」占用", $"Taken by global “{check.Message}”"),
            PatternIssue.GlobalPinnedByOthers => Loc.T($"要固定需先移除程序含义（如「{check.Message}」）", $"Remove app overrides (e.g. “{check.Message}”) before pinning"),
            _ when scope.Length > 0 && _c.Settings.Commands.Global(pattern) is null =>
                Loc.T($"{scope} 中可用；其他程序里这个节奏没有含义", $"Free in {scope}; unused elsewhere"),
            _ when scope.Length == 0 && check.Message.Length > 0 =>
                Loc.T($"可用；{check.Message} 中有程序含义", $"OK; overridden in {check.Message}"),
            _ => ""
        };
        SuggestPanel.Children.Clear();
        if (check.Suggestions.Count > 0)
        {
            SuggestPanel.Children.Add(new TextBlock { Text = Loc.T("推荐：", "Try: "), FontSize = 11.5, Foreground = Ui.Brush("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            foreach (var s in check.Suggestions)
            {
                var btn = new Button { Style = (Style)FindResource("BtnSmGhost"), Margin = new Thickness(0, 0, 6, 0), Content = new PatternGlyph { Pattern = s, GlyphSize = GlyphSize.Small } };
                string pick = s;
                btn.Click += (_, _) => { Composer.Pattern = pick; PatternLength.Text = $"{pick.Length} / 5"; RefreshPatternHint(); };
                SuggestPanel.Children.Add(btn);
            }
        }
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var path = TargetField.Browse(DraftKind, TargetBox.Text.Trim());
        if (path is null) return;
        TargetBox.Text = path;
        if (string.IsNullOrWhiteSpace(NameBox.Text) && DraftKind == ActionKind.Application)
            NameBox.Text = "打开 " + Path.GetFileNameWithoutExtension(path);
    }

    private async void PickScopeClick(object sender, RoutedEventArgs e)
    {
        _shell.Toast(Loc.T("3 秒内把光标移到目标窗口…", "Move the cursor onto the target window within 3s…"), IconKind.Info);
        await Task.Delay(3000);
        var app = WindowCatalog.UnderCursor();
        if (app is null) { _shell.Toast(Loc.T("未读到程序", "No app detected"), IconKind.Alert); return; }
        ScopeAppBox.Text = app.Process;
        EnsureContextApp(app.Process, app.DisplayName, app.ExePath);
        RefreshInspector();
    }

    private void EnsureContextApp(string process, string display, string exe)
    {
        if (_c.Settings.ContextApps.Any(a => a.Process.Equals(process, StringComparison.OrdinalIgnoreCase))) return;
        _c.Settings.ContextApps.Add(new ContextApp { Process = process, DisplayName = display.Length > 0 ? display : process, ExePath = exe });
        _c.SaveSoon();
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim(), pattern = Composer.Pattern, target = TargetBox.Text.Trim();
        var kind = DraftKind;
        string scope = DraftScope;
        bool pinned = scope.Length == 0 && (kind is ActionKind.Workflow or ActionKind.Repeat || PinSwitch.IsChecked == true);
        if (name.Length == 0) { _shell.Toast(Loc.T("请填写名称", "Name required"), IconKind.Alert); return; }
        if (pattern.Length == 0) { _shell.Toast(Loc.T("请添加 1–5 位节奏", "Add a 1–5 pulse rhythm"), IconKind.Alert); return; }
        if (kind is ActionKind.Workflow or ActionKind.Repeat) scope = "";
        if (ScopeSeg.SelectedIndex == 1 && scope.Length == 0) { _shell.Toast(Loc.T("请填写程序名", "Enter a process name"), IconKind.Alert); return; }
        var check = _c.Settings.Commands.Check(pattern, scope, _editing?.Id, pinned, kind);
        if (check.SaveBlocked) { _shell.Toast(PatternHint.Text.Length > 0 ? PatternHint.Text : Loc.T("无法保存", "Cannot save"), IconKind.Alert); return; }
        if (kind is not (ActionKind.Workflow or ActionKind.Repeat) && PulseController.ValidateTarget(kind, target) is { } error)
        { _shell.Toast(error, IconKind.Alert); return; }
        if (kind == ActionKind.Shortcut && KeyChord.TryParse(target, out var chord)) target = chord.ToString();
        if (kind == ActionKind.Repeat) target = "";

        bool wasReplace = scope.Length > 0 && _c.Settings.Commands.Global(pattern) is { Pinned: false };
        string oldPattern = _editing?.Pattern ?? "";
        string oldScope = _editing?.Scope ?? "";

        if (_editing is null)
        {
            _editing = new CommandBinding
            {
                Id = Guid.NewGuid().ToString("N"), Name = name, Pattern = pattern, ActionKind = kind, Target = target,
                Scope = scope, Pinned = pinned, PinnedManually = _pinManual,
                RemindLeft = wasReplace ? 5 : 0
            };
            _c.Settings.Commands.Add(_editing);
        }
        else
        {
            _editing.Name = name;
            _editing.Pattern = pattern;
            if (kind != ActionKind.Workflow) _editing.Target = target;
            _editing.ActionKind = kind;
            _editing.Scope = scope;
            _editing.PinnedManually = _pinManual;
            if (scope.Length == 0) _editing.Pinned = pinned;
            // Scope change remind rules (spec 5.4)
            bool nowReplace = scope.Length > 0 && _c.Settings.Commands.Global(pattern) is { Pinned: false };
            if (!nowReplace) _editing.RemindLeft = 0;
            else if (_editing.RemindLeft == 0 && (oldScope != scope || oldPattern != pattern)) _editing.RemindLeft = 5;
        }
        PinPolicy.Apply(_editing);
        if (scope.Length > 0) EnsureContextApp(scope, scope, "");
        _mapFocusPattern = pattern;
        _mapFocusScope = scope;
        _c.NotifyBindingsChanged();
        _shell.Toast(Loc.T($"已保存「{name}」", $"Saved “{name}”"));
        LoadDraft(_editing);
        Rebuild();
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } command) return;
        string title = command.Scope.Length > 0 && _c.Settings.Commands.Global(command.Pattern) is { } g
            ? Loc.T($"恢复为全局「{g.Name}」？", $"Revert to global “{g.Name}”?")
            : Loc.T($"删除指令「{command.Name}」？", $"Delete “{command.Name}”?");
        bool ok = await _shell.ConfirmAsync(new ConfirmRequest(title, Loc.T("删除后会立即写入配置，可通过导入备份恢复。", "Writes immediately; restore via import."), null, Loc.T("删除", "Delete"), true));
        if (!ok) return;
        _c.Settings.Commands.Remove(command);
        _editing = _c.Settings.Commands.FirstOrDefault();
        _c.NotifyBindingsChanged();
        _shell.Toast(Loc.T("已删除", "Deleted"));
        LoadDraft(_editing);
        Rebuild();
    }

    private void TestClick(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        if (_editing.ActionKind == ActionKind.Shortcut)
        {
            if (!_c.Settings.ActivateTargetForShortcut)
                _shell.Toast(Loc.T("未开启「发送前激活」，快捷键会发到前台窗口", "Activate-before-send is off — shortcut goes to the foreground"), IconKind.Info);
            _ = _c.TestCommand(_editing);
            return;
        }
        _ = _c.Execute(_editing, _editing.Pattern);
    }

    private void ToWorkflowsClick(object sender, RoutedEventArgs e)
    {
        if (_editing is { ActionKind: ActionKind.Workflow } workflow) _shell.Workflows.Select(workflow);
        else _shell.Workflows.BeginNew(NameBox.Text.Trim(), Composer.Pattern);
        _shell.Navigate(Page.Workflows);
    }

    // ---------- apps / template ----------

    private async void AddAppClick(object sender, RoutedEventArgs e)
    {
        var running = WindowCatalog.List();
        var choices = new List<(string Process, string Label)>();
        if (_c.RecentProcess.Length > 0)
            choices.Add((_c.RecentProcess, Loc.T($"{_c.RecentProcess} · 刚刚用过", $"{_c.RecentProcess} · just used")));
        foreach (var app in running)
        {
            if (choices.Any(c => c.Process.Equals(app.Process, StringComparison.OrdinalIgnoreCase))) continue;
            bool added = _c.Settings.ContextApps.Any(a => a.Process.Equals(app.Process, StringComparison.OrdinalIgnoreCase));
            choices.Add((app.Process, $"{app.DisplayName} ({app.Process}.exe){(added ? Loc.T(" · 已添加", " · added") : Loc.T(" · 运行中", " · running"))}"));
        }
        if (choices.Count == 0) { _shell.Toast(Loc.T("没有可添加的程序，可点编辑栏「拾取」", "No apps — use Pick in the editor"), IconKind.Info); return; }

        // Pick first non-added, or ask via confirm list
        var pick = choices.FirstOrDefault(c => !_c.Settings.ContextApps.Any(a => a.Process.Equals(c.Process, StringComparison.OrdinalIgnoreCase)));
        if (pick.Process is null)
        {
            _shell.Toast(Loc.T("运行中的程序都已添加", "All running apps are already listed"), IconKind.Info);
            return;
        }
        var run = running.FirstOrDefault(r => r.Process.Equals(pick.Process, StringComparison.OrdinalIgnoreCase));
        EnsureContextApp(pick.Process, run?.DisplayName ?? pick.Process, run?.ExePath ?? "");
        _c.NotifyBindingsChanged();
        _shell.Toast(Loc.T($"已添加 {pick.Process}", $"Added {pick.Process}"));
        Rebuild();
        await Task.CompletedTask;
    }

    private void RecentAddClick(object sender, RoutedEventArgs e)
    {
        string recent = _c.RecentProcess;
        if (recent.Length == 0) return;
        var under = WindowCatalog.List().FirstOrDefault(a => a.Process.Equals(recent, StringComparison.OrdinalIgnoreCase));
        EnsureContextApp(recent, under?.DisplayName ?? recent, under?.ExePath ?? "");
        _c.NotifyBindingsChanged();
        _shell.Toast(Loc.T($"已添加 {recent}", $"Added {recent}"));
        Rebuild();
    }

    private async Task RemoveAppAsync(string process)
    {
        var doomed = _c.Settings.Commands.Where(c => c.Scope.Equals(process, StringComparison.OrdinalIgnoreCase)).ToList();
        var lines = doomed.Select(c =>
        {
            var g = _c.Settings.Commands.Global(c.Pattern);
            return g is null
                ? $"{c.Name} ({PatternText(c.Pattern)}) → {Loc.T("无含义", "no meaning")}"
                : $"{c.Name} ({PatternText(c.Pattern)}) → {Loc.T($"全局「{g.Name}」", $"global “{g.Name}”")}";
        }).ToList();
        bool ok = await _shell.ConfirmAsync(new ConfirmRequest(
            Loc.T($"移除程序 {process}？", $"Remove app {process}?"),
            Loc.T("将删除该程序的全部含义：", "Will delete all meanings for this app:"),
            lines.Count > 0 ? lines : new[] { Loc.T("（没有程序含义）", "(no app meanings)") },
            Loc.T("移除", "Remove"), true));
        if (!ok) return;
        foreach (var c in doomed) _c.Settings.Commands.Remove(c);
        _c.Settings.ContextApps.RemoveAll(a => a.Process.Equals(process, StringComparison.OrdinalIgnoreCase));
        if (_editing?.Scope.Equals(process, StringComparison.OrdinalIgnoreCase) == true) _editing = null;
        _c.NotifyBindingsChanged();
        _shell.Toast(Loc.T("已移除", "Removed"));
        LoadDraft(_editing);
        Rebuild();
    }

    private async void TemplateClick(object sender, RoutedEventArgs e)
    {
        string app = _c.Settings.ContextApps.FirstOrDefault(a => a.Process is "chrome" or "msedge")?.Process
            ?? (_c.RecentProcess is "chrome" or "msedge" ? _c.RecentProcess : "chrome");
        bool english = Loc.English;
        var picks = new Dictionary<int, bool>();
        var plan = _c.Settings.Commands.Plan(app, english, picks);
        var items = plan.Select((r, i) =>
        {
            string state = r.State switch
            {
                TemplateRowState.Added => Loc.T("已添加", "Added"),
                TemplateRowState.Move => Loc.T($"自动改用 {PatternText(r.Pattern)}", $"Remap {PatternText(r.Pattern)}"),
                TemplateRowState.Warn => Loc.T("替换全局", "Replaces global"),
                _ => Loc.T("空闲", "Free")
            };
            return $"{(r.Checked ? "☑" : "☐")} {r.Name} · {PatternText(r.Pattern)} · {r.Shortcut} · {state}";
        }).ToList();
        bool ok = await _shell.ConfirmAsync(new ConfirmRequest(
            Loc.T($"浏览器模板 → {app}", $"Browser template → {app}"),
            Loc.T("将添加默认勾选的推荐条目（后退 / 刷新 / 重新打开标签页）。确认后写入，不执行动作。",
                "Adds the three recommended items. Writes bindings only — does not run them."),
            items, Loc.T("添加", "Add")));
        if (!ok) return;

        // Apply default-checked rows from a fresh plan
        plan = _c.Settings.Commands.Plan(app, english);
        foreach (var row in plan.Where(r => r.Checked && r.Selectable && r.State != TemplateRowState.Added))
        {
            bool replaces = _c.Settings.Commands.Global(row.Pattern) is { Pinned: false };
            _c.Settings.Commands.Add(new CommandBinding
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = row.Name,
                Pattern = row.Pattern,
                ActionKind = ActionKind.Shortcut,
                Target = row.Shortcut,
                Scope = app,
                RemindLeft = replaces ? 5 : 0
            });
        }
        var run = WindowCatalog.List().FirstOrDefault(a => a.Process.Equals(app, StringComparison.OrdinalIgnoreCase));
        EnsureContextApp(app, run?.DisplayName ?? app, run?.ExePath ?? "");
        _c.NotifyBindingsChanged();
        _shell.Toast(Loc.T($"已为 {app} 添加模板指令", $"Template applied to {app}"));
        Rebuild();
    }

    private static string PatternText(string pattern) =>
        string.Concat(pattern.Select(c => c == '.' ? '·' : c == '-' ? '—' : c));
}
