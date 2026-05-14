using SupremeCommanderEditor.Core.Services;
using Xunit;

namespace SupremeCommanderEditor.Core.Tests.Services;

/// <summary>
/// Pure-math regression tests for <see cref="SymmetryService.SourceOf"/> and <see cref="SymmetryService.DestOf"/>.
/// Mirror mode coverage is mostly sanity-check (the existing UI has been used long enough that
/// any obvious break would have been reported); the new Rotational mode is exercised more thoroughly
/// since it's brand new and has tricky 90°-chain bookkeeping.
/// </summary>
public class SymmetryServiceTests
{
    // ===== Rotational, 2-region patterns =====
    // 180° rotation around the center: (u, v) → (1-u, 1-v). Same formula regardless of dividing axis.

    [Theory]
    [InlineData(SymmetryPattern.Vertical,     0.8f, 0.4f)]  // u>0.5 → R1 (right half)
    [InlineData(SymmetryPattern.Horizontal,   0.4f, 0.8f)]  // v>0.5 → R1 (bottom)
    [InlineData(SymmetryPattern.DiagonalTLBR, 0.3f, 0.7f)]  // v>u   → R1 (bottom-left triangle)
    [InlineData(SymmetryPattern.DiagonalTRBL, 0.7f, 0.7f)]  // u+v>1 → R1 (bottom-right triangle)
    public void Rotational2Region_BackwardMap_Is180DegreesAroundCenter(SymmetryPattern p, float u, float v)
    {
        // For any 2-region pattern, asking SourceOf with a point in R1 (destination) and source=R0
        // must return the 180°-rotated image regardless of the dividing axis.
        var (su, sv) = SymmetryService.SourceOf(p, SymmetryRegion.R0, u, v, SymmetryMode.Rotational);
        Assert.Equal(1f - u, su, 5);
        Assert.Equal(1f - v, sv, 5);
    }

    [Theory]
    [InlineData(SymmetryPattern.Vertical)]
    [InlineData(SymmetryPattern.Horizontal)]
    public void Rotational2Region_PointInSourceRegion_IsIdentity(SymmetryPattern p)
    {
        // Source = R0 (the half we keep). Points inside R0 must map to themselves.
        // For Vertical R0 = left half (u < 0.5); for Horizontal R0 = top (v < 0.5).
        var (u, v) = SymmetryService.SourceOf(p, SymmetryRegion.R0, 0.2f, 0.3f, SymmetryMode.Rotational);
        Assert.Equal(0.2f, u, 5);
        Assert.Equal(0.3f, v, 5);
    }

    [Fact]
    public void Rotational2Region_DestOf_IsInvolutive()
    {
        // Forward map = backward map = 180° rotation. Applying twice returns the original.
        var (u1, v1) = SymmetryService.DestOf(
            SymmetryPattern.Vertical, SymmetryRegion.R0, SymmetryRegion.R1,
            0.2f, 0.3f, SymmetryMode.Rotational);
        var (u2, v2) = SymmetryService.DestOf(
            SymmetryPattern.Vertical, SymmetryRegion.R1, SymmetryRegion.R0,
            u1, v1, SymmetryMode.Rotational);
        Assert.Equal(0.2f, u2, 5);
        Assert.Equal(0.3f, v2, 5);
    }

    // ===== Rotational, 4-region patterns =====
    // 90° CW rotation: (u, v) → (1 - v, u). Chain: source → +1step → +2step → +3step → source.
    // For QuadCross the chain order is TL → TR → BR → BL (not the enum order TL,TR,BL,BR).

    [Fact]
    public void RotationalQuadCross_OneStepCW_TLtoTR()
    {
        // A seed at (0.1, 0.1) in TL, rotated 90° CW, should land near (0.9, 0.1) in TR.
        var (u, v) = SymmetryService.DestOf(
            SymmetryPattern.QuadCross, SymmetryRegion.R0, SymmetryRegion.R1,  // TL → TR
            0.1f, 0.1f, SymmetryMode.Rotational);
        Assert.Equal(0.9f, u, 5);
        Assert.Equal(0.1f, v, 5);
    }

    [Fact]
    public void RotationalQuadCross_TwoStepsCW_TLtoBR()
    {
        // Two CW steps = 180°. (0.1, 0.1) → (0.9, 0.9).
        var (u, v) = SymmetryService.DestOf(
            SymmetryPattern.QuadCross, SymmetryRegion.R0, SymmetryRegion.R3,  // TL → BR (enum order)
            0.1f, 0.1f, SymmetryMode.Rotational);
        Assert.Equal(0.9f, u, 5);
        Assert.Equal(0.9f, v, 5);
    }

    [Fact]
    public void RotationalQuadCross_ThreeStepsCW_TLtoBL()
    {
        // Three CW steps = 270° = 90° CCW. (0.1, 0.1) → (0.1, 0.9).
        var (u, v) = SymmetryService.DestOf(
            SymmetryPattern.QuadCross, SymmetryRegion.R0, SymmetryRegion.R2,  // TL → BL (enum order)
            0.1f, 0.1f, SymmetryMode.Rotational);
        Assert.Equal(0.1f, u, 5);
        Assert.Equal(0.9f, v, 5);
    }

    [Fact]
    public void RotationalQuadCross_FullChain_ReturnsToStart()
    {
        // TL → TR → BR → BL → TL should bring a seed back to its original position.
        var p = SymmetryPattern.QuadCross;
        var (u, v) = (0.1f, 0.2f);
        (u, v) = SymmetryService.DestOf(p, SymmetryRegion.R0, SymmetryRegion.R1, u, v, SymmetryMode.Rotational);
        (u, v) = SymmetryService.DestOf(p, SymmetryRegion.R1, SymmetryRegion.R3, u, v, SymmetryMode.Rotational);
        (u, v) = SymmetryService.DestOf(p, SymmetryRegion.R3, SymmetryRegion.R2, u, v, SymmetryMode.Rotational);
        (u, v) = SymmetryService.DestOf(p, SymmetryRegion.R2, SymmetryRegion.R0, u, v, SymmetryMode.Rotational);
        Assert.Equal(0.1f, u, 5);
        Assert.Equal(0.2f, v, 5);
    }

    [Fact]
    public void RotationalQuadDiagonals_NToE_Is90CW()
    {
        // QuadDiagonals enum is already in CW order (N=R0, E=R1). One step CW from N to E.
        // A point near the top edge (slightly off-center to land cleanly in the N triangle) rotates
        // 90° CW around the center.
        var (u, v) = SymmetryService.DestOf(
            SymmetryPattern.QuadDiagonals, SymmetryRegion.R0, SymmetryRegion.R1,
            0.5f, 0.1f, SymmetryMode.Rotational);
        // 90° CW around (0.5, 0.5): (0.5, 0.1) → (1 - 0.1, 0.5) = (0.9, 0.5)
        Assert.Equal(0.9f, u, 5);
        Assert.Equal(0.5f, v, 5);
    }

    // ===== SourceOf and DestOf consistency =====

    [Theory]
    [InlineData(SymmetryMode.Mirror)]
    [InlineData(SymmetryMode.Rotational)]
    public void DestOf_ThenSourceOf_RoundTrips(SymmetryMode mode)
    {
        // For any (pattern, source, dest), forward then backward must return the original point.
        // Critical contract — without it, mirroring then un-mirroring would drift.
        var p = SymmetryPattern.QuadCross;
        var (u, v) = (0.15f, 0.25f);
        var (du, dv) = SymmetryService.DestOf(p, SymmetryRegion.R0, SymmetryRegion.R1, u, v, mode);
        var (su, sv) = SymmetryService.SourceOf(p, SymmetryRegion.R0, du, dv, mode);
        Assert.Equal(u, su, 5);
        Assert.Equal(v, sv, 5);
    }

    // ===== Sanity: Mirror mode still works on a representative case =====

    [Fact]
    public void MirrorVertical_StillMirrorsAcrossVerticalAxis()
    {
        // Source = R0 (left half). For a query point at (0.8, 0.4) in R1 (right), the source pixel
        // is at (0.2, 0.4) — same y, mirrored x.
        var (u, v) = SymmetryService.SourceOf(SymmetryPattern.Vertical, SymmetryRegion.R0, 0.8f, 0.4f);
        Assert.Equal(0.2f, u, 5);
        Assert.Equal(0.4f, v, 5);
    }
}
