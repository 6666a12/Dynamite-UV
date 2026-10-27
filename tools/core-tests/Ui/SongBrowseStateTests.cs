using DynamiteUniverse.Ui;

namespace CoreTests;

internal static class SongBrowseStateTests
{
    internal static void Run(Action<bool, string> check)
    {
        var state = new SongBrowseState([
            new("a", "Alpha", "Alice", [new("easy", "Jane"), new("hard", "张三")]),
            new("b", "Beta", "Bob", [new("mega", "John")]),
            new("c", "Empty", "Carol", []),
        ]);
        state.Filter(" ");
        check(state.VisibleIndices.SequenceEqual(new[] { 0, 1, 2 }) && state.PackIndex == -1,
            "empty search preserves catalog order and does not implicitly commit a selection");
        state.SelectPack(0);
        state.NextDifficulty(2);
        state.Filter("  BOB  ");
        check(state.VisibleIndices.SequenceEqual(new[] { 1 }) && state.PackIndex == 0 && state.DifficultyIndex == 1,
            "filter matches artist without moving the selection before its card has collapsed");
        state.SelectPack(state.VisibleIndices[0]);
        check(state.PackIndex == 1 && state.DifficultyIndex == 0, "replacement selection resets difficulty");
        state.Filter("张三");
        check(state.VisibleIndices.SequenceEqual(new[] { 0 }), "search includes each chart's charter");
        state.Filter("aLpHa");
        check(state.VisibleIndices.SequenceEqual(new[] { 0 }), "title search uses ordinal case-insensitive matching");
        state.Restore("a", "hard");
        check(state.PackIndex == 0 && state.DifficultyIndex == 1, "restore uses exact pack/chart identities");
        state.Restore("missing", "hard");
        check(state.PackIndex == 0 && state.DifficultyIndex == 1, "missing committed pack retains browsing state");
        state.NextDifficulty(2);
        check(state.DifficultyIndex == 0, "difficulty wraps around in catalog order");
        state.NextDifficulty(0);
        state.Filter("nothing matches");
        state.ClearSelection();
        check(state.PackIndex == -1 && state.VisibleIndices.Count == 0, "empty results clear selection explicitly");
        state.Restore("a", "hard");
        state.ClampDifficulty(1);
        check(state.DifficultyIndex == 0, "difficulty clamp follows current chart count");
        Console.WriteLine("  Song browse selection/filter/restore checks complete.");
    }
}
