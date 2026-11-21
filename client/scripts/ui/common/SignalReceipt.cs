using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

/// <summary>One short, interruptible indication of an already accepted change. No
/// input, audio, timers or completion callback: this cannot commit a setting or sample.</summary>
internal sealed partial class SignalReceipt : Node2D
{
    private Vector2[] _path = [];
    private float _time, _duration, _length;
    private Color _tint;

    public override void _Ready() => SetProcess(false);
    // Only the preview transport and the calibration meter use the brighter instrument profile.
    private bool _conductor;
    private float _fixedLength;
    internal void Emit(Color tint, params Vector2[] path) => Start(tint, .32f, false, path);
    internal void EmitConductor(Color tint, float seconds, params Vector2[] path) => Start(tint, seconds, true, path);
    private void Start(Color tint, float seconds, bool conductor, Vector2[] path)
    {
        if (path.Length < 2 || !IsVisibleInTree() || MenuContactFeedback.Mode == UiMotionMode.Off) { Clear(); return; }
        _path = path; _tint = tint; _time = _length = 0;
        for (var i=1; i<path.Length; i++) _length += path[i].DistanceTo(path[i-1]);
        _conductor = conductor;
        _fixedLength = _length - path[^2].DistanceTo(path[^1]);
        _duration = MenuContactFeedback.Mode == UiMotionMode.Full ? seconds : conductor ? .18f : .11f;
        SetProcess(true); QueueRedraw();
    }
    // The preview receiver moves with the actual audio head even during this short flight.
    internal void TrackReceiver(Vector2 receiver)
    {
        if (!_conductor || _path.Length < 2) return;
        _path[^1] = receiver;
        _length = _fixedLength + _path[^2].DistanceTo(receiver);
    }
    internal void Cancel() => Clear();
    private void Clear() { _path = []; SetProcess(false); QueueRedraw(); }
    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (!IsVisibleInTree() || _time >= _duration || MenuContactFeedback.Mode == UiMotionMode.Off) Clear();
        else QueueRedraw();
    }
    // At the 1920 design scale the V3 track's 6px bloom / 2.1px core become 9px / 3.15px.
    private void DrawConductor(float t, bool full)
    {
        var hot = _tint.Lerp(Colors.White,.83f);
        if (full)
        {
            float head = _length * Mathf.Min(1, t/.76f);
            float span = Mathf.Min(_length*.40f, 135f);
            float fade = 1-Mathf.SmoothStep(.76f,1,t);
            var tip = At(head);
            for (int i=15;i>=0;i--)
            {
                float f=i/16f, hi=head-span*f; if (hi<=0) continue;
                var a=At(Mathf.Max(0,hi-span/16f)); var b=At(hi);
                if(a.DistanceSquaredTo(b)<.01f) continue;
                var power=fade*(1-f)*(1-f);
                DrawLine(a,b,new Color(_tint,.33f*power),11f,true);
                DrawLine(a,b,new Color(_tint,.95f*power),4.5f,true);
                DrawLine(a,b,new Color(hot,.95f*power),2.2f,true);
            }
            DrawCircle(tip,7.5f,new Color(_tint,.29f*fade),true,-1,true);
            DrawCircle(tip,3.9f,new Color(Colors.White,fade),true,-1,true);
        }
        var powerAtEnd=full ? Mathf.SmoothStep(.64f,.78f,t)*(1-Mathf.SmoothStep(.84f,1,t)) : (1-t)*.78f;
        if(powerAtEnd<.005f) return;
        var q=_path[^1];
        DrawCircle(q,14f,new Color(_tint,.23f*powerAtEnd),true,-1,true);
        DrawLine(q-new Vector2(14,0),q+new Vector2(14,0),new Color(_tint,.82f*powerAtEnd),6,true);
        DrawLine(q-new Vector2(8,0),q+new Vector2(8,0),new Color(Colors.White,powerAtEnd),3,true);
    }
    private Vector2 At(float s)
    {
        for (var i=1;i<_path.Length;i++)
        {
            var len=_path[i-1].DistanceTo(_path[i]);
            if (len < .001f) continue;
            if (s<=len) return _path[i-1].Lerp(_path[i],Mathf.Clamp(s/len,0,1));
            s-=len;
        }
        return _path[^1];
    }
    public override void _Draw()
    {
        if (_path.Length<2) return;
        var t=Mathf.Clamp(_time/_duration,0,1);
        var mode=MenuContactFeedback.Mode;
        if (mode==UiMotionMode.Off) return;
        var full=mode==UiMotionMode.Full;
        if (_conductor) { DrawConductor(t,full); return; }
        var head=_length*Mathf.SmoothStep(0,1,Mathf.Min(t/.73f,1));
        if (full && t<.87f && head>.1f)
        {
            var span=Mathf.Min(_length*.33f,54f);var opacity=1-Mathf.SmoothStep(.68f,.87f,t);
            for (var i=0;i<12;i++)
            {
                var d=head-span*i/12; if(d<=0) break;
                var a=At(d); var b=At(Mathf.Max(0,d-span/12));
                DrawLine(a,b,new Color(_tint,opacity*(1-i/12f)*.81f),2.5f,true);
            }
        }
        var pulse=full?Mathf.SmoothStep(.55f,.76f,t)*(1-Mathf.SmoothStep(.78f,1,t)):(1-t)*.65f;
        if(pulse<=.005f)return;
        var q=_path[^1];DrawRect(new Rect2(q.X-2.5f,q.Y-6,5,12),new Color(_tint,pulse*.7f));
        DrawRect(new Rect2(q.X-1,q.Y-4,2,8),new Color(Colors.White,pulse*.75f));
    }
}
