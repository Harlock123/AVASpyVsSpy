using Avalonia.Input;

namespace AVASpyVsSpy.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, up and down (up the screen is -1), and three buttons.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// The arrows (or W, A, S and D) to walk; Space, Z or Ctrl to search or swing the club; X or Shift to set the chosen
    /// trap; C or Tab to choose the next trap. On a gamepad: the stick, A, B, and Y or a shoulder button.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int dx = (Any(Key.Right, Key.D, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.A, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
            // Up (towards the back wall) is -1.
            int dy = (Any(Key.Down, Key.S, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.W, Key.NumPad8) ? 1 : 0);
            if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.4f ? -1 : pad.DPadDown || pad.LeftStickY > 0.4f ? 1 : 0;
            bool act = Any(Key.Space, Key.Z, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA;
            bool trap = Any(Key.X, Key.LeftShift, Key.RightShift) || pad.ButtonB;
            bool next = Any(Key.C, Key.Tab) || pad.ButtonY || pad.LeftBumper || pad.RightBumper;
            return new Controls(dx, dy, act, trap, next);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
