namespace DynamiteUniverse.Ui;

/// <summary>UI 动效的统一时长、位移和效果策略。</summary>
public sealed class UiMotionProfile
{
    private static readonly UiMotionProfile Full = new(
        UiMotionMode.Full,
        pressDuration: 0.09,
        hoverDuration: 0.16,
        valueDuration: 0.26,
        panelDuration: 0.42,
        sceneDuration: 0.62,
        majorSceneDuration: 0.76,
        resultDuration: 0.96,
        loopDuration: 1.20,
        trackContextHoldDuration: 0.65,
        gameplayRevealDuration: 0.42,
        valueShift: 8f,
        listShift: 12f,
        contentShift: 20f,
        relayShift: 28f,
        stagger: 0.06,
        allowDirectionalMotion: true,
        allowStagger: true,
        allowLoop: true,
        allowEcho: true);

    private static readonly UiMotionProfile Reduced = new(
        UiMotionMode.Reduced,
        pressDuration: 0.07,
        hoverDuration: 0.10,
        valueDuration: 0.14,
        panelDuration: 0.18,
        sceneDuration: 0.22,
        majorSceneDuration: 0.22,
        resultDuration: 0.26,
        loopDuration: 0.0,
        trackContextHoldDuration: 0.18,
        gameplayRevealDuration: 0.18,
        valueShift: 0f,
        listShift: 0f,
        contentShift: 0f,
        relayShift: 0f,
        stagger: 0.0,
        allowDirectionalMotion: false,
        allowStagger: false,
        allowLoop: false,
        allowEcho: false);

    private static readonly UiMotionProfile Off = new(
        UiMotionMode.Off,
        pressDuration: 0.0,
        hoverDuration: 0.0,
        valueDuration: 0.0,
        panelDuration: 0.0,
        sceneDuration: 0.0,
        majorSceneDuration: 0.0,
        resultDuration: 0.0,
        loopDuration: 0.0,
        trackContextHoldDuration: 0.0,
        gameplayRevealDuration: 0.0,
        valueShift: 0f,
        listShift: 0f,
        contentShift: 0f,
        relayShift: 0f,
        stagger: 0.0,
        allowDirectionalMotion: false,
        allowStagger: false,
        allowLoop: false,
        allowEcho: false);

    private UiMotionProfile(
        UiMotionMode mode,
        double pressDuration,
        double hoverDuration,
        double valueDuration,
        double panelDuration,
        double sceneDuration,
        double majorSceneDuration,
        double resultDuration,
        double loopDuration,
        double trackContextHoldDuration,
        double gameplayRevealDuration,
        float valueShift,
        float listShift,
        float contentShift,
        float relayShift,
        double stagger,
        bool allowDirectionalMotion,
        bool allowStagger,
        bool allowLoop,
        bool allowEcho)
    {
        Mode = mode;
        PressDuration = pressDuration;
        HoverDuration = hoverDuration;
        ValueDuration = valueDuration;
        PanelDuration = panelDuration;
        SceneDuration = sceneDuration;
        MajorSceneDuration = majorSceneDuration;
        ResultDuration = resultDuration;
        LoopDuration = loopDuration;
        TrackContextHoldDuration = trackContextHoldDuration;
        GameplayRevealDuration = gameplayRevealDuration;
        ValueShift = valueShift;
        ListShift = listShift;
        ContentShift = contentShift;
        RelayShift = relayShift;
        Stagger = stagger;
        AllowDirectionalMotion = allowDirectionalMotion;
        AllowStagger = allowStagger;
        AllowLoop = allowLoop;
        AllowEcho = allowEcho;
    }

    public UiMotionMode Mode { get; }
    public double PressDuration { get; }
    public double HoverDuration { get; }
    public double ValueDuration { get; }
    public double FocusDuration => ValueDuration;
    public double PanelDuration { get; }
    public double SceneDuration { get; }
    public double MajorSceneDuration { get; }
    public double ResultDuration { get; }
    public double LoopDuration { get; }
    public double TrackContextHoldDuration { get; }
    public double GameplayRevealDuration { get; }
    public float ValueShift { get; }
    public float ListShift { get; }
    public float ContentShift { get; }
    public float RelayShift { get; }
    public double Stagger { get; }
    public bool AllowDirectionalMotion { get; }
    public bool AllowStagger { get; }
    public bool AllowLoop { get; }
    public bool AllowEcho { get; }
    public bool IsAnimated => Mode != UiMotionMode.Off;

    public static UiMotionProfile For(UiMotionMode mode) => mode switch
    {
        UiMotionMode.Reduced => Reduced,
        UiMotionMode.Off => Off,
        _ => Full,
    };
}
