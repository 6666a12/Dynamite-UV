using System.Text.Json;

namespace DynamiteUniverse.Editor.Shell.VideoExport;

internal static class VideoExportReport
{
    internal static (int Trim, int Frames) Validate(JsonElement report, string chart, int preview, int hold)
    {
        if (report.GetProperty("version").GetInt32() != 2)
            throw new InvalidDataException("The recorder report version does not match this editor. Use the bundled recorder.");
        if (!report.GetProperty("pass").GetBoolean() || !report.GetProperty("autoResult").GetBoolean() ||
            report.GetProperty("chartId").GetString() != chart || report.GetProperty("previewFrames").GetInt32() != preview ||
            report.GetProperty("resultHoldFrames").GetInt32() != hold)
            throw new InvalidDataException("The worker did not confirm the selected difficulty, Auto result and durations.");

        var available = report.GetProperty("previewAvailableFrames").GetInt32();
        var recorded = report.GetProperty("recordedPreviewFrames").GetInt32();
        var max = 60 * VideoExportService.Fps;
        if (available < 0 || available > max || (preview == 0 && available != 0) || recorded != Math.Min(preview, available))
            throw new InvalidDataException("The recorder reported an invalid decoded preview duration.");

        var trim = report.GetProperty("trimFrame").GetInt32();
        var marks = report.GetProperty("frames");
        int Frame(string key) => marks.GetProperty(key).GetInt32();
        var handoff = Frame("handoff");
        var revealed = Frame("result-revealed");
        var last = Frame("result-held");
        var total = report.GetProperty("totalFrames").GetInt32();
        if (trim < 0 || handoff < trim || revealed <= handoff || last < revealed || total != (long)last + 1 || last - revealed != hold)
            throw new InvalidDataException("Invalid clip or result boundaries.");

        var hasStarted = marks.TryGetProperty("preview-playing", out var start);
        var hasEnded = marks.TryGetProperty("preview-ended", out var end);
        var valid = recorded == 0
            ? trim == handoff && !hasStarted && !hasEnded
            : hasStarted && hasEnded && start.GetInt32() >= trim && (long)end.GetInt32() - start.GetInt32() == recorded &&
                end.GetInt32() == Frame("start") && end.GetInt32() <= handoff;
        if (!valid) throw new InvalidDataException("Invalid captured preview boundaries.");
        return (trim, total - trim);
    }
}
