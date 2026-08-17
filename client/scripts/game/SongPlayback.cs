using Godot;
using DuxCommunity.Audio;

namespace DuxCommunity.Game;

/// <summary>Owns the Godot player plus SongClock without changing clock policy or pause semantics.</summary>
internal sealed class SongPlayback
{
    private readonly AudioStreamPlayer _player;

    public SongPlayback(Node parent, string audioPath, double userOffsetMs,
        V2IntegrationVerification? verification)
    {
        _player = new AudioStreamPlayer
        {
            Stream = Res.LoadAudio(audioPath),
            Bus = "Music",
        };
        parent.AddChild(_player);

        Clock = new SongClock();
        if (verification is { FixedClockEnabled: true } fixedVerification)
            Clock.EnableFixedFrameClock(fixedVerification.FixedFps);
        Clock.Attach(_player);
        Clock.UserOffsetMs = userOffsetMs;
        parent.AddChild(Clock);
    }

    public SongClock Clock { get; }
    public bool HasNaturallyEnded => !Clock.FixedFrameEnabled && !Clock.ManualFallback &&
        Clock.IsPlaying && !_player.Playing;

    public void Play(double startSecond) => Clock.Play(startSecond);
    public void Pause() => Clock.Pause();
    public void Resume() => Clock.Resume();
    public void Seek(double second) => Clock.Seek(second);
    public void AdvanceFixedFrame() => Clock.AdvanceFixedFrame();
    public double GetSongTime() => Clock.GetSongTime();
}
