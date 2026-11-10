using PulseMouse.Core;
using System.Text.Json;
static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
var settings = new AppSettings();
var engine = new RhythmEngine(() => settings);
Recognition? result = null;
engine.Completed += r => result = r;
engine.Down(1000); engine.Up(1100); engine.Down(1250); engine.Up(1350); engine.Tick(2100);
Check(result is { Pattern: "..", Command: null }, "two dots emit pattern; resolve is outside the engine");
result = null;
engine.Down(3000); engine.Up(3100); engine.Down(3300); engine.Up(4000); engine.Tick(4800);
Check(result is { Pattern: ".-" }, "dot dash");
result = null;
engine.Down(5000); engine.Up(5400); engine.Tick(6300);
Check(result is null && engine.Sequence == "", "ambiguous pulse rejected");
engine.Down(7000); engine.Up(7100); engine.Tick(7900);
Check(result is { Pattern: ".", Command: null }, "unknown mapping safe no-op");
result = null;
engine.Down(9000); engine.Up(9100); engine.Tick(9850);
engine.Down(10000); engine.Up(10100); engine.Tick(10900);
Check(result is { Pattern: "." }, "gap completes separate sequences");
Console.WriteLine("All smoke tests passed.");

var gate = new MiddleActivation();
gate.Down(1000, 10, 10);
Check(!gate.Up(1300) && !gate.Armed, "short middle click doesn't arm");
gate.Down(2000, 10, 10); gate.Move(80, 10);
Check(!gate.Up(2700) && !gate.Armed, "middle drag doesn't arm");
gate.Down(3000, 10, 10);
Check(gate.Up(3600) && gate.Armed, "stationary middle hold arms");
Check(!gate.Expire(4000, false), "active window retained");
Check(gate.Expire(10601, false) && !gate.Armed, "idle input window expires");
gate.Down(11000, 10, 10); gate.Up(11600);
Check(!gate.Expire(19000, true), "held left button prevents timeout");
gate.Extend(19000, 2000);
Check(!gate.Expire(20000, false), "left release extends time");
gate.Disarm(); Check(!gate.Armed, "cancel clears armed state");
Console.WriteLine("Middle activation tests passed.");

var mid = new MiddleDecision();
Check(mid.Down(0, 10, 10, classicMiddle: false, allowGesture: true) == MiddleDecisionStep.Swallow
      && mid.Up(200) == MiddleDecisionStep.ReplayClick && mid.IsIdle, "stationary short middle click replays");
mid.Down(0, 10, 10, false, true);
Check(mid.Move(90, 10) == MiddleDecisionStep.Swallow && mid.Up(300) == MiddleDecisionStep.Gesture, "drag reaches gesture distance");
mid.Down(0, 10, 10, classicMiddle: true, allowGesture: true);
Check(mid.Tick(700) == MiddleDecisionStep.Pass && mid.Up(600) == MiddleDecisionStep.Arm, "classic middle still hold arms");
mid.Down(0, 10, 10, false, true);
Check(mid.Tick(500) == MiddleDecisionStep.Pass && mid.Tick(600) == MiddleDecisionStep.ReplayDown && mid.IsPassthrough
      && mid.Up(800) == MiddleDecisionStep.Pass, "chord mode still hold passes through");
mid.Down(0, 10, 10, false, true); mid.Move(90, 12);
Check(mid.Tick(700) == MiddleDecisionStep.Pass && mid.Up(800) == MiddleDecisionStep.Gesture, "gesture wins over pass-through timer");
mid.Down(0, 10, 10, false, true); mid.Move(30, 10);
Check(mid.Up(200) == MiddleDecisionStep.Cancel, "wiggle under gesture distance cancels");
mid.Down(0, 10, 10, false, true);
Check(mid.Up(450) == MiddleDecisionStep.Cancel, "hold between click max and pass-through cancels");
mid.PassThroughMs = 0;
mid.Down(0, 10, 10, false, true);
Check(mid.Tick(800) == MiddleDecisionStep.Pass && mid.Up(800) == MiddleDecisionStep.Cancel, "pass-through 0 keeps still long press as cancel");
mid.PreserveMiddleClick = false; mid.PassThroughMs = 600;
mid.Down(0, 10, 10, false, true);
Check(mid.Up(100) == MiddleDecisionStep.Cancel, "preserve off skips short click replay");
Check(MiddleDecision.NormalizePassThroughMs(500, 400) == 500 && MiddleDecision.NormalizePassThroughMs(300, 400) == 401
      && MiddleDecision.NormalizePassThroughMs(0, 400) == 0, "pass-through normalization");
Console.WriteLine("Middle decision tests passed.");

var gesture=new GestureRecognizer(75);
gesture.Start(100,100); Check(gesture.End(180,102)=="Right", "right gesture");
gesture.Start(100,100); Check(gesture.End(98,0)=="Up", "up gesture");
gesture.Start(100,100); Check(gesture.End(120,125) is null, "small movement rejected");
gesture.Start(100,100); Check(gesture.End(180,180) is null, "diagonal rejected");
gesture.Start(100,100); gesture.Cancel(); Check(gesture.End(200,100) is null, "cancel gesture");
Console.WriteLine("Gesture tests passed.");

var chord = new ChordActivation();
Check(chord.TriggerDown(0, 10, 10, false) == ChordStep.Swallow && chord.TriggerUp() == ChordStep.ReplayClick && chord.IsIdle, "right tap replays a click");
chord.TriggerDown(0, 10, 10, false);
Check(chord.Move(12, 13) == ChordStep.Pass && chord.Move(30, 10) == ChordStep.ReplayDown && chord.TriggerUp() == ChordStep.Pass, "right drag replays press and passes through");
chord.TriggerDown(1000, 10, 10, false);
Check(chord.Tick(8900) == ChordStep.Pass && chord.Tick(9000) == ChordStep.ReplayDown && chord.LeftDown() == ChordStep.Pass && chord.TriggerUp() == ChordStep.Pass, "long right hold replays press");
chord.Reset();
Check(chord.TriggerDown(0, 10, 10, true) == ChordStep.Pass && chord.TriggerUp() == ChordStep.Pass, "right press while left held is untouched");
chord.TriggerDown(0, 10, 10, false);
Check(chord.LeftDown() == ChordStep.Begin && chord.IsChording && chord.LeftUp() == ChordStep.PulseUp
      && chord.LeftDown() == ChordStep.PulseDown && chord.LeftUp() == ChordStep.PulseUp
      && chord.TriggerUp() == ChordStep.Commit && chord.IsIdle, "chord taps then release commits");
chord.TriggerDown(0, 10, 10, false);
Check(chord.LeftDown() == ChordStep.Begin && chord.TriggerUp() == ChordStep.Swallow && chord.LeftUp() == ChordStep.PulseUpCommit && chord.IsIdle, "trigger released first commits after pulse");
chord.TriggerDown(0, 10, 10, false); chord.LeftDown(); chord.LeftUp();
Check(chord.Move(300, 300) == ChordStep.Pass && chord.IsChording, "movement during chord keeps rhythm");
Check(chord.Wheel() == ChordStep.Cancel && chord.LeftDown() == ChordStep.Swallow && chord.LeftUp() == ChordStep.Swallow && chord.TriggerUp() == ChordStep.Swallow && chord.IsIdle, "wheel cancels and swallows until release");
chord.TriggerDown(0, 10, 10, false); chord.LeftDown();
Check(chord.Tick(10001) == ChordStep.Cancel && !chord.IsChording && chord.LeftUp() == ChordStep.Swallow && chord.IsIdle, "stuck chord times out");
Check(chord.LeftDown() == ChordStep.Pass && chord.LeftUp() == ChordStep.Pass && chord.TriggerUp() == ChordStep.Pass, "idle chord passes everything");

result = null;
engine.GapSuspended = true;
engine.Down(20000); engine.Up(20100); engine.Tick(22000);
engine.Down(22100); engine.Up(22200); engine.Tick(25000);
Check(result is null && engine.Sequence == "..", "suspended gap waits for commit");
engine.GapSuspended = false; engine.Commit();
Check(result is { Pattern: ".." }, "commit completes immediately");
result = null; engine.Commit();
Check(result is null, "empty commit is a no-op");
Console.WriteLine("Chord activation tests passed.");

var hintCommands = new[] { "-.", ".-", "..", "...", ".-.", "" }.Select(p => new CommandBinding { Name = p, Pattern = p }).ToList();
var (shown, hidden) = RhythmHints.For(hintCommands, "", 3);
Check(shown.Select(c => c.Pattern).SequenceEqual(new[] { "..", ".-", "-." }) && hidden == 2, "hints: PatternOrder (length then · before —), empty skipped, overflow counted");
(shown, hidden) = RhythmHints.For(hintCommands, ".-", 6);
Check(shown.Select(c => c.Pattern).SequenceEqual(new[] { ".-", ".-." }) && hidden == 0, "hints filter by typed prefix");
Check(RhythmHints.For(hintCommands, "--", 6).Shown.Count == 0, "hints: no reachable command");
Console.WriteLine("Rhythm hint tests passed.");

Check(KeyChord.TryParse("ctrl + shift + t", out var keys) && keys.ToString() == "Ctrl+Shift+T" && keys.Key == 'T', "shortcut parses case-insensitively and formats canonically");
Check(KeyChord.TryParse("Shift+Win+d", out keys) && keys.ToString() == "Shift+Win+D", "modifiers are reordered");
Check(KeyChord.TryParse("VolumeUp", out keys) && keys.Modifiers.Count == 0 && keys.Key == 0xAF, "media key without modifiers");
Check(KeyChord.TryParse("Ctrl++", out keys) && keys.ToString() == "Ctrl+=", "plus key");
Check(KeyChord.TryParse("Alt+F4", out keys) && keys.Key == 0x73 && KeyChord.TryParse("Control+PgDn", out keys) && keys.ToString() == "Ctrl+PageDown", "function keys and aliases");
Check(!KeyChord.TryParse("Ctrl+Ctrl+C", out _) && !KeyChord.TryParse("Ctrl", out _) && !KeyChord.TryParse("Hyper+C", out _) && !KeyChord.TryParse(" ", out _), "invalid shortcuts rejected");
Console.WriteLine("Shortcut tests passed.");

var v07 = new AppSettings
{
    StartupEnabled = true,
    SuccessSound = true,
    UiLanguage = "en-US",
    CapsulePosition = "Right",
    CapsuleBreathing = false,
    ExecutionsToday = 12,
    ActiveSecondsThisWeek = 7200
};
var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(v07));
Check(roundTrip is { StartupEnabled: true, SuccessSound: true, UiLanguage: "en-US", CapsulePosition: "Right", CapsuleBreathing: false, ExecutionsToday: 12 }, "v0.7 settings round trip");
var legacy = JsonSerializer.Deserialize<AppSettings>("{\"Enabled\":true,\"Commands\":[]}");
Check(legacy is { Enabled: true, UiLanguage: "zh-CN", CapsulePosition: "Center", CapsuleBreathing: true }, "legacy settings receive v0.7 defaults");
Check(legacy is { HesitationHints: true, PauseInFullscreen: true, DetectedSideButton: 0, ExcludedApps.Count: 0 }, "legacy settings receive v0.8 interception defaults");
Console.WriteLine("V0.7 settings compatibility tests passed.");

// ---- v0.9 Context Pulse (M2) ----
CommandBinding C(string id, string pattern, string scope = "", bool pinned = false, string name = "", ActionKind kind = ActionKind.Shortcut, string target = "Ctrl+C") =>
    new() { Id = id, Name = name.Length > 0 ? name : id, Pattern = pattern, Scope = scope, Pinned = pinned, ActionKind = kind, Target = target };

var cmds = new List<CommandBinding>
{
    C("g1", ".-", "", pinned: false, name: "复制", target: "Ctrl+C"),
    C("g2", "-.", "", pinned: true, name: "切换窗口", target: "Alt+Tab"),
    C("c1", ".-", "chrome", name: "后退", target: "Alt+Left"),
    C("e1", ".-", "excel", name: "插入日期", target: "Ctrl+;"),
};
Check(cmds.Resolve(".-", "chrome", true)?.Id == "c1", "resolve: program overrides unpinned global");
Check(cmds.Resolve(".-", "notepad", true)?.Id == "g1", "resolve: fall back to global");
Check(cmds.Resolve("-.", "chrome", true)?.Id == "g2", "resolve: pinned global wins over missing program");
Check(cmds.Resolve("...", "chrome", true) is null, "resolve: miss");
Check(cmds.Resolve(".-", "chrome", false)?.Id == "g1", "resolve: context off uses global only");
Check(cmds.Resolve(".-", ContextNames.Desktop, true)?.Id == "g1", "resolve: desktop sentinel ignores program scopes");
Check(cmds.Resolve(".-", "explorer", true)?.Id == "g1", "resolve: explorer without own binding falls to global");
cmds.Add(C("x1", ".-", "explorer", name: "刷新", target: "F5"));
Check(cmds.Resolve(".-", "explorer", true)?.Id == "x1", "resolve: explorer file window can own a binding");
Check(cmds.Resolve(".-", ContextNames.Desktop, true)?.Id == "g1", "resolve: desktop still ignores explorer scope");

var (reach, hide) = cmds.Reachable("chrome", true, "", 6);
Check(reach.Any(c => c.Id == "c1") && !reach.Any(c => c.Id == "g1") && hide >= 0, "reachable: chrome shows own .- not global");
Check(ContextResolver.Classify(cmds.Own("chrome", ".-"), "chrome") == MeaningSource.Program, "classify: program");
Check(ContextResolver.Classify(cmds.Global("-."), "chrome") == MeaningSource.Pinned, "classify: pinned");
Check(ContextResolver.Classify(cmds.Global(".-"), "notepad") == MeaningSource.Global, "classify: global");
Console.WriteLine("Context resolve tests passed.");

Check(PinPolicy.ShouldAutoPin(ActionKind.Application, "notepad.exe"), "auto-pin: application");
Check(PinPolicy.ShouldAutoPin(ActionKind.Shortcut, "Win+D"), "auto-pin: system shortcut");
Check(!PinPolicy.ShouldAutoPin(ActionKind.Shortcut, "Ctrl+C"), "auto-pin: in-app shortcut stays free");
var wf = C("w1", ".", kind: ActionKind.Workflow, target: "[]"); wf.Scope = "chrome";
PinPolicy.Apply(wf);
Check(wf.Scope == "" && wf.Pinned, "auto-pin: workflow forced global+pinned");
var manual = C("m1", "..", kind: ActionKind.Application, target: "calc.exe"); manual.PinnedManually = true; manual.Pinned = false;
PinPolicy.Apply(manual);
Check(!manual.Pinned, "auto-pin: manual toggle is sticky");
Console.WriteLine("Pin policy tests passed.");

var edit = new List<CommandBinding> { C("a", ".-", "", name: "复制"), C("b", ".-", "chrome", name: "后退") };
Check(edit.Check(".-", "chrome", "b", false).Issue == PatternIssue.None || edit.Check(".-", "chrome", "b", false).Issue == PatternIssue.ReplacesGlobal, "validate: editing own chrome binding ok");
Check(edit.Check(".-", "chrome", null, false).Issue == PatternIssue.DuplicateInScope, "validate: duplicate in scope");
Check(edit.Check("-.", "chrome", null, false, ActionKind.Shortcut).Issue is PatternIssue.None or PatternIssue.ReplacesGlobal, "validate: free or replace");
var pinnedGlobal = new List<CommandBinding> { C("p", "-.", "", pinned: true, name: "切换") };
Check(pinnedGlobal.Check("-.", "chrome", null, false).Issue == PatternIssue.PinnedGlobal, "validate: pinned blocks program");
Check(edit.Check(".-", "", "a", true).Issue == PatternIssue.GlobalPinnedByOthers, "validate: pin global with replacers");
Check(edit.Check("", "", null, false).Issue == PatternIssue.Empty, "validate: empty");
Check(edit.Check(".", "chrome", null, false, ActionKind.Workflow).Issue == PatternIssue.WorkflowOrRepeatScoped, "validate: workflow cannot be scoped");
var rec = edit.Recommend("chrome", null, null);
Check(rec.Count == 3 && rec.All(p => p.Length >= 1) && !rec.Contains(".-"), "recommend: 3 free patterns excluding used");
Console.WriteLine("Pattern rules tests passed.");

var emptyChrome = new List<CommandBinding>();
var plan = emptyChrome.Plan("chrome", english: false);
Check(plan.Count(r => r.Checked) == 3 && plan.Where(r => r.Checked).All(r => r.Recommended), "template: default-check only recommended");
Check(plan.First(r => r.Name == "新标签页").Checked == false, "template: new tab never default-checked");
var withChrome = new List<CommandBinding>
{
    C("t1", ".-", "chrome", name: "后退", target: "Alt+Left"),
    C("t2", "...", "chrome", name: "刷新", target: "F5"),
    C("t3", ".--", "chrome", name: "重新打开标签页", target: "Ctrl+Shift+T"),
};
plan = withChrome.Plan("chrome", false);
Check(plan.Count(r => r.State == TemplateRowState.Added) == 3, "template: existing rows marked Added");
var edgePick = new Dictionary<int, bool> { [3] = true }; // 前进
plan = emptyChrome.Plan("msedge", false, edgePick);
Check(plan[3].Checked && plan[3].State is TemplateRowState.Free or TemplateRowState.Move, "template: checking Forward allocates");
Console.WriteLine("Template catalog tests passed.");

var heatSettings = new AppSettings
{
    CommandsToday = new Dictionary<string, int> { ["id1"] = 2 },
    History =
    [
        new DailyStat { Date = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), Commands = new() { ["id1"] = 3 } },
        new DailyStat { Date = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd"), Commands = new() { ["id1"] = 1 } },
    ]
};
var week = CommandHeat.Series(heatSettings, "id1", DateTime.Today);
Check(week.Length == 7 && week[6] == 2 && week[5] == 3 && week[3] == 1 && CommandHeat.Total(week) == 6, "heat: 7-day series by id");
Console.WriteLine("Command heat tests passed.");

var v8 = new AppSettings
{
    SchemaVersion = 8,
    Commands = [new CommandBinding { Name = "打开记事本", Pattern = "..", ActionKind = ActionKind.Application, Target = "notepad.exe" }],
    CommandsToday = new Dictionary<string, int> { ["打开记事本"] = 4 },
    History = [new DailyStat { Date = "2026-01-01", Commands = new() { ["打开记事本"] = 2 } }],
};
SettingsMigrator.MigrateFromV8(v8);
Check(v8.SchemaVersion == 9 && v8.Commands[0].Id.Length > 0 && v8.Commands[0].Pinned, "migrate: id + auto-pin app");
Check(v8.CommandsToday.ContainsKey(v8.Commands[0].Id) && !v8.CommandsToday.ContainsKey("打开记事本"), "migrate: stats key renamed to id");
Check(v8.History[0].Commands.ContainsKey(v8.Commands[0].Id), "migrate: history stats renamed");
Check(v8.MiddleClickMaxMs == 400 && v8.MiddlePassThroughMs == 600 && v8.PreserveMiddleClick, "migrate: middle defaults");
Check(v8.Commands[0].Pattern == "..", "migrate: pattern unchanged");
Console.WriteLine("Settings migration tests passed.");

var miss = ExecutionResult.Miss(".-", ContextSnapshot.Desktop);
Check(!miss.Ok && miss.FailReason == ExecutionFailReason.Miss, "exec result: miss");
var req = new ExecutionRequest(ExecutionSource.Rhythm, ".-", ContextSnapshot.Desktop, C("x", ".-"), MeaningSource.Global, true);
Check(ExecutionResult.Fail(ExecutionFailReason.WindowGone, "目标窗口已关闭", req).FailReason == ExecutionFailReason.WindowGone, "exec result: window gone");
Check(ExecutionResult.Fail(ExecutionFailReason.NothingToRepeat, "还没有可重复的动作", req).FailReason == ExecutionFailReason.NothingToRepeat, "exec result: nothing to repeat");
Check(ExecutionResult.Fail(ExecutionFailReason.ProcessMismatch, "原窗口已关闭", req).FailReason == ExecutionFailReason.ProcessMismatch, "exec result: process mismatch");
Console.WriteLine("Execution result tests passed.");
Console.WriteLine("All v0.9 M2 smoke tests passed.");
