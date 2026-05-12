using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SupremeCommanderEditor.App.Views;

public partial class InfoDialog : Window
{
    public InfoDialog()
    {
        InitializeComponent();
    }

    public InfoDialog(string title, string body) : this()
    {
        Title = title;
        Body.Text = body;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
