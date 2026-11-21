using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

public partial class NoteColorsScreen
{
    private static readonly float[] CollectionWidths = [260, 290, 240, 320, 260, 220];
    private static readonly Vector2[] CollectionPositions =
        [new(258, 322), new(777, 354), new(204, 514), new(586, 663), new(970, 532), new(236, 674)];
    private static readonly Vector2[] CollectionLabelPositions =
        [new(128, 264), new(632, 294), new(84, 454), new(460, 398), new(840, 473), new(126, 612)];

    private void BuildHeader()
    {
        Line(_content, 48, 131, 1824);
        Button(_content, "‹", 48, 28, 76, 76, RequestBack, size: 40).BackChevron = true;
        _title = Text(_content, "NOTE COLORS", 152, 30, 700, 44, 36, bold: true);
        MenuStyle.Heading(_title);
        _subtitle = Text(_content, "音符配色  /  EFFECTS", 154, 80, 700, 26, 16, MutedColor);
        _status = Text(_content, "未应用修改", 1088, 46, 248, 32, 18, WarningColor);
        _presetButton = Button(_content, "", 1352, 28, 520, 76, ShowPresets);
        Text(_content, "CURRENT PRESET", 1376, 38, 240, 20, 12, MutedColor);
        _presetName = Text(_content, "DEFAULT", 1376, 58, 220, 36, 24, bold: true);
        for (var i = 0; i < 6; i++)
            _headerSwatches[i] = Panel(_content, 1620 + i * 28, 63, 20, 20, cut: 0);
        Text(_content, "⌄", 1824, 41, 34, 42, 26, MutedColor, align: HorizontalAlignment.Center);
    }

    private void BuildMain()
    {
        Text(_main, "NOTE COLLECTION", 72, 164, 400, 30, 15, MutedColor);
        Text(_main, "六类音符 · 同屏调色", 72, 204, 500, 28, 18, new Color("B2BECF"));
        Button(_main, "可读性观察  ↗", 932, 164, 212, 48, () => ShowObservation(true), size: 17);
        for (var i = 0; i < 6; i++)
        {
            var p = CollectionLabelPositions[i];
            _collectionLabels[i] = Text(_main, $"{i + 1:00}  /  {Names[i]}", p.X, p.Y, 260, 24, 14, MutedColor);
            var at = CollectionPositions[i];
            _collection[i] = Specimen(_main, at.X, at.Y);
            var width = CollectionWidths[i] + 44f;
            var reach = i == 3 ? 190f : 0f;
            var channel = i;
            var target = Button(_main, "", at.X - width / 2f, at.Y - reach - 32f, width, reach + 64f,
                () => SelectChannel(channel), frameOnly: true);
            target.Name = $"SpecimenPick{i}";
            target.AccessibilityName = Names[i];
            ContactRim.Attach(target, () => target.Enabled);
        }
        _selectionHint = Text(_main, "当前编辑 · HOLD", 426, 704, 390, 26, 15, new Color("8FA69F"));
        Text(_main, "静态陈列 · 真实音符材质", 832, 704, 280, 26, 14, new Color("68798B"), align: HorizontalAlignment.Right);
        Line(_main, 72, 780, 1072);
        Button(_main, "测试命中", 72, 804, 168, 48, () =>
        {
            CancelInteractions();
            _collection[_channel].PlayHit(Palette(_draft.Current), _draft.Current.BurstFollowsNote, Settings);
        }, size: 17);
        Text(_main, "仅预览当前类型", 260, 810, 300, 34, 15, MutedColor);
        Text(_main, "命中特效配色", 632, 813, 176, 30, 15, MutedColor);
        _judgeBurst = Button(_main, "判定色", 814, 804, 150, 48, () => Mutate(() => _draft.SetBurst(false)), size: 16);
        _noteBurst = Button(_main, "音符色", 972, 804, 172, 48, () => Mutate(() => _draft.SetBurst(true)), size: 16);
        Text(_main, "调色时，其他音符保持可见", 72, 893, 600, 24, 16, MutedColor);
        Text(_main, $"跟随当前特效设置 · 亮度 {Settings.NoteBrightnessPercent}%", 752, 893, 392, 24, 14,
            new Color("748496"), align: HorizontalAlignment.Right);

        Panel(_main, 1200, 160, 672, 768, cut: 24).BorderWidth = 0;
        Text(_main, "选择音符", 1232, 180, 240, 24, 16, MutedColor);
        Text(_main, "单独调色 · 整套预览", 1540, 180, 300, 24, 14, MutedColor, align: HorizontalAlignment.Right);
        for (var i = 0; i < 6; i++)
        {
            var channel = i;
            var x = 1232 + i % 3 * 205;
            var y = 214 + i / 3 * 72;
            _typeButtons[i] = Button(_main, "", x, y, 197, 64, () => SelectChannel(channel));
            _typeIcons[i] = Specimen(_main, x + 36, y + 32, .58f);
            Text(_main, Names[i], x + 72, y, 116, 64, 18, bold: true);
        }
        Line(_main, 1232, 370, 608);
        _selectedName = Text(_main, "HOLD", 1232, 385, 180, 44, 32, bold: true);
        _selectedDescription = Text(_main, "长按音符 · 头部 / 连线 / 尾部", 1350, 391, 350, 32, 15, MutedColor);
        _resetColor = Button(_main, "恢复此颜色", 1692, 388, 148, 42, () => Mutate(() => _draft.ResetColor(_channel)), size: 15);
        ContactRim.Attach(_resetColor, () => _resetColor.Enabled);
        _sv = new SvField { Position = new(1232, 446), Size = new(608, 214) };
        _sv.DragStarted += () => BeginPicker(false);
        _sv.DragEnded += EndPicker;
        _sv.Changed += SetColor;
        _main.AddChild(_sv);
        _hue = new HueBar { Position = new(1232, 670), Size = new(608, 48) };
        _hue.DragStarted += () => BeginPicker(true);
        _hue.DragEnded += EndPicker;
        _hue.Changed += value =>
        {
            if (_comparing) return;
            _sv.SetHue(value);
            _hue.Hue = value;
            SetColor(Color.FromHsv(value, _sv.Saturation, _sv.Value));
        };
        _main.AddChild(_hue);
        _deskComparison = new NoteColorComparison(_main, new Rect2(1232, 726, 280, 134), TestBackgrounds[0], false);
        Text(_main, "HEX", 1530, 726, 100, 24, 14, MutedColor);
        _hexFrame = Panel(_main, 1530, 758, 310, 48, new Color("0B111A"), cut: 8);
        _hex = InputField(_main, 1542, 760, 196, 44, 7);
        _hex.FocusEntered += () => _hexEditing = true;
        _hex.TextChanged += HexChanged;
        _hex.TextSubmitted += _ => { CommitHex(); _hex.ReleaseFocus(); Refresh(); };
        _hex.FocusExited += () => { CommitHex(); _hexEditing = false; Refresh(); };
        Button(_main, "RGB  ›", 1744, 758, 96, 48, ShowRgb, size: 14);
        Text(_main, "最近", 1530, 829, 53, 28, 14, MutedColor);
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            _recentButtons.Add(Button(_main, "", 1590 + i * 50, 817, 46, 50, () =>
            {
                if (index < RecentColors.Count) Mutate(() => _draft.SetColor(_channel, RecentColors[index]));
            }));
            _recentSwatches.Add(Panel(_main, 1596 + i * 50, 826, 34, 32, cut: 6));
        }
        Line(_main, 1232, 876, 608);
        _readabilityHint = Text(_main, "对比背景与相近音符", 1262, 889, 444, 28, 14, MutedColor);
        Text(_main, "ⓘ", 1232, 889, 22, 28, 16, MutedColor);
        Button(_main, "观察  ↗", 1730, 881, 110, 44, () => ShowObservation(true), size: 14);

        Line(_main, 48, 960, 1824);
        CompareButton(_main);
        _undoButton = Button(_main, "撤销", 344, 984, 108, 60, () => History(false));
        _redoButton = Button(_main, "重做", 464, 984, 108, 60, () => History(true));
        _sessionHint = Text(_main, "仅预览，应用后生效", 622, 988, 610, 56, 18, MutedColor);
        _saveAsButton = Button(_main, "另存为预设", 1268, 984, 276, 60, () => ShowNameDialog(), size: 19);
        _applyButton = Button(_main, "应用配色", 1568, 984, 304, 60, () => ApplyDraft(), TechButton.Kind.Solid, 21);
        ContactRim.Attach(_applyButton, () => _applyButton.Enabled);
    }

    private void BuildObservation()
    {
        Text(_observation, "观察对象", 48, 160, 150, 24, 16, MutedColor);
        for (var i = 0; i < 6; i++)
        {
            var channel = i;
            var x = 224 + i * 182;
            _observeTypes[i] = Button(_observation, "", x, 160, 170, 72, () => SelectChannel(channel));
            _observeSwatches[i] = Panel(_observation, x + 18, 186, 20, 20, cut: 0);
            Text(_observation, Names[i], x + 52, 160, 110, 72, 18, bold: true);
        }
        _observedColor = Text(_observation, "HOLD  /  #7BECC5", 1400, 162, 472, 32, 24, bold: true, align: HorizontalAlignment.Right);
        Text(_observation, "原方案与草稿 · 三种背景", 1318, 202, 554, 28, 15, MutedColor, align: HorizontalAlignment.Right);
        Line(_observation, 48, 258, 1824);
        string[] titles = ["深色基准", "中间背景", "亮背景测试"];
        for (var i = 0; i < 3; i++)
        {
            var x = 48 + i * 616;
            Text(_observation, titles[i], x, 275, 340, 40, 24, bold: true);
            Text(_observation, "#" + NotePalette.ToHex(TestBackgrounds[i]), x + 375, 278, 217, 34,
                18, MutedColor, align: HorizontalAlignment.Right);
            _backgroundComparisons[i] = new NoteColorComparison(_observation,
                new Rect2(x, 336, 592, 240), TestBackgrounds[i], true);
        }
        Text(_observation, "相近音符", 48, 608, 420, 44, 26, bold: true);
        _delta = Text(_observation, "ΔE  0.0", 562, 608, 299, 44, 23, align: HorizontalAlignment.Right);
        Panel(_observation, 48, 668, 814, 240, TestBackgrounds[0], cut: 14);
        _pairLeft = Specimen(_observation, 251, 780);
        _pairRight = Specimen(_observation, 659, 780);
        _pairLeftName = Text(_observation, "HOLD", 76, 684, 328, 30, 19, bold: true);
        _pairName = Text(_observation, "EX TAP", 484, 684, 328, 30, 19, bold: true);
        _pairLeftHex = Text(_observation, "", 76, 862, 328, 26, 16, MutedColor);
        _pairHex = Text(_observation, "", 484, 862, 328, 26, 16, MutedColor);
        Text(_observation, "比较对象", 928, 608, 320, 44, 23, bold: true);
        _observeStatus = Text(_observation, "", 1270, 615, 602, 32, 18, MutedColor, align: HorizontalAlignment.Right);
        for (var i = 0; i < 2; i++)
        {
            var index = i;
            _pairButtons[i] = Button(_observation, "", 928 + i * 246, 684, 224, 68, () =>
            {
                if (_pairOrder.Length > index) { _pairOverride = _pairOrder[index]; Refresh(); }
            }, size: 20);
        }
        Button(_observation, "其他  ▾", 1420, 684, 224, 68, ShowPairChooser, size: 18);
        Button(_observation, "最近色", 1666, 684, 206, 68, () => { _pairOverride = null; Refresh(); }, size: 18);
        Text(_observation, "比较基础色的对比度与 CIE76 ΔE。", 928, 800, 930, 36, 19, MutedColor);
        Text(_observation, "不计光晕、透明条身和动态叠加；数值仅供参考。", 928, 852, 930, 32, 17, MutedColor);
        Line(_observation, 48, 960, 1824);
        CompareButton(_observation);
        Text(_observation, "仅观察当前草稿 · 返回不会丢失修改", 360, 988, 860, 56, 18, MutedColor);
        Button(_observation, "返回调色", 1568, 984, 304, 60, () => ShowObservation(false), TechButton.Kind.Solid, 21);
    }

    private void RefreshObservation(NotePalette palette, Color color, Color originalColor)
    {
        _observedColor.Text = $"{Names[_channel]}  /  #{NotePalette.ToHex(color)}";
        for (var i = 0; i < _backgroundComparisons.Length; i++)
            _backgroundComparisons[i].Show(Types[_channel], originalColor, color, _comparing, Settings.NoteBrightnessScale);
        // Stable comparison object while held: colours switch in place, but the identity
        // of the neighbour must not change merely because Original has a different nearest hue.
        var current = Palette(_draft.Current);
        var ink = current.Get((NoteColorChannel)_channel);
        _pairOrder = Enumerable.Range(0, 6).Where(i => i != _channel)
            .OrderBy(i => NotePaletteMetrics.DeltaE(ink, current.Get((NoteColorChannel)i))).ToArray();
        var pair = _pairOverride is { } requested && requested != _channel ? requested : _pairOrder[0];
        var other = palette.Get((NoteColorChannel)pair);
        _pairLeft.Position = new Vector2(251, _channel == 3 ? 819 : 780);
        _pairRight.Position = new Vector2(659, pair == 3 ? 819 : 780);
        _pairLeft.Configure(Types[_channel], color, 256, Settings.NoteBrightnessScale, showPath: _channel == 3, tailWidth: 212, gap: 78);
        _pairRight.Configure(Types[pair], other, 256, Settings.NoteBrightnessScale, showPath: pair == 3, tailWidth: 212, gap: 78);
        _pairLeftName.Text = Names[_channel];
        _pairLeftHex.Text = "#" + NotePalette.ToHex(color);
        _pairName.Text = Names[pair];
        _pairHex.Text = "#" + NotePalette.ToHex(other);
        var delta = NotePaletteMetrics.DeltaE(color, other);
        _delta.Text = $"ΔE  {delta:0.0}";
        var threshold = (_channel == 0 && pair == 1) || (_channel == 1 && pair == 0)
            ? NotePaletteMetrics.MinTapExDeltaE : NotePaletteMetrics.MinPairDeltaE;
        _observeStatus.Text = delta < threshold ? "基础色接近 · 留意轮廓与端标" : pair == _pairOrder[0] ? "最接近当前颜色" : "手动选择的比较对象";
        _observeStatus.AddThemeColorOverride("font_color", delta < threshold ? WarningColor : MutedColor);
        for (var i = 0; i < 2; i++)
        {
            _pairButtons[i].Text = Names[_pairOrder[i]];
            _pairButtons[i].Selected = pair == _pairOrder[i];
        }
    }

    private LineEdit InputField(Node parent, float x, float y, float width, float height, int maxLength = 0)
    {
        var input = new LineEdit { Position = new(x, y), Size = new(width, height), MaxLength = maxLength };
        input.AddThemeFontOverride("font", UiFonts.Tech);
        input.AddThemeFontSizeOverride("font_size", 22);
        input.AddThemeColorOverride("font_color", TextColor);
        input.AddThemeColorOverride("caret_color", UiFonts.Cyan);
        input.AddThemeColorOverride("selection_color", new Color(UiFonts.Cyan, .25f));
        input.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        input.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        parent.AddChild(input);
        return input;
    }
}
