using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Cells.UI;

public enum DialogChoice { Cancel, Primary, Secondary }

// Small modal dialog in the widget's style, optionally with one text field.
public sealed class Dialog : Window
{
    readonly TextBox? input;

    public DialogChoice Choice { get; private set; }
    public string Text => input?.Text.Trim() ?? "";

    Dialog(Theme t, string heading, string body, string? note, string primary, string? secondary, string? value)
    {
        Title = "Cells";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        UseLayoutRounding = true;
        FontFamily = new FontFamily("Arial");
        FontSize = 12;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Background = Paint.Of(t.Body);
        Foreground = Paint.Of(t.Ink);

        var panel = new StackPanel { Margin = new Thickness(20, 16, 20, 18), Width = 380 };
        panel.Children.Add(new TextBlock { Text = "CELLS", FontWeight = FontWeights.Bold, FontSize = 10, Foreground = Paint.Of(t.Dim), Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = heading, FontSize = 17, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
        if (body.Length > 0) panel.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Foreground = Paint.Of(t.Ink) });
        if (note != null) panel.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Foreground = Paint.Of(t.Dim), Margin = new Thickness(0, 8, 0, 0) });
        if (value != null)
        {
            input = new TextBox
            {
                Text = value, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(5, 4, 5, 4), BorderThickness = new Thickness(1),
                BorderBrush = Paint.Of(t.Ink), Background = Paint.Of(t.Hover), Foreground = Paint.Of(t.Ink), CaretBrush = Paint.Of(t.Ink),
            };
            input.Loaded += (_, _) =>
            {
                input.Focus();
                input.SelectAll();
            };
            panel.Children.Add(input);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        if (secondary != null) buttons.Children.Add(Button(t, secondary, false, () => Finish(DialogChoice.Secondary)));
        buttons.Children.Add(Button(t, primary, true, () => Finish(DialogChoice.Primary)));
        panel.Children.Add(buttons);

        Content = new Border { BorderBrush = Paint.Of(t.Ink), BorderThickness = new Thickness(1), Child = panel };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(DialogChoice.Cancel);
            if (e.Key == Key.Enter) Finish(DialogChoice.Primary);
        };
    }

    void Finish(DialogChoice choice)
    {
        Choice = choice;
        Close();
    }

    static Button Button(Theme t, string text, bool primary, Action click)
    {
        var face = new Border
        {
            Background = Paint.Of(primary ? t.Ink : t.Body),
            BorderBrush = Paint.Of(t.Ink),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 5, 14, 5),
            Child = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = Paint.Of(primary ? t.Body : t.Ink) },
        };
        var button = new Button { Template = new ControlTemplate(typeof(Button)), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Focusable = false };
        button.Template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        button.Content = face;
        button.Click += (_, _) => click();
        return button;
    }

    public static DialogChoice Ask(Theme t, string heading, string body, string? note, string primary, string? secondary)
    {
        var d = new Dialog(t, heading, body, note, primary, secondary, null);
        d.ShowDialog();
        return d.Choice;
    }

    public static string? Input(Theme t, string heading, string body, string value)
    {
        var d = new Dialog(t, heading, body, null, "Save", "Cancel", value);
        d.ShowDialog();
        return d.Choice == DialogChoice.Primary ? d.Text : null;
    }
}
