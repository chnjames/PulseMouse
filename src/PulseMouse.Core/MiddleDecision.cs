namespace PulseMouse.Core;

/// <summary>What the hook should do with a middle-button event under the v0.9 delay decision.</summary>
public enum MiddleDecisionStep
{
    /// <summary>Let the event through untouched.</summary>
    Pass,
    /// <summary>Swallow the event; keep holding the press back.</summary>
    Swallow,
    /// <summary>Swallow the up and inject a middle click (short stationary tap).</summary>
    ReplayClick,
    /// <summary>Inject a middle press at the origin, then pass subsequent events through.</summary>
    ReplayDown,
    /// <summary>Swallow the up and arm classic middle rhythm input.</summary>
    Arm,
    /// <summary>Swallow the up and fire a direction gesture (hook reads the recognizer).</summary>
    Gesture,
    /// <summary>Swallow the up and do nothing (cancelled hold).</summary>
    Cancel,
}

/// <summary>
/// Pure middle-button delay decision (spec §5.16): short click, drag gesture, hold-still pass-through,
/// or classic middle arm. All times are monotonic ms.
/// </summary>
public sealed class MiddleDecision
{
    private enum State { Idle, Holding, GesturePending, Wiggled, Passthrough }

    private State _state;
    private long _downAt;
    private int _x, _y;
    private bool _classicMiddle;
    private bool _allowGesture;

    public int StillTolerancePx { get; set; } = 12;
    public int GestureMinDistancePx { get; set; } = 75;
    public int ClickMaxMs { get; set; } = 400;
    /// <summary>0 disables hold-still pass-through (chord/auto only).</summary>
    public int PassThroughMs { get; set; } = 600;
    public int ArmHoldMs { get; set; } = 550;
    public bool PreserveMiddleClick { get; set; } = true;

    public bool IsIdle => _state == State.Idle;
    public bool IsHolding => _state is State.Holding or State.GesturePending or State.Wiggled;
    public bool IsPassthrough => _state == State.Passthrough;
    public int OriginX => _x;
    public int OriginY => _y;

    /// <summary>
    /// Begin a held middle press. <paramref name="classicMiddle"/> enables arm-on-long-hold and disables pass-through.
    /// <paramref name="allowGesture"/> allows crossing the gesture distance to become a direction gesture.
    /// </summary>
    public MiddleDecisionStep Down(long ms, int x, int y, bool classicMiddle, bool allowGesture)
    {
        if (_state != State.Idle) return MiddleDecisionStep.Pass;
        _downAt = ms;
        _x = x;
        _y = y;
        _classicMiddle = classicMiddle;
        _allowGesture = allowGesture;
        _state = State.Holding;
        return MiddleDecisionStep.Swallow;
    }

    public MiddleDecisionStep Move(int x, int y)
    {
        if (_state is not (State.Holding or State.GesturePending or State.Wiggled))
            return MiddleDecisionStep.Pass;

        long dx = (long)x - _x, dy = (long)y - _y;
        long dist2 = dx * dx + dy * dy;
        long gesture2 = (long)GestureMinDistancePx * GestureMinDistancePx;
        long still2 = (long)StillTolerancePx * StillTolerancePx;

        if (_allowGesture && dist2 >= gesture2)
        {
            _state = State.GesturePending;
            return MiddleDecisionStep.Swallow;
        }
        if (dist2 > still2)
            _state = State.Wiggled;
        return MiddleDecisionStep.Swallow;
    }

    /// <summary>While holding in chord/auto mode, a still press past <see cref="PassThroughMs"/> hands the button to the system.</summary>
    public MiddleDecisionStep Tick(long ms)
    {
        if (_state != State.Holding) return MiddleDecisionStep.Pass;
        if (_classicMiddle) return MiddleDecisionStep.Pass;
        if (PassThroughMs <= 0) return MiddleDecisionStep.Pass;
        if (ms - _downAt < PassThroughMs) return MiddleDecisionStep.Pass;
        _state = State.Passthrough;
        return MiddleDecisionStep.ReplayDown;
    }

    public MiddleDecisionStep Up(long ms)
    {
        switch (_state)
        {
            case State.Passthrough:
                _state = State.Idle;
                return MiddleDecisionStep.Pass;
            case State.GesturePending:
                _state = State.Idle;
                return MiddleDecisionStep.Gesture;
            case State.Holding:
            {
                long held = ms - _downAt;
                _state = State.Idle;
                if (held < ClickMaxMs)
                    return PreserveMiddleClick ? MiddleDecisionStep.ReplayClick : MiddleDecisionStep.Cancel;
                if (_classicMiddle && held >= ArmHoldMs)
                    return MiddleDecisionStep.Arm;
                return MiddleDecisionStep.Cancel;
            }
            case State.Wiggled:
                _state = State.Idle;
                return MiddleDecisionStep.Cancel;
            default:
                return MiddleDecisionStep.Pass;
        }
    }

    public void Reset()
    {
        _state = State.Idle;
        _classicMiddle = false;
        _allowGesture = false;
    }

    /// <summary>Clamp pass-through so it stays above the click max when enabled.</summary>
    public static int NormalizePassThroughMs(int passThroughMs, int clickMaxMs)
    {
        if (passThroughMs <= 0) return 0;
        return Math.Max(passThroughMs, clickMaxMs + 1);
    }
}
