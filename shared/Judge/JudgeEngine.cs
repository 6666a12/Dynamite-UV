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

/// <summary>Original internal result states, kept separately from the four visible grades.</summary>
public enum JudgeResolution
{
    Pending = 0,
    AutoMiss = 1,
    InputMiss = 2,
    Good = 3,
    Great = 4,
    Prefect = 5,
}

/// <summary>计分类别（规格书 §7.1 Score 表 10 类）。</summary>
public enum ScoreCategory
{
    Tap,
    Burst,  // §7.1 原名；现承载 Drag（T2 绿条）计分
    Chain,  // §7.1 原名；现行类型映射下不产生（保留以维持 §7 数值表完整）
    Mine,
    HoldStart,
    HoldEnd,
    HoldHolding,
    MixerStart,
    MixerEnd,
    MixerHolding,
}

/// <summary>判定单元种类：Input=需玩家输入；Auto=自动命中；HoldPoint=按住期间判定点；
/// Contact=接触判定（Drag：按住经过即中，不按即 Miss）；Mine=不碰给分。</summary>
public enum UnitKind
{
    Input,
    Auto,
    HoldPoint,
    HoldEnd,
    MixerEnd,
    Contact,
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
    /// <summary>Head note id for Hold/Mixer units; null for ordinary units.</summary>
    public int? SustainHeadId { get; init; }
    /// <summary>判定窗口倍率（EX-Tap &gt; 1，其余 1）。</summary>
    public double WindowScale { get; init; } = 1.0;
    /// <summary>是否改变 Combo；Holding tick 只结算持续得分，不改变主连击。</summary>
    public bool AffectsCombo { get; init; } = true;
    /// <summary>是否计入 P/Great/Good/Miss；Holding tick 不进入主判定统计。</summary>
    public bool AffectsJudgeCounts { get; init; } = true;
    public bool Judged { get; set; }
}

/// <summary>一次判定的结果。</summary>
public readonly record struct JudgeResult(
    JudgeGrade Grade, HitTiming Timing, double DeltaSec, JudgeResolution Resolution);

/// <summary>
/// 判定引擎：窗口判定 + 连击 + 计分（§7.1）+ 血量（§7.2）+ Boost（§7.3）。
/// 纯逻辑，不依赖 Godot。
/// </summary>
public sealed class JudgeEngine
{
    // 模式化上限仍未知；原版未取得模式值时的 fallback 为 10000。
    public const int DefaultMaxHealth = 10000;
    public const int MaxBoost = 3000;
    /// <summary>社区版统一满分。</summary>
    public const int NormalizedScoreMax = 1_000_000;

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
    public int CountAutoMiss { get; private set; }
    public int CountInputMiss { get; private set; }

    public bool GameOver => Health <= 0;

    private readonly int _totalMainNote;

    public JudgeEngine(JudgePreset preset, int totalMainNote = 0,
        int maxHealth = DefaultMaxHealth)
    {
        Preset = preset;
        Settings = JudgeSettings.ForPreset(preset);
        _totalMainNote = Math.Max(0, totalMainNote);
        MaxHealth = Math.Max(1, maxHealth);
        Health = MaxHealth; // 开局满血
    }

    /// <summary>
    /// 窗口判定：输入 (音符命中时刻, 实际输入时刻) → 等级 + Early/Late。
    /// 超出 Good 窗口（含超出 Miss 窗口）一律 Miss；是否需要拦截超窗输入
    /// 由调用方用 <see cref="InWindow"/> 判断。
    /// windowScale 用于 EX-Tap 等宽窗口类型（各窗口同比放大）。
    /// </summary>
    public JudgeResult Judge(double noteTime, double inputTime, double windowScale = 1.0)
    {
        var delta = inputTime - noteTime;
        var abs = Math.Abs(delta);
        var grade =
            abs <= Settings.PrefectSec * windowScale ? JudgeGrade.Prefect :
            abs <= Settings.GreatSec * windowScale ? JudgeGrade.Great :
            abs <= Settings.GoodSec * windowScale ? JudgeGrade.Good :
            JudgeGrade.Miss;
        var timing = abs < 1e-9 ? HitTiming.Exact : delta < 0 ? HitTiming.Early : HitTiming.Late;
        var resolution = grade switch
        {
            JudgeGrade.Prefect => JudgeResolution.Prefect,
            JudgeGrade.Great => JudgeResolution.Great,
            JudgeGrade.Good => JudgeResolution.Good,
            _ => JudgeResolution.InputMiss,
        };
        return new JudgeResult(grade, timing, delta, resolution);
    }

    /// <summary>输入是否落在可判定窗口（±Miss 窗口）内。</summary>
    public bool InWindow(double noteTime, double inputTime, double windowScale = 1.0) =>
        Math.Abs(inputTime - noteTime) <= Settings.MissSec * windowScale;

    /// <summary>
    /// 应用一次判定。所有单元都结算分数/血量/Boost；持续 tick 可通过标志排除在
    /// 主 Combo 与 P/GR/GD/M 统计之外。
    /// </summary>
    public void Apply(ScoreCategory category, JudgeGrade grade,
        bool affectsCombo = true, bool affectsJudgeCounts = true,
        JudgeResolution? resolution = null)
    {
        Score += ScoreDelta(category, grade);
        Health = Math.Clamp(Health + HealthDelta(category, grade), 0, MaxHealth);
        Boost = Math.Clamp(Boost + BoostDelta(category, grade), 0, MaxBoost);

        if (grade == JudgeGrade.Miss)
        {
            if (resolution == JudgeResolution.AutoMiss)
                CountAutoMiss++;
            else
                CountInputMiss++;
        }

        if (affectsJudgeCounts)
        {
            switch (grade)
            {
                case JudgeGrade.Prefect: CountPrefect++; break;
                case JudgeGrade.Great: CountGreat++; break;
                case JudgeGrade.Good: CountGood++; break;
                default: CountMiss++; break;
            }
        }

        if (affectsCombo)
        {
            if (grade == JudgeGrade.Miss)
                Combo = 0;
            else
            {
                Combo++;
                if (Combo > MaxCombo) MaxCombo = Combo;
            }
        }
    }

    /// <summary>
    /// Mine 判定：到点前未触发 = Prefect；危险窗内接触 = InputMiss。
    /// </summary>
    public void ApplyMine(bool touched) =>
        Apply(ScoreCategory.Mine, touched ? JudgeGrade.Miss : JudgeGrade.Prefect,
            resolution: touched ? JudgeResolution.InputMiss : JudgeResolution.Prefect);

    /// <summary>社区版 CLEAR = 当前基础得分 / 理论满分 × 100。</summary>
    public double Percent(int theoreticalMax) =>
        theoreticalMax <= 0 ? 0.0 : 100.0 * Score / theoreticalMax;

    /// <summary>
    /// 社区版显示/存档分数：原始加权得分按该谱理论满分归一化到 0..1,000,000。
    /// 使用整数四舍五入，并钳制上限，保证全 Prefect 严格等于 1,000,000。
    /// </summary>
    public int NormalizedScore(int theoreticalMax)
    {
        if (theoreticalMax <= 0)
            return 0;
        var scaled = (long)Score * NormalizedScoreMax;
        var rounded = (scaled + theoreticalMax / 2L) / theoreticalMax;
        return (int)Math.Clamp(rounded, 0L, NormalizedScoreMax);
    }

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
        var assetValue = c switch
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
        return _totalMainNote <= 0
            ? assetValue
            : (int)Math.Floor(assetValue * 600.0 / _totalMainNote);
    }

    public int BoostDelta(ScoreCategory c, JudgeGrade g)
    {
        var assetValue = c switch
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
        return _totalMainNote <= 0
            ? assetValue
            : (int)Math.Floor(assetValue * 100.0 / _totalMainNote);
    }
}

/// <summary>
/// 由谱面结构展开判定单元序列并计算理论满分。
/// 规则（用户权威映射，规格书 §4）：Tap/EX-Tap 各计一次输入（EX-Tap 窗口放宽）；
/// Drag（T2 绿条）接触判定（按住经过即中）；Hold/Mixer 头计一次输入 +
/// Holding 判定点按 0.125 bar 生成（当前 BPM clamp 120–200）+ 自动尾判；
/// Mine 不碰给分；BarLine（T9）纯视觉，不产生判定单元。
/// </summary>
public static class JudgePlan
{
    /// <summary>EX-Tap 判定窗口倍率（用户拍板"更宽松"，具体数值可调）。</summary>
    public const double ExTapWindowScale = 1.5;

    public sealed class Plan
    {
        public required List<JudgeUnit> Units { get; init; }
        public required IReadOnlyDictionary<int, SustainPath> Sustains { get; init; }
        /// <summary>理论满分 = 全部单元 Prefect 的得分和。</summary>
        public required int TheoreticalMax { get; init; }
        /// <summary>主判定总数；与谱面 Note 数、满 Combo、P/GR/GD/M 总和同口径。</summary>
        public required int HeadlineUnitCount { get; init; }
        public double EndTime => Units.Count == 0 ? 0 : Units[^1].Time;
    }

    public static Plan Build(DuxShared.Chart.Chart chart, JudgeSettings settings)
    {
        var units = new List<JudgeUnit>();
        var sustains = new Dictionary<int, SustainPath>();

        foreach (var track in new[] { Track.Left, Track.Center, Track.Right })
        {
            var notes = chart.NotesOf(track);
            var byId = new Dictionary<int, Note>();
            foreach (var n in notes)
                byId.TryAdd(n.Id, n);

            foreach (var n in notes)
            {
                switch (n.Type)
                {
                    case NoteType.Tap:
                        units.Add(Unit(n, ScoreCategory.Tap, UnitKind.Input));
                        break;

                    case NoteType.Drag:
                        // 绿条：接触判定——按住经过即中，不按即 Miss。
                        units.Add(Unit(n, ScoreCategory.Burst, UnitKind.Contact));
                        break;

                    case NoteType.ExTap:
                        // 蓝 EX-Tap：判定窗口更宽松（ExTapWindowScale 倍）。
                        units.Add(Unit(n, ScoreCategory.Tap, UnitKind.Input,
                            ExTapWindowScale));
                        break;

                    case NoteType.Mine:
                        units.Add(Unit(n, ScoreCategory.Mine, UnitKind.Mine));
                        break;

                    case NoteType.HoldHead:
                    {
                        var path = BuildSustainPath(n, byId, SustainKind.Hold);
                        units.Add(Unit(n, ScoreCategory.HoldStart, UnitKind.Input,
                            sustainHeadId: n.Id));
                        if (path != null)
                        {
                            sustains[n.Id] = path;
                            AddHoldingPoints(units, chart, path, ScoreCategory.HoldHolding,
                                settings.HoldHoldingJudgeBarTime, settings.HoldHoldingSec);
                            units.Add(new JudgeUnit
                            {
                                Time = path.EndTime,
                                Category = ScoreCategory.HoldEnd,
                                Track = track,
                                NoteId = path.End.Id,
                                Kind = UnitKind.HoldEnd,
                                SustainHeadId = n.Id,
                            });
                        }
                        // 悬空/无后继：只计 HoldStart（§5.1 容错）。
                        break;
                    }

                    case NoteType.HoldNode:
                        break; // 由 HoldHead 展开

                    case NoteType.MixerHead:
                    {
                        var path = BuildSustainPath(n, byId, SustainKind.Mixer);
                        units.Add(Unit(n, ScoreCategory.MixerStart, UnitKind.Input,
                            sustainHeadId: n.Id));
                        if (path != null)
                        {
                            sustains[n.Id] = path;
                            AddHoldingPoints(units, chart, path, ScoreCategory.MixerHolding,
                                settings.MixerHoldingJudgeBarTime, settings.MixerHoldingSec);
                            units.Add(new JudgeUnit
                            {
                                Time = path.EndTime,
                                Category = ScoreCategory.MixerEnd,
                                Track = track,
                                NoteId = path.End.Id,
                                Kind = UnitKind.MixerEnd,
                                SustainHeadId = n.Id,
                            });
                        }
                        break;
                    }

                    case NoteType.MixerNode:
                        break; // 由 MixerHead 展开

                    case NoteType.BarLine:
                        break; // 小节线：纯视觉，不计分不计 combo
                }
            }
        }

        units.Sort((a, b) => a.Time.CompareTo(b.Time));
        var max = units.Sum(u => JudgeEngine.ScoreDelta(u.Category, JudgeGrade.Prefect));
        var headlineCount = units.Count(u => u.AffectsJudgeCounts);
        return new Plan
        {
            Units = units,
            Sustains = sustains,
            TheoreticalMax = max,
            HeadlineUnitCount = headlineCount,
        };
    }

    private static JudgeUnit Unit(Note n, ScoreCategory c, UnitKind k,
        double windowScale = 1.0, int? sustainHeadId = null) => new()
    {
        Time = n.Second,
        Category = c,
        Track = n.Track,
        NoteId = n.Id,
        Kind = k,
        WindowScale = windowScale,
        SustainHeadId = sustainHeadId,
    };

    /// <summary>沿 SubNoteId 构建时间有序的 sustain 路径（悬空/环容错）。</summary>
    private static SustainPath? BuildSustainPath(Note head, Dictionary<int, Note> byId,
        SustainKind kind)
    {
        var nodes = new List<Note> { head };
        var next = head.SubNoteId;
        var guard = 0;
        var seen = new HashSet<int> { head.Id };
        while (next != -1 && byId.TryGetValue(next, out var target) && guard++ < 10000)
        {
            if (!seen.Add(target.Id))
                break;
            nodes.Add(target);
            next = target.SubNoteId;
        }
        if (nodes.Count < 2 || nodes[^1].Second <= head.Second)
            return null;
        return new SustainPath
        {
            Kind = kind,
            HeadId = head.Id,
            Track = head.Track,
            Nodes = nodes,
        };
    }

    /// <summary>在开区间内按 0.125 bar 生成 tick；空时间线才回退固定秒间隔。</summary>
    private static void AddHoldingPoints(List<JudgeUnit> units, DuxShared.Chart.Chart chart,
        SustainPath path, ScoreCategory category, double intervalBar, double fallbackIntervalSec)
    {
        if (chart.Sections.Count > 0 && path.End.BarTime > path.Head.BarTime)
        {
            for (var bar = path.Head.BarTime + intervalBar;
                 bar < path.End.BarTime - 1e-9; bar += intervalBar)
            {
                units.Add(HoldingUnit(chart.BarTimeToSeconds(bar), path, category));
            }
            return;
        }

        for (var t = path.StartTime + fallbackIntervalSec;
             t < path.EndTime - 1e-9; t += fallbackIntervalSec)
            units.Add(HoldingUnit(t, path, category));
    }

    private static JudgeUnit HoldingUnit(double time, SustainPath path,
        ScoreCategory category) => new()
    {
        Time = time,
        Category = category,
        Track = path.Track,
        NoteId = path.HeadId,
        Kind = UnitKind.HoldPoint,
        SustainHeadId = path.HeadId,
        AffectsCombo = false,
        AffectsJudgeCounts = false,
    };
}
