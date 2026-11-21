using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Judge;

namespace DynamiteUniverse.Ui;

public partial class SongSelect
{
    private SongChannelStrip _channels = null!;

    private void BuildQuickSettings()
    {
        _channels = new SongChannelStrip
        {
            Name = "SongChannelStrip",
            MotionProfileProvider = () => UiMotionProfile.For(GameSession.Settings.MotionMode),
        };
        _settingsRoot.AddChild(_channels);
        _channels.Chosen += ChooseChannel;
    }

    private void ChooseChannel(SongChannelStrip.Port port)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy) return;
        var s = GameSession.Settings;
        switch (port)
        {
            case SongChannelStrip.Port.Standard: SetGameplayMode(GameplayMode.Standard); return;
            case SongChannelStrip.Port.Hardcore: SetGameplayMode(GameplayMode.Hardcore); return;
            case SongChannelStrip.Port.Bleed:
                // The locked ON display is derived from mode; never overwrite Standard's preference.
                if (s.GameplayMode == GameplayMode.Hardcore) return;
                s.BleedEnabled = !s.BleedEnabled; break;
            case SongChannelStrip.Port.Mirror: s.MirrorEnabled = !s.MirrorEnabled; break;
            case SongChannelStrip.Port.Auto: s.AutoEnabled = !s.AutoEnabled; break;
            case SongChannelStrip.Port.Settings:
                StopPreview();
                TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Standard, "OPEN CHANNEL", "SETTINGS");
                return;
            default: return;
        }
        s.Save(); RefreshQuickSettings();
    }

    private void SetGameplayMode(GameplayMode mode)
    {
        if (_localTransitionBusy || TransitionDirector.IsBusy || GameSession.Settings.GameplayMode == mode) return;
        var s = GameSession.Settings;
        s.GameplayMode = mode;
        s.Save();
        // The receipt is decoration after a successful state change; BEST/preload and the lock do not wait for it.
        RefreshQuickSettings(modeCommitted: true);
        BeginRefreshAll();
    }

    private void RefreshQuickSettings(bool modeCommitted = false)
    {
        var s = GameSession.Settings;
        _channels.ShowValues(s.GameplayMode == GameplayMode.Hardcore, s.BleedEnabled, s.MirrorEnabled, s.AutoEnabled, modeCommitted);
    }
}
