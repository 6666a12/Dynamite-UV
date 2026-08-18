using System.Text.Json;
using System.Text.Json.Serialization;
using DynamiteUniverse.Ui;
using Godot;

namespace DynamiteUniverse.Game;

/// <summary>本地玩法设置，持久化到 user://settings.json。</summary>
public sealed class GameSettings
{
    private const string SavePath = "user://settings.json";

    public int TimingOffsetMs { get; set; } = 0;
    public int FallSpeedLevel { get; set; } = 10;
    public int MusicVolume { get; set; } = 80;
    public int HitVolume { get; set; } = 80;
    public int UiVolume { get; set; } = 80;
    public UiMotionMode MotionMode { get; set; } = UiMotionMode.Full;

    public double FallSpeedMultiplier => FallSpeedLevel / 10.0;

    public void ResetToDefaults()
    {
        TimingOffsetMs = 0;
        FallSpeedLevel = 10;
        MusicVolume = 80;
        HitVolume = 80;
        UiVolume = 80;
        MotionMode = UiMotionMode.Full;
    }

    public void Load()
    {
        ResetToDefaults();
        if (!Godot.FileAccess.FileExists(SavePath))
        {
            ApplyAudioBuses();
            return;
        }

        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            var data = JsonSerializer.Deserialize<SettingsData>(f.GetAsText());
            if (data != null)
            {
                TimingOffsetMs = data.TimingOffsetMs;
                FallSpeedLevel = data.FallSpeedLevel;
                MusicVolume = data.MusicVolume;
                HitVolume = data.HitVolume;
                UiVolume = data.UiVolume;
                MotionMode = ParseMotionMode(data.MotionMode);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"GameSettings: 读取 {SavePath} 失败，使用默认值。{e.Message}");
        }

        ClampValues();
        ApplyAudioBuses();
    }

    public void Save()
    {
        ClampValues();
        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(JsonSerializer.Serialize(new SettingsData
            {
                TimingOffsetMs = TimingOffsetMs,
                FallSpeedLevel = FallSpeedLevel,
                MusicVolume = MusicVolume,
                HitVolume = HitVolume,
                UiVolume = UiVolume,
                MotionMode = JsonSerializer.SerializeToElement(SerializeMotionMode(MotionMode)),
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            GD.PushWarning($"GameSettings: 写入 {SavePath} 失败。{e.Message}");
        }
    }

    public void ApplyAudioBuses()
    {
        ApplyBus("Music", MusicVolume);
        ApplyBus("Hit", HitVolume);
        ApplyBus("UI", UiVolume);
    }

    private static void ApplyBus(string name, int percent)
    {
        var index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
        }
        AudioServer.SetBusVolumeDb(index, PercentToDb(percent));
    }

    private void ClampValues()
    {
        TimingOffsetMs = Math.Clamp(TimingOffsetMs, -300, 300);
        FallSpeedLevel = Math.Clamp(FallSpeedLevel, 1, 20);
        MusicVolume = Math.Clamp(MusicVolume, 0, 100);
        HitVolume = Math.Clamp(HitVolume, 0, 100);
        UiVolume = Math.Clamp(UiVolume, 0, 100);
        if (!Enum.IsDefined(MotionMode))
            MotionMode = UiMotionMode.Full;
    }

    private static UiMotionMode ParseMotionMode(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return UiMotionMode.Full;

        return value.GetString() switch
        {
            "reduced" => UiMotionMode.Reduced,
            "off" => UiMotionMode.Off,
            _ => UiMotionMode.Full,
        };
    }

    private static string SerializeMotionMode(UiMotionMode mode) => mode switch
    {
        UiMotionMode.Reduced => "reduced",
        UiMotionMode.Off => "off",
        _ => "full",
    };

    private static float PercentToDb(int percent) =>
        percent <= 0 ? -80f : 20f * (float)Math.Log10(percent / 100.0);

    private sealed class SettingsData
    {
        [JsonPropertyName("timingOffsetMs")] public int TimingOffsetMs { get; set; }
        [JsonPropertyName("fallSpeedLevel")] public int FallSpeedLevel { get; set; } = 10;
        [JsonPropertyName("musicVolume")] public int MusicVolume { get; set; } = 80;
        [JsonPropertyName("hitVolume")] public int HitVolume { get; set; } = 80;
        [JsonPropertyName("uiVolume")] public int UiVolume { get; set; } = 80;
        [JsonPropertyName("motionMode")] public JsonElement MotionMode { get; set; }
    }
}
