using Godot;
using System;
using System.Globalization;
using DynamiteUniverse.ChartEditor.Core;
using DynamiteUniverse.Ui;

namespace DynamiteUniverse.Editor;

internal sealed partial class CreatePageOverlay
{

    // ---------------------------------------------------------------- BPM / OFFSET

    /// <summary>BPM 与 offset 的大数字：LineEdit 直接输入，提交时校验。非有限数（以及 BPM ≤ 0）不写进值，
    /// 短暂红框后回退到上一个合法值，非法参数不会流到 draft factory。</summary>
    private sealed partial class NumericField : Control
    {
        public enum Kind { Bpm, Offset }

        private const float FlashSeconds = .7f;
        private readonly Kind _kind;
        private readonly LineEdit _edit;
        private bool _suppress;
        private float _flash;
        // 焦点 glow（0..1）：本控件没有 stylebox 可给焦点反馈，cyan 描边框按这个值插值透明度。
        private float _glow;

        public double Value { get; private set; }
        public event Action? Committed;

        public NumericField(Kind kind, Rect2 rect)
        {
            _kind = kind;
            Name = kind == Kind.Bpm ? "Bpm" : "OffsetSec";
            Position = rect.Position;
            Size = rect.Size;
            MouseFilter = MouseFilterEnum.Ignore;
            SetProcess(false);
            _edit = new LineEdit
            {
                Position = new Vector2(2f, 3f), Size = rect.Size - new Vector2(4f, 6f),
                Alignment = HorizontalAlignment.Left,
                CaretBlink = true,
            };
            var empty = new StyleBoxEmpty();
            foreach (var state in new[] { "normal", "focus", "read_only" }) _edit.AddThemeStyleboxOverride(state, empty);
            // 与等级数字框同一防护：LineEdit 最小宽度 = minimum_character_width × 'W' 宽，
            // Control.SetSize 会钳到不小于它；34px 大数字在高 UI SCALE 档下会逼近框宽，
            // 清零避免控件被静默撑宽、溢出 TIMING 面板的数字位。
            _edit.AddThemeConstantOverride("minimum_character_width", 0);
            _edit.AddThemeFontOverride("font", UiFonts.TechBold);
            EditorFonts.ApplySize(_edit, 34);
            _edit.AddThemeColorOverride("font_color", UiFonts.Text);
            _edit.AddThemeColorOverride("caret_color", UiFonts.Cyan);
            _edit.TextSubmitted += _ => Commit();
            _edit.FocusExited += Commit;
            // 焦点反馈复用 _Process：glow 向 1 lerp（约 .12s），失焦向 0 回落；MOTION OFF 直接跳终值。
            _edit.FocusEntered += () =>
            {
                if (EditorSettings.Current.MotionMode == UiMotionMode.Off) _glow = 1f;
                SetProcess(true);
                QueueRedraw();
            };
            _edit.FocusExited += () => { SetProcess(true); QueueRedraw(); };
            AddChild(_edit);
            // 与等级数字框同理：树外设 Size 会被默认主题的最小宽度钳制，进树后再回正。
            _edit.Ready += () => _edit.Size = rect.Size - new Vector2(4f, 6f);
        }

        public void SetValue(double value)
        {
            Value = value;
            SetText(Format(value));
        }

        public override void _Process(double delta)
        {
            var step = (float)delta / GlowSeconds;
            var target = _edit.HasFocus() ? 1f : 0f;
            _glow = EditorSettings.Current.MotionMode == UiMotionMode.Off
                ? target
                : Mathf.MoveToward(_glow, target, step);
            // flash 仍在倒数，glow 未到终值也继续跑，两者都停了才关 _Process。
            if (_flash <= 0f && _glow == target) { SetProcess(false); return; }
            if (_flash > 0f) _flash -= (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_glow > 0f)
                EditorOverlayDraw.CutStroke(this, new Rect2(-3f, -3f, Size.X + 6f, Size.Y + 6f), 6f,
                    new Color(UiFonts.Cyan, _glow), 2f);
            if (_flash <= 0f) return;
            EditorOverlayDraw.CutStroke(this, new Rect2(-3f, -3f, Size.X + 6f, Size.Y + 6f), 6f, UiFonts.Giga, 2f);
        }

        private string Format(double value) => _kind == Kind.Bpm
            ? value.ToString("0.0###", CultureInfo.InvariantCulture)
            : value.ToString("+0.000;-0.000;+0.000", CultureInfo.InvariantCulture);

        private void Commit()
        {
            if (_suppress) return;
            if (TryParse(_edit.Text, out var parsed))
            {
                Value = parsed;
                SetText(Format(parsed));
                Committed?.Invoke();
                return;
            }
            _flash = FlashSeconds;
            SetProcess(true);
            SetText(Format(Value));
            QueueRedraw();
        }

        private bool TryParse(string text, out double value)
        {
            value = 0d;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                !double.IsFinite(parsed)) return false;
            if (_kind == Kind.Bpm && parsed <= 0d) return false;
            value = parsed;
            return true;
        }

        private void SetText(string text)
        {
            if (_edit.Text == text) return;
            _suppress = true;
            _edit.Text = text;
            _suppress = false;
        }
    }

    // ---------------------------------------------------------------- 等级输入

    /// <summary>等级输入（设计稿 Widget — Level States，规范 §4 第 5 条）：范围 1..99，‹ › 步进钳制、
    /// 99 时 › 禁用；数字是 LineEdit、永远是权威值；块编码 = 十位 26×34 切角大块 + 个位 12×18 半高小块
    /// （等距 18 不分组），0 位不出块；非法输入被拒（短暂红框 + 回到上一个合法值）；unrated 时数字禁用、
    /// 不出块、等级不写入。</summary>
    private sealed partial class LevelField : Control
    {
        public const int MinLevel = 1, MaxLevel = 99;

        private const float FlashSeconds = .7f;
        private const float StepY = 20f, StepSize = 30f, StepCut = 6f, StepGlyphOffsetX = 15f, StepGlyphCenterY = 35f;
        private const float BackX = 0f, ForwardX = 88f;
        // 数字框居中于两个步进器之间：间隙 30..88 的中点是 59（Widget — Level States 的居中规则——
        // 组件板数字 ctxt 280 正是 250..310 的中点；Create 板样例把数字放在 1062，比真实中点 1067
        // 偏左 5px，是样例的手误，以组件板为准）。框中心 (59,35)，左上角即 (39,15)。
        private const float NumberX = 39f, NumberY = 15f, NumberWidth = 40f, NumberHeight = 40f;
        private const float BlocksX = 152f, BlocksBaseline = 50f;
        private const float BigWidth = 26f, BigHeight = 34f, BigPitch = 34f, BigCut = 4f;
        private const float SmallWidth = 12f, SmallHeight = 18f, SmallPitch = 18f, SmallRadius = 2f;
        private const float GapAfterTens = 6f;
        private const float UnratedX = 772f, UnratedY = 20f, UnratedSize = 20f;
        // 设计稿 UNRATED 的行心是绝对 y=414，本层原点在 (1008,384)，所以局部中心是 30。
        private const float UnratedLabelX = 804f, UnratedLabelCenterY = 30f, UnratedHitWidth = 130f;
        // 红框/焦点框随数字框居中：框中心与数字框同一点 (59,35)。
        private static readonly Rect2 FlashFrame = new(25f, 13f, 68f, 44f);
        /// <summary>hover 目标的最新值（_GuiInput 写、_Process 读推进），与过渡值分开持有。</summary>
        private bool _backTarget, _forwardTarget, _unratedTarget;
        /// <summary>个位小块是设计稿 lvl_blocks 的 `rect r:2`（圆角 2），不是切角。</summary>
        private readonly StyleBoxFlat _smallBlock = new()
        {
            CornerRadiusTopLeft = (int)SmallRadius, CornerRadiusTopRight = (int)SmallRadius,
            CornerRadiusBottomLeft = (int)SmallRadius, CornerRadiusBottomRight = (int)SmallRadius,
        };

        private readonly LineEdit _number;
        private int _level = 10;
        private bool _unrated, _suppress;
        // hover 统一过渡（‹/›/UNRATED 框）：瞬时换色改为 0..1 lerp，_Process 推进到终值自关。
        private HoverFade _backHover, _forwardHover, _unratedHover;
        private float _flash;
        // 数字区焦点 glow（0..1）：cyan 描边框按这个值淡入淡出，_Process 与 flash 合并驱动。
        private float _glow;
        private Color _difficultyColor = UiFonts.Giga;

        public event Action? ValueChanged;

        public LevelField()
        {
            MouseFilter = MouseFilterEnum.Stop;
            SetProcess(false);
            _number = new LineEdit
            {
                Position = new Vector2(NumberX, NumberY), Size = new Vector2(NumberWidth, NumberHeight),
                Alignment = HorizontalAlignment.Center,
                CaretBlink = true,
            };
            var empty = new StyleBoxEmpty();
            foreach (var state in new[] { "normal", "focus", "read_only" }) _number.AddThemeStyleboxOverride(state, empty);
            // LineEdit 的最小宽度 = minimum_character_width（默认 4）× 当前字号 'W' 宽，而 Control.SetSize
            // 会把尺寸钳到不小于最小宽度：40px 的数字框在 22px+ 字号下被静默撑到 ~90px，居中文字因此
            // 偏出两个步进器之间（视觉上"数字不居中"的真因）。清零该常量，控件保持 40px。
            _number.AddThemeConstantOverride("minimum_character_width", 0);
            _number.AddThemeFontOverride("font", UiFonts.TechBold);
            EditorFonts.ApplySize(_number, 22);
            _number.AddThemeColorOverride("caret_color", UiFonts.Cyan);
            _number.AddThemeColorOverride("font_uneditable_color", UiFonts.Dim);
            _number.TextChanged += OnNumberChanged;
            _number.TextSubmitted += _ => CommitNumber();
            _number.FocusExited += CommitNumber;
            // 数字框 stylebox 全空，焦点反馈画在 cyan 描边框上；MOTION OFF 直接跳终值，不动画。
            _number.FocusEntered += () =>
            {
                if (EditorSettings.Current.MotionMode == UiMotionMode.Off) _glow = 1f;
                SetProcess(true);
                QueueRedraw();
            };
            _number.FocusExited += () => { SetProcess(true); QueueRedraw(); };
            AddChild(_number);
            // 初始化器里设 Size 时控件还吃默认主题（16px 字体 → 最小宽 4×'W'≈68.5），会被钳宽；
            // 而上面的 minimum_character_width 清零要进树、theme 缓存刷新后才生效（树外重设 Size
            // 仍按旧缓存钳），所以进树后的 Ready 里再回正一次尺寸。
            _number.Ready += () => _number.Size = new Vector2(NumberWidth, NumberHeight);
            ApplyColor();
            RefreshNumber();
        }

        /// <summary>unrated 时等级为 null（v2 要求 level 与 unrated 恰有其二）。</summary>
        public int? Level => _unrated ? null : _level;
        public bool Unrated => _unrated;

        public Color DifficultyColor
        {
            get => _difficultyColor;
            set { _difficultyColor = value; ApplyColor(); QueueRedraw(); }
        }

        public void SetValue(int level, bool unrated)
        {
            _level = Math.Clamp(level, MinLevel, MaxLevel);
            _unrated = unrated;
            RefreshNumber();
        }

        public override void _Process(double delta)
        {
            var step = (float)delta / GlowSeconds;
            var target = _number.HasFocus() ? 1f : 0f;
            _glow = EditorSettings.Current.MotionMode == UiMotionMode.Off
                ? target
                : Mathf.MoveToward(_glow, target, step);
            // hover 过渡：MOTION OFF 时 Update 直接跳终值，失能（划过禁用步进）也随之回落 0。
            var back = _backHover.Update(BackEnabled && _backTarget, delta);
            var forward = _forwardHover.Update(ForwardEnabled && _forwardTarget, delta);
            var unrated = _unratedHover.Update(_unratedTarget, delta);
            // flash 仍在倒数，glow/hover 未到终值也继续跑，全部停了才关 _Process。
            if (_flash <= 0f && _glow == target && !back && !forward && !unrated) { SetProcess(false); return; }
            if (_flash > 0f) _flash -= (float)delta;
            QueueRedraw();
        }

        public override void _Draw()
        {
            EditorOverlayDraw.LeftText(this, Tech2, new Vector2(0f, 5.5f), "LEVEL", EditorFonts.Pt(11), UiFonts.Dim);
            DrawStepper(BackX, BackEnabled, _backHover.Value, "‹");
            DrawStepper(ForwardX, ForwardEnabled, _forwardHover.Value, "›");
            DrawBlocks();
            DrawUnrated();
            if (_glow > 0f)
                EditorOverlayDraw.CutStroke(this,
                    new Rect2(NumberX - 3f, NumberY - 3f, NumberWidth + 6f, NumberHeight + 6f), 6f,
                    new Color(UiFonts.Cyan, _glow), 2f);
            if (_flash <= 0f) return;
            EditorOverlayDraw.CutStroke(this, FlashFrame, 6f, UiFonts.Giga, 2f);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion motion)
            {
                // hover 目标变了才推进过渡值（_Process 已开时无需重复开，SetProcess 幂等）。
                var back = BackEnabled && BackRect.HasPoint(motion.Position);
                var forward = ForwardEnabled && ForwardRect.HasPoint(motion.Position);
                var unrated = UnratedRect.HasPoint(motion.Position);
                if (back == _backTarget && forward == _forwardTarget && unrated == _unratedTarget) return;
                _backTarget = back;
                _forwardTarget = forward;
                _unratedTarget = unrated;
                SetProcess(true);
                return;
            }
            if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left) return;
            if (BackEnabled && BackRect.HasPoint(button.Position)) { Step(-1); AcceptEvent(); return; }
            if (ForwardEnabled && ForwardRect.HasPoint(button.Position)) { Step(1); AcceptEvent(); return; }
            if (!UnratedRect.HasPoint(button.Position)) return;
            ToggleUnrated();
            AcceptEvent();
        }

        /// <summary>`‹` 只在 unrated 时禁用，到 1 仍可按但被钳制（规范只规定 99 时 `›` 禁用）。</summary>
        private bool BackEnabled => !_unrated;
        private bool ForwardEnabled => !_unrated && _level < MaxLevel;

        private static Rect2 BackRect => new(BackX, StepY, StepSize, StepSize);
        private static Rect2 ForwardRect => new(ForwardX, StepY, StepSize, StepSize);
        private static Rect2 UnratedRect => new(UnratedX, UnratedY - 6f, UnratedHitWidth, UnratedSize + 12f);

        private void ApplyColor()
        {
            _number.AddThemeColorOverride("font_color", _difficultyColor);
            _number.AddThemeColorOverride("selection_color", new Color(_difficultyColor, .35f));
        }

        /// <summary>数字框是唯一权威值；unrated 时禁用并显示 `--`，块不绘制。</summary>
        private void RefreshNumber()
        {
            _suppress = true;
            _number.Editable = !_unrated;
            _number.FocusMode = _unrated ? FocusModeEnum.None : FocusModeEnum.All;
            _number.Text = _unrated ? "--" : _level.ToString(CultureInfo.InvariantCulture);
            _suppress = false;
            QueueRedraw();
        }

        private void OnNumberChanged(string text)
        {
            if (_suppress || _unrated) return;
            if (!TryParseLevel(text, out var parsed) || parsed == _level) return;
            _level = parsed;
            QueueRedraw();
            ValueChanged?.Invoke();
        }

        private void CommitNumber()
        {
            if (_suppress || _unrated) return;
            if (TryParseLevel(_number.Text, out var parsed))
            {
                _level = parsed;
                RefreshNumber();
                ValueChanged?.Invoke();
                return;
            }
            // 非法值（-1、0、100…）拒绝：红框提示后回到上一个合法值。
            _flash = FlashSeconds;
            SetProcess(true);
            RefreshNumber();
            QueueRedraw();
        }

        private void Step(int delta)
        {
            var next = Math.Clamp(_level + delta, MinLevel, MaxLevel);
            if (next == _level) return;
            _level = next;
            RefreshNumber();
            ValueChanged?.Invoke();
        }

        private void ToggleUnrated()
        {
            _unrated = !_unrated;
            RefreshNumber();
            ValueChanged?.Invoke();
        }

        private static bool TryParseLevel(string text, out int level) =>
            int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out level) &&
            level is >= MinLevel and <= MaxLevel;

        private void DrawStepper(float x, bool enabled, float hovered, string glyph)
        {
            // hover 用 lerp 过渡：Line → Cyan 按值推进，失能态（35% alpha 灰描边）不参与 hover 提亮。
            var stroke = enabled
                ? UiFonts.Line.Lerp(UiFonts.Cyan, hovered)
                : new Color(UiFonts.Line, .35f);
            EditorOverlayDraw.CutStroke(this, new Rect2(x, StepY, StepSize, StepSize), StepCut, stroke, 2f);
            EditorOverlayDraw.CenteredText(this, EditorFonts.Medium, new Vector2(x + StepGlyphOffsetX, StepGlyphCenterY),
                glyph, EditorFonts.Pt(14), enabled ? UiFonts.Text : new Color(UiFonts.Text, .3f));
        }

        /// <summary>十进制块编码：十位大块（26×34 切角 4），个位小块（12×18 半高、圆角 2、等距 18 不分组），
        /// 0 位不出块。unrated 时整组不绘制。</summary>
        private void DrawBlocks()
        {
            if (_unrated) return;
            var tens = _level / 10;
            var ones = _level % 10;
            for (var i = 0; i < tens; i++)
                EditorOverlayDraw.CutPanel(this,
                    new Rect2(BlocksX + i * BigPitch, BlocksBaseline - BigHeight, BigWidth, BigHeight),
                    BigCut, _difficultyColor, new Color(0, 0, 0, 0), 0f);
            if (ones == 0) return;
            var start = BlocksX + tens * BigPitch + (tens > 0 ? GapAfterTens : 0f);
            _smallBlock.BgColor = new Color(_difficultyColor, .85f);
            for (var i = 0; i < ones; i++)
                DrawStyleBox(_smallBlock,
                    new Rect2(start + i * SmallPitch, BlocksBaseline - SmallHeight, SmallWidth, SmallHeight));
        }

        private void DrawUnrated()
        {
            var rect = new Rect2(UnratedX, UnratedY, UnratedSize, UnratedSize);
            if (_unrated)
            {
                EditorOverlayDraw.CutPanel(this, rect, 3f, new Color(UiFonts.Cyan, .15f), UiFonts.Cyan, 2f);
                DrawPolyline(new[]
                {
                    rect.Position + new Vector2(4f, 10.5f), rect.Position + new Vector2(8f, 14.5f),
                    rect.Position + new Vector2(16f, 5.5f),
                }, UiFonts.Cyan, 2.4f, true);
            }
            else
            {
                DrawRect(rect, UiFonts.Line.Lerp(UiFonts.Cyan, _unratedHover.Value), false, 2f);
            }
            EditorOverlayDraw.LeftText(this, EditorFonts.Medium, new Vector2(UnratedLabelX, UnratedLabelCenterY),
                "UNRATED", EditorFonts.Pt(13), _unrated ? UiFonts.Cyan : UiFonts.Dim);
        }
    }
}
