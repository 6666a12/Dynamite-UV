using System.Text;
using System.Text.Json.Nodes;
using DynamiteUniverse.Shared.Chart.V2;
using static CoreTests.CoreTestSupport;

namespace CoreTests;

/// <summary>Exercise the public loaders with real, frozen v2.0 source bytes, not just an in-memory model.</summary>
internal static class CustomCurveFormatTests
{
    internal static void Run()
    {
        const string bounce = "1 - (1-t)^1.5 * cos(3*pi*t)";
        var schemas = Path.Combine(FindRepositoryRoot(), "schemas", "chart-format-v2");
        var source = Path.Combine(schemas, "examples", "golden-pack");
        var oldMeta = File.ReadAllText(Path.Combine(source, "meta.json"));
        var oldJson = File.ReadAllText(Path.Combine(source, "chart_hard.json"));
        var audio = File.ReadAllBytes(Path.Combine(source, "audio.wav"));
        var golden = JsonNode.Parse(File.ReadAllText(Path.Combine(schemas, "vectors", "gameplay-digest-v1.json")))!;
        var old = V2Integration.LoadStrict(oldMeta, oldJson, "hard", audio);
        Check(old.GameplayDigest == (string?)golden["expectedSha256"], "2.0 original source and audio reproduce golden digest");
        var pack = JsonNode.Parse(oldMeta)!.AsObject(); pack["formatMinor"] = 1;
        var oldInside = V2Integration.LoadStrict(pack.ToJsonString(), oldJson, "hard", audio);
        Check(oldInside.RulesetId == old.RulesetId && oldInside.GameplayDigest == old.GameplayDigest,
            "2.0 chart retains its score identity inside a 2.1 pack");
        var chart = JsonNode.Parse(oldJson)!.AsObject(); chart["formatMinor"] = 1;
        var scroll = chart["scrollSpeeds"]![0]!; var mixer = chart["notesLeft"]![0]!;
        var holdNode = chart["notesCenter"]![1]!["nodes"]![0]!;
        foreach (var point in new[] { scroll, mixer, holdNode }) { point["curveToNext"] = "custom"; point["customExpr"] = bounce; }
        V2LoadedPackageChart Load(JsonNode data) => V2Integration.LoadStrict(pack.ToJsonString(), data.ToJsonString(), "hard", audio);
        var loaded = Load(chart);
        Check(loaded.RulesetId == "dynamite-uv-ruleset-2.1" && loaded.GameplayDigest != old.GameplayDigest,
            "2.1 score identity is distinct");
        var persisted = JsonNode.Parse(V2JsonEncoder.EncodeChart(loaded.SemanticChart))!;
        Check((string?)persisted["notesCenter"]![1]!["nodes"]![0]!["customExpr"] == bounce &&
            Load(persisted).GameplayDigest == loaded.GameplayDigest, "formula source survives codec and reload without a changed digest");
        scroll["customExpr"] = bounce.Replace(" ", "");
        Check(Load(chart).GameplayDigest == loaded.GameplayDigest, "expression whitespace does not split score identity");
        scroll["customExpr"] = "t^1.5";
        Check(Load(chart).GameplayDigest != loaded.GameplayDigest, "editing only a Scroll expression changes identity");
        scroll["customExpr"] = bounce;
        foreach (var point in new[] { mixer, holdNode })
        {
            point["customExpr"] = "t";
            Check(Load(chart).GameplayDigest != loaded.GameplayDigest, "changing just a path expression changes identity");
            point["customExpr"] = bounce;
        }
        void Reject(string name, Action mutate, Action restore)
        {
            mutate();
            try { _ = Load(chart); Check(false, name); }
            catch (V2DiagnosticException e) { Check(e.JsonPointer.StartsWith('/') && !string.IsNullOrEmpty(e.SourceName), name); }
            finally { restore(); }
        }
        Reject("old pack cannot smuggle new chart", () => pack.Remove("formatMinor"), () => pack["formatMinor"] = 1);
        Reject("2.0 chart does not silently admit custom", () => chart.Remove("formatMinor"), () => chart["formatMinor"] = 1);
        Reject("unknown minor rejected", () => chart["formatMinor"] = 2, () => chart["formatMinor"] = 1);
        Reject("explicit minor zero rejected", () => chart["formatMinor"] = 0, () => chart["formatMinor"] = 1);
        Reject("custom requires expression", () => scroll.AsObject().Remove("customExpr"), () => scroll["customExpr"] = bounce);
        Reject("built-in rejects expression", () => scroll["curveToNext"] = "linear", () => scroll["curveToNext"] = "custom");
        Reject("tail cannot have custom outgoing curve", () => { var tail = mixer["nodes"]![1]!; tail["curveToNext"] = "custom"; tail["customExpr"] = "t"; },
            () => { var tail = mixer["nodes"]![1]!.AsObject(); tail.Remove("curveToNext"); tail.Remove("customExpr"); });
        Reject("basic note cannot store customExpr", () => chart["notesCenter"]![0]!["customExpr"] = "t",
            () => chart["notesCenter"]![0]!.AsObject().Remove("customExpr"));
        Reject("speed cannot go negative in the middle even when endpoints are legal", () => chart["scrollSpeeds"]![1]!["value"] = 0.00001,
            () => chart["scrollSpeeds"]![1]!["value"] = 0.75);
        Reject("speed cannot exceed 64 in the middle", () => chart["scrollSpeeds"]![1]!["value"] = 64,
            () => chart["scrollSpeeds"]![1]!["value"] = 0.75);
        Reject("a positive width at both endpoints cannot hide an internal nonpositive width", () => mixer["nodes"]![0]!["width"] = 0.001,
            () => mixer["nodes"]![0]!["width"] = 0.6);
        Reject("invalid intermediate domain rejected on load", () => mixer["customExpr"] = "t+0/(t-0.314159)", () => mixer["customExpr"] = bounce);
        Check(!Encoding.UTF8.GetString(V2JsonEncoder.EncodeChart(old.SemanticChart)).Contains("formatMinor"), "old chart writes no new fields");
        Console.WriteLine("[2.1] format, original-source golden digest, mixed pack identities, strict field and geometry checks");
    }
}
