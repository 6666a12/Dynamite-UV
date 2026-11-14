using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Compact result upload status; paints vectors rather than relying on emoji font coverage.</summary>
internal sealed partial class ResultUploadControl : Button
{
    private readonly PlayUpload _upload;
    private readonly Func<bool> _enabled;
    private UploadStatus? _last;
    private ResultReauthentication? _login;
    internal ResultUploadControl(PlayUpload upload, Func<bool> enabled)
    {
        _upload = upload; _enabled = enabled; Size = new(76, 64); Position = new(960, 972);
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled" }) AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        Pressed += Click;
    }
    internal bool HasLoginDialog => _login is not null && GodotObject.IsInstanceValid(_login) && !_login.IsQueuedForDeletion();
    private void Click()
    {
        if (!_enabled()) return;
        var code = _upload.State.Code;
        if (_upload.State.RetryAfter is { } after && after > DateTimeOffset.UtcNow) { Explain(); return; }
        if (code == "reauthenticate")
        {
            _ = _upload.AttemptAsync(GameplayOnlineRun.CurrentAccess());
            if (_upload.State.Code != "reauthenticate") return;
            if (_login is not null && GodotObject.IsInstanceValid(_login)) return;
            _login = new ResultReauthentication(_upload.Origin) { ZIndex = 200 };
            GetTree().CurrentScene.AddChild(_login);
        }
        else if (code is "retryable" or "ready") _ = _upload.AttemptAsync(GameplayOnlineRun.CurrentAccess());
        else Explain();
    }
    private void Explain()
    {
        var dialog = new AcceptDialog { DialogText = TooltipText, Title = "成绩上传" };
        AddChild(dialog); dialog.Confirmed += dialog.QueueFree; dialog.Canceled += dialog.QueueFree;
        dialog.PopupCentered(new(620, 180));
    }
    public override void _Process(double delta)
    {
        var state = _upload.State;
        Disabled = !_enabled();
        if (_last == state) return;
        _last = state;
        TooltipText = state.Code switch
        {
            "uploaded" => "已上传：客户端上报记录，未经反作弊认证",
            "uploading" => "正在上传", "ready" or "retryable" => "未确认上传成功，点击手动重试同一结果",
            "reauthenticate" => "需重新登录本局原服务器、原账号；点击登录后返回重试",
            "not_logged_in" => "开局未登录，本局仅保留本地结果", "incompatible" => "服务器成绩协议不兼容",
            "release_unavailable" => "发布已下架或不可用；本地成绩保留，不再重试",
            "content_mismatch" => "谱面内容与服务器发布不符；本地成绩保留，不再重试",
            "ineligible" => "本局不符合上传条件；本地成绩不受影响",
            "conflict" => "结果ID内容冲突，禁止自动换ID重试", "not_accepted" => "该发布已下架、未知或内容身份不符",
            _ => "本局不可上传或服务器拒绝；本地成绩不受影响",
        };
        if (state.RetryAfter is { } after) TooltipText += $"；可重试时间 {after.LocalDateTime:T}";
        QueueRedraw();
    }
    public override void _Draw()
    {
        var code = _upload.State.Code;
        var color = code == "uploaded" ? UiFonts.Casual : code is "retryable" or "reauthenticate" ? UiFonts.Hard : UiFonts.Dim;
        var cloud = new Vector2[] { new(12, 39), new(8, 32), new(12, 23), new(23, 21), new(27, 12),
            new(42, 10), new(52, 21), new(62, 22), new(67, 31), new(62, 39) };
        DrawPolyline(cloud, color, 2, true);
        if (code == "uploaded") DrawPolyline([new(26, 40), new(34, 48), new(51, 30)], color, 3, true);
        else if (code is "uploading" or "retryable" or "ready")
        {
            DrawLine(new(38, 52), new(38, 29), color, 2);
            DrawPolyline([new(29, 38), new(38, 29), new(47, 38)], color, 2, true);
            if (code == "retryable") DrawCircle(new(60, 49), 3, color);
        }
        else DrawLine(new(23, 50), new(53, 24), color, 3, true);
    }
    public override void _ExitTree() { _upload.Dispose(); if (_login is not null && GodotObject.IsInstanceValid(_login)) _login.QueueFree(); }
}
