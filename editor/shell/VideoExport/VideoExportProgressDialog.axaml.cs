using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

public sealed partial class VideoExportProgressDialog : Window
{
    private readonly IWorkbenchLocalizer _text;
    private readonly Func<IProgress<VideoExportProgress>, CancellationToken, Task> _export;
    private readonly TextBlock[] _steps;
    private readonly CancellationTokenSource _cancellation = new();
    private VideoExportPhase _phase;
    private bool _running, _succeeded, _hasOpened, _resultClosed;

    public VideoExportProgressDialog(IWorkbenchLocalizer localizer,
        Func<IProgress<VideoExportProgress>, CancellationToken, Task> export)
    {
        (_text, _export) = (localizer, export);
        AvaloniaXamlLoader.Load(this);
        _steps = [C<TextBlock>("PrepareStep"), C<TextBlock>("RenderStep"), C<TextBlock>("EncodeStep"), C<TextBlock>("ValidateStep")];
        Apply(new(VideoExportPhase.Prepare));
        Opened += StartOnOpen;
        Closed += (_, _) => _cancellation.Dispose();
    }

    private T C<T>(string name) where T : Control => this.FindControl<T>(name)!;
    private string PhaseTitle(VideoExportPhase phase) => _text[$"VideoExport.Phase.{phase}"];

    private async void StartOnOpen(object? sender, EventArgs e)
    {
        if (_hasOpened) return;
        _hasOpened = _running = true;
        try
        {
            await _export(new Progress<VideoExportProgress>(update =>
            {
                if (_running && !_cancellation.IsCancellationRequested) Apply(update);
            }), _cancellation.Token);
            _succeeded = true;
            ShowResult("VideoExport.Success");
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
            ShowResult("VideoExport.Canceled");
        }
        catch (VideoExportLowSpaceException error)
        {
            ShowResult("VideoExport.Failed", _text.Format("VideoExport.LowSpace",
                error.OutputDirectory, error.FreeMiB, error.ThresholdMiB));
        }
        catch (Exception error)
        {
            ShowResult("VideoExport.Failed", error.Message);
        }
        finally { _running = false; }
    }

    private void Apply(VideoExportProgress update)
    {
        if (update.Phase < _phase || (int)update.Phase >= _steps.Length) return;
        _phase = update.Phase;
        RefreshSteps();
        var bar = C<ProgressBar>("ProgressValue");
        bar.IsIndeterminate = update.Fraction is not { } fraction || !double.IsFinite(fraction);
        bar.Value = bar.IsIndeterminate ? 0 : Math.Clamp(update.Fraction!.Value, 0, 1) * 100;
        C<TextBlock>("Status").Text = bar.IsIndeterminate ? PhaseTitle(_phase)
            : _text.Format("VideoExport.PhasePercent", PhaseTitle(_phase), bar.Value);
        SetDetail(update.Detail);
    }

    private void RefreshSteps()
    {
        for (var i = 0; i < _steps.Length; i++)
        {
            var title = PhaseTitle((VideoExportPhase)i);
            var current = !_succeeded && i == (int)_phase;
            _steps[i].Text = _succeeded || i < (int)_phase ? $"✓ {title}" : current ? $"● {title}" : $"{i + 1}  {title}";
            _steps[i].Classes.Set("cyan", current);
        }
    }

    private void ShowResult(string message, string? detail = null)
    {
        RefreshSteps();
        var bar = C<ProgressBar>("ProgressValue");
        bar.IsIndeterminate = false;
        bar.IsVisible = _succeeded;
        if (_succeeded) bar.Value = 100;
        C<TextBlock>("Status").Text = _text[message];
        SetDetail(detail);
        var action = C<Button>("ActionButton");
        action.Content = _text["VideoExport.Close"];
        action.IsEnabled = true;
        action.IsDefault = true;
        action.Focus();
    }

    private void SetDetail(string? detail)
    {
        var block = C<SelectableTextBlock>("Detail");
        block.IsVisible = !string.IsNullOrWhiteSpace(detail);
        block.Text = detail;
    }

    private void RequestCancel()
    {
        if (_cancellation.IsCancellationRequested) return;
        C<TextBlock>("Status").Text = _text["VideoExport.Canceling"];
        C<Button>("ActionButton").IsEnabled = false;
        _cancellation.Cancel();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_running) { e.Cancel = true; RequestCancel(); }
        else if (_hasOpened && !_resultClosed)
        {
            e.Cancel = true;
            // An OS close after completion must return the same result as the Close button.
            Avalonia.Threading.Dispatcher.UIThread.Post(CloseWithResult);
        }
        base.OnClosing(e);
    }

    private void ActionClicked(object? sender, RoutedEventArgs e)
    {
        if (_running) RequestCancel();
        else CloseWithResult();
    }

    private void CloseWithResult()
    {
        if (_resultClosed) return;
        _resultClosed = true;
        Close(_succeeded);
    }
}
