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
    private readonly WorkbenchFactory _factory;
    private readonly IWorkbenchLocalizer _text = WorkbenchLocalizer.Shared;
    private bool _playing;
    private bool _ioBusy;
    private double _bar;
    private double _second;
    private double _playerSpeed = 1.0;
    private double _playbackRate = 1.0;
    private string _tool = "SELECT";
    private int _snapDenominator = EditorDocument.DefaultGridDivisor;
    private bool _refreshingCharts;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _factory = new WorkbenchFactory(_stageView, _inspector, _tools);
        var dock = this.FindControl<DockControl>("Dock")!;
        dock.Factory = _factory;
        dock.Layout = _factory.CreateLayout();
        dock.InitializeFactory = true;
        dock.InitializeLayout = true;
        _stageView.FloatRequested += FloatStage;
        _stageView.DockRequested += DockStage;
        _stageView.PlayRequested += TogglePlayback;
        _inspector.SpeedChanged += ApplySpeed;
        _inspector.CurveChanged += ApplyCurve;
        _tools.ToolSelected += tool =>
        {
            _tool = tool;
            SendAuthoring();
            Report(_text.Format("Message.ToolSelected", _text[$"Tools.{TitleCase(tool)}"]));
        };
        _tools.AuthoringChanged += SendAuthoring;
        _tools.AddPanelRequested += AddPanel;
        _tools.Notice += _ => Report(_text["Message.WorkspaceNotice"]);
        _service.MessageReceived += message => Dispatcher.UIThread.Post(() => StageEvent(message));
        _service.RuntimeError += error => Dispatcher.UIThread.Post(() => Report("Godot: " + error));
        _text.LanguageChanged += UpdateLocalizedState;
        UpdateLocalizedState();
        Opened += (_, _) => StartStage();
        Closing += (_, _) =>
        {
            _text.LanguageChanged -= UpdateLocalizedState;
            _stageView.Host.SetGodotHandle(0);
            _service.Dispose();
        };
    }

    internal StageView Stage => _stageView;
    internal WorkbenchFactory LayoutFactory => _factory;
    internal bool Ready { get; private set; }
    internal int StageProcessId => _service.ProcessId;
    internal int TickCount { get; private set; }
    internal string LastStageInput { get; private set; } = "";
    internal double CurrentBar => _bar;
    internal double CurrentSecond => _second;
    internal double PlayerSpeed => _playerSpeed;
    internal double PlaybackRate => _playbackRate;
    internal string CurrentTool => _tool;
    internal int SnapDenominator => _snapDenominator;
    internal string? SelectedNoteId { get; private set; }
    internal EditorDocument? CurrentDocument => _session.Document;
    internal string StatusMessage => this.FindControl<TextBlock>("StatusText")!.Text ?? "";
    internal bool ShortcutsVisible => this.FindControl<Border>("ShortcutOverlay")!.IsVisible;
    internal void CaptureStage(string path) => _service.Capture(path);
    internal bool ShortcutForVerification(Key key, KeyModifiers modifiers = KeyModifiers.None) => HandleShortcut(key, modifiers, null);
    internal async Task OpenPackageForVerification(string path) => await OpenPackage(path);
    internal async Task SavePackageForVerification(bool saveAs = false) => await SavePackage(saveAs);
    internal async Task SavePackageToForVerification(string path) => await SavePackage(true, path);
    internal void ApplySpeedForVerification(double speed) => _inspector.SetSpeedForVerification(speed);
    internal void SelectNoteForVerification(string id)
    {
        CurrentDocument?.Select(new EditorSelection(EditorSelectionKind.Note, id));
        SelectedNoteId = id;
        _inspector.ShowDocument(CurrentDocument);
    }
    internal async Task CreatePackageForVerification(EditorCreateForm form) => await CreatePackage(form);
    internal async Task AddChartForVerification(EditorCreateForm form) => await AddChart(form);
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
                _stageView.Host.SetGodotHandle((nint)data.GetProperty("hwnd").GetInt64());
                this.FindControl<TextBlock>("ReadyBadge")!.Text = _text["Status.GodotLive"];
                SendDocument();
                Report(_text[CurrentDocument is null ? "Message.OpenPackage" : "Message.GodotReady"]);
                UpdateNativeStatus();
                break;
            case "selection":
                _stageView.Host.FocusNative();
                var ids = data.GetProperty("ids").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                SelectedNoteId = data.GetProperty("parentId").GetString();
                var nodeId = data.TryGetProperty("nodeId", out var selectedNode) ? selectedNode.GetString() : null;
                CurrentDocument?.Select(nodeId is null
                    ? new EditorSelection(EditorSelectionKind.Note, SelectedNoteId)
                    : new EditorSelection(EditorSelectionKind.PathNode, nodeId));
                _inspector.Select(CurrentDocument, ids, SelectedNoteId ?? "");
                Report(_text.Format("Message.SelectionSynced", ids.Length));
                break;
            case "place": PlaceNote(data); break;
            case "pathAdd": AddPathNode(data); break;
            case "pathMove": MovePathPoint(data); break;
            case "tick":
                TickCount++;
                _second = data.GetProperty("second").GetDouble();
                _bar = data.GetProperty("bar").GetDouble();
                _playing = data.GetProperty("playing").GetBoolean();
                _playerSpeed = data.GetProperty("playerSpeed").GetDouble();
                _playbackRate = data.GetProperty("playbackRate").GetDouble();
                this.FindControl<TimelineView>("Timeline")!.CurrentBar = _bar;
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
            case "error": Report("Stage error · " + data.GetProperty("message").GetString()); break;
            case "exited":
                Ready = false;
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

    private void PlaceNote(JsonElement data)
    {
        if (CurrentDocument is not { } document) return;
        try
        {
            var type = (V2NoteType)data.GetProperty("type").GetInt32();
            var track = (EditorTrack)data.GetProperty("track").GetInt32();
            if (!Enum.IsDefined(type) || !Enum.IsDefined(track)) throw new InvalidOperationException("Invalid Note type or track.");
            var time = document.Snap(ExactTime(data.GetProperty("bar").GetDouble()));
            var tail = document.Snap(ExactTime(data.GetProperty("tailBar").GetDouble()));
            var id = new CanvasAuthoringController(document).AddNote(type, track, time,
                data.GetProperty("center").GetDouble(), data.GetProperty("width").GetDouble(),
                type is V2NoteType.Hold or V2NoteType.Mixer ? tail : null);
            RefreshDocument();
            Report(_text.Format("Message.NotePlaced", id, ExactBarTimeText.Format(time)));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void AddPathNode(JsonElement data)
    {
        if (CurrentDocument is not { } document) return;
        try
        {
            var noteId = data.GetProperty("noteId").GetString()!;
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            var time = document.Snap(ExactTime(data.GetProperty("bar").GetDouble()));
            var shape = EditorPathProjection.ShapeOf(note);
            var (center, width) = EditorPathProjection.Sample(shape, time);
            var previous = note.Nodes.LastOrDefault(node => node.Time < time);
            var curve = previous?.CurveToNext ?? (previous is null && note.Time < time ? note.CurveToNext : null);
            var id = new CanvasAuthoringController(document).AddPathNode(noteId, time, center, width,
                curve, note.Type == V2NoteType.Hold ? true : null);
            RefreshDocument();
            Report(_text.Format("Message.PathNodeAdded", id));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void MovePathPoint(JsonElement data)
    {
        if (CurrentDocument is not { } document) return;
        try
        {
            var noteId = data.GetProperty("noteId").GetString()!;
            var nodeId = data.TryGetProperty("nodeId", out var node) ? node.GetString() : null;
            var note = document.SelectedChart.Notes.Single(item => item.Id == noteId);
            var pathNode = nodeId is null ? null : note.Nodes.Single(item => item.Id == nodeId);
            var delta = ExactTime(data.GetProperty("endBar").GetDouble()) -
                ExactTime(data.GetProperty("startBar").GetDouble());
            var originalTime = pathNode?.Time ?? note.Time;
            var originalCenter = pathNode?.Center ?? note.Center;
            var width = pathNode?.Width ?? note.Width;
            var time = document.Snap(originalTime + delta);
            var center = Math.Clamp(originalCenter + data.GetProperty("endCenter").GetDouble() -
                data.GetProperty("startCenter").GetDouble(), width / 2d, 5 - width / 2d);
            var controller = new CanvasAuthoringController(document);
            if (nodeId is null) controller.MoveNote(noteId, time, center, width);
            else controller.EditPathNode(noteId, nodeId, time, center, width);
            RefreshDocument();
            Report(_text.Format("Message.PathPointMoved", nodeId ?? noteId, ExactBarTimeText.Format(time)));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private static ExactBarTime ExactTime(double bar) =>
        ExactBarTime.FromFraction((long)Math.Round(Math.Max(0, bar) * 1_000_000d), 1_000_000);

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

    private void RefreshDocument(bool reloadStage = true)
    {
        var document = CurrentDocument;
        _inspector.ShowDocument(document);
        SelectedNoteId = document?.Selection switch
        {
            { Kind: EditorSelectionKind.Note, Id: { } id } => id,
            { Kind: EditorSelectionKind.PathNode, Id: { } nodeId } => document.SelectedChart.Notes
                .FirstOrDefault(note => note.Nodes.Any(node => node.Id == nodeId))?.Id,
            _ => null,
        };
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
        RefreshChartChoices();
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
        RefreshDocument();
        Report(_text.Format("Message.ChartSelected", id));
    }

    private void SendDocument()
    {
        if (!Ready || CurrentDocument is not { } document) return;
        var chart = Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(
            document.BuildChartSnapshot(document.SelectedChartId), requireMainJudgement: false));
        _service.Load(chart, document.Title, document.Artist, _session.ResolveDigestAudio(), _bar);
        SendAuthoring();
    }

    private void SendAuthoring()
    {
        _service.SetAuthoring(_tool, _tools.PlacementType.ToString(), _tools.PlacementTrack.ToString(),
            _tools.PlacementWidth, _snapDenominator);
    }

    private void UpdateDocumentChrome()
    {
        var document = CurrentDocument;
        this.FindControl<TextBlock>("SongTitle")!.Text = document?.Title ?? _text["Document.None"];
        this.FindControl<TextBlock>("SongMeta")!.Text = document is null ? _text["Header.Prototype"] :
            $"{document.SelectedChart.Difficulty} {document.SelectedChart.Level?.ToString() ?? "—"} / {document.SelectedChart.Id} · {document.SelectedChart.Notes.Count} NOTES";
        Title = document is null ? "DynaMaker UV" : $"DynaMaker UV — {document.Title}{(document.HasUnsavedChanges ? " *" : "")}";
    }

    private void ApplySpeed(string id, double speed)
    {
        if (CurrentDocument is not { } document) return;
        var note = document.SelectedChart.Notes.SingleOrDefault(item => item.Id == id);
        if (note is null || note.ScrollSpeedMultiplier == speed) return;
        try
        {
            document.Execute(new SetNoteScrollSpeedMultiplierCommand(id, speed));
            RefreshDocument();
            Report(_text.Format("Message.NoteSpeedSent", id, speed));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void ApplyCurve(string noteId, string? nodeId, V2PathCurve curve)
    {
        if (CurrentDocument is not { } document) return;
        try
        {
            document.Execute(new SetPathCurveCommand(noteId, nodeId, curve));
            RefreshDocument();
            Report(_text.Format("Message.CurveApplied", nodeId ?? noteId, curve));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void DeleteSelection()
    {
        if (CurrentDocument is not { } document || SelectedNoteId is null) return;
        try
        {
            var id = SelectedNoteId;
            if (document.Selection is { Kind: EditorSelectionKind.PathNode, Id: { } nodeId })
                document.Execute(new DeletePathNodeCommand(id, nodeId));
            else document.Execute(new DeleteNoteCommand(id));
            RefreshDocument();
            Report(_text.Format("Message.Deleted", document.Selection.Id ?? id));
        }
        catch (Exception ex) { Report(ex.Message); }
    }

    private void Undo()
    {
        if (CurrentDocument is not { CanUndo: true } document) return;
        document.Undo();
        RefreshDocument();
    }

    private void Redo()
    {
        if (CurrentDocument is not { CanRedo: true } document) return;
        document.Redo();
        RefreshDocument();
    }

    private void TogglePlayback()
    {
        if (!Ready || CurrentDocument is null) return;
        _service.TogglePlayback(!_playing);
        Report(_text[_playing ? "Message.PauseRequested" : "Message.PlayRequested"]);
    }

    private void TimelineSeek(object? sender, double bar) => SeekBar(bar);

    private void SeekBar(double bar)
    {
        if (CurrentDocument is null) return;
        var maximum = this.FindControl<TimelineView>("Timeline")!.MaximumBar;
        var snapped = Math.Clamp(Math.Round(bar * _snapDenominator) / _snapDenominator, 0, maximum);
        _service.Seek(snapped);
        _bar = snapped;
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
        _factory.DockStage();
        if (_stageView.Parent is ContentControl previousHost) previousHost.Content = null;
        else if (_stageView.Parent is Panel previousPanel) previousPanel.Children.Remove(_stageView);
        var dock = this.FindControl<DockControl>("Dock")!;
        dock.Layout = null;
        dock.Layout = _factory.CreateLayout();
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

    private void UpdateLocalizedState()
    {
        this.FindControl<TextBlock>("ReadyBadge")!.Text = _text[Ready ? "Status.GodotLive" : "Status.StartingGodot"];
        var motion = this.FindControl<ComboBox>("MotionChoice")!;
        var motionIndex = motion.SelectedIndex;
        motion.SelectedIndex = -1;
        motion.SelectedIndex = motionIndex;
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
    private void RestoreClicked(object? sender, RoutedEventArgs e) => DockStage();
    private void AddPanelClicked(object? sender, RoutedEventArgs e) => AddPanel();
    private void ShortcutsClicked(object? sender, RoutedEventArgs e) => ToggleShortcuts();
    private void ThemeClicked(object? sender, RoutedEventArgs e) => Report(_text["Message.ThemeActive"]);
    private async void NewClicked(object? sender, RoutedEventArgs e) => await CreatePackage();
    private async void OpenClicked(object? sender, RoutedEventArgs e) => await OpenPackage();
    private async void AddChartClicked(object? sender, RoutedEventArgs e) => await AddChart();
    private async void SaveClicked(object? sender, RoutedEventArgs e) => await SavePackage(false);
    private async void SaveAsClicked(object? sender, RoutedEventArgs e) => await SavePackage(true);
    private void UndoClicked(object? sender, RoutedEventArgs e) => Undo();
    private void RedoClicked(object? sender, RoutedEventArgs e) => Redo();
    private void DeleteClicked(object? sender, RoutedEventArgs e) => DeleteSelection();

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
            case Key.R when modifiers == KeyModifiers.None: SeekBar(0); return true;
            case Key.A: SeekBar(_bar - (modifiers == KeyModifiers.Shift ? 1d / _snapDenominator : 1)); return true;
            case Key.D: SeekBar(_bar + (modifiers == KeyModifiers.Shift ? 1d / _snapDenominator : 1)); return true;
            case Key.C: ChangeSnap(-1, modifiers == KeyModifiers.Shift); return true;
            case Key.V: ChangeSnap(1, modifiers == KeyModifiers.Shift); return true;
            case Key.Q when modifiers == KeyModifiers.None: ChangePlayerSpeed(-1); return true;
            case Key.E when modifiers == KeyModifiers.None: ChangePlayerSpeed(1); return true;
            case Key.W when modifiers == KeyModifiers.None: ChangePlaybackRate(1); return true;
            case Key.S when modifiers == KeyModifiers.None: ChangePlaybackRate(-1); return true;
            case Key.D1 when modifiers == KeyModifiers.None: _tools.SelectTool("SELECT"); return true;
            case Key.D2 when modifiers == KeyModifiers.None: _tools.SelectTool("PLACE"); return true;
            case Key.D3 when modifiers == KeyModifiers.None: _tools.SelectTool("PATH"); return true;
            case Key.D4 when modifiers == KeyModifiers.None: _tools.SelectTool("PAN"); return true;
            case Key.Delete:
            case Key.Back: DeleteSelection(); return true;
            case Key.F1: ToggleShortcuts(); return true;
            default: return false;
        }
    }

    private static string TitleCase(string value) => char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
}
