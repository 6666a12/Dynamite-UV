using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DynamiteUniverse.Editor.Shell;

/// <summary>Inline, non-interactive help on hover. The keyboard can also show it.</summary>
public sealed class ContextHelpButton : Button
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<ContextHelpButton, string>(nameof(Text), "");

    protected override Type StyleKeyOverride => typeof(Button);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ContextHelpButton()
    {
        Content = "ⓘ";
        FontFamily = new FontFamily("Segoe UI Symbol");
        FontSize = 12;
        Classes.Add("ghost");
        Classes.Add("context-help");
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Width = Height = 16;
        MinWidth = MinHeight = 0;
        Padding = new Thickness(0);
        Bind(AutomationProperties.NameProperty, WorkbenchLocalizer.Shared.CreateBinding("Help.Context"));
        Bind(AutomationProperties.HelpTextProperty, this.GetObservable(TextProperty));

        var body = new TextBlock { MaxWidth = 216, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        body.Bind(TextBlock.TextProperty, this.GetObservable(TextProperty));
        ToolTip.SetTip(this, new ToolTip { Classes = { "context-help" }, Content = body, IsHitTestVisible = false });
        ToolTip.SetPlacement(this, PlacementMode.BottomEdgeAlignedLeft);
        ToolTip.SetVerticalOffset(this, 2);
        ToolTip.SetShowDelay(this, 0);
    }

    protected override void OnClick()
    {
        base.OnClick();
        ToolTip.SetIsOpen(this, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetIsOpen(this, false);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!IsPointerOver) ToolTip.SetIsOpen(this, false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ToolTip.GetIsOpen(this))
        {
            ToolTip.SetIsOpen(this, false);
            e.Handled = true;
        }
        else base.OnKeyDown(e);
    }
}
