using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DynamiteUniverse.Editor.Shell;

public sealed record NativeStageDiagnostics(
    long Child, long Parent, long ExpectedParent, bool Embedded, string Bounds, double Scale, string WindowTitle);

public sealed class NativeStageHost : Control
{
    private nint _child;
    private nint _parent;
    private nint _fallbackParent;
    private bool _syncQueued;
    private bool _occluded;

    public event Action? GeometryChanged;

    public void SetGodotHandle(nint value)
    {
        if (_child == value) return;
        if (_child != 0) Win32.ShowWindow(_child, Win32.SwHide);
        _child = value;
        _parent = 0;
        QueueSync();
    }

    public void SetOccluded(bool value)
    {
        if (_occluded == value) return;
        _occluded = value;
        QueueSync();
    }

    public NativeStageDiagnostics Diagnostics()
    {
        var top = TopLevel.GetTopLevel(this);
        var expected = top?.TryGetPlatformHandle()?.Handle ?? 0;
        var actual = _child == 0 ? 0 : Win32.GetParent(_child);
        var bounds = _child != 0 && Win32.GetWindowRect(_child, out var rect)
            ? $"{rect.Right - rect.Left}x{rect.Bottom - rect.Top}" : "none";
        return new NativeStageDiagnostics(_child, actual, expected,
            _child != 0 && actual != 0 && actual == expected, bounds, top?.RenderScaling ?? 1,
            (top as Window)?.Title ?? "detached");
    }

    public void QueueSync()
    {
        if (_syncQueued) return;
        _syncQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncQueued = false;
            SyncNativeWindow();
        }, DispatcherPriority.Render);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        QueueSync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_child != 0 && _fallbackParent != 0)
        {
            Win32.SetParent(_child, _fallbackParent);
            _parent = _fallbackParent;
            Win32.ShowWindow(_child, Win32.SwHide);
        }
        QueueSync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == IsVisibleProperty)
            QueueSync();
    }

    private void SyncNativeWindow()
    {
        if (_child == 0) return;
        var top = TopLevel.GetTopLevel(this);
        var handle = top?.TryGetPlatformHandle()?.Handle ?? 0;
        var point = top is null ? null : Avalonia.VisualExtensions.TranslatePoint(this, new Point(), top);
        if (handle == 0 || point is null || Bounds.Width < 1 || Bounds.Height < 1 || !IsEffectivelyVisible || _occluded)
        {
            Win32.ShowWindow(_child, Win32.SwHide);
            return;
        }
        if (_fallbackParent == 0) _fallbackParent = handle;
        if (_parent != handle)
        {
            Win32.SetParent(_child, handle);
            Win32.ShowWindow(_child, Win32.SwRestore);
            var style = (uint)Win32.GetWindowLongPtr(_child, Win32.GwlStyle).ToInt64();
            style = (style & ~(Win32.WsPopup | Win32.WsCaption | Win32.WsThickFrame))
                    | Win32.WsChild | Win32.WsVisible | Win32.WsClipSiblings;
            Win32.SetWindowLongPtr(_child, Win32.GwlStyle, (nint)(long)style);
            Win32.SetWindowPos(_child, 0, 0, 0, 0, 0,
                Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoZOrder | Win32.SwpFrameChanged);
            _parent = handle;
        }
        var scale = top!.RenderScaling;
        var width = Math.Max(1, (int)Math.Round(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Round(Bounds.Height * scale));
        Win32.MoveWindow(_child, (int)Math.Round(point.Value.X * scale),
            (int)Math.Round(point.Value.Y * scale), width, height, true);
        Win32.ShowWindow(_child, Win32.SwShow);
        GeometryChanged?.Invoke();
    }

    private static class Win32
    {
        public const int GwlStyle = -16;
        public const uint WsPopup = 0x80000000;
        public const uint WsCaption = 0x00C00000;
        public const uint WsThickFrame = 0x00040000;
        public const uint WsChild = 0x40000000;
        public const uint WsVisible = 0x10000000;
        public const uint WsClipSiblings = 0x04000000;
        public const int SwHide = 0;
        public const int SwShow = 5;
        public const int SwRestore = 9;
        public const uint SwpNoSize = 1, SwpNoMove = 2, SwpNoZOrder = 4, SwpFrameChanged = 0x20;

        [DllImport("user32.dll")] public static extern nint SetParent(nint child, nint parent);
        [DllImport("user32.dll")] public static extern nint GetParent(nint child);
        [DllImport("user32.dll")] public static extern bool MoveWindow(nint window, int x, int y, int width, int height, bool repaint);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] public static extern bool ShowWindow(nint window, int command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint window, int index, nint value);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out Rect rect);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    }
}
