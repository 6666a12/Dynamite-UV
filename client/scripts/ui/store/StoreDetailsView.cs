using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Network;

namespace DynamiteUniverse.Ui;

/// <summary>Immutable release detail presentation; fetching and installing are owned outside the view.</summary>
internal sealed partial class StoreDetailsView : Control
{
    private readonly Label _title, _mapper, _bpm, _status;
    private readonly TextureRect _cover;
    private readonly HBoxContainer _charts;
    private readonly Button _download;
    private StoreRelease? _release;
    private Texture2D? _texture;
    public event Action<StoreRelease>? Download;
    public StoreDetailsView()
    {
        Size = UiLayout.DesignSize; MouseFilter = MouseFilterEnum.Ignore;
        AddChild(new ColorRect { Position = new(994, 138), Size = new(850, 430), Color = new Color("203C51"), MouseFilter = MouseFilterEnum.Ignore });
        OnlineWidgets.Text(this, "NO COVER", new(994, 308, 850, 70), 32, MenuStyle.Muted, true).HorizontalAlignment = HorizontalAlignment.Center;
        _cover = new TextureRect { Position = new(994, 138), Size = new(850, 430),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore }; AddChild(_cover);
        _title = OnlineWidgets.Text(this, "选择谱包", new(994, 601, 850, 73), 58, MenuStyle.Text);
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _mapper = OnlineWidgets.Text(this, "", new(998, 685, 530, 30), 21, MenuStyle.Secondary);
        _bpm = OnlineWidgets.Text(this, "", new(1535, 674, 310, 48), 28, MenuStyle.Text, true);
        _bpm.HorizontalAlignment = HorizontalAlignment.Right;
        OnlineWidgets.Text(this, "差分", new(998, 764, 280, 24), 16, MenuStyle.Muted);
        var scroll = new ScrollContainer { Position = new(998, 800), Size = new(846, 80),
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _charts = new HBoxContainer(); _charts.AddThemeConstantOverride("separation", 20); scroll.AddChild(_charts); AddChild(scroll);
        _status = OnlineWidgets.Text(this, "", new(998, 882, 846, 36), 18, MenuStyle.Secondary);
        _download = OnlineWidgets.Primary(this, "下载谱包", new(994, 925, 850, 78), () =>
        { if (_release is { } r && _download is { Disabled: false }) Download?.Invoke(r); });
        _download.Disabled = true;
    }
    public void ShowRelease(StoreRelease? release)
    {
        _release = release; _cover.Texture = null; _texture?.Dispose(); _texture = null;
        foreach (var child in _charts.GetChildren()) { _charts.RemoveChild(child); child.QueueFree(); }
        _title.Text = release?.Title ?? "选择谱包"; _title.TooltipText = release?.Title ?? "";
        _mapper.Text = ""; _bpm.Text = "";
        _download.TooltipText = release is null ? "" : $"授权说明：{release.Rights}\n发布：{release.Version}";
        if (release?.Display is not { } display) return;
        foreach (var chart in display.Charts)
        {
            var cell = new Control { CustomMinimumSize = new(164, 60) }; _charts.AddChild(cell);
            cell.AddChild(new Polygon2D { Polygon = UiGeometry.CutCorners(new(52, 52), 6), Color = UiFonts.DiffColor(chart.Difficulty) });
            OnlineWidgets.Text(cell, chart.Unrated ? "?" : chart.Level?.ToString("00") ?? "?", new(0, 0, 52, 52), 20, MenuStyle.Ink, true).HorizontalAlignment = HorizontalAlignment.Center;
            var text = chart.Difficulty == "custom" ? chart.DifficultyKey ?? "CUSTOM" : chart.Difficulty.ToUpperInvariant();
            var select = OnlineWidgets.Link(cell, text, new(66, 0, 98, 52), 17, () => ShowChart(chart), MenuStyle.Secondary);
            select.TooltipText = text;
        }
        ShowChart(display.Charts[0]);
    }
    private void ShowChart(StoreChart chart)
    {
        _mapper.Text = "谱师  " + string.Join(", ", chart.Charters); _mapper.TooltipText = _mapper.Text;
        _bpm.Text = (chart.BpmMin == chart.BpmMax ? $"{chart.BpmMin:0.##}" : $"{chart.BpmMin:0.##}–{chart.BpmMax:0.##}") + " BPM";
    }
    public void SetCover(byte[] bytes, string mediaType)
    {
        var extension = mediaType switch { "image/png" => "png", "image/jpeg" => "jpg", "image/webp" => "webp", _ => "" };
        _cover.Texture = null; _texture?.Dispose();
        _texture = Res.LoadTextureBytes(extension, bytes); _cover.Texture = _texture;
    }
    public void Refresh(InstallProgress progress, CatalogIdentity? source, bool installed, bool authenticated)
    {
        var ours = _release is not null && progress.ReleaseId == _release.Id && progress.Source == source;
        var busy = progress.Status is "downloading" or "installing";
        var supported = _release?.Display?.ValidationProfile == StoreContract.Profile;
        _download.Disabled = !authenticated || _release is null || (!installed && (busy || !supported));
        _download.Modulate = _download.Disabled ? new Color(1, 1, 1, 0.45f) : Colors.White;
        _download.Text = installed ? "前往本地选曲" : ours && busy ? progress.Status == "installing" ? "正在安装…" :
            $"正在下载  {progress.Received * 100 / Math.Max(1, progress.Total)}%" : "下载谱包";
        _status.Text = !authenticated ? "请先登录当前服务器" : _release is null ? "" : !supported ? "此发布尚未完成兼容性审核，暂不可下载" :
            ours && progress.Status == "failed" ? "下载失败：" + StoreScreen.Message(progress.Error ?? "installation_failed") : installed ? "可返回本地选曲游玩" : "";
    }
    public override void _ExitTree() { _cover.Texture = null; _texture?.Dispose(); }
}
