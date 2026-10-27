using Godot;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain
{
    private EditorHudView _hudView = null!;
    private Control BuildEditChrome()
    {
        _hudView = new EditorHudView(ReturnToPack);
        return _hudView.Root;
    }
    private void RefreshEditHud() => _hudView?.Refresh(_document, _canvas, _time, _auto);

    /// <summary>AUTO 是视觉预览的运行态。CheckButton 被移除后，开关入口是右键菜单的播放行、
    /// `Space` 与 `F1`（F1 不再顺带改动工具态）；三处都走这里，HUD 的 GRID/SNAP 行随之刷新。</summary>
    private void SetAuto(bool enabled)
    {
        _auto = enabled;
        _playing = enabled;
    }
}
