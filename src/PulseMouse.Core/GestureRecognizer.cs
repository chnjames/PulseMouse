namespace PulseMouse.Core;
/// <summary>One-stroke direction detection; no Windows dependencies.</summary>
public sealed class GestureRecognizer
{
    private int _x, _y;
    private bool _active;
    private int _minDistance;
    public GestureRecognizer(int minDistance = 75) => _minDistance = Math.Clamp(minDistance, 25, 500);
    public void Configure(int minDistance) => _minDistance = Math.Clamp(minDistance, 25, 500);
    public void Start(int x, int y) { _x=x; _y=y; _active=true; }
    public string? End(int x, int y)
    {
        if (!_active) return null;
        _active=false;
        long dx=(long)x-_x, dy=(long)y-_y;
        if (dx*dx+dy*dy < (long)_minDistance*_minDistance) return null;
        if (Math.Abs(dx)>=Math.Abs(dy)*1.3) return dx>0?"Right":"Left";
        if (Math.Abs(dy)>=Math.Abs(dx)*1.3) return dy>0?"Down":"Up";
        return null; // reject diagonal rather than triggering unintentionally
    }
    public void Cancel() => _active=false;
}
