namespace PulseMouse.Core;

/// <summary>
/// Resolves which command a rhythm means for the program under the cursor (spec 5.2).
/// Pinned global → program scope → global → miss. Pure and unit-testable.
/// </summary>
public static class ContextResolver
{
    public static CommandBinding? Global(this IEnumerable<CommandBinding> commands, string pattern)
        => commands.FirstOrDefault(c => c.Scope.Length == 0 && c.Pattern == pattern);

    public static CommandBinding? Own(this IEnumerable<CommandBinding> commands, string app, string pattern)
        => commands.FirstOrDefault(c => c.Scope == app && c.Pattern == pattern);

    public static IEnumerable<CommandBinding> ReplacedBy(this IEnumerable<CommandBinding> commands, CommandBinding global)
        => commands.Where(c => c.Scope.Length > 0 && c.Pattern == global.Pattern);

    /// <param name="context">Process name, or <see cref="ContextNames.Desktop"/> for desktop/taskbar.</param>
    public static CommandBinding? Resolve(this IReadOnlyList<CommandBinding> commands, string pattern, string context, bool contextEnabled)
    {
        var global = commands.Global(pattern);
        if (global is { Pinned: true }) return global;
        if (contextEnabled && context.Length > 0 && context != ContextNames.Desktop)
        {
            var own = commands.Own(context, pattern);
            if (own is not null) return own;
        }
        return global;
    }

    public static MeaningSource Classify(CommandBinding? command, string context)
    {
        if (command is null) return MeaningSource.None;
        if (command.Scope.Length > 0) return MeaningSource.Program;
        return command.Pinned ? MeaningSource.Pinned : MeaningSource.Global;
    }

    /// <summary>Meanings reachable in a context (hesitation hints / dashboard candidates), sorted shortest first then '.' before '-'.</summary>
    public static (IReadOnlyList<CommandBinding> Shown, int Hidden) Reachable(
        this IReadOnlyList<CommandBinding> commands, string context, bool contextEnabled, string prefix, int max = 6)
    {
        var list = new List<CommandBinding>();
        foreach (var pattern in PatternOrder.All)
        {
            if (!pattern.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var resolved = commands.Resolve(pattern, context, contextEnabled);
            if (resolved is not null) list.Add(resolved);
        }
        return (list.Take(max).ToList(), Math.Max(0, list.Count - max));
    }
}

/// <summary>Rhythm enumeration: 1–5 pulses, shortest first, then '.' before '-'.</summary>
public static class PatternOrder
{
    public static readonly string[] All = Build(5);

    public static int Rank(string pattern)
    {
        int index = Array.IndexOf(All, pattern);
        return index < 0 ? int.MaxValue : index;
    }

    public static string[] Free(int count, HashSet<string> used)
    {
        var result = new List<string>();
        foreach (var pattern in All)
        {
            if (result.Count >= count) break;
            if (!used.Contains(pattern)) result.Add(pattern);
        }
        return result.ToArray();
    }

    private static string[] Build(int maxLength)
    {
        var result = new List<string>();
        var current = new[] { "" };
        for (int length = 1; length <= maxLength; length++)
        {
            var next = new List<string>();
            foreach (var prefix in current) { next.Add(prefix + "."); next.Add(prefix + "-"); }
            result.AddRange(next);
            current = next.ToArray();
        }
        return result.ToArray();
    }
}
