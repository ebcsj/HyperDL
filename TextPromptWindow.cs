using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HyperDL;

/// <summary>
/// Small modal prompt used for URL editing. Kept code-only so the project does
/// not need another XAML file for a single text box.
/// </summary>
public sealed class TextPromptWindow : Window
{
    private readonly TextBox _input;

    private TextPromptWindow(string title, string label, string initialValue)
    {
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFB));
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var caption = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        root.Children.Add(caption);

        _input = new TextBox
        {
            Text = initialValue,
            Height = 34,
            Padding = new Thickness(9, 6, 9, 6),
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White
        };
        _input.KeyDown += Input_KeyDown;
        Grid.SetRow(_input, 1);
        root.Children.Add(_input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 92,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 92,
            Height = 34,
            IsDefault = true
        };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    public string Value => _input.Text.Trim();

    public static string? Prompt(
        Window owner,
        string title,
        string label,
        string initialValue)
    {
        var dialog = new TextPromptWindow(title, label, initialValue) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Accept();
            e.Handled = true;
        }
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            return;
        }

        DialogResult = true;
    }
}
