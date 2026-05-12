using System.Runtime.InteropServices;
using SkiaSharp;
using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.App.Services;

/// <summary>
/// Loads marker icons from the SupCom SCD archives via <see cref="GameDataService"/>, with a
/// fallback to null when nothing matches (callers then fall back to vector shapes).
///
/// SupCom hides marker / strategic icons in a handful of fairly stable paths. For each
/// <see cref="MarkerType"/> we try several candidates in order; the first one that decodes wins.
/// All attempts are written to the debug log so we can tighten paths from real installs.
/// </summary>
public class MarkerIconService
{
    private readonly GameDataService _gameData;
    private readonly Dictionary<MarkerType, SKBitmap?> _cache = new();
    private bool _loggedManifest;

    public MarkerIconService(GameDataService gameData)
    {
        _gameData = gameData;
    }

    private static readonly Dictionary<MarkerType, string[]> Candidates = new()
    {
        // Mass deposits stay as a plain green dot drawn by SkiaMapControl — user preference.
        // (No entry here so Get(Mass) returns null and the vector fallback kicks in.)
        [MarkerType.Hydrocarbon] = new[]
        {
            "/textures/ui/common/game/strategicicons/icon_structure_energy.dds",
            "/textures/ui/common/game/strategicicons/icon_structure_energy1_rest.dds",
            "/textures/ui/common/maps/marker_hydrocarbon.dds",
            "/textures/marker_hydrocarbon.dds",
        },
        [MarkerType.BlankMarker] = new[]
        {
            "/textures/ui/common/game/strategicicons/icon_commander_generic.dds",
            "/textures/ui/common/game/strategicicons/icon_commander.dds",
            "/textures/ui/common/maps/marker_spawn.dds",
        },
        [MarkerType.ExpansionArea] = new[]
        {
            "/textures/ui/common/maps/marker_expansion.dds",
            "/textures/ui/common/game/strategicicons/icon_structure_generic.dds",
        },
        [MarkerType.LargeExpansionArea] = new[]
        {
            "/textures/ui/common/maps/marker_large_expansion.dds",
            "/textures/ui/common/game/strategicicons/icon_structure_generic.dds",
        },
        [MarkerType.NavalArea] = new[]
        {
            "/textures/ui/common/maps/marker_naval.dds",
            "/textures/ui/common/game/strategicicons/icon_seaplant_generic.dds",
        },
        [MarkerType.DefensePoint] = new[]
        {
            "/textures/ui/common/maps/marker_defense.dds",
            "/textures/ui/common/game/strategicicons/icon_structure_directfire.dds",
        },
        [MarkerType.RallyPoint] = new[]
        {
            "/textures/ui/common/maps/marker_rally.dds",
            "/textures/ui/common/game/strategicicons/icon_factory_generic.dds",
        },
        [MarkerType.NavalRallyPoint] = new[]
        {
            "/textures/ui/common/maps/marker_naval_rally.dds",
        },
        [MarkerType.LandPathNode] = new[]
        {
            "/textures/ui/common/maps/marker_path_land.dds",
        },
        [MarkerType.WaterPathNode] = new[]
        {
            "/textures/ui/common/maps/marker_path_water.dds",
        },
        [MarkerType.AirPathNode] = new[]
        {
            "/textures/ui/common/maps/marker_path_air.dds",
        },
        [MarkerType.AmphibiousPathNode] = new[]
        {
            "/textures/ui/common/maps/marker_path_amph.dds",
        },
        [MarkerType.CombatZone] = new[]
        {
            "/textures/ui/common/maps/marker_combat.dds",
        },
    };

    /// <summary>Return the cached SKBitmap for a type, loading on demand. Null if not available.</summary>
    public SKBitmap? Get(MarkerType type)
    {
        if (_cache.TryGetValue(type, out var bm)) return bm;
        if (_gameData == null || !_gameData.IsInitialized) { _cache[type] = null; return null; }
        if (!Candidates.TryGetValue(type, out var paths)) { _cache[type] = null; return null; }

        LogManifestOnce();

        foreach (var path in paths)
        {
            try
            {
                var tex = _gameData.LoadTextureDds(path);
                if (tex == null) continue;
                var skb = new SKBitmap(tex.Value.Width, tex.Value.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                Marshal.Copy(tex.Value.Pixels, 0, skb.GetPixels(), tex.Value.Pixels.Length);
                DebugLog.Write($"[MarkerIcon] {type}: loaded {path} ({tex.Value.Width}x{tex.Value.Height})");
                _cache[type] = skb;
                return skb;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[MarkerIcon] {type}: {path} → {ex.Message}");
            }
        }

        DebugLog.Write($"[MarkerIcon] {type}: no candidate matched, falling back to vector");
        _cache[type] = null;
        return null;
    }

    /// <summary>Drop any cached icons so a new game install path takes effect next render.</summary>
    public void Invalidate()
    {
        foreach (var b in _cache.Values) b?.Dispose();
        _cache.Clear();
        _loggedManifest = false;
    }

    private void LogManifestOnce()
    {
        if (_loggedManifest) return;
        _loggedManifest = true;
        try
        {
            // Dump a sample of marker/icon DDS paths from the SCD archives so we can spot the
            // real names if our candidate list misses.
            DebugLog.Write("[MarkerIcon] Searching for marker-like assets in SCD archives...");
            int n = 0;
            foreach (var p in _gameData.FindFiles("marker", ".dds"))
            {
                DebugLog.Write($"  marker DDS: {p}");
                if (++n >= 40) break;
            }
            n = 0;
            foreach (var p in _gameData.FindFiles("strategicicons", ".dds"))
            {
                DebugLog.Write($"  strategic DDS: {p}");
                if (++n >= 60) break;
            }
        }
        catch { /* non-fatal */ }
    }
}
