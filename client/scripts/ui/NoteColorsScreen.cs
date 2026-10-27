using Godot;
using DynamiteUniverse.Game;
using DynamiteUniverse.Shared.Chart;

namespace DynamiteUniverse.Ui;

/// <summary>
/// Note Colors v2: a persistent color desk and staggered collection, plus a full-screen
/// readability view. Both use one isolated draft; only Apply writes GameSettings.
/// </summary>
public partial class NoteColorsScreen : Node2D
{
    private static readonly Color Background = new("090C12");
    private static readonly Color PanelColor = new("111720");
    private static readonly Color BorderColor = new("29313E");
    private static readonly Color TextColor = new("EDF2FA");
    private static readonly Color MutedColor = new("8995A8");
    private static readonly Color WarningColor = new("E8BB79");
    private static readonly string[] Names = ["TAP", "EX TAP", "DRAG", "HOLD", "MIXER", "MINE"];
    private static readonly string[] Descriptions =
        ["点击音符", "扩展点击音符", "滑动接触音符", "长按音符 · 头部 / 连线 / 尾部", "接收音符 · 本体 / 徽章", "危险音符"];
    private static readonly NoteType[] Types =
        [NoteType.Tap, NoteType.ExTap, NoteType.Drag, NoteType.HoldHead, NoteType.MixerHead, NoteType.Mine];
    private static readonly Color[] TestBackgrounds = [new("05070F"), new("18212D"), new("35424F")];
    private static GameSettings Settings => GameSession.Settings;

    private NotePaletteDraft _draft = null!;
    private Node2D _content = null!;
    private Node2D _main = null!;
    private Node2D _observation = null!;
    private Control _inputShield = null!;
    private CanvasLayer _modalLayer = null!;
    private Control? _modal;
    private Action? _modalBack;
    private Tween? _entrance;
    private bool _built;
    private bool _observing;
    private bool _comparing;
    private bool _refreshing;
    private bool _pickerDragging;
    private bool _hexGesture;
    private int _channel = (int)NoteColorChannel.Hold;
    private int? _pairOverride;
    private int[] _pairOrder = [];
    private string _feedback = "";
    private double _feedbackTtl;
    private readonly List<string> _recent = [];
    private readonly List<TechButton> _normalButtons = [];
    private readonly List<NotePaletteSpecimen> _allSpecimens = [];
    private readonly List<TechButton> _compareButtons = [];
    private readonly List<TechButton> _recentButtons = [];
    private readonly List<CutPanel> _recentSwatches = [];
    private readonly TechButton[] _typeButtons = new TechButton[6];
    private readonly TechButton[] _observeTypes = new TechButton[6];
    private readonly NotePaletteSpecimen[] _typeIcons = new NotePaletteSpecimen[6];
    private readonly CutPanel[] _observeSwatches = new CutPanel[6];
    private readonly CutPanel[] _headerSwatches = new CutPanel[6];
    private readonly NotePaletteSpecimen[] _collection = new NotePaletteSpecimen[6];
    private readonly Label[] _collectionLabels = new Label[6];
    private readonly NotePaletteSpecimen[] _backgroundSpecimens = new NotePaletteSpecimen[3];
    private readonly Label[] _contrasts = new Label[3];
    private readonly TechButton[] _pairButtons = new TechButton[2];
    private NotePaletteSpecimen _pairLeft = null!, _pairRight = null!;
    private Label _title = null!, _subtitle = null!, _status = null!, _presetName = null!;
    private Label _selectedName = null!, _selectedDescription = null!, _selectionHint = null!;
    private Label _sessionHint = null!, _observeStatus = null!, _observedColor = null!;
    private Label _pairName = null!, _pairHex = null!, _pairLeftName = null!, _pairLeftHex = null!, _delta = null!;
    private Label _readabilityHint = null!;
    private TechButton _presetButton = null!, _undoButton = null!, _redoButton = null!, _applyButton = null!;
    private TechButton _saveAsButton = null!, _resetColor = null!, _judgeBurst = null!, _noteBurst = null!;
    private SvField _sv = null!;
    private HueBar _hue = null!;
    private LineEdit _hex = null!;
    private CutPanel _hexFrame = null!, _oldSwatch = null!, _newSwatch = null!;

    public override void _Ready()
    {
        GameSession.EnsureInit();
        _draft = new NotePaletteDraft(CaptureSettings(), NotePalette.FactoryColors.Select(NotePalette.ToHex).ToArray());
        AddChild(new ColorRect { Size = UiLayout.DesignSize, Color = Background, MouseFilter = Control.MouseFilterEnum.Ignore });
        _content = new Node2D { Name = "PaletteContent" };
        AddChild(_content);
        _main = new Node2D { Name = "ColorDesk" };
        _observation = new Node2D { Name = "Readability", Visible = false };
        _content.AddChild(_main);
        _content.AddChild(_observation);
        BuildHeader();
        BuildMain();
        BuildObservation();
        _modalLayer = new CanvasLayer { Layer = 20, Name = "PaletteDialogs" };
        AddChild(_modalLayer);
        _inputShield = new Control { Size = UiLayout.DesignSize, MouseFilter = Control.MouseFilterEnum.Stop, ZIndex = 100 };
        _content.AddChild(_inputShield);
        _built = true;
        Refresh();
        var profile = UiMotionProfile.For(Settings.MotionMode);
        _content.Modulate = profile.IsAnimated ? new Color(1, 1, 1, 0) : Colors.White;
        TransitionDirector.ReportSceneReady(() =>
        {
            if (!IsInsideTree()) return;
            if (profile.IsAnimated)
            {
                _entrance = CreateTween();
                _entrance.TweenProperty(_content, "modulate:a", 1f, profile.SceneDuration);
                _entrance.Finished += () => { if (IsInsideTree()) _inputShield.Visible = false; };
            }
            else _inputShield.Visible = false;
        });
        SetProcess(false);
    }

    public override void _Notification(int what)
    {
        if (!_built) return;
        if (what == NotificationWMWindowFocusOut) CancelInteractions();
        if (what == NotificationWMGoBackRequest) RequestBack();
    }

    public override void _ExitTree()
    {
        _entrance?.Kill();
        _built = false;
        foreach (var specimen in _allSpecimens) specimen.StopHit();
        // Deliberately no save: an abandoned or externally closed page must not leak its draft.
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_built || TransitionDirector.IsBusy || _inputShield.Visible || e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.Keycode == Key.Escape)
        {
            RequestBack();
            GetViewport().SetInputAsHandled();
        }
        else if (_modal is null && !_comparing && (key.CtrlPressed || key.MetaPressed))
        {
            if (key.Keycode == Key.Z) { History(key.ShiftPressed); GetViewport().SetInputAsHandled(); }
            else if (key.Keycode == Key.Y) { History(true); GetViewport().SetInputAsHandled(); }
            else if (key.Keycode == Key.S) { ApplyDraft(); GetViewport().SetInputAsHandled(); }
        }
    }

    private static PaletteLibrarySnapshot CaptureSettings() => new(
        Settings.NotePalettes.Select(p => new PaletteSlotSnapshot(p.Name, p.IsEmpty,
            Enumerable.Range(0, 6).Select(i => NotePalette.ToHex(p.Get((NoteColorChannel)i))).ToArray())).ToArray(),
        Settings.ActiveNotePaletteIndex, Settings.BurstColorFollowsNote);

    private static NotePalette Palette(PaletteLibrarySnapshot snapshot)
    {
        var source = snapshot.Slots[snapshot.ActiveIndex];
        var result = NotePalette.CreateFactory(source.Name);
        for (var i = 0; i < 6; i++) result.Set((NoteColorChannel)i, Color.FromHtml("#" + source.Colors[i]));
        return result;
    }

    private static void Install(PaletteLibrarySnapshot snapshot)
    {
        var slots = snapshot.Slots.Select(s =>
        {
            var p = NotePalette.CreateFactory(s.Name);
            p.IsEmpty = s.IsEmpty;
            for (var i = 0; i < 6; i++) p.Set((NoteColorChannel)i, Color.FromHtml("#" + s.Colors[i]));
            return p;
        }).ToList();
        Settings.RestoreNotePaletteState(slots, snapshot.ActiveIndex);
        Settings.BurstColorFollowsNote = snapshot.BurstFollowsNote;
    }

    private void Refresh()
    {
        if (!_built || _refreshing) return;
        _refreshing = true;
        var current = _draft.Current;
        var shown = _comparing ? _draft.Original : current;
        var palette = Palette(shown);
        var color = palette.Get((NoteColorChannel)_channel);
        _title.Text = _observing ? "READABILITY" : "NOTE COLORS";
        _subtitle.Text = _observing ? "可读性观察  /  NOTE COLORS" : "音符配色  /  EFFECTS";
        _status.Text = _comparing ? "正在对比进入页时的方案" : _draft.IsDirty ? "● 未应用修改" : "当前方案已应用";
        _status.AddThemeColorOverride("font_color", _comparing ? UiFonts.Cyan : _draft.IsDirty ? WarningColor : MutedColor);
        _presetName.Text = shown.Slots[shown.ActiveIndex].Name;
        for (var i = 0; i < 6; i++)
        {
            var c = palette.Get((NoteColorChannel)i);
            _headerSwatches[i].Fill = c;
            _typeButtons[i].Selected = _observeTypes[i].Selected = i == _channel;
            _observeSwatches[i].Fill = c;
            _typeIcons[i].Configure(Types[i], c, 68, Settings.NoteBrightnessScale);
            _collection[i].Configure(Types[i], c, CollectionWidths[i], Settings.NoteBrightnessScale,
                showPath: i == 3, tailWidth: 260, gap: 190, selected: i == _channel);
            _collectionLabels[i].AddThemeColorOverride("font_color", i == _channel ? TextColor : MutedColor);
        }
        _selectedName.Text = Names[_channel];
        _selectedDescription.Text = Descriptions[_channel];
        _selectionHint.Text = $"当前编辑 · {Names[_channel]}";
        if (!_pickerDragging)
        {
            _sv.SetColor(color);
            _hue.Hue = color.H;
        }
        if (!_hex.HasFocus()) _hex.Text = "#" + NotePalette.ToHex(color);
        _newSwatch.Fill = color;
        var original = _draft.Original;
        var oldSlot = original.Slots[current.ActiveIndex].IsEmpty ? original.Slots[original.ActiveIndex] : original.Slots[current.ActiveIndex];
        _oldSwatch.Fill = Color.FromHtml("#" + oldSlot.Colors[_channel]);
        _judgeBurst.Selected = !shown.BurstFollowsNote;
        _noteBurst.Selected = shown.BurstFollowsNote;
        _sessionHint.Text = _feedback.Length > 0 ? _feedback : _comparing ? "松开恢复当前草稿" : _draft.IsDirty ? "仅预览，应用后生效" : "调整颜色后，点击应用保存";
        _sessionHint.AddThemeColorOverride("font_color", _feedback.Length > 0 ? WarningColor : MutedColor);
        _readabilityHint.Text = NotePaletteMetrics.Warnings(palette).Count is var count && count > 0
            ? $"{count} 处配色提醒 · 仅供参考" : "对比背景与相近音符";
        RefreshObservation(palette, color);
        foreach (var button in _normalButtons) button.Enabled = !_comparing;
        _presetButton.Enabled = !_comparing && !_observing;
        _undoButton.Enabled = !_comparing && _draft.CanUndo;
        _redoButton.Enabled = !_comparing && _draft.CanRedo;
        _applyButton.Enabled = !_comparing && _draft.IsDirty;
        _saveAsButton.Enabled = !_comparing && current.Slots.Any(p => p.IsEmpty);
        _resetColor.Enabled = !_comparing && current.Slots[current.ActiveIndex].Colors[_channel] != NotePalette.ToHex(NotePalette.FactoryColors[_channel]);
        _sv.MouseFilter = _hue.MouseFilter = _comparing ? Control.MouseFilterEnum.Ignore : Control.MouseFilterEnum.Stop;
        _hex.Editable = !_comparing;
        for (var i = 0; i < _recentButtons.Count; i++)
        {
            _recentButtons[i].Visible = _recentSwatches[i].Visible = i < _recent.Count;
            if (i < _recent.Count) _recentSwatches[i].Fill = Color.FromHtml("#" + _recent[i]);
        }
        _refreshing = false;
    }

    private void Mutate(Func<bool> action)
    {
        if (_comparing || _modal is not null || TransitionDirector.IsBusy) return;
        CommitHex();
        if (action()) { _feedback = ""; Refresh(); }
    }

    private void SelectChannel(int channel)
    {
        if (_comparing) return;
        CancelInteractions();
        _channel = channel;
        _pairOverride = null;
        Refresh();
    }

    private void BeginPicker(bool hue)
    {
        if (_comparing) return;
        // Finish the previous owner's transaction BEFORE starting the new one. Otherwise a
        // second finger's GrabFocus would end this new gesture via the old control's FocusExit.
        if (hue) _sv.CancelInteraction(); else _hue.CancelInteraction();
        CommitHex();
        _pickerDragging = true;
        _draft.BeginGesture();
    }

    private void EndPicker()
    {
        if (!_pickerDragging) return;
        _pickerDragging = false;
        _draft.EndGesture();
        RememberCurrentColor();
        Refresh();
    }

    private void SetColor(Color color)
    {
        if (_refreshing || _comparing) return;
        if (_draft.SetColor(_channel, NotePalette.ToHex(color))) { _feedback = ""; Refresh(); }
    }

    private void HexChanged(string text)
    {
        if (_refreshing || _comparing) return;
        if (!_hexGesture) { _draft.BeginGesture(); _hexGesture = true; }
        var valid = NotePalette.TryParseHex(text, out var color);
        _hexFrame.Border = valid ? UiFonts.Cyan : WarningColor;
        if (valid) SetColor(color);
    }

    private void CommitHex()
    {
        if (!_built || _refreshing) return;
        if (_hexGesture)
        {
            _draft.EndGesture();
            _hexGesture = false;
            RememberCurrentColor();
        }
        // Programmatic normalization must not open a new HEX undo transaction.
        _refreshing = true;
        _hex.Text = "#" + _draft.Current.Slots[_draft.Current.ActiveIndex].Colors[_channel];
        _refreshing = false;
        _hexFrame.Border = BorderColor;
    }

    private void RememberCurrentColor()
    {
        var color = _draft.Current.Slots[_draft.Current.ActiveIndex].Colors[_channel];
        _recent.Remove(color);
        _recent.Insert(0, color);
        if (_recent.Count > 5) _recent.RemoveAt(5);
    }

    private void History(bool redo)
    {
        if (_comparing || _modal is not null) return;
        CancelInteractions();
        if (redo ? _draft.Redo() : _draft.Undo()) { _feedback = ""; Refresh(); }
    }

    private bool ApplyDraft()
    {
        if (_comparing || TransitionDirector.IsBusy) return false;
        CancelInteractions();
        var before = CaptureSettings();
        Install(_draft.Current);
        if (!Settings.TrySave())
        {
            Install(before);
            Feedback("保存失败，草稿仍保留，请稍后重试");
            return false;
        }
        _draft.AcceptApplied();
        Feedback("配色已应用");
        return true;
    }

    private void Compare(bool held)
    {
        if (!_built || (held && (_modal is not null || TransitionDirector.IsBusy || _inputShield.Visible))) return;
        if (held) { _sv.CancelInteraction(); _hue.CancelInteraction(); CommitHex(); }
        if (_comparing == held) return;
        _comparing = held;
        Refresh();
    }

    private void CancelInteractions()
    {
        if (!_built) return;
        _sv.CancelInteraction();
        _hue.CancelInteraction();
        foreach (var button in _compareButtons) button.CancelInteraction();
        foreach (var button in _normalButtons) button.CancelInteraction();
        _comparing = false;
        CommitHex();
        if (_hex.HasFocus()) _hex.ReleaseFocus();
        foreach (var specimen in _allSpecimens) specimen.StopHit();
    }

    private void ShowObservation(bool value)
    {
        if (_comparing || TransitionDirector.IsBusy) return;
        CancelInteractions();
        _observing = value;
        _main.Visible = !value;
        _observation.Visible = value;
        Refresh();
    }

    private void RequestBack()
    {
        if (!_built || TransitionDirector.IsBusy || _inputShield.Visible) return;
        CancelInteractions();
        if (_modal is not null) { (_modalBack ?? CloseModal)(); return; }
        if (_observing) { ShowObservation(false); return; }
        if (_draft.IsDirty) ShowUnsaved(Leave);
        else Leave();
    }

    private void Leave() => TransitionDirector.Navigate(UiRoutes.Settings, TransitionKind.Back);

    private void Feedback(string message)
    {
        _feedback = message;
        _feedbackTtl = 4;
        SetProcess(true);
        Refresh();
    }

    public override void _Process(double delta)
    {
        _feedbackTtl -= delta;
        if (_feedbackTtl > 0) return;
        _feedback = "";
        SetProcess(false);
        Refresh();
    }

    private Label Text(Node parent, string text, float x, float y, float width, float height, int size = 18,
        Color? color = null, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Position = new Vector2(x, y), Size = new Vector2(width, height), Text = text,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = align,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", text.Any(c => c > 127) ? UiFonts.Cjk : bold ? UiFonts.TechBold : UiFonts.Tech);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? TextColor);
        parent.AddChild(label);
        return label;
    }

    private CutPanel Panel(Node parent, float x, float y, float width, float height, Color? fill = null, Color? border = null, float cut = 12)
    {
        var p = new CutPanel { Position = new(x, y), Size = new(width, height), Fill = fill ?? PanelColor,
            Border = border ?? BorderColor, BorderWidth = 1, Cut = cut, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(p);
        return p;
    }

    private TechButton Button(Node parent, string text, float x, float y, float width, float height,
        Action action, TechButton.Kind kind = TechButton.Kind.Outline, int size = 18, bool register = true)
    {
        var button = new TechButton { Position = new(x, y), Size = new(width, height), Text = text,
            FontSize = size, Cut = 12, StyleKind = kind };
        button.Pressed += () => { if (!TransitionDirector.IsBusy && !_inputShield.Visible) action(); };
        parent.AddChild(button);
        if (register) _normalButtons.Add(button);
        return button;
    }

    private void Line(Node parent, float x, float y, float width) => parent.AddChild(new ColorRect
    {
        Position = new(x, y), Size = new(width, 1), Color = BorderColor, MouseFilter = Control.MouseFilterEnum.Ignore,
    });

    private NotePaletteSpecimen Specimen(Node parent, float x, float y, float scale = 1)
    {
        var view = new NotePaletteSpecimen { Position = new(x, y), Scale = new Vector2(scale, scale) };
        parent.AddChild(view);
        _allSpecimens.Add(view);
        return view;
    }

    private TechButton CompareButton(Node parent)
    {
        var button = Button(parent, "按住对比原方案", 48, 984, 272, 60, () => { }, size: 19, register: false);
        button.Momentary = true;
        button.HeldChanged += Compare;
        _compareButtons.Add(button);
        return button;
    }
}
