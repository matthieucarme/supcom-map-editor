using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SupremeCommanderEditor.App.Controls;

namespace SupremeCommanderEditor.App.Views;

/// <summary>
/// Color picker built around a custom HSV wheel (avoids the DPI/scaling hit-test issues of
/// Avalonia 12's ColorView). The wheel controls Hue + Saturation; the vertical slider controls
/// Value (brightness). Returns the picked color on OK, null on Cancel.
/// </summary>
public partial class ColorPickerDialog : Window
{
    public Color? PickedColor { get; private set; }

    public ColorPickerDialog()
    {
        InitializeComponent();
        // React to wheel color changes (via the styled-property propagator) to refresh the preview.
        Wheel.PropertyChanged += (_, e) =>
        {
            if (e.Property == ColorWheelControl.SelectedColorProperty) UpdatePreview();
        };
    }

    public ColorPickerDialog(string title, Color initial) : this()
    {
        HeaderText.Text = title;
        var (h, s, v) = ColorWheelControl.RgbToHsv(initial);
        BrightnessSlider.Value = v;
        // Set the wheel's color preserving brightness so the selector dot lands at the right H/S.
        Wheel.SelectedColor = initial;
        UpdatePreview();
    }

    private bool _suppressRecursion;

    private void OnBrightnessChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_suppressRecursion) return;
        // Re-color the wheel's SelectedColor with the new V, preserving H+S.
        var (h, s, _) = ColorWheelControl.RgbToHsv(Wheel.SelectedColor);
        var (r, g, b) = ColorWheelControl.HsvToRgb(h, s, (float)BrightnessSlider.Value);
        _suppressRecursion = true;
        Wheel.SelectedColor = Color.FromRgb(r, g, b);
        _suppressRecursion = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var c = Wheel.SelectedColor;
        PreviewSwatch.Background = new SolidColorBrush(c);
        HexText.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        RgbText.Text = $"R {c.R}   G {c.G}   B {c.B}";
        BrightnessText.Text = BrightnessSlider.Value.ToString("F2");
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        PickedColor = Wheel.SelectedColor;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        PickedColor = null;
        Close();
    }
}
