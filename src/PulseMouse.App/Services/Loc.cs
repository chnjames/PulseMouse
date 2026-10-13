using PulseMouse.Core;

namespace PulseMouse.App.Services;

public enum Page { Dashboard, Commands, Gestures, Workflows, Settings }

/// <summary>Language switch covers navigation, page headers, kind tags and the status bar.</summary>
public static class Loc
{
    public static bool English { get; set; }

    private static string T(string zh, string en) => English ? en : zh;

    public static string Nav(Page page) => page switch
    {
        Page.Dashboard => T("仪表盘", "Dashboard"),
        Page.Commands => T("指令库", "Commands"),
        Page.Gestures => T("方向手势", "Gestures"),
        Page.Workflows => T("工作流", "Workflows"),
        _ => T("设置", "Settings")
    };

    public static string Eyebrow(Page page) => $"0{(int)page + 1} · {page.ToString().ToUpperInvariant()}";

    public static string Description(Page page) => page switch
    {
        Page.Dashboard => T("实时观察节奏输入、监听状态与最近执行。", "Monitor rhythm input, listening status and recent runs."),
        Page.Commands => T("把节奏绑定到程序、文件夹、网页与工作流。", "Bind rhythms to apps, folders, websites and workflows."),
        Page.Gestures => T("按住中键向四个方向移动，松开后执行。", "Hold the middle button, move, release to run."),
        Page.Workflows => T("把多个本地动作串成一条节奏指令，执行前确认。", "Chain local actions into one rhythm, confirmed before running."),
        _ => T("行为、节奏识别、Pulse Capsule、数据与备份。", "Behavior, timing, Pulse Capsule, data and backup.")
    };

    public static string Kind(ActionKind kind) => kind switch
    {
        ActionKind.Folder => T("文件夹", "Folder"),
        ActionKind.Url => T("网址", "URL"),
        ActionKind.Workflow => T("工作流", "Workflow"),
        _ => T("应用", "App")
    };

    public static string NavLabel => T("控制面", "Control surfaces");
    public static string Listening => T("监听中", "Listening");
    public static string Paused => T("已暂停", "Paused");
    public static string StatusPaused => T("监听已暂停，正常鼠标操作已恢复", "Listening paused — normal mouse input restored");
    public static string Saved(string time) => T($"已保存 {time}", $"Saved {time}");
    public static string Offline => T("本地离线运行", "Local · offline");
    public static string HookInstalled => T("Hook 已安装 · 数据仅保存在本机", "Hook installed · data stays on this PC");
    public static string HookMissing => T("Hook 未安装 · 数据仅保存在本机", "Hook not installed · data stays on this PC");
}
