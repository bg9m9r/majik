using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ViashinoPyromancerFactory"/>.
///
/// Viashino Pyromancer (Dominaria United reprint, {1}{R}):
///   Creature — Lizard Wizard 2/1.
///   When this creature enters, it deals 2 damage to target player or
///   planeswalker.
///
/// Covers:
///   - Identity (Lizard Wizard 2/1, {1}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - ETB triggered-ability shape: one 1..1 "target player or
///     planeswalker" request.
///   - Resolution: 2 damage to a player target; 2 damage to a planeswalker
///     target routes through loyalty removal (CR 306.8); a non-player /
///     non-planeswalker target no-ops (CR 608.2b).
/// </summary>
public class ViashinoPyromancerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ViashinoPyromancer_Identity()
    {
        var vp = ViashinoPyromancerFactory.Create(_alice);

        vp.Name.Should().Be("Viashino Pyromancer");
        vp.ManaCost.Should().Be("{1}{R}");
        vp.HasType(CardType.Creature).Should().BeTrue();
        vp.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
        vp.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        vp.BasePower.Should().Be(2);
        vp.BaseToughness.Should().Be(1);
        vp.Owner.Should().BeSameAs(_alice);
        vp.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ViashinoPyromancer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Viashino Pyromancer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Viashino Pyromancer");
        card.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB triggered-ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ViashinoPyromancer_HasEtbTrigger_OnePlayerOrPlaneswalkerTarget()
    {
        var vp = ViashinoPyromancerFactory.Create(_alice);

        var trigger = vp.Abilities.OfType<TriggeredAbility>().Single();

        trigger.TargetRequests.Should().HaveCount(1);
        trigger.TargetRequests[0].MinTargets.Should().Be(1);
        trigger.TargetRequests[0].MaxTargets.Should().Be(1);
        trigger.TargetRequests[0].Description.Should()
            .Contain("player or planeswalker");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_DealsTwoToPlayerTarget()
    {
        var vp = ViashinoPyromancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vp);
        vp.SetZone(ZoneType.Battlefield);

        var trigger = vp.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        trigger.Resolve();

        _bob.LifeTotal.Should().Be(18, "2 damage to Bob");
        _bob.LifeLostThisTurn.Should().Be(2);
    }

    [Fact]
    public void Etb_DealsTwoToPlaneswalkerTarget_RoutesToLoyaltyRemoval()
    {
        // CR 306.8 — damage to a planeswalker removes that many loyalty counters.
        var pw = new Planeswalker("Test Walker", "{3}", startingLoyalty: 5,
            subtypes: new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var vp = ViashinoPyromancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vp);
        vp.SetZone(ZoneType.Battlefield);

        var trigger = vp.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { pw },
        });

        trigger.Resolve();

        pw.Loyalty.Should().Be(3, "2 loyalty counters removed (5 - 2)");
    }

    [Fact]
    public void Etb_NoChosenTarget_NoOps()
    {
        var vp = ViashinoPyromancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vp);
        vp.SetZone(ZoneType.Battlefield);

        var trigger = vp.Abilities.OfType<TriggeredAbility>().Single();

        // No targets set — resolution is a clean no-op (CR 608.2b).
        trigger.Resolve();

        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Etb_CreatureTarget_NoOps()
    {
        // CR 608.2b — a creature is not a legal "player or planeswalker"
        // target; if one is somehow resolved (redirect), the effect no-ops.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        bears.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var vp = ViashinoPyromancerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(vp);
        vp.SetZone(ZoneType.Battlefield);

        var trigger = vp.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bears },
        });

        trigger.Resolve();

        bears.Damage.Should().Be(0, "a creature is not a legal target — no damage");
    }
}
