using System.Collections.Generic;
using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>
/// 命中爆发的 GPU 粒子质感层：速度对齐的火花束 + 向上飘的余烬点，
/// 一次性发射后在自身寿命结束时回收，不参与判定。
/// 纹理、材质与配色规则和 Hold/Mixer 持续层共用 HitParticleAssets。
/// Off 档不生成，Reduced 档只留半量火花，Mine 交给原危险爆发。
/// </summary>
public partial class GameplayHitParticles : Node2D
{
    private const float SparkLifetime = 0.42f;
    private const float EmberLifetime = 0.62f;
    private const float RecycleTail = 0.12f;
    private const int SparkFullCount = 14;
    private const int EmberFullCount = 6;

    private double _age;
    private double _lifetime = SparkLifetime;

    /// <summary>命中点生成一组粒子。Mine 与 Off 档直接跳过，Reduced 档减半且无余烬。</summary>
    public static void Spawn(Node parent, Vector2 position, float rotation, Color accent,
        HitEffectKind kind, float strength, UiMotionMode mode)
    {
        if (parent == null || kind == HitEffectKind.Mine || mode == UiMotionMode.Off)
            return;

        var node = new GameplayHitParticles
        {
            Name = "HitParticles",
            Position = position,
            Rotation = rotation,
            ZIndex = 2,
        };
        node.Build(accent, kind, strength, mode, HitParticleAssets.BurstCullRect);
        parent.AddChild(node);
    }

    /// <summary>
    /// 屏外预发射一次，让命中与持续两套粒子 shader 在首击前完成编译。
    /// 只在舞台就绪后调用一次。
    /// </summary>
    public static void Prewarm(Node parent)
    {
        if (parent == null)
            return;

        var burst = new GameplayHitParticles
        {
            Name = "HitParticlesPrewarm",
            Position = new Vector2(-2000f, -2000f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 2,
        };
        burst.Build(NoteVisualSpec.Tap, HitEffectKind.Tap, 1f, UiMotionMode.Full,
            HitParticleAssets.PrewarmCullRect);
        parent.AddChild(burst);
        GameplaySustainParticles.Prewarm(parent);
    }

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= _lifetime + RecycleTail)
            QueueFree();
    }

    private void Build(Color accent, HitEffectKind kind, float strength, UiMotionMode mode, Rect2 cullRect)
    {
        var tinted = HitParticleAssets.Tint(accent, kind);
        var amountScale = Mathf.Clamp(strength, 0.4f, 1.3f) * KindAmountFactor(kind);
        if (mode == UiMotionMode.Reduced)
            amountScale *= 0.5f;

        var sparkCount = Mathf.Max(4, Mathf.RoundToInt(SparkFullCount * amountScale));
        AddEmitter(HitParticleAssets.LayerBurstSpark, "SparkLayer", sparkCount,
            HitParticleAssets.SparkTexture(), HitParticleAssets.SparkMaterial(tinted), cullRect, SparkLifetime);

        if (mode != UiMotionMode.Full)
            return;

        var emberCount = Mathf.Max(3, Mathf.RoundToInt(EmberFullCount * Mathf.Clamp(strength, 0.5f, 1.2f)));
        AddEmitter(HitParticleAssets.LayerBurstEmber, "EmberLayer", emberCount,
            HitParticleAssets.EmberTexture(), HitParticleAssets.EmberMaterial(tinted), cullRect, EmberLifetime);
    }

    private void AddEmitter(int layer, string name, int amount, Texture2D texture,
        ParticleProcessMaterial material, Rect2 cullRect, float lifetime)
    {
        _lifetime = Mathf.Max(_lifetime, lifetime);
        AddChild(new GpuParticles2D
        {
            Name = name,
            Amount = amount,
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 1f,
            Randomness = 0.5f,
            Emitting = true,
            LocalCoords = false,
            FixedFps = 0,
            Texture = texture,
            ProcessMaterial = material,
            VisibilityRect = cullRect,
            // 与持续层一致的加法混合；ramp 峰值已按过曝预算下调。
            Material = HitParticleAssets.Additive,
        });
    }

    private static float KindAmountFactor(HitEffectKind kind) => kind switch
    {
        // Hold 节点和 Mixer 八分点命中频繁，压低单次粒子量避免糊屏。
        HitEffectKind.Hold or HitEffectKind.Mixer => 0.75f,
        HitEffectKind.ExTap => 1.10f,
        _ => 1f,
    };
}

/// <summary>
/// Hold/Mixer 接触保持期间的持续 GPU 粒子拖尾。位置每帧由 GameplayMain 的动态头
/// 更新逻辑提供，因此粒子留在世界空间里形成拖尾。
/// 断开接触时 SetEmitting(false) 停止发射，存量粒子自然消亡后自我回收；
/// 宽限内接回时重新 SetEmitting(true) 即可恢复。
/// </summary>
public partial class GameplaySustainParticles : Node2D
{
    // 非 one-shot 的发射速率 = Amount / Lifetime；存活数 = 速率 × lifetime。
    // 排空尾段（ramp 末段 alpha→0）的粒子几乎不可见，按经验只有约 60~70% 的存活粒子
    // 能被看清，所以数量按"清晰可见 12~14 颗"反推，多留三分之一余量。
    private const int SparkFullAmount = 14;    // 0.35s 寿命 ⇒ 40 颗/秒，存活 14、清晰可见约 10
    private const int SparkReducedAmount = 7;  // 20 颗/秒，存活 7、清晰可见约 5
    private const int EmberFullAmount = 6;     // 0.5s 寿命 ⇒ 12 颗/秒，存活 6、清晰可见约 4
    private const float SparkLifetime = 0.35f;
    private const float EmberLifetime = 0.50f;
    private const float DrainTail = 0.15f;
    // 彗尾参数（GPUParticles2D 上的 Trail* 属性）。
    private const double TrailLifetime = 0.20;
    private const int TrailSections = 8;
    private const int TrailSectionSubdivisions = 4;

    private readonly List<GpuParticles2D> _emitters = new();
    private double _age;
    private double _drainAge;
    private double _drainLifetime = SparkLifetime;
    private double _autoStopSecond = -1.0;
    private bool _emitting = true;
    private bool _counted;
    private static int _liveEmitters;

    /// <summary>
    /// 接触激活期间创建；返回的实例需要每帧更新 Position。
    /// <paramref name="headSpanPx"/> 是接触头沿轨道方向的实宽，用于按宽度铺开发射；
    /// 旋转需按轨道给符号（中轨 0、左轨 +π/2、右轨 −π/2），配合局部 (0,1,0) 方向
    /// 把粒子推向面板外侧。
    /// </summary>
    public static GameplaySustainParticles Spawn(Node parent, float rotation,
        HitEffectKind kind, UiMotionMode mode, float headSpanPx)
    {
        // 并发折算：同一时刻多条 Hold/Mixer 保持时单条减量，避免总量失控。
        // 只影响新建的发射器，先开始的那条保持完整密度。
        var node = new GameplaySustainParticles
        {
            Name = "SustainParticles",
            Rotation = rotation,
            ZIndex = 2,
        };
        node.Build(kind, mode, HitParticleAssets.BurstCullRect, ConcurrencyFactor(),
            HitParticleAssets.BoxBucket(headSpanPx * 0.5f));
        node._counted = true;
        _liveEmitters++;
        parent.AddChild(node);
        return node;
    }

    private static float ConcurrencyFactor() => _liveEmitters switch
    {
        <= 1 => 1f,
        2 => 0.85f,
        _ => 0.72f,
    };

    public override void _ExitTree()
    {
        if (!_counted)
            return;
        _counted = false;
        _liveEmitters = Mathf.Max(0, _liveEmitters - 1);
    }

    /// <summary>屏外预发射一次并自行停发回收，让持续层 shader 提前编译。</summary>
    public static void Prewarm(Node parent)
    {
        if (parent == null)
            return;

        var node = new GameplaySustainParticles
        {
            Name = "SustainParticlesPrewarm",
            Position = new Vector2(-2600f, -1400f),
            Modulate = new Color(1f, 1f, 1f, 0f),
            ZIndex = 2,
        };
        node.Build(HitEffectKind.Hold, UiMotionMode.Full, HitParticleAssets.PrewarmCullRect, 1f,
            HitParticleAssets.BoxBucket(80f));
        node.StopAfter(0.4);
        parent.AddChild(node);
    }

    public void SetEmitting(bool emitting)
    {
        if (_emitting == emitting)
            return;

        _emitting = emitting;
        _drainAge = 0.0;
        foreach (var emitter in _emitters)
        {
            if (GodotObject.IsInstanceValid(emitter))
                emitter.Emitting = emitting;
        }
    }

    public override void _Process(double delta)
    {
        _age += delta;
        if (_autoStopSecond > 0.0 && _age >= _autoStopSecond)
        {
            // 只给预热探针用：闪一下就走，不需要外部回收。
            _autoStopSecond = -1.0;
            SetEmitting(false);
        }

        if (_emitting)
            return;

        _drainAge += delta;
        if (_drainAge >= _drainLifetime + DrainTail)
            QueueFree();
    }

    private void Build(HitEffectKind kind, UiMotionMode mode, Rect2 cullRect,
        float concurrencyFactor, int boxBucket)
    {
        // 持续段的判定色恒为 HitPerfect，由类型色承担可辨识度（和命中爆发同一比例）。
        var tinted = HitParticleAssets.Tint(NoteVisualSpec.HitPerfect, kind);
        var reduced = mode == UiMotionMode.Reduced;

        // 火花层带彗尾：拖尾由 GPUParticles2D 的 Trail 属性控制（不是 ParticleProcessMaterial）。
        AddEmitter("SustainSparkLayer",
            ScaleAmount(reduced ? SparkReducedAmount : SparkFullAmount, concurrencyFactor),
            HitParticleAssets.SparkTexture(), HitParticleAssets.SustainSparkMaterial(tinted, boxBucket),
            cullRect, SparkLifetime, trail: true);

        if (reduced)
            return;

        _drainLifetime = Mathf.Max(_drainLifetime, EmberLifetime);
        AddEmitter("SustainEmberLayer",
            ScaleAmount(EmberFullAmount, concurrencyFactor),
            HitParticleAssets.EmberTexture(), HitParticleAssets.SustainEmberMaterial(tinted, boxBucket),
            cullRect, EmberLifetime, trail: false);
    }

    // Godot 的连续发射速率 = Amount / Lifetime，所以折算数量就是折算速率与稳态可见数。
    private static int ScaleAmount(int amount, float factor) =>
        Mathf.Max(1, Mathf.RoundToInt(amount * factor));

    private void StopAfter(double seconds)
    {
        _autoStopSecond = seconds;
    }

    private void AddEmitter(string name, int amount, Texture2D texture,
        ParticleProcessMaterial material, Rect2 cullRect, float lifetime, bool trail)
    {
        var emitter = new GpuParticles2D
        {
            Name = name,
            Amount = amount,
            Lifetime = lifetime,
            OneShot = false,
            Explosiveness = 0f,
            Randomness = 0.55f,
            Emitting = true,
            LocalCoords = false,
            FixedFps = 0,
            Texture = texture,
            ProcessMaterial = material,
            VisibilityRect = cullRect,
            // 持续层用加法混合发光。
            Material = HitParticleAssets.Additive,
            TrailEnabled = trail,
            TrailLifetime = TrailLifetime,
            TrailSections = TrailSections,
            TrailSectionSubdivisions = TrailSectionSubdivisions,
        };
        _emitters.Add(emitter);
        AddChild(emitter);
    }
}

/// <summary>
/// 两个粒子层共用的运行时合成纹理、颜色斜坡和静态材质缓存。
/// 所有纹理与材质只生成一次；(accent, 层, 接触头宽度桶) 是唯一缓存键。
/// </summary>
internal static class HitParticleAssets
{
    public const int LayerBurstSpark = 0;
    public const int LayerBurstEmber = 1;
    public const int LayerSustainSpark = 2;
    public const int LayerSustainEmber = 3;
    public const float TintWeight = 0.60f;

    // 接触头宽度量化步长：材质按桶缓存，宽度小幅变化不会新建材质。
    public const float BoxBucketPx = 24f;
    private const int BoxBucketMin = 1;
    private const int BoxBucketMax = 10;

    private const int SparkTextureWidth = 16;
    private const int SparkTextureHeight = 64;
    private const int EmberTextureSize = 32;

    // 火花飞行半径约 60px，矩形给足即可；余烬更慢。
    public static readonly Rect2 BurstCullRect = new(-700f, -700f, 1400f, 1400f);
    // 预发射探针在屏外，矩形必须同时覆盖它和视口，否则画布剔除会跳过 shader 编译。
    public static readonly Rect2 PrewarmCullRect = new(-3000f, -3000f, 6000f, 6000f);

    private static readonly Dictionary<(Color Accent, int Layer, int Bucket), ParticleProcessMaterial> Materials = new();
    private static Texture2D? _sparkTexture;
    private static Texture2D? _emberTexture;
    private static Texture2D? _shrinkCurve;
    private static CanvasItemMaterial? _additive;

    /// <summary>持续层的加法混合材质：让粒子在深色背景上发光。</summary>
    public static CanvasItemMaterial Additive => _additive ??= new CanvasItemMaterial
    {
        BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
    };

    public static int BoxBucket(float halfWidthPx) => Mathf.Clamp(
        Mathf.RoundToInt(halfWidthPx / BoxBucketPx), BoxBucketMin, BoxBucketMax);

    // ---- 配色 ----

    // 判定色决定粒子主体；把音符类型色推入 60%，让同屏的 Tap/Drag/Hold/Mixer
    // 质感可区分。Miss/Mine 的危险红不做偏移。
    public static Color Tint(Color accent, HitEffectKind kind)
    {
        if (accent.IsEqualApprox(NoteVisualSpec.HitMiss))
            return accent;

        var tint = kind switch
        {
            HitEffectKind.ExTap => NoteVisualSpec.ExTap,
            HitEffectKind.Drag => NoteVisualSpec.Drag,
            HitEffectKind.Hold => NoteVisualSpec.Hold,
            HitEffectKind.Mixer => NoteVisualSpec.Mixer,
            _ => NoteVisualSpec.Tap,
        };
        return accent.Lerp(tint, TintWeight);
    }

    // ---- 材质（静态缓存） ----

    public static ParticleProcessMaterial SparkMaterial(Color accent)
    {
        if (Materials.TryGetValue((accent, LayerBurstSpark, 0), out var cached))
            return cached;

        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 180f,
            InitialVelocityMin = 200f,
            InitialVelocityMax = 440f,
            DampingMin = 500f,
            DampingMax = 500f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.55f,
            ScaleMax = 0.78f,
            ScaleCurve = ShrinkCurve(),
            // 加法混合下靠爆发的核心很容易饱和，峰值从 1.0 降到 0.80。
            ColorRamp = Ramp(new[]
            {
                new Color(1f, 1f, 1f, 0.80f),
                new Color(1f, 1f, 1f, 0.74f),
                new Color(accent, 0.80f),
                new Color(accent, 0.44f),
                new Color(accent, 0f),
            }, new[] { 0f, 0.14f, 0.32f, 0.70f, 1f }),
            LifetimeRandomness = 0.18,
            ParticleFlagAlignY = true,
        };
        Materials[(accent, LayerBurstSpark, 0)] = material;
        return material;
    }

    public static ParticleProcessMaterial EmberMaterial(Color accent)
    {
        if (Materials.TryGetValue((accent, LayerBurstEmber, 0), out var cached))
            return cached;

        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Point,
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 180f,
            InitialVelocityMin = 40f,
            InitialVelocityMax = 120f,
            DampingMin = 40f,
            DampingMax = 40f,
            Gravity = new Vector3(0f, -55f, 0f),
            ScaleMin = 0.60f,
            ScaleMax = 0.95f,
            ScaleCurve = ShrinkCurve(),
            // 余烬同样按加法混合下调峰值。
            ColorRamp = Ramp(new[]
            {
                new Color(1f, 1f, 1f, 0.68f),
                new Color(accent, 0.64f),
                new Color(accent, 0.32f),
                new Color(accent, 0f),
            }, new[] { 0f, 0.18f, 0.62f, 1f }),
            LifetimeRandomness = 0.22,
            ParticleFlagAlignY = true,
        };
        Materials[(accent, LayerBurstEmber, 0)] = material;
        return material;
    }

    // 持续层沿接触头宽度发射，方向统一取局部 (0,1,0)，由 GameplayMain 传入的
    // Rotation（中轨 0 / 左轨 +π/2 / 右轨 −π/2）把它转到"面板外侧"。
    public static ParticleProcessMaterial SustainSparkMaterial(Color accent, int bucket)
    {
        if (Materials.TryGetValue((accent, LayerSustainSpark, bucket), out var cached))
            return cached;

        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(bucket * BoxBucketPx, 3f, 0f),
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 140f,
            InitialVelocityMin = 60f,
            InitialVelocityMax = 140f,
            DampingMin = 260f,
            DampingMax = 260f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.60f,
            ScaleMax = 0.90f,
            ScaleCurve = ShrinkCurve(),
            // 前 28% 寿命保持近白核心，再过渡到类型色；末端淡出。
            // 加法混合下接触头附近会叠加出纯白，整体透明度比中性版低 20%。
            ColorRamp = Ramp(new[]
            {
                new Color(1f, 1f, 1f, 0.80f),
                new Color(1f, 1f, 1f, 0.80f),
                new Color(accent, 0.58f),
                new Color(accent, 0.24f),
                new Color(accent, 0f),
            }, new[] { 0f, 0.28f, 0.62f, 0.85f, 1f }),
            LifetimeRandomness = 0.25,
            ParticleFlagAlignY = true,
        };
        Materials[(accent, LayerSustainSpark, bucket)] = material;
        return material;
    }

    public static ParticleProcessMaterial SustainEmberMaterial(Color accent, int bucket)
    {
        if (Materials.TryGetValue((accent, LayerSustainEmber, bucket), out var cached))
            return cached;

        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(bucket * BoxBucketPx, 3f, 0f),
            Direction = new Vector3(0f, 1f, 0f),
            Spread = 140f,
            InitialVelocityMin = 25f,
            InitialVelocityMax = 70f,
            DampingMin = 30f,
            DampingMax = 30f,
            Gravity = new Vector3(0f, -40f, 0f),
            ScaleMin = 0.50f,
            ScaleMax = 0.75f,
            ScaleCurve = ShrinkCurve(),
            ColorRamp = Ramp(new[]
            {
                new Color(1f, 1f, 1f, 0.74f),
                new Color(1f, 1f, 1f, 0.70f),
                new Color(accent, 0.44f),
                new Color(accent, 0f),
            }, new[] { 0f, 0.25f, 0.65f, 1f }),
            LifetimeRandomness = 0.30,
        };
        Materials[(accent, LayerSustainEmber, bucket)] = material;
        return material;
    }

    private static Texture2D Ramp(Color[] colors, float[] offsets)
    {
        var gradient = new Gradient { Offsets = offsets, Colors = colors };
        return new GradientTexture1D { Gradient = gradient, Width = 64 };
    }

    private static Texture2D ShrinkCurve()
    {
        if (_shrinkCurve != null)
            return _shrinkCurve;

        var curve = new Curve();
        curve.AddPoint(new Vector2(0f, 1f));
        curve.AddPoint(new Vector2(1f, 0.22f));
        _shrinkCurve = new CurveTexture { Curve = curve, Width = 64 };
        return _shrinkCurve;
    }

    // ---- 纹理 ----

    public static Texture2D SparkTexture()
    {
        if (_sparkTexture != null)
            return _sparkTexture;

        var image = Image.CreateEmpty(SparkTextureWidth, SparkTextureHeight, false, Image.Format.Rgba8);
        for (var y = 0; y < SparkTextureHeight; y++)
        {
            var v = Mathf.Abs((y + 0.5f) / SparkTextureHeight * 2f - 1f);
            var along = Mathf.Pow(1f - v, 1.35f);
            for (var x = 0; x < SparkTextureWidth; x++)
            {
                var u = Mathf.Abs((x + 0.5f) / SparkTextureWidth * 2f - 1f);
                var across = Mathf.Pow(1f - u, 0.55f);
                var core = Mathf.Pow(1f - u, 5f);
                var alpha = Mathf.Clamp(along * across * 1.05f + along * core * 0.45f, 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        _sparkTexture = ImageTexture.CreateFromImage(image);
        return _sparkTexture;
    }

    public static Texture2D EmberTexture()
    {
        if (_emberTexture != null)
            return _emberTexture;

        const float radius = EmberTextureSize * 0.5f;
        var image = Image.CreateEmpty(EmberTextureSize, EmberTextureSize, false, Image.Format.Rgba8);
        for (var y = 0; y < EmberTextureSize; y++)
        {
            var dy = y + 0.5f - radius;
            for (var x = 0; x < EmberTextureSize; x++)
            {
                var dx = x + 0.5f - radius;
                var distance = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                var alpha = Mathf.Pow(Mathf.Clamp(1f - distance, 0f, 1f), 1.7f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp(alpha * 1.25f, 0f, 1f)));
            }
        }

        _emberTexture = ImageTexture.CreateFromImage(image);
        return _emberTexture;
    }
}
