using PulseMouse.App.Controls;
using PulseMouse.Core;

namespace PulseMouse.App.Services;

public enum LivePhase { Idle, Armed, Press, Typing, Matched, Miss, Reset }

public enum ActivityType { Ok, Gesture, Miss, Failed, System }

public sealed record ActivityEntry(
    DateTime Time, ActivityType Type, string Title, string Pattern = "", ActionKind? Kind = null,
    string Target = "", string Direction = "", string Context = "", bool Own = false);

public enum LogTone { None, Hit, Pass, Err }

public sealed record LogEntry(DateTime Time, string Text, string Note, LogTone Tone)
{
    public override string ToString() => $"{Time:HH:mm:ss.fff} {Text}{(Note.Length > 0 ? " " + Note : "")}";
}

public sealed record ConfirmRequest(string Title, string Body, IReadOnlyList<string>? Items = null, string OkText = "确认", bool Danger = false);

public interface IShell
{
    void Toast(string message, IconKind icon = IconKind.Check);
    Task<bool> ConfirmAsync(ConfirmRequest request);
    void Navigate(Page page);
}

public static class Directions
{
    public static readonly string[] All = ["Up", "Down", "Left", "Right"];
    public static string Zh(string direction) => direction switch { "Up" => "上", "Down" => "下", "Left" => "左", "Right" => "右", _ => direction };
    public static IconKind Icon(string direction) => direction switch
    {
        "Up" => IconKind.Up, "Down" => IconKind.Down, "Left" => IconKind.Left, "Right" => IconKind.Right, _ => IconKind.Dot
    };
}
