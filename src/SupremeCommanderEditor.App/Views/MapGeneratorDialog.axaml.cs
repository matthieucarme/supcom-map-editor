using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SupremeCommanderEditor.App.Services;
using SupremeCommanderEditor.Core.Operations;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.App.Views;

/// <summary>
/// Procedural map generation dialog. Collects user options into a <see cref="MapGenerationOptions"/>
/// and exposes it via <see cref="Result"/>. The actual generation happens in the caller, which
/// also resolves texture paths per biome before invoking <c>MapGenerator.Generate</c>.
/// </summary>
public partial class MapGeneratorDialog : Window
{
    public MapGenerationOptions? Result { get; private set; }
    /// <summary>Biome key picked in the dialog (e.g. "evergreen2"). Used by the caller to
    /// resolve texture paths from the library.</summary>
    public string BiomeKey { get; private set; } = "evergreen2";

    private readonly TextureLibraryService? _library;

    // One pair of spinners per team — rebuilt every time the team count changes (preset click or
    // custom spinner edit). We keep parallel lists so OnGenerate can read them out in order.
    private readonly List<NumericUpDown> _playerBoxes = new();
    private readonly List<NumericUpDown> _massPerPlayerBoxes = new();

    public MapGeneratorDialog()
    {
        InitializeComponent();
        // Initial render — default 4v4.
        Loaded += (_, _) =>
        {
            RebuildTeamsGrid(new List<int> { 4, 4 }, defaultMassPerPlayer: 4);
            RefreshTexturePreview();
        };
    }

    public MapGeneratorDialog(TextureLibraryService library) : this()
    {
        _library = library;
    }

    private void OnRandomizeSeed(object? sender, RoutedEventArgs e)
    {
        // 64-bit random seed: combine two Random.Next() for full long range.
        var rng = new Random();
        long high = rng.Next();
        long low = rng.Next();
        SeedBox.Text = ((high << 32) | (uint)low).ToString();
    }

    private void OnVersusPreset(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        bool isCustom = ReferenceEquals(rb, VCustom);
        CustomTeamCountPanel.IsVisible = isCustom;
        if (isCustom)
        {
            // Honour the current TeamCountBox value : recreate that many 1-player rows.
            int count = (int)(TeamCountBox.Value ?? 2m);
            RebuildTeamsGrid(Enumerable.Repeat(1, count).ToList(), defaultMassPerPlayer: 4);
            return;
        }
        if (rb.Tag is not string s || string.IsNullOrWhiteSpace(s)) return;
        var sizes = s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(p => int.TryParse(p.Trim(), out var n) ? n : 0)
                     .Where(n => n > 0)
                     .ToList();
        if (sizes.Count == 0) return;
        RebuildTeamsGrid(sizes, defaultMassPerPlayer: 4);
    }

    /// <summary>Custom mode : user changed the "Number of teams" spinner. Rebuild the grid with
    /// that many rows, preserving any existing values where they fit.</summary>
    private void OnTeamCountChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        int newCount = (int)(e.NewValue ?? 2m);
        if (newCount < 1) return;
        // Preserve existing values if possible, default new rows to 1 player / 4 mass per player.
        var sizes = new List<int>();
        var massPP = new List<int>();
        for (int i = 0; i < newCount; i++)
        {
            sizes.Add(i < _playerBoxes.Count ? (int)(_playerBoxes[i].Value ?? 1m) : 1);
            massPP.Add(i < _massPerPlayerBoxes.Count ? (int)(_massPerPlayerBoxes[i].Value ?? 4m) : 4);
        }
        RebuildTeamsGrid(sizes, defaultMassPerPlayer: 4, massPerPlayerOverrides: massPP);
    }

    /// <summary>Rebuilds the per-team Grid : one row per team with [Team N:] [players spinner]
    /// [× mass-per-player spinner] [mass/player label]. The lists <see cref="_playerBoxes"/> and
    /// <see cref="_massPerPlayerBoxes"/> are kept in parallel so OnGenerate can read them out.</summary>
    private void RebuildTeamsGrid(List<int> sizes, int defaultMassPerPlayer, List<int>? massPerPlayerOverrides = null)
    {
        TeamsGrid.Children.Clear();
        TeamsGrid.RowDefinitions.Clear();
        TeamsGrid.ColumnDefinitions.Clear();
        _playerBoxes.Clear();
        _massPerPlayerBoxes.Clear();

        // 5 columns : label / players spinner / "× " label / mass spinner / "mass/player" label.
        for (int c = 0; c < 5; c++)
            TeamsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var labelBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        var subBrush   = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99));

        for (int i = 0; i < sizes.Count; i++)
        {
            TeamsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBlock
            {
                Text = $"Team {i + 1}:",
                Foreground = labelBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetRow(label, i); Grid.SetColumn(label, 0);
            TeamsGrid.Children.Add(label);

            var players = new NumericUpDown
            {
                Value = Math.Clamp(sizes[i], 1, 8),
                Minimum = 1, Maximum = 8, Increment = 1, Width = 90,
            };
            Grid.SetRow(players, i); Grid.SetColumn(players, 1);
            TeamsGrid.Children.Add(players);
            _playerBoxes.Add(players);

            var sep = new TextBlock
            {
                Text = "players  ×",
                Foreground = subBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            Grid.SetRow(sep, i); Grid.SetColumn(sep, 2);
            TeamsGrid.Children.Add(sep);

            int massPP = massPerPlayerOverrides != null && i < massPerPlayerOverrides.Count
                ? massPerPlayerOverrides[i] : defaultMassPerPlayer;
            var mass = new NumericUpDown
            {
                Value = Math.Clamp(massPP, 0, 20),
                Minimum = 0, Maximum = 20, Increment = 1, Width = 90,
            };
            Grid.SetRow(mass, i); Grid.SetColumn(mass, 3);
            TeamsGrid.Children.Add(mass);
            _massPerPlayerBoxes.Add(mass);

            var sep2 = new TextBlock
            {
                Text = "mass / player",
                Foreground = subBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetRow(sep2, i); Grid.SetColumn(sep2, 4);
            TeamsGrid.Children.Add(sep2);
        }
    }

    private void OnGenerate(object? sender, RoutedEventArgs e)
    {
        if (!long.TryParse(SeedBox.Text, out long seed))
        {
            // Fallback: hash the typed string into a seed so even free-text inputs work deterministically.
            seed = (SeedBox.Text ?? "").GetHashCode();
        }

        int size = 512;
        if (SizeBox.SelectedItem is ComboBoxItem szItem && szItem.Tag is string szStr
            && int.TryParse(szStr, out int s)) size = s;

        Core.Services.SymmetryPattern? symmetry = null;
        if (SymmetryBox.SelectedItem is ComboBoxItem symItem && symItem.Tag is string symStr
            && !string.IsNullOrEmpty(symStr)
            && Enum.TryParse<Core.Services.SymmetryPattern>(symStr, out var pat))
        {
            symmetry = pat;
        }

        if (BiomeBox.SelectedItem is ComboBoxItem biomeItem && biomeItem.Tag is string biome)
            BiomeKey = biome;

        // Read each team's player count + mass-per-player from the dynamic grid. Total mass per
        // team = players × mass/player.
        var teamSizes = _playerBoxes.Select(b => (int)(b.Value ?? 1m)).ToList();
        if (teamSizes.Count == 0) teamSizes = new() { 4, 4 };
        var massCounts = new List<int>();
        for (int i = 0; i < teamSizes.Count; i++)
        {
            int mpp = i < _massPerPlayerBoxes.Count ? (int)(_massPerPlayerBoxes[i].Value ?? 4m) : 4;
            massCounts.Add(mpp * teamSizes[i]);
        }

        Result = new MapGenerationOptions
        {
            Seed = seed,
            Size = size,
            HasWater = WaterBox.IsChecked == true,
            TeamPlayerCounts = teamSizes,
            TeamMassCounts = massCounts,
            Symmetry = symmetry,
            MapName = $"Generated {DateTime.Now:yyyy-MM-dd HH-mm}",
        };
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void OnBiomeChanged(object? sender, SelectionChangedEventArgs e) => RefreshTexturePreview();

    /// <summary>Repopulate the WrapPanel of texture thumbnails based on the currently-selected biome.
    /// Each card shows the resolved texture for one smart category (or "(missing)" if neither the
    /// biome nor any fallback library entry has a texture for it).</summary>
    private void RefreshTexturePreview()
    {
        if (_library == null || TexturePreviews == null) return;
        string biome = "evergreen2";
        if (BiomeBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag) biome = tag;
        var resolved = TextureSetResolver.Resolve(_library, biome);

        TexturePreviews.Children.Clear();
        // Display categories in painting-priority order (Grass first, SeaFloor last).
        SmartBrushTool.TerrainCategory[] order =
        {
            SmartBrushTool.TerrainCategory.Grass,
            SmartBrushTool.TerrainCategory.Rock,
            SmartBrushTool.TerrainCategory.Dirt,
            SmartBrushTool.TerrainCategory.Beach,
            SmartBrushTool.TerrainCategory.Snow,
            SmartBrushTool.TerrainCategory.Plateau,
            SmartBrushTool.TerrainCategory.SeaFloor,
        };
        foreach (var cat in order)
        {
            resolved.TryGetValue(cat, out var entry);
            TexturePreviews.Children.Add(BuildCard(cat, entry));
        }
    }

    private static Control BuildCard(SmartBrushTool.TerrainCategory cat, TextureEntry? entry)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Width = 96, Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var img = new Image { Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Center };
        // Note: Avalonia's default bitmap interpolation in 12+ is acceptable for thumbnails;
        // skip the explicit RenderOptions.SetBitmapInterpolationMode call.
        if (entry != null) img.Source = entry.Thumbnail;
        // No source = empty image; we add the "(missing)" label below.
        panel.Children.Add(img);

        panel.Children.Add(new TextBlock
        {
            Text = cat.ToString(),
            Foreground = Brushes.White,
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = entry?.Name ?? "(missing)",
            Foreground = new SolidColorBrush(entry != null ? Color.FromRgb(170, 170, 170) : Color.FromRgb(180, 100, 100)),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 92,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });
        return panel;
    }
}
