namespace PulseMouse.Core;

public enum PatternIssue
{
    None,
    Empty,
    WorkflowOrRepeatScoped,
    DuplicateInScope,
    PinnedGlobal,
    ReplacesGlobal,
    GlobalTaken,
    GlobalPinnedByOthers,
}

public sealed record PatternCheck(PatternIssue Issue, string Message, IReadOnlyList<string> Suggestions)
{
    public bool SaveBlocked => Issue is PatternIssue.Empty or PatternIssue.WorkflowOrRepeatScoped
        or PatternIssue.DuplicateInScope or PatternIssue.PinnedGlobal or PatternIssue.GlobalTaken
        or PatternIssue.GlobalPinnedByOthers;
}

/// <summary>Validates a rhythm while editing (spec 5.4) and recommends free rhythms (1–5 pulses).</summary>
public static class PatternRules
{
    public static PatternCheck Check(
        this IReadOnlyList<CommandBinding> commands,
        string pattern,
        string scope,
        string? exceptId,
        bool asPinned,
        ActionKind kind = ActionKind.Shortcut)
    {
        if (pattern.Length == 0)
            return new PatternCheck(PatternIssue.Empty, "", Array.Empty<string>());
        if (kind is ActionKind.Workflow or ActionKind.Repeat && scope.Length > 0)
            return new PatternCheck(PatternIssue.WorkflowOrRepeatScoped, "", Array.Empty<string>());

        bool global = scope.Length == 0;
        CommandBinding? Same(string s) =>
            commands.FirstOrDefault(c => c.Pattern == pattern && c.Scope == s && c.Id != exceptId);

        if (global)
        {
            var duplicate = Same("");
            if (duplicate is not null)
                return new PatternCheck(PatternIssue.GlobalTaken, duplicate.Name, Recommend(commands, scope, exceptId, null));
            var replacers = commands.Where(c => c.Scope.Length > 0 && c.Pattern == pattern && c.Id != exceptId).ToList();
            if (asPinned && replacers.Count > 0)
                return new PatternCheck(PatternIssue.GlobalPinnedByOthers, replacers[0].Name, Array.Empty<string>());
            return new PatternCheck(PatternIssue.None, replacers.Count == 0 ? "" : replacers[0].Name, Array.Empty<string>());
        }

        var own = Same(scope);
        if (own is not null)
            return new PatternCheck(PatternIssue.DuplicateInScope, own.Name, Recommend(commands, scope, exceptId, null));
        var globalBinding = commands.Global(pattern);
        if (globalBinding is { Pinned: true })
            return new PatternCheck(PatternIssue.PinnedGlobal, globalBinding.Name, Recommend(commands, scope, exceptId, null));
        if (globalBinding is not null)
            return new PatternCheck(PatternIssue.ReplacesGlobal, globalBinding.Name, Array.Empty<string>());
        return new PatternCheck(PatternIssue.None, "", Array.Empty<string>());
    }

    public static IReadOnlyList<string> Recommend(
        this IReadOnlyList<CommandBinding> commands, string scope, string? exceptId, HashSet<string>? exclude)
    {
        var used = new HashSet<string>();
        foreach (var c in commands)
        {
            if (c.Id == exceptId) continue;
            if (c.Scope.Length == 0 || scope.Length == 0 || c.Scope == scope) used.Add(c.Pattern);
        }
        if (exclude is not null) foreach (var p in exclude) used.Add(p);
        return PatternOrder.Free(3, used);
    }
}
