using DuxShared.Chart;

namespace DuxShared.Judge;

/// <summary>判定等级（规格书 §6.4，原文拼写 Prefect）。</summary>
public enum JudgeGrade
{
    Prefect,
    Great,
    Good,
    Miss,
}

/// <summary>偏移细分（§6.4 Early/Late，用于偏移提示）。</summary>
public enum HitTiming
{
    Exact,
    Early,
    Late,
}

/// <summary>计分类别（规格书 §7.1 Score 表 10 类）。</summary>
public enum ScoreCategory
{
    Tap,
    Burst,
    Chain,
    Mine,
    HoldStart,
    HoldEnd,
    HoldHolding,
    MixerStart,
    MixerEnd,
    MixerHolding,
}

/// <summary>判定单元种类：Input=需玩家输入；Auto=自动命中（链体）；HoldPoint=按住期间判定点；Mine=不碰给分。</summary>
public enum UnitKind
{
    Input,
    Auto,
    HoldPoint,
    Mine,
}

/// <summary>谱面结构展开出的最小判定单元。</summary>
public sealed class JudgeUnit
{
    public required double Time { get; init; }
    public required ScoreCategory Category { get; init; }
    public required Track Track { get; init; }
    public required int NoteId { get; init; }
    public required UnitKind Kind { get; init; }
    public bool Judged { get; set; }
}

/// <summary>一次判定的结果。</summary>
public readonly record struct JudgeResult(JudgeGrade Grade, HitTiming Timing, double DeltaSec);

/// <summary>
/// 判定引擎：窗口判定 + 连击 + 计分（§7.1）+ 血量（§7.2）+ Boost（§7.3）。
/// 纯逻辑，不依赖 Godot。
/// </summary>
public sealed class JudgeEngine
{
    // 血量上限 UNKNOWN（dump 仅见 get_PlayerMaxHealth 符号），MVP 取 1000。
    public const int DefaultMaxHealth = 1000;

    public JudgeSettings Settings { get; }
    public JudgePreset Preset { get; }

    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int Health { get; private set; }
    public int MaxHealth { get; }
    public int Boost { get; private set; }

    public int CountPrefect { get; private set; }
    public int CountGreat { get; private set; }
    public int CountGood { get; private set; }
    public int CountMiss { get; private set; }

    public bool GameOver => Health <= 0;

    public JudgeEngine(JudgePreset preset)
    {
        Preset = preset;
        Settings = JudgeSettings.ForPreset(preset);
        MaxHealth = DefaultMaxHealth;
        Health = MaxHealth; // 开局满血
    }

    /// <summary>
    /// 窗口判定：输入 (音符命中时刻, 实际输入时刻) → 等级 + Early/Late。
    /// 超出 Good 窗口（含超出 Miss 窗口）一律 Miss；是否需要拦截超窗输入
    /// 由调用方用 <see cref="InWindow"/> 判断。
    /// </summary>
    public JudgeResult Judge(double noteTime, double inputTime)
    {
        var delta = inputTime - noteTime;
        var abs = Math.Abs(delta);
        var grade =
            abs <= Settings.PrefectSec ? JudgeGrade.Prefect :
            abs <= Settings.GreatSec ? JudgeGrade.Great :
            abs <= Settings.GoodSec ? JudgeGrade.Good :
            JudgeGrade.Miss;
        var timing = abs < 1e-9 ? HitTiming.Exact : delta < 0 ? HitTiming.Early : HitTiming.Late;
        return new JudgeResult(grade, timing, delta);
    }

    /// <summary>输入是否落在可判定窗口（±Miss 窗口）内。</summary>
    public bool InWindow(double noteTime, double inputTime) =>
        Math.Abs(inputTime - noteTime) <= Settings.MissSec;

    /// <summary>应用一次判定：计分/血量/Boost/连击（Miss 断连）。</summary>
    public void Apply(ScoreCategory category, JudgeGrade grade)
    {
        Score += ScoreDelta(category, grade);
        Health = Math.Clamp(Health + HealthDelta(category, grade), 0, MaxHealth);
        Boost = Math.Max(0, Boost + BoostDelta(category, grade));

        switch (grade)
        {
            case JudgeGrade.Prefect: CountPrefect++; break;
            case JudgeGrade.Great: CountGreat++; break;
            case JudgeGrade.Good: CountGood++; break;
            default: CountMiss++; break;
        }

        if (grade == JudgeGrade.Miss)
            Combo = 0;
        else
        {
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;
        }
    }

    /// <summary>
    /// Mine 判定【TODO 规格书 §9#7 推测方向】：不触碰 = Prefect 给分，
    /// 误触 = Miss（扣分与最重血量惩罚，Hard -1500）。
    /// </summary>
    public void ApplyMine(bool touched) =>
        Apply(ScoreCategory.Mine, touched ? JudgeGrade.Miss : JudgeGrade.Prefect);

    /// <summary>结算百分比 = 得分 / 理论满分 × 100（§7.4 GetClearPercent100）。</summary>
    public double Percent(int theoreticalMax) =>
        theoreticalMax <= 0 ? 0.0 : 100.0 * Score / theoreticalMax;

    // ---- 数值表（规格书 §7，Score/Boost 全 4 档通用；Health 仅 Miss 惩罚分档）----

    public static int ScoreDelta(ScoreCategory c, JudgeGrade g) => c switch
    {
        ScoreCategory.Chain => g switch
        {
            JudgeGrade.Prefect => 50,
            JudgeGrade.Great => 35,
            JudgeGrade.Good => 25,
            _ => 0,
        },
        ScoreCategory.HoldHolding or ScoreCategory.MixerHolding =>
            g == JudgeGrade.Prefect ? 10 : 0,
        _ => g switch // Tap/Burst/Mine/HoldStart/HoldEnd/MixerStart/MixerEnd
        {
            JudgeGrade.Prefect => 100,
            JudgeGrade.Great => 70,
            JudgeGrade.Good => 50,
            _ => 0,
        },
    };

    public int HealthDelta(ScoreCategory c, JudgeGrade g)
    {
        var tutorial = Preset == JudgePreset.Tutorial;
        return c switch
        {
            ScoreCategory.Mine => g switch
            {
                JudgeGrade.Prefect => 20,
                JudgeGrade.Great => 15,
                JudgeGrade.Good => 10,
                _ => tutorial ? -200 : Preset == JudgePreset.Hard ? -1500 : -1000,
            },
            ScoreCategory.HoldHolding => g switch
            {
                JudgeGrade.Prefect => 5,
                JudgeGrade.Miss => tutorial ? -20 : -100,
                _ => 0,
            },
            ScoreCategory.MixerHolding => g switch
            {
                JudgeGrade.Prefect => 10,
                JudgeGrade.Miss => tutorial ? -20 : -100,
                _ => 0,
            },
            ScoreCategory.MixerEnd => g switch
            {
                JudgeGrade.Prefect => 20,
                JudgeGrade.Great => 10,
                JudgeGrade.Miss => tutorial ? -100 : -500,
                _ => 0,
            },
            _ => g switch // Tap/Burst/Chain/HoldStart/HoldEnd/MixerStart
            {
                JudgeGrade.Prefect => 20,
                JudgeGrade.Great => 15,
                JudgeGrade.Good => 10,
                _ => tutorial ? -100 : -500,
            },
        };
    }

    public static int BoostDelta(ScoreCategory c, JudgeGrade g) => c switch
    {
        ScoreCategory.Chain => g switch
        {
            JudgeGrade.Prefect => 50,
            JudgeGrade.Great => 25,
            JudgeGrade.Good => 12,
            _ => 0,
        },
        ScoreCategory.HoldHolding or ScoreCategory.MixerHolding =>
            g == JudgeGrade.Miss ? 0 : 15,
        _ => g switch
        {
            JudgeGrade.Prefect => 100,
            JudgeGrade.Great => 50,
            JudgeGrade.Good => 25,
            _ => 0,
        },
    };
}

/// <summary>
/// 由谱面结构展开判定单元序列并计算理论满分。
/// 规则：链头尾各计一次输入判定（§5.3 解读）、链体节点自动命中、
/// Hold/Mixer 的 Holding 判定点按 §6.2 0.125 bar（=200ms@150BPM）间隔生成。
/// </summary>
public static class JudgePlan
{
    public sealed class Plan
    {
        public required List<JudgeUnit> Units { get; init; }
        /// <summary>理论满分 = 全部单元 Prefect 的得分和。</summary>
        public required int TheoreticalMax { get; init; }
        public double EndTime => Units.Count == 0 ? 0 : Units[^1].Time;
    }

    public static Plan Build(DuxShared.Chart.Chart chart, JudgeSettings settings)
    {
        var units = new List<JudgeUnit>();

        foreach (var track in new[] { Track.Left, Track.Center, Track.Right })
        {
            var notes = chart.NotesOf(track);
            var byId = new Dictionary<int, Note>();
            foreach (var n in notes)
                byId.TryAdd(n.Id, n);

            bool HasNext(Note n) => n.SubNoteId != -1 && byId.ContainsKey(n.SubNoteId);

            // Mixer 起点列表（用于把 Type9 滑条段归到最近的起点；§8.2【推测】）
            var mixerStarts = notes.Where(n => n.Type == NoteType.MixerStart).ToList();

            foreach (var n in notes)
            {
                switch (n.Type)
                {
                    case NoteType.Tap:
                        units.Add(Unit(n, ScoreCategory.Tap, UnitKind.Input));
                        break;

                    case NoteType.Burst:
                        // TODO(§4【推测】)：Burst 可能用宽判定区/不同打击音，MVP 按 Tap 处理。
                        units.Add(Unit(n, ScoreCategory.Burst, UnitKind.Input));
                        break;

                    case NoteType.Mine:
                        units.Add(Unit(n, ScoreCategory.Mine, UnitKind.Mine));
                        break;

                    case NoteType.ChainHead:
                        // 链头计一次输入判定（§5.3：Chain 头计开始+结束两次）。
                        units.Add(Unit(n, ScoreCategory.Chain, UnitKind.Input));
                        break;

                    case NoteType.ChainNode:
                        // 链尾（无有效后继，含悬空容错 §5.1）计一次输入判定；
                        // 中间链体节点自动命中（§4【推测】划过即中，按 Chain 档计分）。
                        units.Add(Unit(n, ScoreCategory.Chain,
                            HasNext(n) ? UnitKind.Auto : UnitKind.Input));
                        break;

                    case NoteType.HoldHead:
                    {
                        units.Add(Unit(n, ScoreCategory.HoldStart, UnitKind.Input));
                        var end = WalkChainEnd(n, byId);
                        var endTime = end?.Second ?? n.Second;
                        if (endTime > n.Second)
                        {
                            AddHoldingPoints(units, n, ScoreCategory.HoldHolding,
                                n.Second, endTime, settings.HoldHoldingSec);
                            units.Add(new JudgeUnit
                            {
                                Time = endTime,
                                Category = ScoreCategory.HoldEnd,
                                Track = track,
                                NoteId = end!.Id,
                                Kind = UnitKind.Input,
                            });
                        }
                        // 悬空/无后继：只计 HoldStart（§5.1 容错）。
                        break;
                    }

                    case NoteType.HoldNode:
                        break; // 由 HoldHead 展开

                    case NoteType.MixerStart:
                    {
                        // TODO(§8.2【推测】/§9)：MVP 按 Hold 处理——把同轨后续 Type9
                        // （到下一个 MixerStart 之前）的最大时刻当作滑条结束。
                        units.Add(Unit(n, ScoreCategory.MixerStart, UnitKind.Input));
                        var nextStart = mixerStarts
                            .Where(m => m.Second > n.Second)
                            .Select(m => (double?)m.Second)
                            .DefaultIfEmpty(null)
                            .Min();
                        var endTime = notes
                            .Where(m => m.Type == NoteType.MixerNode &&
                                        m.Second >= n.Second &&
                                        (nextStart == null || m.Second < nextStart))
                            .Select(m => (double?)m.Second)
                            .DefaultIfEmpty(null)
                            .Max();
                        if (endTime is > 0 && endTime > n.Second)
                        {
                            AddHoldingPoints(units, n, ScoreCategory.MixerHolding,
                                n.Second, endTime.Value, settings.MixerHoldingSec);
                            units.Add(new JudgeUnit
                            {
                                Time = endTime.Value,
                                Category = ScoreCategory.MixerEnd,
                                Track = track,
                                NoteId = n.Id,
                                Kind = UnitKind.Input,
                            });
                        }
                        break;
                    }

                    case NoteType.MixerNode:
                        break; // 由 MixerStart 展开；孤立 Type9（无起点）忽略
                }
            }
        }

        units.Sort((a, b) => a.Time.CompareTo(b.Time));
        var max = units.Sum(u => JudgeEngine.ScoreDelta(u.Category, JudgeGrade.Prefect));
        return new Plan { Units = units, TheoreticalMax = max };
    }

    private static JudgeUnit Unit(Note n, ScoreCategory c, UnitKind k) => new()
    {
        Time = n.Second,
        Category = c,
        Track = n.Track,
        NoteId = n.Id,
        Kind = k,
    };

    /// <summary>沿 SubNoteId 走到链尾（悬空/环容错）。</summary>
    private static Note? WalkChainEnd(Note head, Dictionary<int, Note> byId)
    {
        Note? cur = null;
        var next = head.SubNoteId;
        var guard = 0;
        while (next != -1 && byId.TryGetValue(next, out var target) && guard++ < 10000)
        {
            cur = target;
            next = target.SubNoteId;
        }
        return cur;
    }

    /// <summary>在 (start, end) 开区间内按固定间隔生成 holding 判定点。</summary>
    private static void AddHoldingPoints(List<JudgeUnit> units, Note head,
        ScoreCategory category, double start, double end, double intervalSec)
    {
        for (var t = start + intervalSec; t < end - 1e-9; t += intervalSec)
        {
            units.Add(new JudgeUnit
            {
                Time = t,
                Category = category,
                Track = head.Track,
                NoteId = head.Id,
                Kind = UnitKind.HoldPoint,
            });
        }
    }
}
