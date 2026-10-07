using System.Text.Json;
using System.Text.Json.Serialization;
using DynamiteUniverse.Shared.Judge;
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
    public bool GameplayEffectsEnabled { get; set; } = true;

    /// <summary>
    /// 判定模式：Standard 按难度取预设；Hardcore 对任何难度强制 Hardcore 实例
    /// （Hard 窗口 ×0.5）。Bleed 为预留成员，见 docs/gameplay-spec.md「游玩模式」。
    /// </summary>
    public GameplayMode GameplayMode { get; set; } = GameplayMode.Standard;

    /// <summary>自动演示：开启后 gameplay 由 Auto 驱动，不写成绩。由选曲页 AUTO 开关写入。</summary>
    public bool AutoEnabled { get; set; } = false;

    /// <summary>
    /// BLEED 游玩修饰（选曲页开关）。机制本身仍是预留，没有判定逻辑；HARDCORE 下强制开启并锁定，
    /// 锁定期不改写本值，切回 STANDARD 时恢复用户此前的选择。见 docs/gameplay-spec.md「游玩模式」。
    /// </summary>
    public bool BleedEnabled { get; set; } = false;

    /// <summary>谱面左右镜像（选曲页开关）：侧轨互换、中轨水平镜像。</summary>
    public bool MirrorEnabled { get; set; } = false;

    /// <summary>BLEED 的实际生效值：HARDCORE 强制开启（锁定态不改写持久化值）。</summary>
    public bool BleedEffective => GameplayMode == GameplayMode.Hardcore || BleedEnabled;

    public double FallSpeedMultiplier => FallSpeedLevel / 10.0;

    public void ResetToDefaults()
    {
        TimingOffsetMs = 0;
        FallSpeedLevel = 10;
        MusicVolume = 80;
        HitVolume = 80;
        UiVolume = 80;
        MotionMode = UiMotionMode.Full;
        GameplayEffectsEnabled = true;
        GameplayMode = GameplayMode.Standard;
        AutoEnabled = false;
        BleedEnabled = false;
        MirrorEnabled = false;
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
                GameplayEffectsEnabled = data.GameplayEffectsEnabled;
                MotionMode = ParseMotionMode(data.MotionMode);
                GameplayMode = ParseGameplayMode(data.GameplayMode);
                AutoEnabled = data.AutoEnabled;
                BleedEnabled = data.BleedEnabled;
                MirrorEnabled = data.MirrorEnabled;
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
                GameplayEffectsEnabled = GameplayEffectsEnabled,
                GameplayMode = JsonSerializer.SerializeToElement(SerializeGameplayMode(GameplayMode)),
                AutoEnabled = AutoEnabled,
                BleedEnabled = BleedEnabled,
                MirrorEnabled = MirrorEnabled,
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
        if (!Enum.IsDefined(GameplayMode))
            GameplayMode = GameplayMode.Standard;
    }

    /// <summary>Bleed 尚无实现，解析时接受但运行时等同 Standard（走难度映射）。</summary>
    private static GameplayMode ParseGameplayMode(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return GameplayMode.Standard;

        return value.GetString() switch
        {
            "hardcore" => GameplayMode.Hardcore,
            "bleed" => GameplayMode.Bleed,
            _ => GameplayMode.Standard,
        };
    }

    private static string SerializeGameplayMode(GameplayMode mode) => mode switch
    {
        GameplayMode.Hardcore => "hardcore",
        GameplayMode.Bleed => "bleed",
        _ => "standard",
    };

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
        [JsonPropertyName("gameplayEffectsEnabled")] public bool GameplayEffectsEnabled { get; set; } = true;
        [JsonPropertyName("gameplayMode")] public JsonElement GameplayMode { get; set; }
        [JsonPropertyName("autoEnabled")] public bool AutoEnabled { get; set; }
        [JsonPropertyName("bleedEnabled")] public bool BleedEnabled { get; set; }
        [JsonPropertyName("mirrorEnabled")] public bool MirrorEnabled { get; set; }
    }
}
