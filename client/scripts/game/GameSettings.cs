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

    /// <summary>打击特效档位（取代旧的 GameplayEffectsEnabled 布尔，见 docs/gameplay-spec.md）。</summary>
    public HitEffectLevel HitEffectLevel { get; set; } = HitEffectLevel.Full;

    /// <summary>音符亮度百分比：100 为设计原值（0.72 / 1.28 / 2.57），本轮实机基线是 130。</summary>
    public int NoteBrightnessPercent { get; set; } = 130;

    /// <summary>打击爆发尺度百分比：100 为设计原值（半径与展开范围同比例缩放）。</summary>
    public int FlashIntensityPercent { get; set; } = 100;

    /// <summary>命中残影时长（毫秒）；0 为关闭。允许 0 / 200 / 300 / 450。</summary>
    public int HitResidualMs { get; set; } = 300;

    /// <summary>PERFECT / GREAT / GOOD / MISS 判定浮字是否显示。</summary>
    public bool JudgeTextEnabled { get; set; } = true;

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

    /// <summary>
    /// 校准向导的目标有效样本数：8–32、每档 4，默认 16。只影响校准页的采样进度门槛，
    /// 不参与判定。取值范围与钳制口径见 <see cref="Ui.CalibrationStats"/>。
    /// </summary>
    public int CalibrationTargetSamples { get; set; } = Ui.CalibrationStats.DefaultTargetSamples;

    /// <summary>
    /// 十个音符配色预设槽。槽 1 是工厂预设，其余初始为空槽；
    /// 只有配色页的 CLEAR 会清空槽位，全局 RESET DEFAULTS 不碰这里。
    /// </summary>
    public List<NotePalette> NotePalettes { get; private set; } = NotePalette.CreateLibrary();

    /// <summary>当前选中的预设槽下标；始终指向非空槽。</summary>
    public int ActiveNotePaletteIndex { get; private set; }

    /// <summary>打击爆发跟随音符色（配色页 BURST · NOTE），默认按判定色。</summary>
    public bool BurstColorFollowsNote { get; set; }

    public NotePalette ActiveNotePalette =>
        ActiveNotePaletteIndex >= 0 && ActiveNotePaletteIndex < NotePalettes.Count &&
        !NotePalettes[ActiveNotePaletteIndex].IsEmpty
            ? NotePalettes[ActiveNotePaletteIndex]
            : NotePalettes[0];

    /// <summary>选中某个非空槽；越界或空槽返回 false（空槽先走命名对话框）。</summary>
    public bool SelectNotePalette(int index)
    {
        if (index < 0 || index >= NotePalettes.Count || NotePalettes[index].IsEmpty)
            return false;
        ActiveNotePaletteIndex = index;
        return true;
    }

    /// <summary>配色页 UNDO 用：整库还原后重新归一化下标与槽名。</summary>
    public void RestoreNotePaletteState(IReadOnlyList<NotePalette> slots, int activeIndex)
    {
        for (var i = 0; i < NotePalettes.Count && i < slots.Count; i++)
            NotePalettes[i].CopyFrom(slots[i]);
        ActiveNotePaletteIndex = activeIndex;
        NormalizeNotePalettes();
    }

    /// <summary>BLEED 的实际生效值：HARDCORE 强制开启（锁定态不改写持久化值）。</summary>
    public bool BleedEffective => GameplayMode == GameplayMode.Hardcore || BleedEnabled;

    public double FallSpeedMultiplier => FallSpeedLevel / 10.0;

    public float NoteBrightnessScale => NoteBrightnessPercent / 100f;

    public float FlashIntensityScale => FlashIntensityPercent / 100f;

    public float HitResidualSeconds => HitResidualMs / 1000f;

    public void ResetToDefaults()
    {
        TimingOffsetMs = 0;
        FallSpeedLevel = 10;
        MusicVolume = 80;
        HitVolume = 80;
        UiVolume = 80;
        MotionMode = UiMotionMode.Full;
        HitEffectLevel = HitEffectLevel.Full;
        NoteBrightnessPercent = 130;
        FlashIntensityPercent = 100;
        HitResidualMs = 300;
        JudgeTextEnabled = true;
        GameplayMode = GameplayMode.Standard;
        AutoEnabled = false;
        BleedEnabled = false;
        MirrorEnabled = false;
        CalibrationTargetSamples = Ui.CalibrationStats.DefaultTargetSamples;
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
                // 旧存档只有 gameplayEffectsEnabled：缺失新档位时按布尔迁移（false → OFF）。
                HitEffectLevel = data.HitEffectLevel.ValueKind == JsonValueKind.Undefined
                    ? data.GameplayEffectsEnabled == false ? HitEffectLevel.Off : HitEffectLevel.Full
                    : ParseHitEffectLevel(data.HitEffectLevel);
                NoteBrightnessPercent = data.NoteBrightnessPercent;
                FlashIntensityPercent = data.FlashIntensityPercent;
                HitResidualMs = data.HitResidualMs;
                JudgeTextEnabled = data.JudgeTextEnabled;
                MotionMode = ParseMotionMode(data.MotionMode);
                GameplayMode = ParseGameplayMode(data.GameplayMode);
                AutoEnabled = data.AutoEnabled;
                BleedEnabled = data.BleedEnabled;
                MirrorEnabled = data.MirrorEnabled;
                CalibrationTargetSamples = data.CalibrationTargetSamples;
                ActiveNotePaletteIndex = data.ActiveNotePalette;
                BurstColorFollowsNote = data.BurstColorFollowsNote;
                ApplyNotePalettes(data.NotePalettes);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"GameSettings: 读取 {SavePath} 失败，使用默认值。{e.Message}");
        }

        ClampValues();
        ApplyAudioBuses();
    }

    public void Save() => TrySave();

    /// <summary>Explicit editors need to keep their draft dirty when persistence fails.</summary>
    public bool TrySave()
    {
        ClampValues();
        try
        {
            var json = JsonSerializer.Serialize(new SettingsData
            {
                TimingOffsetMs = TimingOffsetMs,
                FallSpeedLevel = FallSpeedLevel,
                MusicVolume = MusicVolume,
                HitVolume = HitVolume,
                UiVolume = UiVolume,
                MotionMode = JsonSerializer.SerializeToElement(SerializeMotionMode(MotionMode)),
                GameplayEffectsEnabled = HitEffectLevel != HitEffectLevel.Off,
                HitEffectLevel = JsonSerializer.SerializeToElement(SerializeHitEffectLevel(HitEffectLevel)),
                NoteBrightnessPercent = NoteBrightnessPercent,
                FlashIntensityPercent = FlashIntensityPercent,
                HitResidualMs = HitResidualMs,
                JudgeTextEnabled = JudgeTextEnabled,
                GameplayMode = JsonSerializer.SerializeToElement(SerializeGameplayMode(GameplayMode)),
                AutoEnabled = AutoEnabled,
                BleedEnabled = BleedEnabled,
                MirrorEnabled = MirrorEnabled,
                CalibrationTargetSamples = CalibrationTargetSamples,
                ActiveNotePalette = ActiveNotePaletteIndex,
                BurstColorFollowsNote = BurstColorFollowsNote,
                NotePalettes = NotePalettes.Select((slot, index) => new NotePaletteData
                {
                    Name = slot.IsEmpty ? "" : NotePalette.NormalizeName(slot.Name, index),
                    Empty = slot.IsEmpty,
                    Colors = IndexColors(slot),
                }).ToList(),
            }, new JsonSerializerOptions { WriteIndented = true });
            SettingsFilePersistence.Write(ProjectSettings.GlobalizePath(SavePath), json);
            return true;
        }
        catch (Exception e)
        {
            GD.PushWarning($"GameSettings: 写入 {SavePath} 失败。{e.Message}");
            return false;
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
        CalibrationTargetSamples = Ui.CalibrationStats.NormalizeTargetSamples(CalibrationTargetSamples);
        FallSpeedLevel = Math.Clamp(FallSpeedLevel, 1, 20);
        MusicVolume = Math.Clamp(MusicVolume, 0, 100);
        HitVolume = Math.Clamp(HitVolume, 0, 100);
        UiVolume = Math.Clamp(UiVolume, 0, 100);
        if (!Enum.IsDefined(MotionMode))
            MotionMode = UiMotionMode.Full;
        if (!Enum.IsDefined(GameplayMode))
            GameplayMode = GameplayMode.Standard;
        if (!Enum.IsDefined(HitEffectLevel))
            HitEffectLevel = HitEffectLevel.Full;
        NoteBrightnessPercent = ClampToStep(NoteBrightnessPercent, 80, 140);
        FlashIntensityPercent = ClampToStep(FlashIntensityPercent, 60, 150);
        HitResidualMs = NormalizeResidualMs(HitResidualMs);
        NormalizeNotePalettes();
    }

    /// <summary>把读到的配色槽并回运行时列表；缺字段的槽按空槽处理，颜色解析失败保留工厂色。</summary>
    private void ApplyNotePalettes(List<NotePaletteData>? data)
    {
        NotePalettes.Clear();
        for (var i = 0; i < NotePalette.SlotCount; i++)
        {
            var slot = NotePalette.CreateFactory(NotePalette.DefaultName(i));
            var entry = data != null && i < data.Count ? data[i] : null;
            slot.IsEmpty = i > 0 && (entry is null || entry.Empty);
            if (!slot.IsEmpty && entry?.Colors is { Count: >= 6 } colors)
            {
                for (var channel = 0; channel < 6; channel++)
                    if (NotePalette.TryParseHex(colors[channel], out var color))
                        slot.Set((NoteColorChannel)channel, color);
            }
            if (!slot.IsEmpty)
                slot.Name = NotePalette.NormalizeName(entry?.Name, i);
            NotePalettes.Add(slot);
        }
        NormalizeNotePalettes();
    }

    private void NormalizeNotePalettes()
    {
        if (NotePalettes.Count != NotePalette.SlotCount)
        {
            NotePalettes.Clear();
            NotePalettes.AddRange(NotePalette.CreateLibrary());
        }
        NotePalettes[0].IsEmpty = false;
        for (var i = 0; i < NotePalettes.Count; i++)
            if (!NotePalettes[i].IsEmpty)
                NotePalettes[i].Name = NotePalette.NormalizeName(NotePalettes[i].Name, i);
        ActiveNotePaletteIndex = Math.Clamp(ActiveNotePaletteIndex, 0, NotePalettes.Count - 1);
        if (NotePalettes[ActiveNotePaletteIndex].IsEmpty)
            ActiveNotePaletteIndex = 0;
    }

    private static List<string> IndexColors(NotePalette slot)
    {
        var colors = new List<string>(6);
        foreach (var channel in Enum.GetValues<NoteColorChannel>())
            colors.Add(NotePalette.ToHex(slot.Get(channel)));
        return colors;
    }

    private static int ClampToStep(int value, int min, int max) =>
        Math.Clamp((int)Math.Round(value / 5.0) * 5, min, max);

    /// <summary>残影时长只有四档，读档时把历史/越界值归到最近一档（0 = 关闭）。</summary>
    private static int NormalizeResidualMs(int ms) => ms switch
    {
        <= 100 => 0,
        < 250 => 200,
        < 375 => 300,
        _ => 450,
    };

    private static HitEffectLevel ParseHitEffectLevel(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            return HitEffectLevel.Full;

        return value.GetString() switch
        {
            "reduced" => HitEffectLevel.Reduced,
            "off" => HitEffectLevel.Off,
            _ => HitEffectLevel.Full,
        };
    }

    private static string SerializeHitEffectLevel(HitEffectLevel level) => level switch
    {
        HitEffectLevel.Reduced => "reduced",
        HitEffectLevel.Off => "off",
        _ => "full",
    };

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
        /// <summary>旧字段，仅用于读旧存档；新存档仍写它以兼容旧客户端。</summary>
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
        [JsonPropertyName("calibrationTargetSamples")]
        public int CalibrationTargetSamples { get; set; } = Ui.CalibrationStats.DefaultTargetSamples;
        [JsonPropertyName("activeNotePalette")] public int ActiveNotePalette { get; set; }
        [JsonPropertyName("burstColorFollowsNote")] public bool BurstColorFollowsNote { get; set; }
        [JsonPropertyName("notePalettes")] public List<NotePaletteData>? NotePalettes { get; set; }
    }

    private sealed class NotePaletteData
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("empty")] public bool Empty { get; set; }
        /// <summary>六类颜色，顺序 = NoteColorChannel（TAP / EX / DRAG / HOLD / MIXER / MINE）。</summary>
        [JsonPropertyName("colors")] public List<string>? Colors { get; set; }
    }
}

/// <summary>
/// 打击特效档位。FULL 爆发 + 火花 + 余烬；REDUCED 爆发 + 半量火花（无余烬）；
/// OFF 只保留判定残影与判定浮字。残影本身由 HitResidualMs 单独控制。
/// </summary>
public enum HitEffectLevel
{
    Full,
    Reduced,
    Off,
}
