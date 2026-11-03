using Godot;
using DynamiteUniverse.Audio;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Chart.V2;
using DynamiteUniverse.Shared.Judge;
using DynamiteUniverse.Shared.Score;

namespace DynamiteUniverse.Game;

public partial class GameplayMain
{
	private readonly HashSet<int> _ignoredTouchIds = new();
	private readonly PointerInputBuffer<Vector2> _pointerBuffer =
		new(AddProjectedTouches, ProjectHoldTouch);
	private readonly List<TouchSample>[] _touchesByTrack =
		[[], [], []];

	public override void _Notification(int what)
	{
		if (what == NotificationWMWindowFocusOut)
			ClearPointerState();
	}

	public override void _Input(InputEvent e)
	{
		if (!_playbackStarted || _finished || _pause.IsPaused || TransitionDirector.IsBusy)
			return;
		if (e is InputEventScreenTouch touch)
		{
			if (touch.Pressed && PauseButtonRect.HasPoint(touch.Position))
			{
				_ignoredTouchIds.Add(touch.Index);
				return;
			}
			if (_ignoredTouchIds.Contains(touch.Index))
			{
				if (!touch.Pressed)
					_ignoredTouchIds.Remove(touch.Index);
				return;
			}
			QueuePointerInput(e);
		}
		else if (e is InputEventScreenDrag drag &&
			!_ignoredTouchIds.Contains(drag.Index))
		{
			QueuePointerInput(e);
		}
	}

	public override void _UnhandledInput(InputEvent e)
	{
		if (!_playbackStarted || _finished || TransitionDirector.IsBusy)
			return;

		if (e is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Escape)
			{
				TogglePause();
				return;
			}
			if (_pause.IsPaused) return;
			if (key.Keycode == Key.F12) // 调试：跳到谱尾前 3s，快速验证结算
			{
				_playback.Seek(Math.Max(0.0, _plan.EndTime - 3.0));
				return;
			}
		}
		if (_pause.IsPaused) return;
		if (e is InputEventMouseButton or InputEventMouseMotion)
			QueuePointerInput(e);
	}

	private void QueuePointerInput(InputEvent e)
	{
		// Auto 演示不接受任何会导致判定的输入：结果严格全 Perfect，触点一律不进入判定管线。
		if (_auto)
			return;

		Vector2 pos;
		int pointerId;
		PointerAction action;
		switch (e)
		{
			case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
				pos = mb.Position;
				pointerId = MousePointerId;
				action = mb.Pressed ? PointerAction.Press : PointerAction.Release;
				break;
			case InputEventScreenTouch screenTouch:
				pos = screenTouch.Position;
				pointerId = screenTouch.Index;
				action = screenTouch.Pressed ? PointerAction.Press : PointerAction.Release;
				break;
			case InputEventScreenDrag drag:
				pos = drag.Position;
				pointerId = drag.Index;
				action = PointerAction.Move;
				break;
			case InputEventMouseMotion motion
				when (motion.ButtonMask & MouseButtonMask.Left) != 0:
				pos = motion.Position;
				pointerId = MousePointerId;
				action = PointerAction.Move;
				break;
			default:
				return;
		}

		var t = _playback.GetSongTime();
		_pointerBuffer.Queue(new PointerInput<Vector2>(
			pointerId, pos, t, action));
	}

	// ---- 输入 ----

	private static TouchTrackMask ProjectedTrackMask(Vector2 pos) =>
		InputJudgeRules.ProjectedTrackMask(pos.X, pos.Y, CenterRegionMinY,
			LeftRegionMaxX, RightRegionMinX);

	private static TouchTrackMask MaskFor(Track track) => track switch
	{
		Track.Center => TouchTrackMask.Center,
		Track.Left => TouchTrackMask.Left,
		Track.Right => TouchTrackMask.Right,
		_ => TouchTrackMask.None,
	};

	/// <summary>
	/// 屏幕位置 → **显示空间**坐标（未镜像）：中轨是面板横向坐标，侧轨是沿判定线的纵向坐标
	/// （纵向不受镜像影响）。镜像**不在这里**施加——判定侧的唯一入口是
	/// <see cref="ChartTouchOf"/> / <see cref="ChartPointOf"/>。
	/// </summary>
	private static double DisplayPositionOf(Track track, Vector2 pos) => track switch
	{
		Track.Center => (pos.X - CenterX0) / PosUnitPx,
		_ => (SideY0 + StageOffsetY - pos.Y) / SideUnit,
	};

	/// <summary>
	/// 屏幕触点 → 判定用 <see cref="TouchSample"/> 的**唯一**入口：
	/// 屏幕区域 → 显示轨道 → 镜像逆映射 → 谱面 (轨道, 坐标)。
	/// <see cref="GameplayMirror.Display"/> 自反，正反两个方向共用同一函数，所以视觉镜像
	/// 与输入逆映射永远互为逆运算；判定侧只认谱面坐标，**别处不要再镜像一次**——
	/// 施加两次会互相抵消（历史上正是这里与 <see cref="DisplayPositionOf"/> 各镜像一次，
	/// 结果输入回到了显示坐标，镜像后 note 判不上）。
	/// </summary>
	private static TouchSample ChartTouchOf(int id, Track screenTrack, Vector2 pos,
		ContactPhase phase) =>
		GameplayMirror.ProjectTouch(id, screenTrack, DisplayPositionOf(screenTrack, pos), phase);

	/// <summary>同上，只要谱面 (轨道, 坐标) 不要 TouchSample（Hold 断触的覆盖检查用）。</summary>
	private static (Track Track, double Position) ChartPointOf(Track screenTrack, Vector2 pos) =>
		GameplayMirror.Display(screenTrack, DisplayPositionOf(screenTrack, pos));

	private static void AddProjectedTouches(List<TouchSample> target, int id,
		Vector2 pos, ContactPhase phase)
	{
		var mask = ProjectedTrackMask(pos);
		// 屏幕区域 -> 显示轨道 -> 谱面轨道/坐标（镜像只在这里施加一次）。
		if ((mask & TouchTrackMask.Center) != 0)
			target.Add(ChartTouchOf(id, Track.Center, pos, phase));
		if ((mask & TouchTrackMask.Left) != 0)
			target.Add(ChartTouchOf(id, Track.Left, pos, phase));
		if ((mask & TouchTrackMask.Right) != 0)
			target.Add(ChartTouchOf(id, Track.Right, pos, phase));
	}

	private void FlushPendingInputs(double frameTime)
	{
		// The reusable buffer owns pointer lifetimes and timestamped frame snapshots;
		// this coordinator only orders them before judge/mixer processing.
		_pointerBuffer.CollectFrame();
		RebuildTrackBuckets();
		AdvancePressWindows(frameTime);
		_mixerEarlyPlacements.Clear();
		_v2InputProtection.Reset();
		for (var i = 0; i < _pointerBuffer.Presses.Count; i++)
		{
			var press = _pointerBuffer.Presses[i];
			ProcessPress(press.Touch, press.Time);
		}
	}

	private static HoldTouchProjection ProjectHoldTouch(int id, Vector2 position)
	{
		var mask = ProjectedTrackMask(position);
		TouchSample? center = null, left = null, right = null;
		Add(Track.Center);
		Add(Track.Left);
		Add(Track.Right);
		return new HoldTouchProjection(center, left, right);

		void Add(Track displayTrack)
		{
			if ((mask & MaskFor(displayTrack)) == 0) return;
			var sample = ChartTouchOf(id, displayTrack, position, ContactPhase.Moved);
			switch (sample.Track)
			{
				case Track.Center: center = sample; break;
				case Track.Left: left = sample; break;
				case Track.Right: right = sample; break;
			}
		}
	}

	private void RebuildTrackBuckets()
	{
		for (var i = 0; i < 3; i++)
			_touchesByTrack[i].Clear();
		for (var i = 0; i < _pointerBuffer.Touches.Count; i++)
		{
			var touch = _pointerBuffer.Touches[i];
			_touchesByTrack[(int)touch.Track].Add(touch);
		}
	}
}
