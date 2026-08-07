using Godot;
using DuxShared.Chart;

namespace DuxCommunity.Game;

/// <summary>
/// 单个音符的占位视觉（ColorRect，居中于节点原点）。
/// 位置由 GameplayMain 每帧按 SongClock 更新。
/// </summary>
public partial class NoteView : Node2D
{
	public Note Model { get; private set; } = null!;

	private ColorRect _rect = null!;
	private float _ttl = -1f; // 命中后的残留显示时间（秒）

	public static NoteView Create(Note model, Vector2 sizePx, Color color)
	{
		var view = new NoteView { Model = model, ZIndex = 1 }; // 连接体（ZIndex 0）之上
		var rect = new ColorRect
		{
			Color = color,
			Size = sizePx,
			Position = -sizePx / 2f,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		view._rect = rect;
		view.AddChild(rect);
		return view;
	}

	/// <summary>命中/错失后的视觉反馈：淡出并在 ttl 秒后由 GameplayMain 回收。</summary>
	public void MarkJudged(Color flash, float ttl = 0.25f)
	{
		_rect.Color = flash;
		Modulate = new Color(1f, 1f, 1f, 0.45f);
		_ttl = ttl;
	}

	/// <summary>已过判定线且未命中（Miss 扫尾），直接标灰。</summary>
	public void MarkExpired()
	{
		if (_ttl >= 0f) return; // 已在淡出流程中
		Modulate = new Color(1f, 1f, 1f, 0.2f);
		_ttl = 0.15f;
	}

	/// <summary>返回 true 表示应被回收。</summary>
	public bool TickTtl(double delta)
	{
		if (_ttl < 0f) return false;
		_ttl -= (float)delta;
		return _ttl <= 0f;
	}
}
