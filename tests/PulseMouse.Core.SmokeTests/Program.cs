using PulseMouse.Core;
using System.Text.Json;
static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
var settings = new AppSettings();
var engine = new RhythmEngine(() => settings);
Recognition? result = null;
engine.Completed += r => result = r;
engine.Down(1000); engine.Up(1100); engine.Down(1250); engine.Up(1350); engine.Tick(2100);
Check(result is { Pattern: ".." } && result.Value.Command?.Name == "打开记事本", "two dots launch mapping");
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
Check(shown.Select(c => c.Pattern).SequenceEqual(new[] { "-.", ".-", ".." }) && hidden == 2, "hints: shortest first, empty patterns skipped, overflow counted");
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
