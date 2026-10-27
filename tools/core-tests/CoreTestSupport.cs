using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace CoreTests;

internal static class CoreTestSupport
{
    internal static int Failures;

	internal static ExactBarTime Bar(long value) =>
		ExactBarTime.FromJsonComponents(value, 0, 1);

	internal static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
				return directory.FullName;
			directory = directory.Parent;
		}
		throw new DirectoryNotFoundException("Cannot find repository root from core-tests output.");
	}

	internal static Note TestNote(int id, int subId, NoteType type,
		double bar, double position, double width, double second,
		Track track = Track.Center) => new()
	{
		Id = id,
		SubNoteId = subId,
		Type = type,
		Track = track,
		BarTime = bar,
		Position = position,
		Width = width,
		Second = second,
		BakedSecond = second,
	};

    // ---- helpers ----

    internal static void Check(bool cond, string what)
    {
        if (!cond)
        {
            Failures++;
            Console.WriteLine($"    FAIL: {what}");
        }
    }
}
