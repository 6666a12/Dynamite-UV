using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Storyboard;

namespace CoreTests;

internal static class StoryboardTests
{
    private static readonly IReadOnlyDictionary<string, StoryboardTextureSize> NoTextures =
        new Dictionary<string, StoryboardTextureSize>();

    internal static void Run(Action<bool, string> check)
    {
        Title(check);
        GroupsAndShapes(check);
        BoundTransforms(check);
        BarClipsAndSequences(check);
        MediaSequences(check);
        StoryboardUsageTests.Run(check);
        StoryboardPrecisionTests.Run(check);
        Rejections(check);
    }

    private static void Title(Action<bool, string> check)
    {
        var document = Parse("""
            // Script begins after the comment.
            storyboard 0
            asset "title" file="assets/t\u{0069}tle.png"
            sprite "chorus-title" asset="title" clock="media-ms" order=10 {
                active from=16000 until=18300
                base "position.y" 300
                base "position.x" -180
                base "opacity" 0
                track "position.x" {
                    key at=16000 value=-180 ease="sine-out"
                    key at=16600 value=420
                }
                track "opacity" {
                    key at=16000 value=0
                    key at=16300 value=1 ease="step"
                    key at=17800 value=1
                    key at=18300 value=0
                }
            }
            """);
        check(document.Assets["title"] == "assets/title.png" && !document.UsesBarClock &&
            document.Backdrop.Mode == StoryboardBackdropMode.Keep, "UVSB title: path escape, clock, default backdrop");
        var sampler = Compile(document, new Dictionary<string, StoryboardTextureSize> { ["title"] = new(100, 40) });
        var items = new List<StoryboardDrawItem>(sampler.MaxDrawItems);
        sampler.Sample(15.999, items);
        check(items.Count == 0, "UVSB title: before first instant");
        sampler.Sample(16, items);
        var first = items.Single();
        check(first.Kind == StoryboardDrawKind.Sprite && first.Image?.AssetId == "title" && first.Opacity == 0 &&
              Nearly(Vector2.Transform(new(50, 20), first.Transform), new(-180, 300)), "UVSB title: active start includes opacity-zero sprite");
        sampler.Sample(16.2, items);
        var at = items.Single();
        check(Nearly(Vector2.Transform(new(50, 20), at.Transform), new(120, 300)) &&
              Nearly(at.Opacity, 2f / 3), "UVSB title: sine-out/linear with fractional media seconds");
        sampler.Sample(16.2005, items);
        check(items.Single().Opacity > at.Opacity, "UVSB title: does not round playhead to milliseconds");
        sampler.Sample(17.5, items);
        check(Nearly(Vector2.Transform(new(50, 20), items.Single().Transform), new(420, 300)) &&
              items.Single().Opacity == 1, "UVSB title: last position holds before active end");
        sampler.Sample(18.3, items);
        check(items.Count == 0, "UVSB title: active end is exclusive with final key at the endpoint");
        sampler.Sample(16.2, items);
        check(items.Single() == at, "UVSB title: backwards seek equals direct sample");
        sampler.Sample(double.NaN, items);
        check(items.Count == 0 && sampler.Clips.Count == 0, "UVSB title: non-finite input clears reused state");
    }

    private static void GroupsAndShapes(Action<bool, string> check)
    {
        var document = Parse("""
            storyboard 0; backdrop mode = "replace" color="#10131A"
            shape "z" kind="rect" width=10 height=20 clock="media-ms" { active from=0 until=5000 }
            group "B" clock="media-ms" order=0 {
                base "position.x" 100
                base "position.y" 50
                base "scale.x" 2
                base "rotation" 90
                clip-rect x=1 y=2 width=25 height=30
                group "inner" {
                    base "position.x" 3
                    clip-rect x=0 y=0 width=3 height=5
                    shape "focus" kind="ellipse" width=10 height=4 {
                        active from=0 until=5000
                        base "pivot" 0.5 0
                        base "flip-x" #true
                        base "blend" "add"
                        base "position.x" 7
                        base "position.y" 9
                        linear-gradient from-x=0 from-y=1 to-x=1 to-y=0 {
                            stop at=0 color="#00000000"
                            stop at=1 color="#FFFFFF"
                        }
                        path {
                            key at=1000 x=0 y=0 c1-x=0 c1-y=100 c2-x=100 c2-y=100
                            key at=3000 x=100 y=0
                        }
                        track "tint" {
                            key at=1000 value="#000000" ease="quad-in"
                            key at=3000 value="#FFFFFF"
                        }
                    }
                }
                shape "over" kind="rect" width=4 height=4 order=5000 { active from=0 until=5000 }
                track "visible" { key at=3500 value=#false; key at=4000 value=#true }
            }
            shape "a" kind="rect" width=10 height=20 clock="media-ms" { active from=0 until=5000 }
            """);
        check(document.Backdrop is { Mode: StoryboardBackdropMode.Replace, Color: { R: 16, G: 19, B: 26, A: 255 } },
            "UVSB backdrop: custom opaque replacement");
        check(Parse("storyboard 0; backdrop mode=\"replace\"").Backdrop.Color == new StoryboardColor(16, 19, 26),
            "UVSB backdrop: default dark replacement");
        var sampler = Compile(document);
        var items = new List<StoryboardDrawItem>();
        sampler.Sample(0, items);
        check(items.Select(item => item.Id).SequenceEqual(new[] { "focus", "over", "a", "z" }),
            "UVSB scene: same-layer ordinal and depth-first subtree order");
        var focus = items[0];
        check(focus.Kind == StoryboardDrawKind.Ellipse && focus.Image is null && focus.Blend == StoryboardBlend.Add &&
              Nearly(Vector2.Transform(new(5, 0), focus.Transform), new(91, 70)) &&
              Nearly(Vector2.Transform(new(10, 0), focus.Transform), new(91, 60)),
            "UVSB transform: path base before first key; parent transform, pivot and local flip");
        check(focus.ClipCount == 2 && items[1].ClipCount == 1 && items[2].ClipCount == 0 &&
              Nearly(Vector2.Transform(new(1, 2), sampler.Clips[focus.ClipStart].Transform), new(98, 52)) &&
              Nearly(Vector2.Transform(Vector2.Zero, sampler.Clips[focus.ClipStart + 1].Transform), new(100, 56)),
            "UVSB scene: transformed ancestor clips are scoped and outermost first");
        check(focus.Gradient is { } gradient && gradient.From == new Vector2(0, 1) && gradient.To == new Vector2(1, 0) &&
              gradient.Stops.Count == 2 && gradient.SampleLinear(0.5f) == new Vector4(0.5f) &&
              gradient.SampleLinear(-1) == Vector4.Zero && gradient.SampleLinear(2) == Vector4.One,
            "UVSB gradient: normalized line, straight linear RGB and alpha, clamped ends");
        sampler.Sample(2, items);
        focus = items[0];
        check(Nearly(Vector2.Transform(new(5, 0), focus.Transform), new(25, 156)) &&
              Nearly(focus.Tint.X, .25f), "UVSB scene: cubic midpoint is (50,75), linear RGB tint varies by quad-in");
        sampler.Sample(3.5, items);
        check(items.All(item => item.Id is "a" or "z") && sampler.Clips.Count == 0, "UVSB visibility: subtree hidden at discrete key");
        sampler.Sample(4, items);
        check(items.Count == 4 && Nearly(Vector2.Transform(new(5, 0), items[0].Transform), new(100, 256)),
            "UVSB visibility: subtree reappears holding last path key");

        static float Expected(string ease, float u) => ease switch
        {
            "step" => 0, "linear" => u,
            "sine-in" => 1 - MathF.Cos(MathF.PI * u / 2),
            "sine-out" => MathF.Sin(MathF.PI * u / 2),
            "sine-in-out" => (1 - MathF.Cos(MathF.PI * u)) / 2,
            "quad-in" => u * u, "quad-out" => 1 - (1 - u) * (1 - u),
            _ => u < .5f ? 2 * u * u : 1 - 2 * (1 - u) * (1 - u)
        };
        foreach (var ease in new[] { "step", "linear", "sine-in", "sine-out", "sine-in-out", "quad-in", "quad-out", "quad-in-out" })
        {
            var track = Compile(Parse("storyboard 0; shape \"r\" kind=\"rect\" width=10 height=20 clock=\"media-ms\" { " +
                $"active from=0 until=2000; track \"opacity\" {{ key at=0 value=0 ease=\"{ease}\"; key at=1000 value=1 }} }}"));
            track.Sample(.25, items);
            var match = Nearly(items.Single().Opacity, Expected(ease, .25f));
            track.Sample(.75, items);
            match &= Nearly(items.Single().Opacity, Expected(ease, .75f));
            track.Sample(1, items);
            check(match && items.Single().Opacity == 1, $"UVSB easing: {ease} interior and incoming boundary");
        }

        var rotation = Compile(Parse("""
            storyboard 0
            shape "r" kind="rect" width=10 height=10 clock="media-ms" {
                active from=0 until=2000
                base "pivot" 0 0
                track "rotation" { key at=0 value=0; key at=1000 value=360 }
            }
            """));
        rotation.Sample(.25, items);
        check(Nearly(Vector2.Transform(new(10, 0), items.Single().Transform), new(0, 10)),
            "UVSB transform: positive angle clockwise; 0→360 is a complete winding");
    }

    private static void BoundTransforms(Action<bool, string> check)
    {
        var sampler = Compile(Parse("""
            storyboard 0
            group "parent" clock="media-ms" {
                base "position.x" 14.125; base "position.y" -19.5
                base "scale.x" 1.25; base "scale.y" 0.75; base "rotation" -196
                clip-rect x=1 y=2 width=100 height=30
                shape "static" kind="rect" width=13 height=7 order=0 {
                    active from=0 until=3000
                    base "position.x" 5.375; base "position.y" -3.25
                    base "scale.x" 0.75; base "scale.y" 1.25; base "rotation" 117
                    base "pivot" 0.25 0.75; base "flip-y" #true
                }
                shape "moving" kind="ellipse" width=13 height=7 order=1 {
                    active from=0 until=3000
                    base "position.x" 17.25; base "position.y" 9.25
                    base "scale.x" 0.75; base "scale.y" 1.25; base "rotation" 117
                    base "pivot" 0.25 0.75; base "flip-y" #true
                    track "position.x" { key at=1000 value=-12; key at=2000 value=27 }
                }
                shape "animated" kind="rect" width=13 height=7 order=2 {
                    active from=0 until=3000
                    base "position.x" 5.375; base "position.y" -3.25
                    base "scale.x" 0.75; base "scale.y" 1.25; base "rotation" 117
                    base "pivot" 0.25 0.75; base "flip-y" #true
                    track "scale.x" { key at=1000 value=0; key at=2000 value=2.25 }
                    track "rotation" { key at=1000 value=-450; key at=2000 value=540 }
                    track "flip-x" { key at=1000 value=#true; key at=2000 value=#false }
                }
            }
            """));
        var items = new List<StoryboardDrawItem>(sampler.MaxDrawItems);
        var parent = Transform(14.125f, -19.5f, 1.25f, .75f, -196) * Matrix3x2.Identity;
        var still = Pivot(false) * (Transform(5.375f, -3.25f, .75f, 1.25f, 117) * parent);
        var matches = true;
        // Direct, backwards and fractional seeks, including adjacent binary64 values at a key.
        foreach (var time in new[] { 2, Math.BitDecrement(1), Math.BitIncrement(1), 1, 1.43, 0, Math.BitDecrement(2), 1.43 })
        {
            var x = Key(time, 17.25f, -12, 27);
            var sx = Key(time, .75f, 0, 2.25f);
            var rotation = Key(time, 117, -450, 540);
            var moving = Pivot(false) * (Transform(x, 9.25f, .75f, 1.25f, 117) * parent);
            var animated = Pivot(time >= 1 && time < 2) * (Transform(5.375f, -3.25f, sx, 1.25f, rotation) * parent);
            sampler.Sample(time, items);
            matches &= items.Count == 3 && items[0].Id == "static" && items[1].Id == "moving" && items[2].Id == "animated" &&
                BitsEqual(items[0].Transform, still) && BitsEqual(items[1].Transform, moving) && BitsEqual(items[2].Transform, animated) &&
                sampler.Clips.Count == 1 && BitsEqual(sampler.Clips[0].Transform, parent) &&
                items.All(item => item.ClipStart == 0 && item.ClipCount == 1);
        }
        check(matches, "UVSB transforms: static, position-only and animated bases/pivots match exact composition and shared clip across seeks");

        static Matrix3x2 Transform(float x, float y, float sx, float sy, float rotation)
        {
            var (s, c) = Math.SinCos(Math.IEEERemainder(rotation, 360) * (Math.PI / 180));
            return new((float)(sx * c), (float)(sx * s), (float)(-sy * s), (float)(sy * c), x, y);
        }

        static Matrix3x2 Pivot(bool flipX)
        {
            var x = flipX ? -1f : 1f;
            return new(x, 0, 0, -1, -3.25f * x, 5.25f);
        }

        static float Key(double time, float basis, float first, float last) => time switch
        {
            < 1 => basis,
            1 => first,
            >= 2 => last,
            _ => Vector3.Lerp(new(first), new(last), (float)(time - 1)).X
        };

        static bool BitsEqual(Matrix3x2 a, Matrix3x2 b) =>
            Same(a.M11, b.M11) && Same(a.M12, b.M12) && Same(a.M21, b.M21) && Same(a.M22, b.M22) &&
            Same(a.M31, b.M31) && Same(a.M32, b.M32);
        static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    }

    private static void BarClipsAndSequences(Action<bool, string> check)
    {
        var timeline = new V2BpmTimeline([new(ExactBarTime.Zero, 120), new(new(8, 1, 8), 180)], 0.4);
        var script = """
            storyboard 0
            asset "sheet" file="sheet.png"
            region "left" asset="sheet" x=0 y=0 width=20 height=10
            region "right" asset="sheet" x=20 y=0 width=20 height=10
            group "bar" clock="bar" {
                shape "speed" kind="rect" width=10 height=10 {
                    active from="8" until="8+1/4"
                    base "pivot" 0 0
                    track "position.x" { key at="8" value=0; key at="8+1/4" value=100 }
                }
                clip "repeated" at="8" duration="0+1/8" {
                    repeat count=4 period="0+1/4"
                    sprite "sequence" {
                        active from="0+1/32" until="0+1/8"
                        base "pivot" 1 0
                        base "sampling" "nearest"
                        sequence mode="loop" { frame "left" duration-ms=80; frame "right" duration-ms=80 }
                    }
                    shape "pulse" kind="rect" width=10 height=10 {
                        active from="0" until="0+1/8"
                        track "opacity" { key at="0" value=0; key at="0+1/8" value=1 }
                    }
                }
                clip "consecutive" at="9" duration="0+1/8" {
                    repeat count=2 period="0+1/8"
                    shape "reset" kind="rect" width=10 height=10 {
                        active from="0" until="0+1/8"
                        track "position.x" { key at="0" value=0; key at="0+1/8" value=100 }
                    }
                }
            }
            """;
        var document = Parse(script);
        check(document.UsesBarClock && document.Regions["left"] == new StoryboardImage("sheet", new(0, 0, 20, 10)),
            "UVSB atlas: public catalog exposes crop and root asset");
        var textures = new Dictionary<string, StoryboardTextureSize> { ["sheet"] = new(40, 10) };
        var sampler = Compile(document, textures, timeline);
        var items = new List<StoryboardDrawItem>();
        sampler.Sample(timeline.ToSeconds(new(8, 1, 8)), items);
        check(items.Count == 1 && items[0].Id == "speed" && Nearly(items[0].Transform.M31, 50),
            "UVSB bar progress: BPM boundary is 50% in chart time; clip interval is half-open");
        sampler.Sample(timeline.ToSeconds(new(8, 3, 16)), items);
        check(items.Count == 1 && items[0].Id == "speed" && Nearly(items[0].Transform.M31, 75),
            "UVSB bar progress: repeat gap does not draw");
        sampler.Sample(timeline.ToSeconds(new(8, 1, 4)), items);
        check(items.Count == 1 && items[0].Id == "pulse" && items[0].Opacity == 0,
            "UVSB repeats: second loop starts at local zero, track uses the same absolute chart axis");
        sampler.Sample(timeline.ToSeconds(new(8, 7, 8)), items);
        check(items.Count == 0, "UVSB repeats: no draw at the fourth iteration's end");
        sampler.Sample(timeline.ToSeconds(new(9, 1, 8)), items);
        check(items.Count == 1 && items[0].Id == "reset" && Nearly(items[0].Transform.M31, -5),
            "UVSB repeats: consecutive boundary restarts without a gap or doubled draw");
        foreach (var anchor in new ExactBarTime[] { new(8, 1, 32), new(8, 9, 32) })
        {
            var start = timeline.ToExactSeconds(anchor);
            var frameBoundary = (start + ExactBarTime.FromFraction(80, 1000)).ToDouble();
            sampler.Sample(Math.BitDecrement(frameBoundary), items);
            var before = items.Single(item => item.Id == "sequence");
            sampler.Sample(frameBoundary, items);
            var after = items.Single(item => item.Id == "sequence");
            check(before.Image?.Crop?.X == 0 && after.Image?.Crop?.X == 20 && after.Image?.AssetId == "sheet" &&
                  after.Width == 20 && after.Height == 10 && after.Sampling == StoryboardSampling.Nearest &&
                  Nearly(after.Transform.M31, -20), "UVSB bar sequence: fixed 80ms boundary in each iteration; crop-relative pivot");
        }
        check(StoryboardSampler.Compile(document, textures).Diagnostic?.Code == "UVSB052", "UVSB bar: reject missing strict timeline");
    }

    private static void MediaSequences(Action<bool, string> check)
    {
        var document = Parse("""
            storyboard 0
            asset "first" file="a.png"; asset "second" file="b.png"; asset "third" file="c.png"
            group "group" clock="media-ms" {
                active from=16040 until=16500
                sprite "hold" {
                    active from=16000 until=16500
                    sequence mode="once-hold" { frame "first" duration-ms=80; frame "second" duration-ms=80; frame "third" duration-ms=80 }
                }
                sprite "loop" {
                    active from=16000 until=16500
                    sequence mode="loop" { frame "first" duration-ms=80; frame "second" duration-ms=80; frame "third" duration-ms=80 }
                    track "visible" { key at=16080 value=#false; key at=16100 value=#true }
                }
            }
            clip "decimals" clock="media-ms" at=100 duration=400 {
                sprite "offset" {
                    active from=50 until=400
                    sequence mode="loop" { frame "first" duration-ms=150; frame "second" duration-ms=100 }
                }
            }
            """);
        var textures = new Dictionary<string, StoryboardTextureSize>
            { ["first"] = new(20, 10), ["second"] = new(20, 10), ["third"] = new(20, 10) };
        var sampler = Compile(document, textures);
        var items = new List<StoryboardDrawItem>();
        sampler.Sample(16.039, items);
        check(items.Count == 0, "UVSB sequence: ancestors may clip earlier active time");
        sampler.Sample(16.079, items);
        check(items.Count == 2 && items.All(item => item.Image?.AssetId == "first"), "UVSB sequence: frame zero at 79ms");
        sampler.Sample(16.08, items);
        check(items.Count == 1 && items[0].Image?.AssetId == "second", "UVSB sequence: frame one at 80ms; boolean track hides loop");
        sampler.Sample(16.1, items);
        check(items.Count == 2 && items.All(item => item.Image?.AssetId == "second"), "UVSB sequence: visibility changes never restart frame clock");
        sampler.Sample(Math.BitDecrement(16.24), items);
        check(items.Count == 2 && items.All(item => item.Image?.AssetId == "third"), "UVSB sequence: last frame before loop wrap");
        sampler.Sample(16.24, items);
        check(items.Count == 2 && items[0].Image?.AssetId == "third" && items[1].Image?.AssetId == "first",
            "UVSB sequence: hold last frame or wrap to frame zero exactly at 240ms");
        sampler.Sample(.3, items);
        check(items.Single().Image?.AssetId == "second", "UVSB sequence: media clip preserves integer absolute frame boundaries");
        sampler.Sample(.4, items);
        check(items.Single().Image?.AssetId == "first", "UVSB sequence: wrapping uses integer absolute boundaries");
    }

    private static void Rejections(Action<bool, string> check)
    {
        var duplicate = StoryboardDocument.Parse("storyboard 0\r\nsprite \"S\" asset=\"title\" clock=\"media-ms\" {\r\n" +
            "    active from=0 \"from\"=1 until=2\r\n}\r\n", "duplicate.uvsb");
        check(duplicate.Value is null && duplicate.Diagnostic is { Code: "UVSB003", SourceName: "duplicate.uvsb", Line: 3, Column: 19, ObjectId: "S" },
            "UVSB diagnostic: duplicate parameter (bare/quoted), source, CRLF line/column and object ID");
        foreach (var (label, script) in new (string, string)[]
        {
            ("header missing", "asset \"foo\" file=\"a.png\""), ("version", "storyboard 1"),
            ("second header", "storyboard 0; storyboard 0"), ("node separator", "storyboard 0 asset \"x\" file=\"a.png\""),
            ("missing entry separator", "storyboard 0; backdrop mode=\"replace\"color=\"#111111\""),
            ("macro", "storyboard 0; include \"other.uvsb\""), ("type annotation", "(type)storyboard 0"),
            ("slashdash", "storyboard 0; /- asset \"x\" file=\"a.png\""),
            ("block comment", "/* no */ storyboard 0"), ("unicode escape", "storyboard 0; asset \"\\u{D800}\" file=\"a.png\""),
            ("malformed UTF-16", "storyboard 0; //\uD800"), ("embedded NUL", "storyboard 0\0"),
            ("multiline quote", "storyboard 0; asset \"x\n\ny\" file=\"a.png\""),
            ("backdrop twice", "storyboard 0; backdrop mode=\"keep\"; backdrop mode=\"replace\""),
            ("backdrop alpha", "storyboard 0; backdrop mode=\"replace\" color=\"#10131AFF\""),
            ("keep color", "storyboard 0; backdrop mode=\"keep\" color=\"#000000\""),
            ("root path", "storyboard 0; asset \"x\" file=\"/a.png\""),
            ("drive path", "storyboard 0; asset \"x\" file=\"C:/a.png\""),
            ("resource path", "storyboard 0; asset \"x\" file=\"res://a.png\""),
            ("parent path", "storyboard 0; asset \"x\" file=\"a/../b.png\""),
            ("Windows path aliases", "storyboard 0; asset \"x\" file=\"safe/nul.png\""),
            ("case collisions", "storyboard 0; asset \"x\" file=\"Assets/a.png\"; asset \"y\" file=\"assets/b.png\""),
            ("unknown parameter", "storyboard 0; asset \"x\" file=\"a.png\" flavor=\"orange\""),
            ("unknown node", "storyboard 0; particle \"x\" clock=\"media-ms\""),
            ("empty asset block", "storyboard 0; asset \"x\" file=\"a.png\" {}"),
            ("region chain", "storyboard 0; asset \"x\" file=\"a.png\"; region \"r\" asset=\"x\" x=0 y=0 width=1 height=1; " +
                "region \"s\" asset=\"r\" x=0 y=0 width=1 height=1"),
            ("shared image namespace", "storyboard 0; asset \"x\" file=\"a.png\"; region \"x\" asset=\"x\" x=0 y=0 width=1 height=1"),
            ("unresolved frame", "storyboard 0; sprite \"x\" clock=\"media-ms\" { active from=0 until=1; " +
                "sequence mode=\"loop\" { frame \"missing\" duration-ms=1 } }"),
            ("unresolved asset", "storyboard 0; sprite \"x\" asset=\"missing\" clock=\"media-ms\" { active from=0 until=1 }"),
            ("no active", Shape("")), ("duplicate objects", Shape("active from=0 until=1") +
                "; shape \"r\" kind=\"rect\" width=1 height=1 clock=\"media-ms\" { active from=0 until=1 }"),
            ("duplicate base", Shape("active from=0 until=10; base \"opacity\" 0; base \"opacity\" 0")),
            ("equal active endpoints", Shape("active from=1 until=1")),
            ("fractional milliseconds", Shape("active from=0.5 until=1")),
            ("non-reduced bars", Shape("active from=\"0+2/4\" until=\"1\"", "bar")),
            ("unquoted BarTime", Shape("active from=0 until=1", "bar")),
            ("missing clock", "storyboard 0; group \"a\" {}"),
            ("child clock", "storyboard 0; group \"a\" clock=\"bar\" { group \"b\" clock=\"bar\" }"),
            ("exponent", Shape("active from=0 until=10; base \"rotation\" 1e2")),
            ("special number", Shape("active from=0 until=10; base \"rotation\" #inf")),
            ("negative scale", Shape("active from=0 until=10; base \"scale.x\" -1")),
            ("misplaced sampling", Shape("active from=0 until=10; base \"sampling\" \"nearest\"")),
            ("group opacity", "storyboard 0; group \"a\" clock=\"bar\" { base \"opacity\" 0.5 }"),
            ("duplicate key", Shape("active from=0 until=10; track \"opacity\" { key at=0 value=0; key at=0 value=1 }")),
            ("out-of-order key", Shape("active from=0 until=10; track \"opacity\" { key at=5 value=0; key at=3 value=1 }")),
            ("duplicate track", Shape("active from=0 until=10; track \"opacity\" { key at=0 value=0 }; track \"opacity\" { key at=1 value=1 }")),
            ("last ease", Shape("active from=0 until=10; track \"opacity\" { key at=0 value=0 ease=\"step\" }")),
            ("non-step bool", Shape("active from=0 until=10; track \"visible\" { key at=0 value=#true ease=\"linear\"; key at=5 value=#false }")),
            ("own active key range", Shape("active from=1 until=10; track \"opacity\" { key at=0 value=0 }")),
            ("path and X track writer", Shape("active from=0 until=10; path { key at=0 x=0 y=0 }; track \"position.x\" { key at=0 value=0 }")),
            ("partial controls", Shape("active from=0 until=10; path { key at=0 x=0 y=0 c1-x=0; key at=2 x=0 y=0 }")),
            ("final controls", Shape("active from=0 until=10; path { key at=0 x=0 y=0 c1-x=0 c1-y=0 c2-x=0 c2-y=0 }")),
            ("overlapping repeat", "storyboard 0; clip \"c\" clock=\"media-ms\" at=0 duration=10 { repeat count=2 period=9 }"),
            ("zero repeat", "storyboard 0; clip \"c\" clock=\"media-ms\" at=0 duration=10 { repeat count=0 period=10 }"),
            ("nested clip", "storyboard 0; clip \"c\" clock=\"media-ms\" at=0 duration=10 { group \"g\" { clip \"c2\" at=0 duration=10 {} } }"),
            ("clip local key range", "storyboard 0; clip \"c\" clock=\"media-ms\" at=0 duration=10 { group \"g\" { track \"position.x\" { key at=11 value=0 } } }"),
            ("clip child active range", "storyboard 0; clip \"c\" clock=\"media-ms\" at=0 duration=10 { group \"g\" { active from=0 until=11 } }"),
            ("gradient missing start", Shape("active from=0 until=10; linear-gradient from-x=0 from-y=0 to-x=1 to-y=0 " +
                "{ stop at=0.1 color=\"#000000\"; stop at=1 color=\"#FFFFFF\" }")),
            ("degenerate gradient", Shape("active from=0 until=10; linear-gradient from-x=0 from-y=0 to-x=0 to-y=0 {}")),
            ("source budget", new string(' ', StoryboardLimits.MaxSourceBytes + 1)),
            ("depth budget", "storyboard 0; " + string.Concat(Enumerable.Range(0, 40).Select(i => $"group \"a{i}\" {{ ")) + new string('}', 40))
        })
        {
            var result = StoryboardDocument.Parse(script, "invalid.uvsb");
            check(result.Value is null && result.Diagnostic is { SourceName: "invalid.uvsb", Line: >= 1, Column: >= 1 },
                $"UVSB reject: {label}");
        }

        var region = Parse("storyboard 0; asset \"Sheet\" file=\"a.png\"; region \"r\" asset=\"Sheet\" x=2 y=0 width=1 height=1");
        check(StoryboardSampler.Compile(region, NoTextures).Diagnostic is { Code: "UVSB050", ObjectId: "Sheet" }, "UVSB bind reject: missing metadata");
        check(StoryboardSampler.Compile(region, new Dictionary<string, StoryboardTextureSize>(StringComparer.OrdinalIgnoreCase)
        {
            ["sheet"] = new(40, 40)
        }).Diagnostic?.Code == "UVSB050", "UVSB bind reject: metadata identifiers remain ordinal");
        check(StoryboardSampler.Compile(region, new Dictionary<string, StoryboardTextureSize> { ["Sheet"] = new(2, 1) })
            .Diagnostic is { Code: "UVSB051", ObjectId: "r" }, "UVSB bind reject: out-of-bounds crop");
        check(StoryboardSampler.Compile(region,
            new Dictionary<string, StoryboardTextureSize> { ["Sheet"] = new(StoryboardLimits.MaxTextureDimension + 1, 1) })
            .Diagnostic?.Code == "UVSB050", "UVSB bind reject: oversized texture");
        var unequal = Parse("storyboard 0; asset \"a\" file=\"a.png\"; asset \"b\" file=\"b.png\"; " +
            "sprite \"s\" clock=\"media-ms\" { active from=0 until=1; sequence mode=\"loop\" { " +
            "frame \"a\" duration-ms=1; frame \"b\" duration-ms=1 } }");
        check(StoryboardSampler.Compile(unequal, new Dictionary<string, StoryboardTextureSize> { ["a"] = new(1, 1), ["b"] = new(2, 1) })
            .Diagnostic is { Code: "UVSB053", ObjectId: "s" }, "UVSB bind reject: unequal visible frame sizes");
        check(StoryboardSampler.Compile(unequal, new Dictionary<string, StoryboardTextureSize> { ["a"] = new(4096, 4096), ["b"] = new(1, 1) })
            .Diagnostic?.Code == "UVSB053", "UVSB bind reject: a large aggregate does not bypass visible frame size checks");
        var aliases = Parse("storyboard 0; asset \"a\" file=\"a.png\"; asset \"b\" file=\"a.png\"");
        check(StoryboardSampler.Compile(aliases, new Dictionary<string, StoryboardTextureSize> { ["a"] = new(2, 2), ["b"] = new(3, 2) })
            .Diagnostic is { Code: "UVSB050", ObjectId: "b" }, "UVSB bind reject: same physical path with conflicting metadata");
        var precision = Parse(Shape("active from=\"999999\" until=\"999999+1/9007199254740991\"", "bar"));
        check(StoryboardSampler.Compile(precision, NoTextures, new V2BpmTimeline([new(ExactBarTime.Zero, 120)], 0))
            .Diagnostic?.Code == "UVSB052", "UVSB bind reject: exact endpoints indistinguishable at runtime");
        var clipPrecision = Parse("""
            storyboard 0
            clip "c" clock="bar" at="999999" duration="1" {
                shape "s" kind="rect" width=1 height=1 { active from="0+1/9007199254740991" until="0+1/2" }
            }
            """);
        check(StoryboardSampler.Compile(clipPrecision, NoTextures, new V2BpmTimeline([new(ExactBarTime.Zero, 120)], 0))
            .Diagnostic?.Code == "UVSB052", "UVSB bind reject: local child time collapses onto clip edge");
        var giant = Parse("""
            storyboard 0
            group "scaled" clock="media-ms" {
                base "scale.x" 1000000
                shape "s" kind="rect" width=1000000 height=1000000 {
                    active from=0 until=1000
                    base "scale.x" 1000000
                }
            }
            """);
        check(StoryboardSampler.Compile(giant, NoTextures).Diagnostic is { Code: "UVSB054", ObjectId: "s" },
            "UVSB bind reject: accumulated world geometry exceeds runtime envelope");
        check(StoryboardSampler.Compile(Parse(Shape("active from=0 until=1; base \"scale.x\" 1920")), NoTextures).Value is not null,
            "UVSB bind: 1px shape can be stretched across the logical screen");
        var manyRepeats = Parse("storyboard 0; clip \"c\" clock=\"media-ms\" at=0 duration=100 { repeat count=4096 period=100; " +
            "group \"g\" { track \"position.x\" { " + string.Concat(Enumerable.Range(0, 64).Select(i => $"key at={i} value={i}; ")) + "} } }");
        check(StoryboardSampler.Compile(manyRepeats, NoTextures).Diagnostic?.Code == "UVSB002", "UVSB bind reject: expanded moment budget");
    }

    private static string Shape(string body, string clock = "media-ms") =>
        $"storyboard 0; shape \"r\" kind=\"rect\" width=1 height=1 clock=\"{clock}\" {{ {body} }}";

    internal static StoryboardDocument Parse(string source)
    {
        var result = StoryboardDocument.Parse(source, "synthetic.uvsb");
        return result.Value ?? throw new InvalidOperationException(result.Diagnostic?.ToString());
    }

    internal static StoryboardSampler Compile(StoryboardDocument document,
        IReadOnlyDictionary<string, StoryboardTextureSize>? textures = null, V2BpmTimeline? timeline = null)
    {
        var result = StoryboardSampler.Compile(document, textures ?? NoTextures, timeline);
        return result.Value ?? throw new InvalidOperationException(result.Diagnostic?.ToString());
    }

    private static bool Nearly(float actual, float expected) => MathF.Abs(actual - expected) < 0.0001f;
    private static bool Nearly(Vector2 actual, Vector2 expected) => Vector2.Distance(actual, expected) < 0.0001f;
}
