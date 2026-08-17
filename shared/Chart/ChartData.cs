namespace DuxShared.Chart;

/// <summary>三条判定轨（规格书 §8.1 NoteRegion_Left/Center/Right）。</summary>
public enum Track
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>音符类型 1–9（用户权威映射，规格书 §4）：
/// 1 蓝 Tap；2 绿 Drag（接触判定：按住经过即中）；3/4 红 Hold 头/体节点；
/// 5 蓝 EX-Tap（判定窗口更宽松）；6/7 粉 Mixer 头/体节点；
/// 8 地雷（勿触）；9 小节线（纯视觉，不计分不计 combo）。</summary>
public enum NoteType
{
    Tap = 1,
    Drag = 2,
    HoldHead = 3,
    HoldNode = 4,   // 长条体/尾节点（SubNoteId 链接）
    ExTap = 5,
    MixerHead = 6,  // Mixer 头（SubNoteId 链接粉缎带）
    MixerNode = 7,  // Mixer 体/尾节点
    Mine = 8,
    BarLine = 9,
}

/// <summary>单个音符（规格书 §1.2）。Second 为权威命中秒。</summary>
public sealed class Note
{
    public required int Id { get; init; }
    public required int SubNoteId { get; init; }
    public required NoteType Type { get; init; }
    public required Track Track { get; init; }
    /// <summary>小节时间（1 bar = 4 拍）。</summary>
    public required double BarTime { get; init; }
    /// <summary>轨内 NS 坐标位置（基准点 UNKNOWN，规格书 §9#6，暂按中心处理）。</summary>
    public required double Position { get; init; }
    /// <summary>NS 单位宽度。</summary>
    public required double Width { get; init; }
    /// <summary>权威命中秒：有时间线时由 §3.2 公式换算，否则回退 BakedSecond。</summary>
    public required double Second { get; set; }
    /// <summary>JSON 内烘焙的原始命中秒（仅作校验/回退用）。</summary>
    public required double BakedSecond { get; init; }
    /// <summary>Baked_SyncNote：非零 ⟺ 同 BarTime 他轨有按下型 note（T1/T3/T6），
    /// 原版渲染为金色描边（金框青芯，视频实证，规格书 §4）。</summary>
    public int SyncNote { get; init; }

    /// <summary>
    /// Optional v2 source metadata. Legacy loaders leave this null; the v2 adapter uses it to retain
    /// exact time, stable string identity, path curve, and Hold shape-only judgement state.
    /// </summary>
    public V2.V2RuntimeNoteMetadata? V2Metadata { get; init; }
}

/// <summary>BPM 时间线段（规格书 §3.1）。</summary>
public sealed class BarSection
{
    public required double Bpm { get; init; }
    public required double BarTime { get; init; }
    public required double Seconds { get; init; }
}

/// <summary>谱面数据。</summary>
public sealed class Chart
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    /// <summary>难度号（Casual=1 … Hard=3 … Tutorial=16，规格书 §2）。</summary>
    public required int Difficulty { get; init; }
    public required int TotalMainNote { get; init; }
    public required IReadOnlyList<BarSection> Sections { get; init; }
    public required IReadOnlyList<Note> NotesLeft { get; init; }
    public required IReadOnlyList<Note> NotesCenter { get; init; }
    public required IReadOnlyList<Note> NotesRight { get; init; }

    /// <summary>变速事件（NoteSystem__DropSpeeds，规格书 §2.2）：BarTime→速度倍率，
    /// <b>相邻事件间线性插值</b>（视频逐帧验证，非阶跃）。
    /// 仅影响视觉下落速度，命中秒不变。</summary>
    public IReadOnlyList<(double BarTime, double Mult)> DropSpeeds { get; init; } = [];

    /// <summary>
    /// Optional v2 chart metadata and exact evaluators. Legacy charts leave this null, preserving
    /// the established loader and judge-plan API.
    /// </summary>
    public V2.V2RuntimeChartMetadata? V2Metadata { get; init; }

    public IEnumerable<Note> AllNotes =>
        NotesLeft.Concat(NotesCenter).Concat(NotesRight);

    public IReadOnlyList<Note> NotesOf(Track track) => track switch
    {
        Track.Left => NotesLeft,
        Track.Center => NotesCenter,
        _ => NotesRight,
    };

    /// <summary>
    /// BarTime → 秒，规格书 §3.2 已验证公式：1 bar = 240/BPM 秒，分段递推。
    /// 时间线为空时（§1.3，仅 3/350 谱）无法换算，调用方应直接用 Note.Second。
    /// </summary>
    public double BarTimeToSeconds(double bar)
    {
        if (Sections.Count == 0)
            throw new InvalidOperationException("空 BPM 时间线，无法换算 BarTime（规格书 §1.3）。");

        var i = 0;
        for (var k = 0; k < Sections.Count; k++)
        {
            if (Sections[k].BarTime <= bar)
                i = k;
            else
                break;
        }
        var s = Sections[i];
        return s.Seconds + (bar - s.BarTime) * 240.0 / s.Bpm;
    }

    /// <summary>
    /// 秒 -> BarTime，与 BarTimeToSeconds 使用同一组 BPM 分段。
    /// 时间线为空时调用方应按自身数据约定回退。
    /// </summary>
    public double SecondsToBarTime(double second)
    {
        if (Sections.Count == 0)
            throw new InvalidOperationException();

        var i = 0;
        for (var k = 0; k < Sections.Count; k++)
        {
            if (Sections[k].Seconds <= second)
                i = k;
            else
                break;
        }
        var s = Sections[i];
        return s.BarTime + (second - s.Seconds) * s.Bpm / 240.0;
    }

    /// <summary>Returns the BPM active at the supplied bar position.</summary>
    public double BpmAtBarTime(double bar)
    {
        if (Sections.Count == 0)
            return 150.0;
        var i = 0;
        for (var k = 0; k < Sections.Count; k++)
        {
            if (Sections[k].BarTime <= bar)
                i = k;
            else
                break;
        }
        return Sections[i].Bpm;
    }

    /// <summary>Returns the BPM active at the supplied playback second.</summary>
    public double BpmAtSeconds(double second)
    {
        if (Sections.Count == 0)
            return 150.0;
        var i = 0;
        for (var k = 0; k < Sections.Count; k++)
        {
            if (Sections[k].Seconds <= second)
                i = k;
            else
                break;
        }
        return Sections[i].Bpm;
    }
}
