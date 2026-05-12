using SupremeCommanderEditor.Core.Models;
using SupremeCommanderEditor.Core.Services;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Resize the map to a target heightmap dimension. Undo restores the full pre-state since
/// resampling is lossy (we capture heightmap + splatmaps + aux DDS + markers/decals/props).
/// </summary>
public class MapScaleOp : IMapOperation
{
    private readonly ScMap _map;
    private readonly int _newSize;

    // Pre-state snapshot
    private int _prevW, _prevH;
    private ushort[]? _prevHeightmap;
    private byte[]? _prevMaskLow, _prevMaskHighDds, _prevNormalDds, _prevWaterDds;
    private int _prevMaskLowW, _prevMaskLowH, _prevMaskHighW, _prevMaskHighH;
    private byte[]? _prevWaterFoam, _prevWaterFlat, _prevWaterDepth, _prevTerrainType;
    private List<System.Numerics.Vector3>? _prevMarkerPos;
    private List<(System.Numerics.Vector3 pos, System.Numerics.Vector3 scale)>? _prevDecals;
    private List<System.Numerics.Vector3>? _prevPropPos;
    private int _prevInfoW, _prevInfoH;
    private int _prevNormalW, _prevNormalH;

    public string Description => $"Scale map → {_newSize}×{_newSize}";

    public MapScaleOp(ScMap map, int newSize)
    {
        _map = map;
        _newSize = newSize;
    }

    public void Execute()
    {
        _prevW = _map.Heightmap.Width;
        _prevH = _map.Heightmap.Height;
        _prevHeightmap = (ushort[])_map.Heightmap.Data.Clone();

        _prevMaskLow = (byte[])_map.TextureMaskLow.DdsData.Clone();
        _prevMaskLowW = _map.TextureMaskLow.Width;
        _prevMaskLowH = _map.TextureMaskLow.Height;
        _prevMaskHighDds = (byte[])_map.TextureMaskHigh.DdsData.Clone();
        _prevMaskHighW = _map.TextureMaskHigh.Width;
        _prevMaskHighH = _map.TextureMaskHigh.Height;

        _prevNormalDds = (byte[])_map.NormalMapDds.Clone();
        _prevNormalW = _map.NormalMapWidth;
        _prevNormalH = _map.NormalMapHeight;
        _prevWaterDds = (byte[])_map.WaterMapDds.Clone();
        _prevWaterFoam = (byte[])_map.WaterFoamMask.Clone();
        _prevWaterFlat = (byte[])_map.WaterFlatness.Clone();
        _prevWaterDepth = (byte[])_map.WaterDepthBias.Clone();
        _prevTerrainType = (byte[])_map.TerrainTypeData.Clone();

        _prevMarkerPos = _map.Markers.Select(m => m.Position).ToList();
        _prevDecals = _map.Decals.Select(d => (d.Position, d.Scale)).ToList();
        _prevPropPos = _map.Props.Select(p => p.Position).ToList();

        _prevInfoW = _map.Info.Width;
        _prevInfoH = _map.Info.Height;

        MapScaleService.Scale(_map, _newSize);
    }

    public void Undo()
    {
        if (_prevHeightmap == null) return;
        _map.Heightmap.Width = _prevW;
        _map.Heightmap.Height = _prevH;
        _map.Heightmap.Data = _prevHeightmap;

        _map.TextureMaskLow.Width = _prevMaskLowW;
        _map.TextureMaskLow.Height = _prevMaskLowH;
        _map.TextureMaskLow.DdsData = _prevMaskLow!;
        _map.TextureMaskHigh.Width = _prevMaskHighW;
        _map.TextureMaskHigh.Height = _prevMaskHighH;
        _map.TextureMaskHigh.DdsData = _prevMaskHighDds!;

        _map.NormalMapWidth = _prevNormalW;
        _map.NormalMapHeight = _prevNormalH;
        _map.NormalMapDds = _prevNormalDds!;
        _map.WaterMapDds = _prevWaterDds!;
        _map.WaterFoamMask = _prevWaterFoam!;
        _map.WaterFlatness = _prevWaterFlat!;
        _map.WaterDepthBias = _prevWaterDepth!;
        _map.TerrainTypeData = _prevTerrainType!;

        for (int i = 0; i < _map.Markers.Count && i < _prevMarkerPos!.Count; i++)
            _map.Markers[i].Position = _prevMarkerPos[i];
        for (int i = 0; i < _map.Decals.Count && i < _prevDecals!.Count; i++)
        {
            _map.Decals[i].Position = _prevDecals[i].pos;
            _map.Decals[i].Scale = _prevDecals[i].scale;
        }
        for (int i = 0; i < _map.Props.Count && i < _prevPropPos!.Count; i++)
            _map.Props[i].Position = _prevPropPos[i];

        _map.Info.Width = _prevInfoW;
        _map.Info.Height = _prevInfoH;
    }
}
