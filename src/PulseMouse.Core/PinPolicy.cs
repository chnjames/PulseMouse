namespace PulseMouse.Core;

/// <summary>Auto-pinning for global bindings the user has not manually toggled (spec 5.3).</summary>
public static class PinPolicy
{
    private static readonly HashSet<string> SystemShortcuts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+Tab", "Win+Tab", "Win+D", "Win+E", "Win+L",
        "Shift+Win+S", "Win+Shift+S",
        "VolumeMute", "VolumeUp", "VolumeDown",
        "MediaPlayPause", "MediaNextTrack", "MediaPrevTrack",
    };

    public static bool ShouldAutoPin(ActionKind kind, string target)
    {
        if (kind is ActionKind.Application or ActionKind.Folder or ActionKind.Url
            or ActionKind.Workflow or ActionKind.Repeat)
            return true;
        return kind == ActionKind.Shortcut && SystemShortcuts.Contains(target.Trim());
    }

    /// <summary>Applies auto-pin unless the user already toggled the switch. Forces Workflow/Repeat to global+pinned.</summary>
    public static void Apply(CommandBinding command)
    {
        if (command.ActionKind is ActionKind.Workflow or ActionKind.Repeat)
        {
            command.Scope = "";
            if (!command.PinnedManually) command.Pinned = true;
            return;
        }
        if (command.Scope.Length > 0 || command.PinnedManually) return;
        command.Pinned = ShouldAutoPin(command.ActionKind, command.Target);
    }
}
