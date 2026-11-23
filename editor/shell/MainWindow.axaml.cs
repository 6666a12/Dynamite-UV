using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Editor.Shell.VideoExport;
using SukiUI.ControlsAnimation;
using SukiUI.Theme;
using Ursa.Controls;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class MainWindow : Window
{
    private static readonly int[] SnapSteps = { 1, 2, 4, 8, 12, 16, 24, 32, 48, 64 };
    private readonly GodotStageService _service = new();
    private readonly VideoExportFlow _videoExport = new();
    private readonly EditorNoteClipboard _noteClipboard = new();
    private readonly EditorDocumentSession _session = new();
    private readonly StageView _stageView = new();
    private readonly InspectorView _inspector = new();
    private readonly ToolsView _tools = new();
    private readonly TimingView _timing = new();
    private readonly WorkbenchTimeline _rhythmTimeline;
    private readonly TimelineView _timeline;
    private readonly WorkbenchFactory _factory;
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private readonly ShellChartEditor _editing;
    private readonly CurveSaveGuard _curves;
    private readonly WorkbenchPreviewClock _clock;
    private bool _ioBusy;
    private bool _closePending;
    private bool _closeApproved;
    internal Func<Task<UnsavedCloseChoice>>? ClosePromptForVerification { get; set; }
    internal Task CloseDecisionForVerification { get; private set; } = Task.CompletedTask;
    private double _playerSpeed = 1.0;
    private double _playbackRate = 1.0;
    private string _tool = "SELECT";
    private int _snapDenominator = EditorDocument.DefaultGridDivisor;
    private bool _refreshingCharts;
    private readonly WorkbenchSelection _selection = new();
    private string[] _selectedNoteIds => _selection.Parents;
    private EditorDocument? _loadedDocument;
    private string? _loadedChartId;
    private string? _loadedAudioPath;
    private long _stageRevision;
    private long _stageLoadRevision = -1;
    private readonly WorkbenchTimingKeys _timingKeys;
    private readonly WorkbenchModalHost _modal;
    private readonly DifficultyCreationFlow _difficulty;
    private readonly BpmImportFlow _bpmImport;
    private readonly WorkbenchUserStore _userStore;
    private readonly WorkbenchPreferenceSession _preferences;
    private readonly WorkbenchLayoutSession _layoutSession;
    private readonly ProjectWorkspaceSession _workspace;
    internal Func<Task<string?>>? PickOpenFolderForVerification { get; set; }
    private double _previewVolume = 100;
    private string _previewEffects = "Full";

    public MainWindow() : this(null) { }
    internal MainWindow(string? userDirectory, Action? launchStage = null)
    {
        _userStore = new WorkbenchUserStore(userDirectory);
        _preferences = new WorkbenchPreferenceSession(_userStore, CapturePreferences, Report);
        AvaloniaXamlLoader.Load(this);
        _timeline = this.FindControl<TimelineView>("Timeline")!;
        _rhythmTimeline = new(_timeline);
        _clock = new(_timeline, _timing, this.FindControl<TextBlock>("TimeLabel")!, _text);
        ClosingBehavior = WindowClosingBehavior.OwnerWindowOnly;
        _editing = new ShellChartEditor(_text, () => RefreshDocument(), Report);
        _timingKeys = new(_timing, _service.Send, Report, _text);
        _timing.OffsetPreviewChanged += () =>
        {
            _rhythmTimeline.RefreshTiming(CurrentDocument?.SelectedChart);
            UpdateDocumentChrome(); SendDocument();
            if (CurrentDocument?.SelectedChart is { } c) Report(_text.Format("Timing.OffsetStatus", c.Id, ChartOffsetUnits.FormatMilliseconds(c.AudioOffsetSec, signed: true)));
        };
        Deactivated += (_, _) => _timingKeys.ResetHeld();
        LostFocus += (_, _) => _timingKeys.ResetHeld();
        _factory = new WorkbenchFactory(_stageView, _inspector, _tools, _timing);
        _modal = new(this, this.FindControl<Grid>("WorkbenchBody")!, this.FindControl<Border>("WorkbenchModalLayer")!,
            this.FindControl<ContentControl>("WorkbenchModalSlot")!, _factory.FloatingWindows, (blocked, epoch) =>
            {
                if (blocked) _timingKeys.ResetHeld();
                _factory.ModalActive = blocked;
                _stageView.Host.SetOccluded(blocked || ShortcutsVisible);
                _service.Send(new { command = "workbenchModal", blocked, epoch });
            });
        _curves = new(_inspector.Formula, _timing.Formula, this.FindControl<Button>("DiscardFormulaDrafts")!, _modal, _text, Report);
        _difficulty = new(_session, _modal, () => { RefreshDocument(); Report(_text.Format("Message.ChartAdded", CurrentDocument!.SelectedChartId)); });
        _bpmImport = new(_session, _modal, () => RefreshDocument(refreshChartChoices: false), Report, _text);
        _layoutSession = new WorkbenchLayoutSession(_userStore, _factory, Report);
        _workspace = new(this, _layoutSession, _stageView, launchStage ?? (() =>
        {
            if (Environment.GetEnvironmentVariable("DYNA_WORKBENCH_NO_GODOT") != "1")
                _service.Start(TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("No native host window."));
        }), Report, _text);
        _timeline.WheelSteps += steps => { if (Ready && !_modal.IsOpen && !ShortcutsVisible)
            _service.Send(new { command = "timelineWheel", steps, revision = _stageRevision }); };
        _stageView.FloatRequested += FloatStage;
        _stageView.DockRequested += DockStage;
        _stageView.PlayRequested += TogglePlayback;
        _stageView.GamePreviewChanged += enabled => _service.Send(new { command = "gamePreview", enabled });
        _inspector.BatchEditRequested = edit =>
        {
            if (_ioBusy || CurrentDocument is not { } document) return _text["Batch.Unavailable"];
            try
            {
                document.Execute(new EditNoteSelectionCommand(_selectedNoteIds, edit));
                RefreshDocument();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        };
        _inspector.BatchLocateRequested += id =>
        {
            if (CurrentDocument?.SelectedChart.Notes.FirstOrDefault(n => n.Id == id) is { } note)
                SeekBar(note.Time);
        };
        _inspector.SpeedChanged += (id, speed) => _editing.ApplySpeed(CurrentDocument, _selectedNoteIds, id, speed);
        _inspector.LocateRequested += (id, nodeId) =>
        {
            if (CurrentDocument is not { } document) return;
            if (nodeId is not null && !_selection.SelectPathNode(document, id, nodeId)) return;
            if (nodeId is null) _selection.SelectNote(document, id);
            _selection.Show(document, _inspector);
            _timing.ClearRowSelection();
            var note = document.SelectedChart.Notes.Single(n => n.Id == id);
            SeekBar(nodeId is null ? note.Time : note.Nodes.Single(n => n.Id == nodeId).Time);
            SendDocument();
        };
        _inspector.CurveChangeRequested = (id, nodeId, curve, expr) => _editing.ApplyCurve(CurrentDocument, id, nodeId, curve, expr);
        _inspector.HoldJudgeChanged += (id, nodeId, judge) => _editing.ApplyHoldJudge(CurrentDocument, id, nodeId, judge);
        _inspector.NudgeRequested += (time, position) => _editing.NudgeSelection(
            CurrentDocument, SelectedNoteId, _selectedNoteIds, _snapDenominator, time, position);
        _inspector.GeometryApplyRequested += (id, nodeId, time, center, width) =>
            _editing.ApplyGeometry(CurrentDocument, id, nodeId, time, center, width);
        _inspector.NoteKindApplyRequested += (id, type, track) =>
            _editing.ApplyNoteKind(CurrentDocument, id, type, track);
        _tools.ToolSelected += tool =>
        {
            _tool = tool;
            SendAuthoring();
            Report(_text.Format("Message.ToolSelected", _text[$"Tools.{TitleCase(tool)}"]));
        };
        _tools.AuthoringChanged += SendAuthoring;
        _tools.AddPanelRequested += AddPanel;
        _tools.CoverStudioRequested += () => { if (!_ioBusy && !_closePending && !_modal.IsOpen && !ShortcutsVisible) CoverStudioFlow.Show(this, _session, _text, Report); };
        _tools.Notice += _ => Report(_text["Message.WorkspaceNotice"]);
        _timing.RowSelectionChanged += selection =>
        {
            if (CurrentDocument is not { } document) return;
            _selection.SelectTiming(document, selection);
            _selection.Show(document, _inspector);
            _service.Send(new { command = "setSelection", revision = _stageRevision, selection = _selection.StageIds(document) });
        };
        _timing.CommandRequested = command => _editing.ApplyTiming(CurrentDocument, command);
        _timing.ErrorRequested += Report;
        var stageDispatch = new StageFrameDispatch(action => Dispatcher.UIThread.Post(action), StageEvent);
        _service.MessageReceived += stageDispatch.Receive;
        _service.RuntimeError += error => Dispatcher.UIThread.Post(() => Report("Godot: " + error));
        _text.LanguageChanged += UpdateLocalizedState;
        var preferences = _preferences.Initial;
        _snapDenominator = preferences.Grid;
        WheelReversed = preferences.WheelReversed;
        _previewVolume = preferences.Volume;
        _previewEffects = preferences.Effects;
        _tools.RestorePreferences(preferences);
        this.FindControl<ComboBox>("LanguageChoice")!.SelectedIndex = preferences.Language == "zh-CN" ? 1 : 0;
        _text.SetLanguage(preferences.Language);
        this.FindControl<ComboBox>("MotionChoice")!.SelectedIndex = preferences.Motion;
        UpdateLocalizedState();
        _preferences.Enabled = true;
        Report(_userStore.Warning ?? _text["Status.Ready"]);
        Opened += (_, _) => _workspace.WindowOpened();
        Closed += (_, _) =>
        {
            _factory.IsShuttingDown = true;
            _preferences.Enabled = false;
            _text.LanguageChanged -= UpdateLocalizedState;
            _factory.Release();
            _stageView.Host.SetGodotHandle(0);
            _service.Dispose();
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_modal.IsOpen) { e.Cancel = true; _modal.Close(); return; }
        if (_ioBusy || _closePending) { e.Cancel = true; return; }
        if (!_closeApproved && (CurrentDocument?.HasUnsavedChanges == true || _curves.HasDraft))
        {
            e.Cancel = true;
            CloseDecisionForVerification = ConfirmClose();
            return;
        }
        // Capture before base raises Dock's closing handlers and tears down floating hosts.
        _preferences.Flush();
        _layoutSession.Save();
        _factory.IsShuttingDown = true;
        base.OnClosing(e);
        if (e.Cancel) { _factory.IsShuttingDown = false; _closeApproved = false; }
    }

    private async Task ConfirmClose()
    {
        _closePending = true;
        var document = CurrentDocument;
        var version = document?.ChangeVersion;
        var draftRevision = _curves.Revision;
        var close = false;
        try
        {
            // Yield so even a synchronously completed test/user decision cannot re-enter OnClosing.
            await Task.Yield();
            var choice = await (ClosePromptForVerification?.Invoke() ?? UnsavedCloseDialog.Show(this, _text));
            if (!ReferenceEquals(document, CurrentDocument) || _ioBusy) return;
            if (choice == UnsavedCloseChoice.Save)
            {
                await SavePackage(false);
                close = ReferenceEquals(document, CurrentDocument) && document?.HasUnsavedChanges != true && !_curves.HasDraft;
            }
            else if (choice == UnsavedCloseChoice.Discard)
                close = document?.ChangeVersion == version && _curves.Revision == draftRevision;
        }
        catch (Exception ex) { Report(_text.Format("Message.FileError", ex.Message)); }
        finally { _closePending = false; }
        if (close) { _closeApproved = true; Close(); }
    }

    internal StageView Stage => _stageView;
    internal GodotStageService StageServiceForVerification => _service;
    internal TimingView Timing => _timing;
    internal WorkbenchFactory LayoutFactory => _factory;
    internal bool Ready => _workspace.IsReady;
    internal int StageProcessId => _service.ProcessId;
    internal int TickCount { get; private set; }
    internal string LastStageInput { get; private set; } = "";
    internal double CurrentBar => _clock.Bar;
    internal double CurrentSecond => _clock.Second;
    internal bool Playing => _clock.Playing;
    internal double PlayerSpeed => _playerSpeed;
    internal double PlaybackRate => _playbackRate;
    internal bool WheelReversed { get; private set; }
    internal string CurrentTool => _tool;
    internal int SnapDenominator => _snapDenominator;
    internal string? SelectedNoteId => _selection.Primary;
    internal EditorDocument? CurrentDocument => _session.Document;
    internal string StatusMessage => this.FindControl<TextBlock>("StatusText")!.Text ?? "";
    internal void SelectToolForVerification(string tool) => _tools.SelectTool(tool);
    internal V2NoteType PlacementTypeForVerification => _tools.PlacementType;
    internal EditorTrack PlacementTrackForVerification => _tools.PlacementTrack;
    internal bool ShortcutsVisible => this.FindControl<Border>("ShortcutOverlay")!.IsVisible;
    internal void CaptureStage(string path) => _service.Capture(path);
    internal void SeekForVerification(double bar) => SeekBar(bar);
    internal bool ShortcutForVerification(Key key, KeyModifiers modifiers = KeyModifiers.None) => HandleShortcut(key, modifiers, null);
    internal async Task OpenPackageForVerification(string path) => await OpenPackage(path);
    internal async Task SavePackageForVerification(bool saveAs = false) => await SavePackage(saveAs);
    internal async Task SavePackageToForVerification(string path) => await SavePackage(true, path);
    internal void ApplySpeedForVerification(double speed) => _inspector.SetSpeedForVerification(speed);
    internal void ApplyHoldJudgeForVerification(bool judge) => _inspector.SetHoldJudgeForVerification(judge);
    internal void ApplyGeometryForVerification(string barTime, double center, double width) =>
        _inspector.SetGeometryForVerification(barTime, center, width);
    internal void ApplyNoteKindForVerification(V2NoteType type, EditorTrack track) =>
        _inspector.SetNoteKindForVerification(type, track);
    internal void NudgeSelectionForVerification(int timeDirection, int positionDirection) =>
        _inspector.NudgeForVerification(timeDirection, positionDirection);
    internal IReadOnlyList<string> SelectedNoteIds => _selectedNoteIds;
    internal void SelectNoteForVerification(string id) => SelectNote(id);
    private void SelectNote(string id)
    {
        SelectNotes(id);
    }
    internal void SelectNotesForVerification(params string[] ids) => SelectNotes(ids);
    private void SelectNotes(params string[] ids)
    {
        if (CurrentDocument is not { } document) return;
        _selection.FromParents(document, ids);
        _selection.Show(document, _inspector);
        _timing.ClearRowSelection();
    }
    internal async Task CreatePackageForVerification(EditorCreateForm form) => await CreatePackage(form);
    internal Task AddChartForVerification(EditorCreateForm form) => _difficulty.AddForVerification(form);
    internal void CopyChartForVerification() => CopyChart();
    internal void DeleteChartForVerification() => DeleteChart();
    internal void MoveChartForVerification(int direction) => MoveChart(direction);
    internal void SelectChartForVerification(string id)
    {
        CurrentDocument?.SelectChart(id);
        RefreshDocument();
    }
    internal void ConfigurePlacementForVerification(V2NoteType type, EditorTrack track, double width)
    {
        _tools.ConfigurePlacementForVerification(type, track, width);
        _tools.SelectTool("PLACE");
    }
    internal void ApplyCurveForVerification(V2PathCurve curve) => _inspector.SetCurveForVerification(curve);
    internal void SendInvalidAuthoringForVerification() =>
        _service.SetAuthoring("SELECT", "not-a-note-type", "Center", 1, 16);
    internal void StageEventForVerification(string name, string json)
    {
        using var jsonDocument = JsonDocument.Parse(json);
        StageEvent(new StageMessage(name, jsonDocument.RootElement.Clone()));
    }

    private async Task CreatePackage(EditorCreateForm? form = null)
    {
        if (_ioBusy || _curves.BlockPending()) return;
        if (CurrentDocument?.HasUnsavedChanges == true)
        {
            Report(_text["Message.SaveBeforeOpen"]);
            return;
        }
        _ioBusy = true;
        try
        {
            var previous = CurrentDocument;
            var version = previous?.ChangeVersion;
            EditorCreateResult? result;
            if (form is null)
            {
                var dialog = new CreateChartDialog(CurrentDocument, _snapDenominator);
                result = await dialog.ShowDialog<EditorCreateResult?>(this);
            }
            else result = EditorCreateRequest.Build(EditorCreateMode.NewPackage, form, _snapDenominator);
            if (result?.DraftRequest is null || !ReferenceEquals(previous, CurrentDocument) ||
                previous?.ChangeVersion != version) return;
            if (_curves.BlockPending()) return;
            if (CurrentDocument?.HasUnsavedChanges == true) { Report(_text["Message.SaveBeforeOpen"]); return; }
            await _curves.ReplaceDocument(() => _session.CreateDraftAsync(result.DraftRequest));
            _clock.ShowPosition(0, 0);
            RefreshDocument();
            Report(_text.Format("Message.Created", CurrentDocument!.Title));
            _workspace.Enter();
        }
        catch (Exception ex) { Report(_text.Format("Message.FileError", ex.Message)); }
        finally { _ioBusy = false; }
    }

    internal void ApplyPackageSettingsForVerification(EditorPackageSettingsRequest request) => ApplyPackageSettings(request);

    private async Task EditPackageSettings()
    {
        if (CurrentDocument is null) return;
        var dialog = new PackageSettingsDialog(CurrentDocument);
        var result = await dialog.ShowDialog<EditorPackageSettingsRequest?>(this);
        if (result is not null) ApplyPackageSettings(result);
    }

    private void ApplyPackageSettings(EditorPackageSettingsRequest request)
    {
        if (CurrentDocument is not { } document) return;
        try
        {
            if (EditorPackageSettings.Apply(document, document.SelectedChartId, request))
            {
                RefreshDocument();
                Report(_text.Format("Message.SettingsApplied", document.SelectedChartId));
            }
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void CopyChart()
    {
        if (CurrentDocument is not { } document) return;
        try
        {
            var source = document.SelectedChart;
            var (id, file, key) = EditorDifficultyNames.SuggestCopy(document);
            document.Execute(new CopyChartCommand(source.Id, id, file, difficultyKey: key));
            RefreshDocument();
            Report(_text.Format("Message.ChartCopied", id));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void DeleteChart()
    {
        if (CurrentDocument is not { } document) return;
        try
        {
            var id = document.SelectedChartId;
            document.Execute(new DeleteChartCommand(id));
            RefreshDocument();
            Report(_text.Format("Message.ChartDeleted", id));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void MoveChart(int direction)
    {
        if (CurrentDocument is not { } document) return;
        var id = document.SelectedChartId;
        var source = document.Charts.FindIndex(chart => chart.Id == id);
        var destination = source + direction;
        if (source < 0 || destination < 0 || destination >= document.Charts.Count) return;
        document.Execute(new MoveChartCommand(id, destination));
        RefreshDocument();
    }

    private void RetryStageClicked(object? sender, RoutedEventArgs e)
    {
        if (CurrentDocument is not null && !_ioBusy && !_closePending) _workspace.Retry();
    }

    private void StageEvent(StageMessage message)
    {
        if (CurrentDocument is null || _factory.IsShuttingDown || _modal.Blocks(message)) return;
        var data = message.Data;
        switch (message.Event)
        {
            case "ready":
                _clock.ResetSession();
                _loadedDocument = null;
                _stageView.Host.SetGodotHandle((nint)data.GetProperty("hwnd").GetInt64(), TryGetPlatformHandle()?.Handle ?? 0);
                _service.Send(new { command = "workbenchModal", blocked = _modal.IsOpen, epoch = _modal.Epoch });
                _workspace.Ready();
                _timingKeys.Sync();
                SendDocument();
                SendContextText();
                SendPreviewPreferences();
                SendAuthoring();
                _service.Send(new { command = "wheelDirection", reversed = WheelReversed });
                Report(_text["Message.GodotReady"]);
                UpdateNativeStatus();
                break;
            case "contextAction":
                var kind = data.GetProperty("kind").GetString();
                var value = data.GetProperty("value").GetString();
                if (kind == "track" && Enum.TryParse<EditorTrack>(value, out var track)) _tools.SelectTrack(track);
                else if (kind == "type" && Enum.TryParse<V2NoteType>(value, out var type)) _tools.SelectPlacementType(type);
                else if (kind == "tool" && value is "SELECT" or "PATH") _tools.SelectTool(value);
                else if (kind == "volume" && double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var volume) && double.IsFinite(volume))
                    _previewVolume = Math.Clamp(volume, 0, 100);
                else if (kind == "effects" && value is "Full" or "Reduced" or "Off") _previewEffects = value;
                _preferences.Changed();
                break;
            case "selection":
                _stageView.Host.FocusNative();
                _selection.FromStage(CurrentDocument, data);
                _selection.Show(CurrentDocument, _inspector);
                _timing.ClearRowSelection();
                Report(_text.Format("Message.SelectionSynced", _selectedNoteIds.Length));
                break;
            case "place":
                _tools.SetPlacementWidth(data.GetProperty("width").GetDouble());
                _editing.PlaceNote(CurrentDocument, data);
                break;
            case "pathAdd": _editing.AddPathNode(CurrentDocument, data); break;
            case "pathMove": _editing.MovePathPoint(CurrentDocument, data); break;
            case "noteMove": _editing.MoveNote(CurrentDocument, _selectedNoteIds, data); break;
            case "noteResize": _editing.ResizeNote(CurrentDocument, data); break;
            case "timelineDuration":
                if (data.GetProperty("revision").GetInt64() == _stageRevision)
                    _rhythmTimeline.SetAudioDuration(data.GetProperty("seconds").GetDouble());
                break;
            case "tick":
                var previousPlaying = _clock.Playing;
                if (!Ready || !_clock.Accept(data, _stageRevision)) break;
                TickCount++;
                if (previousPlaying != _clock.Playing) UpdatePlaybackText();
                var speed = data.GetProperty("playerSpeed").GetDouble();
                var rate = data.GetProperty("playbackRate").GetDouble();
                if (_playerSpeed != speed || _playbackRate != rate)
                { _playerSpeed = speed; _playbackRate = rate; UpdatePlaybackParameters(); }
                break;
            case "input":
                LastStageInput = data.GetProperty("detail").GetString() ?? "";
                Report("Godot · " + LastStageInput);
                break;
            case "key": HandleStageKey(data); break;
            case "size": _stageView.Host.QueueSync(); UpdateNativeStatus(); break;
            case "error":
                _loadedDocument = null;
                Report("Stage error · " + data.GetProperty("message").GetString());
                break;
            case "exited":
                _loadedDocument = null;
                _stageView.Host.SetGodotHandle(0);
                _clock.ResetSession();
                UpdatePlaybackText();
                UpdateNativeStatus();
                _timingKeys.ResetHeld();
                _workspace.Stopped();
                Report(_text["Status.GodotStopped"]);
                break;
        }
    }

    private void HandleStageKey(JsonElement data)
    {
        var name = data.GetProperty("key").GetString() ?? "";
        if (name.Length == 4 && name.StartsWith("Key") && char.IsDigit(name[3])) name = "D" + name[3];
        if (!Enum.TryParse<Key>(name, out var key)) return;
        if (WorkbenchTimingKeys.IsTimingKey(key) && data.TryGetProperty("loadRevision", out var load) &&
            (load.ValueKind != JsonValueKind.Number || !load.TryGetInt64(out var generation) || generation != _stageLoadRevision)) return;
        if (data.TryGetProperty("pressed", out var pressed) && pressed.ValueKind == JsonValueKind.False)
        { _timingKeys.Release(key); return; }
        var modifiers = KeyModifiers.None;
        if (data.GetProperty("ctrl").GetBoolean()) modifiers |= KeyModifiers.Control;
        if (data.GetProperty("shift").GetBoolean()) modifiers |= KeyModifiers.Shift;
        if (data.GetProperty("alt").GetBoolean()) modifiers |= KeyModifiers.Alt;
        HandleShortcut(key, modifiers, null, native: true, echo: data.TryGetProperty("echo", out var e) && e.ValueKind == JsonValueKind.True);
    }

    private async Task OpenPackage(string? path = null)
    {
        if (_ioBusy || _curves.BlockPending()) return;
        if (CurrentDocument?.HasUnsavedChanges == true)
        {
            Report(_text["Message.SaveBeforeOpen"]);
            return;
        }
        _ioBusy = true;
        try
        {
            var previous = CurrentDocument;
            var version = previous?.ChangeVersion;
            var directory = path ?? await (PickOpenFolderForVerification?.Invoke() ?? PickFolder("File.Open"));
            if (directory is null || !ReferenceEquals(previous, CurrentDocument) ||
                previous?.ChangeVersion != version) return;
            if (_curves.BlockPending()) return;
            if (CurrentDocument?.HasUnsavedChanges == true) { Report(_text["Message.SaveBeforeOpen"]); return; }
            await _curves.ReplaceDocument(() => _session.OpenAsync(directory));
            RefreshDocument();
            Report(_text.Format("Message.Opened", CurrentDocument!.Title));
            _workspace.Enter();
        }
        catch (Exception ex) { Report(_text.Format("Message.FileError", ex.Message)); }
        finally { _ioBusy = false; }
    }

    private async Task SavePackage(bool saveAs, string? destination = null)
    {
        if (_ioBusy || CurrentDocument is not { } document || _curves.BlockPending()) return;
        _timingKeys.ResetHeld();
        _ioBusy = true;
        try
        {
            var directory = destination ?? (saveAs || document.IsUnpersisted ? await PickFolder("File.SaveAs") : document.PackageDirectory);
            if (directory is null || _curves.BlockPending() || !await _curves.ConfirmVersion(document) || _curves.BlockPending()) return;
            var result = await _session.SaveAsync(directory);
            RefreshDocument(false);
            Report(document.HasUnsavedChanges || _curves.HasDraft
                ? _text.Format("Message.SavedWithNewEdits", result.DestinationDirectory)
                : _text.Format("Message.Saved", result.DestinationDirectory));
        }
        catch (Exception ex) { Report(_text.Format("Message.FileError", ex.Message)); }
        finally { _ioBusy = false; }
    }

    private async Task<string?> PickFolder(string titleKey)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = _text[titleKey],
            AllowMultiple = false,
        });
        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private void RefreshDocument(bool reloadStage = true, bool refreshChartChoices = true)
    {
        var document = CurrentDocument;
        _selection.FromDocument(document);
        _selection.Show(document, _inspector);
        _timing.SetDocument(document);
        _rhythmTimeline.Refresh(document?.SelectedChart);
        if (document is not null) document.GridDivisor = _snapDenominator;
        if (refreshChartChoices) RefreshChartChoices();
        UpdateDocumentChrome();
        UpdateSnapText();
        if (reloadStage) SendDocument();
    }

    private void RefreshChartChoices()
    {
        var choice = this.FindControl<ComboBox>("ChartChoice")!;
        _refreshingCharts = true;
        try
        {
            choice.Items.Clear();
            foreach (var chart in CurrentDocument?.Charts ?? Enumerable.Empty<EditableChart>())
                choice.Items.Add(new ComboBoxItem
                {
                    Content = $"{chart.Id} · {chart.Difficulty} {chart.Level?.ToString() ?? "—"}",
                    Tag = chart.Id,
                });
            choice.SelectedItem = choice.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(item => Equals(item.Tag, CurrentDocument?.SelectedChartId));
        }
        finally { _refreshingCharts = false; }
    }

    private void ChartChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingCharts || CurrentDocument is not { } document ||
            this.FindControl<ComboBox>("ChartChoice")!.SelectedItem is not ComboBoxItem { Tag: string id } ||
            id == document.SelectedChartId) return;
        document.SelectChart(id);
        _timingKeys.ResetHeld();
        // Avalonia is still committing this ComboBox selection. Its items must remain stable
        // until that operation completes; changing the active chart does not change the list.
        RefreshDocument(refreshChartChoices: false);
        Report(_text.Format("Message.ChartSelected", id));
    }

    internal int GuideLevelForVerification(Key key) => _timingKeys.GuideLevel(key);
    internal long StageRevision => _stageRevision;
    internal bool LastStageUpdateIncremental { get; private set; }

    private void SendDocument()
    {
        if (!Ready || CurrentDocument is not { } document) return;
        var chart = Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(
            document.BuildChartSnapshot(document.SelectedChartId), requireMainJudgement: false));
        var audioPath = _session.ResolveDigestAudio();
        _rhythmTimeline.SetAudioSource(audioPath);
        LastStageUpdateIncremental = ReferenceEquals(_loadedDocument, document) &&
            _loadedChartId == document.SelectedChartId && _loadedAudioPath == audioPath;
        var selection = _selection.StageIds(document);
        var revision = ++_stageRevision;
        if (LastStageUpdateIncremental)
            _service.Update(chart, document.Title, document.Artist, audioPath, revision, selection,
                document.SelectedChart.Difficulty.ToString(), document.SelectedChart.Level, document.SelectedChart.Key.ToString());
        else
        {
            _timingKeys.ResetHeld(); _stageLoadRevision = revision;
            _service.Load(chart, document.Title, document.Artist, audioPath, _clock.Bar, revision, selection,
                document.SelectedChart.Difficulty.ToString(), document.SelectedChart.Level, document.SelectedChart.Key.ToString());
        }
        _loadedDocument = document;
        _loadedChartId = document.SelectedChartId;
        _loadedAudioPath = audioPath;
        SendAuthoring();
        _service.Send(new { command = "gamePreview", enabled = _stageView.GamePreviewEnabled });
    }

    private WorkbenchPreferences CapturePreferences() => new()
    {
        Language = _text.CurrentLanguage, Grid = _snapDenominator, WheelReversed = WheelReversed,
        ObjectSnap = _tools.ObjectSnapEnabled,
        PositionSnap = _tools.PositionSnapEnabled, PlacementWidth = _tools.PlacementWidth,
        Volume = _previewVolume, Effects = _previewEffects,
        Motion = this.FindControl<ComboBox>("MotionChoice")!.SelectedIndex,
    };

    private void SendPreviewPreferences() => _service.Send(new
    { command = "previewPreferences", volume = _previewVolume, effects = _previewEffects });

    private void SendAuthoring()
    {
        _timeline.Track = _tools.PlacementTrack;
        _preferences.Changed();
        _service.Send(new { command = "objectSnap", enabled = _tools.ObjectSnapEnabled });
        _service.SetAuthoring(_tool, _tools.PlacementType.ToString(), _tools.PlacementTrack.ToString(),
            _tools.PlacementWidth, _snapDenominator, _tools.PositionSnapEnabled);
    }

    private void UpdateDocumentChrome()
    {
        var document = CurrentDocument;
        this.FindControl<TextBlock>("SongTitle")!.Text = document?.Title ?? _text["Document.None"];
        this.FindControl<TextBlock>("SongMeta")!.Text = document is null ? _text["Header.Prototype"] :
            $"{document.SelectedChart.Difficulty} {document.SelectedChart.Level?.ToString() ?? "—"} / {document.SelectedChart.Id} · {document.SelectedChart.Notes.Count} NOTES";
        Title = document is null ? "DynaMaker UV" : $"DynaMaker UV — {document.Title}{(document.HasUnsavedChanges ? " *" : "")}";
    }

    private void Undo() => ApplyHistory(false);

    private void Redo() => ApplyHistory(true);

    private void ApplyHistory(bool redo)
    {
        if (CurrentDocument is not { } document || (redo ? !document.CanRedo : !document.CanUndo)) return;
        var selection = document.Selection;
        var selectedIds = _selectedNoteIds;
        _timingKeys.ResetHeld();
        if (redo) document.Redo();
        else document.Undo();
        if (document.Selection.Kind == EditorSelectionKind.None)
        {
            if (selection.Kind == EditorSelectionKind.Note && document.SelectedChart.Notes.Any(note => note.Id == selection.Id))
                document.Select(selection);
            else if (selection.Kind == EditorSelectionKind.PathNode && document.SelectedChart.Notes
                    .SelectMany(note => note.Nodes).Any(node => node.Id == selection.Id))
                document.Select(selection);
            else if ((selection.Kind == EditorSelectionKind.Bpm || selection.Kind == EditorSelectionKind.Scroll) &&
                ExactBarTimeText.TryParse(selection.Id, out var timingTime))
            {
                var exists = selection.Kind == EditorSelectionKind.Bpm
                    ? document.SelectedChart.Bpms.Any(item => item.Time == timingTime)
                    : document.SelectedChart.ScrollSpeeds.Any(item => item.Time == timingTime);
                if (exists) document.Select(selection);
            }
            if (document.Selection.Kind == EditorSelectionKind.Note)
                _selection.FromDocument(document, selectedIds);
        }
        RefreshDocument();
    }

    private void TogglePlayback()
    {
        if (!Ready || CurrentDocument is null) return;
        var next = _clock.Toggle(_service.TogglePlayback);
        Report(_text[next ? "Message.PlayRequested" : "Message.PauseRequested"]);
    }

    private void TimelineSeek(object? sender, double bar) => SeekBar(bar);

    private void SeekSeconds(double delta)
    {
        if (CurrentDocument is not null) SeekBar(_rhythmTimeline.Advance(_clock.Bar, delta), snap: false);
    }

    private void SeekBar(ExactBarTime anchor) => SeekBar(anchor.ToDouble(), snap: false, anchor);

    private void SeekBar(double bar, bool snap = true, ExactBarTime? anchor = null)
    {
        if (CurrentDocument is not { } document) return;
        var target = _rhythmTimeline.ResolveSeek(bar, snap && document.SnapEnabled ? _snapDenominator : null, anchor);
        _clock.Seek(target.Bar, _rhythmTimeline.SecondsAt(target.Bar), _service.Seek, target.Anchor);
        Report(_text.Format("Message.Seek", target.Bar));
    }

    private void ChangeSnap(int direction, bool fine)
    {
        if (fine) _snapDenominator = Math.Clamp(_snapDenominator + direction, 1, 64);
        else
        {
            var index = Array.IndexOf(SnapSteps, _snapDenominator);
            if (index < 0) index = Array.FindLastIndex(SnapSteps, step => step < _snapDenominator);
            _snapDenominator = SnapSteps[Math.Clamp(index + direction, 0, SnapSteps.Length - 1)];
        }
        if (CurrentDocument is { } document) document.GridDivisor = _snapDenominator;
        SendAuthoring();
        UpdateSnapText();
        Report(_text.Format("Message.SnapChanged", _snapDenominator));
    }

    private void ChangePlayerSpeed(int direction)
    {
        _playerSpeed = Math.Clamp(Math.Round((_playerSpeed + direction * .1) * 10) / 10, .1, 2.0);
        _service.SetPlayerSpeed(_playerSpeed);
        UpdatePlaybackParameters();
    }

    private void ChangePlaybackRate(int direction)
    {
        _playbackRate = Math.Clamp(Math.Round((_playbackRate + direction * .1) * 10) / 10, .2, 2.0);
        _service.SetPlaybackRate(_playbackRate);
        UpdatePlaybackParameters();
    }

    private void FloatStage()
    {
        if (_stageView.Host.Diagnostics().WindowTitle != Title) { Report(_text["Message.AlreadyFloating"]); return; }
        _factory.FloatStage();
        Report(_text["Message.Floated"]);
        Dispatcher.UIThread.Post(UpdateNativeStatus, DispatcherPriority.Render);
    }

    private void DockStage()
    {
        // Dock owns reparenting and its layout lifecycle. Clearing Layout here tears down
        // the whole workspace, even when the stage is already docked.
        _factory.DockStage();
        Report(_text["Message.Restored"]);
        Dispatcher.UIThread.Post(UpdateNativeStatus, DispatcherPriority.Render);
    }

    private void AddPanel()
    {
        _factory.AddAnalysisPanel();
        Report(_text["Message.AnalysisAdded"]);
    }

    private void MotionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_factory is null) return;
        var index = (sender as ComboBox)?.SelectedIndex ?? 0;
        var profile = index switch { 1 => SukiAnimationProfile.Lite, 2 => SukiAnimationProfile.Minimal, _ => SukiAnimationProfile.Normal };
        SukiAnimation.SetProfile(this, profile);
        _preferences.Changed();
        if (this.FindControl<TextBlock>("StatusText") is { } status)
            status.Text = _text.Format("Message.MotionProfile", _text[index switch { 1 => "Motion.Lite", 2 => "Motion.Minimal", _ => "Motion.Normal" }]);
    }

    private void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_factory is null) return;
        _text.SetLanguage((sender as ComboBox)?.SelectedIndex == 1 ? "zh-CN" : "en");
        _preferences.Changed();
        Report(_text["Message.LanguageSwitched"]);
    }

    private void SendContextText()
    {
        var keys = new[] { "Context.Left", "Context.Center", "Context.Right", "Context.Volume", "Context.Effects",
            "Context.Off", "Context.Reduced", "Context.Full", "Tools.Select", "Tools.Path" }
            .Concat(Enum.GetNames<V2NoteType>().Select(type => "Inspector." + type));
        _service.Send(new { command = "contextText", labels = keys.ToDictionary(key => key, key => _text[key]) });
    }

    private void UpdateLocalizedState()
    {
        SendContextText();
        _workspace.UpdateCaption();
        UpdateDocumentChrome();
        _clock.RefreshReadout();
        UpdateSnapText();
        UpdatePlaybackText();
        UpdatePlaybackParameters();
        UpdateNativeStatus();
    }

    private void UpdateSnapText() => this.FindControl<TextBlock>("SnapText")!.Text = _text.Format("Transport.Snap", _snapDenominator);
    private void UpdatePlaybackParameters()
    {
        this.FindControl<TextBlock>("PlayerSpeedText")!.Text = _text.Format("Transport.PlayerSpeed", _playerSpeed);
        this.FindControl<TextBlock>("PlaybackRateText")!.Text = _text.Format("Transport.PlaybackRate", _playbackRate);
    }

    private void UpdatePlaybackText()
    {
        _stageView.SetPlaying(_clock.Playing);
        this.FindControl<IconButton>("TransportButton")!.Content = _text[_clock.Playing ? "Transport.Pause" : "Transport.Play"];
    }

    private void UpdateNativeStatus()
    {
        var d = _stageView.Host.Diagnostics();
        this.FindControl<TextBlock>("NativeText")!.Text = d.Child == 0 ? _text["Status.NativeWaiting"] :
            $"PID {_service.ProcessId} · {_text[d.Embedded ? "Status.Embedded" : "Status.Reparenting"]} · {d.Bounds} · {d.Scale:0.##}×";
    }

    private void ToggleShortcuts()
    {
        var overlay = this.FindControl<Border>("ShortcutOverlay")!;
        _timingKeys.ResetHeld();
        overlay.IsVisible = !overlay.IsVisible;
        _stageView.Host.SetOccluded(overlay.IsVisible);
    }

    private void Report(string message) => this.FindControl<TextBlock>("StatusText")!.Text = message;
    private void PlayClicked(object? sender, RoutedEventArgs e) => TogglePlayback();
    private void RestoreClicked(object? sender, RoutedEventArgs e)
    {
        _factory.RestoreWorkspace();
        Report(_text["Message.Restored"]);
        Dispatcher.UIThread.Post(UpdateNativeStatus, DispatcherPriority.Render);
    }
    private void AddPanelClicked(object? sender, RoutedEventArgs e) => AddPanel();
    private void ShortcutsClicked(object? sender, RoutedEventArgs e) => ToggleShortcuts();
    private void ThemeClicked(object? sender, RoutedEventArgs e) => Report(_text["Message.ThemeActive"]);
    private async void NewClicked(object? sender, RoutedEventArgs e) => await CreatePackage();
    private async void OpenClicked(object? sender, RoutedEventArgs e) => await OpenPackage();
    private void AddChartClicked(object? sender, RoutedEventArgs e) { if (!_ioBusy && !_closePending && !ShortcutsVisible) _difficulty.Show(); }
    private void ImportBpmClicked(object? sender, RoutedEventArgs e)
    {
        if (!_ioBusy && !_closePending && !ShortcutsVisible && !_modal.IsOpen && !_curves.BlockPending()) _bpmImport.Show();
    }
    private async void SettingsClicked(object? sender, RoutedEventArgs e) => await EditPackageSettings();
    private void CopyChartClicked(object? sender, RoutedEventArgs e) => CopyChart();
    private void MoveChartUpClicked(object? sender, RoutedEventArgs e) => MoveChart(-1);
    private void MoveChartDownClicked(object? sender, RoutedEventArgs e) => MoveChart(1);
    private void DeleteChartClicked(object? sender, RoutedEventArgs e) => DeleteChart();
    private async void SaveClicked(object? sender, RoutedEventArgs e) => await SavePackage(false);
    private async void SaveAsClicked(object? sender, RoutedEventArgs e) => await SavePackage(true);
    private async void VideoExportClicked(object? sender, RoutedEventArgs e) => await _videoExport.Show(this,
        _session, _userStore, _text, () => _ioBusy || _closePending || ShortcutsVisible || _modal.IsOpen || _curves.BlockPending(),
        busy => _ioBusy = busy, () => { _timingKeys.ResetHeld(); _clock.Pause(_service.TogglePlayback); }, Report);
    private void CopyNotesClicked(object? sender, RoutedEventArgs e) => HandleShortcut(Key.C, KeyModifiers.Control, null);
    private void PasteNotesClicked(object? sender, RoutedEventArgs e) => HandleShortcut(Key.V, KeyModifiers.Control, null);
    private void UndoClicked(object? sender, RoutedEventArgs e) => Undo();
    private void RedoClicked(object? sender, RoutedEventArgs e) => Redo();
    private void DeleteClicked(object? sender, RoutedEventArgs e) => HandleShortcut(Key.Delete, KeyModifiers.None, null);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((!IsTextInput(e.Source) || (e.Key == Key.S &&
             (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)))) &&
            HandleShortcut(e.Key, e.KeyModifiers, e.Source))
            e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _timingKeys.Release(e.Key);
        base.OnKeyUp(e);
    }

    private static bool IsTextInput(object? source) => source is Avalonia.Visual visual &&
        visual.GetVisualAncestors().Prepend(visual).Any(v => v is TextBox or Avalonia.Controls.NumericUpDown or ComboBox);

    private bool HandleShortcut(Key key, KeyModifiers modifiers, object? source, bool native = false, bool echo = false)
    {
        if (_modal.IsOpen) { if (key == Key.Escape) _modal.Close(); return false; } // Form controls keep their own keys.
        if (ShortcutsVisible)
        {
            if (key is not (Key.Escape or Key.F1)) return true;
            ToggleShortcuts();
            return true;
        }
        if (CurrentDocument is null && !(modifiers == KeyModifiers.Control && key is Key.N or Key.O)) return false;
        if (modifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            switch (key)
            {
                case Key.F: FloatStage(); return true;
                case Key.D: DockStage(); return true;
                case Key.A: AddPanel(); return true;
                case Key.S: _ = SavePackage(true); return true;
            }
        }
        if (modifiers == KeyModifiers.Control)
        {
            switch (key)
            {
                case Key.N: _ = CreatePackage(); return true;
                case Key.O: _ = OpenPackage(); return true;
                case Key.S: _ = SavePackage(false); return true;
                case Key.Z: Undo(); return true;
                case Key.Y: Redo(); return true;
                case Key.C:
                    if (CurrentDocument is not null && _selection.CanCopyNotes)
                    {
                        var copied = _noteClipboard.Copy(CurrentDocument, _selectedNoteIds);
                        Report(_text.Format("Message.NotesCopied", copied));
                        return true;
                    }
                    return false;
                case Key.V:
                    if (!_ioBusy && CurrentDocument is not null && _noteClipboard.Count > 0)
                    {
                        try
                        {
                            var anchor = EditorGeometry.Snap(_clock.ExactBar ?? ExactBarTime.FromDouble(Math.Max(0, _clock.Bar)), _snapDenominator);
                            var pasted = _noteClipboard.Paste(CurrentDocument, anchor);
                            SelectNotes(pasted);
                            RefreshDocument();
                            Report(_text.Format("Message.NotesPasted", pasted.Length));
                        }
                        catch (Exception ex) { Report(ex.Message); }
                        return true;
                    }
                    return false;
            }
            return false;
        }
        if (modifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return false;
        if (_timingKeys.Press(key, modifiers, native, echo, _ioBusy || _closePending || !IsEffectivelyEnabled)) return true;
        switch (key)
        {
            case Key.Space when source is not Button: TogglePlayback(); return true;
            case Key.B when modifiers == KeyModifiers.None:
                WheelReversed = !WheelReversed;
                _preferences.Changed();
                _service.Send(new { command = "wheelDirection", reversed = WheelReversed });
                Report(_text[WheelReversed ? "Message.WheelReversed" : "Message.WheelNormal"]);
                return true;
            case Key.R when modifiers == KeyModifiers.None: SeekBar(ExactBarTime.Zero); return true;
            case Key.A: SeekSeconds(modifiers == KeyModifiers.Shift ? -.01 : -1); return true;
            case Key.D: SeekSeconds(modifiers == KeyModifiers.Shift ? .01 : 1); return true;
            case Key.C: ChangeSnap(-1, modifiers == KeyModifiers.Shift); return true;
            case Key.V: ChangeSnap(1, modifiers == KeyModifiers.Shift); return true;
            case Key.Q when modifiers == KeyModifiers.None: ChangePlayerSpeed(-1); return true;
            case Key.E when modifiers == KeyModifiers.None: ChangePlayerSpeed(1); return true;
            case Key.W when modifiers == KeyModifiers.None: ChangePlaybackRate(1); return true;
            case Key.S when modifiers == KeyModifiers.None: ChangePlaybackRate(-1); return true;
            case Key.D1 when modifiers == KeyModifiers.None: _tools.SelectPlacementType(V2NoteType.Tap); return true;
            case Key.D2 when modifiers == KeyModifiers.None: _tools.SelectPlacementType(V2NoteType.Drag); return true;
            case Key.D3 when modifiers == KeyModifiers.None: _tools.SelectPlacementType(V2NoteType.Hold); return true;
            case Key.D4 when modifiers == KeyModifiers.None: _tools.SelectTool("SELECT"); return true;
            case Key.D5 when modifiers == KeyModifiers.None: _tools.SelectPlacementType(V2NoteType.ExTap); return true;
            case Key.D6 when modifiers == KeyModifiers.None: _tools.SelectPlacementType(V2NoteType.Mixer); return true;
            case Key.D7 when modifiers == KeyModifiers.None: _tools.SelectPlacementType(V2NoteType.Mine); return true;
            case Key.Up when modifiers == KeyModifiers.None: _tools.CycleTrack(); return true;
            case Key.Delete:
            case Key.Back:
                if (!_selection.CanDeleteNote) return false;
                _editing.DeleteSelection(CurrentDocument, SelectedNoteId, _selectedNoteIds);
                return true;
            case Key.F1: ToggleShortcuts(); return true;
            default: return false;
        }
    }

    private static string TitleCase(string value) => char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
