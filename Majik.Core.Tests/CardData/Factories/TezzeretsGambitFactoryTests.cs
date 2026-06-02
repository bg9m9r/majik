using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="TezzeretsGambitFactory"/> — Sorcery {3}{U/P}
/// (New Phyrexia).
///
/// Oracle text (verified against Scryfall):
///   "({U/P} can be paid with either {U} or 2 life.)
///    Draw two cards, then proliferate. (Choose any number of permanents
///    and/or players, then give each another counter of each kind already
///    there.)"
///
/// Covers:
///   - Identity (Sorcery, {3}{U/P}, owner / controller) + NamedCardFactory
///     dispatch (built from the embedded JSON definition).
///   - SpellDefinition shape: no modes, no X, no target requests, no
///     additional costs (Phyrexian mana is a cost-payment option, not an
///     additional cost).
///   - Resolve: caster draws two cards (CR 121.1), then proliferate
///     (CR 701.27) adds one more counter of an existing kind to each
///     permanent that already has a counter.
///   - Resolve: empty library mid-draw → draws what's available, SBA loss
///     flag set (CR 704.5b).
/// </summary>
[Trait("Color", "U")]
public class TezzeretsGambitFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = TezzeretsGambitFactory.Create(_alice);

        card.Name.Should().Be("Tezzeret's Gambit");
        card.ManaCost.Should().Be("{3}{U/P}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsSorcery()
    {
        var card = NamedCardFactory.Create("Tezzeret's Gambit", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Tezzeret's Gambit");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_NoModesNoXNoTargetsNoAdditionalCosts()
    {
        var def = TezzeretsGambitFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Tezzeret's Gambit has no targets");
        def.AdditionalCostsOrEmpty.Should().BeEmpty(
            "Phyrexian mana is a cost-payment option (CR 107.4f), not an additional cost");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw two cards, then proliferate
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCards()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3");

        foreach (var e in TezzeretsGambitFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    [Fact]
    public void Resolve_Proliferates_AddsOneCounterToPermanentsWithCounters()
    {
        // Seed the library so the draw step doesn't flag the empty-library loss.
        SeedLibraryCard(_alice, "Top1");
        SeedLibraryCard(_alice, "Top2");

        // One permanent already has a +1/+1 counter (gets proliferated),
        // one has no counters (skipped).
        var counted = new Creature("Walking Ballista", "{0}", 0, 0);
        counted.SetOwner(_alice);
        counted.SetController(_alice);
        counted.Counters.Add(CounterType.PlusOnePlusOne, 2);
        _alice.Zones.Battlefield.AddCard(counted);
        counted.SetZone(ZoneType.Battlefield);

        var uncountered = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        uncountered.SetOwner(_alice);
        uncountered.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(uncountered);
        uncountered.SetZone(ZoneType.Battlefield);

        foreach (var e in TezzeretsGambitFactory.BuildResolveEffect(_alice)) e.Execute();

        counted.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "proliferate adds one more counter of an existing kind (CR 701.27) to every permanent with a counter");
        uncountered.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "permanents with no counters are NOT touched by proliferate");
    }

    [Fact]
    public void Resolve_EmptyLibraryMidDraw_DrawsWhatsAvailable_FlagsSbaLoss()
    {
        var only = SeedLibraryCard(_alice, "Only");

        foreach (var e in TezzeretsGambitFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
