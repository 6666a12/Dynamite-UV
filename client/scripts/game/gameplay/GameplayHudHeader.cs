using Godot;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Game;

/// <summary>Top HUD composition; does not own statistics, health rules or pause state.</summary>
internal sealed class GameplayHudHeader
{
    private readonly GameplayHudPlate[] _plates = new GameplayHudPlate[6];
    private readonly GameplayHudPlate _pause;
    private readonly GameplayBleedBar _bleed;
    private readonly ColorRect[] _lines;
    private static readonly Color[] ColorsByKind =
        [new("35b9db"), new("d9a438"), new("49b97a"), new("959aa5"), new("9abb45"), new("aab7d5")];
    private readonly int[] _counts = [-1, -1, -1, -1, -1];
    private double _rawAcc = double.NaN;
    private bool? _hardcore;

    internal GameplayHudHeader(Node2D root, Action pause, Func<bool> canPause,
        Func<UiMotionMode> motion, Func<bool> frozen)
    {
        _bleed = new GameplayBleedBar { Motion = motion, Frozen = frozen };
        root.AddChild(_bleed);
        _lines = [Line(root, 53), Line(root, 1093)];
        for (var i = 0; i < _plates.Length; i++)
        {
            _plates[i] = new GameplayHudPlate
            {
                Position = new Vector2(461 + i * 168, 12), Size = new Vector2(158, 40),
                Accent = ColorsByKind[i], CornerSide = i < 3 ? -1 : 1, Motion = motion, ZIndex = 2,
            };
            root.AddChild(_plates[i]);
        }
        _pause = new GameplayHudPlate
        {
            Position = new Vector2(900, 60), Size = new Vector2(120, 64),
            Accent = new Color("7463c7"), CornerSide = 0, FontSize = 30,
            Activated = pause, CanActivate = canPause, Motion = motion, ZIndex = 3,
        };
        root.AddChild(_pause);
        _pause.SetText("II");
    }
    internal void PulsePause() => _pause.Pulse();
    internal void Update(GameplayHudSnapshot s)
    {
        UpdateCount(0, s.CountPerfect, "PERFECT", 0);
        UpdateCount(1, s.CountGreat, "GREAT", 1);
        UpdateCount(2, s.CountGood, "GOOD", 2);
        UpdateCount(3, s.CountMiss, "MISS", 3);
        UpdateCount(4, s.MaxCombo, "M.COMBO", 5);
        if (_rawAcc != s.LiveAcc)
        {
            _rawAcc = s.LiveAcc;
            _plates[4].SetText($"ACC {s.LiveAcc:F2}%"); // plate suppresses pulse when rounded text is unchanged
        }
        if (_hardcore != s.Hardcore)
        {
            _hardcore = s.Hardcore;
            var color = s.Hardcore ? GameplayBleedBar.HardcoreColor : GameplayBleedBar.StandardColor;
            foreach (var line in _lines) line.Color = new Color(color, 0.62f);
        }
        _bleed.Observe(s.BleedEnabled, s.Hardcore, s.BleedHealth);
    }
    private void UpdateCount(int cache, int value, string prefix, int plate)
    {
        if (_counts[cache] == value) return;
        _counts[cache] = value;
        _plates[plate].SetText($"{prefix} {value}");
    }
    private static ColorRect Line(Node root, float x)
    {
        var line = new ColorRect
        {
            Position = new Vector2(x, 64), Size = new Vector2(773, 2),
            Color = new Color(GameplayBleedBar.StandardColor, 0.62f),
            MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = -1,
        };
        root.AddChild(line);
        return line;
    }
}
