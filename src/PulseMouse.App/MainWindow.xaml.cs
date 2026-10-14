using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PulseMouse.App.Controls;
using PulseMouse.App.Services;
using PulseMouse.App.Views;
using PulseMouse.Core;
using Forms = System.Windows.Forms;

namespace PulseMouse.App;

public partial class MainWindow : Window, IShell
{
    private readonly PulseController _controller;
    private readonly Forms.NotifyIcon _tray;
    private readonly Forms.ToolStripMenuItem _trayToggle;
    private readonly DispatcherTimer _toastTimer;
    private readonly Dictionary<Page, IPageView> _views = new();
    private TaskCompletionSource<bool>? _modal;
    private Page _page = Page.Dashboard;
    private bool _exiting;
    private IconTone _trayTone = IconTone.Active;

    private static int TraySize => Forms.SystemInformation.SmallIconSize.Width;

    public PulseController Controller => _controller;
    public DashboardView Dashboard => (DashboardView)View(Page.Dashboard);
    public CommandsView Commands => (CommandsView)View(Page.Commands);
    public WorkflowsView Workflows => (WorkflowsView)View(Page.Workflows);

    public MainWindow()
    {
        InitializeComponent();
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/PulseMouse.ico"));
        _controller = new PulseController(Dispatcher) { Shell = this };
        _controller.StateChanged += RefreshChrome;
        _controller.BindingsChanged += RefreshNavMeta;
        _controller.Saved += () => SavedText.Text = Loc.Saved(_controller.SavedAt);
        _controller.SettingsReplaced += () =>
        {
            foreach (var view in _views.Values) view.Reload();
            Relocalize();
        };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1700) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); AnimateToast(false); };

        _trayToggle = new Forms.ToolStripMenuItem("暂停监听", null, (_, _) => Dispatcher.Invoke(() => _controller.SetEnabled(!_controller.Enabled)));
        _tray = new Forms.NotifyIcon { Text = "PulseMouse", Icon = AppIcon.CreateTrayIcon(_trayTone, TraySize), Visible = true, ContextMenuStrip = new Forms.ContextMenuStrip() };
        _tray.ContextMenuStrip.Items.Add("打开 PulseMouse", null, (_, _) => Dispatcher.Invoke(RestoreWindow));
        _tray.ContextMenuStrip.Items.Add(_trayToggle);
        _tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Exit));
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);

        SourceInitialized += (_, _) => ApplyRoundedCorners();
        StateChanged += (_, _) => Frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Closing += (_, e) => { if (!_exiting) { e.Cancel = true; Hide(); } };
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) =>
        {
            Relocalize();
            NavDashboard.IsChecked = true;
            _controller.Start();
            RefreshChrome();
        };
    }

    // ---------- navigation ----------

    private IPageView View(Page page)
    {
        if (_views.TryGetValue(page, out var view)) return view;
        view = page switch
        {
            Page.Dashboard => new DashboardView(this),
            Page.Commands => new CommandsView(this),
            Page.Gestures => new GesturesView(this),
            Page.Workflows => new WorkflowsView(this),
            _ => new SettingsView(this)
        };
        _views[page] = view;
        return view;
    }

    private void NavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<Page>(tag, out var page)) ShowPage(page);
    }

    public void Navigate(Page page)
    {
        var nav = NavButton(page);
        if (nav.IsChecked == true) ShowPage(page); else nav.IsChecked = true;
    }

    private RadioButton NavButton(Page page) => page switch
    {
        Page.Dashboard => NavDashboard,
        Page.Commands => NavCommands,
        Page.Gestures => NavGestures,
        Page.Workflows => NavWorkflows,
        _ => NavSettings
    };

    private void ShowPage(Page page)
    {
        _page = page;
        var view = View(page);
        if (PageHost.Content is IPageView previous && previous != view) previous.OnHidden();
        PageHost.Content = view;
        view.OnShown();
        MainScroller.ScrollToTop();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        PageShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(6, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    public void Relocalize()
    {
        NavLabel.Text = Loc.NavLabel.ToUpperInvariant();
        foreach (var page in Enum.GetValues<Page>()) NavButton(page).Content = Loc.Nav(page);
        OfflineText.Text = Loc.Offline;
        SavedText.Text = Loc.Saved(_controller.SavedAt);
        foreach (var view in _views.Values) view.Relocalize();
        RefreshNavMeta();
        RefreshChrome();
    }

    private void RefreshNavMeta()
    {
        var s = _controller.Settings;
        Ui.SetMeta(NavCommands, s.Commands.Count.ToString());
        Ui.SetMeta(NavGestures, $"{s.Gestures.Count(g => Directions.All.Contains(g.Direction))}/4");
        Ui.SetMeta(NavWorkflows, s.Commands.Count(c => c.ActionKind == ActionKind.Workflow).ToString());
    }

    private void RefreshChrome()
    {
        bool on = _controller.Enabled;
        EngineDot.IsOn = StatusDot.IsOn = on;
        EnabledSwitch.IsChecked = on;
        EngineText.Text = on ? Loc.Listening : Loc.Paused;
        EngineSub.Text = _controller.ActiveInputText + "\n" + (_controller.HookInstalled ? Loc.HookInstalled : Loc.HookMissing);
        StatusText.Text = on ? Loc.Listening : Loc.StatusPaused;
        StatusInput.Text = _controller.ActiveInputText;
        _trayToggle.Text = on ? "暂停监听" : "恢复监听";
        bool broken = !_controller.HookInstalled || _controller.InjectionBlocked;
        _tray.Text = !on ? "PulseMouse · 已暂停"
            : !_controller.HookInstalled ? "PulseMouse · 监听未生效"
            : _controller.InjectionBlocked ? "PulseMouse · 模拟输入被拦截"
            : "PulseMouse · 监听中";
        var tone = !on ? IconTone.Paused : broken ? IconTone.Blocked : IconTone.Active;
        if (tone != _trayTone)
        {
            _trayTone = tone;
            var old = _tray.Icon;
            _tray.Icon = AppIcon.CreateTrayIcon(tone, TraySize);
            old?.Dispose();
        }
    }

    private void EnabledSwitchClick(object sender, RoutedEventArgs e) => _controller.SetEnabled(EnabledSwitch.IsChecked == true);

    // ---------- IShell ----------

    public void Toast(string message, IconKind icon = IconKind.Check)
    {
        ToastIcon.Kind = icon;
        ToastText.Text = message;
        _toastTimer.Stop();
        AnimateToast(true);
        _toastTimer.Start();
    }

    private void AnimateToast(bool show)
    {
        var duration = TimeSpan.FromMilliseconds(180);
        ToastBox.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, duration));
        ToastShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(show ? 0 : 8, duration));
    }

    public Task<bool> ConfirmAsync(ConfirmRequest request)
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            var window = new ConfirmWindow(request);
            window.ShowDialog();
            return Task.FromResult(window.Result);
        }
        _modal?.TrySetResult(false);
        var tcs = new TaskCompletionSource<bool>();
        _modal = tcs;
        var card = new ConfirmCard(request);
        card.Closed += ok =>
        {
            ModalLayer.Visibility = Visibility.Collapsed;
            ModalHost.Content = null;
            tcs.TrySetResult(ok);
        };
        ModalHost.Content = card;
        ModalLayer.Visibility = Visibility.Visible;
        ModalLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        Activate();
        return tcs.Task;
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ModalLayer.Visibility == Visibility.Visible) return;
        if (_controller.Phase is LivePhase.Press or LivePhase.Typing or LivePhase.Armed || _controller.Sequence.Length > 0)
        {
            _controller.CancelInput();
            e.Handled = true;
        }
    }

    // ---------- window chrome ----------

    private void TitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseClick(object sender, RoutedEventArgs e) => Hide();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    public void RestoreWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int preference = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
        }
        catch { /* Windows 10 has no rounded corner attribute. */ }
    }

    public void Exit()
    {
        _exiting = true;
        _modal?.TrySetResult(false);
        _controller.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
