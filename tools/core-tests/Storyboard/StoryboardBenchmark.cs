using System.Diagnostics;
using System.Text;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Storyboard;

namespace CoreTests;

internal static class StoryboardBenchmark
{
    internal static int Run()
    {
        const int groups = 24;
        const int spritesPerGroup = 24;
        const int samples = 20_000;
        var source = new StringBuilder("""
            storyboard 0
            asset "atlas" file="synthetic.png"
            region "first" asset="atlas" x=0 y=0 width=16 height=16
            region "second" asset="atlas" x=16 y=0 width=16 height=16

            """);
        for (var i = 0; i < groups; i++)
        {
            var bar = i % 2 == 0;
            var clock = bar ? "bar" : "media-ms";
            var clipAt = bar ? $"\"{i % 3}\"" : (i % 3 * 2000).ToString();
            var clipDuration = bar ? "\"4\"" : "8000";
            var clipPeriod = bar ? "\"5\"" : "10000";
            var midpoint = bar ? "\"2\"" : "4000";
            var firstTime = bar ? "\"0\"" : "0";
            source.AppendLine($"clip \"clip-{i}\" clock=\"{clock}\" at={clipAt} duration={clipDuration} {{");
            source.AppendLine($"repeat count=4 period={clipPeriod}");
            source.AppendLine($"group \"group-{i}\" {{");
            source.AppendLine($"base \"rotation\" {i}; clip-rect x=0 y=0 width=600 height=300");
            for (var j = 0; j < spritesPerGroup; j++)
            {
                source.AppendLine($"sprite \"sprite-{i}-{j}\" order={j} {{");
                source.AppendLine($"active from={firstTime} until={clipDuration}; base \"position.y\" {20 * j}");
                source.AppendLine("sequence mode=\"loop\" { frame \"first\" duration-ms=70; frame \"second\" duration-ms=90 }");
                source.AppendLine($"track \"opacity\" {{ key at={firstTime} value=0; key at={midpoint} value=1; key at={clipDuration} value=0 }}");
                source.AppendLine($"path {{ key at={firstTime} x={j} y={j} c1-x=0 c1-y=85 c2-x=100 c2-y=85 ease=\"quad-in-out\"; key at={clipDuration} x=100 y=0 }}");
                source.AppendLine("}");
            }
            source.AppendLine("} }");
        }
        var timeline = new V2BpmTimeline([new(ExactBarTime.Zero, 120), new(new(3, 1, 8), 180), new(new(10, 0, 1), 145)], .125);
        var textures = new Dictionary<string, StoryboardTextureSize> { ["atlas"] = new(32, 16) };
        var text = source.ToString();
        var start = Stopwatch.GetTimestamp();
        var document = StoryboardTests.Parse(text);
        var parsed = Stopwatch.GetTimestamp();
        var sampler = StoryboardTests.Compile(document, textures, timeline);
        var compiled = Stopwatch.GetTimestamp();
        var items = new List<StoryboardDrawItem>(sampler.MaxDrawItems);
        // Deliberately seek non-monotonically, including repeat gaps. Warm up allocation counters
        // and tiered JIT before timing the sampling loop.
        for (var i = 0; i < 4096; i++) sampler.Sample(i * .137 % 40, items);
        long drawn = 0;
        double checksum = 0;
        var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
        var sampleStart = Stopwatch.GetTimestamp();
        for (var i = 0; i < samples; i++)
        {
            sampler.Sample((i * 913u % 40_000) / 1000d, items);
            drawn += items.Count;
            if (items.Count > 0) checksum += items[^1].Opacity + items[^1].Transform.M31;
        }
        var elapsed = Stopwatch.GetElapsedTime(sampleStart);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
        Console.WriteLine($"Storyboard benchmark: {groups * spritesPerGroup} sprites, {Encoding.UTF8.GetByteCount(text):N0} bytes, {samples:N0} seek samples");
        Console.WriteLine($"Parse: {Stopwatch.GetElapsedTime(start, parsed).TotalMilliseconds:F2} ms; compile: {Stopwatch.GetElapsedTime(parsed, compiled).TotalMilliseconds:F2} ms");
        Console.WriteLine($"Sampling: {elapsed.TotalMilliseconds:F1} ms ({elapsed.TotalMicroseconds / samples:F2} µs/sample), {allocated:N0} managed bytes");
        Console.WriteLine($"Draw items: {drawn:N0} ({(double)drawn / samples:F1}/sample), checksum: {checksum:F3}");
        return drawn == 0 || !double.IsFinite(checksum) ? 1 : 0;
    }
}
