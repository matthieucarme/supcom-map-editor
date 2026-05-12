using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SupremeCommanderEditor.App.Views;

public partial class RenameMapDialog : Window
{
    public string? Result { get; private set; }

    public RenameMapDialog() { InitializeComponent(); }

    public RenameMapDialog(string currentName, string currentFolder) : this()
    {
        NameBox.Text = currentName;
        NameBox.SelectAll();
        HintText.Text = $"Current folder: {currentFolder}";
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = NameBox.Text?.Trim();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) { Result = null; Close(); }
}
