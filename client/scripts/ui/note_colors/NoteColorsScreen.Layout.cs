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
        Button(_content, "‹", 48, 28, 76, 76, RequestBack, size: 40);
        _title = Text(_content, "NOTE COLORS", 152, 28, 700, 48, 36, bold: true);
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
            _collection[i] = Specimen(_main, CollectionPositions[i].X, CollectionPositions[i].Y);
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

        Panel(_main, 1200, 160, 672, 768, cut: 24);
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
        Text(_main, "原色", 1232, 726, 100, 24, 14, MutedColor);
        Text(_main, "新色", 1374, 726, 100, 24, 14, MutedColor);
        Text(_main, "HEX", 1530, 726, 100, 24, 14, MutedColor);
        _oldSwatch = Panel(_main, 1232, 758, 112, 48, cut: 8);
        Text(_main, "→", 1344, 758, 34, 48, 22, MutedColor, align: HorizontalAlignment.Center);
        _newSwatch = Panel(_main, 1378, 758, 112, 48, cut: 8);
        _hexFrame = Panel(_main, 1530, 758, 310, 48, new Color("0B111A"), cut: 8);
        _hex = InputField(_main, 1542, 760, 196, 44, 7);
        _hex.TextChanged += HexChanged;
        _hex.TextSubmitted += _ => { CommitHex(); _hex.ReleaseFocus(); Refresh(); };
        _hex.FocusExited += () => { CommitHex(); Refresh(); };
        Button(_main, "RGB  ›", 1744, 758, 96, 48, ShowRgb, size: 14);
        Text(_main, "最近使用", 1232, 829, 126, 28, 14, MutedColor);
        for (var i = 0; i < 5; i++)
        {
            var index = i;
            _recentButtons.Add(Button(_main, "", 1342 + i * 50, 817, 46, 50, () =>
            {
                if (index < _recent.Count) Mutate(() => _draft.SetColor(_channel, _recent[index]));
            }));
            _recentSwatches.Add(Panel(_main, 1348 + i * 50, 826, 34, 32, cut: 6));
        }
        Line(_main, 1232, 876, 608);
        _readabilityHint = Text(_main, "对比背景与相近音符", 1262, 889, 444, 28, 14, MutedColor);
        Text(_main, "ⓘ", 1232, 889, 22, 28, 16, MutedColor);
        Button(_main, "观察  ↗", 1730, 881, 110, 44, () => ShowObservation(true), size: 14);
        foreach (var color in _draft.Current.Slots[_draft.Current.ActiveIndex].Colors.Take(5)) _recent.Add(color);

        Line(_main, 48, 960, 1824);
        CompareButton(_main);
        _undoButton = Button(_main, "撤销", 344, 984, 108, 60, () => History(false));
        _redoButton = Button(_main, "重做", 464, 984, 108, 60, () => History(true));
        _sessionHint = Text(_main, "仅预览，应用后生效", 622, 988, 610, 56, 18, MutedColor);
        _saveAsButton = Button(_main, "另存为预设", 1268, 984, 276, 60, () => ShowNameDialog(), size: 19);
        _applyButton = Button(_main, "应用配色", 1568, 984, 304, 60, () => ApplyDraft(), TechButton.Kind.Solid, 21);
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
        Text(_observation, "同一颜色 · 三种背景 · 相近类型", 1318, 202, 554, 28, 15, MutedColor, align: HorizontalAlignment.Right);
        Line(_observation, 48, 258, 1824);
        Text(_observation, "背景对比", 48, 282, 400, 36, 24, bold: true);
        Text(_observation, "同一类型 · 同一显示尺寸", 548, 286, 620, 30, 15, MutedColor, align: HorizontalAlignment.Right);
        string[] titles = ["深色基准", "中间背景", "亮背景测试"];
        for (var i = 0; i < 3; i++)
        {
            var x = 48 + i * 384;
            Panel(_observation, x, 340, 352, 464, TestBackgrounds[i], new Color("35414E"), 16);
            Text(_observation, titles[i], x + 24, 358, 304, 34, 20, bold: true);
            Text(_observation, "#" + NotePalette.ToHex(TestBackgrounds[i]), x + 24, 395, 304, 25, 14, new Color("99A9BB"));
            _backgroundSpecimens[i] = Specimen(_observation, x + 176, 663);
            Text(_observation, i == 0 ? "游戏底色" : "测试色，不代表实机采样", x + 24, 690, 304, 24, 13, new Color("B0BDCB"));
            Line(_observation, x + 24, 730, 304);
            Text(_observation, "基础色对比", x + 24, 748, 150, 28, 14, new Color("A7B6C8"));
            _contrasts[i] = Text(_observation, "0.00 : 1", x + 170, 742, 158, 40, 27, align: HorizontalAlignment.Right);
        }
        Text(_observation, "与相近音符并排看", 1200, 282, 500, 36, 24, bold: true);
        Text(_observation, "按基础色色差排序", 1594, 287, 278, 28, 14, MutedColor, align: HorizontalAlignment.Right);
        Panel(_observation, 1200, 340, 672, 250, new Color("0D131C"), cut: 18);
        _observeStatus = Text(_observation, "最接近当前颜色", 1224, 356, 360, 26, 14, MutedColor);
        _delta = Text(_observation, "ΔE  0.0", 1650, 354, 196, 30, 20, new Color("BCCBDD"), align: HorizontalAlignment.Right);
        _pairLeft = Specimen(_observation, 1368, 453);
        _pairRight = Specimen(_observation, 1704, 453);
        _pairLeftName = Text(_observation, "HOLD", 1232, 485, 272, 30, 19, bold: true);
        _pairName = Text(_observation, "EX TAP", 1568, 485, 272, 30, 19, bold: true);
        _pairLeftHex = Text(_observation, "#7BECC5", 1232, 521, 272, 26, 15, MutedColor);
        _pairHex = Text(_observation, "#B5EBFF", 1568, 521, 272, 26, 15, MutedColor);
        Text(_observation, "比较对象", 1200, 611, 130, 28, 15, MutedColor);
        for (var i = 0; i < 2; i++)
        {
            var index = i;
            _pairButtons[i] = Button(_observation, "", 1350 + i * 170, 606, 158, 44, () =>
            {
                if (_pairOrder.Length > index) { _pairOverride = _pairOrder[index]; Refresh(); }
            }, size: 16);
        }
        Button(_observation, "其他  ▾", 1690, 606, 182, 44, ShowPairChooser, size: 16);
        Line(_observation, 1200, 674, 672);
        Text(_observation, "先看形态，再参考数值", 1200, 698, 672, 34, 21, bold: true);
        Text(_observation, "轮廓、端标、Hold 条身是否仍然清楚？", 1200, 750, 660, 30, 18, new Color("A7B5C7"));
        Text(_observation, "色差越小，基础色越接近；没有统一的“安全分”。", 1200, 796, 660, 30, 17, new Color("9CAABD"));
        Text(_observation, "ⓘ  这里不会限制颜色，也不会自动修改草稿。", 1200, 856, 672, 34, 16, MutedColor);
        Line(_observation, 48, 836, 1120);
        Text(_observation, "数值口径", 48, 854, 140, 28, 16, new Color("B8C4D3"));
        Text(_observation, "对比度取基础色与背景的相对亮度；色差使用 CIE76 ΔE。", 208, 848, 948, 34, 16, MutedColor);
        Text(_observation, "不计光晕、透明条身和动态叠加，不能替代实际游玩观感。", 208, 884, 948, 30, 16, new Color("738498"));
        Line(_observation, 48, 960, 1824);
        CompareButton(_observation);
        Text(_observation, "仅观察当前草稿 · 返回不会丢失修改", 360, 988, 860, 56, 18, MutedColor);
        Button(_observation, "返回调色", 1568, 984, 304, 60, () => ShowObservation(false), TechButton.Kind.Solid, 21);
    }

    private void RefreshObservation(NotePalette palette, Color color)
    {
        _observedColor.Text = $"{Names[_channel]}  /  #{NotePalette.ToHex(color)}";
        for (var i = 0; i < 3; i++)
        {
            _backgroundSpecimens[i].Position = new Vector2(224 + i * 384, _channel == 3 ? 663 : 568);
            _backgroundSpecimens[i].Configure(Types[_channel], color, 272, Settings.NoteBrightnessScale,
                showPath: _channel == 3, tailWidth: 240, gap: 172);
            var value = NotePaletteMetrics.Contrast(color, TestBackgrounds[i]);
            _contrasts[i].Text = $"{value:0.00} : 1";
            _contrasts[i].AddThemeColorOverride("font_color", value < NotePaletteMetrics.MinContrast ? WarningColor : TextColor);
        }
        _pairOrder = Enumerable.Range(0, 6).Where(i => i != _channel)
            .OrderBy(i => NotePaletteMetrics.DeltaE(color, palette.Get((NoteColorChannel)i))).ToArray();
        var pair = _pairOverride is { } requested && requested != _channel ? requested : _pairOrder[0];
        var other = palette.Get((NoteColorChannel)pair);
        _pairLeft.Configure(Types[_channel], color, 272, Settings.NoteBrightnessScale);
        _pairRight.Configure(Types[pair], other, 272, Settings.NoteBrightnessScale);
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
