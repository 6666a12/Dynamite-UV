using Godot;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Game;

/// <summary>
/// 单个音符的运行时视觉，居中于节点原点。位置由调用方每帧按播放时钟更新。
///
/// 非 BarLine 使用 note_material（与 design/note-light-v1 的 WebGL 实现逐像素对照过的
/// 单色程序化材质）：一个 MeshInstance2D 承载「尺寸 + 2×pad」的矩形，UV 0..1；
/// 侧轨把 mesh 子节点旋转 90°，局部空间始终横排。BarLine 继续沿用旧的
/// ColorRect + note_surface 材质，避免小节线被新材质的光晕重新定义。
/// </summary>
public partial class NoteView : Node2D
{
	// 设计原值（100%）：下落 0.72 / 接触 1.28 / 命中峰值 2.57。设置里的音符亮度按百分比缩放。
	private const float FallEnergyBase = 0.72f;
	private const float ContactEnergyBase = 1.28f;
	private const float PeakEnergyBase = 2.57f;
	private const float PeakDecaySeconds = 0.115f;
	private const float DefaultResidualSeconds = 0.30f;
	private const float GoldWidthPx = 3f;

	private float _brightness = 1f;
	private float _residualTtl = DefaultResidualSeconds;

	private float FallEnergy => FallEnergyBase * _brightness;
	private float ContactEnergy => ContactEnergyBase * _brightness;
	private float PeakEnergy => PeakEnergyBase * _brightness;

	private static readonly SurfacePalette BarLinePalette = new(
		new Color(0.72f, 0.77f, 0.86f, 0.52f),
		new Color(0.42f, 0.46f, 0.54f, 0.46f),
		new Color(0.17f, 0.19f, 0.24f, 0.40f),
		new Color(0.76f, 0.82f, 0.90f, 0.58f),
		new Color(0.35f, 0.88f, 1.00f));

	private readonly record struct SurfacePalette(
		Color Top, Color Mid, Color Bottom, Color Border, Color Glow);

	private static Shader? _materialShader;
	private static Shader MaterialShader =>
		_materialShader ??= GD.Load<Shader>("res://shaders/note_material.gdshader");
	private static Shader? _surfaceShader;
	private static Shader SurfaceShader =>
		_surfaceShader ??= GD.Load<Shader>("res://shaders/note_surface.gdshader");
	private static Mesh? _unitQuad;
	private static Mesh UnitQuad => _unitQuad ??= BuildUnitQuad();

    /// <summary>Current body dimensions, including a moving sustain head's sampled width.</summary>
    public Vector2 BodySize => _sizePx;

	public Note Model { get; private set; } = null!;

	private MeshInstance2D? _mesh;
	private ShaderMaterial? _material;
	private ColorRect? _barLineRect;
	private ShaderMaterial? _barLineMaterial;

	private Vector2 _sizePx;
	private Color _baseColor = Colors.White;
	private float _energy = FallEnergyBase;
	private float _opacity = 1f;
	private bool _haloOnly;
	private bool _gold;
	private bool _vertical;
	private int _kind;
	private float _residualElapsed;
	private float _ttl = -1f; // 命中后的残留显示时间（秒）
	private float _depthAlpha = 1f;
	private float _stateAlpha = 1f;
	public bool IsResolved { get; private set; }
	public bool IsMissFalling { get; private set; }
	public bool IsRecoverableHoldFalling { get; private set; }
	public bool IsLineAnchored { get; private set; }
	public bool IsResidual => _haloOnly;
	public float MissDistancePx { get; private set; }

	public void ResetForPool()
	{
		Visible = false;
		Position = Vector2.Zero;
		Rotation = 0f;
		Scale = Vector2.One;
		Skew = 0f;
		Modulate = Colors.Transparent;
		ZIndex = 1;
		_ttl = -1f;
		_depthAlpha = 1f;
		_stateAlpha = 1f;
		_energy = FallEnergy;
		_opacity = 1f;
		_haloOnly = false;
		_gold = false;
		_vertical = false;
		_kind = 0;
		_residualElapsed = 0f;
		_baseColor = Colors.White;
		IsResolved = false;
		IsMissFalling = false;
		IsRecoverableHoldFalling = false;
		IsLineAnchored = false;
		MissDistancePx = 0f;
		if (_mesh != null)
		{
			_mesh.Rotation = 0f;
			_mesh.Scale = Vector2.One;
		}
		if (_barLineMaterial != null)
		{
			_barLineMaterial.SetShaderParameter("flash_color", Colors.White);
			_barLineMaterial.SetShaderParameter("flash_mix", 0f);
		}
	}

	public void Reuse(Note model, Vector2 sizePx, Color color, bool goldFrame = false, bool ghost = false,
		float brightness = 1f)
	{
		ResetForPool();
		Model = model;
		_sizePx = sizePx;
		_brightness = Mathf.Max(0.1f, brightness);
		Visible = true;
		Modulate = Colors.White;
		_vertical = model.Track != Track.Center;
		if (_mesh == null)
		{
			ApplyBarLineSurface(sizePx);
			return;
		}
		_baseColor = color;
		_kind = KindFor(model.Type);
		_gold = goldFrame;
		_energy = FallEnergy;
		_opacity = 1f;
		_haloOnly = false;
		ApplyMaterial();
	}

	public static NoteView Create(Note model, Vector2 sizePx, Color color, bool goldFrame = false,
		bool ghost = false, float brightness = 1f)
	{
		var view = new NoteView { Model = model, ZIndex = 1, _sizePx = sizePx };
		if (model.Type == NoteType.BarLine)
			view.BuildBarLine();
		else
			view.BuildMaterial();
		view.Reuse(model, sizePx, color, goldFrame, ghost, brightness);
		return view;
	}

	private void BuildMaterial()
	{
		var material = new ShaderMaterial { Shader = MaterialShader };
		material.SetShaderParameter("u_mask", NoteMask.Texture);
		var mesh = new MeshInstance2D { Mesh = UnitQuad, Material = material };
		_material = material;
		_mesh = mesh;
		AddChild(mesh);
	}

	private void BuildBarLine()
	{
		var material = new ShaderMaterial { Shader = SurfaceShader };
		var rect = new ColorRect
		{
			Color = Colors.White,
			Material = material,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_barLineMaterial = material;
		_barLineRect = rect;
		AddChild(rect);
	}

	private static int KindFor(NoteType type) => type switch
	{
		NoteType.ExTap => 1,
		NoteType.MixerHead => 2,
		NoteType.Mine => 3,
		_ => 0,
	};

	private void ApplyMaterial()
	{
		if (_material is null || _mesh is null)
			return;
		// 残影的椭圆光斑半径与 energy 无关，quad 固定用最大 pad，避免衰减时把光斑边缘裁掉。
		var pad = _haloOnly ? 130f : Mathf.Clamp(_energy * 38f, 64f, 130f);
		// 侧轨在局部空间仍是横排：把长边放在局部 X，再由 mesh 子节点旋转 90°。
		var inner = _vertical ? new Vector2(_sizePx.Y, _sizePx.X) : _sizePx;
		var draw = inner + new Vector2(pad * 2f, pad * 2f);
		_mesh.Scale = draw;
		_mesh.Rotation = _vertical ? Mathf.Pi * 0.5f : 0f;
		_material.SetShaderParameter("u_size", inner);
		_material.SetShaderParameter("u_pad", pad);
		_material.SetShaderParameter("u_color",
			new Vector3(_baseColor.R, _baseColor.G, _baseColor.B));
		_material.SetShaderParameter("u_energy", _energy);
		_material.SetShaderParameter("u_glow", 1f);
		_material.SetShaderParameter("u_kind", (float)_kind);
		_material.SetShaderParameter("u_opacity", _opacity);
		_material.SetShaderParameter("u_mode", _haloOnly ? 1f : 0f);
		_material.SetShaderParameter("u_gold", _gold ? 1f : 0f);
		_material.SetShaderParameter("u_gold_width", GoldWidthPx);
	}

	private void ApplyBarLineSurface(Vector2 sizePx)
	{
		if (_barLineRect is null || _barLineMaterial is null)
			return;
		var surface = sizePx;
		_barLineRect.Size = surface;
		_barLineRect.Position = -surface / 2f;
		_barLineMaterial.SetShaderParameter("size_px", surface);
		_barLineMaterial.SetShaderParameter("cut_px",
			Mathf.Min(7f, Mathf.Min(surface.X, surface.Y) * 0.34f));
		_barLineMaterial.SetShaderParameter("border_px", 1.5f);
		_barLineMaterial.SetShaderParameter("vertical", _vertical);
		_barLineMaterial.SetShaderParameter("detail_strength", 0.32f);
		_barLineMaterial.SetShaderParameter("detail_kind", 4);
		_barLineMaterial.SetShaderParameter("top_color", BarLinePalette.Top);
		_barLineMaterial.SetShaderParameter("mid_color", BarLinePalette.Mid);
		_barLineMaterial.SetShaderParameter("bottom_color", BarLinePalette.Bottom);
		_barLineMaterial.SetShaderParameter("border_color", BarLinePalette.Border);
	}

	private static Mesh BuildUnitQuad()
	{
		var mesh = new ArrayMesh();
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = new Vector2[]
		{
			new(-0.5f, -0.5f), new(0.5f, -0.5f), new(-0.5f, 0.5f),
			new(-0.5f, 0.5f), new(0.5f, -0.5f), new(0.5f, 0.5f),
		};
		arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
		{
			new(0f, 0f), new(1f, 0f), new(0f, 1f),
			new(0f, 1f), new(1f, 0f), new(1f, 1f),
		};
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	/// <summary>按路径当前宽度调整头部，保持描边和光晕的像素尺寸。</summary>
	public void SetSize(Vector2 sizePx)
	{
		if (_sizePx == sizePx)
			return;
		_sizePx = sizePx;
		if (_barLineRect != null)
		{
			ApplyBarLineSurface(sizePx);
			return;
		}
		ApplyMaterial();
	}

	/// <summary>命中/错失后的视觉反馈；ttl 为负时保留到调用方显式结束。</summary>
	public void MarkJudged(Color flash, float ttl = 0.25f)
	{
		IsResolved = true;
		IsMissFalling = false;
		IsRecoverableHoldFalling = false;
		if (_barLineMaterial != null)
		{
			_barLineMaterial.SetShaderParameter("flash_color", flash);
			_barLineMaterial.SetShaderParameter("flash_mix", 0.68f);
		}
		else
		{
			// Hold/Mixer 的接头不再闪白：亮度落在持续接触档，透明度保持满值。
			_haloOnly = false;
			_energy = ContactEnergy;
			ApplyMaterial();
		}
		_stateAlpha = ttl < 0f ? 1f : 0.45f;
		ApplyAlpha();
		_ttl = ttl;
	}

	/// <summary>Mixer 滑块保持在线时使用持续接触亮度。</summary>
	public void SetContactLight()
	{
		if (_material is null || (!_haloOnly && _energy == ContactEnergy))
			return;
		_energy = ContactEnergy;
		_haloOnly = false;
		ApplyMaterial();
	}

	/// <summary>把本体从命中帧起替换成判定线上的纯光残影，默认 0.30s 内归零。</summary>
	public void BeginResidual(float seconds = DefaultResidualSeconds)
	{
		IsResolved = true;
		IsLineAnchored = true;
		IsMissFalling = false;
		IsRecoverableHoldFalling = false;
		_haloOnly = true;
		_residualElapsed = 0f;
		_residualTtl = Mathf.Max(0.05f, seconds);
		_energy = PeakEnergy;
		_opacity = 1f;
		_stateAlpha = 1f;
		_depthAlpha = 1f;
		_ttl = _residualTtl;
		ApplyMaterial();
		ApplyAlpha();
	}

	/// <summary>从判定线开始执行距离驱动的 Miss 下穿，不改变 Note 原本材质。</summary>
	public void BeginMissFallthrough(float initialDistancePx = 0f)
	{
		if (IsResolved || IsMissFalling)
			return;
		IsLineAnchored = false;
		IsMissFalling = true;
		MissDistancePx = Mathf.Max(0f, initialDistancePx);
		_ttl = -1f;
		UpdateMissAlpha();
	}

	/// <summary>
	/// Hold 接头后的临时断触下穿。到达淡出终点后仍保留视图，以便宽限内重新接回。
	/// </summary>
	public void BeginRecoverableHoldFallthrough()
	{
		if (IsMissFalling || IsRecoverableHoldFalling)
			return;
		IsLineAnchored = false;
		IsRecoverableHoldFalling = true;
		MissDistancePx = 0f;
		_ttl = -1f;
		UpdateMissAlpha();
	}

	/// <summary>宽限内重新接回 Hold：撤销下穿并恢复判定线上的已接头状态。</summary>
	public void RestoreHoldContact()
	{
		if (IsMissFalling)
			return;
		IsRecoverableHoldFalling = false;
		IsLineAnchored = true;
		MissDistancePx = 0f;
		_stateAlpha = 1f;
		_energy = ContactEnergy;
		_ttl = -1f;
		ApplyMaterial();
		ApplyAlpha();
	}

	/// <summary>推进下穿距离；最终 Miss 到 64px 后回收，可恢复 Hold 始终保留。</summary>
	public bool AdvanceMissFallthrough(float distancePx)
	{
		if (!IsMissFalling && !IsRecoverableHoldFalling)
			return false;
		MissDistancePx += Mathf.Max(0f, distancePx);
		UpdateMissAlpha();
		return IsMissFalling && MissDistancePx >= 64f;
	}

	private void UpdateMissAlpha()
	{
		_stateAlpha = MissDistancePx <= 24f
			? 1f
			: 1f - Mathf.Clamp((MissDistancePx - 24f) / 40f, 0f, 1f);
		ApplyAlpha();
	}

	/// <summary>将已判定的端帽锚定到判定线；沿线位置由调用方更新。</summary>
	public void AnchorToJudgeLine() => IsLineAnchored = true;

	/// <summary>出生区顶部淡入；与命中/过线淡出相乘，避免互相覆盖。</summary>
	public void SetDepthAlpha(float alpha)
	{
		_depthAlpha = Mathf.Clamp(alpha, 0f, 1f);
		ApplyAlpha();
	}

	private void ApplyAlpha() =>
		Modulate = new Color(1f, 1f, 1f, _depthAlpha * _stateAlpha);

	/// <summary>返回 true 表示应被回收。</summary>
	public bool TickTtl(double delta)
	{
		if (_haloOnly)
		{
			_residualElapsed += (float)delta;
			// 亮度先冲到峰值再熄灭；本体已隐藏，只剩光晕随亮度衰减。
			_energy = PeakEnergy * Mathf.Exp(-_residualElapsed / PeakDecaySeconds);
			_opacity = Mathf.Max(0f, 1f - _residualElapsed / _residualTtl);
			ApplyMaterial();
			return _residualElapsed >= _residualTtl;
		}
		if (_ttl < 0f) return false;
		_ttl -= (float)delta;
		return _ttl <= 0f;
	}
}
