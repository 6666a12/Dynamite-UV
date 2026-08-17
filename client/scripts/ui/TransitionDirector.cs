using Godot;
using DuxCommunity.Game;

namespace DuxCommunity.Ui;

/// <summary>Opaque scene relay styles. Gameplay and Restart use the Track Handoff presentation.</summary>
public enum TransitionKind
{
    Standard,
    Back,
    Gameplay,
    Restart,
}

/// <summary>
/// Persistent, single-flight scene navigation. It covers the outgoing scene, presents at least one
/// fully opaque frame before switching, then waits for an explicit incoming ready signal.
/// </summary>
public partial class TransitionDirector : Node
{
    private const double SceneReadyTimeoutSeconds = 15.0;

    private enum TransitionPhase
    {
        Idle,
        Covering,
        OpaqueBarrier,
        AwaitingSceneReady,
        ReadyBarrier,
        Revealing,
    }

    public static TransitionDirector? Instance { get; private set; }
    public static bool IsBusy => Instance?._phase != TransitionPhase.Idle;
    public static bool IsAwaitingSceneReady =>
        Instance?._phase == TransitionPhase.AwaitingSceneReady;

    private CanvasLayer _layer = null!;
    private TransitionOverlay _overlay = null!;
    private TransitionPhase _phase;
    private UiMotionProfile _profile = UiMotionProfile.For(UiMotionMode.Full);
    private TransitionKind _kind;
    private string _route = string.Empty;
    private Action? _beforeSwitch;
    private Action<float>? _revealProgress;
    private Action? _revealCompleted;
    private double _phaseElapsed;
    private double _phaseDuration;
    private bool _treeWasPaused;
    private bool _sceneReady;
    private bool _readyTimeoutReported;
    private int _opaqueFrames;

    public override void _EnterTree()
    {
        if (Instance is not null && Instance != this)
        {
            GD.PushError("TransitionDirector: duplicate autoload instance; removing duplicate.");
            QueueFree();
            return;
        }

        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        GameSession.EnsureInit();

        _layer = new CanvasLayer { Layer = UiLayout.TransitionLayer };
        AddChild(_layer);
        _overlay = new TransitionOverlay
        {
            Size = UiLayout.DesignSize,
            Visible = false,
        };
        _layer.AddChild(_overlay);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Starts an opaque scene transition. beforeSwitch runs after the outgoing scene is covered.
    /// A relay payload is presentation-only and is released when the transition completes.
    /// </summary>
    public static bool Navigate(
        string route,
        TransitionKind kind = TransitionKind.Standard,
        string? label = null,
        string? detail = null,
        Action? beforeSwitch = null,
        TrackRelayPresentation? relay = null)
    {
        var instance = Instance;
        if (instance is null || !instance.IsNodeReady())
        {
            GD.PushError("TransitionDirector.Navigate called before the autoload was ready.");
            return false;
        }

        return instance.BeginNavigation(route, kind, label, detail, beforeSwitch, relay);
    }

    /// <summary>
    /// Incoming scenes call this after required content is ready. With no pending navigation, the
    /// callback runs immediately, which preserves direct scene launch and verification behavior.
    /// </summary>
    public static void ReportSceneReady(Action? revealCompleted = null) =>
        ReportSceneReady(null, revealCompleted);

    /// <summary>
    /// Publishes eased reveal progress while the persistent director continues processing through
    /// a paused SceneTree. The completion callback runs only after overlay removal and input unlock.
    /// </summary>
    public static void ReportSceneReady(
        Action<float>? revealProgress,
        Action? revealCompleted)
    {
        var instance = Instance;
        if (instance is null ||
            instance._phase != TransitionPhase.AwaitingSceneReady)
        {
            InvokeSafely(revealProgress, 1f, "scene-ready progress callback");
            InvokeSafely(revealCompleted, "scene-ready callback");
            return;
        }

        instance.AcceptSceneReady(revealProgress, revealCompleted);
    }

    public override void _Process(double delta)
    {
        switch (_phase)
        {
            case TransitionPhase.Covering:
                AdvanceCover(delta);
                break;
            case TransitionPhase.OpaqueBarrier:
                AdvanceOpaqueBarrier();
                break;
            case TransitionPhase.AwaitingSceneReady:
                AdvanceWait(delta);
                break;
            case TransitionPhase.ReadyBarrier:
                AdvanceReadyBarrier();
                break;
            case TransitionPhase.Revealing:
                AdvanceReveal(delta);
                break;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_phase != TransitionPhase.Idle)
            GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_phase != TransitionPhase.Idle)
            GetViewport().SetInputAsHandled();
    }

    private bool BeginNavigation(
        string route,
        TransitionKind kind,
        string? label,
        string? detail,
        Action? beforeSwitch,
        TrackRelayPresentation? relay)
    {
        if (_phase != TransitionPhase.Idle)
            return false;
        if (string.IsNullOrWhiteSpace(route))
        {
            GD.PushError("TransitionDirector: scene route must not be empty.");
            return false;
        }
        if (!ResourceLoader.Exists(route))
        {
            GD.PushError($"TransitionDirector: scene does not exist: {route}");
            return false;
        }

        _profile = UiMotionProfile.For(GameSession.Settings.MotionMode);
        _kind = kind;
        _route = route;
        _beforeSwitch = beforeSwitch;
        _revealProgress = null;
        _revealCompleted = null;
        _sceneReady = false;
        _readyTimeoutReported = false;
        _opaqueFrames = 0;
        _phaseElapsed = 0.0;
        _phaseDuration = CoverDuration(_profile);
        _treeWasPaused = GetTree().Paused;
        GetTree().Paused = true;

        _overlay.Configure(kind, label, detail, _profile, relay);
        _overlay.Coverage = 0f;
        _overlay.Visible = true;

        if (!_profile.IsAnimated)
        {
            _overlay.Coverage = 1f;
            _phase = TransitionPhase.OpaqueBarrier;
        }
        else
        {
            _phase = TransitionPhase.Covering;
        }

        return true;
    }

    private void AdvanceCover(double delta)
    {
        _phaseElapsed += delta;
        var progress = NormalizedPhaseProgress();
        _overlay.Coverage = UiEase.Signal(progress);
        if (_phaseElapsed < _phaseDuration)
            return;

        _overlay.Coverage = 1f;
        _phase = TransitionPhase.OpaqueBarrier;
        _opaqueFrames = 0;
    }

    private void AdvanceOpaqueBarrier()
    {
        // ChangeSceneToFile can synchronously stall while constructing a large scene. Waiting one
        // process frame guarantees the complete handoff has reached the renderer first.
        if (_opaqueFrames++ == 0)
            return;
        SwitchScene();
    }

    private void SwitchScene()
    {
        try
        {
            _beforeSwitch?.Invoke();
        }
        catch (Exception exception)
        {
            GD.PushError($"TransitionDirector: beforeSwitch failed: {exception}");
            RecoverToCurrentScene();
            return;
        }
        finally
        {
            _beforeSwitch = null;
        }

        _phase = TransitionPhase.AwaitingSceneReady;
        _phaseElapsed = 0.0;
        var error = GetTree().ChangeSceneToFile(_route);
        if (error == Error.Ok)
            return;

        GD.PushError($"TransitionDirector: ChangeSceneToFile failed for '{_route}' ({error}).");
        RecoverToCurrentScene();
    }

    private void AcceptSceneReady(Action<float>? revealProgress, Action? revealCompleted)
    {
        if (_sceneReady)
            return;

        _sceneReady = true;
        _revealProgress = revealProgress;
        _revealCompleted = revealCompleted;
        _overlay.MarkReady();
        InvokeSafely(_revealProgress, 0f, "reveal-progress callback");
        // Let all remaining ready notifications finish before the wait/hold gate may reveal.
        CallDeferred(MethodName.TryBeginRevealDeferred);
    }

    private void TryBeginRevealDeferred()
    {
        if (_phase != TransitionPhase.AwaitingSceneReady || !_sceneReady)
            return;
        if (_phaseElapsed < ContextHoldDuration())
            return;
        BeginReveal();
    }

    private void AdvanceWait(double delta)
    {
        _phaseElapsed += delta;
        _overlay.AdvanceWaitingScan(delta);

        if (_sceneReady)
        {
            if (_phaseElapsed >= ContextHoldDuration())
                BeginReveal();
            return;
        }

        if (_readyTimeoutReported || _phaseElapsed < SceneReadyTimeoutSeconds)
            return;

        _readyTimeoutReported = true;
        GD.PushError($"TransitionDirector: scene '{_route}' did not report ready within " +
                     $"{SceneReadyTimeoutSeconds:F0} seconds; returning to a safe route.");
        _overlay.MarkFailed();
        _route = UiRoutes.SongSelect;
        _phaseElapsed = 0.0;
        var error = GetTree().ChangeSceneToFile(_route);
        if (error == Error.Ok)
            return;

        GD.PushError($"TransitionDirector: timeout recovery route failed ({error}); releasing lock.");
        _sceneReady = true;
        _revealProgress = null;
        _revealCompleted = null;
        BeginReveal();
    }

    private void BeginReveal()
    {
        if (_phase != TransitionPhase.AwaitingSceneReady)
            return;

        if (!_profile.IsAnimated)
        {
            // READY must be visible for one opaque frame even when every interpolation is off.
            _phase = TransitionPhase.ReadyBarrier;
            _opaqueFrames = 0;
            return;
        }

        StartAnimatedReveal();
    }

    private void AdvanceReadyBarrier()
    {
        if (_opaqueFrames++ == 0)
            return;
        CompleteTransition();
    }

    private void StartAnimatedReveal()
    {
        _phaseDuration = RevealDuration(_profile, _kind);
        if (_phaseDuration <= 0.0)
        {
            CompleteTransition();
            return;
        }

        _phase = TransitionPhase.Revealing;
        _phaseElapsed = 0.0;
        _overlay.Visible = true;
        _overlay.Coverage = 1f;
    }

    private void AdvanceReveal(double delta)
    {
        _phaseElapsed += delta;
        var raw = NormalizedPhaseProgress();
        _overlay.Coverage = 1f - UiEase.Signal(raw);
        InvokeSafely(_revealProgress, raw, "reveal-progress callback");
        if (_phaseElapsed < _phaseDuration)
            return;

        CompleteTransition();
    }

    private void RecoverToCurrentScene()
    {
        _sceneReady = true;
        _revealProgress = null;
        _revealCompleted = null;
        _phaseDuration = CoverDuration(_profile);
        if (!_profile.IsAnimated || _phaseDuration <= 0.0)
        {
            CompleteTransition();
            return;
        }

        _phase = TransitionPhase.Revealing;
        _phaseElapsed = 0.0;
        _overlay.Visible = true;
        _overlay.Coverage = 1f;
    }

    private void CompleteTransition()
    {
        var progress = _revealProgress;
        var callback = _revealCompleted;
        InvokeSafely(progress, 1f, "reveal-progress callback");

        _revealProgress = null;
        _revealCompleted = null;
        _beforeSwitch = null;
        _route = string.Empty;
        _sceneReady = false;
        _readyTimeoutReported = false;
        _phaseElapsed = 0.0;
        _overlay.Coverage = 0f;
        _overlay.Visible = false;
        _overlay.ClearPresentation();
        GetTree().Paused = _treeWasPaused;
        _phase = TransitionPhase.Idle;
        InvokeSafely(callback, "reveal-completed callback");
    }

    private float NormalizedPhaseProgress() => _phaseDuration <= 0.0
        ? 1f
        : Mathf.Clamp((float)(_phaseElapsed / _phaseDuration), 0f, 1f);

    private double ContextHoldDuration() => _kind is TransitionKind.Gameplay or TransitionKind.Restart
        ? _profile.TrackContextHoldDuration
        : 0.0;

    private static double CoverDuration(UiMotionProfile profile) =>
        Math.Max(0.0, profile.SceneDuration * 0.5);

    private static double RevealDuration(UiMotionProfile profile, TransitionKind kind) =>
        kind is TransitionKind.Gameplay or TransitionKind.Restart
            ? profile.GameplayRevealDuration
            : Math.Max(0.0, profile.SceneDuration * 0.5);

    private static void InvokeSafely(Action? callback, string context)
    {
        if (callback is null)
            return;
        try
        {
            callback();
        }
        catch (Exception exception)
        {
            GD.PushError($"TransitionDirector: {context} failed: {exception}");
        }
    }

    private static void InvokeSafely(Action<float>? callback, float value, string context)
    {
        if (callback is null)
            return;
        try
        {
            callback(value);
        }
        catch (Exception exception)
        {
            GD.PushError($"TransitionDirector: {context} failed: {exception}");
        }
    }
}
