using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DuxCommunity.ChartEditor.Controls;
using DuxCommunity.ChartEditor.Core;
using DuxCommunity.ChartEditor.Editor;
using DuxCommunity.ChartEditor.Views;
using DuxShared.Chart.V2;
using UiEditorTrack = DuxCommunity.ChartEditor.Editor.EditorTrack;
using CoreEditorTrack = DuxCommunity.ChartEditor.Core.EditorTrack;

namespace DuxCommunity.ChartEditor;

public sealed partial class MainWindow : Window
{
    private enum WorkspacePage
    {
        Main,
        Project,
        Events,
        Validation,
        Publish,
    }

    private readonly SharedV2ProjectLoader _loader = new();
    private readonly DispatcherTimer _playbackTimer;
    private readonly Stopwatch _playbackClock = new();
    private EditorDocument? _document;
    private EditorProject? _project;
    private EditorChartDocument? _activeChart;
    private EditorProjectDraft? _draft;
    private double _playbackOrigin;
    private bool _timelineInternalUpdate;
    private bool _refreshing;
    private UiEditorTrack _track = UiEditorTrack.Center;
    private EditorTool _tool = EditorTool.Select;
    private WorkspacePage _workspacePage;
    private ExactBarTime? _pendingBpmTime;
    private bool _shiftDown;

    public MainWindow()
    {
        InitializeComponent();
        StageCanvas.SelectionChanged += StageSelectionChanged;
        StageCanvas.PlacementRequested += StagePlacementRequested;
        StageCanvas.TimeChanged += StageTimeChanged;
        StageCanvas.StatusChanged += (_, message) => SetStatus(message);
        KeyDown += WindowKeyDown;
        KeyUp += WindowKeyUp;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Closing += WindowClosing;
        WelcomePage.NewProjectRequested += (_, _) => ShowNewProjectPage();
        WelcomePage.OpenProjectRequested += (_, _) => OpenAndLoadClicked(this, new RoutedEventArgs());
        ProjectSetupPage.BackRequested += (_, _) => ShowWelcomePage();
        ProjectSetupPage.CreateRequested += ProjectDraftRequested;

        _playbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render, PlaybackTick);

        PackPathBox.Text = FindDefaultGoldenPack() ?? string.Empty;
        SetTrack(UiEditorTrack.Center, announce: false);
        SetTool(EditorTool.Select, announce: false);
        SetWorkspace(WorkspacePage.Main);
        SetVisualSpeed(1.0, announce: false);
        ClearCoreSelection();
        UpdateCommandButtons();
        ShowWelcomePage();
    }

    private void ShowWelcomePage()
    {
        if (_document?.HasUnsavedChanges == true && EditorShell.IsVisible)
        {
            SetStatus("Save or discard the current unsaved document before returning home.");
            return;
        }
        StopPlayback();
        WelcomePage.IsVisible = true;
        ProjectSetupPage.IsVisible = false;
        EditorShell.IsVisible = false;
    }

    private void ShowNewProjectPage()
    {
        if (_document?.HasUnsavedChanges == true && EditorShell.IsVisible)
        {
            SetStatus("Save or discard the current unsaved document before starting another project.");
            return;
        }
        StopPlayback();
        ProjectSetupPage.Reset();
        WelcomePage.IsVisible = false;
        ProjectSetupPage.IsVisible = true;
        EditorShell.IsVisible = false;
    }

    private void ShowEditorPage()
    {
        WelcomePage.IsVisible = false;
        ProjectSetupPage.IsVisible = false;
        EditorShell.IsVisible = true;
        EditorShell.ColumnDefinitions[1].Width = new GridLength(Bounds.Width < 1320 ? 320 : 360);
    }

    private void ProjectDraftRequested(object? sender, EditorProjectDraft draft)
    {
        try
        {
            _draft = draft;
            AttachDocument(_draft.Document);
            SetTool(EditorTool.Select, announce: false);
            SetWorkspace(WorkspacePage.Main);
            ShowEditorPage();
            SetStatus("Memory draft created. Add a scoring Note before creating the strict v2 package.");
        }
        catch (Exception exception)
        {
            SetStatus($"Draft creation failed: {exception.Message}");
        }
    }

    private async void OpenAndLoadClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a clean-room Dynamite UV v2 pack",
            AllowMultiple = false,
        });
        if (folders.Count != 1)
            return;
        PackPathBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
        LoadPack();
    }

    private async void BrowseSaveTargetClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select an empty directory for Save As",
            AllowMultiple = false,
        });
        if (folders.Count == 1)
            SaveTargetBox.Text = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
    }

    private void LoadPackClicked(object? sender, RoutedEventArgs e) => LoadPack();

    private void LoadPack()
    {
        StopPlayback();
        try
        {
            var opened = _loader.Open(PackPathBox.Text ?? string.Empty);
            _draft = null;
            AttachDocument(opened);
            SetWorkspace(WorkspacePage.Main);
            ShowEditorPage();
            SetStatus($"Loaded {_document!.PackId} revision {_document.Revision}.");
        }
        catch (Exception exception)
        {
            ProjectStateText.Text = "● LOAD FAILED";
            ProjectStateText.Foreground = this.FindResource("CommunityPink") as IBrush;
            ValidationHeadlineText.Text = "FAIL · package was rejected";
            ValidationHeadlineText.Foreground = this.FindResource("CommunityPink") as IBrush;
            ValidationDetailText.Text = exception.Message;
            ValidationList.ItemsSource = new[] { exception.Message };
            if (_document is not null)
                SetWorkspace(WorkspacePage.Validation);
            SetStatus($"Load failed: {exception.Message}");
        }
    }

    private void AttachDocument(EditorDocument document)
    {
        _document = document;
        _project = SharedV2ProjectLoader.Snapshot(document);
        PopulateProject(_project);
        _refreshing = true;
        ChartCombo.ItemsSource = _project.Charts;
        ChartCombo.SelectedIndex = 0;
        _refreshing = false;
        document.SelectChart(_project.Charts[0].Id);
        SaveTargetBox.Text = document.PackageDirectory ?? string.Empty;
        RefreshActiveChart(document.SelectedChartId);
        UpdateEditorIdentity();
    }

    private void UpdateEditorIdentity()
    {
        if (_document is null || _project is null || _activeChart is null)
            return;
        EditorTitleText.Text = _project.Title;
        EditorChartIdentityText.Text = _activeChart.Level is { } level
            ? $"{_activeChart.DifficultyDisplay.ToUpperInvariant()} · Lv {level} · {_activeChart.Id}"
            : $"{_activeChart.DifficultyDisplay.ToUpperInvariant()} · UNRATED · {_activeChart.Id}";
        DraftStateText.Text = _document.IsUnpersisted ? "● MEMORY DRAFT" : string.Empty;
        SaveButton.Content = _document.IsUnpersisted ? "CREATE PACKAGE" : "SAVE";
    }

    private void PopulateProject(EditorProject project)
    {
        PackTitleText.Text = project.Title;
        PackArtistText.Text = project.Artist;
        PackIdentityText.Text = $"ID {project.PackId} · REVISION {project.Revision}";
        ProjectChartsText.Text = project.Charts.Count.ToString(CultureInfo.InvariantCulture);
        ProjectRootText.Text = project.RootPath;
        ProjectSourceText.Text = project.MetaFile;
        ProjectTitleBox.Text = project.Title;
        ProjectArtistBox.Text = project.Artist;
        ProjectAudioBox.Text = _document?.Audio ?? string.Empty;
    }

    private void ChartSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || ChartCombo.SelectedItem is not EditorChartDocument chart || _document is null)
            return;
        try
        {
            StopPlayback();
            _document.SelectChart(chart.Id);
            RefreshActiveChart(chart.Id);
            SetStatus($"Showing {chart.Id}: {chart.Notes.Count} notes and {chart.Bpms.Count} BPM events.");
        }
        catch (Exception exception)
        {
            SetStatus($"Chart selection failed: {exception.Message}");
        }
    }

    private void RefreshActiveChart(string? chartId = null)
    {
        if (_document is null)
            return;
        _project = SharedV2ProjectLoader.Snapshot(_document);
        PopulateProject(_project);
        var id = chartId ?? _document.SelectedChartId;
        var chart = _project.Charts.Single(item => item.Id == id);
        _activeChart = chart;

        _refreshing = true;
        ChartCombo.ItemsSource = _project.Charts;
        ChartCombo.SelectedIndex = _project.Charts.ToList().FindIndex(item => item.Id == id);
        StageCanvas.GridDivisor = _document.GridDivisor;
        StageCanvas.SnapEnabled = _document.SnapEnabled;
        StageCanvas.SongTitle = _project.Title;
        StageCanvas.DifficultyText = chart.Level is { } level
            ? $"{chart.DifficultyDisplay} · Lv {level}"
            : chart.DifficultyDisplay;
        StageCanvas.AudioOffsetSec = chart.AudioOffsetSec;
        StageCanvas.Document = chart;
        _refreshing = false;

        TimelineSlider.Maximum = chart.DurationSec;
        _timelineInternalUpdate = true;
        TimelineSlider.Value = Math.Min(TimelineSlider.Value, chart.DurationSec);
        _timelineInternalUpdate = false;
        DurationText.Text = FormatTime(chart.DurationSec);
        OffsetValueText.Text = FormatOffset(chart.AudioOffsetSec);
        BpmList.ItemsSource = chart.Bpms.Select(bpm =>
            $"BAR {bpm.ExactTime}   {bpm.Bpm:0.###} BPM\n{bpm.Second:0.000}s").ToArray();
        UpdateTransport(TimelineSlider.Value);
        RefreshCoreSelection();
        UpdateValidation();
        UpdateEditorIdentity();
        UpdateCommandButtons();
    }

    private void RefreshCoreSelection()
    {
        if (_document is null || _activeChart is null || _document.Selection.Kind == EditorSelectionKind.None)
        {
            StageCanvas.SetSelection(null);
            ClearInspector();
            return;
        }

        var selection = _document.Selection;
        if (selection.Kind == EditorSelectionKind.Note)
        {
            var note = _activeChart.Notes.SingleOrDefault(item => item.Id == selection.Id);
            if (note is null)
            {
                ClearCoreSelection();
                return;
            }
            StageCanvas.SetSelection(ToSelectionInfo(note));
            return;
        }

        var bpm = _activeChart.Bpms.SingleOrDefault(item => item.Id == selection.Id);
        if (bpm is null)
        {
            ClearCoreSelection();
            return;
        }
        StageCanvas.SetSelection(new EditorSelectionInfo(
            "EVENT", bpm.Id, "BPM", $"{bpm.Second:0.000}s · bar {bpm.ExactTime}",
            "—", "—", $"BPM {bpm.Bpm:0.###}"));
    }

    private static EditorSelectionInfo ToSelectionInfo(EditorNoteModel note) => new(
        "NOTE", note.Id, note.Track.ToString().ToUpperInvariant(),
        $"{note.Second:0.000}s · bar {note.ExactTime}",
        note.Center.ToString("0.###", CultureInfo.InvariantCulture),
        note.Width.ToString("0.###", CultureInfo.InvariantCulture),
        note.IsPath
            ? $"{note.Type} · terminal {note.EndBar:0.####} · {note.Path.Count} evaluated samples"
            : note.Type);

    private void ClearCoreSelection()
    {
        _document?.Select(EditorSelection.None);
        StageCanvas.SetSelection(null);
        ClearInspector();
    }

    private void ClearInspector()
    {
        SelectionEmptyCard.IsVisible = true;
        SelectionCard.IsVisible = false;
        NoteFieldsPanel.IsVisible = false;
        TailFieldsPanel.IsVisible = false;
        BpmFieldsPanel.IsVisible = false;
    }

    private void TrackButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && Enum.TryParse<UiEditorTrack>(text, out var track))
            SetTrack(track);
    }

    private void ToolButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && Enum.TryParse<EditorTool>(text, out var tool))
            SetTool(tool);
    }

    private void SetTrack(UiEditorTrack track, bool announce = true)
    {
        _track = track;
        StageCanvas.ActiveTrack = track;
        SetSelected(TrackLeftButton, track == UiEditorTrack.Left);
        SetSelected(TrackCenterButton, track == UiEditorTrack.Center);
        SetSelected(TrackRightButton, track == UiEditorTrack.Right);
        if (announce)
            SetStatus($"{track.ToString().ToUpperInvariant()} track selected.");
    }

    private void SetTool(EditorTool tool, bool announce = true)
    {
        _tool = tool;
        StageCanvas.ActiveTool = tool;
        SetSelected(SelectToolButton, tool == EditorTool.Select);
        SetSelected(TapToolButton, tool == EditorTool.Tap);
        SetSelected(DragToolButton, tool == EditorTool.Drag);
        SetSelected(HoldToolButton, tool == EditorTool.Hold);
        SetSelected(BpmToolButton, tool == EditorTool.Bpm);
        var trackEnabled = tool != EditorTool.Bpm;
        TrackLeftButton.IsEnabled = trackEnabled;
        TrackCenterButton.IsEnabled = trackEnabled;
        TrackRightButton.IsEnabled = trackEnabled;
        if (announce)
        {
            SetStatus(tool switch
            {
                EditorTool.Select => "SELECT active. Click an object to inspect it.",
                EditorTool.Hold => "HOLD active. Place the head, move in time, then place the tail.",
                EditorTool.Bpm => "BPM active. Click a target Bar and enter its value.",
                _ => $"{tool.ToString().ToUpperInvariant()} active. Drag to set center and width.",
            });
        }
    }

    private static void SetSelected(Button button, bool selected)
    {
        if (selected)
            button.Classes.Add("selected");
        else
            button.Classes.Remove("selected");
    }

    private void PlayClicked(object? sender, RoutedEventArgs e)
    {
        if (_activeChart is null)
        {
            SetStatus("Load a project before starting visual transport.");
            return;
        }
        if (_playbackTimer.IsEnabled)
            StopPlayback();
        else
            StartPlayback();
    }

    private void StartPlayback()
    {
        if (_activeChart is null)
            return;
        if (TimelineSlider.Value >= _activeChart.DurationSec)
            TimelineSlider.Value = 0;
        _playbackOrigin = TimelineSlider.Value;
        _playbackClock.Restart();
        _playbackTimer.Start();
        PlayButton.Content = "❚❚";
    }

    private void StopPlayback()
    {
        var wasEnabled = _playbackTimer.IsEnabled;
        _playbackTimer.Stop();
        if (wasEnabled && _activeChart is not null)
        {
            var next = Math.Min(_activeChart.DurationSec,
                _playbackOrigin + _playbackClock.Elapsed.TotalSeconds);
            SetTimelineWithoutEvent(next);
            UpdateTransport(next);
        }
        _playbackClock.Stop();
        PlayButton.Content = "▶";
    }

    private void PlaybackTick(object? sender, EventArgs e)
    {
        if (_activeChart is null)
        {
            StopPlayback();
            return;
        }
        var next = Math.Min(_activeChart.DurationSec,
            _playbackOrigin + _playbackClock.Elapsed.TotalSeconds);
        SetTimelineWithoutEvent(next);
        UpdateTransport(next);
        if (next >= _activeChart.DurationSec)
        {
            StopPlayback();
            SetStatus("Reached the end of the visual timeline.");
        }
    }

    private void TimelineChanged(object? sender,
        Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_timelineInternalUpdate)
            return;
        if (_playbackTimer.IsEnabled)
        {
            _playbackOrigin = e.NewValue;
            _playbackClock.Restart();
        }
        UpdateTransport(e.NewValue);
    }

    private void StageTimeChanged(object? sender, EditorTimeChangedEventArgs e)
    {
        if (_activeChart is null)
            return;
        SetTimelineWithoutEvent(e.Second);
        if (_playbackTimer.IsEnabled)
        {
            _playbackOrigin = e.Second;
            _playbackClock.Restart();
        }
        UpdateTransport(e.Second);
    }

    private void SetTimelineWithoutEvent(double second)
    {
        _timelineInternalUpdate = true;
        TimelineSlider.Value = second;
        _timelineInternalUpdate = false;
    }

    private void UpdateTransport(double second)
    {
        StageCanvas.CurrentSecond = second;
        TimecodeText.Text = FormatTime(second);
        StageStatusText.Text = _activeChart is null
            ? "Ready"
            : $"BAR {_activeChart.SecondsToBar(second):0.####} · GRID 1/{_document?.GridDivisor ?? 16}";
    }

    private static double BpmAt(EditorChartDocument chart, double bar)
    {
        var value = chart.Bpms.FirstOrDefault()?.Bpm ?? 0;
        foreach (var bpm in chart.Bpms)
        {
            if (bpm.Bar <= bar)
                value = bpm.Bpm;
            else
                break;
        }
        return value;
    }

    private void SpeedDownClicked(object? sender, RoutedEventArgs e) =>
        SetVisualSpeed(StageCanvas.PlayerSpeed - 0.1);

    private void SpeedUpClicked(object? sender, RoutedEventArgs e) =>
        SetVisualSpeed(StageCanvas.PlayerSpeed + 0.1);

    private void SetVisualSpeed(double speed, bool announce = true)
    {
        speed = Math.Round(Math.Clamp(speed, 0.3, 4.0), 1,
            MidpointRounding.AwayFromZero);
        StageCanvas.PlayerSpeed = speed;
        SpeedValueText.Text = $"{speed:0.0}×";
        if (announce)
            SetStatus($"Visual speed {speed:0.0}×.");
    }

    private void OffsetDownClicked(object? sender, RoutedEventArgs e) => AdjustOffset(-1);
    private void OffsetUpClicked(object? sender, RoutedEventArgs e) => AdjustOffset(1);

    private void AdjustOffset(int direction)
    {
        if (_document is null)
        {
            SetStatus("Load a chart before adjusting its audio offset.");
            return;
        }
        var fine = _shiftDown;
        var delta = direction * (fine ? 0.001 : 0.005);
        var next = Math.Round(_document.SelectedChart.AudioOffsetSec + delta, 6);
        ExecuteCommand(new EditChartOffsetCommand(next));
    }

    private void StageSelectionChanged(object? sender, EditorSelectionInfo? selection)
    {
        SelectionEmptyCard.IsVisible = selection is null;
        SelectionCard.IsVisible = selection is not null;
        if (selection is null)
        {
            _document?.Select(EditorSelection.None);
            ClearInspector();
            UpdateCommandButtons();
            return;
        }
        SelectionKindText.Text = selection.Kind;
        SelectionIdText.Text = selection.Id;
        SelectionTrackText.Text = selection.Track;
        SelectionTimeText.Text = selection.Time;
        SelectionPositionText.Text = selection.Position;
        SelectionWidthText.Text = selection.Width;
        SelectionDetailText.Text = selection.Detail;
        if (_document is not null)
        {
            var kind = selection.Kind == "EVENT" ? EditorSelectionKind.Bpm : EditorSelectionKind.Note;
            _document.Select(new EditorSelection(kind, selection.Id));
            PopulateSelectionEditors();
        }
        SetWorkspace(WorkspacePage.Main);
        UpdateCommandButtons();
    }

    private void PopulateSelectionEditors()
    {
        if (_document is null || _activeChart is null)
            return;
        SelectionEmptyCard.IsVisible = false;
        SelectionCard.IsVisible = true;

        if (_document.Selection.Kind == EditorSelectionKind.Note)
        {
            var note = _document.SelectedChart.Notes.SingleOrDefault(item => item.Id == _document.Selection.Id);
            if (note is null)
                return;
            SelectionBarBox.Text = note.Time.ToDouble().ToString("0.########", CultureInfo.InvariantCulture);
            SelectionCenterBox.Text = note.Center.ToString("0.########", CultureInfo.InvariantCulture);
            SelectionWidthBox.Text = note.Width.ToString("0.########", CultureInfo.InvariantCulture);
            NoteFieldsPanel.IsVisible = true;
            BpmFieldsPanel.IsVisible = false;
            var hasTerminal = note.Type is V2NoteType.Hold or V2NoteType.Mixer && note.Nodes.Count > 0;
            TailFieldsPanel.IsVisible = hasTerminal;
            if (hasTerminal)
            {
                var tail = note.Nodes[^1];
                TailBarBox.Text = tail.Time.ToDouble().ToString("0.########", CultureInfo.InvariantCulture);
                TailCenterBox.Text = tail.Center.ToString("0.########", CultureInfo.InvariantCulture);
                TailWidthBox.Text = tail.Width.ToString("0.########", CultureInfo.InvariantCulture);
                TailPreservedText.Text = note.Nodes.Count > 1
                    ? $"+ {note.Nodes.Count - 1} intermediate node(s) preserved"
                    : "Terminal node";
            }
        }
        else if (_document.Selection.Kind == EditorSelectionKind.Bpm)
        {
            var bpm = _document.SelectedChart.Bpms.SingleOrDefault(item =>
                item.Time.ToString() == _document.Selection.Id);
            if (bpm is null)
                return;
            BpmBarBox.Text = bpm.Time.ToDouble().ToString("0.########", CultureInfo.InvariantCulture);
            BpmValueBox.Text = bpm.Bpm.ToString("0.########", CultureInfo.InvariantCulture);
            NoteFieldsPanel.IsVisible = false;
            TailFieldsPanel.IsVisible = false;
            BpmFieldsPanel.IsVisible = true;
        }
    }

    private void ApplyProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null)
            return;
        var title = (ProjectTitleBox.Text ?? string.Empty).Trim();
        var artist = (ProjectArtistBox.Text ?? string.Empty).Trim();
        if (title.Length == 0 || artist.Length == 0)
        {
            SetStatus("Project title and artist are required.");
            return;
        }
        ExecuteCommand(new EditProjectCommand(title, artist, NullIfEmpty(ProjectAudioBox.Text)));
    }

    private void ApplyNoteClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _document.Selection.Kind != EditorSelectionKind.Note ||
            _document.Selection.Id is null)
            return;
        if (!TryReadPointFields(out var time, out var center, out var width))
            return;

        var note = _document.SelectedChart.Notes.Single(item => item.Id == _document.Selection.Id);
        if (TailFieldsPanel.IsVisible)
        {
            if (!TryReadBar(TailBarBox.Text, out var tailTime) ||
                !TryReadFinite(TailCenterBox.Text, out var tailCenter) ||
                !TryReadPositive(TailWidthBox.Text, out var tailWidth))
                return;
            ExecuteCommand(new CompositeEditorCommand(
                new MoveNoteCommand(note.Id, time, center, width),
                new EditPathTerminalCommand(note.Id, tailTime, tailCenter, tailWidth)));
            return;
        }
        ExecuteCommand(new MoveNoteCommand(note.Id, time, center, width));
    }

    private void ApplyBpmClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _document.Selection.Kind != EditorSelectionKind.Bpm ||
            _document.Selection.Id is null)
            return;
        if (!TryReadBar(BpmBarBox.Text, out var time) ||
            !TryReadPositive(BpmValueBox.Text, out var bpm))
            return;
        var original = _document.SelectedChart.Bpms.FirstOrDefault(item =>
            item.Time.ToString() == _document.Selection.Id);
        if (original is null)
        {
            SetStatus("Select a BPM marker first.");
            return;
        }
        ExecuteCommand(new EditBpmCommand(original.Time, time, bpm));
    }

    private bool TryReadPointFields(out ExactBarTime time, out double center, out double width)
    {
        time = ExactBarTime.Zero;
        center = width = 0;
        return TryReadBar(SelectionBarBox.Text, out time) &&
               TryReadFinite(SelectionCenterBox.Text, out center) &&
               TryReadPositive(SelectionWidthBox.Text, out width);
    }

    private bool TryReadBar(string? text, out ExactBarTime time)
    {
        time = ExactBarTime.Zero;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var bar) ||
            !double.IsFinite(bar) || bar < 0)
        {
            SetStatus("BAR must be a finite non-negative number.");
            return false;
        }
        try
        {
            var exact = ExactBarTime.FromDouble(bar);
            time = _document?.SnapEnabled == true
                ? EditorGeometry.Snap(exact, _document.GridDivisor)
                : exact;
            return true;
        }
        catch (Exception exception)
        {
            SetStatus($"Invalid BAR: {exception.Message}");
            return false;
        }
    }

    private bool TryReadFinite(string? text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value))
            return true;
        SetStatus("Numeric value must be finite.");
        return false;
    }

    private bool TryReadPositive(string? text, out double value)
    {
        if (TryReadFinite(text, out value) && value > 0)
            return true;
        SetStatus("Numeric value must be greater than zero.");
        return false;
    }

    private void StagePlacementRequested(object? sender, EditorPlacementRequest request)
    {
        if (_document is null || _activeChart is null)
            return;
        try
        {
            var exact = ExactBarTime.FromDouble(request.Bar);
            var time = _document.SnapEnabled
                ? EditorGeometry.Snap(exact, _document.GridDivisor)
                : exact;
            if (request.Tool == EditorTool.Bpm)
            {
                _pendingBpmTime = time;
                BpmPlacementBarText.Text = $"BAR {time}";
                BpmPlacementValueBox.Text = BpmAt(_activeChart, request.Bar)
                    .ToString("0.###", CultureInfo.InvariantCulture);
                BpmPopup.IsOpen = true;
                Dispatcher.UIThread.Post(() =>
                {
                    BpmPlacementValueBox.Focus();
                    BpmPlacementValueBox.SelectAll();
                });
                return;
            }

            var type = request.Tool switch
            {
                EditorTool.Drag => V2NoteType.Drag,
                EditorTool.Hold => V2NoteType.Hold,
                _ => V2NoteType.Tap,
            };
            var note = new EditableNote
            {
                Id = _document.AllocateId(type switch
                {
                    V2NoteType.Hold => "hold",
                    V2NoteType.Drag => "drag",
                    _ => "tap",
                }),
                Type = type,
                Track = request.Track switch
                {
                    UiEditorTrack.Left => CoreEditorTrack.Left,
                    UiEditorTrack.Right => CoreEditorTrack.Right,
                    _ => CoreEditorTrack.Center,
                },
                Time = time,
                Center = request.Center,
                Width = request.Width,
            };
            if (type == V2NoteType.Hold)
            {
                if (request.TailBar is not { } tailBar || request.TailCenter is not { } tailCenter ||
                    request.TailWidth is not { } tailWidth)
                    throw new InvalidOperationException("Hold placement requires a terminal tail.");
                var tailExact = ExactBarTime.FromDouble(tailBar);
                var tailTime = _document.SnapEnabled
                    ? EditorGeometry.Snap(tailExact, _document.GridDivisor)
                    : tailExact;
                if (tailTime <= time)
                    throw new InvalidOperationException("Hold tail must be later than the head.");
                note.Nodes.Add(new EditablePathNode
                {
                    Id = _document.AllocateId("hold-node"),
                    Time = tailTime,
                    Center = tailCenter,
                    Width = tailWidth,
                    Judge = true,
                    CurveToNext = null,
                });
            }
            ExecuteCommand(new AddNoteCommand(note));
        }
        catch (Exception exception)
        {
            SetStatus($"Placement failed: {exception.Message}");
        }
    }

    private void BpmPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitBpmPlacement();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelBpmPlacement();
            e.Handled = true;
        }
    }

    private void CommitBpmPlacementClicked(object? sender, RoutedEventArgs e) => CommitBpmPlacement();
    private void CancelBpmPlacementClicked(object? sender, RoutedEventArgs e) => CancelBpmPlacement();

    private void CommitBpmPlacement()
    {
        if (_pendingBpmTime is not { } time)
            return;
        if (!TryReadPositive(BpmPlacementValueBox.Text, out var bpm))
            return;
        BpmPopup.IsOpen = false;
        _pendingBpmTime = null;
        ExecuteCommand(new AddBpmCommand(time, bpm));
    }

    private void CancelBpmPlacement()
    {
        BpmPopup.IsOpen = false;
        _pendingBpmTime = null;
        SetStatus("BPM placement cancelled.");
    }

    private void UndoClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null)
            return;
        _document.Undo();
        RefreshAfterCommand();
    }

    private void RedoClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null)
            return;
        _document.Redo();
        RefreshAfterCommand();
    }

    private void DeleteSelectedClicked(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _document.Selection.Id is null)
            return;
        try
        {
            if (_document.Selection.Kind == EditorSelectionKind.Note)
            {
                _document.Execute(new DeleteNoteCommand(_document.Selection.Id));
            }
            else if (_document.Selection.Kind == EditorSelectionKind.Bpm)
            {
                var bpm = _document.SelectedChart.Bpms.FirstOrDefault(item =>
                    item.Time.ToString() == _document.Selection.Id);
                if (bpm is null)
                    return;
                _document.Execute(new DeleteBpmCommand(bpm.Time));
            }
            RefreshAfterCommand();
        }
        catch (Exception exception)
        {
            SetStatus($"Delete failed: {exception.Message}");
        }
    }

    private void ExecuteCommand(IEditorCommand command)
    {
        if (_document is null)
            return;
        try
        {
            _document.Execute(command);
            RefreshAfterCommand();
        }
        catch (Exception exception)
        {
            SetStatus($"Command failed: {exception.Message}");
        }
    }

    private void RefreshAfterCommand()
    {
        if (_document is null)
            return;
        RefreshActiveChart(_document.SelectedChartId);
        SetStatus(_document.IsDirty ? "Document changed · unsaved" : "Document is saved.");
    }

    private void UndoRedoKey(bool redo)
    {
        if (_document is null)
            return;
        if (redo)
            _document.Redo();
        else
            _document.Undo();
        RefreshAfterCommand();
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
            _shiftDown = true;
        if (e.Source is TextBox)
            return;
        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (e.Key == Key.N)
            {
                ShowNewProjectPage();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.O)
            {
                OpenAndLoadClicked(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.S)
            {
                _ = SaveAsync(asNewTarget: (e.KeyModifiers & KeyModifiers.Shift) != 0);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Z)
            {
                UndoRedoKey(false);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Y)
            {
                UndoRedoKey(true);
                e.Handled = true;
                return;
            }
        }
        if (e.KeyModifiers != KeyModifiers.None)
            return;
        switch (e.Key)
        {
            case Key.Space: PlayClicked(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Delete: DeleteSelectedClicked(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.E: SetTool(EditorTool.Select); break;
            case Key.D1:
            case Key.NumPad1: SetTool(EditorTool.Tap); break;
            case Key.D2:
            case Key.NumPad2: SetTool(EditorTool.Drag); break;
            case Key.D3:
            case Key.NumPad3: SetTool(EditorTool.Hold); break;
            case Key.B: SetTool(EditorTool.Bpm); break;
            case Key.Left: SetTrack(UiEditorTrack.Left); break;
            case Key.Down: SetTrack(UiEditorTrack.Center); break;
            case Key.Right: SetTrack(UiEditorTrack.Right); break;
        }
    }

    private void UpdateResponsiveLayout()
    {
        if (!EditorShell.IsVisible || EditorShell.ColumnDefinitions[1].Width.Value == 0)
            return;
        EditorShell.ColumnDefinitions[1].Width = new GridLength(Bounds.Width < 1320 ? 320 : 360);
    }

    private void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_document?.HasUnsavedChanges != true)
            return;
        e.Cancel = true;
        SetStatus("Unsaved document: save it before closing the editor.");
        if (EditorShell.IsVisible)
            SetWorkspace(WorkspacePage.Publish);
    }

    private void WindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
            _shiftDown = false;
    }

    private void ShowMainWorkspaceClicked(object? sender, RoutedEventArgs e) => SetWorkspace(WorkspacePage.Main);
    private void ShowProjectWorkspaceClicked(object? sender, RoutedEventArgs e) => SetWorkspace(WorkspacePage.Project);
    private void ShowEventsWorkspaceClicked(object? sender, RoutedEventArgs e) => SetWorkspace(WorkspacePage.Events);
    private void ShowValidationWorkspaceClicked(object? sender, RoutedEventArgs e) => SetWorkspace(WorkspacePage.Validation);
    private void ShowPublishWorkspaceClicked(object? sender, RoutedEventArgs e) => SetWorkspace(WorkspacePage.Publish);

    private void ToggleWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        var collapsed = EditorShell.ColumnDefinitions[1].Width.Value == 0;
        EditorShell.ColumnDefinitions[1].Width = new GridLength(collapsed ?
            (Bounds.Width < 1320 ? 320 : 360) : 0);
    }

    private void ToggleFileMenuClicked(object? sender, RoutedEventArgs e)
    {
        FileMenuPopup.PlacementTarget = sender as Control;
        FileMenuPopup.IsOpen = !FileMenuPopup.IsOpen;
    }

    private void NewFromMenuClicked(object? sender, RoutedEventArgs e)
    {
        FileMenuPopup.IsOpen = false;
        ShowNewProjectPage();
    }

    private void ReturnHomeClicked(object? sender, RoutedEventArgs e)
    {
        FileMenuPopup.IsOpen = false;
        ShowWelcomePage();
    }

    private void SetWorkspace(WorkspacePage page)
    {
        _workspacePage = page;
        MainWorkspace.IsVisible = page == WorkspacePage.Main;
        ProjectWorkspace.IsVisible = page == WorkspacePage.Project;
        EventsWorkspace.IsVisible = page == WorkspacePage.Events;
        ValidationWorkspace.IsVisible = page == WorkspacePage.Validation;
        PublishWorkspace.IsVisible = page == WorkspacePage.Publish;
        SetSelected(MainPageButton, page == WorkspacePage.Main);
        SetSelected(ProjectPageButton, page == WorkspacePage.Project);
        SetSelected(EventsPageButton, page == WorkspacePage.Events);
        SetSelected(ValidationPageButton, page == WorkspacePage.Validation);
        SetSelected(PublishPageButton, page == WorkspacePage.Publish);
    }

    private async void SaveClicked(object? sender, RoutedEventArgs e) => await SaveAsync(asNewTarget: false);
    private async void SaveAsClicked(object? sender, RoutedEventArgs e) => await SaveAsync(asNewTarget: true);

    private async Task SaveAsync(bool asNewTarget)
    {
        await Task.Yield();
        if (_document is null)
        {
            SetStatus("Save requires an open document.");
            return;
        }
        if (_document.IsUnpersisted)
        {
            await CreatePackageAsync();
            return;
        }
        if (string.IsNullOrWhiteSpace(_document.PackageDirectory))
        {
            SetStatus("Save requires a package directory.");
            return;
        }
        var diagnostics = UpdateValidation();
        if (diagnostics.Any(item => item.Severity == V2DiagnosticSeverity.Error))
        {
            ShowDiagnostics("SAVE BLOCKED · validation errors", diagnostics);
            SetWorkspace(WorkspacePage.Validation);
            SetStatus("Save blocked by v2 validation diagnostics.");
            return;
        }
        var target = asNewTarget
            ? (SaveTargetBox.Text ?? string.Empty).Trim().Trim('"')
            : _document.PackageDirectory;
        if (string.IsNullOrWhiteSpace(target))
        {
            SetStatus("Choose a Save As target directory.");
            return;
        }
        try
        {
            var source = Path.GetFullPath(_document.PackageDirectory);
            target = Path.GetFullPath(target);
            var revision = _document.IsDirty ? checked(_document.Revision + 1) : _document.Revision;
            var pack = _document.BuildPackSnapshot() with { Revision = revision };
            var result = V2PackageWriter.Write(new V2PackageWriteRequest
            {
                SourceDirectory = source,
                DestinationDirectory = target,
                Pack = pack,
                Charts = _document.BuildAllChartSnapshots(),
                AllowEmptyDestination = !string.Equals(source, target, StringComparison.OrdinalIgnoreCase),
            });
            _document.Revision = revision;
            _document.MarkSaved(target);
            SaveTargetBox.Text = target;
            PackPathBox.Text = target;
            RefreshActiveChart(_document.SelectedChartId);
            DigestList.ItemsSource = result.Digests.Select(item =>
                $"PASS  {item.Key}\n{item.Value.Sha256}").ToArray();
            SaveResultText.Text = $"SAVED · {result.DestinationDirectory}";
            ProjectStateText.Text = "● READY";
            ProjectStateText.Foreground = this.FindResource("CommunityGreen") as IBrush;
            SetStatus($"Saved and verified {result.Digests.Count} gameplay digest(s).");
        }
        catch (V2DiagnosticException exception)
        {
            ShowDiagnostics("SAVE BLOCKED · writer diagnostic",
                [V2Diagnostic.FromException(exception)]);
            SetWorkspace(WorkspacePage.Validation);
            SetStatus($"Save blocked: {exception.Reason}");
        }
        catch (Exception exception)
        {
            SaveResultText.Text = "SAVE FAILED";
            DigestList.ItemsSource = new[] { exception.Message };
            SetWorkspace(WorkspacePage.Publish);
            SetStatus($"Save failed: {exception.Message}");
        }
        UpdateCommandButtons();
    }

    private async Task CreatePackageAsync()
    {
        if (_document is null || _draft is null)
        {
            SetStatus("This draft has no verified external resource plan.");
            return;
        }
        var diagnostics = UpdateValidation();
        if (diagnostics.Any(item => item.Severity == V2DiagnosticSeverity.Error))
        {
            ShowDiagnostics("CREATE BLOCKED · resolve validation errors", diagnostics);
            SetWorkspace(WorkspacePage.Validation);
            SetStatus("Add at least one valid scoring Note before creating the strict v2 package.");
            return;
        }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose an empty destination for the new v2 package",
            AllowMultiple = false,
        });
        if (folders.Count != 1)
            return;
        var target = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
        try
        {
            var result = V2PackageWriter.Create(_draft.BuildPackageCreateRequest(target,
                allowEmptyDestination: true));
            _document.MarkSaved(result.DestinationDirectory);
            SaveTargetBox.Text = result.DestinationDirectory;
            PackPathBox.Text = result.DestinationDirectory;
            DigestList.ItemsSource = result.Digests.Select(item =>
                $"PASS  {item.Key}\n{item.Value.Sha256}").ToArray();
            SaveResultText.Text = $"CREATED · {result.DestinationDirectory}";
            _draft = null;
            RefreshActiveChart(_document.SelectedChartId);
            SetStatus($"Created strict v2 package and verified {result.Digests.Count} gameplay digest(s).");
        }
        catch (V2DiagnosticException exception)
        {
            ShowDiagnostics("CREATE BLOCKED · writer diagnostic",
                [V2Diagnostic.FromException(exception)]);
            SetWorkspace(WorkspacePage.Validation);
            SetStatus($"Package creation blocked: {exception.Reason}");
        }
        catch (Exception exception)
        {
            SetWorkspace(WorkspacePage.Publish);
            SaveResultText.Text = "CREATE FAILED";
            DigestList.ItemsSource = new[] { exception.Message };
            SetStatus($"Package creation failed: {exception.Message}");
        }
        UpdateCommandButtons();
    }

    private IReadOnlyList<V2Diagnostic> UpdateValidation()
    {
        if (_document is null)
        {
            ValidationHeadlineText.Text = "Waiting for a package";
            ValidationDetailText.Text = "Load a v2 package into the editable document core.";
            ValidationList.ItemsSource = null;
            return [];
        }
        var diagnostics = EditorValidationService.Validate(_document);
        if (_document.IsUnpersisted && diagnostics.Any(item => item.Severity == V2DiagnosticSeverity.Error &&
                item.JsonPointer == "/" && item.Reason.Contains("main judgement", StringComparison.Ordinal)))
        {
            ValidationHeadlineText.Text = "DRAFT · add a scoring Note to create the package";
            ValidationHeadlineText.Foreground = this.FindResource("CommunityAmber") as IBrush;
            ValidationDetailText.Text = "Empty charts are valid in memory, but strict v2 packages require at least one main judgement unit.";
            ValidationList.ItemsSource = diagnostics.Select(item =>
                $"{item.Severity.ToString().ToUpperInvariant()}  {item.SourceName}{item.JsonPointer}\n{item.Reason}").ToArray();
            return diagnostics;
        }
        ShowDiagnostics(diagnostics.Any(item => item.Severity == V2DiagnosticSeverity.Error)
            ? "FAIL · core validation errors"
            : diagnostics.Any(item => item.Severity == V2DiagnosticSeverity.Warning)
                ? "WARN · core validation passed with warnings"
                : "PASS · core validation passed", diagnostics);
        return diagnostics;
    }

    private void ShowDiagnostics(string headline, IReadOnlyList<V2Diagnostic> diagnostics)
    {
        ValidationHeadlineText.Text = headline;
        ValidationHeadlineText.Foreground = headline.StartsWith("PASS", StringComparison.Ordinal)
            ? this.FindResource("CommunityGreen") as IBrush
            : headline.StartsWith("WARN", StringComparison.Ordinal)
                ? this.FindResource("CommunityAmber") as IBrush
                : this.FindResource("CommunityPink") as IBrush;
        ValidationDetailText.Text = diagnostics.Count == 0
            ? "No errors or warnings. Writer re-verifies the staged package before replacement."
            : $"{diagnostics.Count} diagnostic(s) from the current snapshot.";
        ValidationList.ItemsSource = diagnostics.Count == 0
            ? new[] { "PASS  no diagnostics" }
            : diagnostics.Select(item =>
                $"{item.Severity.ToString().ToUpperInvariant()}  {item.SourceName}{item.JsonPointer}\n{item.Reason}").ToArray();
    }

    private void UpdateCommandButtons()
    {
        UndoButton.IsEnabled = _document?.CanUndo == true;
        RedoButton.IsEnabled = _document?.CanRedo == true;
        DeleteButton.IsEnabled = _document?.Selection.Kind is EditorSelectionKind.Note or EditorSelectionKind.Bpm;
        SaveButton.IsEnabled = _document?.HasUnsavedChanges == true;
        SaveAsButton.IsEnabled = _document is not null && !_document.IsUnpersisted;
        DirtyText.Text = _document?.IsUnpersisted == true
            ? "● MEMORY DRAFT"
            : _document?.HasUnsavedChanges == true
                ? "● UNSAVED"
                : _document is null ? "NO DOCUMENT" : "● CLEAN";
    }

    private void SetStatus(string message)
    {
        GlobalStatusText.Text = message;
        StageStatusText.Text = message;
        if (_document?.HasUnsavedChanges == true)
        {
            ProjectStateText.Text = "● UNSAVED";
            ProjectStateText.Foreground = this.FindResource("CommunityAmber") as IBrush;
        }
        else if (_document is not null)
        {
            ProjectStateText.Text = "● READY";
            ProjectStateText.Foreground = this.FindResource("CommunityGreen") as IBrush;
        }
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalMinutes:00}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    private static string FormatOffset(double seconds) =>
        $"{seconds * 1000:+0;-0;0}ms";

    private static string? FindDefaultGoldenPack()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "schemas", "chart-format-v2", "examples", "golden-pack"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schemas", "chart-format-v2", "examples", "golden-pack")),
        };
        return candidates.FirstOrDefault(directory => File.Exists(Path.Combine(directory, "meta.json")));
    }
}
