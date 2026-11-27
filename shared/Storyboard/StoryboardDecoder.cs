using System.Globalization;
using System.Numerics;
using DynamiteUniverse.Shared.Chart.V2;

namespace DynamiteUniverse.Shared.Storyboard;

/// <summary>Validates storyboard semantics without filesystem or renderer dependencies.</summary>
internal sealed class StoryboardDecoder
{
    private const int MaxMediaMilliseconds = 86_400_000;
    private const float MaxMeasure = 1_000_000;
    private const float MaxScale = 1_000_000;
    private readonly Dictionary<string, bool> _images = new(StringComparer.Ordinal);
    private readonly HashSet<string> _objects = new(StringComparer.Ordinal);
    // Index a trie by (parent ID, segment) rather than keeping a string for every full prefix.
    private readonly Dictionary<PathSegment, (string Spelling, int Id)> _pathSpellings = new(new PathSegmentComparer());
    private int _assetCount;
    private int _repeatCount;

    internal static StoryboardDocument Decode(List<KdlNode> nodes, string sourceName) =>
        new StoryboardDecoder().DecodeDocument(nodes, sourceName);

    private StoryboardDocument DecodeDocument(List<KdlNode> nodes, string sourceName)
    {
        if (nodes.Count == 0 || nodes[0].Name != "storyboard")
            throw new StoryboardError("UVSB010", nodes.Count == 0 ? new(1, 1) : nodes[0].Point,
                null, "First node must be 'storyboard 0'.");
        var header = new NodeInput(nodes[0], null);
        header.Arity(1);
        if (header.Argument(0) is not { Kind: KdlKind.Integer, Text: "0" })
            throw header.Error(header.Argument(0).Point, "Only storyboard version 0 is supported.", "UVSB010");
        header.Leaf();

        var assets = new List<AssetDeclaration>();
        var regions = new List<RegionDeclaration>();
        var roots = new List<SceneNode>();
        StoryboardBackdrop? backdrop = null;
        for (var i = 1; i < nodes.Count; i++)
        {
            var node = nodes[i];
            switch (node.Name)
            {
                case "asset": assets.Add(ReadAsset(node)); break;
                case "region": regions.Add(ReadRegion(node)); break;
                case "backdrop":
                    if (backdrop.HasValue)
                        throw new NodeInput(node, null).Error(node.Point, "Duplicate backdrop declaration.");
                    backdrop = ReadBackdrop(node);
                    break;
                default: roots.Add(ReadScene(node, null, null, null)); break;
            }
        }
        foreach (var region in regions)
            if (!_images.TryGetValue(region.AssetId, out var isAsset) || !isAsset)
                throw new StoryboardError("UVSB030", region.Point, region.Id,
                    $"Region asset '{region.AssetId}' must refer to a declared whole image.");
        foreach (var root in roots)
            ValidateReferences(root);
        return new StoryboardDocument(sourceName, assets.ToArray(), regions.ToArray(), Sort(roots),
            backdrop ?? new(StoryboardBackdropMode.Keep, new(0x10, 0x13, 0x1a)));
    }

    private static StoryboardBackdrop ReadBackdrop(KdlNode node)
    {
        var input = new NodeInput(node, null);
        input.Arity(0);
        var mode = input.Require("mode");
        var result = Text(input, mode) switch
        {
            "keep" => new StoryboardBackdrop(StoryboardBackdropMode.Keep, new(0x10, 0x13, 0x1a)),
            "replace" => new StoryboardBackdrop(StoryboardBackdropMode.Replace,
                input.Optional("color") is { } color ? Color(input, color, false) : new(0x10, 0x13, 0x1a)),
            _ => throw input.Error(mode.Point, "Backdrop mode must be 'keep' or 'replace'.")
        };
        input.Leaf();
        return result;
    }

    private AssetDeclaration ReadAsset(KdlNode node)
    {
        var input = Identified(node, null);
        var id = input.ObjectId!;
        if (++_assetCount > StoryboardLimits.MaxAssetCount)
            throw input.Error(node.Point, "Asset count limit exceeded.", "UVSB002");
        AddImage(input, id, isAsset: true);
        var path = input.Require("file");
        var text = Text(input, path);
        ValidatePath(input, path.Point, text);
        input.Leaf();
        return new(id, text, node.Point);
    }

    private RegionDeclaration ReadRegion(KdlNode node)
    {
        var input = Identified(node, null);
        var id = input.ObjectId!;
        AddImage(input, id, isAsset: false);
        var asset = input.Require("asset");
        var assetId = Id(input, asset);
        var crop = new StoryboardCrop(Integer(input, input.Require("x"), 0, int.MaxValue),
            Integer(input, input.Require("y"), 0, int.MaxValue),
            Integer(input, input.Require("width"), 1, int.MaxValue),
            Integer(input, input.Require("height"), 1, int.MaxValue));
        input.Leaf();
        return new(id, assetId, crop, node.Point);
    }

    private void AddImage(NodeInput input, string id, bool isAsset)
    {
        if (!_images.TryAdd(id, isAsset))
            throw input.Error(input.Argument(0).Point, $"Duplicate image ID '{id}'.", "UVSB030");
    }

    private void ValidatePath(NodeInput input, SourcePoint point, string path)
    {
        if (path.Length == 0 || path.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format) ||
            path.IndexOfAny(['\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            throw input.Error(point, "Asset file must be a portable, relative path with '/' separators.", "UVSB031");
        var parent = 0;
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part.Length > 255 || part is "." or ".." ||
                part[0] == ' ' || part[^1] is '.' or ' ' || IsReservedDevice(part))
                throw input.Error(point, "Asset path has an empty, reserved or escaping segment.", "UVSB031");
            var segment = new PathSegment(parent, part);
            if (_pathSpellings.TryGetValue(segment, out var existing))
            {
                if (!StringComparer.Ordinal.Equals(existing.Spelling, part))
                    throw input.Error(point, $"Asset path segment '{part}' has a case collision with '{existing.Spelling}'.", "UVSB031");
                parent = existing.Id;
            }
            else
            {
                parent = _pathSpellings.Count + 1;
                _pathSpellings.Add(segment, (part, parent));
            }
        }
    }

    private readonly record struct PathSegment(int Parent, string Name);

    private sealed class PathSegmentComparer : IEqualityComparer<PathSegment>
    {
        public bool Equals(PathSegment x, PathSegment y) =>
            x.Parent == y.Parent && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode(PathSegment segment) =>
            HashCode.Combine(segment.Parent, StringComparer.OrdinalIgnoreCase.GetHashCode(segment.Name));
    }

    private static bool IsReservedDevice(string part)
    {
        var dot = part.IndexOf('.');
        var name = (dot < 0 ? part : part[..dot]).TrimEnd(' ').ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
            name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) ||
                name.StartsWith("LPT", StringComparison.Ordinal)) &&
            name[3] is >= '0' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3';
    }

    private SceneNode ReadScene(KdlNode node, Clock? inheritedClock, ExactBarTime? clipDuration, string? parentId)
    {
        var input = new NodeInput(node, parentId);
        var kind = node.Name switch
        {
            "clip" => NodeKind.Clip,
            "group" => NodeKind.Group,
            "sprite" => NodeKind.Sprite,
            "shape" => NodeKind.Rect,
            _ => throw input.Error(node.Point, $"Unknown scene node '{node.Name}'.", "UVSB011")
        };
        input = Identified(node, parentId);
        var id = input.ObjectId!;
        if (!_objects.Add(id))
            throw input.Error(input.Argument(0).Point, $"Duplicate object ID '{id}'.");
        if (kind == NodeKind.Rect)
        {
            var kindValue = input.Require("kind");
            kind = Text(input, kindValue) switch
            {
                "rect" => NodeKind.Rect,
                "ellipse" => NodeKind.Ellipse,
                _ => throw input.Error(kindValue.Point, "Shape kind must be 'rect' or 'ellipse'.")
            };
        }
        var clock = inheritedClock ?? ReadClock(input);
        var order = input.Optional("order") is { } layer ? Integer(input, layer, int.MinValue, int.MaxValue) : 0;
        var scene = new SceneNode(id, node.Point, kind, clock, order);
        if (kind == NodeKind.Clip)
        {
            if (clipDuration.HasValue) throw input.Error(node.Point, "Clips cannot be nested.");
            ReadClip(input, scene);
            return scene;
        }
        if (kind == NodeKind.Sprite && input.Optional("asset") is { } asset)
            scene.ImageId = Id(input, asset);
        if (kind is NodeKind.Rect or NodeKind.Ellipse)
            scene.Size = new(PositiveMeasure(input, "width"), PositiveMeasure(input, "height"));
        var block = input.Block(required: kind != NodeKind.Group);
        var children = new List<SceneNode>();
        var bases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in block)
        {
            var field = new NodeInput(child, id);
            switch (child.Name)
            {
                case "active" when scene.Active is null: scene.Active = ReadActive(field, clock, clipDuration); break;
                case "base": ReadBase(field, scene, bases); break;
                case "track": ReadTrack(field, scene); break;
                case "path" when scene.Path is null: scene.Path = ReadPath(field, clock); break;
                case "sequence" when kind == NodeKind.Sprite && scene.Sequence is null:
                    scene.Sequence = ReadSequence(field); break;
                case "linear-gradient" when kind is NodeKind.Rect or NodeKind.Ellipse && scene.Gradient is null:
                    scene.Gradient = ReadGradient(field); break;
                case "clip-rect" when kind == NodeKind.Group && scene.ClipRect is null:
                    scene.ClipRect = ReadLocalRect(field); break;
                case "sprite" or "shape" or "group" or "clip" when kind == NodeKind.Group:
                    children.Add(ReadScene(child, clock, clipDuration, id)); break;
                default: throw field.Error(child.Point, $"Unknown, repeated or unsupported '{child.Name}' in '{node.Name}'.");
            }
        }
        if (kind != NodeKind.Group && scene.Active is null)
            throw input.Error(node.Point, "Drawable requires one explicit active interval.");
        if (kind == NodeKind.Sprite && (scene.ImageId is null) == (scene.Sequence is null))
            throw input.Error(node.Point, "Sprite requires exactly one of 'asset' and 'sequence'.");
        if (scene.Path is not null && (scene.Tracks[(int)Property.X] is not null || scene.Tracks[(int)Property.Y] is not null))
            throw input.Error(node.Point, "Path cannot be combined with position.x or position.y tracks.");
        foreach (var track in scene.Tracks)
            if (track is not null)
                foreach (var key in track) CheckKeyRange(input, key.At, key.Point, scene.Active, clipDuration);
        if (scene.Path is not null)
            foreach (var key in scene.Path) CheckKeyRange(input, key.At, key.Point, scene.Active, clipDuration);
        scene.Children = Sort(children);
        return scene;
    }

    private void ReadClip(NodeInput input, SceneNode scene)
    {
        var atValue = input.Require("at");
        var at = Time(input, atValue, scene.Clock);
        var durationValue = input.Require("duration");
        var duration = Time(input, durationValue, scene.Clock);
        if (duration.IsZero) throw input.Error(durationValue.Point, "Clip duration must be positive.");
        var block = input.Block();
        var period = duration;
        var count = 1;
        var seenRepeat = false;
        var children = new List<SceneNode>();
        foreach (var child in block)
        {
            if (child.Name != "repeat")
            {
                children.Add(ReadScene(child, scene.Clock, duration, scene.Id));
                continue;
            }
            var field = new NodeInput(child, scene.Id);
            if (seenRepeat) throw field.Error(child.Point, "Duplicate clip repeat declaration.");
            seenRepeat = true;
            field.Arity(0);
            count = Integer(field, field.Require("count"), 1, StoryboardLimits.MaxRepeatCount);
            var periodValue = field.Require("period");
            period = Time(field, periodValue, scene.Clock);
            if (period < duration) throw field.Error(periodValue.Point, "Repeat period must be at least clip duration.");
            field.Leaf();
        }
        if ((_repeatCount += count) > StoryboardLimits.MaxRepeatCount)
            throw input.Error(atValue.Point, "Total clip repetition limit exceeded.", "UVSB002");
        if (at + period * (count - 1) + duration > TimeLimit(scene.Clock))
            throw input.Error(atValue.Point, "Clip series end exceeds the timeline limit.", "UVSB002");
        scene.Repeat = new(at, duration, count, period);
        scene.Children = Sort(children);
    }

    private static Active ReadActive(NodeInput input, Clock clock, ExactBarTime? clipDuration)
    {
        input.Arity(0);
        var from = Time(input, input.Require("from"), clock);
        var end = input.Require("until");
        var until = Time(input, end, clock);
        input.Leaf();
        if (until <= from) throw input.Error(end.Point, "Active requires from < until.");
        if (clipDuration is { } bound && until > bound)
            throw input.Error(end.Point, "Active interval extends beyond the enclosing clip.");
        return new(from, until);
    }

    private static void ReadBase(NodeInput input, SceneNode scene, HashSet<string> seen)
    {
        var arg = input.Argument(0);
        var name = Text(input, arg);
        if (!seen.Add(name)) throw input.Error(arg.Point, $"Duplicate base '{name}'.");
        input.Arity(name == "pivot" ? 3 : 2);
        input.Leaf();
        var value = input.Argument(1);
        switch (name)
        {
            case "pivot" when scene.Kind != NodeKind.Group:
                scene.Pivot = new(Unit(input, value), Unit(input, input.Argument(2))); break;
            case "blend" when scene.Kind != NodeKind.Group:
                scene.Blend = Text(input, value) switch
                {
                    "normal" => StoryboardBlend.Normal,
                    "add" => StoryboardBlend.Add,
                    _ => throw input.Error(value.Point, "Blend must be 'normal' or 'add'.")
                }; break;
            case "sampling" when scene.Kind == NodeKind.Sprite:
                scene.Sampling = Text(input, value) switch
                {
                    "linear" => StoryboardSampling.Linear,
                    "nearest" => StoryboardSampling.Nearest,
                    _ => throw input.Error(value.Point, "Sampling must be 'linear' or 'nearest'.")
                }; break;
            default:
                var property = ReadProperty(input, arg, name, scene.Kind);
                scene.Base[(int)property] = PropertyValue(input, value, property);
                break;
        }
    }

    private static void ReadTrack(NodeInput input, SceneNode scene)
    {
        input.Arity(1);
        var arg = input.Argument(0);
        var name = Text(input, arg);
        var property = ReadProperty(input, arg, name, scene.Kind);
        if (scene.Tracks[(int)property] is not null)
            throw input.Error(arg.Point, $"Duplicate track '{name}'.", "UVSB041");
        var block = input.NonEmptyBlock();
        var keys = new Key[block.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var key = KeyInput(input, block[i]);
            var timeValue = key.Require("at");
            var time = Time(key, timeValue, scene.Clock);
            EnsureIncreasing(key, timeValue, time, i > 0 ? keys[i - 1].At : null, $"Track '{name}' key {i + 1}");
            var value = PropertyValue(key, key.Require("value"), property);
            var ease = ReadEase(key, i + 1 == keys.Length, property is Property.Visible or Property.FlipX or Property.FlipY);
            key.Leaf();
            keys[i] = new(time, value, ease, timeValue.Point);
        }
        scene.Tracks[(int)property] = keys;
    }

    private static PathKey[] ReadPath(NodeInput input, Clock clock)
    {
        input.Arity(0);
        var block = input.NonEmptyBlock();
        var keys = new PathKey[block.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var key = KeyInput(input, block[i]);
            var timeValue = key.Require("at");
            var time = Time(key, timeValue, clock);
            EnsureIncreasing(key, timeValue, time, i > 0 ? keys[i - 1].At : null, $"Path key {i + 1}");
            var point = new Vector2(Measure(key, key.Require("x")), Measure(key, key.Require("y")));
            Vector2? c1 = null;
            Vector2 c2 = default;
            var cx = key.Optional("c1-x");
            var cy = key.Optional("c1-y");
            var dx = key.Optional("c2-x");
            var dy = key.Optional("c2-y");
            if ((cx ?? cy ?? dx ?? dy) is { } control)
            {
                if (i + 1 == keys.Length || !cx.HasValue || !cy.HasValue || !dx.HasValue || !dy.HasValue)
                    throw key.Error(control.Point, "An outgoing path segment needs all four controls and a following key.");
                c1 = new(Measure(key, cx.Value), Measure(key, cy.Value));
                c2 = new(Measure(key, dx.Value), Measure(key, dy.Value));
            }
            var ease = ReadEase(key, i + 1 == keys.Length, discrete: false);
            key.Leaf();
            keys[i] = new(time, point, c1, c2, ease, timeValue.Point);
        }
        return keys;
    }

    private static NodeInput KeyInput(NodeInput parent, KdlNode node)
    {
        var key = new NodeInput(node, parent.ObjectId);
        if (node.Name != "key") throw key.Error(node.Point, "Expected a key node.");
        key.Arity(0);
        return key;
    }

    private static void EnsureIncreasing(NodeInput input, KdlValue value, ExactBarTime time,
        ExactBarTime? previous, string subject)
    {
        if (previous.HasValue && time <= previous.Value)
            throw input.Error(value.Point, $"{subject} must be strictly after the previous key.", "UVSB041");
    }

    private static void CheckKeyRange(NodeInput input, ExactBarTime time, SourcePoint point, Active? active, ExactBarTime? clipDuration)
    {
        if (active is { } interval && (time < interval.From || time > interval.Until))
            throw input.Error(point, "Key is outside its object's active interval.", "UVSB041");
        if (clipDuration is { } limit && time > limit)
            throw input.Error(point, "Key is outside the enclosing clip.", "UVSB041");
    }

    private static Ease ReadEase(NodeInput input, bool last, bool discrete)
    {
        if (input.Optional("ease") is not { } value) return discrete ? Ease.Step : Ease.Linear;
        if (last) throw input.Error(value.Point, "A final key has no outgoing easing segment.");
        var ease = Text(input, value) switch
        {
            "step" => Ease.Step, "linear" => Ease.Linear,
            "sine-in" => Ease.SineIn, "sine-out" => Ease.SineOut, "sine-in-out" => Ease.SineInOut,
            "quad-in" => Ease.QuadIn, "quad-out" => Ease.QuadOut, "quad-in-out" => Ease.QuadInOut,
            _ => throw input.Error(value.Point, "Unknown easing name.")
        };
        if (discrete && ease != Ease.Step) throw input.Error(value.Point, "Boolean tracks require step easing.");
        return ease;
    }

    private static Sequence ReadSequence(NodeInput input)
    {
        input.Arity(0);
        var mode = input.Require("mode");
        var loop = Text(input, mode) switch
        {
            "once-hold" => false,
            "loop" => true,
            _ => throw input.Error(mode.Point, "Sequence mode must be 'once-hold' or 'loop'.")
        };
        var block = input.NonEmptyBlock();
        var frames = new Frame[block.Count];
        long total = 0;
        for (var i = 0; i < frames.Length; i++)
        {
            var node = block[i];
            var member = new NodeInput(node, input.ObjectId);
            if (node.Name != "frame") throw member.Error(node.Point, "Expected a sequence frame.");
            member.Arity(1);
            var id = Id(member, member.Argument(0));
            var duration = Integer(member, member.Require("duration-ms"), 1, MaxMediaMilliseconds);
            member.Leaf();
            if ((total += duration) > MaxMediaMilliseconds)
                throw member.Error(node.Point, "Sequence duration limit exceeded.", "UVSB002");
            frames[i] = new(id, duration, node.Point);
        }
        return new(loop, frames);
    }

    private static StoryboardGradient ReadGradient(NodeInput input)
    {
        input.Arity(0);
        var from = new Vector2(Unit(input, input.Require("from-x")), Unit(input, input.Require("from-y")));
        var to = new Vector2(Unit(input, input.Require("to-x")), Unit(input, input.Require("to-y")));
        if (Vector2.DistanceSquared(from, to) < 1e-12f)
            throw input.Error(input.Point, "Gradient endpoints must be at least 0.000001 units apart.");
        var block = input.NonEmptyBlock();
        var stops = new StoryboardGradientStop[block.Count];
        for (var i = 0; i < stops.Length; i++)
        {
            var node = block[i];
            var stop = new NodeInput(node, input.ObjectId);
            if (node.Name != "stop") throw stop.Error(node.Point, "Expected a gradient stop.");
            stop.Arity(0);
            var atValue = stop.Require("at");
            var at = Unit(stop, atValue);
            if (i > 0 && ((double)at - stops[i - 1].At) * (StoryboardLimits.GradientRampWidth - 1) < 1)
                throw stop.Error(atValue.Point, $"Gradient stops must be increasing and separated by at least 1/{StoryboardLimits.GradientRampWidth - 1} of the ramp.");
            stops[i] = new(at, Color(stop, stop.Require("color"), true));
            stop.Leaf();
        }
        if (stops.Length < 2 || stops[0].At != 0 || stops[^1].At != 1)
            throw input.Error(input.Point, "Gradient requires endpoints at 0 and 1.");
        return new(from, to, stops);
    }

    private static LocalRect ReadLocalRect(NodeInput input)
    {
        input.Arity(0);
        var rect = new LocalRect(Measure(input, input.Require("x")), Measure(input, input.Require("y")),
            PositiveMeasure(input, "width"), PositiveMeasure(input, "height"));
        input.Leaf();
        return rect;
    }

    private static Property ReadProperty(NodeInput input, KdlValue arg, string name, NodeKind kind) => name switch
    {
        "position.x" => Property.X, "position.y" => Property.Y,
        "scale.x" => Property.ScaleX, "scale.y" => Property.ScaleY,
        "rotation" => Property.Rotation, "visible" => Property.Visible,
        "opacity" when kind != NodeKind.Group => Property.Opacity,
        "tint" when kind != NodeKind.Group => Property.Tint,
        "flip-x" when kind != NodeKind.Group => Property.FlipX,
        "flip-y" when kind != NodeKind.Group => Property.FlipY,
        _ => throw input.Error(arg.Point, $"Unknown or unsupported property '{name}'.")
    };

    private static Vector3 PropertyValue(NodeInput input, KdlValue value, Property property)
    {
        if (property == Property.Tint)
        {
            var color = Color(input, value, false).ToLinearRgba();
            return new(color.X, color.Y, color.Z);
        }
        var scalar = property switch
        {
            Property.Opacity => Unit(input, value),
            Property.ScaleX or Property.ScaleY => Number(input, value, 0, MaxScale),
            Property.Rotation => Number(input, value, -360_000, 360_000),
            Property.Visible or Property.FlipX or Property.FlipY => value.Kind == KdlKind.Boolean
                ? value.Text == "#true" ? 1f : 0f
                : throw input.Error(value.Point, "Expected a #true or #false boolean."),
            _ => Measure(input, value)
        };
        return new(scalar, 0, 0);
    }

    private void ValidateReferences(SceneNode scene)
    {
        if (scene.ImageId is { } image && !_images.ContainsKey(image))
            throw new StoryboardError("UVSB030", scene.Point, scene.Id, $"Unknown image '{image}'.");
        if (scene.Sequence is { } sequence)
            foreach (var frame in sequence.Frames)
                if (!_images.ContainsKey(frame.ImageId))
                    throw new StoryboardError("UVSB030", frame.Point, scene.Id, $"Unknown frame image '{frame.ImageId}'.");
        foreach (var child in scene.Children) ValidateReferences(child);
    }

    private static SceneNode[] Sort(List<SceneNode> scenes)
    {
        var result = scenes.ToArray();
        Array.Sort(result, (left, right) => left.Order != right.Order ? left.Order.CompareTo(right.Order)
            : StringComparer.Ordinal.Compare(left.Id, right.Id));
        return result;
    }

    private static Clock ReadClock(NodeInput input)
    {
        var value = input.Require("clock");
        return Text(input, value) switch
        {
            "media-ms" => Clock.Media, "bar" => Clock.Bar,
            _ => throw input.Error(value.Point, "Clock must be 'media-ms' or 'bar'.")
        };
    }

    private static ExactBarTime Time(NodeInput input, KdlValue value, Clock clock)
    {
        if (clock == Clock.Media) return new(Integer(input, value, 0, MaxMediaMilliseconds), 0, 1);
        if (value.Kind == KdlKind.String)
        {
            var text = value.Text.AsSpan();
            var plus = text.IndexOf('+');
            if (plus < 0 && CanonicalDigits(text, out var whole) && whole <= StoryboardLimits.MaxBar)
                return new(whole, 0, 1);
            if (plus > 0 && CanonicalDigits(text[..plus], out var bar) && bar < StoryboardLimits.MaxBar)
            {
                var fraction = text[(plus + 1)..];
                var slash = fraction.IndexOf('/');
                if (slash > 0 && CanonicalDigits(fraction[..slash], out var numerator) &&
                    CanonicalDigits(fraction[(slash + 1)..], out var denominator) &&
                    numerator > 0 && denominator > numerator && denominator <= ExactBarTime.MaxSafeInteger &&
                    BigInteger.GreatestCommonDivisor(numerator, denominator) == BigInteger.One)
                    return new(bar, numerator, denominator);
            }
        }
        throw input.Error(value.Point, "Expected a canonical, bounded BarTime string (for example '8' or '8+1/4').", "UVSB040");
    }

    private static bool CanonicalDigits(ReadOnlySpan<char> chars, out long value)
    {
        value = 0;
        if (chars.Length is < 1 or > 16 || chars.Length > 1 && chars[0] == '0') return false;
        foreach (var c in chars)
        {
            if (c is < '0' or > '9') return false;
            value = value * 10 + c - '0';
        }
        return true;
    }

    private static ExactBarTime TimeLimit(Clock clock) => new(clock == Clock.Bar ? StoryboardLimits.MaxBar : MaxMediaMilliseconds, 0, 1);

    private static NodeInput Identified(KdlNode node, string? parent)
    {
        var input = new NodeInput(node, parent);
        input.Arity(1);
        return new NodeInput(node, Id(input, input.Argument(0)));
    }

    private static string Id(NodeInput input, KdlValue value)
    {
        var id = Text(input, value);
        if (id.Length is < 1 or > 128 || id != id.Trim() ||
            id.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
            throw input.Error(value.Point, "ID must be nonempty, bounded and free of control characters or surrounding whitespace.");
        return id;
    }

    private static string Text(NodeInput input, KdlValue value) => value.Kind == KdlKind.String ? value.Text
        : throw input.Error(value.Point, "Expected a double-quoted string.");

    private static int Integer(NodeInput input, KdlValue value, int min, int max) =>
        value.Kind == KdlKind.Integer && int.TryParse(value.Text, NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var integer) && integer >= min && integer <= max
            ? integer : throw input.Error(value.Point, $"Expected an integer in [{min}, {max}].");

    private static float Number(NodeInput input, KdlValue value, float min, float max)
    {
        if (value.Kind is not (KdlKind.Integer or KdlKind.Decimal) ||
            !double.TryParse(value.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < min || number > max)
            throw input.Error(value.Point, $"Expected a finite decimal in [{min}, {max}].");
        return (float)number;
    }

    private static float Unit(NodeInput input, KdlValue value) => Number(input, value, 0, 1);
    private static float Measure(NodeInput input, KdlValue value) => Number(input, value, -MaxMeasure, MaxMeasure);
    private static float PositiveMeasure(NodeInput input, string name)
    {
        var value = input.Require(name);
        var number = Number(input, value, 0, MaxMeasure);
        return number > 0 ? number : throw input.Error(value.Point, $"{name} must be positive.");
    }

    private static StoryboardColor Color(NodeInput input, KdlValue value, bool allowAlpha)
    {
        var text = Text(input, value);
        if (text.Length != 7 && !(allowAlpha && text.Length == 9) || text[0] != '#')
            throw InvalidColor();
        uint rgba = 0;
        foreach (var c in text.AsSpan(1))
        {
            var nibble = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => throw InvalidColor()
            };
            rgba = rgba << 4 | (uint)nibble;
        }
        if (text.Length == 7) rgba = rgba << 8 | 255;
        return new((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

        StoryboardError InvalidColor() =>
            input.Error(value.Point, allowAlpha ? "Expected #RRGGBB or #RRGGBBAA." : "Expected opaque #RRGGBB.");
    }

    private sealed class NodeInput(KdlNode node, string? objectId)
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);
        internal string? ObjectId => objectId;
        internal SourcePoint Point => node.Point;
        internal void Arity(int count)
        {
            if (node.Arguments.Count != count)
                throw Error(node.Arguments.Count > count ? node.Arguments[count].Point : Point,
                    $"'{node.Name}' requires exactly {count} positional argument(s).");
        }
        internal KdlValue Argument(int i) => i < node.Arguments.Count ? node.Arguments[i]
            : throw Error(Point, $"Missing positional argument for '{node.Name}'.");
        internal KdlValue Require(string name) => Optional(name)
            ?? throw Error(Point, $"Missing '{name}' parameter on '{node.Name}'.");
        internal KdlValue? Optional(string name)
        {
            if (!node.Parameters.TryGetValue(name, out var parameter)) return null;
            _used.Add(name);
            return parameter.Value;
        }
        internal void Leaf()
        {
            CheckParameters();
            if (node.Children is not null) throw Error(Point, $"'{node.Name}' cannot have children.");
        }
        internal List<KdlNode> Block(bool required = true)
        {
            CheckParameters();
            return node.Children ?? (required ? throw Error(Point, $"'{node.Name}' requires a child block.") : []);
        }
        internal List<KdlNode> NonEmptyBlock()
        {
            var children = Block();
            return children.Count > 0 ? children : throw Error(Point, $"'{node.Name}' cannot be empty.");
        }
        private void CheckParameters()
        {
            if (_used.Count == node.Parameters.Count) return;
            foreach (var (name, parameter) in node.Parameters)
                if (!_used.Contains(name)) throw Error(parameter.Point, $"Unknown or unsupported '{name}' parameter on '{node.Name}'.");
        }
        internal StoryboardError Error(SourcePoint point, string message, string code = "UVSB020") =>
            new(code, point, objectId, message);
    }
}
