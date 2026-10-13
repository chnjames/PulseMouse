namespace PulseMouse.Core;
public enum PulseKind { Dot, Dash }
public enum MouseInputMode { Auto = 0, X1 = 1, X2 = 2, Middle = 3 }
public enum ActionKind { Application, Folder, Url, Workflow }
public sealed class CommandBinding
{
    public string Name { get; set; } = "";
    public string Pattern { get; set; } = "";
    public ActionKind ActionKind { get; set; }
    public string Target { get; set; } = "";
}
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 8;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public bool Enabled { get; set; } = true;
    public int MouseButton { get; set; } = 2; // compatibility with previous settings
    public MouseInputMode InputMode { get; set; } = MouseInputMode.Auto;
    public int MiddleHoldMs { get; set; } = 550;
    public int ShortMaxMs { get; set; } = 300;
    public int LongMinMs { get; set; } = 500;
    public int LongMaxMs { get; set; } = 1600;
    public int SequenceGapMs { get; set; } = 750;
    public int MaxPulses { get; set; } = 5;
    public bool GesturesEnabled { get; set; } = true;
    public int GestureMinDistancePx { get; set; } = 75;
    public bool FloatingFeedback { get; set; } = true;
    public bool ConfirmWorkflows { get; set; } = true;
    public bool StartupEnabled { get; set; }
    public bool SuccessSound { get; set; }
    public string UiLanguage { get; set; } = "zh-CN";
    public string CapsulePosition { get; set; } = "Center";
    public bool CapsuleBreathing { get; set; } = true;
    public string StatisticsDate { get; set; } = "";
    public int ExecutionsToday { get; set; }
    public int MissesToday { get; set; }
    public string StatisticsWeekStart { get; set; } = "";
    public double ActiveSecondsThisWeek { get; set; }
    public string AiProviderUrl { get; set; } = "https://chatgpt.com/";
    public List<GestureBinding> Gestures { get; set; } = new()
    {
        new(){ Direction="Up", Name="打开文件管理器", ActionKind=ActionKind.Application, Target="explorer.exe" },
        new(){ Direction="Right", Name="打开记事本", ActionKind=ActionKind.Application, Target="notepad.exe" }
    };
    public List<CommandBinding> Commands { get; set; } = new()
    {
        new(){ Name="打开记事本", Pattern="..", ActionKind=ActionKind.Application, Target="notepad.exe" },
        new(){ Name="打开计算器", Pattern=".-", ActionKind=ActionKind.Application, Target="calc.exe" },
        new(){ Name="打开文件管理器", Pattern="-.", ActionKind=ActionKind.Application, Target="explorer.exe" }
    };
}
public readonly record struct Recognition(string Pattern, CommandBinding? Command);

public sealed class GestureBinding
{
    public string Direction { get; set; } = "";
    public string Name { get; set; } = "";
    public ActionKind ActionKind { get; set; }
    public string Target { get; set; } = "";
}
public sealed class WorkflowStep
{
    public ActionKind Kind { get; set; }
    public string Target { get; set; } = "";
    public int DelayMs { get; set; }
}
