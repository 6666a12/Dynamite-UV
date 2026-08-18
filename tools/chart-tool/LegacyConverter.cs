using ChartTool.IsolatedV2;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace ChartTool;

internal static class LegacyConverter
{
    private static readonly Dictionary<string, string> StandardDifficulties =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["casual"] = "casual",
            ["normal"] = "normal",
            ["hard"] = "hard",
            ["mega"] = "mega",
            ["giga"] = "giga",
            ["tech"] = "tech",
        };

    public static ConvertedPack? ConvertPack(LegacyPack pack, long revision,
        DiagnosticBag packDiagnostics, out IReadOnlyDictionary<string, DiagnosticBag> chartDiagnostics,
        out IReadOnlyDictionary<string, AuditMetrics> chartAudits)
    {
        var diagnosticsByChart = new Dictionary<string, DiagnosticBag>(StringComparer.Ordinal);
        var auditsByChart = new Dictionary<string, AuditMetrics>(StringComparer.Ordinal);
        var entries = new List<ConvertedChartEntry>();
        foreach (var entry in pack.Charts)
        {
            var diagnostics = new DiagnosticBag();
            diagnosticsByChart[entry.Diff] = diagnostics;
            if (entry.Level == 0)
            {
                diagnostics.Warning("meta.json", entry.Pointer + "/level",
                    "legacy level 0 maps to unrated:true and omits level");
            }
            var legacy = LegacyParser.ParseChart(entry, pack.SourceDirectory, diagnostics);
            if (legacy is null)
                continue;
            var converted = ConvertChart(pack, entry, legacy, diagnostics, out var audit);
            auditsByChart[entry.Diff] = audit;
            if (converted is not null)
                entries.Add(converted);
        }
        chartDiagnostics = diagnosticsByChart;
        chartAudits = auditsByChart;
        ValidateCrossChartMetadata(entries, packDiagnostics);
        return new ConvertedPack
        {
            Id = pack.Id,
            Revision = revision,
            Title = pack.Title,
            Artist = pack.Artist,
            Audio = pack.AudioPath,
            Cover = pack.CoverPath,
            Charts = entries,
        };
    }

    private static void ValidateCrossChartMetadata(
        IReadOnlyList<ConvertedChartEntry> entries, DiagnosticBag diagnostics)
    {
        var customKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(item => item.DifficultyKey is not null))
        {
            var folded = entry.DifficultyKey!.Normalize(System.Text.NormalizationForm.FormC)
                .ToUpperInvariant().ToLowerInvariant();
            if (!customKeys.Add(folded))
            {
                diagnostics.Error("meta.json", "/charts",
                    $"custom difficultyKey '{entry.DifficultyKey}' duplicates another key after NFC and case folding");
            }
        }
    }

    private static ConvertedChartEntry? ConvertChart(LegacyPack pack, LegacyChartEntry entry,
        LegacyChart legacy, DiagnosticBag diagnostics, out AuditMetrics audit)
    {
        ValidateSections(legacy, diagnostics);
        var chains = ValidateAndBuildChains(legacy, diagnostics);
        var scrolls = ConvertScrolls(legacy, diagnostics);
        audit = AuditLegacy(legacy, chains, diagnostics);
        if (diagnostics.HasErrors || legacy.Sections.Count == 0)
            return null;

        var first = legacy.Sections[0];
        var audioOffset = first.Seconds - first.BarTimeValue * 240.0 / first.Bpm;
        if (!double.IsFinite(audioOffset))
        {
            diagnostics.Error(legacy.File, first.Pointer,
                "extrapolated bar-0 audioOffsetSec is not finite");
            return null;
        }

        var bpms = legacy.Sections.Select(section => new ConvertedBpm
        {
            Time = section.BarTime,
            Bpm = section.Bpm,
        }).ToList();
        if (bpms[0].Time.CompareTo(RationalBarTime.Zero) > 0)
        {
            bpms.Insert(0, new ConvertedBpm
            {
                Time = RationalBarTime.Zero,
                Bpm = first.Bpm,
            });
            diagnostics.Warning(legacy.File, "/TimeLine/BakedBarSections/0/BarTime",
                "first BPM section occurs after bar 0; extrapolated its BPM and Seconds back to bar 0");
        }

        var consumedNodes = new HashSet<LegacyNote>(
            chains.Values.SelectMany(chain => chain.Skip(1)),
            ReferenceEqualityComparer.Instance);
        var left = ConvertTrack(legacy, LegacyTrack.Left, chains, consumedNodes, diagnostics);
        var center = ConvertTrack(legacy, LegacyTrack.Center, chains, consumedNodes, diagnostics);
        var right = ConvertTrack(legacy, LegacyTrack.Right, chains, consumedNodes, diagnostics);
        if (diagnostics.HasErrors)
            return null;

        var difficulty = StandardDifficulties.TryGetValue(entry.Diff, out var standard)
            ? standard
            : "custom";
        string? difficultyKey = null;
        if (difficulty == "custom")
        {
            difficultyKey = entry.Diff.Normalize(System.Text.NormalizationForm.FormC);
            if (!TextRules.IsDisplayText(difficultyKey, 24))
            {
                diagnostics.Error("meta.json", entry.Pointer + "/diff",
                    $"custom legacy diff '{entry.Diff}' cannot be used as a v2 difficultyKey (NFC, 1..24 code points, trimmed/control-free required)");
                return null;
            }
        }
        return new ConvertedChartEntry
        {
            Id = entry.Diff,
            Difficulty = difficulty,
            DifficultyKey = difficultyKey,
            Level = entry.Level == 0 ? null : entry.Level,
            Unrated = entry.Level == 0,
            Charter = pack.Charter,
            File = entry.File,
            Chart = new ConvertedChart
            {
                ChartId = entry.Diff,
                AudioOffsetSec = audioOffset,
                Bpms = bpms,
                ScrollSpeeds = scrolls,
                NotesLeft = left,
                NotesCenter = center,
                NotesRight = right,
            },
            Audit = audit,
        };
    }

    private static void ValidateSections(LegacyChart legacy, DiagnosticBag diagnostics)
    {
        if (legacy.Sections.Count == 0)
            return;
        var previous = legacy.Sections[0];
        for (var i = 1; i < legacy.Sections.Count; i++)
        {
            var current = legacy.Sections[i];
            if (previous.BarTime.CompareTo(current.BarTime) >= 0)
            {
                diagnostics.Error(legacy.File, current.Pointer + "/BarTime",
                    "BPM section BarTime values must be strictly increasing in source order");
            }
            var expected = previous.Seconds +
                (current.BarTimeValue - previous.BarTimeValue) * 240.0 / previous.Bpm;
            if (!double.IsFinite(expected) || Math.Abs(expected - current.Seconds) > 1e-9)
            {
                diagnostics.Error(legacy.File, current.Pointer + "/Seconds",
                    $"BPM Seconds continuity mismatch: stored={current.Seconds:R}, expected={expected:R}");
            }
            previous = current;
        }
    }

    private static IReadOnlyDictionary<LegacyNote, IReadOnlyList<LegacyNote>>
        ValidateAndBuildChains(LegacyChart legacy, DiagnosticBag diagnostics)
    {
        var allById = new Dictionary<int, LegacyNote>();
        foreach (var note in legacy.AllNotes)
        {
            if (!allById.TryAdd(note.Id, note))
                diagnostics.Error(legacy.File, note.Pointer + "/Id", $"duplicate legacy note Id {note.Id}");
        }

        var incoming = new Dictionary<LegacyNote, int>(ReferenceEqualityComparer.Instance);
        foreach (var note in legacy.AllNotes)
        {
            if (note.SubNoteId == -1)
                continue;
            if (!allById.TryGetValue(note.SubNoteId, out var target))
            {
                diagnostics.Error(legacy.File, note.Pointer + "/SubNoteId",
                    $"dangling SubNoteId {note.SubNoteId}");
                continue;
            }
            incoming[target] = incoming.GetValueOrDefault(target) + 1;
            if (incoming[target] > 1)
                diagnostics.Error(legacy.File, target.Pointer + "/Id",
                    $"shared path node {target.Id} has multiple incoming links");
        }

        var result = new Dictionary<LegacyNote, IReadOnlyList<LegacyNote>>(
            ReferenceEqualityComparer.Instance);
        var globallyConsumed = new HashSet<LegacyNote>(ReferenceEqualityComparer.Instance);
        foreach (var head in legacy.AllNotes.Where(note => note.Type is 3 or 6))
        {
            var expectedNodeType = head.Type == 3 ? 4 : 7;
            var kind = head.Type == 3 ? "Hold" : "Mixer";
            if (head.SubNoteId == -1)
            {
                diagnostics.Error(legacy.File, head.Pointer + "/SubNoteId",
                    $"{kind} head must link to at least one node");
                continue;
            }
            var chain = new List<LegacyNote> { head };
            var seen = new HashSet<LegacyNote>(ReferenceEqualityComparer.Instance) { head };
            var current = head;
            while (current.SubNoteId != -1)
            {
                if (!allById.TryGetValue(current.SubNoteId, out var next))
                    break;
                if (!seen.Add(next))
                {
                    diagnostics.Error(legacy.File, current.Pointer + "/SubNoteId",
                        $"cycle detected at legacy note Id {next.Id}");
                    break;
                }
                if (next.Track != head.Track)
                {
                    diagnostics.Error(legacy.File, current.Pointer + "/SubNoteId",
                        $"{kind} chain crosses tracks at legacy note Id {next.Id}");
                    break;
                }
                if (next.Type != expectedNodeType)
                {
                    diagnostics.Error(legacy.File, next.Pointer + "/Type",
                        $"wrong path node type {next.Type}; {kind} requires type {expectedNodeType}");
                    break;
                }
                if (next.BarTime.CompareTo(current.BarTime) <= 0)
                {
                    diagnostics.Error(legacy.File, next.Pointer + "/BarTime",
                        $"{kind} node times must strictly increase");
                    break;
                }
                chain.Add(next);
                current = next;
            }
            foreach (var note in chain)
            {
                if (!globallyConsumed.Add(note))
                    diagnostics.Error(legacy.File, note.Pointer + "/Id",
                        $"legacy note Id {note.Id} is shared by multiple sustain chains");
            }
            if (chain.Count >= 2 && chain[^1].SubNoteId == -1)
                result[head] = chain;
        }

        foreach (var node in legacy.AllNotes.Where(note => note.Type is 4 or 7))
        {
            if (!globallyConsumed.Contains(node))
            {
                diagnostics.Error(legacy.File, node.Pointer + "/Type",
                    $"standalone type {node.Type} path node is not reachable from a matching head");
            }
        }
        return result;
    }

    private static IReadOnlyList<ConvertedScroll> ConvertScrolls(LegacyChart legacy,
        DiagnosticBag diagnostics)
    {
        if (legacy.ScrollEvents.Count == 0)
            return [];
        var byTime = new SortedDictionary<RationalBarTime, LegacyScrollEvent>(
            RationalBarTimeComparer.Instance);
        foreach (var item in legacy.ScrollEvents)
            byTime[item.BarTime] = item;
        var converted = byTime.Values.Select(item => new ConvertedScroll
        {
            Time = item.BarTime,
            Value = item.Value,
        }).ToList();
        if (converted[0].Time.CompareTo(RationalBarTime.Zero) > 0)
        {
            converted.Insert(0, new ConvertedScroll
            {
                Time = RationalBarTime.Zero,
                Value = converted[0].Value,
            });
            diagnostics.Warning(legacy.File, "/NoteSystem__DropSpeeds",
                "first scroll event occurs after bar 0; prefixed bar 0 with its value");
        }
        return converted;
    }

    private static IReadOnlyList<ConvertedNote> ConvertTrack(LegacyChart legacy,
        LegacyTrack track,
        IReadOnlyDictionary<LegacyNote, IReadOnlyList<LegacyNote>> chains,
        HashSet<LegacyNote> consumedNodes, DiagnosticBag diagnostics)
    {
        var result = new List<ConvertedNote>();
        foreach (var note in legacy.NotesOf(track))
        {
            if (consumedNodes.Contains(note))
                continue;
            var type = TypeName(note.Type);
            if (type is null)
                continue;
            IReadOnlyList<ConvertedNode> nodes = [];
            if (note.Type is 3 or 6)
            {
                if (!chains.TryGetValue(note, out var chain))
                    continue;
                nodes = chain.Skip(1).Select(node => new ConvertedNode
                {
                    Id = LegacyId(node.Id),
                    Time = node.BarTime,
                    Center = CanonicalZero(node.Position + node.Width / 2.0),
                    Width = node.Width,
                    Judge = note.Type == 3 ? true : null,
                }).ToArray();
            }
            result.Add(new ConvertedNote
            {
                Id = LegacyId(note.Id),
                Type = type,
                Time = note.BarTime,
                Center = CanonicalZero(note.Position + note.Width / 2.0),
                Width = note.Width,
                Nodes = nodes,
            });
        }
        result.Sort((left, right) =>
        {
            var time = left.Time.CompareTo(right.Time);
            return time != 0 ? time : string.CompareOrdinal(left.Id, right.Id);
        });
        return result;
    }

    private static AuditMetrics AuditLegacy(LegacyChart legacy,
        IReadOnlyDictionary<LegacyNote, IReadOnlyList<LegacyNote>> chains,
        DiagnosticBag diagnostics)
    {
        var derivableSync = DeriveSyncNotes(legacy);
        var bakedSyncCount = legacy.AllNotes.Count(note => note.BakedSyncNote != 0);
        var syncMismatch = 0;
        foreach (var note in legacy.AllNotes)
        {
            if (note.Type == 1 && (note.BakedSyncNote != 0) != derivableSync.Contains(note))
                syncMismatch++;
        }
        var continuity = diagnostics.Items.Count(item =>
            item.Severity == DiagnosticSeverity.Error &&
            item.Reason.StartsWith("BPM Seconds continuity mismatch", StringComparison.Ordinal));
        var crossBpm = chains.Values.Count(chain => legacy.Sections.Skip(1).Any(section =>
            section.BarTime.CompareTo(chain[0].BarTime) > 0 &&
            section.BarTime.CompareTo(chain[^1].BarTime) <= 0));
        int? derived = TryBuildSharedJudgePlan(legacy, out var count) ? count : null;
        if (legacy.BakedTotalMainNote != 0 && derived is not null &&
            legacy.BakedTotalMainNote != derived.Value)
        {
            diagnostics.Warning(legacy.File, "/Baked_TotalMainNote",
                $"baked total {legacy.BakedTotalMainNote} differs from current derived JudgePlan {derived.Value}");
        }
        if (syncMismatch > 0)
        {
            diagnostics.Warning(legacy.File, "/",
                $"Baked_SyncNote differs from exact-time cross-track derivation on {syncMismatch} note(s); baked flags are dropped");
        }
        return new AuditMetrics
        {
            BakedTotal = legacy.BakedTotalMainNote,
            DerivedTotal = derived,
            BakedSyncCount = bakedSyncCount,
            DerivableSyncCount = derivableSync.Count,
            SyncMismatchCount = syncMismatch,
            ContinuityErrorCount = continuity,
            CrossBpmSustainCount = crossBpm,
        };
    }

    private static HashSet<LegacyNote> DeriveSyncNotes(LegacyChart legacy)
    {
        var pressTypes = new HashSet<int> { 1, 3, 5, 6 };
        var result = new HashSet<LegacyNote>(ReferenceEqualityComparer.Instance);
        foreach (var group in legacy.AllNotes.Where(note => pressTypes.Contains(note.Type))
                     .GroupBy(note => note.BarTime))
        {
            if (group.Select(note => note.Track).Distinct().Count() < 2)
                continue;
            foreach (var note in group.Where(note => note.Type == 1))
                result.Add(note);
        }
        return result;
    }

    private static bool TryBuildSharedJudgePlan(LegacyChart legacy, out int count)
    {
        count = 0;
        try
        {
            var sections = legacy.Sections.Select(section => new BarSection
            {
                Bpm = section.Bpm,
                BarTime = section.BarTimeValue,
                Seconds = section.Seconds,
            }).ToArray();
            var chart = new Chart
            {
                Name = legacy.Name,
                Title = legacy.Name,
                Difficulty = 3,
                TotalMainNote = legacy.BakedTotalMainNote,
                Sections = sections,
                NotesLeft = SharedNotes(legacy.NotesLeft, sections),
                NotesCenter = SharedNotes(legacy.NotesCenter, sections),
                NotesRight = SharedNotes(legacy.NotesRight, sections),
                DropSpeeds = legacy.ScrollEvents.Select(item =>
                    (item.BarTimeValue, item.Value)).ToArray(),
            };
            count = JudgePlan.Build(chart, JudgeSettings.ForPreset(JudgePreset.Hard))
                .HeadlineUnitCount;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<Note> SharedNotes(IEnumerable<LegacyNote> source,
        IReadOnlyList<BarSection> sections)
    {
        return source.Select(note => new Note
        {
            Id = note.Id,
            SubNoteId = note.SubNoteId,
            Type = (NoteType)note.Type,
            Track = note.Track switch
            {
                LegacyTrack.Left => Track.Left,
                LegacyTrack.Center => Track.Center,
                _ => Track.Right,
            },
            BarTime = note.BarTimeValue,
            Position = note.Position,
            Width = note.Width,
            BakedSecond = note.BakedSecond,
            Second = sections.Count == 0
                ? note.BakedSecond
                : SecondsAt(sections, note.BarTimeValue),
            SyncNote = note.BakedSyncNote,
        }).ToArray();
    }

    private static double SecondsAt(IReadOnlyList<BarSection> sections, double bar)
    {
        var active = sections[0];
        foreach (var section in sections)
        {
            if (section.BarTime > bar)
                break;
            active = section;
        }
        return active.Seconds + (bar - active.BarTime) * 240.0 / active.Bpm;
    }

    private static string? TypeName(int type) => type switch
    {
        1 => "tap",
        2 => "drag",
        3 => "hold",
        5 => "exTap",
        6 => "mixer",
        8 => "mine",
        9 => "barLine",
        _ => null,
    };

    private static string LegacyId(int id) => $"legacy-{id}";
    private static double CanonicalZero(double value) => value == 0 ? 0 : value;
}
