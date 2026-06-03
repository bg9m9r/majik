using FluentAssertions;
using Majik.Core.Players;
using Majik.Core.Rules;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for the CR 605/616 Ethersworn-Canonist nonartifact-spell rail on
/// <see cref="CastingRestrictions"/>: the always-on per-player nonartifact-cast
/// counter plus the battlefield-gated symmetric active flag, and the combined
/// <see cref="CastingRestrictions.IsRestrictedByCanonistNonartifact"/> gate.
/// </summary>
public class CanonistNonartifactRestrictionRailTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CanonistNonartifactRestrictionRailTests() => CastingRestrictions.Clear();

    public void Dispose() => CastingRestrictions.Clear();

    [Fact]
    public void Counter_RecordsAndReports_PerPlayer()
    {
        CastingRestrictions.HasCastNonartifactSpellThisTurn(_alice).Should().BeFalse();

        CastingRestrictions.RecordNonartifactSpellCast(_alice);

        CastingRestrictions.HasCastNonartifactSpellThisTurn(_alice).Should().BeTrue();
        CastingRestrictions.HasCastNonartifactSpellThisTurn(_bob).Should().BeFalse(
            "the nonartifact-cast counter is per-player");
    }

    [Fact]
    public void Counter_Clears()
    {
        CastingRestrictions.RecordNonartifactSpellCast(_alice);
        CastingRestrictions.ClearNonartifactSpellsCastThisTurn();
        CastingRestrictions.HasCastNonartifactSpellThisTurn(_alice).Should().BeFalse(
            "the per-turn tally refreshes (CR 514.2)");
    }

    [Fact]
    public void ActiveFlag_AloneDoesNotRestrict_UntilANonartifactCast()
    {
        var token = new object();
        CastingRestrictions.AddCanonistNonartifactRestriction(token, _alice);

        CastingRestrictions.HasCanonistNonartifactRestriction(_alice).Should().BeTrue();
        CastingRestrictions.IsRestrictedByCanonistNonartifact(_alice).Should().BeFalse(
            "a registered Canonist alone does not block — the player must have "
            + "already cast a nonartifact spell this turn (CR 605/616)");

        CastingRestrictions.RecordNonartifactSpellCast(_alice);

        CastingRestrictions.IsRestrictedByCanonistNonartifact(_alice).Should().BeTrue(
            "after casting a nonartifact spell, additional nonartifact spells are blocked");
    }

    [Fact]
    public void CounterAlone_DoesNotRestrict_WithoutAnActiveCanonist()
    {
        CastingRestrictions.RecordNonartifactSpellCast(_alice);
        CastingRestrictions.IsRestrictedByCanonistNonartifact(_alice).Should().BeFalse(
            "without a Canonist on the battlefield the nonartifact counter is inert");
    }

    [Fact]
    public void Restriction_IsPerPlayer_NotSymmetricInState()
    {
        var token = new object();
        CastingRestrictions.AddCanonistNonartifactRestriction(token, _alice);
        CastingRestrictions.AddCanonistNonartifactRestriction(token, _bob);

        CastingRestrictions.RecordNonartifactSpellCast(_alice);

        CastingRestrictions.IsRestrictedByCanonistNonartifact(_alice).Should().BeTrue();
        CastingRestrictions.IsRestrictedByCanonistNonartifact(_bob).Should().BeFalse(
            "Bob is registered but hasn't cast a nonartifact spell yet — he is unrestricted");
    }

    [Fact]
    public void Remove_LiftsRestriction_ScopedByToken()
    {
        var tokenA = new object();
        var tokenB = new object();
        CastingRestrictions.AddCanonistNonartifactRestriction(tokenA, _alice);
        CastingRestrictions.AddCanonistNonartifactRestriction(tokenB, _alice);
        CastingRestrictions.RecordNonartifactSpellCast(_alice);

        CastingRestrictions.RemoveCanonistNonartifactRestriction(tokenA);
        CastingRestrictions.IsRestrictedByCanonistNonartifact(_alice).Should().BeTrue(
            "a second Canonist (tokenB) still keeps the restriction active");

        CastingRestrictions.RemoveCanonistNonartifactRestriction(tokenB);
        CastingRestrictions.IsRestrictedByCanonistNonartifact(_alice).Should().BeFalse(
            "with every Canonist gone the restriction lifts");
    }

    [Fact]
    public void AddCanonist_IsIdempotent_PerTokenPlayer()
    {
        var token = new object();
        CastingRestrictions.AddCanonistNonartifactRestriction(token, _alice);
        CastingRestrictions.AddCanonistNonartifactRestriction(token, _alice);

        // Removing the single token clears it regardless of duplicate adds.
        CastingRestrictions.RemoveCanonistNonartifactRestriction(token);
        CastingRestrictions.HasCanonistNonartifactRestriction(_alice).Should().BeFalse();
    }
}
