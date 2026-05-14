using System.Numerics;

namespace SupremeCommanderEditor.Core.Models;

/// <summary>
/// A pre-placed unit on a map — civilian/neutral structures, defenses, props that are actual game
/// entities (shields, turrets, mass extractors set up by mappers, wreckage, etc.). Lives under
/// <c>Armies[name].Units.Units.&lt;Category&gt;.Units</c> in the _save.lua.
/// </summary>
public class UnitSpawn
{
    /// <summary>Local key in the per-category Units table, e.g. "UNIT_42".</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>SC blueprint ID, e.g. "UEB1101" (UEF mass extractor), "XEC0001" (wreck), …</summary>
    public string BlueprintId { get; set; } = string.Empty;
    public Vector3 Position { get; set; }
    /// <summary>Orientation in radians per axis (most maps use a Y rotation, X/Z stay 0).</summary>
    public Vector3 Orientation { get; set; }
    /// <summary>Optional platoon string; rarely used outside campaign maps.</summary>
    public string? Platoon { get; set; }
    /// <summary>Optional orders string; rarely used outside campaign maps.</summary>
    public string? Orders { get; set; }
    /// <summary>Top-level spawn category under <c>army.Units.Units</c>. The SC1 engine treats this
    /// label as load instructions: <c>INITIAL</c> spawns a live, controllable entity; <c>WRECKAGE</c>
    /// spawns a pre-destroyed husk on the ground (the carcass maps that ship vanilla — SCMP_005,
    /// _009, _021, etc.). Free-form names like <c>GROUP_4</c> behave like INITIAL (the engine spawns
    /// them live). We record the original label so a round-trip doesn't silently turn wrecks into
    /// live units. New units placed in the editor default to INITIAL.</summary>
    public string Category { get; set; } = "INITIAL";
}
