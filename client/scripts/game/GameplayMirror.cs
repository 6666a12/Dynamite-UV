using DynamiteUniverse.Shared.Chart;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Game;

/// <summary>
/// 谱面左右镜像（<see cref="GameSettings.MirrorEnabled"/>）。
/// 只改**屏幕映射**，不改任何判定数据：侧轨 Left↔Right 互换（含触点归属、
/// 判定线、note 视觉与粒子朝向），Center 轨的水平位置关于面板中轴镜像、宽度不变。
/// 映射规则本体在 <see cref="GameplayStageGeometry.MirroredDisplay"/>（与判定几何同源、可单测），
/// 这里只负责读设置。
/// </summary>
internal static class GameplayMirror
{
	public static bool Enabled => GameSession.Settings.MirrorEnabled;

	/// <summary>显示轨道：Left↔Right 互换，Center 不变（自反）。</summary>
	public static Track DisplayTrack(Track track) =>
		GameplayStageGeometry.MirroredTrack(track, Enabled);

	/// <summary>中轨水平坐标关于面板中轴镜像（自反）。侧轨位置是沿判定线的 y，不参与。</summary>
	public static double DisplayCenter(double center) =>
		GameplayStageGeometry.MirroredDisplay(Track.Center, center, Enabled).Center;

	/// <summary>谱面 (轨道, 坐标) → 屏幕 (轨道, 坐标) 的单一映射入口。</summary>
	public static (Track Track, double Center) Display(Track track, double center) =>
		GameplayStageGeometry.MirroredDisplay(track, center, Enabled);

	/// <summary>屏幕触点到谱面数据的投影：映射自反，正反两侧用同一函数。</summary>
	public static TouchSample ProjectTouch(int id, Track screenTrack, double screenCenter,
		ContactPhase phase)
	{
		var (track, center) = Display(screenTrack, screenCenter);
		return new(id, track, center, phase);
	}
}
