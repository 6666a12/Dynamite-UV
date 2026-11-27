using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Storyboard;
using static CoreTests.StoryboardTests;

namespace CoreTests;

internal static class StoryboardPrecisionTests
{
    internal static void Run(Action<bool, string> check)
    {
        SequenceMidpoint(check);
        AncestorBoundaries(check);
        RelativeBarProgress(check);
        ExactBpmWorkLimit(check);
        DecoderInputs(check);
        GradientRampCatalog(check);
    }

    private static void SequenceMidpoint(Action<bool, string> check)
    {
        var timeline = new V2BpmTimeline([new(ExactBarTime.Zero, 240)], Math.ScaleB(1, -106));
        var sampler = Compile(Parse("""
            storyboard 0
            asset "a" file="a.png"; asset "b" file="b.png"
            sprite "s" clock="bar" {
                active from="7+985162418487297/1125899906842624" until="9"
                sequence mode="once-hold" { frame "a" duration-ms=125; frame "b" duration-ms=125 }
            }
            """), new Dictionary<string, StoryboardTextureSize> { ["a"] = new(1, 1), ["b"] = new(1, 1) }, timeline);
        var boundary = Math.BitIncrement(8);
        var items = new List<StoryboardDrawItem>();
        sampler.Sample(8, items);
        var at = items.Single().Image?.AssetId;
        sampler.Sample(boundary, items);
        var intervals = sampler.BuildTextureUsage(8.5);
        check(at == "a" && items.Single().Image?.AssetId == "b" && intervals[0].Intervals.Single().Until == boundary &&
              intervals[1].Intervals.SequenceEqual([new(boundary, 8.5)]),
            "UVSB precision: sub-ULP residual beyond frame midpoint determines both sample and residency boundary");
    }

    private static void AncestorBoundaries(Action<bool, string> check)
    {
        var timeline = new V2BpmTimeline([new(ExactBarTime.Zero, 120)], 0);
        const string near = "8+1/9007199254740991";
        const string script = """
            storyboard 0
            group "parent" clock="bar" {
                active from="0" until="@END@"
                group "nested" {
                    clip "c" at="8" duration="1" {
                        shape "s" kind="rect" width=1 height=1 { active from="0" until="1" }
                    }
                }
            }
            """;
        check(StoryboardSampler.Compile(Parse(script.Replace("@END@", near, StringComparison.Ordinal)),
            NoTextures(), timeline).Diagnostic is { Code: "UVSB052", ObjectId: "c" },
            "UVSB precision: an ancestor active end cannot collapse onto a distinct absolute clip start");
        check(StoryboardSampler.Compile(Parse(script.Replace("@END@", "8", StringComparison.Ordinal)),
            NoTextures(), timeline).Value is not null, "UVSB precision: an exact shared ancestor and clip boundary is allowed");
        var visible = Parse("""
            storyboard 0
            group "parent" clock="bar" {
                track "visible" { key at="8+1/9007199254740991" value=#false }
                clip "c" at="7" duration="2" {
                    shape "s" kind="rect" width=1 height=1 { active from="1" until="2" }
                }
            }
            """);
        check(StoryboardSampler.Compile(visible, NoTextures(), timeline).Diagnostic is { Code: "UVSB052", ObjectId: "s" },
            "UVSB precision: shifted authored child time cannot collapse onto ancestor visibility key");
    }

    private static void RelativeBarProgress(Action<bool, string> check)
    {
        var extreme = Math.ScaleB(1, 53);
        var timeline = new V2BpmTimeline([new(ExactBarTime.Zero, 240 / extreme)], -extreme);
        var sampler = Compile(Parse("""
            storyboard 0
            shape "r" kind="rect" width=1 height=1 clock="bar" {
                active from="1" until="1+1/2251799813685248"
                base "pivot" 0 0
                track "opacity" { key at="1" value=0; key at="1+1/4503599627370496" value=1 }
                path { key at="1" x=0 y=0; key at="1+1/4503599627370496" x=100 y=50 }
            }
            """), timeline: timeline);
        var items = new List<StoryboardDrawItem>();
        sampler.Sample(1, items);
        var draw = items.Single();
        check(draw.Opacity == .5f && draw.Transform.M31 == 50 && draw.Transform.M32 == 25,
            "UVSB precision: an extreme offset/tiny Bar span still interpolates track and path at media midpoint");

        var withTempo = new V2BpmTimeline([new(ExactBarTime.Zero, 240 / extreme), new(new(1, 1, 4503599627370496), 480 / extreme)], -extreme);
        sampler = Compile(Parse("""
            storyboard 0
            shape "r" kind="rect" width=1 height=1 clock="bar" {
                active from="1" until="1+1/1125899906842624"
                track "opacity" { key at="1" value=0; key at="1+1/2251799813685248" value=1 }
            }
            """), timeline: withTempo);
        sampler.Sample(1, items);
        var first = items.Single().Opacity;
        sampler.Sample(2, items);
        var transition = items.Single().Opacity;
        sampler.Sample(2.5, items);
        check(first == .25f && transition == .5f && items.Single().Opacity == .75f,
            "UVSB precision: extreme cross-BPM interpolation follows exact relative Bar fraction at tempo breakpoint");
    }

    private static void ExactBpmWorkLimit(Action<bool, string> check)
    {
        var events = Enumerable.Range(4001, 10000).Where(IsPrime).Take(750)
            .Select((bpm, i) => new V2BpmEvent(new(i, 0, 1), bpm)).ToArray();
        var timeline = new V2BpmTimeline(events, 0);
        var late = Parse("storyboard 0; shape \"late\" kind=\"rect\" width=1 height=1 clock=\"bar\" { active from=\"750\" until=\"751\" }");
        var early = Parse("storyboard 0; shape \"early\" kind=\"rect\" width=1 height=1 clock=\"bar\" { active from=\"0\" until=\"1\" }");
        check(StoryboardSampler.Compile(late, NoTextures(), timeline).Diagnostic is { Code: "UVSB002", ObjectId: "late" } &&
              StoryboardSampler.Compile(early, NoTextures(), timeline).Value is not null,
            "UVSB precision: bound growing exact BPM operands without rejecting a cue that precedes them");

        static bool IsPrime(int n)
        {
            for (var divisor = 2; divisor * divisor <= n; divisor++)
                if (n % divisor == 0) return false;
            return true;
        }
    }

    private static void DecoderInputs(Action<bool, string> check)
    {
        foreach (var source in new[]
        {
            "storyboard 0; backdrop mode=\"replace\" color=\"#FFFFF\\u{0}\"",
            """storyboard 0; shape "r" kind="rect" width=1 height=1 clock="media-ms" { active from=0 until=10; base "tint" "#FF\u{0}FFF" }""",
            """
            storyboard 0
            shape "r" kind="rect" width=1 height=1 clock="media-ms" {
                active from=0 until=10
                linear-gradient from-x=0 from-y=0 to-x=1 to-y=0 {
                    stop at=0 color="#1234567\u{0}"; stop at=1 color="#FFFFFF"
                }
            }
            """
        })
            check(StoryboardDocument.Parse(source, "color.uvsb").Diagnostic is { Code: "UVSB020" },
                "UVSB decode: ASCII hex validation rejects escaped NUL in colors");

        var prefix = string.Concat(Enumerable.Repeat("x/", 470));
        var paths = "storyboard 0\n" + string.Concat(Enumerable.Range(0, 128).Select(i =>
            $"asset \"a{i}\" file=\"branch{i}/{prefix}Leaf.png\"\n"));
        check(StoryboardDocument.Parse(paths + $"asset \"alias\" file=\"branch0/{prefix}Leaf.png\"\n" +
                $"asset \"other\" file=\"different/{prefix}leaf.png\"", "path.uvsb").Value?.Assets.Count == 130 &&
              StoryboardDocument.Parse(paths + $"asset \"collision\" file=\"branch0/{prefix}leaf.png\"", "path.uvsb")
                .Diagnostic is { Code: "UVSB031", ObjectId: "collision" },
            "UVSB decode: deep path segment index preserves aliases and detects cases only on shared paths");
    }

    private static void GradientRampCatalog(Action<bool, string> check)
    {
        const string narrow = """
            storyboard 0
            shape "highlight" kind="rect" width=4000 height=20 clock="media-ms" {
                active from=0 until=1000
                linear-gradient from-x=0 from-y=0 to-x=1 to-y=0 {
                    stop at=0 color="#000000"
                    stop at=0.4991 color="#000000"
                    stop at=0.5 color="#FFFFFF"
                    stop at=0.5009 color="#000000"
                    stop at=1 color="#000000"
                }
            }
            """;
        check(StoryboardLimits.GradientRampWidth == 512 &&
              StoryboardDocument.Parse(narrow, "narrow.uvsb").Diagnostic is { Code: "UVSB020", ObjectId: "highlight" },
            "UVSB gradient: reject authored stripes narrower than the supported ramp spacing");
        var sampler = Compile(Parse("""
            storyboard 0
            shape "later" kind="rect" width=10 height=10 clock="media-ms" order=2 {
                active from=0 until=500
                linear-gradient from-x=0 from-y=0 to-x=1 to-y=0 { stop at=0 color="#FF0000"; stop at=1 color="#00FF00" }
            }
            clip "earlier" clock="media-ms" at=0 duration=200 order=1 {
                repeat count=2 period=250
                shape "repeated" kind="rect" width=10 height=10 {
                    active from=0 until=200
                    linear-gradient from-x=0 from-y=0 to-x=0 to-y=1 { stop at=0 color="#0000FF"; stop at=1 color="#000000" }
                }
            }
            """));
        var items = new List<StoryboardDrawItem>();
        sampler.Sample(.1, items);
        var catalog = sampler.Gradients;
        var matches = catalog.Count == 2 && ReferenceEquals(catalog[0], items[0].Gradient) && ReferenceEquals(catalog[1], items[1].Gradient);
        sampler.Sample(.3, items);
        check(matches && ReferenceEquals(catalog[0], items[0].Gradient) && ReferenceEquals(catalog[1], items[1].Gradient),
            "UVSB gradient: stable compiled catalog follows draw ordering and repeat instances share the authored gradient");
    }

    private static Dictionary<string, StoryboardTextureSize> NoTextures() => new();
}
