using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.Mana;

/// <summary>
/// CR 500.4 exception + CR 106.1b colorless-restricted mana: a unit of mana
/// produced by a triggered ability can carry a "doesn't empty as steps and
/// phases end" rider (Karn, Legacy Reforged) AND a spend-restriction on
/// colorless ({C}) mana. These tests exercise the
/// <see cref="ManaProvenanceSlot.DoesNotEmpty"/> flag + the colorless
/// provenance-slot recording + the <see cref="Player.EmptyManaPool(bool)"/>
/// step/phase vs end-of-turn split, independent of any specific card.
/// </summary>
public class NonEmptyingRestrictedManaTests
{
    private static readonly SpendRestriction ArtifactSpellsOnly =
        new("artifact spell",
            spell => spell.Card.HasType(Majik.Core.Cards.Types.CardType.Artifact));

    [Fact]
    public void AddManaToPool_Colorless_WithRestriction_RecordsColorlessSlots()
    {
        // CR 106.1b — colorless {C} mana lives in the Generic bucket but,
        // when produced with a rider, records ManaColor.Colorless slots so the
        // gate + empty sweep can find them.
        var alice = new Player("Alice", 20);
        var source = new object();

        alice.AddManaToPool(
            ManaCost.Parse("CCC"), // {C}{C}{C} — three colorless units
            provenanceSource: source,
            restriction: ArtifactSpellsOnly,
            doesNotEmpty: true);

        alice.ManaPool.Generic.Should().Be(3, "colorless mana counts toward the Generic bucket");
        alice.ManaPool.Colorless.Should().Be(3, "and is tagged as the colorless type");
        alice.ManaProvenance.Should().HaveCount(3);
        alice.ManaProvenance.Should().OnlyContain(s =>
            s.Color == ManaColor.Colorless
            && s.DoesNotEmpty
            && s.Restriction == ArtifactSpellsOnly);
    }

    [Fact]
    public void EmptyManaPool_StepBoundary_RetainsDoesNotEmptyMana()
    {
        // CR 500.4 exception — "you don't lose this mana as steps and phases
        // end." A step/phase-boundary empty (endOfTurn: false) keeps the
        // flagged units (and their provenance slots) floating.
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(
            ManaCost.Parse("CC"),
            provenanceSource: new object(),
            restriction: ArtifactSpellsOnly,
            doesNotEmpty: true);

        alice.EmptyManaPool(endOfTurn: false);

        alice.ManaPool.Generic.Should().Be(2, "doesn't-empty mana survives a step boundary");
        alice.ManaPool.Colorless.Should().Be(2, "the colorless tag survives too");
        alice.ManaProvenance.Should().HaveCount(2);
    }

    [Fact]
    public void EmptyManaPool_StepBoundary_StillEmptiesPlainMana()
    {
        // A step/phase boundary empties ordinary mana even while protected
        // mana floats alongside it.
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(
            ManaCost.Parse("C"),
            provenanceSource: new object(),
            restriction: ArtifactSpellsOnly,
            doesNotEmpty: true);
        alice.AddManaToPool(ManaCost.Parse("RR")); // plain, no rider

        alice.EmptyManaPool(endOfTurn: false);

        alice.ManaPool.Red.Should().Be(0, "plain red empties at a step boundary");
        alice.ManaPool.Generic.Should().Be(1, "protected colorless survives");
        alice.ManaPool.Colorless.Should().Be(1);
        alice.ManaProvenance.Should().HaveCount(1);
    }

    [Fact]
    public void EmptyManaPool_EndOfTurn_ClearsEverythingIncludingProtected()
    {
        // CR 514.2 — the "until end of turn" rider lapses; the end-of-turn
        // empty (default) clears even doesn't-empty mana.
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(
            ManaCost.Parse("2"),
            provenanceSource: new object(),
            restriction: ArtifactSpellsOnly,
            doesNotEmpty: true);

        alice.EmptyManaPool(); // default endOfTurn: true

        alice.ManaPool.IsEmpty.Should().BeTrue("end of turn clears protected mana too");
        alice.ManaProvenance.Should().BeEmpty();
    }

    [Fact]
    public void EmptyManaPool_NoProtectedMana_FullEmptyBackwardCompatible()
    {
        // Legacy behaviour preserved: with no protected mana floating, a
        // step-boundary empty is a full empty (the fast path).
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("RRG"));

        alice.EmptyManaPool(endOfTurn: false);

        alice.ManaPool.IsEmpty.Should().BeTrue();
        alice.ManaProvenance.Should().BeEmpty();
    }
}
