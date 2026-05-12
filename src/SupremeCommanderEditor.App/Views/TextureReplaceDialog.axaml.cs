using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using SupremeCommanderEditor.App.Services;
using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.App.Views;

/// <summary>
/// Asks the user which existing strata to replace when they pick a new library texture but the
/// map has no free slot. Shows the proposed new texture at the top + a grid of the map's current
/// strata cards (clickable). Cancel returns null.
/// </summary>
public partial class TextureReplaceDialog : Window
{
    public sealed record StrataChoice(
        int Strata,
        string StrataLabel,
        string TextureLabel,
        Bitmap Thumbnail);

    /// <summary>Set to the chosen strata index when the user picks one; null on cancel.</summary>
    public int? SelectedStrata { get; private set; }

    public TextureReplaceDialog()
    {
        InitializeComponent();
    }

    public TextureReplaceDialog(
        string newTextureName,
        Bitmap newTextureThumb,
        IReadOnlyList<StrataChoice> choices,
        int maxPaintable) : this()
    {
        NewTextureImage.Source = newTextureThumb;
        ExplanationText.Text =
            $"This map already uses {maxPaintable}/{maxPaintable} blendable textures. " +
            $"Pick the strata you want to replace with \"{newTextureName}\", or cancel.";
        StrataItems.ItemsSource = choices;
    }

    private void OnStrataPicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int strata)
        {
            SelectedStrata = strata;
            Close();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        SelectedStrata = null;
        Close();
    }
}
