using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="CorruptedConvictionFactory"/> — Instant {B}.
///
/// "As an additional cost to cast this spell, sacrifice a creature.
///  Draw two cards."
///
/// Covers ONLY the card's unique behaviour (additional sac-a-creature cost +
/// draw-two resolve) plus a single identity assert for the exact mana cost.
/// Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests.
/// </summary>
[Trait("Color", "B")]
public class CorruptedConvictionTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Permanent card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity — exact mana cost (non-vanilla-stat assert per task spec)
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_InstantManaCostB()
    {
        var card = CorruptedConvictionFactory.Create(_alice);

        card.Name.Should().Be("Corrupted Conviction");
        card.ManaCost.Should().Be("{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape — sac a creature, no targets/modes/X
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSacrificeACreatureCost_NoTargets()
    {
        var def = CorruptedConvictionFactory.BuildSpellDefinition(_alice);

        def.AdditionalCostsOrEmpty.Should().ContainSingle()
            .Which.Should().BeOfType<SacrificeACreatureAdditionalCost>(
                "Corrupted Conviction prints 'As an additional cost to cast this spell, sacrifice a creature.' (CR 601.2f)");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty("Corrupted Conviction has no targets");
    }

    // -----------------------------------------------------------------------
    // Resolve — draw two cards (no token)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsTwoCards_NoToken()
    {
        var top1 = SeedLibraryCard(_alice, "Top1");
        var top2 = SeedLibraryCard(_alice, "Top2");
        SeedLibraryCard(_alice, "Top3");

        foreach (var e in CorruptedConvictionFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(2)
            .And.Contain(new[] { top1, top2 });
        _alice.Zones.Library.GetCards().Should().ContainSingle();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Corrupted Conviction creates no token — it only draws two cards");
    }

    [Fact]
    public void Resolve_EmptyLibraryMidDraw_FlagsSbaLoss()
    {
        var only = SeedLibraryCard(_alice, "Only");

        foreach (var e in CorruptedConvictionFactory.BuildResolveEffect(_alice)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "second draw hit an empty library — SBA flag must be set (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // Cost: sacrifices a creature; unpayable with no creature (CR 601.2f)
    // -----------------------------------------------------------------------

    [Fact]
    public void Cost_SacrificesCreature_WhenAvailable()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        PutOnBattlefield(_alice, bear);

        var cost = new SacrificeACreatureAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Sacrificed.Should().BeSameAs(bear);
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Cost_CanPay_FalseWhenNoCreature()
    {
        // A land on the battlefield is not a creature, so the sacrifice
        // additional cost is unpayable (CR 601.2f).
        var land = new Land("Swamp");
        PutOnBattlefield(_alice, land);

        var cost = new SacrificeACreatureAdditionalCost();
        cost.CanPay(_alice).Should().BeFalse(
            "no creature is controlled, so 'sacrifice a creature' can't be paid");
    }
}
