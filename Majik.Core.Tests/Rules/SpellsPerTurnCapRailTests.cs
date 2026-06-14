using FluentAssertions;
using Majik.Core.Players;
using Majik.Core.Rules;
using Xunit;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// Unit tests for the CR 601.3 "each player can't cast more than N spells each
/// turn" rail on <see cref="CastingRestrictions"/> (Eidolon of Rhetoric / Archon
/// of Emeria). The rail is modeled as a TRUE static (CR 611) — a battlefield-
/// gated token-keyed per-player cap entry plus an always-on per-player
/// "spells cast this turn" counter — rather than the consumable, one-shot
/// <see cref="CastingRestrictions.SetMaxAdditionalSpellsThisTurn"/> allowance
/// ledger Irencrag Feat uses.
///
/// This separation pays down the
/// <c>eidolon-archon-shared-cap-turn-start-reseed</c> deferral: the static cap
/// and the Irencrag-Feat extra-cast allowance no longer share a single mutable
/// field, so the static-cap turn-start reseed can no longer clobber (or be
/// clobbered by) the Feat's same-turn allowance.
/// </summary>
public class SpellsPerTurnCapRailTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellsPerTurnCapRailTests() => CastingRestrictions.Clear();

    public void Dispose() => CastingRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Spells-cast-this-turn counter (the looked-back state the cap reads).
    // -----------------------------------------------------------------------

    [Fact]
    public void Counter_RecordsAndReports_PerPlayer()
    {
        CastingRestrictions.SpellsCastThisTurn(_alice).Should().Be(0);

        CastingRestrictions.RecordSpellCast(_alice);
        CastingRestrictions.RecordSpellCast(_alice);

        CastingRestrictions.SpellsCastThisTurn(_alice).Should().Be(2);
        CastingRestrictions.SpellsCastThisTurn(_bob).Should().Be(0,
            "the spells-cast counter is per-player");
    }

    [Fact]
    public void Counter_Clears_AtTurnBoundary()
    {
        CastingRestrictions.RecordSpellCast(_alice);
        CastingRestrictions.ClearSpellsCastThisTurn();
        CastingRestrictions.SpellsCastThisTurn(_alice).Should().Be(0,
            "the per-turn tally refreshes at the CR 514/500 turn boundary");
    }

    // -----------------------------------------------------------------------
    // The static cap (battlefield-gated, token-scoped) combined with the
    // counter via IsAtSpellsPerTurnCap.
    // -----------------------------------------------------------------------

    [Fact]
    public void Cap_AloneDoesNotBlock_UntilCapSpellsCast()
    {
        var token = new object();
        CastingRestrictions.AddSpellsPerTurnCap(token, _alice, 1);

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse(
            "no spells cast yet — under the cap of 1");

        CastingRestrictions.RecordSpellCast(_alice);

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeTrue(
            "one spell cast meets the cap of 1 — a second is blocked (CR 601.3)");
    }

    [Fact]
    public void Cap_TighterEntryWins_WhenMultipleSourcesStack()
    {
        var archon = new object();
        var eidolon = new object();
        CastingRestrictions.AddSpellsPerTurnCap(archon, _alice, 1);
        CastingRestrictions.AddSpellsPerTurnCap(eidolon, _alice, 1);

        CastingRestrictions.RecordSpellCast(_alice);

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeTrue(
            "two cap-of-1 sources both gate at one spell; the tighter cap wins");
    }

    [Fact]
    public void Cap_LiftsForOneSource_WithoutTearingDownAnother()
    {
        var archon = new object();
        var eidolon = new object();
        CastingRestrictions.AddSpellsPerTurnCap(archon, _alice, 1);
        CastingRestrictions.AddSpellsPerTurnCap(eidolon, _alice, 1);
        CastingRestrictions.RecordSpellCast(_alice);

        // Archon leaves the battlefield — only its entry is removed.
        CastingRestrictions.RemoveSpellsPerTurnCap(archon);

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeTrue(
            "Eidolon's cap-of-1 entry still gates after one spell — token-scoped removal");

        CastingRestrictions.RemoveSpellsPerTurnCap(eidolon);
        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse(
            "with no cap entries left, the restriction lifts entirely");
    }

    [Fact]
    public void Cap_IsTrueStatic_NotConsumed_RecomputesFromCounter()
    {
        // A true static (CR 611) recomputes from game state — clearing the
        // per-turn counter (a new turn) immediately lifts the gate without any
        // re-seed of the cap entry itself.
        var token = new object();
        CastingRestrictions.AddSpellsPerTurnCap(token, _alice, 1);
        CastingRestrictions.RecordSpellCast(_alice);
        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeTrue();

        // New turn: only the counter resets — the cap entry persists.
        CastingRestrictions.ClearSpellsCastThisTurn();

        CastingRestrictions.IsAtSpellsPerTurnCap(_alice).Should().BeFalse(
            "the cap reads the (now-zeroed) counter — no entry re-seed needed (CR 611)");
    }

    // -----------------------------------------------------------------------
    // The race the deferral names: the static cap and Irencrag Feat's
    // consumable allowance must use SEPARATE ledgers.
    // -----------------------------------------------------------------------

    [Fact]
    public void StaticCapReseed_DoesNotClobber_IrencragFeatAllowance()
    {
        // Irencrag Feat resolves for Bob (no Archon out): one-more-spell cap.
        CastingRestrictions.SetMaxAdditionalSpellsThisTurn(_bob, 1);

        // Archon's static cap is on Alice and gets reseeded at turn start. Under
        // the OLD shared-ledger design, the static reseed cleared the SAME
        // MaxAdditionalSpells dictionary and wiped Bob's Feat allowance. With the
        // rails separated, the static-cap turn-boundary reset only touches the
        // SpellsCastThisTurn counter — Bob's Feat allowance survives.
        CastingRestrictions.ClearSpellsCastThisTurn();

        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_bob).Should().BeFalse(
            "Irencrag Feat's allowance must survive the static-cap turn-start reseed");
        CastingRestrictions.ConsumeAdditionalSpellAllowance(_bob);
        CastingRestrictions.HasExhaustedAdditionalSpellAllowance(_bob).Should().BeTrue(
            "the Feat allowance still tracks independently on its own ledger");
    }
}
