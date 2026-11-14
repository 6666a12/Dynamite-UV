using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Server selection overlay owns only draft controls; connection and persistence stay with OnlineSession.</summary>
internal sealed partial class OnlineServerPicker : Control
{
    public LineEdit Address { get; }
    private readonly OptionButton _saved;
    private readonly Button _connect, _confirm;
    private readonly Label _identity, _status;
    public event Action? Closed;

    public OnlineServerPicker()
    {
        Size = UiLayout.DesignSize; MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Size = Size, Color = new Color(0, 0, 0, 0.65f), MouseFilter = MouseFilterEnum.Ignore });
        OnlineWidgets.Surface(this, new(440, 160, 1040, 760));
        OnlineWidgets.Text(this, "切换服务器", new(496, 194, 760, 60), 36, MenuStyle.Text);
        OnlineWidgets.Link(this, "关闭  ×", new(1300, 208, 130, 48), 22, Close);
        OnlineWidgets.Text(this, "账号资料由所选服务器的运营者管理；切服后需重新登录", new(496, 270, 920, 38), 20, MenuStyle.Secondary);
        _saved = new OptionButton { Position = new(496, 330), Size = new(920, 52) };
        _saved.AddThemeFontOverride("font", UiFonts.Cjk); _saved.AddThemeFontSizeOverride("font_size", 22);
        _saved.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = MenuStyle.Idle });
        AddChild(_saved);
        Address = OnlineWidgets.Input(this, "https://服务器地址", new(496, 418, 920, 76), 2048);
        _saved.ItemSelected += index => { if (index > 0) Address.Text = OnlineSession.Servers[(int)index - 1].Address; };
        _connect = OnlineWidgets.Link(this, "连接 / 检查服务器  →", new(496, 526, 420, 56), 24,
            () => ConnectRequested?.Invoke());
        _identity = OnlineWidgets.Text(this, "", new(496, 600, 920, 100), 19, MenuStyle.Secondary);
        _identity.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _confirm = OnlineWidgets.Primary(this, "确认此服务器身份", new(496, 724, 920, 72),
            Confirm);
        _status = OnlineWidgets.Text(this, "", new(496, 820, 920, 70), 19, MenuStyle.Secondary);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Visible = false;
    }
    public event Action? ConnectRequested;
    public event Action? SaveFailed;
    public void Open()
    {
        Address.Text = OnlineSession.Connection.State.Server?.BaseUri.AbsoluteUri ?? "";
        _saved.Clear(); _saved.AddItem("已保存的服务器（仅地址，不含密码）");
        foreach (var entry in OnlineSession.Servers) _saved.AddItem(entry.Address);
        Visible = true; Address.GrabFocus();
    }
    public void Close()
    {
        OnlineSession.Connection.CancelPending();
        Address.Text = OnlineSession.Connection.State.Server?.BaseUri.AbsoluteUri ?? "";
        Visible = false; Closed?.Invoke();
    }
    public bool AddressMatches()
    {
        try { return ServerAddress.Parse(Address.Text, OnlineSession.AllowLocalHttp) == OnlineSession.Connection.State.Server; }
        catch (OnlineException) { return false; }
    }
    private void Confirm()
    {
        if (!AddressMatches() || _confirm.Disabled) return;
        OnlineSession.Connection.ConfirmInstance();
        if (!OnlineSession.SaveConfirmed()) { SaveFailed?.Invoke(); return; }
        Close();
    }
    public void Refresh(AccountConnectionState state, bool blocked, string message)
    {
        _connect.Disabled = blocked; _saved.Disabled = blocked; Address.Editable = !blocked;
        _confirm.Disabled = blocked || !state.NeedsConfirmation || !AddressMatches();
        _confirm.Modulate = _confirm.Disabled ? new Color(1, 1, 1, 0.45f) : Colors.White;
        _identity.Text = state.Descriptor is { } d
            ? $"{d.Name}\n{state.Server!.BaseUri}\n实例 ID：{d.InstanceId}"
            : "先无凭据检查协议，再确认服务器身份。";
        _status.Text = message;
    }
}
