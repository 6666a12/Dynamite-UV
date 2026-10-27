using Godot;
using DynamiteUniverse.Audio;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;

public partial class CalibrationScreen
{
    private readonly CalibrationBeatSource _beatSource = new();
    private void StartBeatLoop() => _beatSource.Start(this);
    private double SongPositionSeconds() => _beatSource.SongPositionSeconds();

    // ---- 合成节拍音源常量 ----
    /// <summary>一小节 2.000s（120 BPM 4/4）；2000ms × 44100 = 88200 采样点整，循环无缝。</summary>
    private const double BarSeconds = CalibrationBeatSource.BarSeconds;

    /// <summary>
    /// 当前所有 in-flight note 的下落进度（0 = 刚从轨道顶生成，1 = 顶边压在判定线上）。
    /// 第 N 个小节头（songSec = N · 2s）是一颗 note 的过线时刻，它从
    /// <c>N·2s − fallSeconds</c> 起下落。可见窗口是 head ∈ [songSec, songSec + fall]
    /// （raw ∈ [0,1]），落速慢时窗口跨过多个小节头，在轨 note 因此可能不止一颗。
    /// 返回列表按小节顺序（最老的在最前），绘制顺序即由上到下。
    /// </summary>
    private static IReadOnlyList<float> NoteProgresses(double songSec, double fallSeconds)
    {
        var fall = Math.Max(0.05, fallSeconds);
        var progress = new List<float>();
        var firstBar = Math.Max(0, (int)Math.Ceiling(songSec / BarSeconds));
        var lastBar = (int)Math.Floor((songSec + fall) / BarSeconds);
        for (var bar = firstBar; bar <= lastBar; bar++)
        {
            var head = bar * BarSeconds;
            var raw = (songSec - (head - fall)) / fall;
            if (raw is < 0.0 or > 1.0)
                continue;
            progress.Add((float)raw);
        }

        return progress;
    }
}
