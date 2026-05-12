using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SupremeCommanderEditor.App.Views;

/// <summary>
/// Generic slider popup — takes 1 to N (label, min, max, initial) entries and lets the user adjust
/// them. Returns the final values on OK, or null on Cancel. Used by the Lighting palette for the
/// non-color settings (Multiplier, Sun Direction X/Y/Z, Bloom, Fog Start/End).
/// </summary>
public partial class SliderPopupDialog : Window
{
    public partial class SliderItem : ObservableObject
    {
        [ObservableProperty] private string _label = "";
        [ObservableProperty] private double _value;
        [ObservableProperty] private double _min;
        [ObservableProperty] private double _max;
    }

    private readonly List<SliderItem> _items = new();

    /// <summary>Final values on OK; null on Cancel.</summary>
    public double[]? Result { get; private set; }

    public SliderPopupDialog()
    {
        InitializeComponent();
    }

    public SliderPopupDialog(string title, IEnumerable<(string Label, double Initial, double Min, double Max)> sliders)
        : this()
    {
        HeaderText.Text = title;
        foreach (var s in sliders)
            _items.Add(new SliderItem { Label = s.Label, Value = s.Initial, Min = s.Min, Max = s.Max });
        ItemsHost.ItemsSource = _items;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Result = _items.Select(i => i.Value).ToArray();
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
