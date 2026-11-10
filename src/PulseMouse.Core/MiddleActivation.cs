namespace PulseMouse.Core;

/// <summary>Pure, deterministic middle-button activation gate; all times are monotonic ms.</summary>
public sealed class MiddleActivation
{
    private long? _downAt;
    private int _x, _y;
    private bool _moved;
    public bool Armed { get; private set; }
    public bool IsPressingMiddle => _downAt.HasValue;
    public long ArmedUntilMs { get; private set; }
    public int MovementTolerancePx { get; set; } = 12;
    public int MinimumHoldMs { get; set; } = 550;
    public int ArmDurationMs { get; set; } = 7000;

    public void Down(long ms, int x, int y) { _downAt = ms; _x = x; _y = y; _moved = false; Armed = false; }
    public void Move(int x, int y)
    {
        if (!_downAt.HasValue) return;
        long dx = (long)x - _x, dy = (long)y - _y;
        if (dx * dx + dy * dy > (long)MovementTolerancePx * MovementTolerancePx) _moved = true;
    }
    public bool Up(long ms)
    {
        if (_downAt is not long start) return false;
        _downAt = null;
        if (_moved || ms - start < MinimumHoldMs) return false;
        Armed = true;
        ArmedUntilMs = ms + ArmDurationMs;
        return true;
    }
    public bool Expire(long ms, bool leftDown)
    {
        if (!Armed || leftDown || ms <= ArmedUntilMs) return false;
        Armed = false;
        return true;
    }
    public void Extend(long ms, int extensionMs) { if (Armed) ArmedUntilMs = ms + extensionMs; }
    public void Disarm() { Armed = false; _downAt = null; _moved = false; }

    /// <summary>Arm rhythm input after the delay decision already swallowed the hold (v0.9 middle path).</summary>
    public void ForceArm(long ms)
    {
        _downAt = null;
        _moved = false;
        Armed = true;
        ArmedUntilMs = ms + ArmDurationMs;
    }
}
