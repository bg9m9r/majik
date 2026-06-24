using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PearlOfWisdomFactory"/>.
///
/// Card: Pearl of Wisdom — Sorcery {2}{U} (Bloomburrow).
///   "This spell costs {1} less to cast if you control an Otter.
///    Draw two cards."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (name, Sorcery type, mana cost {2}{U}).
///   - Otter-conditional cost reduction (CR 117.7a) — generic reduced by {1}
///     iff the caster controls an Otter; floor / {U} pip preserved (CR 117.7c);
///     non-Otter permanents do not trigger it.
///   - Resolve effect draws two cards from top of library.
///   - Empty / one-card library mid-resolve flags the SBA-driven loss.
///
/// (Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests, so no DispatchesViaNamedCardFactory test here.)
/// </summary>
[Trait("Color", "U")]
public class PearlOfWisdomFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static Card SeedLibraryCard(Player owner, string name)
    {
        var c = new Creature(name, "{0}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void PearlOfWisdom_Identity()
    {
        var c = PearlOfWisdomFactory.Create(_alice);

        c.Name.Should().Be("Pearl of Wisdom");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -------------------------------------------------------------------------
    // Conditional cost reduction (CR 117.7a) — "{1} less if you control an Otter"
    // -------------------------------------------------------------------------

    [Fact]
    public void Reduction_NoOtter_FullPrintedCost()
    {
        var pearl = PearlOfWisdomFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(pearl);
        pearl.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(pearl, _alice);

        effective.Generic.Should().Be(2);
        effective.Blue.Should().Be(1, "the {U} pip is unaffected (CR 117.7c — only generic reduces)");
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Reduction_NonOtterPermanent_DoesNotReduce()
    {
        var pearl = PearlOfWisdomFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(pearl);
        pearl.SetZone(ZoneType.Hand);

        // A non-Otter permanent (and a non-Otter creature) must not trigger it.
        PutOnBattlefield(_alice, new Artifact("Some Artifact", "{0}"));
        PutOnBattlefield(_alice, new Creature("Grizzly Bears", "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear }));

        var effective = CostReduction.GetEffectiveCost(pearl, _alice);

        effective.Generic.Should().Be(2, "no Otter is controlled");
        effective.Blue.Should().Be(1);
        effective.TotalValue.Should().Be(3);
    }

    [Fact]
    public void Reduction_ControlsOtter_GenericReducedByOne()
    {
        var pearl = PearlOfWisdomFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(pearl);
        pearl.SetZone(ZoneType.Hand);

        PutOnBattlefield(_alice, new Creature("Bellowing Crier", "{1}{U}", 2, 1, subtypes: new[] { CardSubtype.Otter }));

        var effective = CostReduction.GetEffectiveCost(pearl, _alice);

        effective.Generic.Should().Be(1, "{2} reduced by 1 → {1} (CR 117.7a)");
        effective.Blue.Should().Be(1, "the {U} pip is unaffected (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Reduction_MultipleOtters_StillOnlyOneReduction()
    {
        // "if you control an Otter" is a single boolean condition — controlling
        // several Otters does not stack the reduction (CR 117.7a).
        var pearl = PearlOfWisdomFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(pearl);
        pearl.SetZone(ZoneType.Hand);

        for (var i = 0; i < 3; i++)
        {
            PutOnBattlefield(_alice, new Creature($"Otter {i}", "{1}{U}", 1, 1, subtypes: new[] { CardSubtype.Otter }));
        }

        var effective = CostReduction.GetEffectiveCost(pearl, _alice);

        effective.Generic.Should().Be(1, "the condition is boolean — three Otters reduce by {1}, not {3}");
        effective.Blue.Should().Be(1);
        effective.TotalValue.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Resolve: draw two cards
    // -------------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCardsFromTopOfLibrary()
    {
        var c1 = SeedLibraryCard(_alice, "Top1");
        var c2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3"); // remains in library

        var effects = PearlOfWisdomFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });
        c1.Zone.Should().Be(ZoneType.Hand);
        c2.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().HaveCount(1,
            "exactly two cards were drawn off the top");
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_EmptyLibrary_FlagsSbaLossOnFirstDraw()
    {
        var effects = PearlOfWisdomFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "empty library mid-draw flags the SBA-driven loss (CR 704.5b)");
    }

    [Fact]
    public void Resolve_OneCardLibrary_DrawsOne_FlagsSbaLossOnSecondDraw()
    {
        var only = SeedLibraryCard(_alice, "OnlyCard");

        var effects = PearlOfWisdomFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(only);
        only.Zone.Should().Be(ZoneType.Hand);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw came up empty — SBA flag is set");
    }
}
