using Godot;
using DynamiteUniverse.Ui;
using static DynamiteUniverse.Editor.EditorPageUi;

namespace DynamiteUniverse.Editor;

internal sealed class EditorSettingsPage
{
    private readonly AuthoringCanvas _canvas;
    private readonly EditorSettings _settings;
    private readonly Action _backRequested;
    private readonly Action _applyUiScale;
    internal Control Root { get; }
    internal void Redraw() => _settingsOverlay.QueueRedraw();
    internal EditorSettingsPage(AuthoringCanvas canvas, EditorSettings settings, Action backRequested, Action applyUiScale)
    {
        _canvas = canvas; _settings = settings;
        _backRequested = backRequested; _applyUiScale = applyUiScale;
        Root = Build();
    }
    private SettingsPageOverlay _settingsOverlay = null!;

    /// <summary>Editor — Settings 板：整页自绘（见 <see cref="SettingsPageOverlay"/>），
    /// 本组件把注入的设置以快照传入、把页面事件落进 <see cref="EditorSettings"/> 并落盘。
    /// 两个入口（Start 页的 SETTINGS 文字键、编辑态右键菜单的 SETTINGS 行）进同一页，
    /// BACK 通过请求回调回到 shell 记录的来源页。</summary>
    private Control Build()
    {
        var page = NewPage(rule: false);
        _settingsOverlay = new SettingsPageOverlay();
        _settingsOverlay.BackRequested += () => _backRequested();
        _settingsOverlay.ResetRequested += ResetSettings;
        _settingsOverlay.MotionSelected += index =>
        {
            _settings.MotionMode = (UiMotionMode)index;
            CommitSettings();
        };
        _settingsOverlay.UiScaleSelected += index =>
        {
            _settings.UiScalePercent = EditorSettings.UiScaleChoices[index];
            _applyUiScale();
            CommitSettings();
        };
        _settingsOverlay.GridSelected += index =>
        {
            _settings.DefaultGridDivisor = EditorSettings.GridDivisorChoices[index];
            _canvas.ApplyDefaultGridDivisor();
            CommitSettings();
        };
        _settingsOverlay.SnapStepped += StepSnapStep;
        _settingsOverlay.SliderChanged += ApplySettingsSlider;
        _settingsOverlay.EditCommitted += () => _settings.Save();
        page.AddChild(_settingsOverlay);
        return page;
    }

    internal void RefreshSettingsPage()
    {
        if (_settingsOverlay is null) return;
        var settings = _settings;
        _settingsOverlay.Apply(new SettingsPageOverlay.Snapshot
        {
            MotionIndex = (int)settings.MotionMode,
            UiScaleIndex = settings.UiScaleChoiceIndex,
            GridIndex = settings.GridChoiceIndex,
            SnapText = SettingsPageOverlay.SnapText(settings.SnapStep),
            GridOpacityPercent = settings.GridOpacityPercent,
            BackgroundDimPercent = settings.BackgroundDimPercent,
            ScrollInertiaPercent = settings.ScrollInertiaPercent,
        });
    }

    /// <summary>离散改动（档位/复位）：落盘后重推快照，页面读数与设置真源保持同一份。</summary>
    private void CommitSettings()
    {
        _settings.Save();
        RefreshSettingsPage();
    }

    private void StepSnapStep(int direction)
    {
        var choices = EditorSettings.SnapStepChoices;
        var index = _settings.SnapChoiceIndex;
        _settings.SnapStep = choices[Math.Clamp(index + direction, 0, choices.Length - 1)];
        CommitSettings();
    }

    /// <summary>滑条只改内存与画面，松手（<c>EditCommitted</c>）才写盘，避免拖动时反复写文件。
    /// 拖动中把新值推回页面快照，滑条与百分比读数才会跟着走。
    /// SCROLL INERTIA 没有额外接点：画布滚轮每帧读设置真源（`ScrollInertiaTauSeconds`），
    /// 所以拖动中正在进行的滑动立刻换手感，不需要重推。</summary>
    private void ApplySettingsSlider(int slider, int percent)
    {
        switch (slider)
        {
            case SettingsPageOverlay.GridOpacitySlider:
                _settings.GridOpacityPercent = percent;
                _canvas.QueueRedraw();
                break;
            case SettingsPageOverlay.BackgroundDimSlider:
                _settings.BackgroundDimPercent = percent;
                _canvas.ApplyBackgroundDim();
                break;
            case SettingsPageOverlay.ScrollInertiaSlider:
                _settings.ScrollInertiaPercent = percent;
                break;
            default:
                return;
        }
        RefreshSettingsPage();
    }

    /// <summary>RESET DEFAULTS 是真实动作：回默认值、落盘，并把当前文档的网格与画面接点一起拉回默认。
    /// UI SCALE 也一并回到 100%，所以这里同样要重放字号。</summary>
    private void ResetSettings()
    {
        _settings.ResetToDefaults();
        _settings.Save();
        _canvas.ApplyDefaultGridDivisor();
        _canvas.ApplyBackgroundDim();
        _applyUiScale();
        RefreshSettingsPage();
    }
}
