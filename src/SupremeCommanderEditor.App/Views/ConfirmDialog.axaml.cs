using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Threading.Tasks;

namespace SupremeCommanderEditor.App.Views;

public partial class ConfirmDialog : Window
{
    private bool _confirmed;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string body) : this()
    {
        Title = title;
        Body.Text = body;
    }

    /// <summary>Shows the dialog modally and returns true if the user confirmed (clicked Overwrite),
    /// false on cancel / close.</summary>
    public async Task<bool> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return _confirmed;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) { _confirmed = true;  Close(); }
    private void OnCancel (object? sender, RoutedEventArgs e) { _confirmed = false; Close(); }
}
