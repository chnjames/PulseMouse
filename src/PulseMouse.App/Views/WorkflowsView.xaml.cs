using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shapes;
using PulseMouse.App.Controls;
using PulseMouse.App.Services;
using PulseMouse.Core;

namespace PulseMouse.App.Views;

public partial class WorkflowsView : System.Windows.Controls.UserControl, IPageView
{
    private const int MaxSteps = 12, MaxDelayMs = 5000;

    private sealed class StepDraft
    {
        public ActionKind Kind { get; set; }
        public string Target { get; set; } = "";
        public string Delay { get; set; } = "0";
    }

    private readonly MainWindow _shell;
    private readonly PulseController _c;
    private readonly List<StepDraft> _steps = new();
    private CommandBinding? _editing;
    private (string Name, string Pattern)? _pendingNew;
    private bool _syncing;

    public WorkflowsView(MainWindow shell)
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

    private IEnumerable<CommandBinding> Workflows => _c.Settings.Commands.Where(c => c.ActionKind == ActionKind.Workflow);

    public void OnShown()
    {
        if (_pendingNew is { } draft)
        {
            _pendingNew = null;
            _editing = null;
            RebuildList();
            LoadDraft(null, draft.Name, draft.Pattern);
            NameBox.Focus();
            return;
        }
        if (_editing is null || !_c.Settings.Commands.Contains(_editing)) _editing = Workflows.FirstOrDefault();
        RebuildList();
        LoadDraft(_editing);
    }

    public void OnHidden() { }

    public void Relocalize()
    {
        Header.Eyebrow = Loc.Eyebrow(Page.Workflows);
        Header.Heading = Loc.Nav(Page.Workflows);
        Header.Description = Loc.Description(Page.Workflows);
    }

    public void Reload()
    {
        _editing = null;
        if (IsLoaded) OnShown();
    }

    public void Select(CommandBinding workflow)
    {
        _editing = workflow;
        _pendingNew = null;
    }

    public void BeginNew(string name, string pattern)
    {
        _editing = null;
        _pendingNew = (name, pattern);
    }

    // ---------- list ----------

    private void RebuildList()
    {
        _syncing = true;
        List.Items.Clear();
        var workflows = Workflows.ToList();
        foreach (var workflow in workflows)
        {
            var item = new ListBoxItem { Content = ItemContent(workflow), Tag = workflow };
            List.Items.Add(item);
            if (ReferenceEquals(workflow, _editing)) item.IsSelected = true;
        }
        EmptyState.Visibility = workflows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SafetyText.Inlines.Clear();
        SafetyText.Inlines.Add(new Run("安全规则：最多 12 步 · 每步延迟 0–5000 ms · 不允许嵌套工作流 · "));
        SafetyText.Inlines.Add(_c.Settings.ConfirmWorkflows
            ? new Run("执行前弹出确认")
            : new Run("执行前确认已关闭") { Foreground = Ui.Brush("AmberBrush"), FontWeight = FontWeights.Bold });
        _syncing = false;
    }

    private static UIElement ItemContent(CommandBinding workflow)
    {
        var steps = PulseController.ParseSteps(workflow.Target);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border
        {
            Style = (Style)System.Windows.Application.Current.Resources["PatternChip"],
            Child = new PatternGlyph { Pattern = workflow.Pattern, GlyphSize = GlyphSize.Medium, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        });
        var chain = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var muted = Ui.Brush("Text3Brush");
        for (int i = 0; i < steps.Count; i++)
        {
            if (i > 0) chain.Children.Add(new TextBlock { Text = "›", Foreground = muted, FontSize = 11.5, Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            chain.Children.Add(new Icon { Kind = KindChip.IconOf(steps[i].Kind), Size = 13, Foreground = muted, VerticalAlignment = VerticalAlignment.Center });
        }
        chain.Children.Add(new TextBlock
        {
            Text = $"{steps.Count} 步", Foreground = muted, FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["MonoFont"]
        });
        var texts = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = workflow.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(chain);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        return grid;
    }

    private void ListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || List.SelectedItem is not ListBoxItem { Tag: CommandBinding workflow }) return;
        _editing = workflow;
        LoadDraft(workflow);
    }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        _editing = null;
        RebuildList();
        LoadDraft(null);
        NameBox.Focus();
    }

    // ---------- editor ----------

    private void LoadDraft(CommandBinding? workflow, string name = "", string pattern = "")
    {
        NameBox.Text = workflow?.Name ?? name;
        Composer.Pattern = workflow?.Pattern ?? pattern;
        PatternLength.Text = $"{Composer.Pattern.Length} / 5";
        _steps.Clear();
        if (workflow is not null)
            _steps.AddRange(PulseController.ParseSteps(workflow.Target).Select(s => new StepDraft { Kind = s.Kind, Target = s.Target, Delay = s.DelayMs.ToString() }));
        if (_steps.Count == 0) _steps.Add(new StepDraft());
        bool editing = workflow is not null;
        InspectorEyebrow.Text = editing ? "EDIT WORKFLOW" : "NEW WORKFLOW";
        InspectorTitle.Text = editing ? workflow!.Name : "新建工作流";
        DeleteButton.Visibility = TestButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        RebuildSteps();
    }

    private void RefreshSummary()
    {
        int total = _steps.Sum(s => int.TryParse(s.Delay, out var d) ? Math.Max(0, d) : 0);
        SummaryChip.Text = $"{_steps.Count} 步 · 等待 {total / 1000.0:0.0}s";
        StepCount.Text = $"{_steps.Count} / {MaxSteps}";
        AddStepButton.IsEnabled = _steps.Count < MaxSteps;
    }

    private void RebuildSteps()
    {
        StepsPanel.Children.Clear();
        for (int i = 0; i < _steps.Count; i++)
        {
            if (i > 0)
                StepsPanel.Children.Add(new Line { X1 = 0.5, X2 = 0.5, Y1 = 0, Y2 = 18, Stroke = Ui.Brush("Line3Brush"), StrokeThickness = 1, StrokeDashArray = [3, 3], Margin = new Thickness(19, 0, 0, 0) });
            StepsPanel.Children.Add(StepRow(i));
        }
        RefreshSummary();
    }

    private UIElement StepRow(int index)
    {
        var step = _steps[index];
        var mono = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["MonoFont"];
        var grid = new Grid();
        foreach (var width in new[] { new GridLength(24), GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(104), GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

        var badge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = Ui.Brush("Surface2Brush"),
            Child = new TextBlock { Text = (index + 1).ToString(), FontFamily = mono, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Brush("Text2Brush"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };

        var target = new System.Windows.Controls.TextBox { Style = (Style)FindResource("InputMono"), Height = 32, Text = step.Target, Margin = new Thickness(8, 0, 0, 0) };
        Ui.SetPlaceholder(target, TargetField.StepPlaceholder(step.Kind));
        target.TextChanged += (_, _) => step.Target = target.Text;

        var kinds = new System.Windows.Controls.ListBox { Style = (Style)FindResource("SegIcons"), Margin = new Thickness(8, 0, 0, 0) };
        foreach (var (kind, label) in new[] { (ActionKind.Application, "应用"), (ActionKind.Folder, "文件夹"), (ActionKind.Url, "网址") })
        {
            var item = new ListBoxItem { ToolTip = label };
            Ui.SetIcon(item, KindChip.IconOf(kind));
            kinds.Items.Add(item);
        }
        kinds.SelectedIndex = Math.Min((int)step.Kind, 2);
        kinds.SelectionChanged += (_, _) =>
        {
            step.Kind = (ActionKind)kinds.SelectedIndex;
            Ui.SetPlaceholder(target, TargetField.StepPlaceholder(step.Kind));
        };

        var delay = new System.Windows.Controls.TextBox
        {
            Style = (Style)FindResource("InputMono"), Height = 32, Text = step.Delay, TextAlignment = TextAlignment.Right, Padding = new Thickness(10, 0, 30, 0), ToolTip = "执行此步骤前等待"
        };
        delay.TextChanged += (_, _) => { step.Delay = delay.Text; RefreshSummary(); };
        var delayBox = new Grid { Margin = new Thickness(8, 0, 0, 0) };
        delayBox.Children.Add(delay);
        delayBox.Children.Add(new TextBlock { Text = "ms", FontSize = 11, Foreground = Ui.Brush("Text3Brush"), HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), IsHitTestVisible = false });

        var tools = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        tools.Children.Add(Tool(IconKind.ChevronUp, "上移", index > 0, () => Move(index, -1)));
        tools.Children.Add(Tool(IconKind.ChevronDown, "下移", index < _steps.Count - 1, () => Move(index, 1)));
        tools.Children.Add(Tool(IconKind.X, "删除步骤", _steps.Count > 1, () => { _steps.RemoveAt(index); RebuildSteps(); }));

        Grid.SetColumn(kinds, 1);
        Grid.SetColumn(target, 2);
        Grid.SetColumn(delayBox, 3);
        Grid.SetColumn(tools, 4);
        grid.Children.Add(badge);
        grid.Children.Add(kinds);
        grid.Children.Add(target);
        grid.Children.Add(delayBox);
        grid.Children.Add(tools);
        return new Border
        {
            Padding = new Thickness(8), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
            BorderBrush = Ui.Brush("LineBrush"), Background = Ui.Brush("Shade18Brush"), Child = grid
        };
    }

    private System.Windows.Controls.Button Tool(IconKind icon, string tip, bool enabled, Action action)
    {
        var button = new System.Windows.Controls.Button { Style = (Style)FindResource("BtnSmIconGhost"), ToolTip = tip, IsEnabled = enabled, Margin = new Thickness(2, 0, 0, 0) };
        Ui.SetIcon(button, icon);
        button.Click += (_, _) => action();
        return button;
    }

    private void Move(int index, int offset)
    {
        (_steps[index], _steps[index + offset]) = (_steps[index + offset], _steps[index]);
        RebuildSteps();
    }

    private void AddStepClick(object sender, RoutedEventArgs e)
    {
        if (_steps.Count >= MaxSteps) return;
        _steps.Add(new StepDraft());
        RebuildSteps();
    }

    private List<WorkflowStep> ToSteps() => _steps.Select(s => new WorkflowStep
    {
        Kind = s.Kind, Target = s.Target.Trim(), DelayMs = int.TryParse(s.Delay, out var d) ? d : 0
    }).ToList();

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim(), pattern = Composer.Pattern;
        if (name.Length == 0) { _shell.Toast("请填写名称", IconKind.Alert); return; }
        if (pattern.Length == 0) { _shell.Toast("请添加 1–5 位节奏", IconKind.Alert); return; }
        if (_c.ConflictName(pattern, _editing) is not null) { _shell.Toast("该节奏已经被占用", IconKind.Alert); return; }
        for (int i = 0; i < _steps.Count; i++)
        {
            if (PulseController.ValidateTarget(_steps[i].Kind, _steps[i].Target) is { } error) { _shell.Toast($"第 {i + 1} 步：{error}", IconKind.Alert); return; }
            if (!int.TryParse(_steps[i].Delay.Trim(), out var delay) || delay is < 0 or > MaxDelayMs) { _shell.Toast($"第 {i + 1} 步：延迟需为 0–5000 ms", IconKind.Alert); return; }
        }
        string json = JsonSerializer.Serialize(ToSteps());
        if (_editing is null)
        {
            _editing = new CommandBinding { Name = name, Pattern = pattern, ActionKind = ActionKind.Workflow, Target = json };
            _c.Settings.Commands.Add(_editing);
        }
        else
        {
            _editing.Name = name;
            _editing.Pattern = pattern;
            _editing.ActionKind = ActionKind.Workflow;
            _editing.Target = json;
        }
        _c.NotifyBindingsChanged();
        _shell.Toast($"已保存「{name}」");
        LoadDraft(_editing);
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_editing is not { } workflow) return;
        bool ok = await _shell.ConfirmAsync(new ConfirmRequest($"删除工作流「{workflow.Name}」？", "删除后会立即写入配置，可通过导入备份恢复。", null, "删除", true));
        if (!ok) return;
        _c.Settings.Commands.Remove(workflow);
        _editing = Workflows.FirstOrDefault();
        _c.NotifyBindingsChanged();
        _shell.Toast("已删除");
        LoadDraft(_editing);
    }

    private void CopyJsonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(JsonSerializer.Serialize(ToSteps(), new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            _shell.Toast("已复制 JSON（兼容 v0.7 格式）");
        }
        catch (Exception ex) { _shell.Toast("复制失败：" + ex.Message, IconKind.Alert); }
    }

    private void TestClick(object sender, RoutedEventArgs e)
    {
        if (_editing is { } workflow) _ = _c.Execute(workflow, workflow.Pattern);
    }
}
