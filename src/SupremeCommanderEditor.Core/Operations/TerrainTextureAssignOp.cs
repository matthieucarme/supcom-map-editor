using SupremeCommanderEditor.Core.Models;

namespace SupremeCommanderEditor.Core.Operations;

/// <summary>
/// Undoable record of a strata texture (re)assignment: snapshots the four fields touched
/// (<see cref="TerrainTexture.AlbedoPath"/>, <see cref="TerrainTexture.NormalPath"/>, and the
/// two scales) before and after so Ctrl+Z restores them exactly.
/// </summary>
public class TerrainTextureAssignOp : IMapOperation
{
    private readonly TerrainTexture _slot;

    private readonly string _beforeAlbedo;
    private readonly string _beforeNormal;
    private readonly float _beforeAlbedoScale;
    private readonly float _beforeNormalScale;

    private readonly string _afterAlbedo;
    private readonly string _afterNormal;
    private readonly float _afterAlbedoScale;
    private readonly float _afterNormalScale;

    public string Description { get; }

    public TerrainTextureAssignOp(TerrainTexture slot,
        string beforeAlbedo, string beforeNormal, float beforeAlbedoScale, float beforeNormalScale,
        string afterAlbedo, string afterNormal, float afterAlbedoScale, float afterNormalScale,
        string description)
    {
        _slot = slot;
        _beforeAlbedo = beforeAlbedo;
        _beforeNormal = beforeNormal;
        _beforeAlbedoScale = beforeAlbedoScale;
        _beforeNormalScale = beforeNormalScale;
        _afterAlbedo = afterAlbedo;
        _afterNormal = afterNormal;
        _afterAlbedoScale = afterAlbedoScale;
        _afterNormalScale = afterNormalScale;
        Description = description;
    }

    public void Execute()
    {
        _slot.AlbedoPath = _afterAlbedo;
        _slot.NormalPath = _afterNormal;
        _slot.AlbedoScale = _afterAlbedoScale;
        _slot.NormalScale = _afterNormalScale;
    }

    public void Undo()
    {
        _slot.AlbedoPath = _beforeAlbedo;
        _slot.NormalPath = _beforeNormal;
        _slot.AlbedoScale = _beforeAlbedoScale;
        _slot.NormalScale = _beforeNormalScale;
    }
}
