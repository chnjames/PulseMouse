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
Console.WriteLine("V0.7 settings compatibility tests passed.");
