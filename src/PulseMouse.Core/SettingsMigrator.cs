namespace PulseMouse.Core;

/// <summary>v8 → v9 migration (additive only): ids, scope, pin, remind, stats by id. Never changes a pattern or target.</summary>
public static class SettingsMigrator
{
    public static AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion >= AppSettings.CurrentSchemaVersion)
        {
            EnsureV9Defaults(settings);
            return settings;
        }
        return MigrateFromV8(settings);
    }

    public static AppSettings MigrateFromV8(AppSettings settings)
    {
        settings.Commands ??= new();
        settings.Gestures ??= new();
        settings.ExcludedApps ??= new();
        settings.CommandsToday ??= new();
        settings.History ??= new();
        foreach (var command in settings.Commands)
        {
            command.Scope ??= "";
            command.Id ??= "";
            if (command.Id.Length == 0) command.Id = Guid.NewGuid().ToString("N");
            command.LastUsed ??= "";
            command.LastContext ??= "";
            command.RemindLeft = 0;
            command.PinnedManually = false;
            PinPolicy.Apply(command);
        }
        RenameStats(settings.CommandsToday, settings.Commands);
        foreach (var day in settings.History) RenameStats(day.Commands ??= new(), settings.Commands);
        EnsureV9Defaults(settings);
        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return settings;
    }

    private static void EnsureV9Defaults(AppSettings settings)
    {
        settings.ContextApps ??= new();
        if (settings.MiddleClickMaxMs is < 200 or > 800) settings.MiddleClickMaxMs = 400;
        settings.MiddlePassThroughMs = MiddleDecision.NormalizePassThroughMs(settings.MiddlePassThroughMs, settings.MiddleClickMaxMs);
    }

    /// <summary>Rename stats keys: a name matching exactly one command becomes its id; duplicates/unknown names stay.</summary>
    public static void RenameStats(Dictionary<string, int> stats, List<CommandBinding> commands)
    {
        if (stats.Count == 0) return;
        List<(string Old, int Value)>? replace = null;
        foreach (var key in stats.Keys.ToArray())
        {
            if (commands.Any(c => c.Id == key)) continue;
            var matches = commands.Where(c => c.Name == key).Take(2).ToList();
            if (matches.Count == 1)
            {
                replace ??= new();
                replace.Add((key, stats[key]));
            }
        }
        if (replace is null) return;
        foreach (var (oldName, value) in replace)
        {
            stats.Remove(oldName);
            stats[commands.First(c => c.Name == oldName).Id] = value;
        }
    }
}

/// <summary>Seven-day usage heat by command id (spec 5.10).</summary>
public static class CommandHeat
{
    public static int[] Series(AppSettings settings, string commandId, DateTime now)
    {
        var week = new int[7];
        var today = now.Date;
        var byDate = settings.History.ToDictionary(h => h.Date, h => h);
        for (int offset = 6; offset >= 0; offset--)
        {
            var date = today.AddDays(-offset);
            int index = 6 - offset;
            if (offset == 0)
                week[index] = settings.CommandsToday.GetValueOrDefault(commandId);
            else if (byDate.TryGetValue(date.ToString("yyyy-MM-dd"), out var day))
                week[index] = day.Commands.GetValueOrDefault(commandId);
        }
        return week;
    }

    public static int Total(int[] week) => week.Sum();
}
