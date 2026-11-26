using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Editor.Shell;

internal static class PackageSettingsVerification
{
    private const string PackAudio = "audio/package.wav";
    private const string ChartAudio = "audio/giga.wav";
    private const string TargetId = "giga";
    private static readonly double PreciseStart = Math.BitIncrement(.6);
    private static readonly V2Preview PackPreview = new(PreciseStart, Math.BitIncrement(4.25));
    private static readonly V2Preview ChartPreview = new(Math.BitIncrement(1.2), Math.BitIncrement(3.5));

    internal static void Run(Action<string, bool> check)
    {
        var text = WorkbenchLocalizer.Shared;
        var language = text.CurrentLanguage;
        try
        {
            VerifyInherited(check, text);
            VerifyMissingDefaults(check, text);
            VerifyAuthoredOverrides(check);
        }
        finally { text.SetLanguage(language); }
    }

    private static void VerifyInherited(Action<string, bool> check, IWorkbenchLocalizer text)
    {
        var document = Fixture(PackAudio, PackPreview, null, null);
        var dialog = new PackageSettingsDialog(document);
        dialog.Show();
        try
        {
            var target = Control<TextBlock>(dialog, "TargetChartId");
            var start = Control<TextBox>(dialog, "InheritedPreviewStartValue");
            var length = Control<TextBox>(dialog, "InheritedPreviewDurationValue");
            var audio = Control<TextBox>(dialog, "InheritedAudioPathValue");
            var previewOverride = Control<CheckBox>(dialog, "ChartPreviewEnabledValue");
            var audioOverride = Control<CheckBox>(dialog, "ChartAudioEnabledValue");
            var initialRequest = dialog.RequestForVerification();
            check("Inherited settings use precise authored package defaults without creating overrides",
                target.Text == TargetId && document.SelectedChartId == TargetId &&
                start.IsReadOnly && start.Text == Display(PackPreview.StartSec) &&
                length.IsReadOnly && length.Text == Display(PackPreview.DurationSec) &&
                audio.IsReadOnly && audio.Text == PackAudio &&
                initialRequest.PackPreview == PackPreview && initialRequest.AudioOffsetSec == PreciseStart &&
                initialRequest is { ClearChartPreview: true, ClearChartAudio: true, ChartPreview: null, ChartAudioSource: null } &&
                !Control<Grid>(dialog, "ChartPreviewFields").IsVisible && !Control<Grid>(dialog, "ChartAudioFields").IsVisible);
            foreach (var locale in new[] { "zh-CN", "en" })
            {
                text.SetLanguage(locale);
                Dispatcher.UIThread.RunJobs();
                check($"Package settings localize target, inheritance and unchanged media warning ({locale})",
                    Control<TextBlock>(dialog, "TargetChartIdLabel").Text == text["Settings.TargetChartId"] && target.Text == TargetId &&
                    Control<TextBlock>(dialog, "InheritedPreviewSource").Text == text["Settings.InheritedPreview"] &&
                    Control<TextBlock>(dialog, "InheritedAudioSource").Text == text["Settings.InheritedAudio"] &&
                    Control<TextBlock>(dialog, "UndoHint").Text == text["Settings.UndoHint"]);
                if (locale == "zh-CN" && WorkbenchLayoutVerification.PreviewRequested)
                {
                    var height = dialog.Height;
                    try
                    {
                        dialog.Height = 1040; // Fit both groups in this optional headless sample.
                        WorkbenchLayoutVerification.Capture(dialog, dialog, "package-settings.png");
                    }
                    finally { dialog.Height = height; }
                }
            }
            dialog.UpdateLayout();
            var targetLabel = Control<TextBlock>(dialog, "TargetChartIdLabel");
            check("Difficulty target ID has a separate column and does not overlap its label",
                targetLabel.TranslatePoint(default, dialog) is { } labelPoint && target.TranslatePoint(default, dialog) is { } targetPoint &&
                targetPoint.X >= labelPoint.X + targetLabel.Bounds.Width + 6);
            var packPath = Control<TextBox>(dialog, "PackAudioPathValue");
            var replace = Control<Button>(dialog, "ReplacePackAudioButton");
            check("Package audio path and Replace button use the same height and baseline",
                packPath.Bounds.Height > 0 && packPath.Bounds.Height == replace.Bounds.Height &&
                packPath.TranslatePoint(default, dialog) is { } pathOrigin &&
                replace.TranslatePoint(default, dialog) is { } buttonOrigin && Math.Abs(pathOrigin.Y - buttonOrigin.Y) < 1);

            Control<TextBox>(dialog, "ChartIdValue").Text = "renamed";
            check("The displayed target difficulty is separate from the editable ID",
                target.Text == TargetId && dialog.RequestForVerification().ChartId == "renamed");

            Control<NumericUpDown>(dialog, "PackPreviewStartValue").Value = 1.125m;
            check("Inherited preview reflects a same-dialog package edit", start.Text == Display(1.125));
            previewOverride.IsChecked = true;
            check("First preview override starts from the visible package draft and hides inheritance",
                Control<Grid>(dialog, "ChartPreviewFields").IsVisible && !Control<StackPanel>(dialog, "InheritedPreviewFields").IsVisible &&
                dialog.RequestForVerification().ChartPreview == new V2Preview(1.125, PackPreview.DurationSec));
            Control<NumericUpDown>(dialog, "ChartPreviewStartValue").Value = 2.25m;
            Control<NumericUpDown>(dialog, "ChartPreviewDurationValue").Value = 3.75m;
            previewOverride.IsChecked = false;
            Control<NumericUpDown>(dialog, "PackPreviewDurationValue").Value = 7.5m;
            check("Turning preview override off suppresses its cached draft and shows the latest package draft",
                dialog.RequestForVerification() is { ClearChartPreview: true, ChartPreview: null } && length.Text == Display(7.5));
            previewOverride.IsChecked = true;
            check("Re-enabling preview restores the custom draft, not the package default",
                dialog.RequestForVerification() is { ClearChartPreview: false, ChartPreview: { StartSec: 2.25, DurationSec: 3.75 } });

            audioOverride.IsChecked = true;
            dialog.PickAudioForVerification(false, "chosen-chart.wav");
            audioOverride.IsChecked = false;
            dialog.PickAudioForVerification(true, "chosen-package.wav");
            check("Audio inheritance shows the current package choice and wins over a cached chart file",
                Control<StackPanel>(dialog, "InheritedAudioFields").IsVisible && !Control<Grid>(dialog, "ChartAudioFields").IsVisible &&
                audio.Text == "chosen-package.wav" && dialog.RequestForVerification() is
                    { PackAudioSource: "chosen-package.wav", ClearChartAudio: true, ChartAudioSource: null });
            audioOverride.IsChecked = true;
            check("Re-enabling audio restores the chart file without changing the document",
                Control<Grid>(dialog, "ChartAudioFields").IsVisible && !Control<StackPanel>(dialog, "InheritedAudioFields").IsVisible &&
                Control<TextBox>(dialog, "ChartAudioPathValue").Text == "chosen-chart.wav" &&
                dialog.RequestForVerification() is { ClearChartAudio: false, ChartAudioSource: "chosen-chart.wav" } &&
                document.Audio == PackAudio && document.Preview == PackPreview && document.SelectedChart.Audio is null &&
                document.SelectedChart.Preview is null && document.PendingResourceReplacements.Count == 0 && !document.CanUndo);
        }
        finally { dialog.Close(); }
    }

    private static void VerifyMissingDefaults(Action<string, bool> check, IWorkbenchLocalizer text)
    {
        var document = Fixture(null, null, ChartAudio, null);
        var dialog = new PackageSettingsDialog(document);
        dialog.Show();
        try
        {
            Control<CheckBox>(dialog, "ChartAudioEnabledValue").IsChecked = false;
            foreach (var locale in new[] { "zh-CN", "en" })
            {
                text.SetLanguage(locale);
                Dispatcher.UIThread.RunJobs();
                var noValue = new[] { "InheritedPreviewStartValue", "InheritedPreviewDurationValue", "InheritedAudioPathValue" }
                    .Select(name => Control<TextBox>(dialog, name))
                    .All(field => field.IsEffectivelyVisible && field.IsReadOnly && string.IsNullOrEmpty(field.Text) &&
                        field.PlaceholderText == text["Settings.NotSet"]);
                check($"Missing inherited defaults show Not set, never 0, 15 or the hidden chart path ({locale})",
                    noValue && dialog.RequestForVerification() is
                        { PackPreview: null, ChartPreview: null, ChartAudioSource: null, ClearChartAudio: true });
            }
        }
        finally { dialog.Close(); }
    }

    private static void VerifyAuthoredOverrides(Action<string, bool> check)
    {
        var document = Fixture(PackAudio, PackPreview, ChartAudio, ChartPreview);
        var dialog = new PackageSettingsDialog(document);
        try
        {
            var initial = dialog.RequestForVerification();
            check("An untouched settings request keeps both exact previews, the offset, and existing audio",
                initial.PackPreview == PackPreview && initial.ChartPreview == ChartPreview && initial.AudioOffsetSec == PreciseStart &&
                initial is { ClearChartPreview: false, ClearChartAudio: false, ChartAudioSource: null } &&
                !EditorPackageSettings.Apply(document, TargetId, initial));
            var previewOverride = Control<CheckBox>(dialog, "ChartPreviewEnabledValue");
            previewOverride.IsChecked = false;
            previewOverride.IsChecked = true;
            check("Toggling away from an authored preview and back keeps its exact values",
                dialog.RequestForVerification().ChartPreview == ChartPreview);
        }
        finally { dialog.Close(); }
    }

    private static EditorDocument Fixture(string? packAudio, V2Preview? packPreview, string? chartAudio, V2Preview? chartPreview)
    {
        var pack = new V2Pack
        {
            Id = "settings-fixture", Revision = 1, Title = "Fixture", Artist = "Community",
            Audio = packAudio, Preview = packPreview,
            Charts =
            [
                new V2ChartEntry { Id = "hard", Difficulty = V2Difficulty.Hard, Charters = ["Reviewer"],
                    Level = 12, File = "charts/hard.json", Audio = packAudio is null ? "audio/hard.wav" : null },
                new V2ChartEntry { Id = TargetId, Difficulty = V2Difficulty.Giga, Charters = ["Reviewer"],
                    Level = 15, File = "charts/giga.json", Audio = chartAudio, Preview = chartPreview },
            ],
        };
        var charts = pack.Charts.ToDictionary(entry => entry.Id, entry => new V2Chart
        {
            ChartId = entry.Id, AudioOffsetSec = PreciseStart, Bpms = [new(ExactBarTime.Zero, 150)],
            NotesCenter = [], NotesLeft = [], NotesRight = [],
        });
        var document = EditorDocument.FromPackage(null, pack, charts);
        document.SelectChart(TargetId);
        return document;
    }

    private static T Control<T>(PackageSettingsDialog dialog, string name) where T : Control => dialog.FindControl<T>(name)!;
    private static string Display(double value) => value.ToString("R", CultureInfo.CurrentCulture);
}
