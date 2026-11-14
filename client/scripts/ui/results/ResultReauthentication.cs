using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Result-local login dialog; retains the result scene and never changes server selection.</summary>
internal sealed partial class ResultReauthentication : Control
{
    private readonly LineEdit _user, _password;
    private readonly Label _status;
    private readonly Button _login;
    private Task? _pending;
    private readonly PlayOrigin? _origin;
    internal ResultReauthentication(PlayOrigin? origin)
    {
        _origin = origin;
        Size = UiLayout.DesignSize; MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Size = Size, Color = new Color(0, 0, 0, .8f), MouseFilter = MouseFilterEnum.Ignore });
        OnlineWidgets.Surface(this, new(510, 200, 900, 680));
        OnlineWidgets.Text(this, "重新登录原账号", new(560, 240, 800, 60), 36, MenuStyle.Text);
        var state = OnlineSession.Connection.State;
        OnlineWidgets.Text(this, origin?.Server.Address ?? "原服务器未知", new(560, 316, 800, 48), 21, MenuStyle.Secondary);
        _user = OnlineWidgets.Input(this, "原账号用户名", new(560, 390, 800, 70), 32);
        _user.Text = origin?.Username ?? "";
        _password = OnlineWidgets.Input(this, "密码", new(560, 490, 800, 70), 128, true);
        _login = OnlineWidgets.Primary(this, "登录", new(560, 600, 800, 70), Login);
        _status = OnlineWidgets.Text(this, "不会转移本局归属；登录后回到结果页手动重试", new(560, 690, 800, 70), 20, MenuStyle.Secondary);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        OnlineWidgets.Link(this, "返回结果", new(560, 788, 240, 52), 23, () => QueueFree());
    }
    private void Login()
    {
        if (_pending is { IsCompleted: false }) return;
        var state = OnlineSession.Connection.State;
        if (_origin is null || state.Server?.BaseUri.AbsoluteUri != _origin.Server.Address || state.Descriptor?.InstanceId != _origin.Server.InstanceId)
        { _password.Text = ""; _status.Text = "当前服务器不是本局原服务器，不能在此提交凭据"; return; }
        var password = _password.Text; _password.Text = "";
        _pending = OnlineSession.Connection.LoginAsync(_user.Text.Trim(), password);
    }
    public override void _Process(double delta)
    {
        _login.Disabled = _pending is { IsCompleted: false };
        if (_pending is not { IsCompleted: true } task) return;
        _pending = null;
        if (task.IsFaulted) { _ = task.Exception; _status.Text = "登录未完成，请重试"; return; }
        _status.Text = OnlineSession.Connection.State.Status == "signed_in"
            ? OnlineSession.Connection.State.Account?.Id == _origin?.AccountId
                ? "原账号已登录；返回结果后点击云图标重试"
                : $"登录的不是本局账号，请使用 {_origin?.Username ?? _origin?.AccountId.ToString()}"
            : "登录失败或服务器尚未确认，请检查原账号和密码";
    }
    public override void _ExitTree() { _password.Text = ""; OnlineSession.Connection.CancelPending(); }
}
