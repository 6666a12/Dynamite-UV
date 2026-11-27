using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Storyboard;
using static CoreTests.StoryboardTests;

namespace CoreTests;

internal static class StoryboardUsageTests
{
    internal static void Run(Action<bool, string> check)
    {
        DeclaredAssets(check);
        AbsoluteWindowsAndAliases(check);
        BarLoopsAndSeek(check);
        BoundedExpansion(check);
    }

    private static void DeclaredAssets(Action<bool, string> check)
    {
        var script = "storyboard 0\n" + string.Concat(Enumerable.Range(0, 1024).Select(i => $"asset \"a{i}\" file=\"a{i}.png\"\n"));
        var withRegions = script + string.Concat(Enumerable.Range(0, 140).Select(i =>
            $"region \"r{i}\" asset=\"a0\" x=0 y=0 width=1 height=1\n"));
        var document = Parse(withRegions);
        var sizes = document.Assets.Keys.ToDictionary(id => id, _ => new StoryboardTextureSize(4096, 4096), StringComparer.Ordinal);
        check(document.Assets.Count == 1024 && document.Regions.Count == 140 &&
              Compile(document, sizes).BuildTextureUsage(2).Count == 0 &&
              StoryboardSampler.Compile(document, sizes, useLocalPreviewTextureBudget: true).Value is not null,
            "UVSB residence: exactly 1024 whole asset declarations; regions are separate and aggregate RGBA8 is not a compile gate");
        check(StoryboardDocument.Parse(script + "asset \"overflow\" file=\"one-more.png\"", "asset-limit.uvsb")
            .Diagnostic is { Code: "UVSB002", ObjectId: "overflow" }, "UVSB residence: reject the 1025th whole asset declaration");

        var simultaneous = script + string.Concat(Enumerable.Range(0, 129).Select(i =>
            $"sprite \"s{i}\" asset=\"a{i}\" clock=\"media-ms\" {{ active from=200 until=900 }}\n"));
        var usage = Compile(Parse(simultaneous), sizes).BuildTextureUsage(1);
        check(usage.Count == 129 && usage.All(use => use.Intervals.SequenceEqual([new(.2, .9)])),
            "UVSB residence: use table reports 129 simultaneously needed physical paths without imposing caller slot limits");
    }

    private static void AbsoluteWindowsAndAliases(Action<bool, string> check)
    {
        var sampler = Compile(Parse("""
            storyboard 0
            asset "atlas" file="textures/atlas.png"
            asset "alias" file="textures/atlas.png"
            asset "b" file="b.png"
            asset "unused" file="unused.png"
            region "left" asset="atlas" x=0 y=0 width=20 height=10
            region "right" asset="alias" x=20 y=0 width=20 height=10
            group "outer" clock="media-ms" {
                active from=1250 until=4300
                clip-rect x=0 y=0 width=1 height=1
                base "visible" #false
                track "visible" { key at=1500 value=#true; key at=3200 value=#false }
                clip "repeat" at=1000 duration=1000 {
                    repeat count=3 period=1500
                    group "inner" {
                        active from=200 until=900
                        sprite "sequence" {
                            active from=100 until=900
                            base "opacity" 0
                            base "position.x" 16000
                            sequence mode="once-hold" { frame "left" duration-ms=200; frame "b" duration-ms=200; frame "right" duration-ms=200 }
                        }
                    }
                }
            }
            sprite "step" asset="alias" clock="media-ms" { active from=1900 until=2050 }
            sprite "overlap" asset="atlas" clock="media-ms" { active from=2040 until=2200 }
            """), new Dictionary<string, StoryboardTextureSize>
        {
            ["atlas"] = new(40, 10), ["alias"] = new(40, 10), ["b"] = new(20, 10), ["unused"] = new(1, 1)
        });
        var usage = sampler.BuildTextureUsage(10);
        check(usage.Select(use => use.Path).SequenceEqual(["b.png", "textures/atlas.png"]) &&
             usage[0].Intervals.SequenceEqual([new(1.3, 1.5), new(2.8, 3)]) &&
             usage[1].Intervals.SequenceEqual([new(1.25, 1.3), new(1.5, 2.2), new(2.7, 2.8), new(3, 3.4), new(4.2, 4.3)]),
            "UVSB residence: ancestor/clip intersections, once-hold and gaps; region and same-path aliases merge, unaffected by visual flags");
        var images = sampler.BuildImageUsage(3.1).ToDictionary(use => use.Image, use => use.Intervals);
        check(images.Count == 5 &&
              images[new("atlas", new(0, 0, 20, 10))].SequenceEqual([new(1.25, 1.3), new(2.7, 2.8)]) &&
              images[new("alias", new(20, 0, 20, 10))].SequenceEqual([new(1.5, 1.9), new(3, 3.1)]) &&
              images[new("alias", null)].SequenceEqual([new(1.9, 2.05)]) &&
              images[new("atlas", null)].SequenceEqual([new(2.04, 2.2)]),
            "UVSB residence: logical crops and root IDs remain independent, clipped at media duration");
        check(new[] { 0, -.5, double.NaN, double.PositiveInfinity }.All(duration =>
              sampler.BuildImageUsage(duration).Count == 0 && sampler.BuildTextureUsage(duration).Count == 0),
            "UVSB residence: empty and nonfinite media extents return no uses");
    }

    private static void BarLoopsAndSeek(Action<bool, string> check)
    {
        var timeline = new V2BpmTimeline([new(ExactBarTime.Zero, 120), new(new(2, 0, 1), 240)], -.5);
        var sampler = Compile(Parse("""
            storyboard 0
            asset "a" file="a.png"; asset "b" file="b.png"
            sprite "preroll" asset="a" clock="bar" { active from="0" until="0+1/2" }
            clip "cue" clock="bar" at="1" duration="1" {
                repeat count=2 period="1"
                sprite "s" {
                    active from="0+1/4" until="1"
                    sequence mode="loop" { frame "a" duration-ms=200; frame "b" duration-ms=300 }
                }
            }
            """), Sizes(), timeline);
        var usage = sampler.BuildTextureUsage(4.4).ToDictionary(use => use.Path, use => use.Intervals);
        check(usage["a.png"].SequenceEqual([new(0, .5), new(2, 2.2), new(2.5, 2.7), new(3, 3.2), new(3.75, 3.95), new(4.25, 4.4)]) &&
              usage["b.png"].SequenceEqual([new(2.2, 2.5), new(2.7, 3), new(3.2, 3.5), new(3.95, 4.25)]),
            "UVSB residence: Bar BPM and negative offset, absolute repeat/sequence windows, wrap and media trim");
        var items = new List<StoryboardDrawItem>();
        var samples = usage.Values.SelectMany(intervals => intervals.SelectMany(interval => new[]
            { interval.From, Math.BitDecrement(interval.Until), interval.Until, Math.BitIncrement(interval.From) }))
            .Where(second => second >= 0 && second < 4.4).OrderByDescending(second => second).ToArray();
        var matches = true;
        foreach (var second in samples.Concat(samples.Reverse()).Concat(samples))
        {
            sampler.Sample(second, items);
            var expected = usage.Where(pair => pair.Value.Any(i => second >= i.From && second < i.Until)).Select(pair => pair.Key);
            matches &= items.Select(item => item.Image!.Value.AssetId + ".png").SequenceEqual(expected);
        }
        check(matches, "UVSB residence: table and sampler agree on half-open boundaries during forward/backward seeks");
    }

    private static void BoundedExpansion(Action<bool, string> check)
    {
        var script = """
            storyboard 0
            asset "a" file="a.png"; asset "b" file="b.png"
            group "owner" clock="media-ms" {
                active from=1100 until=800000
                sprite "dense" {
                    active from=1000 until=900000
                    sequence mode="loop" { frame "a" duration-ms=1; frame "b" duration-ms=1 }
                }
            }
            sprite "separate" asset="a" clock="media-ms" { active from=800100 until=900000 }
            """;
        var sampler = Compile(Parse(script), Sizes());
        var usage = sampler.BuildImageUsage(850).ToDictionary(use => use.Image.AssetId, use => use.Intervals);
        check(usage["a"].SequenceEqual([new(1.1, 800), new(800.1, 850)]) && usage["b"].SequenceEqual([new(1.1, 800)]),
            "UVSB residence: dense sequence fallback keeps all distinct images only over the affected active window");
        var clipped = sampler.BuildTextureUsage(1.102);
        check(clipped[0].Intervals.SequenceEqual([new(1.1, 1.101)]) && clipped[1].Intervals.SequenceEqual([new(1.101, 1.102)]),
            "UVSB residence: shortened media range does not spend work on earlier or later sequence spans");

        var manyRepeatedSprites = "storyboard 0; asset \"a\" file=\"a.png\"; clip \"c\" at=0 duration=1 clock=\"media-ms\" { repeat count=4096 period=2; " +
            string.Concat(Enumerable.Range(0, 65).Select(i => $"sprite \"s{i}\" asset=\"a\" {{ active from=0 until=1 }}; ")) + "}";
        check(StoryboardSampler.Compile(Parse(manyRepeatedSprites), Sizes()).Diagnostic is { Code: "UVSB002", ObjectId: "c" },
            "UVSB residence: many uses sharing identical repeat moments still hit the independent work limit");
    }

    private static Dictionary<string, StoryboardTextureSize> Sizes() => new() { ["a"] = new(1, 1), ["b"] = new(1, 1) };
}
