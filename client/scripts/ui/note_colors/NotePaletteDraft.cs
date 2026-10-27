using System;
using System.Collections.Generic;
using System.Linq;

namespace DynamiteUniverse.Ui;

/// <summary>A detached, immutable palette slot. Color strings are copied, not borrowed.</summary>
public sealed record PaletteSlotSnapshot
{
    public string Name { get; }
    public bool IsEmpty { get; }
    public IReadOnlyList<string> Colors { get; }

    public PaletteSlotSnapshot(string Name, bool IsEmpty, IReadOnlyList<string> Colors)
    {
        ArgumentNullException.ThrowIfNull(Name);
        ArgumentNullException.ThrowIfNull(Colors);
        this.Name = Name;
        this.IsEmpty = IsEmpty;
        this.Colors = Array.AsReadOnly(Colors.ToArray());
    }

    public void Deconstruct(out string Name, out bool IsEmpty, out IReadOnlyList<string> Colors)
    {
        Name = this.Name;
        IsEmpty = this.IsEmpty;
        Colors = this.Colors;
    }
}

/// <summary>Deeply copied, immutable slots plus the two library-wide settings.</summary>
public sealed record PaletteLibrarySnapshot
{
    public IReadOnlyList<PaletteSlotSnapshot> Slots { get; }
    public int ActiveIndex { get; }
    public bool BurstFollowsNote { get; }

    public PaletteLibrarySnapshot(IReadOnlyList<PaletteSlotSnapshot> Slots,
        int ActiveIndex, bool BurstFollowsNote)
    {
        ArgumentNullException.ThrowIfNull(Slots);
        var copies = new PaletteSlotSnapshot[Slots.Count];
        for (var i = 0; i < copies.Length; i++)
        {
            var slot = Slots[i] ?? throw new ArgumentException("A slot must not be null.", nameof(Slots));
            copies[i] = new PaletteSlotSnapshot(slot.Name, slot.IsEmpty, slot.Colors);
        }
        this.Slots = Array.AsReadOnly(copies);
        this.ActiveIndex = ActiveIndex;
        this.BurstFollowsNote = BurstFollowsNote;
    }

    public void Deconstruct(out IReadOnlyList<PaletteSlotSnapshot> Slots,
        out int ActiveIndex, out bool BurstFollowsNote)
    {
        Slots = this.Slots;
        ActiveIndex = this.ActiveIndex;
        BurstFollowsNote = this.BurstFollowsNote;
    }
}

/// <summary>
/// Godot-free session draft for ten six-color presets. Nothing here writes settings or touches
/// rendering. All operations return true only for a real change; invalid inputs and no-ops return
/// false without altering state, history or the apply baseline. Canonical colors are uppercase
/// six-digit RGB strings without '#'. Occupied names are trimmed ASCII uppercase names of 1–12
/// characters; illegal characters are rejected rather than filtered or truncated.
/// </summary>
public sealed class NotePaletteDraft
{
    private const int SlotCount = 10;
    private const int ColorCount = 6;
    private const int NameMaxLength = 12;
    private const int HistoryLimit = 20;

    private readonly string[] _factoryColors;
    private readonly List<PaletteLibrarySnapshot> _undo = new();
    private readonly List<PaletteLibrarySnapshot> _redo = new();
    private PaletteLibrarySnapshot _current;
    private PaletteLibrarySnapshot _applied;
    private PaletteLibrarySnapshot? _gestureStart;

    /// <summary>Immutable current state, safe to retain while later edits create new snapshots.</summary>
    public PaletteLibrarySnapshot Current => _current;

    /// <summary>The entry-page snapshot, never changed by apply, discard, undo or redo.</summary>
    public PaletteLibrarySnapshot Original { get; }

    public bool IsDirty => !SameState(_current, _applied);
    public bool CanUndo => GestureChanged || _undo.Count > 0;
    public bool CanRedo => !GestureChanged && _redo.Count > 0;

    public NotePaletteDraft(PaletteLibrarySnapshot initial, IReadOnlyList<string> factoryColors)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(factoryColors);
        _factoryColors = NormalizeColors(factoryColors, nameof(factoryColors));
        _current = NormalizeInitial(initial);
        Original = Copy(_current);
        _applied = Copy(_current);
    }

    /// <summary>
    /// Starts one history transaction. Repeated begins are harmless. State updates immediately,
    /// but history is recorded at EndGesture only if the final state differs from the start.
    /// </summary>
    public void BeginGesture() => _gestureStart ??= _current;

    /// <summary>A drag back to its starting value consumes no history and preserves redo.</summary>
    public void EndGesture()
    {
        if (_gestureStart is not { } start)
            return;
        _gestureStart = null;
        if (SameState(start, _current))
            return;
        PushBounded(_undo, start);
        _redo.Clear();
    }

    public bool SetColor(int channel, string hex)
    {
        if (!ValidChannel(channel) || !TryNormalizeHex(hex, out var normalized))
            return false;
        var slot = _current.Slots[_current.ActiveIndex];
        var colors = slot.Colors.ToArray();
        colors[channel] = normalized;
        return ReplaceSlot(_current.ActiveIndex, new PaletteSlotSnapshot(slot.Name, false, colors));
    }

    public bool SetBurst(bool followsNote) => Change(new PaletteLibrarySnapshot(
        _current.Slots, _current.ActiveIndex, followsNote));

    public bool SelectSlot(int slot)
    {
        if (!ValidSlot(slot) || _current.Slots[slot].IsEmpty)
            return false;
        return Change(new PaletteLibrarySnapshot(_current.Slots, slot, _current.BurstFollowsNote));
    }

    /// <summary>Creates only in an empty non-factory slot, copying current colors and selecting it.</summary>
    public bool CreatePreset(int slot, string name)
    {
        if (slot <= 0 || !ValidSlot(slot) || !_current.Slots[slot].IsEmpty ||
            !TryNormalizeName(name, out var normalized) || NameTaken(normalized, slot))
            return false;
        return ReplaceSlot(slot, new PaletteSlotSnapshot(normalized, false,
            _current.Slots[_current.ActiveIndex].Colors), slot);
    }

    public bool RenamePreset(int slot, string name)
    {
        if (!ValidSlot(slot) || _current.Slots[slot].IsEmpty ||
            !TryNormalizeName(name, out var normalized) || NameTaken(normalized, slot))
            return false;
        return ReplaceSlot(slot, new PaletteSlotSnapshot(normalized, false, _current.Slots[slot].Colors));
    }

    /// <summary>Slot zero cannot be cleared. Clearing the selected slot returns selection to zero.</summary>
    public bool ClearSlot(int slot)
    {
        if (slot <= 0 || !ValidSlot(slot) || _current.Slots[slot].IsEmpty)
            return false;
        return ReplaceSlot(slot, new PaletteSlotSnapshot("", true, _factoryColors),
            _current.ActiveIndex == slot ? 0 : _current.ActiveIndex);
    }

    public bool ResetColor(int channel) => ValidChannel(channel) && SetColor(channel, _factoryColors[channel]);

    /// <summary>Resets the six colors only; the slot name and current selection are preserved.</summary>
    public bool ResetSlot(int slot)
    {
        if (!ValidSlot(slot) || _current.Slots[slot].IsEmpty)
            return false;
        return ReplaceSlot(slot, new PaletteSlotSnapshot(_current.Slots[slot].Name, false, _factoryColors));
    }

    /// <summary>Closes an outstanding gesture before undoing it as a single change.</summary>
    public bool Undo()
    {
        EndGesture();
        if (_undo.Count == 0)
            return false;
        PushBounded(_redo, _current);
        _current = Pop(_undo);
        return true;
    }

    public bool Redo()
    {
        // A changed live gesture already owns the new branch. Reject redo without implicitly
        // committing that gesture; the caller can continue dragging or undo it as one change.
        if (!CanRedo)
            return false;
        EndGesture();
        PushBounded(_undo, _current);
        _current = Pop(_redo);
        return true;
    }

    /// <summary>
    /// Call after the adapter successfully persists Current. History remains available, but dirty
    /// compares against this state from now on; Original is intentionally left unchanged.
    /// </summary>
    public void AcceptApplied()
    {
        EndGesture();
        _applied = Copy(_current);
    }

    /// <summary>Restores the most recently applied state and cancels all pending/history changes.</summary>
    public void DiscardChanges()
    {
        _gestureStart = null;
        _current = Copy(_applied);
        _undo.Clear();
        _redo.Clear();
    }

    private bool GestureChanged => _gestureStart is { } start && !SameState(start, _current);

    private bool ReplaceSlot(int index, PaletteSlotSnapshot replacement, int? activeIndex = null)
    {
        var slots = _current.Slots.ToArray();
        slots[index] = replacement;
        return Change(new PaletteLibrarySnapshot(slots, activeIndex ?? _current.ActiveIndex,
            _current.BurstFollowsNote));
    }

    private bool Change(PaletteLibrarySnapshot next)
    {
        if (SameState(_current, next))
            return false;
        if (_gestureStart is null)
        {
            PushBounded(_undo, _current);
            _redo.Clear();
        }
        _current = next;
        return true;
    }

    private bool NameTaken(string name, int exceptSlot) => _current.Slots
        .Where((slot, index) => index != exceptSlot && !slot.IsEmpty)
        .Any(slot => string.Equals(slot.Name, name, StringComparison.Ordinal));

    private static PaletteLibrarySnapshot NormalizeInitial(PaletteLibrarySnapshot initial)
    {
        if (initial.Slots.Count != SlotCount)
            throw new ArgumentException("A palette library must have exactly ten slots.", nameof(initial));
        if (!ValidSlot(initial.ActiveIndex) || initial.Slots[0].IsEmpty ||
            initial.Slots[initial.ActiveIndex].IsEmpty)
            throw new ArgumentException("Slot zero and the selected slot must not be empty.", nameof(initial));

        var slots = new PaletteSlotSnapshot[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var slot = initial.Slots[i];
            var colors = NormalizeColors(slot.Colors, nameof(initial));
            var name = "";
            if (!slot.IsEmpty && !TryNormalizeName(slot.Name, out name))
                throw new ArgumentException("Occupied slot names must be valid.", nameof(initial));
            // Old settings may already contain duplicate names. Preserve those so the page can
            // open and repair them; create/rename still reject introducing any name conflict.
            // Empty slot labels are presentation only, not a persisted preset name.
            slots[i] = new PaletteSlotSnapshot(name, slot.IsEmpty, colors);
        }
        return new PaletteLibrarySnapshot(slots, initial.ActiveIndex, initial.BurstFollowsNote);
    }

    private static string[] NormalizeColors(IReadOnlyList<string> colors, string parameterName)
    {
        if (colors.Count != ColorCount)
            throw new ArgumentException("A palette must have exactly six RGB colors.", parameterName);
        var result = new string[ColorCount];
        for (var i = 0; i < ColorCount; i++)
            if (!TryNormalizeHex(colors[i], out result[i]))
                throw new ArgumentException("Colors must be six hexadecimal digits, optionally prefixed with '#'.",
                    parameterName);
        return result;
    }

    private static bool TryNormalizeHex(string? hex, out string normalized)
    {
        normalized = "";
        if (hex is null)
            return false;
        var start = hex.Length > 0 && hex[0] == '#' ? 1 : 0;
        if (hex.Length - start != 6)
            return false;
        for (var i = start; i < hex.Length; i++)
            if (hex[i] is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
                return false;
        normalized = hex[start..].ToUpperInvariant();
        return true;
    }

    private static bool TryNormalizeName(string? name, out string normalized)
    {
        normalized = "";
        if (name is null)
            return false;
        foreach (var ch in name)
            if (ch is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-'))
                return false;
        var trimmed = name.Trim(' ');
        if (trimmed.Length is < 1 or > NameMaxLength)
            return false;
        normalized = trimmed;
        return true;
    }

    private static bool ValidSlot(int index) => index is >= 0 and < SlotCount;
    private static bool ValidChannel(int index) => index is >= 0 and < ColorCount;

    private static PaletteLibrarySnapshot Copy(PaletteLibrarySnapshot value) =>
        new(value.Slots, value.ActiveIndex, value.BurstFollowsNote);

    private static bool SameState(PaletteLibrarySnapshot left, PaletteLibrarySnapshot right)
    {
        if (left.ActiveIndex != right.ActiveIndex || left.BurstFollowsNote != right.BurstFollowsNote)
            return false;
        for (var i = 0; i < SlotCount; i++)
        {
            var a = left.Slots[i];
            var b = right.Slots[i];
            if (a.IsEmpty != b.IsEmpty || !string.Equals(a.Name, b.Name, StringComparison.Ordinal) ||
                !a.Colors.SequenceEqual(b.Colors, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static void PushBounded(List<PaletteLibrarySnapshot> history, PaletteLibrarySnapshot state)
    {
        history.Add(state);
        if (history.Count > HistoryLimit)
            history.RemoveAt(0);
    }

    private static PaletteLibrarySnapshot Pop(List<PaletteLibrarySnapshot> history)
    {
        var last = history[^1];
        history.RemoveAt(history.Count - 1);
        return last;
    }
}
