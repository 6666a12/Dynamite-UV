using Avalonia.Controls;
using DynamiteUniverse.ChartEditor.Core;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

internal sealed class VideoExportFlow
{
    private readonly VideoExportService _service = new();

    public async Task Show(Window owner, EditorDocumentSession session, WorkbenchUserStore store, IWorkbenchLocalizer text,
        Func<bool> blocked, Action<bool> setBusy, Action pauseEditor, Action<string> report)
    {
        if (blocked() || session.Document is not { } document) return;
        setBusy(true);
        try
        {
            pauseEditor();
            var chart = document.SelectedChart;
            var version = document.ChangeVersion;
            var previewMaximum = chart.Preview?.DurationSec ?? document.Preview?.DurationSec ?? 0;
            var directory = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (!Directory.Exists(directory)) directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var source = new VideoExportSource(document.Title, $"{chart.Difficulty} Lv {chart.Level?.ToString() ?? "—"}",
                document.HasUnsavedChanges, previewMaximum, Path.Combine(directory, FileName(document.Title, chart.Id)));
            var configured = store.Load<VideoExportService.Preferences>(VideoExportService.PreferenceFile)?.FfmpegPath;
            var settings = new VideoExportSettingsDialog(text, source, configured, _service.ProbeAsync);
            VideoExportOptions? options;
            try { options = await settings.ShowDialog<VideoExportOptions?>(owner); }
            finally { await settings.ProbeCleanup; }
            if (options is null) return;
            if (!ReferenceEquals(document, session.Document) || document.SelectedChartId != chart.Id || document.ChangeVersion != version)
                throw new InvalidOperationException(text["VideoExport.SourceChanged"]);
            store.Save(VideoExportService.PreferenceFile, new VideoExportService.Preferences(options.FfmpegPath));
            var task = new VideoExportProgressDialog(text,
                (feedback, token) => _service.ExportAsync(session, options, feedback, token));
            if (await task.ShowDialog<bool>(owner)) report($"{text["VideoExport.Success"]} {options.OutputPath}");
        }
        catch (Exception error) { report(text.Format("Message.FileError", error.Message)); }
        finally { setBusy(false); }
    }

    private static string FileName(string title, string id)
    {
        var illegal = Path.GetInvalidFileNameChars();
        var safe = new string(title.Take(80).Select(c => illegal.Contains(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return $"{(safe.Length == 0 ? "Chart" : safe)} - {id}.mp4";
    }
}
