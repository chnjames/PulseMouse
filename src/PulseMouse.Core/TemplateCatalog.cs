namespace PulseMouse.Core;

public enum TemplateRowState { Added, Move, Warn, Free }

public sealed record TemplateItem(string Zh, string En, string Pattern, string Shortcut, bool Recommended);

public sealed record TemplateRow(
    string Name,
    string DefaultPattern,
    string Shortcut,
    string Pattern,
    TemplateRowState State,
    bool Checked,
    bool Selectable,
    bool Recommended);

/// <summary>Browser template (spec 5.11): status + free-pattern allocation; default-check only recommended rows.</summary>
public static class TemplateCatalog
{
    public static readonly string[] TargetApps = ["chrome", "msedge"];

    public static readonly TemplateItem[] Items =
    [
        new("后退", "Back", ".-", "Alt+Left", true),
        new("刷新", "Refresh", "...", "F5", true),
        new("重新打开标签页", "Reopen tab", ".--", "Ctrl+Shift+T", true),
        new("前进", "Forward", "-.", "Alt+Right", false),
        new("下一个标签页", "Next tab", "-..", "Ctrl+Tab", false),
        new("新标签页", "New tab", ".", "Ctrl+T", false),
    ];

    public static string DisplayName(TemplateItem item, bool english) => english ? item.En : item.Zh;

    /// <summary>
    /// Computes per-row status and allocation. Checked defaults: recommended + Free/Move; Warn and optional rows stay off.
    /// </summary>
    public static IReadOnlyList<TemplateRow> Plan(
        this IReadOnlyList<CommandBinding> commands, string app, bool english, IReadOnlyDictionary<int, bool>? pick = null)
    {
        var rows = new List<(int Index, TemplateRowState State, bool Selectable, string Pattern, bool DefaultOn)>();
        for (int i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            CommandBinding? existing = commands.FirstOrDefault(c => c.Scope == app && c.Pattern == item.Pattern);
            TemplateRowState state;
            bool selectable = true;
            bool defaultOn;
            string pattern = item.Pattern;
            if (existing is not null && existing.Name == DisplayName(item, english))
            {
                state = TemplateRowState.Added;
                selectable = false;
                defaultOn = false;
            }
            else if (existing is not null || commands.Global(item.Pattern) is { Pinned: true })
            {
                state = TemplateRowState.Move;
                defaultOn = item.Recommended;
            }
            else if (commands.Global(item.Pattern) is not null)
            {
                state = TemplateRowState.Warn;
                selectable = true;
                defaultOn = false;
            }
            else
            {
                state = TemplateRowState.Free;
                defaultOn = item.Recommended;
            }
            rows.Add((i, state, selectable, pattern, defaultOn));
        }

        var taken = new HashSet<string>();
        foreach (var row in rows)
            if (row.State != TemplateRowState.Move) taken.Add(row.Pattern);
        foreach (var c in commands.Where(c => c.Scope == app)) taken.Add(c.Pattern);

        // Allocate free patterns for Move rows: checked ones first so they don't collide with each other.
        var moves = rows.Select((r, i) => (r, i)).Where(t => t.r.State == TemplateRowState.Move).ToList();
        bool On(int index)
        {
            var r = rows[index];
            if (!r.Selectable || r.State == TemplateRowState.Added) return false;
            return pick?.TryGetValue(index, out var v) == true ? v : r.DefaultOn;
        }
        foreach (var ordered in moves.OrderByDescending(t => On(t.i)).ThenBy(t => t.i))
        {
            var free = PatternOrder.Free(1, taken);
            if (free.Length == 0) break;
            var r = rows[ordered.i];
            rows[ordered.i] = (r.Index, r.State, r.Selectable, free[0], r.DefaultOn);
            taken.Add(free[0]);
        }

        var result = new List<TemplateRow>();
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            var item = Items[r.Index];
            bool on = r.State == TemplateRowState.Added ? false
                : pick?.TryGetValue(i, out var chosen) == true ? chosen && r.Selectable
                : r.DefaultOn && r.Selectable;
            result.Add(new TemplateRow(
                DisplayName(item, english), item.Pattern, item.Shortcut, r.Pattern, r.State, on, r.Selectable, item.Recommended));
        }
        return result;
    }
}
