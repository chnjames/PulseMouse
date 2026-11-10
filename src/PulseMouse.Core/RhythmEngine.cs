namespace PulseMouse.Core;
/// <summary>Deterministic timestamp-based recognizer. Call Tick after a pause; ticks are monotonic milliseconds.</summary>
public sealed class RhythmEngine
{
    private readonly Func<AppSettings> _settings;
    private long? _pressedAt;
    private long? _lastRelease;
    private string _sequence = "";
    public string Sequence => _sequence;
    /// <summary>While true a pause never completes the sequence; the caller completes it with <see cref="Commit"/>.</summary>
    public bool GapSuspended { get; set; }
    public event Action<string>? SequenceChanged;
    public event Action<Recognition>? Completed;
    public RhythmEngine(Func<AppSettings> settings) => _settings = settings;
    public void Down(long now)
    {
        if (_pressedAt is not null) return;
        Tick(now);
        _pressedAt = now;
    }
    public void Up(long now)
    {
        if (_pressedAt is not long down) return;
        _pressedAt = null;
        var duration = now - down;
        var s = _settings();
        if (duration < 35 || duration > s.LongMaxMs) { Reset(); return; }
        char? pulse = duration <= s.ShortMaxMs ? '.' : duration >= s.LongMinMs ? '-' : null;
        if (pulse is null) { Reset(); return; }
        _sequence += pulse.Value;
        _lastRelease = now;
        SequenceChanged?.Invoke(_sequence);
        if (_sequence.Length >= s.MaxPulses) Finish();
    }
    public void Tick(long now)
    {
        if (GapSuspended || _pressedAt is not null || _lastRelease is not long end) return;
        if (now - end >= _settings().SequenceGapMs) Finish();
    }
    public void Commit()
    {
        if (_pressedAt is not null) return;
        Finish();
    }
    public void Reset()
    {
        _pressedAt = null;
        _lastRelease = null;
        _sequence = "";
        SequenceChanged?.Invoke("");
    }
    private void Finish()
    {
        var pattern = _sequence;
        Reset();
        if (pattern.Length == 0) return;
        // Engine only emits the rhythm; ContextResolver fills Command on the UI/controller side.
        Completed?.Invoke(new Recognition(pattern, null));
    }
}
