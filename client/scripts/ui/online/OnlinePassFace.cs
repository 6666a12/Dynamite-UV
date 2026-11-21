using Godot;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Account desk's physical pass, rendered from a verified account snapshot only. It
/// never creates a credential, stores a secret or schedules navigation. No idle processing.</summary>
internal sealed partial class OnlinePassFace : Control
{
    private static readonly Vector2 LampPoint = new(132, 711);
    // Starts at the login key's left contact. The endpoint stays inside the perforated ticket.
    private static readonly Vector2[] Wire = [new(1116, 770), new(1078, 770), new(1038, 715),
        new(933, 715), new(873, 764), new(132, 764), LampPoint];
    private static readonly Color Plate = new("172D39"), Idle = new("839AA8"), Engraving = new("395361");
    private readonly SignalReceipt _receipt = new() { Name = "PassIssueReceipt" };
    private readonly Label _alias, _issuer, _stamp;
    private readonly Polygon2D _lamp;
    private Guid? _id;
    private bool _initialized;

    public OnlinePassFace()
    {
        Name = "OnlinePass"; Size = UiLayout.DesignSize; MouseFilter = MouseFilterEnum.Ignore;
        _issuer = OnlineWidgets.Text(this, "尚未选择服务器", new(127, 475, 589, 41), 21, NetworkDeskChrome.Muted);
        _issuer.Name = "OnlinePassIssuer";
        _alias = OnlineWidgets.Text(this, "访客", new(122, 524, 616, 94), 52, NetworkDeskChrome.Ink);
        _alias.Name = "OnlinePassAlias";
        _alias.TextOverrunBehavior = _issuer.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _stamp = OnlineWidgets.Text(this, "未签发", new(156, 686, 550, 49), 25, Idle);
        _stamp.Name = "OnlinePassStatus";
        var points = new Vector2[24];
        for (var i = 0; i < points.Length; i++) points[i] = Vector2.FromAngle(i * Mathf.Tau / points.Length) * 6.3f;
        _lamp = new Polygon2D { Name = "OnlinePassLamp", Position = LampPoint, Polygon = points, Color = new Color("405663") };
        AddChild(_lamp); AddChild(_receipt);
    }

    internal void Observe(AccountConnectionState state)
    {
        var id = state.Account?.Id;
        string issuer = state.Descriptor?.Name ?? (state.Server == null ? "尚未选择服务器" : "尚未确认服务器");
        if (_issuer.Text != issuer) { _issuer.Text = issuer; _issuer.TooltipText = issuer; }
        string alias = state.Account?.Username ?? "访客";
        if (_alias.Text != alias) { _alias.Text = alias; _alias.TooltipText = state.Account?.Username ?? ""; }
        if (_initialized && id == _id) return;
        var previouslyObserved = _initialized;
        _initialized = true; _id = id;
        _stamp.Text = id.HasValue ? "本次有效" : "未签发";
        _stamp.AddThemeColorOverride("font_color", id.HasValue ? NetworkDeskChrome.Teal : Idle);
        _lamp.Color = id.HasValue ? NetworkDeskChrome.Teal : new Color("405663");
        QueueRedraw();
        // An already-valid pass on entry shows its steady state, never a new-issue event.
        if (id.HasValue && previouslyObserved) _receipt.Emit(NetworkDeskChrome.Teal, Wire);
        else _receipt.Cancel();
    }

    public override void _Draw()
    {
        // 593 × 234 reference ticket at 1.5×, with one left locator and a detachable stub.
        DrawColoredPolygon(UiGeometry.CutCorners(82, 439, 890, 351, 19), Plate);
        DrawRect(new(83, 490, 5, 108), _id.HasValue ? NetworkDeskChrome.Teal : Idle);
        DrawLine(new(125, 461), new(190, 461), new Color("405B67"), 2, true);
        for (int i = 0; i < 15; i++)
            DrawLine(new(773, 452 + i * 22.5f), new(773, 462 + i * 22.5f), Engraving, 1.5f, true);
        // Engraved connector teeth, not a purported account ID / machine-readable credential.
        for (int group = 0; group < 3; group++)
        for (int i = 0; i < 5; i++)
        {
            float x = 803 + group * 40 + i * 5.4f, h = 38 + 9 * (i % 3);
            DrawRect(new(x, 645 - h, i % 2 == 0 ? 3 : 2, h), new Color(Idle, .62f));
        }
        DrawPolyline(Wire, new Color("304653"), 1.6f, true);
        DrawArc(LampPoint, 10, 0, Mathf.Tau, 28, _id.HasValue ? new Color(NetworkDeskChrome.Teal, .42f) : Engraving, 1.5f, true);
        DrawLine(new(1044, 204), new(1044, 921), NetworkDeskChrome.Rule, 1.6f, true);
    }
}
