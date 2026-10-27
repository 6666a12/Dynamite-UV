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
