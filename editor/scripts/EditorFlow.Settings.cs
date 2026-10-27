using Godot;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Ui;
using static Godot.Control;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;

public partial class StandaloneEditorMain
{
    private EditorFlowPage _settingsReturn;
    private EditorSettingsPage _settingsView = null!;
    private Control BuildSettingsPage()
    {
        _settingsView = new EditorSettingsPage(_canvas, EditorSettings.Current,
            () => ShowFlowPage(_settingsReturn), ApplyUiScale);
        return _settingsView.Root;
    }
    private void RefreshSettingsPage() => _settingsView.RefreshSettingsPage();

    /// <summary>Start 页与编辑态右键菜单共用这一条：记录来源页，BACK 各回其处。</summary>
    private void OpenSettings(EditorFlowPage from)
    {
        _settingsReturn = from;
        RefreshSettingsPage();
        ShowFlowPage(EditorFlowPage.Settings);
    }

    /// <summary>UI SCALE 的立即生效：重放字号注册表（Label / LineEdit / Button 靠它），再把
    /// 自绘页面重绘一次——画布、Pack / Create / Settings overlay 的字号都在 <c>_Draw</c> 里现算
    /// <see cref="EditorFonts.Pt"/>，所以**重绘就是应用**，不需要重建任何页面。
    /// 三个 overlay 都刷而不是只刷当前页：从 Edit 右键菜单进 Settings 时画布文字仍然可见，
    /// 少刷一个会留下旧字号的残影。Start 页的文字全是注册表管的 Label，没有自绘字号。</summary>
    private void ApplyUiScale()
    {
        EditorFonts.ReapplySizes();
        _canvas.RedrawForUiScale();
        _packOverlay?.QueueRedraw();
        _createOverlay?.QueueRedraw();
        _settingsView?.Redraw();
        _menu?.QueueRedraw();
    }
}
