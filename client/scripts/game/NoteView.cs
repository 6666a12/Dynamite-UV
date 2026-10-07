using Godot;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Game;

/// <summary>
/// 单个音符的运行时视觉，居中于节点原点。全部 Note 类型使用同一套
/// clean-room 切角渐变材质，并通过类型纹理区分语义。
/// 位置由 GameplayMain 每帧按 SongClock 更新。
/// </summary>
public partial class NoteView : Node2D
{
	private readonly record struct SurfacePalette(
		Color Top, Color Mid, Color Bottom, Color Border, Color Glow);

	private static readonly SurfacePalette TapPalette = new(
		new Color(0.85f, 0.96f, 1.00f),
		new Color(0.30f, 0.72f, 1.00f),
		new Color(0.09f, 0.42f, 0.68f),
		new Color(0.72f, 0.92f, 1.00f),
		new Color(0.30f, 0.72f, 1.00f));
	private static readonly SurfacePalette ExTapPalette = new(
		new Color(1.00f, 1.00f, 1.00f),
		new Color(0.46f, 0.84f, 1.00f),
		new Color(0.21f, 0.55f, 0.77f),
		new Color(0.91f, 0.98f, 1.00f),
		new Color(0.46f, 0.84f, 1.00f));
	private static readonly SurfacePalette DragPalette = new(
		new Color(0.88f, 1.00f, 0.91f),
		new Color(0.29f, 0.87f, 0.50f),
		new Color(0.10f, 0.55f, 0.28f),
		new Color(0.78f, 1.00f, 0.83f),
		new Color(0.29f, 0.87f, 0.50f));
	private static readonly SurfacePalette HoldPalette = new(
		new Color(1.00f, 0.95f, 0.82f),
		new Color(1.00f, 0.72f, 0.30f),
		new Color(0.68f, 0.40f, 0.07f),
		new Color(1.00f, 0.94f, 0.80f),
		new Color(1.00f, 0.72f, 0.30f));
	private static readonly SurfacePalette MixerPalette = new(
		new Color(1.00f, 0.84f, 0.91f),
		new Color(1.00f, 0.30f, 0.56f),
		new Color(0.66f, 0.12f, 0.35f),
		new Color(1.00f, 0.82f, 0.89f),
		new Color(1.00f, 0.30f, 0.56f));
	private static readonly SurfacePalette MinePalette = new(
		new Color(0.77f, 0.29f, 0.35f),
		new Color(0.46f, 0.16f, 0.21f),
		new Color(0.21f, 0.07f, 0.11f),
		new Color(0.93f, 0.42f, 0.47f),
		new Color(0.77f, 0.20f, 0.29f));
	private static readonly SurfacePalette BarLinePalette = new(
		new Color(0.72f, 0.77f, 0.86f, 0.52f),
		new Color(0.42f, 0.46f, 0.54f, 0.46f),
		new Color(0.17f, 0.19f, 0.24f, 0.40f),
		new Color(0.76f, 0.82f, 0.90f, 0.58f),
		new Color(0.35f, 0.88f, 1.00f));
	private static readonly SurfacePalette GoldPalette = new(
		new Color(1.00f, 0.94f, 0.60f),
		new Color(1.00f, 0.82f, 0.30f),
		new Color(0.68f, 0.43f, 0.08f),
		new Color(1.00f, 0.93f, 0.58f),
		new Color(1.00f, 0.82f, 0.30f));

	private static Shader? _surfaceShader;
	private static Shader SurfaceShader =>
		_surfaceShader ??= GD.Load<Shader>("res://shaders/note_surface.gdshader");

	public Note Model { get; private set; } = null!;

	private ColorRect? _flatRect;
	private readonly List<(ColorRect Rect, ShaderMaterial Material, Vector2 Expansion)> _surfaces = new();
	private Vector2 _sizePx;
	private float _ttl = -1f; // 命中后的残留显示时间（秒）
	private float _depthAlpha = 1f;
	private float _stateAlpha = 1f;
	public bool IsResolved { get; private set; }
	public bool IsMissFalling { get; private set; }
	public bool IsRecoverableHoldFalling { get; private set; }
	public bool IsLineAnchored { get; private set; }
	public float MissDistancePx { get; private set; }

	public static NoteView Create(Note model, Vector2 sizePx, Color color, bool goldFrame = false, bool ghost = false)
	{
		var view = new NoteView { Model = model, ZIndex = 1, _sizePx = sizePx }; // 连接体（ZIndex 0）之上
		if (TryPaletteFor(model.Type, out var palette))
		{
			var vertical = model.Track != Track.Center;
			var glowExpansion = vertical ? new Vector2(8f, 14f) : new Vector2(14f, 8f);
			var glowAlpha = GlowAlphaFor(model.Type);
			if (glowAlpha > 0f && !ghost)
			{
				var glow = FlatPalette(palette.Glow, glowAlpha);
				AddSurface(view, sizePx + glowExpansion, glow, vertical, 0f, 0);
			}

				if (goldFrame)
				{
					// Runtime-derived cross-track press chord accent; never trust Baked_SyncNote.
					AddSurface(view, sizePx + new Vector2(8f, 8f), GoldPalette,

					vertical, 0.35f, 0);
			}
			AddSurface(view, sizePx, palette, vertical,
				DetailStrengthFor(model.Type), DetailKindFor(model.Type));
			return view;
		}

		var rect = new ColorRect
		{
			Color = color,
			Size = sizePx,
			Position = -sizePx / 2f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		view._flatRect = rect;
		view.AddChild(rect);
		return view;
	}

	private static bool TryPaletteFor(NoteType type, out SurfacePalette palette)
	{
		palette = type switch
		{
			NoteType.Tap => TapPalette,
			NoteType.ExTap => ExTapPalette,
			NoteType.Drag => DragPalette,
			NoteType.HoldHead or NoteType.HoldNode => HoldPalette,
			NoteType.MixerHead or NoteType.MixerNode => MixerPalette,
			NoteType.Mine => MinePalette,
			NoteType.BarLine => BarLinePalette,
			_ => default,
		};
		return type is NoteType.Tap or NoteType.ExTap or NoteType.Drag or
			NoteType.HoldHead or NoteType.HoldNode or NoteType.MixerHead or
			NoteType.MixerNode or NoteType.Mine or NoteType.BarLine;
	}

	private static float GlowAlphaFor(NoteType type) => type switch
	{
		NoteType.BarLine => 0f,
		NoteType.Mine => 0.07f,
		NoteType.Tap => 0.09f,
		NoteType.ExTap or NoteType.Drag => 0.10f,
		_ => 0.12f,
	};

	private static float DetailStrengthFor(NoteType type) =>
		type == NoteType.BarLine ? 0.32f : 1f;

	private static int DetailKindFor(NoteType type) => type switch
	{
		NoteType.ExTap => 1,
		NoteType.Drag => 2,
		NoteType.Mine => 3,
		NoteType.BarLine => 4,
		NoteType.MixerHead or NoteType.MixerNode => 5,
		_ => 0,
	};

	private static SurfacePalette FlatPalette(Color color, float alpha)
	{
		var translucent = new Color(color, alpha);
		return new SurfacePalette(translucent, translucent, translucent,
			translucent, translucent);
	}

	private static void AddSurface(NoteView view, Vector2 sizePx,
		SurfacePalette palette, bool vertical, float detailStrength, int detailKind)
	{
		var material = new ShaderMaterial { Shader = SurfaceShader };
		material.SetShaderParameter("size_px", sizePx);
		material.SetShaderParameter("cut_px",
			Mathf.Min(7f, Mathf.Min(sizePx.X, sizePx.Y) * 0.34f));
		material.SetShaderParameter("border_px", detailStrength > 0f ? 1.5f : 0.6f);
		material.SetShaderParameter("vertical", vertical);
		material.SetShaderParameter("detail_strength", detailStrength);
		material.SetShaderParameter("detail_kind", detailKind);
		material.SetShaderParameter("top_color", palette.Top);
		material.SetShaderParameter("mid_color", palette.Mid);
		material.SetShaderParameter("bottom_color", palette.Bottom);
		material.SetShaderParameter("border_color", palette.Border);

		var rect = new ColorRect
		{
			Color = Colors.White,
			Size = sizePx,
			Position = -sizePx / 2f,
			Material = material,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		view._surfaces.Add((rect, material, sizePx - view._sizePx));
		view.AddChild(rect);
	}

	/// <summary>按路径当前宽度调整头部，保持描边和光晕的像素尺寸。</summary>
	public void SetSize(Vector2 sizePx)
	{
		if (_sizePx == sizePx)
			return;
		_sizePx = sizePx;
		if (_flatRect != null)
		{
			_flatRect.Size = sizePx;
			_flatRect.Position = -sizePx / 2f;
		}
		foreach (var (rect, material, expansion) in _surfaces)
		{
			var surfaceSize = sizePx + expansion;
			rect.Size = surfaceSize;
			rect.Position = -surfaceSize / 2f;
			material.SetShaderParameter("size_px", surfaceSize);
			material.SetShaderParameter("cut_px",
				Mathf.Min(7f, Mathf.Min(surfaceSize.X, surfaceSize.Y) * 0.34f));
		}
	}

	/// <summary>命中/错失后的视觉反馈；ttl 为负时保留到调用方显式结束。</summary>
	public void MarkJudged(Color flash, float ttl = 0.25f)
	{
		IsResolved = true;
		IsMissFalling = false;
		IsRecoverableHoldFalling = false;
		if (_flatRect != null)
			_flatRect.Color = flash;
		foreach (var (_, material, _) in _surfaces)
		{
			material.SetShaderParameter("flash_color", flash);
			material.SetShaderParameter("flash_mix", 0.68f);
		}
		_stateAlpha = 0.45f;
		ApplyAlpha();
		_ttl = ttl;
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
		_stateAlpha = 0.45f;
		_ttl = -1f;
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
		if (_ttl < 0f) return false;
		_ttl -= (float)delta;
		return _ttl <= 0f;
	}
}
