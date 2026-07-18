using System.Windows;
using System.Windows.Input;

namespace OcrSearch.App;

public partial class PromptDialog : Window
{
    private PromptDialog(
        string title,
        string heading,
        string message,
        string primaryText,
        string secondaryText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;
        SecondaryButton.Content = secondaryText;
    }

    public static bool Show(
        Window owner,
        string heading,
        string message,
        string primaryText,
        string secondaryText,
        string title = "OcrSearch")
    {
        var dialog = new PromptDialog(title, heading, message, primaryText, secondaryText)
        {
            Owner = owner,
        };
        return dialog.ShowDialog() == true;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
