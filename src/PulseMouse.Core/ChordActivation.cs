namespace PulseMouse.Core;

/// <summary>What the hook should do with the current event.</summary>
public enum ChordStep
{
    /// <summary>Let the event through untouched.</summary>
    Pass,
    /// <summary>Swallow the event; nothing else.</summary>
    Swallow,
    /// <summary>Let the current event through, but first replay the held trigger press; the chord is abandoned.</summary>
    ReplayDown,
    /// <summary>Swallow the event and replay a full trigger click (the trigger was tapped without rhythm input).</summary>
    ReplayClick,
    /// <summary>Swallow; a chord started and this is its first pulse press.</summary>
    Begin,
    /// <summary>Swallow; rhythm pulse press.</summary>
    PulseDown,
    /// <summary>Swallow; rhythm pulse release.</summary>
    PulseUp,
    /// <summary>Swallow; the trigger was released, commit the sequence now.</summary>
    Commit,
    /// <summary>Swallow; rhythm pulse release, then commit (trigger was released first).</summary>
    PulseUpCommit,
    /// <summary>Swallow; discard the sequence.</summary>
    Cancel,
}

/// <summary>
/// Pure, deterministic "hold trigger + tap rhythm with left button" gate; all times are monotonic ms.
/// The trigger press is held back until it is clear whether it starts a chord, so a plain tap,
/// drag or long press keeps its native behaviour through replay.
/// </summary>
public sealed class ChordActivation
{
    private enum State { Idle, Pending, Chording, Releasing, Cancelled, Passthrough }

    private State _state;
    private long _downAt;
    private int _x, _y;
    private bool _leftSwallowed;

    public int MovementTolerancePx { get; set; } = 16;
    /// <summary>A still, held trigger turns back into a native press after this long; generous so the rhythm hint can be read.</summary>
    public int HoldTimeoutMs { get; set; } = 8000;
    /// <summary>Safety net in case the trigger release is never observed.</summary>
    public int MaxChordMs { get; set; } = 10000;

    /// <summary>A rhythm is being entered; pauses must not complete it.</summary>
    public bool IsChording => _state is State.Chording or State.Releasing;
    public bool IsIdle => _state == State.Idle && !_leftSwallowed;
    /// <summary>Where the held trigger was pressed; replays happen here.</summary>
    public int OriginX => _x;
    public int OriginY => _y;
    public long PressedAtMs => _downAt;

    public ChordStep TriggerDown(long ms, int x, int y, bool leftHeld)
    {
        if (_state != State.Idle) return ChordStep.Pass;
        _downAt = ms; _x = x; _y = y;
        if (leftHeld) { _state = State.Passthrough; return ChordStep.Pass; }
        _state = State.Pending;
        return ChordStep.Swallow;
    }

    public ChordStep TriggerUp()
    {
        switch (_state)
        {
            case State.Pending: _state = State.Idle; return ChordStep.ReplayClick;
            case State.Chording when _leftSwallowed: _state = State.Releasing; return ChordStep.Swallow;
            case State.Chording: _state = State.Idle; return ChordStep.Commit;
            case State.Cancelled: _state = State.Idle; return ChordStep.Swallow;
            case State.Passthrough: _state = State.Idle; return ChordStep.Pass;
            default: return ChordStep.Pass;
        }
    }

    public ChordStep LeftDown()
    {
        switch (_state)
        {
            case State.Pending: _state = State.Chording; _leftSwallowed = true; return ChordStep.Begin;
            case State.Chording: _leftSwallowed = true; return ChordStep.PulseDown;
            case State.Cancelled: _leftSwallowed = true; return ChordStep.Swallow;
            default: return ChordStep.Pass;
        }
    }

    public ChordStep LeftUp()
    {
        if (!_leftSwallowed) return ChordStep.Pass;
        _leftSwallowed = false;
        switch (_state)
        {
            case State.Chording: return ChordStep.PulseUp;
            case State.Releasing: _state = State.Idle; return ChordStep.PulseUpCommit;
            default: return ChordStep.Swallow;
        }
    }

    public ChordStep Move(int x, int y)
    {
        if (_state != State.Pending) return ChordStep.Pass;
        long dx = (long)x - _x, dy = (long)y - _y;
        if (dx * dx + dy * dy <= (long)MovementTolerancePx * MovementTolerancePx) return ChordStep.Pass;
        _state = State.Passthrough;
        return ChordStep.ReplayDown;
    }

    public ChordStep Wheel()
    {
        switch (_state)
        {
            case State.Pending: _state = State.Passthrough; return ChordStep.ReplayDown;
            case State.Chording: _state = State.Cancelled; return ChordStep.Cancel;
            case State.Releasing: _state = State.Idle; return ChordStep.Cancel;
            case State.Cancelled: return ChordStep.Swallow;
            default: return ChordStep.Pass;
        }
    }

    public ChordStep Tick(long ms)
    {
        if (_state == State.Pending && ms - _downAt >= HoldTimeoutMs)
        {
            _state = State.Passthrough;
            return ChordStep.ReplayDown;
        }
        if (_state != State.Idle && ms - _downAt > MaxChordMs)
        {
            bool wasChording = IsChording;
            _state = State.Idle;
            return wasChording ? ChordStep.Cancel : ChordStep.Pass;
        }
        return ChordStep.Pass;
    }

    /// <summary>Discard the rhythm but keep swallowing until the trigger is released.</summary>
    public void Cancel()
    {
        if (_state == State.Chording) _state = State.Cancelled;
        else if (_state == State.Releasing) _state = State.Idle;
    }

    public void Reset() { _state = State.Idle; _leftSwallowed = false; }
}
