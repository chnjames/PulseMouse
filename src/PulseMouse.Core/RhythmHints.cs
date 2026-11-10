namespace PulseMouse.Core;

/// <summary>Commands still reachable from what has been typed. Prefer <see cref="ContextResolver.Reachable"/> when context is known.</summary>
public static class RhythmHints
{
    public static (IReadOnlyList<CommandBinding> Shown, int Hidden) For(
        IReadOnlyList<CommandBinding> commands, string prefix, int max) =>
        commands.Reachable(ContextNames.Desktop, contextEnabled: false, prefix, max);

    public static (IReadOnlyList<CommandBinding> Shown, int Hidden) ForContext(
        IReadOnlyList<CommandBinding> commands, string context, bool contextEnabled, string prefix, int max = 6) =>
        commands.Reachable(context, contextEnabled, prefix, max);
}
