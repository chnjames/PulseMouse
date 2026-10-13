namespace PulseMouse.Core;

/// <summary>Commands still reachable from what has been typed, shortest rhythms first.</summary>
public static class RhythmHints
{
    public static (IReadOnlyList<CommandBinding> Shown, int Hidden) For(IEnumerable<CommandBinding> commands, string prefix, int max)
    {
        var matches = commands
            .Where(c => !string.IsNullOrEmpty(c.Pattern) && c.Pattern.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(c => c.Pattern.Length).ThenBy(c => c.Pattern, StringComparer.Ordinal)
            .ToList();
        return (matches.Take(max).ToList(), Math.Max(0, matches.Count - max));
    }
}
