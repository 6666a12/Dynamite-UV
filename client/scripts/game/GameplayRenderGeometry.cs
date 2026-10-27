using Godot;
using DynamiteUniverse.Ui;
using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

/// <summary>Frozen render projections. Numeric rules remain owned by GameplayStageGeometry.</summary>
internal static class GameplayRenderGeometry
{
	internal const float CenterLineY = GameplayStageGeometry.CenterLineY;
	internal const float CenterX0 = GameplayStageGeometry.CenterX0;
	internal const float PosUnitPx = GameplayStageGeometry.CenterUnitPx;
	internal const float LeftLineX = GameplayStageGeometry.LeftLineX;
	internal const float RightLineX = GameplayStageGeometry.RightLineX;
	internal const float SideDistScale = GameplayStageGeometry.SideDistanceScale;
	internal const float SideLeadPx = GameplayStageGeometry.SideLeadPx; // = 790，与 TravelPx 相同
	internal const float SideY0 = GameplayStageGeometry.SideY0;
	internal const float SideUnit = GameplayStageGeometry.SideUnitPx;
	internal const float SidePosUnitPx = GameplayStageGeometry.SidePositionUnitPx;
	internal const float SideNoteLenUnitPx = GameplayStageGeometry.SideNoteLengthUnitPx;
	internal const float BaseFallSpeedPx = GameplayStageGeometry.BaseFallSpeedPx;
	internal const float TravelPx = GameplayStageGeometry.TravelPx;
	internal const double PostHitViewLifetimeSec = 0.77;
	internal const float NoteVisualScale = GameplayStageGeometry.NoteVisualScale;

	// 将段 a→b（中心线端点，半宽 wa/wb）按判定线裁剪：a 端已过线时用
	// 中心线与判定线的交点取而代之（面板边缘沿线滑动）；整段过线返回 false。
	// 链按时间有序，from 端恒领先于 b 端，故 b 不会单独过线。
	internal static bool ClipToLine(Track track, ref Vector2 a, ref Vector2 b,
		ref float wa, ref float wb)
	{
		return GameplayVisualMapper.ClipToJudgeLine(track, ref a, ref b, ref wa, ref wb,
			GameplayMirror.Enabled);
	 }

	internal static Vector2 PositionPastLine(Note note, float distancePx) =>
		PositionPastLine(note.Track, PositionAt(note, 0f), distancePx);

	internal static Vector2 PositionPastLine(Track track, Vector2 linePosition, float distancePx)
	{
		return GameplayMirror.DisplayTrack(track) switch
		{
			Track.Center => linePosition + new Vector2(0f, distancePx),
			Track.Left => linePosition - new Vector2(distancePx, 0f),
			_ => linePosition + new Vector2(distancePx, 0f),
		};
	}

	internal static float DistancePastJudgeLine(Track track, Vector2 position) =>
		Mathf.Max(0f, GameplayMirror.DisplayTrack(track) switch
		{
			Track.Center => position.Y - CenterLineY,
			Track.Left => LeftLineX - position.X,
			_ => position.X - RightLineX,
		});

	internal static float PastLineDistanceFromRemaining(Track track, float remaining) =>
		Mathf.Max(0f, -remaining * (track == Track.Center ? 1f : SideDistScale));

	internal static Vector2 PositionAt(Note n, float rem)
		=> GameplayVisualMapper.PositionAt(n, rem, GameplayMirror.Enabled);

	internal static float TopFadeAlpha(float y) =>
		y <= 128f ? 0f : y >= 200f ? 1f : (y - 128f) / 72f;

	internal static Vector2 SizeFor(Note n)
		=> GameplayVisualMapper.SizeFor(n);

	internal static Color ColorFor(Note n) => GameplayVisualMapper.ColorFor(n);

	internal static bool UsesOrdinaryMissFallthrough(NoteType type) => type is
		NoteType.Tap or NoteType.ExTap or NoteType.Drag or
		NoteType.HoldHead or NoteType.HoldNode;

	internal static Color HitAccentFor(NoteType type) => NoteVisualSpec.HitAccent(type);

	internal static HitEffectKind HitEffectKindFor(NoteType type) => NoteVisualSpec.HitEffect(type);

	// 连接体半宽：Hold 是带亮边的宽面板；Mixer 只有节点间细连线，
	// 不生成完整宽面板；其他链沿用窄缎带。
	internal static float LinkHalfFor(Note n)
	{
		return GameplayVisualMapper.LinkHalfFor(n);
	 }

	internal static Color LinkColorFor(Note n) => GameplayVisualMapper.LinkColorFor(n);

	internal static void UpdateSustainHeadGeometry(NoteView view, NoteBounds bounds,
		double position, float pastLineDistance = 0f)
	{
		view.SetSize(GameplayVisualMapper.SizeFor(
			view.Model.Type, view.Model.Track, bounds.Right - bounds.Left));
		view.Position = PositionPastLine(view.Model.Track,
			TrackPositionAtLine(view.Model.Track, position), pastLineDistance);
		view.SetDepthAlpha(1f);
	}

	internal static Vector2 TrackPositionAtLine(Track track, double position)
	{
		// 谱面坐标 -> 屏幕：轨道与中轨水平坐标都过一层镜像映射。
		var displayTrack = GameplayMirror.DisplayTrack(track);
		var displayPosition = track == Track.Center
			? GameplayMirror.DisplayCenter(position) : position;
		return displayTrack switch
		{
			Track.Center => new Vector2(
				CenterX0 + (float)displayPosition * PosUnitPx, CenterLineY),
			Track.Left => new Vector2(
				LeftLineX, SideY0 - (float)displayPosition * SideUnit),
			_ => new Vector2(
				RightLineX, SideY0 - (float)displayPosition * SideUnit),
		};
	}

	internal static float SustainSpanPx(Track track, NoteBounds bounds)
	{
		var width = (float)Math.Max(0.0, bounds.Right - bounds.Left);
		return Mathf.Max(24f, width * (track == Track.Center
			? PosUnitPx : SideNoteLenUnitPx) * NoteVisualScale);
	}
}
