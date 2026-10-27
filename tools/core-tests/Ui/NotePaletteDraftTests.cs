using DynamiteUniverse.Ui;

namespace CoreTests;

internal static class NotePaletteDraftTests
{
    private static readonly string[] Factory =
        ["40C9FF", "B5EBFF", "FFC66A", "56EEA5", "FF75BB", "FF6977"];

    public static void Run(Action<bool, string> check)
    {
        InitialIsolation(check);
        InvalidInputs(check);
        GesturesAndNoOps(check);
        UndoRedoBranch(check);
        HistoryCapacity(check);
        ApplyDiscardAndOriginal(check);
        PresetsAndLibrarySettings(check);
        LegacyDuplicateNames(check);
        Console.WriteLine("  Note palette draft checks complete.");
    }

    private static void InitialIsolation(Action<bool, string> check)
    {
        var sourceColors = Factory.Select(hex => "#" + hex.ToLowerInvariant()).ToList();
        var slot = new PaletteSlotSnapshot(" DEFAULT ", false, sourceColors);
        sourceColors[0] = "000000";
        check(slot.Colors[0] == "#40c9ff", "slot snapshot copies the caller's color collection");

        var sourceSlots = EmptySlots().ToList();
        sourceSlots[0] = slot;
        var initial = new PaletteLibrarySnapshot(sourceSlots, 0, false);
        sourceSlots[0] = new PaletteSlotSnapshot("REPLACED", false, Factory);
        var factoryInput = Factory.ToList();
        var draft = new NotePaletteDraft(initial, factoryInput);
        factoryInput[0] = "000000";
        check(draft.Current.Slots[0].Name == "DEFAULT" &&
              draft.Current.Slots[0].Colors.SequenceEqual(Factory),
            "draft normalizes RGB and names without borrowing caller state");
        check(ReferenceEquals(draft.Current, draft.Current) && !draft.IsDirty &&
              !draft.CanUndo && !draft.CanRedo,
            "current getter returns an immutable stable snapshot, initially clean with no history");
        check(!ReferenceEquals(draft.Current, initial) &&
              !ReferenceEquals(draft.Current.Slots[0], initial.Slots[0]) &&
              !ReferenceEquals(draft.Current.Slots[0].Colors, initial.Slots[0].Colors),
            "draft initialization deeply detaches every nested snapshot");

        var exposed = draft.Current;
        var colorsBlocked = exposed.Slots[0].Colors is not IList<string> colors ||
                            Throws<NotSupportedException>(() => colors[0] = "000000");
        var slotsBlocked = exposed.Slots is not IList<PaletteSlotSnapshot> slots ||
                           Throws<NotSupportedException>(() => slots[0] = sourceSlots[0]);
        check(colorsBlocked && slotsBlocked && State(draft.Current) == State(draft.Original),
            "public snapshot collections cannot be cast back to writable draft state");
        var originalBlocked = draft.Original.Slots[0].Colors is not IList<string> originalColors ||
                              Throws<NotSupportedException>(() => originalColors[0] = "000000");
        check(originalBlocked, "Original's nested collections are read-only too");

        check(draft.SetColor(0, "#a1b2c3") && draft.Current.Slots[0].Colors[0] == "A1B2C3",
            "optional hash and lowercase hex canonicalize to uppercase six digits");
        check(exposed.Slots[0].Colors[0] == Factory[0] && initial.Slots[0].Colors[0] == "#40c9ff",
            "retained current and input snapshots do not change after later edits");
        check(draft.ResetColor(0) && draft.Current.Slots[0].Colors[0] == Factory[0],
            "factory colors are copied and cannot be changed by the constructor caller");
    }

    private static void InvalidInputs(Action<bool, string> check)
    {
        var draft = NewDraft();
        var initial = State(draft.Current);
        foreach (var hex in new string?[]
                 { null, "", "12345", "1234567", "#12345", "#1234567", "##123456",
                   "GG0000", " 123456", "123456 ", "#12 456", "１２３４５６", "#12345678" })
            check(!draft.SetColor(0, hex!), "invalid RGB is rejected: " + (hex ?? "<null>"));
        foreach (var name in new string?[]
                 { null, "", "   ", "lowercase", "BAD_NAME", "BAD.NAME", "中文", "A\tB",
                   " A\n", "ABCDEFGHIJKLM" })
        {
            check(!draft.CreatePreset(1, name!), "invalid preset name is rejected: " + (name ?? "<null>"));
            check(!draft.RenamePreset(0, name!), "rename rejects the same invalid name");
        }
        check(!draft.SetColor(-1, "123456") && !draft.SetColor(6, "123456") &&
              !draft.ResetColor(-1) && !draft.ResetColor(6), "channel range is exactly zero through five");
        foreach (var index in new[] { -1, 10, int.MaxValue })
            check(!draft.SelectSlot(index) && !draft.CreatePreset(index, "NEW") &&
                  !draft.RenamePreset(index, "NEW") && !draft.ClearSlot(index) &&
                  !draft.ResetSlot(index), "out-of-range slots are rejected by every slot operation");
        check(!draft.SelectSlot(1) && !draft.CreatePreset(0, "NEW") &&
              !draft.RenamePreset(1, "NEW") && !draft.ClearSlot(0) &&
              !draft.ClearSlot(1) && !draft.ResetSlot(1),
            "empty slot actions and overwriting/clearing slot zero are rejected");
        check(!draft.CreatePreset(1, "DEFAULT"), "creating a duplicate occupied name is rejected");
        check(State(draft.Current) == initial && !draft.IsDirty && !draft.CanUndo && !draft.CanRedo,
            "all invalid operations leave both state and history untouched");

        check(Throws<ArgumentException>(() => new NotePaletteDraft(
                  new PaletteLibrarySnapshot(EmptySlots().Take(9).ToArray(), 0, false), Factory)),
            "constructor rejects a non-ten-slot library");
        check(Throws<ArgumentException>(() => new NotePaletteDraft(Initial(), Factory.Take(5).ToArray())),
            "constructor rejects a non-six-color factory");
        var invalidSlots = EmptySlots();
        invalidSlots[0] = new PaletteSlotSnapshot("DEFAULT", false, ["123456"]);
        check(Throws<ArgumentException>(() => new NotePaletteDraft(
                  new PaletteLibrarySnapshot(invalidSlots, 0, false), Factory)),
            "constructor rejects a slot with the wrong color count");
        invalidSlots = EmptySlots();
        invalidSlots[2] = new PaletteSlotSnapshot("", true,
            ["123456", "123456", "123456", "123456", "123456", "BADHEX"]);
        check(Throws<ArgumentException>(() => new NotePaletteDraft(
                  new PaletteLibrarySnapshot(invalidSlots, 0, false), Factory)),
            "constructor validates even empty slot RGB values");
        check(Throws<ArgumentException>(() => new NotePaletteDraft(
                  new PaletteLibrarySnapshot(EmptySlots(), 1, false), Factory)) &&
              Throws<ArgumentException>(() => new NotePaletteDraft(
                  new PaletteLibrarySnapshot(EmptySlots(), -1, false), Factory)),
            "constructor rejects empty or out-of-range active selection");
        invalidSlots = EmptySlots();
        invalidSlots[0] = new PaletteSlotSnapshot("", true, Factory);
        check(Throws<ArgumentException>(() => new NotePaletteDraft(
                  new PaletteLibrarySnapshot(invalidSlots, 1, false), Factory)),
            "constructor requires a nonempty fallback slot zero");
        check(Throws<ArgumentNullException>(() => new NotePaletteDraft(null!, Factory)) &&
              Throws<ArgumentNullException>(() => new NotePaletteDraft(Initial(), null!)),
            "constructor rejects missing initial state or factory colors");
    }

    private static void GesturesAndNoOps(Action<bool, string> check)
    {
        var draft = NewDraft();
        var initial = State(draft.Current);
        draft.BeginGesture();
        draft.BeginGesture();
        check(draft.SetColor(3, "000001") && draft.SetColor(3, "000002") &&
              draft.SetColor(3, "#000003"), "gesture exposes each intermediate RGB immediately");
        check(draft.CanUndo && !draft.CanRedo && draft.IsDirty,
            "pending changed gesture is visible to undo and dirty state");
        check(!draft.SetColor(3, "000003") && !draft.SetColor(3, "invalid"),
            "no-op and invalid updates do not add gesture changes");
        draft.EndGesture();
        draft.EndGesture();
        check(draft.Undo() && State(draft.Current) == initial && !draft.CanUndo,
            "multiple drag updates (including repeated Begin) make exactly one undo step");
        check(draft.Redo() && draft.Current.Slots[0].Colors[3] == "000003" && !draft.CanRedo,
            "redo restores the final gesture value, not an intermediate drag sample");
        draft.DiscardChanges();
        draft.BeginGesture();
        draft.EndGesture();
        draft.BeginGesture();
        check(!draft.SetColor(0, "#40c9ff"), "canonical-equivalent RGB is a no-op");
        draft.EndGesture();
        check(!draft.CanUndo && !draft.IsDirty, "empty and unchanged gestures consume no history");

        check(draft.SetColor(0, "111111") && draft.Undo(), "prepare a redo branch");
        draft.BeginGesture();
        check(draft.SetColor(0, "222222") && !draft.CanRedo && !draft.Redo() &&
              draft.SetColor(0, Factory[0]),
            "redo is rejected during a changed gesture without ending a drag back to its start");
        draft.EndGesture();
        check(!draft.CanUndo && draft.CanRedo && !draft.IsDirty && draft.Redo() &&
              draft.Current.Slots[0].Colors[0] == "111111",
            "net-zero gesture preserves existing redo and consumes no undo step");
        draft.DiscardChanges();
        draft.BeginGesture();
        check(draft.SetColor(1, "123456") && draft.Undo() &&
              draft.Current.Slots[0].Colors[1] == Factory[1] && draft.Redo(),
            "undo closes and reverts an unclosed gesture without a separate End call");
    }

    private static void UndoRedoBranch(Action<bool, string> check)
    {
        var draft = NewDraft();
        check(draft.SetColor(0, "111111") && draft.SetColor(0, "222222") && draft.Undo(),
            "ordinary operations have individual undo steps");
        var beforeNoOps = State(draft.Current);
        check(!draft.SetColor(0, "#111111") && !draft.SetBurst(false) &&
              !draft.SelectSlot(0) && !draft.RenamePreset(0, " DEFAULT "),
            "color, burst, selection and name no-ops return false");
        check(!draft.CreatePreset(1, "DEFAULT") && !draft.SetColor(8, "333333") &&
              State(draft.Current) == beforeNoOps && draft.CanRedo,
            "invalid actions and no-ops preserve the redo branch");
        check(draft.SetBurst(true) && !draft.CanRedo && !draft.Redo(),
            "a real edit after undo drops the abandoned redo branch");
        check(draft.Undo() && !draft.Current.BurstFollowsNote &&
              draft.Current.Slots[0].Colors[0] == "111111" && draft.Undo() && !draft.CanUndo,
            "undo walks the new branch back without resurrecting abandoned changes");
        check(draft.Redo() && draft.Redo() && draft.Current.BurstFollowsNote &&
              draft.Current.Slots[0].Colors[0] == "111111" && !draft.CanRedo,
            "redo replays exactly the replacement branch");
    }

    private static void HistoryCapacity(Action<bool, string> check)
    {
        var draft = NewDraft();
        for (var i = 1; i <= 25; i++)
        {
            var hex = i.ToString("X6");
            check(draft.SetColor(0, hex), "history test edit " + i);
            check(!draft.SetColor(0, hex), "history test no-op " + i);
        }
        draft.BeginGesture();
        check(draft.SetColor(0, "FFFFFF") && draft.SetColor(0, "000019"),
            "full history permits a gesture that returns to its start");
        draft.EndGesture();
        var count = 0;
        while (draft.Undo())
            count++;
        check(count == 20 && draft.Current.Slots[0].Colors[0] == "000005" && !draft.CanUndo,
            "history keeps the newest twenty changes and no-ops never evict real edits");
        count = 0;
        while (draft.Redo())
            count++;
        check(count == 20 && draft.Current.Slots[0].Colors[0] == "000019" && !draft.CanRedo,
            "all twenty retained changes can be redone");
        draft.DiscardChanges();
        check(draft.Current.Slots[0].Colors[0] == Factory[0] && !draft.CanUndo && !draft.CanRedo,
            "discard baseline survives history truncation");
    }

    private static void ApplyDiscardAndOriginal(Action<bool, string> check)
    {
        var draft = NewDraft();
        var entry = State(draft.Original);
        check(draft.SetColor(0, "123456") && draft.IsDirty, "color edits mark the draft dirty");
        check(draft.SetColor(0, Factory[0]) && !draft.IsDirty && draft.CanUndo,
            "dirty compares content, not whether history contains commands");
        draft.DiscardChanges();
        check(draft.SetColor(0, "123456") && draft.CreatePreset(2, "MINT") && draft.SetBurst(true),
            "prepare an applied color, preset, active selection and burst setting");
        var applied = State(draft.Current);
        draft.AcceptApplied();
        check(!draft.IsDirty && draft.CanUndo && State(draft.Original) == entry,
            "apply updates only the dirty baseline and preserves entry Original and history");
        check(draft.Undo() && draft.IsDirty && !draft.Current.BurstFollowsNote &&
              draft.Redo() && !draft.IsDirty,
            "undo away from applied content is dirty; redo to it is clean");
        draft.BeginGesture();
        check(draft.SetColor(3, "ABCDEF") && draft.RenamePreset(2, "NEW NAME") &&
              draft.SelectSlot(0) && draft.SetBurst(false), "prepare an unclosed multi-field change");
        draft.DiscardChanges();
        check(State(draft.Current) == applied && !draft.IsDirty && !draft.CanUndo && !draft.CanRedo,
            "discard cancels an active gesture and restores all fields to latest apply");
        check(State(draft.Original) == entry, "discard never changes fixed entry Original");
        draft.BeginGesture();
        check(draft.SetColor(1, "FEDCBA"), "prepare applying an active gesture");
        draft.AcceptApplied();
        var latestApplied = State(draft.Current);
        check(!draft.IsDirty && draft.Undo() && draft.IsDirty && State(draft.Original) == entry,
            "apply closes a gesture as one undo step while keeping Original fixed");
        draft.DiscardChanges();
        check(State(draft.Current) == latestApplied && !draft.CanUndo && !draft.CanRedo,
            "discard returns to the newest apply, not the page-entry state");
    }

    private static void PresetsAndLibrarySettings(Action<bool, string> check)
    {
        var draft = NewDraft();
        check(draft.SetColor(3, "7BECC5") && draft.CreatePreset(1, "  MINT-02  "),
            "creating a named empty preset accepts trimmed valid names");
        check(draft.Current.ActiveIndex == 1 && !draft.Current.Slots[1].IsEmpty &&
              draft.Current.Slots[1].Name == "MINT-02" &&
              draft.Current.Slots[1].Colors.SequenceEqual(draft.Current.Slots[0].Colors),
            "creation copies current six colors and selects the new slot in one operation");
        check(draft.Undo() && draft.Current.ActiveIndex == 0 && draft.Current.Slots[1].IsEmpty &&
              draft.Redo() && draft.Current.ActiveIndex == 1 && !draft.Current.Slots[1].IsEmpty,
            "creating a preset and selecting it is exactly one undo/redo step");
        check(!draft.CreatePreset(1, "OVERWRITE") && !draft.CreatePreset(2, "MINT-02"),
            "creation cannot overwrite an occupied slot or reuse its name");
        check(draft.SetColor(0, "ABCDEF") && draft.Current.Slots[0].Colors[0] == Factory[0],
            "editing a copied preset leaves its source palette isolated");
        check(draft.CreatePreset(2, "ABCDEFGHIJKL") && draft.Current.Slots[2].Name.Length == 12,
            "twelve-character names are accepted");
        check(!draft.RenamePreset(2, "MINT-02") && !draft.RenamePreset(2, "DEFAULT") &&
              draft.RenamePreset(2, "CUSTOM 2") && !draft.RenamePreset(2, "CUSTOM 2"),
            "rename requires unique occupied names and ignores unchanged names");
        check(draft.SelectSlot(1) && draft.IsDirty && draft.Undo() && draft.Current.ActiveIndex == 2 &&
              draft.Redo() && draft.Current.ActiveIndex == 1,
            "active selection participates in undo and redo");
        check(draft.ResetColor(0) && draft.Current.Slots[1].Colors[0] == Factory[0] &&
              draft.Current.Slots[1].Colors[3] == "7BECC5",
            "reset color changes only the selected channel in the active slot");
        check(draft.ResetSlot(1) && draft.Current.Slots[1].Colors.SequenceEqual(Factory) &&
              draft.Current.Slots[1].Name == "MINT-02" && draft.Current.ActiveIndex == 1 &&
              !draft.ResetSlot(1), "reset slot preserves its name and selection; repeated reset is a no-op");
        check(draft.ResetSlot(2) && draft.Current.ActiveIndex == 1 &&
              draft.Current.Slots[2].Colors.SequenceEqual(Factory),
            "reset can target another occupied slot without changing selection");
        check(draft.ClearSlot(2) && draft.Current.ActiveIndex == 1,
            "clearing an inactive preset preserves the current selection");
        check(draft.ClearSlot(1) && draft.Current.ActiveIndex == 0 &&
              draft.Current.Slots[1].IsEmpty && draft.Current.Slots[1].Name == "" &&
              draft.Current.Slots[1].Colors.SequenceEqual(Factory),
            "clear active preset returns to zero and clears its name and colors");
        check(draft.Undo() && draft.Current.ActiveIndex == 1 &&
              draft.Current.Slots[1].Name == "MINT-02" && !draft.Current.Slots[1].IsEmpty &&
              draft.Redo() && draft.Current.ActiveIndex == 0,
            "clearing and selection fallback are one undoable change");
        check(draft.CreatePreset(1, "MINT-02"), "a cleared name can be reused");
        draft.AcceptApplied();
        check(draft.SelectSlot(0) && draft.IsDirty && draft.Undo() && !draft.IsDirty,
            "selection alone affects dirty relative to apply");
        check(draft.SetBurst(true) && draft.IsDirty && draft.Undo() && !draft.IsDirty &&
              !draft.Current.BurstFollowsNote && draft.Redo() && draft.Current.BurstFollowsNote,
            "burst alone participates in dirty, undo and redo");
        check(draft.RenamePreset(0, "FACTORY-ALT") && draft.Current.Slots[0].Name == "FACTORY-ALT" &&
              !draft.ClearSlot(0), "slot zero may be renamed but never cleared");
    }

    private static void LegacyDuplicateNames(Action<bool, string> check)
    {
        var slots = EmptySlots();
        slots[1] = new PaletteSlotSnapshot("DEFAULT", false, Factory);
        var draft = new NotePaletteDraft(new PaletteLibrarySnapshot(slots, 1, false), Factory);
        check(draft.Current.Slots[0].Name == "DEFAULT" && draft.Current.Slots[1].Name == "DEFAULT" &&
              !draft.IsDirty, "legacy duplicate names do not prevent opening the palette page");
        check(!draft.CreatePreset(2, "DEFAULT") && draft.RenamePreset(1, "REPAIRED") &&
              !draft.RenamePreset(1, "DEFAULT"),
            "legacy duplicates can be repaired without allowing new name conflicts");
        check(draft.Undo() && draft.Current.Slots[1].Name == "DEFAULT" && !draft.IsDirty,
            "undo can restore a legacy duplicate without changing initial semantics");
    }

    private static PaletteSlotSnapshot[] EmptySlots()
    {
        var slots = new PaletteSlotSnapshot[10];
        slots[0] = new PaletteSlotSnapshot("DEFAULT", false, Factory);
        for (var i = 1; i < slots.Length; i++)
            slots[i] = new PaletteSlotSnapshot("", true, Factory);
        return slots;
    }

    private static PaletteLibrarySnapshot Initial() => new(EmptySlots(), 0, false);
    private static NotePaletteDraft NewDraft() => new(Initial(), Factory);

    private static string State(PaletteLibrarySnapshot snapshot) =>
        $"{snapshot.ActiveIndex}|{snapshot.BurstFollowsNote}|" + string.Join("|", snapshot.Slots.Select(
            slot => $"{slot.Name}:{slot.IsEmpty}:{string.Join(",", slot.Colors)}"));

    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }
}
