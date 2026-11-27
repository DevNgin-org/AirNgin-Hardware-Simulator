using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;

namespace AirNgin.Simulator;

internal static class Ui
{
    public static readonly FontFamily PersianFont = new("Tahoma");
    public static readonly IBrush Ink = Brush.Parse("#183334"), Teal = Brush.Parse("#09695F"), Muted = Brush.Parse("#48676A"),
        Canvas = Brush.Parse("#EFF4F3"), Line = Brush.Parse("#CCDAD7");
    public static TextBlock Text(string text, double size = 14, IBrush? color = null, bool bold = false) =>
        new()
        {
            Text = text,
            FontFamily = PersianFont,
            FontSize = size,
            Foreground = color ?? Ink,
            FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
    public static Border Wrap(string s, double padding) => new() { Padding = new Thickness(padding), Child = Text(s) };
    public static StackPanel Stack(double gap = 9) => new() { Spacing = gap };
    public static WrapPanel Row(params Control[] controls)
    {
        var p = new WrapPanel();
        foreach (var c in controls) { c.Margin = new Thickness(0, 2, 6, 2); p.Children.Add(c); }
        return p;
    }
    public static Button Button(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = label, MinHeight = 36, CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 6) };
        if (primary) { b.Background = Teal; b.Foreground = Brushes.White; }
        b.Click += (_, _) => action(); return b;
    }
    public static TextBox Box(string? value = "", string? hint = null, bool latin = true) => new()
    {
        Text = value,
        Watermark = hint,
        MinHeight = 35,
        FlowDirection = latin ? FlowDirection.LeftToRight : Language.Direction
    };
    public static string Val(TextBox box) => box.Text ?? "";
    public static StackPanel Field(string title, Control input)
    {
        var p = Stack(4); p.Children.Add(Text(title, 13, Muted)); p.Children.Add(input); return p;
    }
    public static Grid Columns(Control a, Control b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") }; g.Children.Add(a); Grid.SetColumn(b, 2); g.Children.Add(b); return g;
    }
    public static StackPanel Section(string title, string help)
    {
        var panel = Stack(8);
        panel.Children.Add(Text(title, 18, Ink, true));
        panel.Children.Add(Text(help, 13, Muted));
        return panel;
    }
    public static Border Card(Control c) => new()
    {
        Child = c,
        Padding = new Thickness(16),
        Background = Brushes.White,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10)
    };
    public static Window Dialog(string title, int width, int height) => new()
    {
        FontFamily = PersianFont,
        Title = title,
        Width = width,
        Height = height,
        MinWidth = Math.Min(420, width),
        MinHeight = Math.Min(height, 280),
        FlowDirection = Language.Direction,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Background = Canvas
    };
    public static void Form(Window w, Panel fields, Control actions)
    {
        var g = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(18) };
        g.Children.Add(new ScrollViewer { Content = fields }); actions.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(actions, 1); g.Children.Add(actions); w.Content = g;
    }
}
