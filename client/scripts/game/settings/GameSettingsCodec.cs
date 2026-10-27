using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamiteUniverse.Game;

public sealed record GameSettingsSnapshot
{
    public int TimingOffsetMs { get; init; }
    public int FallSpeedLevel { get; init; } = 10;
    public int MusicVolume { get; init; } = 80;
    public int HitVolume { get; init; } = 80;
    public int UiVolume { get; init; } = 80;
    public string MotionMode { get; init; } = "full";
    public string HitEffectLevel { get; init; } = "full";
    public int NoteBrightnessPercent { get; init; } = 130;
    public int FlashIntensityPercent { get; init; } = 100;
    public int HitResidualMs { get; init; } = 300;
    public bool JudgeTextEnabled { get; init; } = true;
    public string GameplayMode { get; init; } = "standard";
    public bool AutoEnabled { get; init; }
    public bool BleedEnabled { get; init; }
    public bool MirrorEnabled { get; init; }
    public int CalibrationTargetSamples { get; init; } = 16;
    public int ActiveNotePalette { get; init; }
    public bool BurstColorFollowsNote { get; init; }
    public IReadOnlyList<GameSettingsPaletteSlot> NotePalettes { get; init; } = CreateDefaultPalettes();

    private static IReadOnlyList<GameSettingsPaletteSlot> CreateDefaultPalettes()
    {
        string[] factory = ["40C9FF", "B5EBFF", "FFC66A", "56EEA5", "FF75BB", "FF6977"];
        return Enumerable.Range(0, 10)
            .Select(i => new GameSettingsPaletteSlot(i == 0 ? "DEFAULT" : $"PRESET {i + 1:00}", i > 0, factory))
            .ToArray();
    }
}

public sealed record GameSettingsPaletteSlot(string Name, bool Empty, IReadOnlyList<string> Colors);

public static class GameSettingsCodec
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static GameSettingsSnapshot Decode(string json)
    {
        var data = JsonSerializer.Deserialize<SettingsData>(json);
        if (data is null)
            return new GameSettingsSnapshot();

        var hitEffect = data.HitEffectLevel.ValueKind == JsonValueKind.Undefined
            ? data.GameplayEffectsEnabled == false ? "off" : "full"
            : ParseString(data.HitEffectLevel, "full", ("reduced", "reduced"), ("off", "off"));

        return new GameSettingsSnapshot
        {
            TimingOffsetMs = data.TimingOffsetMs,
            FallSpeedLevel = data.FallSpeedLevel,
            MusicVolume = data.MusicVolume,
            HitVolume = data.HitVolume,
            UiVolume = data.UiVolume,
            MotionMode = ParseString(data.MotionMode, "full", ("reduced", "reduced"), ("off", "off")),
            HitEffectLevel = hitEffect,
            NoteBrightnessPercent = data.NoteBrightnessPercent,
            FlashIntensityPercent = data.FlashIntensityPercent,
            HitResidualMs = data.HitResidualMs,
            JudgeTextEnabled = data.JudgeTextEnabled,
            GameplayMode = ParseString(data.GameplayMode, "standard", ("hardcore", "hardcore"), ("bleed", "bleed")),
            AutoEnabled = data.AutoEnabled,
            BleedEnabled = data.BleedEnabled,
            MirrorEnabled = data.MirrorEnabled,
            CalibrationTargetSamples = data.CalibrationTargetSamples,
            ActiveNotePalette = data.ActiveNotePalette,
            BurstColorFollowsNote = data.BurstColorFollowsNote,
            NotePalettes = RestorePalettes(data.NotePalettes),
        };
    }

    public static string Encode(GameSettingsSnapshot snapshot)
    {
        var data = new SettingsData
        {
            TimingOffsetMs = snapshot.TimingOffsetMs,
            FallSpeedLevel = snapshot.FallSpeedLevel,
            MusicVolume = snapshot.MusicVolume,
            HitVolume = snapshot.HitVolume,
            UiVolume = snapshot.UiVolume,
            MotionMode = JsonSerializer.SerializeToElement(NormalizeMotion(snapshot.MotionMode)),
            GameplayEffectsEnabled = snapshot.HitEffectLevel != "off",
            HitEffectLevel = JsonSerializer.SerializeToElement(NormalizeHitEffect(snapshot.HitEffectLevel)),
            NoteBrightnessPercent = snapshot.NoteBrightnessPercent,
            FlashIntensityPercent = snapshot.FlashIntensityPercent,
            HitResidualMs = snapshot.HitResidualMs,
            JudgeTextEnabled = snapshot.JudgeTextEnabled,
            GameplayMode = JsonSerializer.SerializeToElement(NormalizeGameplayMode(snapshot.GameplayMode)),
            AutoEnabled = snapshot.AutoEnabled,
            BleedEnabled = snapshot.BleedEnabled,
            MirrorEnabled = snapshot.MirrorEnabled,
            CalibrationTargetSamples = snapshot.CalibrationTargetSamples,
            ActiveNotePalette = snapshot.ActiveNotePalette,
            BurstColorFollowsNote = snapshot.BurstColorFollowsNote,
            NotePalettes = snapshot.NotePalettes.Select((slot, index) => new NotePaletteData
            {
                Name = slot.Empty ? "" : NormalizeName(slot.Name, index),
                Empty = slot.Empty,
                Colors = slot.Colors.ToList(),
            }).ToList(),
        };
        return JsonSerializer.Serialize(data, WriteOptions);
    }

    private static IReadOnlyList<GameSettingsPaletteSlot> RestorePalettes(List<NotePaletteData>? data)
    {
        string[] factory = ["40C9FF", "B5EBFF", "FFC66A", "56EEA5", "FF75BB", "FF6977"];
        var slots = new GameSettingsPaletteSlot[10];
        for (var i = 0; i < slots.Length; i++)
        {
            var entry = data is not null && i < data.Count ? data[i] : null;
            var empty = i > 0 && (entry is null || entry.Empty);
            var colors = factory.ToArray();
            if (!empty && entry?.Colors is { Count: >= 6 })
                for (var channel = 0; channel < 6; channel++)
                    if (TryNormalizeHex(entry.Colors[channel], out var color))
                        colors[channel] = color;
            slots[i] = new GameSettingsPaletteSlot(
                empty ? $"PRESET {i + 1:00}" : NormalizeName(entry?.Name, i), empty, colors);
        }
        return slots;
    }

    private static string ParseString(JsonElement value, string fallback, params (string Input, string Output)[] values)
    {
        if (value.ValueKind != JsonValueKind.String)
            return fallback;
        var text = value.GetString();
        foreach (var pair in values)
            if (text == pair.Input)
                return pair.Output;
        return fallback;
    }

    private static string NormalizeMotion(string? value) => value is "reduced" or "off" ? value : "full";
    private static string NormalizeHitEffect(string? value) => value is "reduced" or "off" ? value : "full";
    private static string NormalizeGameplayMode(string? value) => value is "hardcore" or "bleed" ? value : "standard";

    private static string NormalizeName(string? value, int index)
    {
        var text = (value ?? "").ToUpperInvariant();
        var name = new string(text.Where(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-')
            .Take(12).ToArray()).Trim();
        return name.Length == 0 ? $"PRESET {index + 1:00}" : name;
    }

    private static bool TryNormalizeHex(string? value, out string normalized)
    {
        normalized = "";
        if (value is null)
            return false;
        var text = value.StartsWith('#') ? value[1..] : value;
        if (text.Length != 6 || text.Any(ch => ch is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
            return false;
        normalized = text.ToUpperInvariant();
        return true;
    }

    private sealed class SettingsData
    {
        [JsonPropertyName("timingOffsetMs")] public int TimingOffsetMs { get; set; }
        [JsonPropertyName("fallSpeedLevel")] public int FallSpeedLevel { get; set; } = 10;
        [JsonPropertyName("musicVolume")] public int MusicVolume { get; set; } = 80;
        [JsonPropertyName("hitVolume")] public int HitVolume { get; set; } = 80;
        [JsonPropertyName("uiVolume")] public int UiVolume { get; set; } = 80;
        [JsonPropertyName("motionMode")] public JsonElement MotionMode { get; set; }
        [JsonPropertyName("gameplayEffectsEnabled")] public bool? GameplayEffectsEnabled { get; set; }
        [JsonPropertyName("hitEffectLevel")] public JsonElement HitEffectLevel { get; set; }
        [JsonPropertyName("noteBrightnessPercent")] public int NoteBrightnessPercent { get; set; } = 130;
        [JsonPropertyName("flashIntensityPercent")] public int FlashIntensityPercent { get; set; } = 100;
        [JsonPropertyName("hitResidualMs")] public int HitResidualMs { get; set; } = 300;
        [JsonPropertyName("judgeTextEnabled")] public bool JudgeTextEnabled { get; set; } = true;
        [JsonPropertyName("gameplayMode")] public JsonElement GameplayMode { get; set; }
        [JsonPropertyName("autoEnabled")] public bool AutoEnabled { get; set; }
        [JsonPropertyName("bleedEnabled")] public bool BleedEnabled { get; set; }
        [JsonPropertyName("mirrorEnabled")] public bool MirrorEnabled { get; set; }
        [JsonPropertyName("calibrationTargetSamples")] public int CalibrationTargetSamples { get; set; } = 16;
        [JsonPropertyName("activeNotePalette")] public int ActiveNotePalette { get; set; }
        [JsonPropertyName("burstColorFollowsNote")] public bool BurstColorFollowsNote { get; set; }
        [JsonPropertyName("notePalettes")] public List<NotePaletteData>? NotePalettes { get; set; }
    }

    private sealed class NotePaletteData
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("empty")] public bool Empty { get; set; }
        [JsonPropertyName("colors")] public List<string>? Colors { get; set; }
    }
}
