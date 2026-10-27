using static CoreTests.CoreTestSupport;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class ScoreIdentityTests
{

	internal static void TestScoreStoreMigrationAndIdentity()
	{
		Console.WriteLine("[16] score migration and four-part identity");
		const string legacyJson = """
		{
		  "pack:giga": {
		    "score": 700000,
		    "acc": 70,
		    "maxCombo": 10,
		    "grade": "C",
		    "perfect": 7,
		    "great": 2,
		    "good": 1,
		    "miss": 0,
		    "date": "2026-01-01 00:00:00"
		  }
		}
		""";
		var store = new ScoreStoreCore();
		store.LoadJson(legacyJson);
		var identity = new ScoreIdentity("pack", "giga",
			V2Format.RulesetId, new string('a', 64));
		Check(store.GetLegacy("pack", "giga")?.Score == 700000,
			"legacy dictionary migrates into historical records");
		Check(store.Get(identity) == null,
			"legacy record never masquerades as current gameplay identity");
		var acceptedAt = new DateTime(2026, 8, 14, 12, 34, 56);
		Check(store.TryUpdate(identity, new ScoreRecord
		{
			Score = 800000, Acc = 91.0, MaxCombo = 20,
		}, acceptedAt), "new four-part score is accepted");
		Check(store.Get(identity)?.Grade == "A" &&
			store.Get(identity)?.Date == "2026-08-14 12:34:56",
			"accepted score resolves grade and stable timestamp");
		Check(!store.TryUpdate(identity, new ScoreRecord
		{
			Score = 799999, Acc = 100.0,
		}, acceptedAt), "lower score cannot overwrite current BEST");
		var changedDigest = new ScoreIdentity("pack", "giga",
			V2Format.RulesetId, new string('b', 64));
		Check(store.Get(changedDigest) == null,
			"gameplay digest change isolates BEST");
		var metadataOnly = new ScoreIdentity("pack", "giga",
			V2Format.RulesetId, new string('a', 64));
		Check(store.Get(metadataOnly)?.Score == 800000,
			"same four-part identity preserves BEST across metadata-only changes");

		var saved = store.SaveJson();
		Check(saved.Contains("\"formatVersion\": 2", StringComparison.Ordinal) &&
			!saved.Contains("\"version\"", StringComparison.Ordinal),
			"score codec writes formatVersion:2");
		var roundTrip = new ScoreStoreCore();
		roundTrip.LoadJson(saved);
		Check(roundTrip.Get(identity)?.Score == 800000 &&
			roundTrip.GetLegacy("pack", "giga")?.Score == 700000,
			"versioned round trip preserves current and historical records");
	}
}
