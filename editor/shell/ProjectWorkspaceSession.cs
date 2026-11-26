using Avalonia.Controls;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Owns the transition from the start page to an attached workbench and a single stage launch.
/// The caller adopts the document first; stage startup failure never rolls that adoption back.</summary>
internal sealed class ProjectWorkspaceSession(Window window, WorkbenchLayoutSession layout, StageView stageView,
    Action launchStage, Action<string> report, IWorkbenchLocalizer text)
{
    private enum StageState { Idle, Starting, Live, Stopped }
    private StageState _stage;
    public bool IsReady => _stage == StageState.Live;
    private bool _opened;
    private bool _hasProject;

    public void WindowOpened() { _opened = true; AttachIfReady(); }

    public void Enter()
    {
        if (_hasProject) return;
        _hasProject = true;
        Find<Border>("StartPage").IsVisible = false;
        foreach (var name in new[] { "ProjectWorkspace", "ProjectHeading", "ProjectSave" })
            Find<Control>(name).IsVisible = true;
        AttachIfReady();
    }

    private void AttachIfReady()
    {
        if (!_opened || !_hasProject || layout.IsAttached) return;
        layout.Attach(window); // Only now may Dock restore top-level floating windows.
        Start();
    }

    private void Start()
    {
        _stage = StageState.Starting;
        UpdateCaption();
        try { launchStage(); }
        catch (Exception ex) { Stopped(); report($"{text["Status.GodotStopped"]} · {ex.Message}"); }
    }

    public void Retry() { if (_stage == StageState.Stopped) Start(); }
    public void Ready() { _stage = StageState.Live; UpdateCaption(); }
    public void Stopped() { _stage = StageState.Stopped; UpdateCaption(); }
    public void UpdateCaption()
    {
        var key = _stage switch { StageState.Live => "Status.GodotLive", StageState.Stopped => "Status.GodotStopped", _ => "Status.StartingGodot" };
        Find<TextBlock>("ReadyBadge").Text = text[key];
        stageView.SetStatus(text[_stage is StageState.Idle or StageState.Starting ? "Stage.Starting" : key]);
        Find<Button>("RetryStageButton").IsVisible = _stage == StageState.Stopped;
    }
    private T Find<T>(string name) where T : Control => window.FindControl<T>(name)!;
}
