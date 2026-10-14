namespace DynamiteUniverse.Shared.Judge;

public static class MixerTimeline
{
    public static Func<double, double> BarAt(Chart.Chart chart, SustainPath path)
    {
        if (path.V2BpmTimeline is { } timeline) return timeline.RuntimeBarAt;
        if (chart.Sections.Count > 0) return chart.SecondsToBarTime;
        // Legacy-direct charts without a BPM map use the same baked path segments
        // as their tick construction. Extrapolate the terminal segment for deadlines.
        return second =>
        {
            for (var i = 1; i < path.Nodes.Count; i++)
            {
                var a = path.Nodes[i - 1];
                var b = path.Nodes[i];
                if (second > b.Second && i < path.Nodes.Count - 1) continue;
                if (b.Second > a.Second)
                    return a.BarTime + (second - a.Second) * (b.BarTime - a.BarTime) / (b.Second - a.Second);
            }
            return path.Head.BarTime + (second - path.StartTime) * 150 / 240;
        };
    }
}
