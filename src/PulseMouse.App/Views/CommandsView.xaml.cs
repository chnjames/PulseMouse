using System.IO;
using System.Windows;
using System.Windows.Controls;
using PulseMouse.App.Controls;
using PulseMouse.App.Services;
using PulseMouse.Core;

namespace PulseMouse.App.Views;

public partial class CommandsView : System.Windows.Controls.UserControl, IPageView
{
    private readonly MainWindow _shell;
    private readonly PulseController _c;
    private CommandBinding? _editing;
    private bool _syncing, _pendingNew;

    public CommandsView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _c = shell.Controller;
        Composer.FindConflict = p => _c.ConflictName(p, _editing);
        Composer.PatternChanged += _ => PatternLength.Text = $"{Composer.Pattern.Length} / 5";
        Composer.LimitReached += () => _shell.Toast("节奏最多 5 位", IconKind.Alert);
        _c.BindingsChanged += () => { if (IsLoaded) RebuildList(); };
        Relocalize();
    }

    public void OnShown()
    {
        if (_pendingNew) { _pendingNew = false; RebuildList(); LoadDraft(null); NameBox.Focus(); return; }
        var target = _editing is not null && _c.Settings.Commands.Contains(_editing) ? _editing : _c.Settings.Commands.FirstOrDefault();
        _editing = target;
        RebuildList();
        LoadDraft(target);
    }

    public void OnHidden() { }

    public void Relocalize()
    {
        Header.Eyebrow = Loc.Eyebrow(Page.Commands);
        Header.Heading = Loc.Nav(Page.Commands);
        Header.Description = Loc.Description(Page.Commands);
        if (IsLoaded) RebuildList();
    }

    public void Reload()
    {
        _editing = null;
        if (IsLoaded) OnShown();
    }

    /// <summary>Opens an empty draft the next time the page is shown.</summary>
    public void BeginNew()
    {
        _editing = null;
        _pendingNew = true;
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
             || c.Pattern.Contains(qPattern, StringComparison.Ordinal)));
    }

    private void RebuildList()
    {
        _syncing = true;
        List.Items.Clear();
        var items = Filtered().ToList();
        foreach (var command in items)
        {
            var item = new ListBoxItem { Content = ItemContent(command), Tag = command };
            List.Items.Add(item);
            if (ReferenceEquals(command, _editing)) item.IsSelected = true;
        }
        ListMeta.Text = $"{items.Count} 条指令 · 每种节奏只能绑定一个动作";
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _syncing = false;
    }

    private static UIElement ItemContent(CommandBinding command)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var chip = new Border
        {
            Style = (Style)System.Windows.Application.Current.Resources["PatternChip"],
            Child = new PatternGlyph { Pattern = command.Pattern, GlyphSize = GlyphSize.Medium, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var texts = new StackPanel { Margin = new Thickness(14, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = command.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock
        {
            Text = PulseController.TargetText(command), FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["MonoFont"],
            FontSize = 11.5, Foreground = Ui.Brush("Text3Brush"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0)
        });
        var tag = new KindChip { Kind = KindChip.From(command.ActionKind) };
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(tag, 2);
        grid.Children.Add(chip);
        grid.Children.Add(texts);
        grid.Children.Add(tag);
        return grid;
    }

    private void ListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || List.SelectedItem is not ListBoxItem { Tag: CommandBinding command }) return;
        _editing = command;
        LoadDraft(command);
    }

    private void FilterChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) RebuildList(); }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) RebuildList(); }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        _editing = null;
        RebuildList();
        LoadDraft(null);
        NameBox.Focus();
    }

    // ---------- inspector ----------

    private ActionKind DraftKind => TargetField.KindAt(KindSeg.SelectedIndex);

    private void LoadDraft(CommandBinding? command)
    {
        _syncing = true;
        NameBox.Text = command?.Name ?? "";
        TargetBox.Text = command is { ActionKind: not ActionKind.Workflow } ? command.Target : "";
        KindSeg.SelectedIndex = (int)(command?.ActionKind ?? ActionKind.Application);
        Composer.Pattern = command?.Pattern ?? "";
        PatternLength.Text = $"{Composer.Pattern.Length} / 5";
        _syncing = false;
        RefreshInspector();
    }

    private void KindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded && !_syncing) RefreshInspector();
    }

    private void RefreshInspector()
    {
        bool editing = _editing is not null;
        var kind = DraftKind;
        InspectorEyebrow.Text = editing ? "EDIT COMMAND" : "NEW COMMAND";
        InspectorTitle.Text = editing ? "编辑指令" : "新建指令";
        InspectorChip.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        InspectorChip.Kind = KindChip.From(kind);
        DeleteButton.Visibility = TestButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        bool workflow = kind == ActionKind.Workflow;
        bool hasSteps = _editing is { ActionKind: ActionKind.Workflow };
        TargetPanel.Visibility = workflow ? Visibility.Collapsed : Visibility.Visible;
        WorkflowNote.Visibility = workflow ? Visibility.Visible : Visibility.Collapsed;
        WorkflowNoteText.Text = "工作流的步骤在「工作流」页编辑。" + (hasSteps ? "" : "保存前请先添加至少 1 个步骤。");
        WorkflowNoteButton.Content = hasSteps ? "打开工作流编辑器" : "前往工作流页创建";
        SaveButton.IsEnabled = !workflow || hasSteps;
        if (!workflow)
        {
            var (label, placeholder, hint, browse) = TargetField.Config(kind);
            TargetLabel.Text = label;
            Ui.SetPlaceholder(TargetBox, placeholder);
            TargetHint.Text = hint;
            BrowseButton.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
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

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim(), pattern = Composer.Pattern, target = TargetBox.Text.Trim();
        var kind = DraftKind;
        if (name.Length == 0) { _shell.Toast("请填写名称", IconKind.Alert); return; }
        if (pattern.Length == 0) { _shell.Toast("请添加 1–5 位节奏", IconKind.Alert); return; }
        if (_c.ConflictName(pattern, _editing) is not null) { _shell.Toast("该节奏已经被占用", IconKind.Alert); return; }
        if (kind != ActionKind.Workflow && PulseController.ValidateTarget(kind, target) is { } error) { _shell.Toast(error, IconKind.Alert); return; }

        if (_editing is null)
        {
            _editing = new CommandBinding { Name = name, Pattern = pattern, ActionKind = kind, Target = target };
            _c.Settings.Commands.Add(_editing);
        }
        else
        {
            _editing.Name = name;
            _editing.Pattern = pattern;
            if (kind != ActionKind.Workflow) _editing.Target = target;
            _editing.ActionKind = kind;
        }
        _c.NotifyBindingsChanged();
        _shell.Toast($"已保存「{name}」");
        LoadDraft(_editing);
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } command) return;
        bool ok = await _shell.ConfirmAsync(new ConfirmRequest($"删除指令「{command.Name}」？", "删除后会立即写入配置，可通过导入备份恢复。", null, "删除", true));
        if (!ok) return;
        _c.Settings.Commands.Remove(command);
        _editing = _c.Settings.Commands.FirstOrDefault();
        _c.NotifyBindingsChanged();
        _shell.Toast("已删除");
        LoadDraft(_editing);
    }

    private void TestClick(object sender, RoutedEventArgs e)
    {
        if (_editing is { } command) _ = _c.Execute(command, command.Pattern);
    }

    private void ToWorkflowsClick(object sender, RoutedEventArgs e)
    {
        if (_editing is { ActionKind: ActionKind.Workflow } workflow) _shell.Workflows.Select(workflow);
        else _shell.Workflows.BeginNew(NameBox.Text.Trim(), Composer.Pattern);
        _shell.Navigate(Page.Workflows);
    }
}
