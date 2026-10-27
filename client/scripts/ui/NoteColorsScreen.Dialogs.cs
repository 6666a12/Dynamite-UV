using Godot;
using DynamiteUniverse.Game;

namespace DynamiteUniverse.Ui;

public partial class NoteColorsScreen
{
    private Control OpenModal(float x, float y, float width, float height, string title, Action? back = null)
    {
        CancelInteractions();
        CloseModal();
        _modal = new Control { Size = UiLayout.DesignSize, MouseFilter = Control.MouseFilterEnum.Stop };
        _modalLayer.AddChild(_modal);
        // The overlay catches pointer events; disabling the content also prevents its focused
        // keyboard controls from activating behind a modal. The CanvasLayer is a sibling.
        _content.ProcessMode = ProcessModeEnum.Disabled;
        _modal.AddChild(new ColorRect
        {
            Size = UiLayout.DesignSize, Color = new Color(0.015f, .025f, .04f, .82f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        });
        Panel(_modal, x, y, width, height, PanelColor, new Color("3C4B5B"), 20);
        Text(_modal, title, x + 32, y + 20, width - 130, 44, 25, bold: true);
        Button(_modal, "×", x + width - 76, y + 16, 48, 48, back ?? CloseModal, size: 26, register: false);
        _modalBack = back;
        return _modal;
    }

    private void CloseModal()
    {
        if (_modal is not null)
        {
            _modal.Visible = false;
            _modal.QueueFree();
            _modal = null;
        }
        _modalBack = null;
        if (_content is not null) _content.ProcessMode = ProcessModeEnum.Inherit;
    }

    private void ShowUnsaved(Action proceed)
    {
        var root = OpenModal(560, 360, 800, 300, "有尚未应用的配色修改");
        Text(root, "应用整个配色库草稿，或放弃所有未应用修改。", 592, 438, 736, 38, 20, MutedColor);
        Text(root, "取消会保留草稿，回到当前页面。", 592, 482, 736, 30, 16, MutedColor);
        Button(root, "取消", 592, 558, 180, 64, CloseModal, register: false);
        Button(root, "放弃修改", 788, 558, 220, 64, () =>
        {
            CloseModal();
            _draft.DiscardChanges();
            Refresh();
            proceed();
        }, register: false);
        Button(root, "应用并继续", 1024, 558, 304, 64, () =>
        {
            CloseModal();
            if (ApplyDraft()) proceed();
        }, TechButton.Kind.Solid, register: false);
    }

    private void ShowPresets()
    {
        var snapshot = _draft.Current;
        var occupied = Enumerable.Range(0, snapshot.Slots.Count).Where(i => !snapshot.Slots[i].IsEmpty).ToArray();
        var height = 224 + ((occupied.Length + 1) / 2) * 108;
        var y = Math.Max(144, (1080 - height) / 2f);
        var root = OpenModal(460, y, 1000, height, "配色预设");
        Text(root, $"{occupied.Length} / 10 个预设 · 管理操作仅修改草稿，应用后保存", 492, y + 68, 936, 30, 17, MutedColor);
        for (var n = 0; n < occupied.Length; n++)
        {
            var index = occupied[n];
            var slot = snapshot.Slots[index];
            var x = 492 + n % 2 * 476;
            var rowY = y + 116 + n / 2 * 108;
            var choose = Button(root, "", x, rowY, 388, 92, () => ChoosePreset(index), register: false);
            choose.Selected = snapshot.ActiveIndex == index;
            Text(root, slot.Name, x + 20, rowY + 34, 350, 40, 22, bold: true);
            for (var c = 0; c < 6; c++) Panel(root, x + 20 + c * 25, rowY + 16, 17, 12, Color.FromHtml("#" + slot.Colors[c]), cut: 0);
            Text(root, index == 0 ? "出厂槽" : $"槽 {index + 1:00}", x + 270, rowY + 8, 98, 24, 13, MutedColor, align: HorizontalAlignment.Right);
            Button(root, "···", x + 396, rowY, 64, 92, () => ShowPresetActions(index), size: 24, register: false);
        }
        var create = Button(root, "+ 新建预设", 492, y + height - 76, 300, 52, () => ShowNameDialog(), register: false);
        create.Enabled = occupied.Length < 10;
        Text(root, occupied.Length == 10 ? "容量已满，可通过 ··· 清空非出厂槽。" : "新预设复制当前颜色，不自动应用。", 820, y + height - 76, 608, 52, 16, MutedColor);
    }

    private void ChoosePreset(int index)
    {
        CloseModal();
        void Choose()
        {
            // Discard restores the whole applied library, so an unsaved target may no longer exist.
            if (_draft.Current.Slots[index].IsEmpty)
            {
                Feedback("该预设尚未应用，已随草稿一起放弃");
                return;
            }
            _draft.SelectSlot(index);
            _feedback = "";
            Refresh();
        }
        if (index == _draft.Current.ActiveIndex) return;
        if (_draft.IsDirty) ShowUnsaved(Choose);
        else Choose();
    }

    private void ShowPresetActions(int index)
    {
        var slot = _draft.Current.Slots[index];
        var root = OpenModal(650, 300, 620, 470, slot.Name, ShowPresets);
        Text(root, "预设管理 · 应用后才写入设置", 682, 370, 556, 32, 17, MutedColor);
        Button(root, "重命名", 682, 430, 556, 64, () => ShowNameDialog(index), register: false);
        Button(root, "恢复六种出厂颜色", 682, 514, 556, 64, () => ShowConfirm("恢复此预设的颜色？", "保留预设名称，仅恢复六类音符颜色。", () =>
        {
            _draft.ResetSlot(index);
            Refresh();
            ShowPresets();
        }, ShowPresets), register: false);
        var clear = Button(root, "清空预设", 682, 598, 556, 64, () => ShowConfirm("清空此预设？", "清空当前预设会选回出厂槽，此操作可撤销。", () =>
        {
            _draft.ClearSlot(index);
            Refresh();
            ShowPresets();
        }, ShowPresets), register: false);
        clear.Enabled = index != 0;
        Text(root, index == 0 ? "出厂槽不可清空。" : "所有管理操作均可在主页面撤销。", 682, 690, 556, 34, 16, MutedColor);
    }

    private void ShowConfirm(string title, string message, Action confirm, Action cancel)
    {
        var root = OpenModal(560, 380, 800, 280, title, cancel);
        Text(root, message, 592, 458, 736, 56, 19, MutedColor);
        Button(root, "取消", 592, 562, 304, 60, cancel, register: false);
        Button(root, "确认", 1024, 562, 304, 60, () => { CloseModal(); confirm(); }, TechButton.Kind.Solid, register: false);
    }

    private void ShowNameDialog(int? rename = null)
    {
        var snapshot = _draft.Current;
        var index = rename ?? Enumerable.Range(1, 9).FirstOrDefault(i => snapshot.Slots[i].IsEmpty, -1);
        if (index < 0) { Feedback("预设已满，请先清空一个非出厂槽"); return; }
        var root = OpenModal(600, 340, 720, 390, rename.HasValue ? "重命名预设" : "另存为预设", ShowPresets);
        Panel(root, 632, 436, 656, 64, new Color("0B111A"));
        var input = InputField(root, 650, 442, 620, 52, NotePalette.NameMaxLength);
        input.Text = rename.HasValue ? snapshot.Slots[index].Name : "";
        input.PlaceholderText = "PRESET NAME";
        Text(root, "1–12 位：大写字母、数字、空格或 -；名称不可重复。", 632, 520, 656, 30, 17, MutedColor);
        var hint = Text(root, "新建后仍需在主页面应用配色。", 632, 560, 656, 30, 16, MutedColor);
        void Submit()
        {
            var name = input.Text.Trim().ToUpperInvariant();
            if (rename.HasValue && name == snapshot.Slots[index].Name) { ShowPresets(); return; }
            var success = rename.HasValue ? _draft.RenamePreset(index, name) : _draft.CreatePreset(index, name);
            if (!success) { hint.Text = "名称无效或已被其他预设使用。"; hint.AddThemeColorOverride("font_color", WarningColor); return; }
            CloseModal();
            Refresh();
        }
        Button(root, "取消", 632, 628, 240, 60, ShowPresets, register: false);
        Button(root, rename.HasValue ? "确认重命名" : "创建预设", 984, 628, 304, 60, Submit, TechButton.Kind.Solid, register: false);
        input.TextSubmitted += _ => Submit();
        input.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void ShowPairChooser()
    {
        var root = OpenModal(650, 320, 620, 440, "选择比较对象");
        var palette = Palette(_comparing ? _draft.Original : _draft.Current);
        var color = palette.Get((NoteColorChannel)_channel);
        for (var i = 0; i < _pairOrder.Length; i++)
        {
            var index = _pairOrder[i];
            var other = palette.Get((NoteColorChannel)index);
            Button(root, $"{Names[index]}     ΔE {NotePaletteMetrics.DeltaE(color, other):0.0}", 682, 404 + i * 62, 556, 50, () =>
            {
                _pairOverride = index;
                CloseModal();
                Refresh();
            }, register: false);
        }
    }

    private void ShowRgb()
    {
        var root = OpenModal(600, 360, 720, 330, "RGB 精确输入");
        var color = Palette(_draft.Current).Get((NoteColorChannel)_channel);
        var values = new[] { color.R, color.G, color.B };
        var fields = new LineEdit[3];
        for (var i = 0; i < 3; i++)
        {
            Text(root, new[] { "R", "G", "B" }[i], 632 + i * 224, 438, 180, 28, 17, MutedColor);
            Panel(root, 632 + i * 224, 478, 208, 60, new Color("0B111A"));
            fields[i] = InputField(root, 648 + i * 224, 482, 176, 52, 3);
            fields[i].Text = Mathf.RoundToInt(values[i] * 255).ToString();
        }
        var hint = Text(root, "每个通道取 0–255 的整数。", 632, 554, 656, 30, 16, MutedColor);
        Button(root, "取消", 632, 610, 240, 52, CloseModal, register: false);
        Button(root, "使用此颜色", 984, 610, 304, 52, () =>
        {
            var channels = new int[3];
            for (var i = 0; i < 3; i++)
                if (!int.TryParse(fields[i].Text, out channels[i]) || channels[i] is < 0 or > 255)
                { hint.Text = "请输入 0–255 的整数。"; hint.AddThemeColorOverride("font_color", WarningColor); return; }
            _draft.SetColor(_channel, $"{channels[0]:X2}{channels[1]:X2}{channels[2]:X2}");
            RememberCurrentColor();
            CloseModal();
            Refresh();
        }, TechButton.Kind.Solid, register: false);
        fields[0].CallDeferred(Control.MethodName.GrabFocus);
    }
}
