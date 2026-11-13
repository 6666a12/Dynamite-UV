using Godot;

namespace DynamiteUniverse.Ui;

internal sealed class SongListViewport
{
    private const float TouchScrollDeadzone = 12f;
    private readonly ScrollContainer _scroll;
    private readonly Func<Rect2> _listRect;
    private readonly Func<double> _tau;
    private readonly Action<Vector2> _selectAt;
    private readonly SongListScrollState _state = new();
    private int? _touchId;
    private float _touchStartY;
    private int _touchStartOffset;
    private bool _touchMoved;

    public SongListViewport(ScrollContainer scroll, Func<Rect2> listRect, Func<double> tau,
        Action<Vector2> selectAt)
    {
        _scroll = scroll;
        _listRect = listRect;
        _tau = tau;
        _selectAt = selectAt;
    }

    public void Step(double delta)
    {
        if (!_state.Step(_scroll.ScrollVertical, _tau(), delta))
            return;
        Write();
    }

    public bool HandleInput(InputEvent e)
    {
        if (HandleListScrollInput(e))
            return true;

        switch (e)
        {
            case InputEventScreenTouch touch when touch.Pressed &&
                _touchId is null && _listRect().HasPoint(touch.Position):
                _touchId = touch.Index;
                _touchStartY = touch.Position.Y;
                _touchStartOffset = _scroll.ScrollVertical;
                _touchMoved = false;
                StopInertia();
                GetViewport().SetInputAsHandled();
                return true;
            case InputEventScreenDrag drag when _touchId == drag.Index:
            {
                var delta = drag.Position.Y - _touchStartY;
                if (Math.Abs(delta) >= TouchScrollDeadzone)
                    _touchMoved = true;
                if (_touchMoved)
                    _scroll.ScrollVertical = _touchStartOffset - Mathf.RoundToInt(delta);
                GetViewport().SetInputAsHandled();
                return true;
            }
            case InputEventScreenTouch touch when !touch.Pressed && _touchId == touch.Index:
                if (!touch.Canceled && !_touchMoved && _listRect().HasPoint(touch.Position))
                    _selectAt(touch.Position);
                _touchId = null;
                _touchMoved = false;
                GetViewport().SetInputAsHandled();
                return true;
            default:
                return false;
        }
    }

    public void StopInertia() => _state.Stop(_scroll.ScrollVertical);

    private bool HandleListScrollInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel
                when _listRect().HasPoint(wheel.Position):
            {
                var up = wheel.ButtonIndex == MouseButton.WheelUp;
                Push(WheelScrollPixels((up ? -1f : 1f) * wheel.Factor));
                GetViewport().SetInputAsHandled();
                return true;
            }
            case InputEventPanGesture pan when _listRect().HasPoint(pan.Position):
                Push(WheelScrollPixels(pan.Delta.Y));
                GetViewport().SetInputAsHandled();
                return true;
            default:
                return false;
        }
    }

    private void Push(float pixels)
    {
        if (_state.Push(_scroll.ScrollVertical, MaxScroll(), pixels, _tau()))
            Write();
    }

    private float WheelScrollPixels(float factor) =>
        (float)(_scroll.GetVScrollBar().Page / 8.0) * factor;

    private float MaxScroll()
    {
        var bar = _scroll.GetVScrollBar();
        return Mathf.Max(0f, (float)(bar.MaxValue - bar.Page));
    }

    private void Write()
    {
        var wanted = Mathf.RoundToInt(_state.Display);
        _scroll.ScrollVertical = wanted;
        _state.AcceptWrite(wanted, _scroll.ScrollVertical);
    }

    private Viewport GetViewport() => _scroll.GetViewport();
}
