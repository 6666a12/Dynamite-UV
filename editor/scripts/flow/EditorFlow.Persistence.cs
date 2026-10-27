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
    private string? _saveError;

    /// <summary>SAVE writes the open document; SAVE AS and the first save of an in-memory draft
    /// ask for a destination directory first.</summary>
    private void RequestSave(bool saveAs)
    {
        if (_document is null) { ReportSave("没有可保存的文档。", true); return; }
        if (!saveAs && !_document.IsUnpersisted)
        {
            SaveToDirectory(_document.PackageDirectory!);
            return;
        }
        if (_document.PackageDirectory is not null) _saveDialog.CurrentDir = _document.PackageDirectory.Replace('\\', '/');
        _saveDialog.PopupCenteredRatio(.82f);
    }

    private void SaveToDirectory(string destinationDirectory)
    {
        if (_document is null) { ReportSave("没有可保存的文档。", true); return; }
        try
        {
            var result = _documentSession.Save(destinationDirectory);
            var action = result.Kind == EditorSaveKind.Created ? "CREATED" : "SAVED";
            InvalidateDigestAudioHash();
            // 状态行是 Pack 页唯一的回报位，刷新会重置它，所以先刷新页面再写回报。
            if (_flowPage == EditorFlowPage.Pack) RefreshPackPage();
            ReportSave($"{action} · {result.ChartCount} CHARTS · {result.DestinationDirectory}", false);
        }
        catch (Exception exception)
        {
            // The session remains dirty on failure. Publication cleanup failures can occur
            // after files were written, so do not claim that every exception rolled back disk.
            ReportSave($"保存失败：{exception.Message}", true);
        }
    }

    /// <summary>保存结果的回报位：Edit 主态是 HUD 的保存状态行（<see cref="RefreshSaveState"/> 会把
    /// <see cref="_saveError"/> 显示出来），其余页面复用各自的消息通道；shell 不再有常驻调试文字行。</summary>
    private void ReportSave(string message, bool error)
    {
        _saveError = error ? message : null;
        if (_flowPage != EditorFlowPage.Edit) SetFlowMessage(message, error);
        RefreshSaveState();
    }

    private void RefreshSaveState() => _hudView?.RefreshSaveState(_document, _saveError);
}
