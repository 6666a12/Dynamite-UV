using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>V3 network desk: show the actual server-issued identity on a pass; network and navigation lifetimes remain outside the presentation.</summary>
public partial class OnlineScreen : Node2D
{
    private LineEdit _username = null!, _password = null!;
    private Label _server = null!, _address = null!, _connection = null!, _status = null!, _heading = null!;
    private Control _form = null!, _signedIn = null!;
    private OnlinePassFace _pass = null!;
    private Button _switchServer = null!;
    private Button _submit = null!, _register = null!, _refresh = null!, _logout = null!, _visibility = null!;
    private OnlineServerPicker _picker = null!;
    private readonly List<Button> _navigation = new();
    private Task? _operation;
    private string? _localMessage;
    private bool _registerMode;
    private StoreLoginIntent? _storeIntent;
    private bool _loginOperation;
    public override void _Ready()
    {
        _navigation.Add(NetworkDeskChrome.Install(this, "账号", "OnlineBack", Back));
        _pass = new OnlinePassFace(); AddChild(_pass);
        OnlineWidgets.Text(this, "会话通行证", new(80, 179, 880, 116), 77, NetworkDeskChrome.Ink);
        OnlineWidgets.Text(this, "服务器签发  ·  仅本次登录有效", new(84, 304, 860, 48), 24, NetworkDeskChrome.Muted);
        var localPlay = OnlineWidgets.Link(this, "继续本地游玩  →", new(82, 912, 440, 72), 23,
            () => { CancelStore(); OnlineSession.Connection.CancelPending(); TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Standard); }, NetworkDeskChrome.Muted);
        localPlay.Name = "OnlineContinueLocal"; _navigation.Add(localPlay);

        _heading = OnlineWidgets.Text(this, "登录", new(1116, 226, 402, 94), 55, NetworkDeskChrome.Ink);
        _register = OnlineWidgets.Link(this, "创建账号  ↗", new(1583, 247, 222, 62), 23, ToggleRegistration, NetworkDeskChrome.Teal);
        _register.Name = "OnlineRegister";
        _form = new Control { Name = "OnlineSignInForm", Size = UiLayout.DesignSize, MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(_form);
        OnlineWidgets.Text(_form, "用户名", new(1116, 353, 662, 44), 24, NetworkDeskChrome.Ink);
        _username = OnlineWidgets.Input(_form, "输入用户名", new(1116, 408, 668, 72), 32);
        _username.Name = "OnlineUsername"; _username.AccessibilityName = "用户名"; _username.TooltipText = "3–32位字母、数字或下划线";
        NetworkDeskChrome.InputStyle(_username);
        OnlineWidgets.Text(_form, "密码", new(1116, 514, 662, 44), 24, NetworkDeskChrome.Ink);
        _password = OnlineWidgets.Input(_form, "输入密码", new(1116, 569, 668, 72), 128, true);
        _password.Name = "OnlinePassword"; _password.AccessibilityName = "密码"; _password.TooltipText = "12–128个字符";
        _password.AddThemeConstantOverride("minimum_character_width", 1); NetworkDeskChrome.InputStyle(_password, 116);
        _visibility = OnlineWidgets.Link(_form, "显示", new(1686, 575, 92, 60), 21,
            () => { _password.Secret = !_password.Secret; _visibility.Text = _password.Secret ? "显示" : "隐藏"; }, NetworkDeskChrome.Muted);
        _visibility.Name = "OnlineShowPassword";
        _password.TextSubmitted += _ => Submit();
        _submit = OnlineWidgets.Link(_form, "登录", new(1116, 732, 668, 76), 26, Submit);
        _submit.Name = "OnlineSubmit"; NetworkDeskChrome.KeyStyle(_submit, NetworkDeskChrome.Teal, MenuStyle.Ink);
        ContactRim.Attach(_submit, () => !_submit.Disabled);

        _signedIn = new Control { Name = "OnlineSignedIn", Size = UiLayout.DesignSize, MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(_signedIn);
        OnlineWidgets.Text(_signedIn, "退出或会话过期后需要重新登录。", new(1116, 394, 668, 64), 24, NetworkDeskChrome.Muted);
        _refresh = OnlineWidgets.Link(_signedIn, "检查会话", new(1116, 568, 668, 74), 25,
            () => Start(OnlineSession.Connection.RefreshAsync));
        _refresh.Name = "OnlineRefresh"; NetworkDeskChrome.KeyStyle(_refresh, NetworkDeskChrome.Teal, MenuStyle.Ink);
        _logout = OnlineWidgets.Link(_signedIn, "退出登录", new(1116, 732, 668, 76), 25,
            () => Start(OnlineSession.Connection.LogoutAsync));
        _logout.Name = "OnlineLogout"; NetworkDeskChrome.KeyStyle(_logout, new Color("1D3040"), NetworkDeskChrome.Ink);
        _status = OnlineWidgets.Text(this, "", new(1116, 816, 672, 67), 19, NetworkDeskChrome.Muted);
        _status.Name = "OnlineMessage"; _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _server = OnlineWidgets.Text(this, "未选择服务器", new(1116, 902, 431, 45), 24, NetworkDeskChrome.Ink);
        _server.Name = "OnlineServerName";
        _address = OnlineWidgets.Text(this, "", new(1116, 949, 668, 33), 18, NetworkDeskChrome.Muted);
        _address.Name = "OnlineServerAddress";
        _connection = OnlineWidgets.Text(this, "", new(1116, 987, 669, 37), 18, NetworkDeskChrome.Muted);
        _connection.Name = "OnlineConnectionState";
        _server.TextOverrunBehavior = _address.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _switchServer = OnlineWidgets.Link(this, "切换服务器  ↗", new(1561, 894, 244, 66), 22, () =>
        { CancelStore(); ClearPassword(); _picker.Open(); }, NetworkDeskChrome.Teal);
        _switchServer.Name = "OnlineSwitchServer"; _navigation.Add(_switchServer);
        _picker = new OnlineServerPicker { Name = "OnlinePicker" }; AddChild(_picker);
        _picker.ConnectRequested += Discover;
        _picker.SaveFailed += () => _localMessage = "服务器已确认，但地址列表未保存";
        _picker.Address.TextChanged += _ => ClearPassword();
        _picker.Closed += accepted =>
        {
            var state = OnlineSession.Connection.State;
            if (!accepted || state.Server is null || state.Descriptor is null || state.NeedsConfirmation) CancelStore();
            else _storeIntent?.Pin(state.Server.BaseUri.AbsoluteUri, state.Descriptor.InstanceId);
            _switchServer.Disabled = false; _switchServer.GrabFocus();
        };
        _picker.Address.Text = OnlineSession.Connection.State.Server?.BaseUri.AbsoluteUri ?? "";
        _Process(0);
        TransitionDirector.ReportSceneReady(() => { });
    }
    internal void ContinueToStore(StoreLoginIntent intent)
    {
        var state = OnlineSession.Connection.State;
        if (state.Account is not null || !intent.Active) return;
        _storeIntent = intent;
        // First server selection is part of this route. A later manual server switch is not.
        if (state.Server is null || state.Descriptor is null || state.NeedsConfirmation) _picker.Open();
        else intent.Pin(state.Server.BaseUri.AbsoluteUri, state.Descriptor.InstanceId);
    }
    private void CancelStore() { _storeIntent?.Cancel(); _storeIntent = null; _loginOperation = false; }
    private void Back()
    {
        CancelStore(); OnlineSession.Connection.CancelPending(); TransitionDirector.Navigate(UiRoutes.Main, TransitionKind.Back);
    }
    private void ClearPassword()
    {
        _password.Text = ""; _password.Secret = true; _visibility.Text = "显示";
    }
    private void ToggleRegistration()
    {
        _registerMode = !_registerMode; ClearPassword(); _localMessage = null;
    }
    private void Submit()
    {
        if (_submit.Disabled || _picker.Visible || !_picker.AddressMatches()) return;
        if (_operation is { IsCompleted: false } || TransitionDirector.IsBusy) return;
        var username = _username.Text.Trim();
        _loginOperation = !_registerMode;
        if (_loginOperation) _storeIntent?.LoginStarted(OnlineSession.Connection.State, username);
        var password = _password.Text; ClearPassword();
        Start(() => _registerMode
            ? OnlineSession.Connection.RegisterAsync(username, password)
            : OnlineSession.Connection.LoginAsync(username, password));
    }
    private void Discover()
    {
        if (_operation is { IsCompleted: false }) return;
        try
        {
            var address = ServerAddress.Parse(_picker.Address.Text, OnlineSession.AllowLocalHttp);
            var endpoint = address.BaseUri.AbsoluteUri;
            // A previously saved address carries a known instance pin. An explicitly accepted
            // replacement can still sign in, but must not inherit the old store destination.
            var expectedInstance = OnlineSession.Servers.FirstOrDefault(entry => entry.Address == endpoint)?.InstanceId;
            _storeIntent?.Pin(endpoint, expectedInstance);
            OnlineSession.Select(_picker.Address.Text); Start(OnlineSession.Connection.DiscoverAsync);
        }
        catch (OnlineException ex) { _localMessage = StatusText(ex.Code); }
    }
    private void Start(Func<Task> action)
    {
        if (_operation is { IsCompleted: false }) return;
        _localMessage = null; _operation = action();
    }
    public override void _Process(double delta)
    {
        if (_operation is { IsCompleted: true })
        {
            if (_operation.IsFaulted) { _ = _operation.Exception; _localMessage = "操作未完成，请重试"; }
            else if (OnlineSession.Connection.State.Status == "registered_login_required") _registerMode = false;
            var login = _loginOperation && _operation.IsCompletedSuccessfully;
            _loginOperation = false; _operation = null;
            if (login)
            {
                // Render the verified receipt even when a requested store visit starts its existing transition immediately.
                _pass.Observe(OnlineSession.Connection.State);
                if (TryContinueStore()) return;
            }
        }
        var state = OnlineSession.Connection.State;
        var blocked = state.Busy || _operation is { IsCompleted: false };
        var signedIn = state.Account is not null;
        foreach (var navigation in _navigation) navigation.Disabled = _picker.Visible;
        _form.Visible = !signedIn; _signedIn.Visible = signedIn; _register.Visible = !signedIn;
        _heading.Text = signedIn ? "会话管理" : _registerMode ? "创建账号" : "登录";
        _register.Text = _registerMode ? "返回登录  ↗" : "创建账号  ↗";
        _register.Disabled = blocked || _picker.Visible;
        _submit.Text = _registerMode ? "创建账号" : "登录";
        _submit.Disabled = blocked || _picker.Visible || signedIn || state.Descriptor?.Capabilities.Accounts != true ||
            state.NeedsConfirmation || !_picker.AddressMatches();
        _submit.Modulate = _submit.Disabled ? new Color(1, 1, 1, 0.45f) : Colors.White;
        _username.Editable = _password.Editable = !blocked && !_picker.Visible;
        _visibility.Disabled = blocked || _picker.Visible;
        _refresh.Disabled = _logout.Disabled = blocked || _picker.Visible || !signedIn;
        _server.Text = state.Descriptor?.Name ?? "未选择服务器";
        _server.TooltipText = state.Descriptor?.Name ?? "";
        _address.Text = state.Server?.BaseUri.AbsoluteUri ?? "选择服务器后登录";
        _address.TooltipText = _address.Text;
        _connection.Text = state.Busy ? "正在连接…" : state.NeedsConfirmation ? "等待确认服务器身份" :
            state.Descriptor is null ? "未连接" : state.Descriptor.Capabilities.Accounts ? "已发现服务器" : "账号服务暂不可用";
        _pass.Observe(state);
        var message = _localMessage ?? StatusText(state.Status);
        _status.Text = message;
        _picker.Refresh(state, blocked, message);
    }
    private bool TryContinueStore()
    {
        var state = OnlineSession.Connection.State;
        if (TransitionDirector.IsBusy || _picker.Visible || !IsInsideTree() ||
            !ReferenceEquals(GetTree().CurrentScene, this) || _storeIntent?.Take(state) != true) return false;
        try
        {
            // Reuse the actual saved-source/compatibility/session gates, never infer auth from form input.
            var access = OnlineSession.CaptureCatalog();
            if (access.AccountId != state.Account?.Id || !OnlineSession.Connection.IsCurrent(access)) return false;
            return TransitionDirector.Navigate(UiRoutes.Store, TransitionKind.Standard, "OPEN CHANNEL", "CHART STORE", beforeSwitch: () =>
            {
                // Authentication can expire between the click and the director's opaque barrier.
                if (OnlineSession.Connection.IsCurrent(access)) return;
                _localMessage = "会话已改变或过期，请重新登录";
                throw new OperationCanceledException("Store navigation cancelled: account session changed.");
            });
        }
        catch (OnlineException ex) { _localMessage = ex.Code == "server_not_saved" ? "请先确认并保存当前服务器" : StatusText(ex.Code); return false; }
    }
    public override void _UnhandledInput(InputEvent e)
    {
        if (!TransitionDirector.IsBusy && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            if (_picker.Visible) _picker.Close(); else Back();
        }
    }
    public override void _Notification(int what)
    {
        if (what != NotificationWMGoBackRequest || _picker is null || !IsInsideTree() || TransitionDirector.IsBusy) return;
        if (_picker.Visible) _picker.Close(); else Back();
    }
    public override void _ExitTree()
    {
        CancelStore(); ClearPassword(); OnlineSession.Connection.CancelPending();
    }
    private static string StatusText(string code) => code switch
    {
        "working" => "正在连接…", "signed_in" => "登录成功", "registered_login_required" => "注册成功，请输入密码登录",
        "confirm_server_identity" => "首次连接或服务器实例已改变：请核对地址和实例后确认，尚未发送登录凭据",
        "ready_to_login" => "服务器已确认，可以登录", "signed_out" => "已退出登录",
        "signed_out_remote_revoke_failed" => "本机已退出；服务器会话未确认撤销，将按期限过期",
        "session_expired" or "unauthorized" => "未登录、密码错误或会话已过期，请重新登录",
        "username_unavailable" => "用户名已被使用", "rate_limited" => "请求过于频繁，请稍后手动重试",
        "invalid_input" => "用户名须为3–32位字母、数字或下划线；密码须为12–128字符",
        "https_required" => "远程服务器必须使用 HTTPS；HTTP仅供受控开发回环使用",
        "invalid_address" => "请输入服务器根地址，不含路径、账号、查询参数或片段",
        "incompatible_server" => "服务器账号协议不兼容", "invalid_response" => "服务器响应无效或超过大小限制",
        "redirect_refused" => "拒绝服务器重定向，请核对实际地址",
        "timeout" or "connection_failed" => "连接失败或超时，可手动重试；不会清除本地歌曲和成绩",
        "service_unavailable" => "服务器账号服务暂不可用", "identity_mismatch" => "返回账号身份不匹配，已清除认证",
        "cancelled" => "操作已取消", "discover_and_confirm_first" or "discover_required" => "请先连接并确认服务器",
        "select_server" => "请选择服务器", _ => "请求未完成，请检查服务器后重试",
    };
}
