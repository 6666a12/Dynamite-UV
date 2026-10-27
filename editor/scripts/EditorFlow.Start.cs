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
    private EditorStartPage _startView = null!;
    private Control BuildStartPage()
    {
        _startView = new EditorStartPage();
        _startView.BrowseRequested += () => _pathDialog.PopupCenteredRatio(.82f);
        _startView.OpenRequested += TryOpenPackage;
        _startView.CreateRequested += () =>
        {
            _createMode = CreateMode.NewPackage;
            PrepareNewPackageForm();
            ShowFlowPage(EditorFlowPage.Create);
        };
        _startView.SettingsRequested += () => OpenSettings(EditorFlowPage.Start);
        return _startView.Root;
    }
}
