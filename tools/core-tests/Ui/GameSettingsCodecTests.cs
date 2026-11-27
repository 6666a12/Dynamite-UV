using System.Text.Json;

namespace CoreTests;

internal static class GameSettingsCodecTests
{
    public static void Run(Action<bool, string> check)
    {
        var defaults = DynamiteUniverse.Game.GameSettingsCodec.Decode("{}");
        check(defaults.FallSpeedLevel == 10 && defaults.MusicVolume == 80 &&
              defaults.NoteBrightnessPercent == 130 && defaults.JudgeTextEnabled &&
              defaults.NotePalettes.Count == 10 && defaults.NotePalettes[1].Empty,
            "missing settings and palette fields use the established defaults");

        var migrated = DynamiteUniverse.Game.GameSettingsCodec.Decode(
            """{"gameplayEffectsEnabled":false,"motionMode":"off","gameplayMode":"hardcore"}""");
        check(migrated.HitEffectLevel == "off" && migrated.MotionMode == "off" &&
              migrated.GameplayMode == "hardcore",
            "legacy effects boolean migrates while string modes retain their values");
        check(defaults.StoryboardEnabled && migrated.StoryboardEnabled,
            "missing Storyboard preference defaults to ON even when legacy effects and UI motion are OFF");

        var storyboardOff = defaults with { StoryboardEnabled = false, HitEffectLevel = "reduced" };
        var encodedOff = DynamiteUniverse.Game.GameSettingsCodec.Encode(storyboardOff);
        using var offDocument = JsonDocument.Parse(encodedOff);
        check(offDocument.RootElement.TryGetProperty("storyboardEnabled", out var offValue) &&
              offValue.ValueKind == JsonValueKind.False,
            "Storyboard OFF is stored as JSON false under storyboardEnabled");
        var restoredOff = DynamiteUniverse.Game.GameSettingsCodec.Decode(encodedOff);
        check(!restoredOff.StoryboardEnabled && restoredOff.MotionMode == "full" &&
              restoredOff.HitEffectLevel == "reduced",
            "Storyboard OFF roundtrips without changing UI motion or hit effects");

        var storyboardOn = DynamiteUniverse.Game.GameSettingsCodec.Decode(
            """{"storyboardEnabled":true,"motionMode":"reduced","hitEffectLevel":"off"}""");
        check(storyboardOn.StoryboardEnabled && storyboardOn.MotionMode == "reduced" &&
              storyboardOn.HitEffectLevel == "off",
            "Storyboard ON loads independently of UI motion and hit effects");

        var malformedStoryboard = DynamiteUniverse.Game.GameSettingsCodec.Decode(
            """{"storyboardEnabled":"false","musicVolume":37}""");
        check(malformedStoryboard.StoryboardEnabled && malformedStoryboard.MusicVolume == 37,
            "a non-boolean Storyboard preference defaults to ON without discarding other settings");

        var dim = defaults with { StoryboardBrightnessPercent = 37, StoryboardEnabled = false };
        var restoredDim = DynamiteUniverse.Game.GameSettingsCodec.Decode(DynamiteUniverse.Game.GameSettingsCodec.Encode(dim));
        check(defaults.StoryboardBrightnessPercent == 100 && !restoredDim.StoryboardEnabled &&
              restoredDim.StoryboardBrightnessPercent == 37 && restoredDim.NoteBrightnessPercent == defaults.NoteBrightnessPercent,
            "Storyboard RGB brightness roundtrips independently of both the toggle and note brightness");
        check(DynamiteUniverse.Game.GameSettingsCodec.Decode("""{"storyboardBrightnessPercent":-50}""").StoryboardBrightnessPercent == 0 &&
              DynamiteUniverse.Game.GameSettingsCodec.Decode("""{"storyboardBrightnessPercent":105}""").StoryboardBrightnessPercent == 100 &&
              DynamiteUniverse.Game.GameSettingsCodec.Decode("""{"storyboardBrightnessPercent":"dark"}""").StoryboardBrightnessPercent == 100,
            "Storyboard brightness clamps to 0–100; invalid types retain the default");
        var tier = DynamiteUniverse.Game.GameSettingsCodec.Decode(
            DynamiteUniverse.Game.GameSettingsCodec.Encode(defaults with { StoryboardMemoryMiB = 128 }));
        check(defaults.StoryboardMemoryMiB == 0 && tier.StoryboardMemoryMiB == 128 &&
              DynamiteUniverse.Game.GameSettingsCodec.Decode("""{"storyboardMemoryMiB":256}""").StoryboardMemoryMiB == 256 &&
              DynamiteUniverse.Game.GameSettingsCodec.Decode("""{"storyboardMemoryMiB":512}""").StoryboardMemoryMiB == 0 &&
              DynamiteUniverse.Game.GameSettingsCodec.Decode("""{"storyboardMemoryMiB":"128"}""").StoryboardMemoryMiB == 0,
            "only 128/256 MiB persist; absent or invalid overrides use the platform default");

        var palettes = defaults.NotePalettes.ToArray();
        palettes[1] = new DynamiteUniverse.Game.GameSettingsPaletteSlot(
            "MINT", false, ["123456", "B5EBFF", "FFC66A", "56EEA5", "FF75BB", "FF6977"]);
        var snapshot = defaults with
        {
            ActiveNotePalette = 1,
            BurstColorFollowsNote = true,
            NotePalettes = palettes,
            GameplayMode = "bleed",
            MotionMode = "reduced",
            HitEffectLevel = "reduced",
        };
        var restored = DynamiteUniverse.Game.GameSettingsCodec.Decode(
            DynamiteUniverse.Game.GameSettingsCodec.Encode(snapshot));
        check(restored.ActiveNotePalette == 1 && restored.BurstColorFollowsNote &&
              !restored.NotePalettes[1].Empty && restored.NotePalettes[1].Name == "MINT" &&
              restored.NotePalettes[1].Colors[0] == "123456" && restored.GameplayMode == "bleed" &&
              restored.MotionMode == "reduced" && restored.HitEffectLevel == "reduced",
            "roundtrip preserves palette slots, active selection, color, burst and modes");
    }
}
