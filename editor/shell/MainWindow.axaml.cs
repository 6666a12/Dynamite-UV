using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;
using SukiUI.ControlsAnimation;
using SukiUI.Theme;
using Ursa.Controls;

namespace DynamiteUniverse.Editor.Shell;

public sealed partial class MainWindow : Window
{
    private static readonly int[] SnapSteps = { 1, 2, 4, 8, 12, 16, 24, 32, 48, 64 };
    private readonly GodotStageService _service = new();
    private readonly EditorDocumentSession _session = new();
    private readonly StageView _stageView = new();
    private readonly InspectorView _inspector = new();
    private readonly ToolsView _tools = new();
    private readonly TimingView _timing = new();
    private readonly WorkbenchFactory _factory;
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private readonly ShellChartEditor _editing;
    private bool _playing;
    private bool _ioBusy;
    private double _bar;
    private double _second;
    private double _playerSpeed = 1.0;
    private double _playbackRate = 1.0;
    private string _tool = "SELECT";
    private int _snapDenominator = EditorDocument.DefaultGridDivisor;
    private bool _refreshingCharts;
    private string[] _selectedNoteIds = Array.Empty<string>();
    private EditorDocument? _loadedDocument;
    private string? _loadedChartId;
    private string? _loadedAudioPath;
    private long _stageRevision;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _editing = new ShellChartEditor(_text, () => RefreshDocument(), Report);
        _factory = new WorkbenchFactory(_stageView, _inspector, _tools, _timing);
        var dock = this.FindControl<DockControl>("Dock")!;
        dock.Factory = _factory;
        dock.Layout = _factory.CreateLayout();
        dock.InitializeFactory = true;
        dock.InitializeLayout = true;
        _stageView.FloatRequested += FloatStage;
        _stageView.DockRequested += DockStage;
        _stageView.PlayRequested += TogglePlayback;
        _inspector.SpeedChanged += (id, speed) => _editing.ApplySpeed(CurrentDocument, _selectedNoteIds, id, speed);
        _inspector.LocateRequested += (id, nodeId) =>
        {
            SelectNote(id);
            if (nodeId is not null) CurrentDocument?.Select(new EditorSelection(EditorSelectionKind.PathNode, nodeId));
            _inspector.ShowDocument(CurrentDocument);
            var note = CurrentDocument!.SelectedChart.Notes.Single(n => n.Id == id);
            SeekBar((nodeId is null ? note.Time : note.Nodes.Single(n => n.Id == nodeId).Time).ToDouble(), snap: false);
            SendDocument();
        };
        _inspector.CurveChanged += (id, nodeId, curve) => _editing.ApplyCurve(CurrentDocument, id, nodeId, curve);
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
        _tools.Notice += _ => Report(_text["Message.WorkspaceNotice"]);
        _timing.CommandRequested += command => _editing.ApplyTiming(CurrentDocument, command);
        _timing.ErrorRequested += Report;
        _service.MessageReceived += message => Dispatcher.UIThread.Post(() => StageEvent(message));
        _service.RuntimeError += error => Dispatcher.UIThread.Post(() => Report("Godot: " + error));
        _text.LanguageChanged += UpdateLocalizedState;
        UpdateLocalizedState();
        Opened += (_, _) => StartStage();
        Closing += (_, _) =>
        {
            _factory.IsShuttingDown = true;
            _text.LanguageChanged -= UpdateLocalizedState;
            _stageView.Host.SetGodotHandle(0);
            _service.Dispose();
        };
    }

    internal StageView Stage => _stageView;
    internal GodotStageService StageServiceForVerification => _service;
    internal TimingView Timing => _timing;
    internal WorkbenchFactory LayoutFactory => _factory;
    internal bool Ready { get; private set; }
    internal int StageProcessId => _service.ProcessId;
    internal int TickCount { get; private set; }
    internal string LastStageInput { get; private set; } = "";
    internal double CurrentBar => _bar;
    internal double CurrentSecond => _second;
    internal bool Playing => _playing;
    internal double PlayerSpeed => _playerSpeed;
    internal double PlaybackRate => _playbackRate;
    internal bool WheelReversed { get; private set; }
    internal string CurrentTool => _tool;
    internal int SnapDenominator => _snapDenominator;
    internal string? SelectedNoteId { get; private set; }
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
        CurrentDocument?.Select(new EditorSelection(EditorSelectionKind.Note, id));
        SelectedNoteId = id;
        _selectedNoteIds = new[] { id };
        _inspector.ShowDocument(CurrentDocument);
    }
    internal void SelectNotesForVerification(params string[] ids)
    {
        if (CurrentDocument is null || ids.Length == 0) return;
        SelectNoteForVerification(ids[^1]);
        _selectedNoteIds = ids.Distinct().ToArray();
        _inspector.Select(CurrentDocument, _selectedNoteIds, ids[^1]);
    }
    internal async Task CreatePackageForVerification(EditorCreateForm form) => await CreatePackage(form);
    internal async Task AddChartForVerification(EditorCreateForm form) => await AddChart(form);
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
        if (_ioBusy) return;
        if (CurrentDocument?.HasUnsavedChanges == true)
        {
            Report(_text["Message.SaveBeforeOpen"]);
            return;
        }
        EditorCreateResult? result;
        if (form is null)
        {
            var dialog = new CreateChartDialog(EditorCreateMode.NewPackage, CurrentDocument, _snapDenominator);
            result = await dialog.ShowDialog<EditorCreateResult?>(this);
        }
        else result = EditorCreateRequest.Build(EditorCreateMode.NewPackage, form, _snapDenominator);
        if (result?.DraftRequest is null) return;
        _ioBusy = true;
        try
        {
            await Task.Run(() => _session.CreateDraft(result.DraftRequest));
            _bar = 0;
            _second = 0;
            RefreshDocument();
            Report(_text.Format("Message.Created", CurrentDocument!.Title));
        }
        catch (Exception ex) { Report(_text.Format("Message.FileError", ex.Message)); }
        finally { _ioBusy = false; }
    }

    private async Task AddChart(EditorCreateForm? form = null)
    {
        if (_ioBusy || CurrentDocument is not { } document) return;
        EditorCreateResult? result;
        if (form is null)
        {
            var dialog = new CreateChartDialog(EditorCreateMode.AddDifficulty, document, _snapDenominator);
            result = await dialog.ShowDialog<EditorCreateResult?>(this);
        }
        else result = EditorCreateRequest.Build(EditorCreateMode.AddDifficulty, form, _snapDenominator);
        if (result?.Chart is null) return;
        try
        {
            _session.AddChart(result.Chart);
            RefreshDocument();
            Report(_text.Format("Message.ChartAdded", result.Chart.Id));
        }
        catch (Exception ex) { Report(ex.Message); }
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
            var baseId = source.Id + "-copy";
            var id = baseId;
            for (var suffix = 2; document.Charts.Any(chart => chart.Id == id); suffix++) id = $"{baseId}-{suffix}";
            document.Execute(new CopyChartCommand(source.Id, id, $"charts/{id}.json"));
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

    private void StartStage()
    {
        if (Environment.GetEnvironmentVariable("DYNA_WORKBENCH_NO_GODOT") == "1") return;
        try { _service.Start(TryGetPlatformHandle()?.Handle ?? 0); }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void StageEvent(StageMessage message)
    {
        var data = message.Data;
        switch (message.Event)
        {
            case "ready":
                Ready = true;
                _loadedDocument = null;
                _stageView.Host.SetGodotHandle((nint)data.GetProperty("hwnd").GetInt64());
                this.FindControl<TextBlock>("ReadyBadge")!.Text = _text["Status.GodotLive"];
                SendDocument();
                SendContextText();
                _service.Send(new { command = "wheelDirection", reversed = WheelReversed });
                Report(_text[CurrentDocument is null ? "Message.OpenPackage" : "Message.GodotReady"]);
                UpdateNativeStatus();
                break;
            case "contextAction":
                var kind = data.GetProperty("kind").GetString();
                var value = data.GetProperty("value").GetString();
                if (kind == "track" && Enum.TryParse<EditorTrack>(value, out var track)) _tools.SelectTrack(track);
                else if (kind == "type" && Enum.TryParse<V2NoteType>(value, out var type)) _tools.SelectPlacementType(type);
                else if (kind == "tool" && value is "SELECT" or "PATH") _tools.SelectTool(value);
                break;
            case "selection":
                _stageView.Host.FocusNative();
                var ids = data.GetProperty("ids").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                _selectedNoteIds = data.TryGetProperty("noteIds", out var noteIds)
                    ? noteIds.EnumerateArray().Select(x => x.GetString() ?? "").Distinct().ToArray()
                    : ids;
                SelectedNoteId = data.GetProperty("parentId").GetString();
                if (ids.Length == 0 || string.IsNullOrEmpty(SelectedNoteId))
                {
                    SelectedNoteId = null;
                    _selectedNoteIds = Array.Empty<string>();
                    CurrentDocument?.Select(EditorSelection.None);
                    _inspector.Select(CurrentDocument, Array.Empty<string>(), "");
                    break;
                }
                var nodeId = data.TryGetProperty("nodeId", out var selectedNode) ? selectedNode.GetString() : null;
                CurrentDocument?.Select(nodeId is null
                    ? new EditorSelection(EditorSelectionKind.Note, SelectedNoteId)
                    : new EditorSelection(EditorSelectionKind.PathNode, nodeId));
                _inspector.Select(CurrentDocument, _selectedNoteIds, SelectedNoteId);
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
            case "tick":
                TickCount++;
                _second = data.GetProperty("second").GetDouble();
                _bar = data.GetProperty("bar").GetDouble();
                _playing = data.GetProperty("playing").GetBoolean();
                _playerSpeed = data.GetProperty("playerSpeed").GetDouble();
                _playbackRate = data.GetProperty("playbackRate").GetDouble();
                this.FindControl<TimelineView>("Timeline")!.CurrentBar = _bar;
                _timing.SetCurrentBar(_bar);
                UpdateTimeLabel();
                UpdatePlaybackText();
                UpdatePlaybackParameters();
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
                Ready = false;
                _loadedDocument = null;
                _stageView.Host.SetGodotHandle(0);
                this.FindControl<TextBlock>("ReadyBadge")!.Text = _text["Status.GodotStopped"];
                Report(_text["Status.GodotStopped"]);
                break;
        }
    }

    private void HandleStageKey(JsonElement data)
    {
        var name = data.GetProperty("key").GetString() ?? "";
        if (name.Length == 4 && name.StartsWith("Key") && char.IsDigit(name[3])) name = "D" + name[3];
        if (!Enum.TryParse<Key>(name, out var key)) return;
        var modifiers = KeyModifiers.None;
        if (data.GetProperty("ctrl").GetBoolean()) modifiers |= KeyModifiers.Control;
        if (data.GetProperty("shift").GetBoolean()) modifiers |= KeyModifiers.Shift;
        if (data.GetProperty("alt").GetBoolean()) modifiers |= KeyModifiers.Alt;
        HandleShortcut(key, modifiers, null);
    }

    private async Task OpenPackage(string? path = null)
    {
        if (_ioBusy) return;
        if (CurrentDocument?.HasUnsavedChanges == true)
        {
            Report(_text["Message.SaveBeforeOpen"]);
            return;
        }
        var directory = path ?? await PickFolder("File.Open");
        if (directory is null) return;
        _ioBusy = true;
        try
        {
            await Task.Run(() => _session.Open(directory));
            RefreshDocument();
            Report(_text.Format("Message.Opened", CurrentDocument!.Title));
        }
        catch (Exception ex) { Report(_text.Format("Message.FileError", ex.Message)); }
        finally { _ioBusy = false; }
    }

    private async Task SavePackage(bool saveAs, string? destination = null)
    {
        if (_ioBusy || CurrentDocument is not { } document) return;
        var directory = destination ?? (saveAs || document.IsUnpersisted ? await PickFolder("File.SaveAs") : document.PackageDirectory);
        if (directory is null) return;
        _ioBusy = true;
        try
        {
            EditorSaveResult? result = null;
            await Task.Run(() => result = _session.Save(directory));
            RefreshDocument(false);
            Report(_text.Format("Message.Saved", result!.DestinationDirectory));
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
        SelectedNoteId = document?.Selection switch
        {
            { Kind: EditorSelectionKind.Note, Id: { } id } => id,
            { Kind: EditorSelectionKind.PathNode, Id: { } nodeId } => document.SelectedChart.Notes
                .FirstOrDefault(note => note.Nodes.Any(node => node.Id == nodeId))?.Id,
            _ => null,
        };
        if (document is null || SelectedNoteId is null) _selectedNoteIds = Array.Empty<string>();
        else if (document.Selection.Kind == EditorSelectionKind.Note && _selectedNoteIds.Contains(SelectedNoteId))
            _selectedNoteIds = _selectedNoteIds.Where(id => document.SelectedChart.Notes.Any(note => note.Id == id)).ToArray();
        else _selectedNoteIds = new[] { SelectedNoteId };
        _inspector.ShowDocument(document);
        _timing.SetDocument(document);
        if (_selectedNoteIds.Length > 1 && SelectedNoteId is not null)
            _inspector.Select(document, _selectedNoteIds, SelectedNoteId);
        var timeline = this.FindControl<TimelineView>("Timeline")!;
        timeline.NoteBars = document?.SelectedChart.Notes.SelectMany(note =>
            new[] { note.Time.ToDouble() }.Concat(note.Nodes.Select(node => node.Time.ToDouble()))).ToArray()
            ?? Array.Empty<double>();
        if (document is not null)
        {
            _snapDenominator = document.GridDivisor;
            var last = document.SelectedChart.Notes.SelectMany(note =>
                new[] { note.Time }.Concat(note.Nodes.Select(node => node.Time)))
                .DefaultIfEmpty(ExactBarTime.Zero).Max().ToDouble();
            timeline.MaximumBar = Math.Max(32, Math.Ceiling(last + 4));
        }
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
        // Avalonia is still committing this ComboBox selection. Its items must remain stable
        // until that operation completes; changing the active chart does not change the list.
        RefreshDocument(refreshChartChoices: false);
        Report(_text.Format("Message.ChartSelected", id));
    }

    internal long StageRevision => _stageRevision;
    internal bool LastStageUpdateIncremental { get; private set; }

    private void SendDocument()
    {
        if (!Ready || CurrentDocument is not { } document) return;
        var chart = Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(
            document.BuildChartSnapshot(document.SelectedChartId), requireMainJudgement: false));
        var audioPath = _session.ResolveDigestAudio();
        LastStageUpdateIncremental = ReferenceEquals(_loadedDocument, document) &&
            _loadedChartId == document.SelectedChartId && _loadedAudioPath == audioPath;
        var selection = document.Selection is { Kind: EditorSelectionKind.PathNode, Id: { } nodeId }
            ? new[] { nodeId } : _selectedNoteIds;
        var revision = ++_stageRevision;
        if (LastStageUpdateIncremental)
            _service.Update(chart, document.Title, document.Artist, audioPath, revision, selection);
        else _service.Load(chart, document.Title, document.Artist, audioPath, _bar, revision, selection);
        _loadedDocument = document;
        _loadedChartId = document.SelectedChartId;
        _loadedAudioPath = audioPath;
        SendAuthoring();
    }

    private void SendAuthoring()
    {
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
                _selectedNoteIds = selectedIds.Where(id => document.SelectedChart.Notes.Any(note => note.Id == id))
                    .Distinct().ToArray();
        }
        RefreshDocument();
    }

    private void TogglePlayback()
    {
        if (!Ready || CurrentDocument is null) return;
        _service.TogglePlayback(!_playing);
        Report(_text[_playing ? "Message.PauseRequested" : "Message.PlayRequested"]);
    }

    private void TimelineSeek(object? sender, double bar) => SeekBar(bar);

    private void SeekSeconds(double delta)
    {
        if (CurrentDocument is not { } document) return;
        var chart = document.BuildChartSnapshot(document.SelectedChartId);
        var timeline = new V2BpmTimeline(chart.Bpms, chart.AudioOffsetSec);
        SeekBar(timeline.RuntimeBarAt(timeline.RuntimeSecondsAt(_bar) + delta), snap: false);
    }

    private void SeekBar(double bar, bool snap = true)
    {
        if (CurrentDocument is null) return;
        var maximum = this.FindControl<TimelineView>("Timeline")!.MaximumBar;
        var snapped = Math.Clamp(snap ? Math.Round(bar * _snapDenominator) / _snapDenominator : bar, 0, maximum);
        _service.Seek(snapped);
        _bar = snapped;
        var chart = CurrentDocument.BuildChartSnapshot(CurrentDocument.SelectedChartId);
        _second = new V2BpmTimeline(chart.Bpms, chart.AudioOffsetSec).RuntimeSecondsAt(_bar);
        _timing.SetCurrentBar(_bar);
        this.FindControl<TimelineView>("Timeline")!.CurrentBar = snapped;
        UpdateTimeLabel();
        Report(_text.Format("Message.Seek", snapped));
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
        if (this.FindControl<TextBlock>("StatusText") is { } status)
            status.Text = _text.Format("Message.MotionProfile", _text[index switch { 1 => "Motion.Lite", 2 => "Motion.Minimal", _ => "Motion.Normal" }]);
    }

    private void LanguageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_factory is null) return;
        _text.SetLanguage((sender as ComboBox)?.SelectedIndex == 1 ? "zh-CN" : "en");
        Report(_text["Message.LanguageSwitched"]);
    }

    private void SendContextText()
    {
        var keys = new[] { "Context.Title", "Context.Track", "Context.Mode", "Context.Volume", "Context.Effects",
            "Context.Off", "Context.Reduced", "Context.Full", "Context.Dismiss", "Inspector.NoteType",
            "Inspector.LeftStage", "Inspector.CenterStage", "Inspector.RightStage", "Tools.Select", "Tools.Path" }
            .Concat(Enum.GetNames<V2NoteType>().Select(type => "Inspector." + type));
        _service.Send(new { command = "contextText", labels = keys.ToDictionary(key => key, key => _text[key]) });
    }

    private void UpdateLocalizedState()
    {
        SendContextText();
        this.FindControl<TextBlock>("ReadyBadge")!.Text = _text[Ready ? "Status.GodotLive" : "Status.StartingGodot"];
        UpdateDocumentChrome();
        UpdateTimeLabel();
        UpdateSnapText();
        UpdatePlaybackText();
        UpdatePlaybackParameters();
        UpdateNativeStatus();
    }

    private void UpdateTimeLabel() => this.FindControl<TextBlock>("TimeLabel")!.Text = _text.Format("Transport.Position",
        TimeSpan.FromSeconds(Math.Max(0, _second)).ToString(@"mm\:ss\.fff"), _bar);
    private void UpdateSnapText() => this.FindControl<TextBlock>("SnapText")!.Text = _text.Format("Transport.Snap", _snapDenominator);
    private void UpdatePlaybackParameters()
    {
        this.FindControl<TextBlock>("PlayerSpeedText")!.Text = _text.Format("Transport.PlayerSpeed", _playerSpeed);
        this.FindControl<TextBlock>("PlaybackRateText")!.Text = _text.Format("Transport.PlaybackRate", _playbackRate);
    }

    private void UpdatePlaybackText()
    {
        _stageView.SetPlaying(_playing);
        this.FindControl<IconButton>("TransportButton")!.Content = _text[_playing ? "Transport.Pause" : "Transport.Play"];
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
    private async void AddChartClicked(object? sender, RoutedEventArgs e) => await AddChart();
    private async void SettingsClicked(object? sender, RoutedEventArgs e) => await EditPackageSettings();
    private void CopyChartClicked(object? sender, RoutedEventArgs e) => CopyChart();
    private void MoveChartUpClicked(object? sender, RoutedEventArgs e) => MoveChart(-1);
    private void MoveChartDownClicked(object? sender, RoutedEventArgs e) => MoveChart(1);
    private void DeleteChartClicked(object? sender, RoutedEventArgs e) => DeleteChart();
    private async void SaveClicked(object? sender, RoutedEventArgs e) => await SavePackage(false);
    private async void SaveAsClicked(object? sender, RoutedEventArgs e) => await SavePackage(true);
    private void UndoClicked(object? sender, RoutedEventArgs e) => Undo();
    private void RedoClicked(object? sender, RoutedEventArgs e) => Redo();
    private void DeleteClicked(object? sender, RoutedEventArgs e) =>
        _editing.DeleteSelection(CurrentDocument, SelectedNoteId, _selectedNoteIds);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Source is not TextBox && e.Source is not Avalonia.Controls.NumericUpDown && e.Source is not ComboBox && HandleShortcut(e.Key, e.KeyModifiers, e.Source))
            e.Handled = true;
        base.OnKeyDown(e);
    }

    private bool HandleShortcut(Key key, KeyModifiers modifiers, object? source)
    {
        if (ShortcutsVisible)
        {
            if (key is not (Key.Escape or Key.F1)) return true;
            ToggleShortcuts();
            return true;
        }
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
            }
            return false;
        }
        if (modifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return false;
        switch (key)
        {
            case Key.Space when source is not Button: TogglePlayback(); return true;
            case Key.B when modifiers == KeyModifiers.None:
                WheelReversed = !WheelReversed;
                _service.Send(new { command = "wheelDirection", reversed = WheelReversed });
                Report(_text[WheelReversed ? "Message.WheelReversed" : "Message.WheelNormal"]);
                return true;
            case Key.R when modifiers == KeyModifiers.None: SeekBar(0); return true;
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
                _editing.DeleteSelection(CurrentDocument, SelectedNoteId, _selectedNoteIds);
                return true;
            case Key.F1: ToggleShortcuts(); return true;
            default: return false;
        }
    }

    private static string TitleCase(string value) => char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
