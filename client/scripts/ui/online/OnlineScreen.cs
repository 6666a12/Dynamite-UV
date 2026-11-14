using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Penpot Online v2 login page; session/network ownership remains outside the scene.</summary>
public partial class OnlineScreen : Node2D
{
    private LineEdit _username = null!, _password = null!;
    private Label _server = null!, _address = null!, _connection = null!, _status = null!, _heading = null!;
    private Control _form = null!, _signedIn = null!;
    private Label _account = null!;
    private Button _submit = null!, _register = null!, _refresh = null!, _logout = null!, _visibility = null!;
    private OnlineServerPicker _picker = null!;
    private readonly List<Button> _navigation = new();
    private Task? _operation;
    private string? _localMessage;
    private bool _registerMode;
    public override void _Ready()
    {
        AddChild(new ColorRect { Size = UiLayout.DesignSize, Color = MenuStyle.Background,
            MouseFilter = Control.MouseFilterEnum.Ignore });
        _navigation.Add(OnlineWidgets.Link(this, "‹", new(64, 40, 50, 45), 36, Back, MenuStyle.Secondary));
        OnlineWidgets.Text(this, "账号", new(124, 42, 400, 40), 32, MenuStyle.Text);
        OnlineWidgets.Text(this, "DYNAMITE / UNIVERSE", new(1420, 49, 430, 22), 17, MenuStyle.Muted, true);
        OnlineWidgets.Text(this, "当前服务器", new(128, 170, 600, 23), 18, MenuStyle.Muted);
        _server = OnlineWidgets.Text(this, "未选择服务器", new(122, 210, 740, 95), 76, MenuStyle.Text);
        _server.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _address = OnlineWidgets.Text(this, "选择服务器后登录", new(128, 320, 730, 29), 23, MenuStyle.Secondary, true);
        _address.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        AddChild(new ColorRect { Position = new(129, 382), Size = new(6, 6), Color = MenuStyle.Accent,
            MouseFilter = Control.MouseFilterEnum.Ignore });
        _connection = OnlineWidgets.Text(this, "未连接", new(150, 372, 640, 24), 17, MenuStyle.Accent);
        _navigation.Add(OnlineWidgets.Link(this, "切换服务器  ↗", new(128, 420, 450, 48), 23, () =>
        { ClearPassword(); _picker.Open(); }));
        OnlineWidgets.Surface(this, new(104, 550, 752, 362));
        AddChild(new ColorRect { Position = new(128, 546), Size = new(36, 3), Color = MenuStyle.Accent,
            MouseFilter = Control.MouseFilterEnum.Ignore });
        OnlineWidgets.Text(this, "服务器公告", new(128, 572, 500, 33), 25, MenuStyle.Text);
        OnlineWidgets.Text(this, "公告功能尚未接入", new(128, 664, 690, 39), 30, MenuStyle.Secondary);
        _navigation.Add(OnlineWidgets.Link(this, "继续本地游玩  →", new(128, 931, 620, 48), 22,
            () => { OnlineSession.Connection.CancelPending(); TransitionDirector.Navigate(UiRoutes.SongSelect, TransitionKind.Standard); }, MenuStyle.Secondary));
        _heading = OnlineWidgets.Text(this, "登录", new(1072, 232, 480, 65), 52, MenuStyle.Text);
        _register = OnlineWidgets.Link(this, "创建账号  ↗", new(1574, 248, 240, 48), 20, ToggleRegistration, MenuStyle.Secondary);
        _form = new Control { Size = UiLayout.DesignSize, MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(_form);
        OnlineWidgets.Text(_form, "用户名", new(1072, 370, 650, 23), 18, MenuStyle.Muted);
        _username = OnlineWidgets.Input(_form, "输入用户名", new(1072, 410, 720, 73), 32);
        _username.TooltipText = "3–32位字母、数字或下划线";
        OnlineWidgets.Text(_form, "密码", new(1072, 536, 650, 23), 18, MenuStyle.Muted);
        _password = OnlineWidgets.Input(_form, "输入密码", new(1072, 576, 720, 73), 128, true);
        _password.TooltipText = "12–128个字符";
        _password.AddThemeConstantOverride("minimum_character_width", 1);
        // Reserve space for the visibility action without shortening the design's underline.
        foreach (var state in new[] { "normal", "read_only", "focus" })
            _password.AddThemeStyleboxOverride(state, new StyleBoxEmpty { ContentMarginRight = 100, ContentMarginBottom = 22 });
        _visibility = OnlineWidgets.Link(_form, "显示", new(1726, 580, 66, 48), 17,
            () => { _password.Secret = !_password.Secret; _visibility.Text = _password.Secret ? "显示" : "隐藏"; }, MenuStyle.Muted);
        _password.TextSubmitted += _ => Submit();
        _submit = OnlineWidgets.Primary(_form, "登录", new(1072, 732, 720, 80), Submit);
        _signedIn = new Control { Size = UiLayout.DesignSize, MouseFilter = Control.MouseFilterEnum.Ignore }; AddChild(_signedIn);
        _account = OnlineWidgets.Text(_signedIn, "", new(1072, 394, 720, 100), 38, MenuStyle.Text);
        _account.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        OnlineWidgets.Text(_signedIn, "仅本次运行保持登录", new(1072, 536, 720, 36), 22, MenuStyle.Secondary);
        _refresh = OnlineWidgets.Link(_signedIn, "检查会话  ↗", new(1072, 592, 300, 48), 23,
            () => Start(OnlineSession.Connection.RefreshAsync));
        _logout = OnlineWidgets.Primary(_signedIn, "退出登录", new(1072, 732, 720, 80),
            () => Start(OnlineSession.Connection.LogoutAsync));
        _status = OnlineWidgets.Text(this, "", new(1072, 844, 720, 110), 20, MenuStyle.Secondary);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _picker = new OnlineServerPicker(); AddChild(_picker);
        _picker.ConnectRequested += Discover;
        _picker.SaveFailed += () => _localMessage = "服务器已确认，但地址列表未保存";
        _picker.Address.TextChanged += _ => ClearPassword();
        _picker.Closed += () => _register.GrabFocus();
        _picker.Address.Text = OnlineSession.Connection.State.Server?.BaseUri.AbsoluteUri ?? "";
        _Process(0);
        TransitionDirector.ReportSceneReady(() => { });
    }
    private void Back()
    {
        OnlineSession.Connection.CancelPending(); TransitionDirector.Navigate(UiRoutes.Main, TransitionKind.Back);
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
        var password = _password.Text; ClearPassword();
        Start(() => _registerMode
            ? OnlineSession.Connection.RegisterAsync(_username.Text.Trim(), password)
            : OnlineSession.Connection.LoginAsync(_username.Text.Trim(), password));
    }
    private void Discover()
    {
        if (_operation is { IsCompleted: false }) return;
        try { OnlineSession.Select(_picker.Address.Text); Start(OnlineSession.Connection.DiscoverAsync); }
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
            _operation = null;
        }
        var state = OnlineSession.Connection.State;
        var blocked = state.Busy || _operation is { IsCompleted: false };
        var signedIn = state.Account is not null;
        foreach (var navigation in _navigation) navigation.Disabled = _picker.Visible;
        _form.Visible = !signedIn; _signedIn.Visible = signedIn; _register.Visible = !signedIn;
        _heading.Text = signedIn ? "账号" : _registerMode ? "创建账号" : "登录";
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
        _account.Text = state.Account?.Username ?? "";
        var message = _localMessage ?? StatusText(state.Status);
        _status.Text = message;
        _picker.Refresh(state, blocked, message);
    }
    public override void _ExitTree()
    {
        ClearPassword(); OnlineSession.Connection.CancelPending();
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
