using SupremeCommanderEditor.Core.Services;
using Xunit;

namespace SupremeCommanderEditor.Core.Tests.Services;

/// <summary>
/// Pure-logic coverage for <see cref="UpdateCheckService.IsNewer"/>. The network path (CheckAsync)
/// is intentionally not unit-tested — it would require mocking GitHub or hitting the live API; the
/// failure paths there all collapse to "no update" by design.
/// </summary>
public class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v1.2.0", "1.1.0", true)]   // tag has leading 'v', current doesn't
    [InlineData("1.2.0",  "v1.1.0", true)]  // and the reverse
    [InlineData("v1.1.0", "1.1.0", false)]  // same version → no update
    [InlineData("v1.1.0", "1.2.0", false)]  // running ahead of latest (dev build)
    [InlineData("v2.0.0", "1.9.9", true)]   // major bump
    [InlineData("v1.10.0", "1.9.0", true)]  // numeric compare, not lexicographic
    public void IsNewer_RespectsSemverOrdering(string latest, string current, bool expected)
    {
        Assert.Equal(expected, UpdateCheckService.IsNewer(latest, current));
    }

    [Fact]
    public void IsNewer_HandlesPreReleaseSuffixOnLatestTag()
    {
        // GitHub Releases can ship tags like "v1.2.0-rc1" — we strip the suffix before comparing
        // so a regular release stays comparable. The pre-release flag itself is checked separately
        // in CheckAsync (we don't prompt users to upgrade to pre-releases).
        Assert.True(UpdateCheckService.IsNewer("v1.2.0-rc1", "1.1.0"));
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("not-a-version", "1.0.0")]
    [InlineData("v1.0.0", "")]
    [InlineData("v1.0.0", "garbage")]
    public void IsNewer_GarbageInputReturnsFalse(string latest, string current)
    {
        // Defensive: any parse failure must collapse to "no update" so we never prompt the user
        // based on noise.
        Assert.False(UpdateCheckService.IsNewer(latest, current));
    }
}
