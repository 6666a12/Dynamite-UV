using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;
using static DynamiteUniverse.Game.GameplayRenderGeometry;

namespace DynamiteUniverse.Game;

/// <summary>A value snapshot of the judge-owned sustain state, read at the original call sites.</summary>
internal readonly record struct SustainVisualSnapshot(SustainPath Path, bool StartResolved,
    bool Holding, bool Broken, bool EndResolved, double LastContactUpdateTime);

/// <summary>
/// Owns note/link/residual/fade views. The scene owns judgement and supplies read-only sustain
/// snapshots. Nodes stay parented to the original stage layer so tree order and teardown stay intact.
/// </summary>
internal sealed partial class GameplayNoteRenderer
{
    private readonly Node2D _noteRoot;
    private readonly LoadedChart _loaded;
    private readonly DynamiteUniverse.Shared.Chart.Chart _chart;
    private readonly GameSettings _settings;
    private readonly IReadOnlyList<Note> _notesByTime;
    private readonly IReadOnlyDictionary<int, Note> _noteById;
    private readonly IReadOnlyDictionary<int, double> _runtimeNoteSeconds;
    private readonly IReadOnlyDictionary<int, SustainPath> _paths;
    private readonly Func<int, SustainVisualSnapshot?> _readSustain;
    private readonly GameplayEffects _effects;

    internal GameplayNoteRenderer(Node2D noteRoot, LoadedChart loaded, GameSettings settings,
        IReadOnlyList<Note> notes, IReadOnlyDictionary<int, Note> noteById,
        IReadOnlyDictionary<int, double> runtimeNoteSeconds, IReadOnlyDictionary<int, SustainPath> paths,
        VisualPoolPlanning.PeakCounts peaks, Func<int, SustainVisualSnapshot?> readSustain)
    {
        _noteRoot = noteRoot;
        _loaded = loaded;
        _chart = loaded.RuntimeChart;
        _settings = settings;
        _notesByTime = notes;
        _noteById = noteById;
        _runtimeNoteSeconds = runtimeNoteSeconds;
        _paths = paths;
        _readSustain = readSustain;
        _effects = new(noteRoot, settings);
        foreach (var path in paths.Values)
            if (path.Kind == SustainKind.Hold) _holdTailIds.Add(path.End.Id);
        _fallSpeedMultiplier = (float)settings.FallSpeedMultiplier;
        PrepareVisualPools(peaks);
        _dropSpeedMap = new DropSpeedMap(_chart.DropSpeeds);
        _usesDifferentialScroll = _notesByTime.Any(note =>
            note.V2Metadata?.ScrollSpeedMultiplier is { } multiplier && multiplier != 1.0);
        if (_usesDifferentialScroll)
            _differentialSpawnNotes.AddRange(_notesByTime);
    }

    internal IReadOnlyList<NoteView> ActiveViews => _active;
    private int _nextSpawn;
    private readonly bool _usesDifferentialScroll;
    private readonly List<Note> _differentialSpawnNotes = new();
    private readonly List<NoteView> _active = new();
    private readonly List<NoteView> _activeResiduals = new();
    private readonly Dictionary<int, NoteView> _viewByNoteId = new();
    private readonly Dictionary<NoteType, Stack<NoteView>> _noteViewPools = new();
    private readonly Dictionary<bool, Stack<NoteLink>> _linkPools = new();
    private readonly HashSet<int> _holdTailIds = new();
    private readonly HashSet<int> _failedHoldNoteIds = new();
    private const float HoldMissFadeDurationSec = 0.18f;
    private readonly List<FadingHoldView> _fadingHoldViews = new();

    // 同一帧所有 note 都使用当前 BarTime 采样的流速，但移动距离以音频秒计算：
    // visualDistancePx = (noteSecond - currentSecond) * speed(currentBar)
    //     * playerScale * noteScrollSpeedMultiplier * BaseFallSpeedPx。
    // 该距离可随流速变化而增加，因此已生成 note 必须允许回退出屏并继续保留。
    private DropSpeedMap _dropSpeedMap = DropSpeedMap.Empty;
    private float _fallSpeedMultiplier = 1f;

    // 链/长条连接体（SubNoteId 链接的可视化）：梯形面板段。Hold/Chain 为刚性
    // 形状整体下落，过线部分被判定线裁剪（连续吞噬），见 UpdateViews/ClipToLine
    private sealed class NoteLink
    {
        public int FromId;
        public int ToId;
        public Track Track;
        public float Wa; // 起点半宽（Center=面板半宽，侧轨=缎带半厚）
        public float Wb; // 终点半宽
        public bool IsHold;
        public Color BaseColor;
        public Polygon2D Poly = null!;
        public Line2D? Frame; // Hold 面板的亮描边轮廓（实机面板边缘亮条）
        public Vector2[] PolyScratch = new Vector2[4]; // 复用缓冲，避免每帧 new
        public Vector2[] FrameScratch = new Vector2[5];
    }
    private readonly List<NoteLink> _links = new();

    private sealed class FadingHoldView
    {
        public required Node2D Root;
        public float RemainingSec = HoldMissFadeDurationSec;
        public readonly List<NoteView> Notes = new();
        public readonly List<NoteLink> Links = new();
    }

    private void PrepareVisualPools(VisualPoolPlanning.PeakCounts peaks)
    {
        var activities = new List<VisualPoolPlanning.TypedActivity>();
        foreach (var note in _notesByTime.Where(ShouldRenderStaticNote))
        {
            var lead = (note.Track == Track.Center ? TravelPx : SideLeadPx) /
                (BaseFallSpeedPx * _fallSpeedMultiplier *
                 (note.V2Metadata?.ScrollSpeedMultiplier ?? 1.0));
            var end = note.Second + PostHitViewLifetimeSec;
            if (_paths.TryGetValue(note.Id, out var sustain))
                end = sustain.EndTime + PostHitViewLifetimeSec;
            activities.Add(new(note.Type, new(note.Second - lead, end)));
        }
        var typedPeaks = VisualPoolPlanning.PeaksByType(activities);
        foreach (var pair in typedPeaks)
        {
            var pool = new Stack<NoteView>();
            _noteViewPools[pair.Key] = pool;
            var model = _notesByTime.First(note => VisualPoolPlanning.PoolType(note.Type) == pair.Key);
            for (var i = 0; i < VisualPoolPlanning.ClampCapacity(pair.Value); i++)
            {
                var view = NoteView.Create(model, SizeFor(model), ColorFor(model),
                    brightness: ViewBrightness);
                _noteRoot.AddChild(view);
                view.ResetForPool();
                pool.Push(view);
            }
        }
        _effects.PrepareHits(peaks.HitCapacity);
        foreach (var isHold in new[] { false, true })
        {
            var pool = new Stack<NoteLink>();
            _linkPools[isHold] = pool;
            for (var i = 0; i < peaks.NoteCapacity; i++)
                pool.Push(CreateLink(isHold));
        }
        _effects.PrepareSustains(peaks.SustainCapacity, _paths.Values);
        GD.Print($"visual pools hits={peaks.PeakHits}/{peaks.HitCapacity} " +
            $"notes={peaks.PeakNotes}/{peaks.NoteCapacity} " +
            $"sustains={peaks.PeakSustains}/{peaks.SustainCapacity}");
    }

    /// <summary>音符亮度设置（100 = 设计原值，默认 130）。</summary>
    private float ViewBrightness => _settings.NoteBrightnessScale;

    // ---- 音符生成与移动 ----

    internal void SpawnNotes(double t, double currentBar)
    {
        if (_usesDifferentialScroll)
        {
            for (var index = 0; index < _differentialSpawnNotes.Count;)
            {
                var note = _differentialSpawnNotes[index];
                var threshold = note.Track == Track.Center ? TravelPx : SideLeadPx;
                if (VisualDistanceFor(note, t, currentBar) > threshold)
                {
                    index++;
                    continue;
                }
                SpawnNote(note, t, currentBar);
                _differentialSpawnNotes.RemoveAt(index);
            }
            return;
        }

        while (_nextSpawn < _notesByTime.Count)
        {
            var peek = _notesByTime[_nextSpawn];
            // 侧轨流速 = 行程比，生成提前量与正面相同（SideLeadPx == TravelPx），同刻入场
            var threshold = peek.Track == Track.Center ? TravelPx : SideLeadPx;
            if (VisualDistanceFor(peek, t, currentBar) > threshold)
                break;
            SpawnNote(_notesByTime[_nextSpawn++], t, currentBar);
        }
    }

    private void SpawnNote(Note n, double t, double currentBar)
    {
        if (_failedHoldNoteIds.Contains(n.Id))
            return;
        if (ShouldRenderStaticNote(n))
        {
            // TakeNoteView 已负责把视图挂到 _noteRoot（新建/归位），这里不再 AddChild。
            var view = TakeNoteView(n);

            view.Position = PositionFor(n, t, currentBar);
            view.SetDepthAlpha(n.Track == Track.Center ? TopFadeAlpha(view.Position.Y) : 1f);
            _viewByNoteId[n.Id] = view;
            _active.Add(view);
        }
        if (n.SubNoteId != -1 && _noteById.TryGetValue(n.SubNoteId, out var toNote))
        {
            var isHold = n.Type is NoteType.HoldHead or NoteType.HoldNode;
            var pool = _linkPools[isHold];
            // 防御：历史版本曾把挂在 fadeRoot 上的连接体入池后被 QueueFree；
            // 弹到死节点时丢弃换新，不让坏视图进场。
            NoteLink? link = null;
            while (pool.Count > 0)
            {
                var candidate = pool.Pop();
                if (GodotObject.IsInstanceValid(candidate.Poly))
                {
                    link = candidate;
                    break;
                }
            }
            link ??= CreateLink(isHold);
            link.FromId = n.Id;
            link.ToId = n.SubNoteId;
            link.Track = n.Track;
            link.Wa = LinkHalfFor(n);
            link.Wb = LinkHalfFor(toNote);
            link.BaseColor = LinkColorFor(n);
            link.Poly.Color = link.BaseColor;
            if (link.Frame != null) _noteRoot.MoveChild(link.Frame, -1);
            _noteRoot.MoveChild(link.Poly, -1);
            _links.Add(link);
        }
    }

    private NoteLink CreateLink(bool isHold)
    {
        var poly = new Polygon2D { Visible = false, ZIndex = 0 };
        var frame = isHold ? new Line2D
        {
            Width = 3f,
            DefaultColor = new Color(NoteVisualSpec.Hold, 0.9f),
            Visible = false,
            ZIndex = 0,
        } : null;
        _noteRoot.AddChild(poly);
        if (frame != null) _noteRoot.AddChild(frame);
        return new NoteLink { IsHold = isHold, Poly = poly, Frame = frame };
    }

    private void ReleaseLink(NoteLink link)
    {
        // 入池前必须归位：断触退场时 Poly/Frame 被挂在 fadeRoot 下，
        // fadeRoot 随后 QueueFree，不归位会把已释放节点留在池里。
        if (link.Poly.GetParent() != _noteRoot)
            link.Poly.Reparent(_noteRoot);
        if (link.Frame != null && link.Frame.GetParent() != _noteRoot)
            link.Frame.Reparent(_noteRoot);
        link.FromId = 0;
        link.ToId = 0;
        link.Track = Track.Center;
        link.Wa = 0f;
        link.Wb = 0f;
        link.BaseColor = Colors.Transparent;
        link.Poly.Visible = false;
        link.Poly.Color = Colors.Transparent;
        link.Poly.Polygon = Array.Empty<Vector2>();
        if (link.Frame != null)
        {
            link.Frame.Visible = false;
            link.Frame.DefaultColor = Colors.Transparent;
            link.Frame.Points = Array.Empty<Vector2>();
        }
        _linkPools[link.IsHold].Push(link);
    }

    private bool ShouldRenderStaticNote(Note note) => note.Type switch
    {
        NoteType.HoldNode => _holdTailIds.Contains(note.Id),
        NoteType.MixerNode => false,
        _ => true,
    };

    internal void UpdateViews(double t, double currentBar, double delta)
    {
        _effects.RecycleExpired();
        UpdateResiduals(delta);
        UpdateHoldMissFades(t, currentBar, delta);
        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var v = _active[i];
            var recycle = v.TickTtl(delta) ||
                (v.Model.Type == NoteType.BarLine && t >= v.Model.Second);
            var startedFallthrough = false;
            if (!v.IsResolved && !v.IsMissFalling &&
                UsesOrdinaryMissFallthrough(v.Model.Type))
            {
                var remaining = VisualDistanceFor(v.Model, t, currentBar);
                if (remaining < 0f)
                {
                    v.BeginMissFallthrough(
                        PastLineDistanceFromRemaining(v.Model.Track, remaining));
                    startedFallthrough = true;
                    recycle |= v.MissDistancePx >= 64f;
                }
            }
            if (v.IsMissFalling || v.IsRecoverableHoldFalling)
            {
                if (!startedFallthrough)
                {
                    var step = MissFallSpeedPx(v.Model, t, currentBar) * (float)delta;
                    recycle |= v.AdvanceMissFallthrough(step);
                }
            }
            else if (!(v.IsLineAnchored && v.Model.Type == NoteType.HoldHead))
                recycle |= t - v.Model.Second > PostHitViewLifetimeSec;

            if (recycle)
            {
                _viewByNoteId.Remove(v.Model.Id);
                ReturnNoteView(v);
                _active.RemoveAt(i);
                continue;
            }
            // 不按屏幕边界回收：变速期间 note 可能先退出画面，随后再次进入。
            // 已接起的 Hold 由当前路径更新位置和宽度，结束后保留尾端位置淡出。
            if (v.Model.Type != NoteType.HoldHead || !v.IsResolved ||
                !_paths.ContainsKey(v.Model.Id))
                v.Position = v.IsMissFalling || v.IsRecoverableHoldFalling
                    ? PositionPastLine(v.Model, v.MissDistancePx)
                    : v.IsLineAnchored
                        ? PositionAt(v.Model, 0f)
                        : PositionForActiveView(v.Model, t, currentBar);
            v.SetDepthAlpha(v.IsMissFalling || v.IsRecoverableHoldFalling ||
                v.Model.Track != Track.Center
                ? 1f : TopFadeAlpha(v.Position.Y));
        }

        // 更新链/长条连接体：Hold/Chain 是**刚性形状**整体对着判定线下落
        // （节点只是形状顶点，端点位置不钳制）；过线部分被判定线裁剪，
        // 裁剪点沿线连续滑动＝连续吞噬；整段过线后销毁。全程可见，
        // 不要求节点视图在场。
        for (var i = _links.Count - 1; i >= 0; i--)
        {
            var l = _links[i];
            if (UpdateLinkView(l, t, currentBar))
                continue;
            ReleaseLink(l);
            _links.RemoveAt(i);
        }
    }

    private bool UpdateLinkView(NoteLink link, double t, double currentBar,
        bool updateAppearance = true)
    {
        var from = _noteById[link.FromId];
        var to = _noteById[link.ToId];
        var rA = VisualDistanceFor(from, t, currentBar);
        var rB = VisualDistanceFor(to, t, currentBar);
        var a = PositionAt(from, rA);
        var b = PositionAt(to, rB);
        var wa = link.Wa;
        var wb = link.Wb;
        var anchoredEarlyHold = IsEarlyHoldAnchored(link, rA);
        if (anchoredEarlyHold)
            a = PositionAt(from, 0f);
        if (!ClipToLine(link.Track, ref a, ref b, ref wa, ref wb))
            return false;
        if (link.PolyScratch.Length < 4)
            link.PolyScratch = new Vector2[4];
        GameplayVisualMapper.WriteLinkPolygon(link.Track, a, b, wa, wb, link.PolyScratch);
        link.Poly.Polygon = link.PolyScratch;
        if (link.Frame != null)
        {
            if (link.FrameScratch.Length < 5)
                link.FrameScratch = new Vector2[5];
            GameplayVisualMapper.WriteLinkFrame(link.Track, a, b, wa, wb, link.FrameScratch);
            link.Frame.Points = link.FrameScratch;
        }
        if (!updateAppearance)
            return true;

        var proximity = 1f;
        if (link.Track != Track.Center)
        {
            var nearestRem = anchoredEarlyHold
                ? 0f : Mathf.Max(0f, Mathf.Min(rA, rB));
            proximity = 1f - Mathf.Clamp(nearestRem / SideLeadPx, 0f, 1f);
        }
        var fillAlpha = link.BaseColor.A;
        if (link.Track == Track.Center)
        {
            // 以靠近判定线的一端控制渐入，避免长 Hold 的远端让整条 Body 不可见。
            fillAlpha *= TopFadeAlpha(Mathf.Max(a.Y, b.Y));
        }
        else if (link.IsHold)
            fillAlpha = Mathf.Lerp(0.08f, 0.28f, proximity);
        link.Poly.Color = new Color(link.BaseColor, fillAlpha);
        link.Poly.Visible = true;
        if (link.Frame != null)
        {
            var frameAlpha = link.Track == Track.Center
                ? 0.9f * TopFadeAlpha(Mathf.Max(a.Y, b.Y))
                : Mathf.Lerp(0.25f, 0.9f, proximity);
            link.Frame.DefaultColor = new Color(NoteVisualSpec.Hold, frameAlpha);
            link.Frame.Visible = true;
        }
        return true;
    }

    private void UpdateHoldMissFades(double t, double currentBar, double delta)
    {
        // 跟随 gameplay 帧推进，因此暂停时也冻结退场动画。
        for (var i = _fadingHoldViews.Count - 1; i >= 0; i--)
        {
            var fading = _fadingHoldViews[i];
            fading.RemainingSec -= (float)delta;
            if (fading.RemainingSec <= 0f)
            {
                foreach (var view in fading.Notes)
                {
                    view.Reparent(_noteRoot);
                    ReturnNoteView(view);
                }
                foreach (var link in fading.Links)
                    ReleaseLink(link);
                fading.Root.QueueFree();
                _fadingHoldViews.RemoveAt(i);
                continue;
            }
            foreach (var view in fading.Notes)
            {
                if (view.IsLineAnchored || view.IsMissFalling || view.IsRecoverableHoldFalling)
                {
                    // 已接起或已下穿的头从当前位置继续落下，不能跳回谱面头的原始位置。
                    var step = MissFallSpeedPx(view.Model, t, currentBar) * (float)delta;
                    view.Position = PositionPastLine(view.Model.Track, view.Position, step);
                }
                else
                    view.Position = PositionForActiveView(view.Model, t, currentBar);
            }
            for (var j = fading.Links.Count - 1; j >= 0; j--)
            {
                var link = fading.Links[j];
                if (UpdateLinkView(link, t, currentBar, updateAppearance: false))
                    continue;
                ReleaseLink(link);
                fading.Links.RemoveAt(j);
            }
            // 父节点乘透明度，保留各端帽/条身原本的深度和断触淡出状态。
            fading.Root.Modulate = new Color(1f, 1f, 1f,
                fading.RemainingSec / HoldMissFadeDurationSec);
        }
    }

    private bool IsEarlyHoldAnchored(NoteLink link, float nearRemainingPx) =>
        link.IsHold && nearRemainingPx > 0f &&
        _readSustain(link.FromId) is { } state &&
        state.Path.Kind == SustainKind.Hold && state.StartResolved &&
        state.Holding && !state.Broken && !state.EndResolved;

    internal void ResolveNoteView(int noteId, ScoreCategory category, JudgeGrade grade,
        bool emitEffect = true, HitTiming timing = HitTiming.Exact)
    {
        if (_failedHoldNoteIds.Contains(noteId) ||
            !_noteById.TryGetValue(noteId, out var note))
            return;

        var isMine = note.Type == NoteType.Mine;
        var isMixer = note.Type is NoteType.MixerHead or NoteType.MixerNode;
        var isMiss = grade == JudgeGrade.Miss;
        var earlyMissEffect = isMiss && timing == HitTiming.Early &&
            note.Type is NoteType.Tap or NoteType.ExTap;
        if (isMiss && (isMixer || note.Type is NoteType.HoldHead or NoteType.HoldNode))
        {
            RecycleNoteViewImmediately(note.Id);
            return;
        }

        var missFallthrough = isMiss && UsesOrdinaryMissFallthrough(note.Type);
        var keepsLineHead = note.Type is NoteType.HoldHead or NoteType.HoldNode;
        var accent = HitAccentFor(note.Type);
        var effectAccent = NoteVisualSpec.EffectAccent(note.Type, grade);
        var fixedPerfectEffect = note.Type is NoteType.Drag or NoteType.HoldHead or NoteType.HoldNode or
            NoteType.MixerHead or NoteType.MixerNode;
        if (!_viewByNoteId.TryGetValue(noteId, out var view) &&
            !isMiss && category == ScoreCategory.HoldStart)
        {
            // 极晚接头时本体可能已经完成普通下穿；重新建立在线 Hold 头。
                view = NoteView.Create(note, SizeFor(note), ColorFor(note),
                    _loaded.SyncAccentRuntimeIds.Contains(note.Id), brightness: ViewBrightness);

            view.Position = PositionAt(note, 0f);
            view.SetDepthAlpha(1f);
            _noteRoot.AddChild(view);
            _viewByNoteId[note.Id] = view;
            _active.Add(view);
        }
        if (view != null)
        {
            if (missFallthrough)
                view.BeginMissFallthrough(
                    DistancePastJudgeLine(note.Track, view.Position));
            else if (keepsLineHead)
            {
                view.AnchorToJudgeLine();
                view.MarkJudged(accent,
                    category == ScoreCategory.HoldStart ? -1f : 0.25f);
                view.Position = PositionAt(note, 0f);
                if (category == ScoreCategory.HoldStart &&
                    _readSustain(note.Id) is { } state)
                {
                    var bounds = state.Path.BoundsAt(state.LastContactUpdateTime);
                    UpdateSustainHeadGeometry(view, bounds, bounds.Center);
                }
            }
            else if (!isMiss && !view.IsResolved && _settings.HitResidualMs > 0 &&
                note.Type is NoteType.Tap or NoteType.ExTap or NoteType.Drag)
                EmitHitResidual(view, note);
            else
                RecycleNoteViewImmediately(note.Id);
        }

        // 普通 Miss 只下穿淡出；Mixer Miss 静默回收；Mine 触发危险爆发。
        if (_settings.HitEffectLevel == HitEffectLevel.Off ||
            (missFallthrough && !earlyMissEffect) || !emitEffect)
            return;

        var size = SizeFor(note);
        var effectKind = HitEffectKindFor(note.Type);
        var effectPosition = PositionAt(note, 0f);
        var effectRotation = note.Track == Track.Center ? 0f : Mathf.Pi * 0.5f;
        var effectStrength = isMine || fixedPerfectEffect ? 1f : grade switch
        {
            JudgeGrade.Perfect => 1f,
            JudgeGrade.Great => 0.82f,
            JudgeGrade.Good => 0.68f,
            _ => 0.75f,
        };

        _effects.EmitBloom(effectPosition, effectRotation, effectAccent, effectKind,
            note.Track == Track.Center ? size.X : size.Y, effectStrength);
        // 粒子密度只看特效档位：UI MOTION 只负责界面动效，不再影响游玩粒子。
        var mode = _settings.HitEffectLevel == HitEffectLevel.Reduced
            ? UiMotionMode.Reduced
            : UiMotionMode.Full;
        if (effectKind != HitEffectKind.Mine)
        {
            _effects.EmitParticles(effectPosition, effectRotation, effectAccent, effectKind, effectStrength, mode);
        }
    }

    private NoteView TakeNoteView(Note note, bool accent = true)
    {
        var type = VisualPoolPlanning.PoolType(note.Type);
        if (!_noteViewPools.TryGetValue(type, out var pool))
            _noteViewPools[type] = pool = new Stack<NoteView>();
        var view = pool.Count > 0
            ? pool.Pop()
            : NoteView.Create(note, SizeFor(note), ColorFor(note), brightness: ViewBrightness);
        if (view.GetParent() != _noteRoot)
        {
            if (view.GetParent() == null) _noteRoot.AddChild(view);
            else view.Reparent(_noteRoot);
        }
        view.Reuse(note, SizeFor(note), ColorFor(note),
            accent && _loaded.SyncAccentRuntimeIds.Contains(note.Id), brightness: ViewBrightness);
        _noteRoot.MoveChild(view, -1);
        return view;
    }

    private void ReturnNoteView(NoteView view)
    {
        if (view.GetParent() != _noteRoot) view.Reparent(_noteRoot);
        var type = VisualPoolPlanning.PoolType(view.Model.Type);
        view.ResetForPool();
        if (!_noteViewPools.TryGetValue(type, out var pool))
            _noteViewPools[type] = pool = new Stack<NoteView>();
        pool.Push(view);
    }

    private void UpdateResiduals(double delta)
    {
        for (var i = _activeResiduals.Count - 1; i >= 0; i--)
        {
            var view = _activeResiduals[i];
            if (!view.TickTtl(delta))
                continue;
            _activeResiduals.RemoveAt(i);
            ReturnNoteView(view);
        }
    }

    /// <summary>
    /// 命中残影固定在判定线上：early/late 时本体并不在线上，而 bloom/粒子已经用
    /// PositionAt(note, 0f) 作为锚点，残影跟随同一位置才不会错位。
    /// </summary>
    private void EmitHitResidual(NoteView view, Note note)
    {
        _viewByNoteId.Remove(note.Id);
        _active.Remove(view);
        view.Position = PositionAt(note, 0f);
        view.SetDepthAlpha(1f);
        view.BeginResidual(_settings.HitResidualSeconds);
        view.ZIndex = 2;
        if (view.GetParent() != _noteRoot)
        {
            if (view.GetParent() == null) _noteRoot.AddChild(view);
            else view.Reparent(_noteRoot);
        }
        _noteRoot.MoveChild(view, -1);
        _activeResiduals.Add(view);
    }

    private void RecycleNoteViewImmediately(int noteId)
    {
        if (!_viewByNoteId.Remove(noteId, out var view))
            return;
        _active.Remove(view);
        ReturnNoteView(view);
    }
}
