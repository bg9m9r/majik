using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Chandra, Torch of Defiance (Kaladesh, {2}{R}{R}).
///
/// Legendary Planeswalker — Chandra, starting loyalty 4. Oracle text
/// (Scryfall, verified):
///   "+1: Exile the top card of your library. You may cast that card. If you
///        don't, Chandra deals 2 damage to each opponent.
///    +1: Add {R}{R}.
///    −3: Chandra deals 4 damage to target creature.
///    −7: You get an emblem with 'Whenever you cast a spell, this emblem deals
///         5 damage to any target.'"
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Chandra, loyalty 4, {2}{R}{R}),
///     materialised from the embedded JSON definition.
///   - Four loyalty abilities: +1 (impulse), +1 (ritual), −3, −7.
///   - +1 impulse: exiles the top card; v1 declines the cast → 2 damage to
///     each opponent.
///   - +1 ritual: adds {R}{R} to the controller's mana pool.
///   - −3: 4 damage to target creature.
///   - −7: emblem with a cast-trigger that deals 5 to any target.
///   - NamedCardFactory dispatch.
/// </summary>
public class ChandraTorchOfDefianceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Chandra_IsLegendaryPlaneswalker_Chandra_4Loyalty_AtCost2RR()
    {
        var chandra = ChandraTorchOfDefianceFactory.Create(_alice);

        chandra.Name.Should().Be("Chandra, Torch of Defiance");
        chandra.ManaCost.Should().Be("{2}{R}{R}");
        chandra.HasType(CardType.Planeswalker).Should().BeTrue();
        chandra.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        chandra.HasSubtype(CardSubtype.Chandra).Should().BeTrue();
        chandra.Loyalty.Should().Be(4);
        chandra.StartingLoyalty.Should().Be(4);
        chandra.Owner.Should().BeSameAs(_alice);
        chandra.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Chandra_HasFourLoyaltyAbilities_Plus1_Plus1_Minus3_Minus7()
    {
        var chandra = ChandraTorchOfDefianceFactory.Create(_alice);

        var loyalty = chandra.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(4);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, +1, -3, -7 });
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Chandra()
    {
        var card = NamedCardFactory.Create("Chandra, Torch of Defiance", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Chandra, Torch of Defiance");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Chandra).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(4);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(4);
    }

    // -----------------------------------------------------------------------
    // +1 (impulse): Exile the top card of your library. You may cast that
    //               card. If you don't, Chandra deals 2 damage to each
    //               opponent.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1Impulse_ExilesTopCard_AndDealsTwoToEachOpponent_WhenCastDeclined()
    {
        var top = new Card("Top", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var chandra = ChandraTorchOfDefianceFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob },
            targetCreatureResolver: null,
            anyTargetResolver: null,
            triggers: null);

        var plus1Impulse = chandra.Abilities.OfType<LoyaltyAbility>()
            .First(a => a.LoyaltyChange == +1);
        plus1Impulse.Activate();

        chandra.Loyalty.Should().Be(5); // 4 + 1

        // Top card exiled (v1 declines the cast).
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        _alice.Zones.Exile.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Exile);

        // Each opponent took 2 damage; the controller did not.
        _bob.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Plus1Impulse_EmptyLibrary_NoOpsButLoyaltyStillApplies()
    {
        var chandra = ChandraTorchOfDefianceFactory.Create(
            _alice,
            allPlayersResolver: () => new[] { _alice, _bob },
            targetCreatureResolver: null,
            anyTargetResolver: null,
            triggers: null);

        chandra.Abilities.OfType<LoyaltyAbility>().First(a => a.LoyaltyChange == +1).Activate();

        chandra.Loyalty.Should().Be(5);
        // No card to exile, no opponent damage (the impulse clause never resolves).
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // +1 (ritual): Add {R}{R}.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1Ritual_AddsTwoRedMana()
    {
        var chandra = ChandraTorchOfDefianceFactory.Create(_alice);

        // The second +1 is the ritual.
        var ritual = chandra.Abilities.OfType<LoyaltyAbility>()
            .Where(a => a.LoyaltyChange == +1).ElementAt(1);
        ritual.Activate();

        chandra.Loyalty.Should().Be(5);
        _alice.ManaPool.Red.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // −3: Chandra deals 4 damage to target creature.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus3_Deals4ToTargetCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield); bear.SetController(_bob);

        var chandra = ChandraTorchOfDefianceFactory.Create(
            _alice,
            allPlayersResolver: null,
            targetCreatureResolver: () => new[] { bear },
            anyTargetResolver: null,
            triggers: null);

        chandra.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        chandra.Loyalty.Should().Be(1); // 4 - 3
        bear.Damage.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // −7: emblem with "Whenever you cast a spell, this emblem deals 5 damage
    //     to any target."
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus7_CreatesEmblem_WithCastTriggerThatDeals5ToAnyTarget()
    {
        var bus = new EventBus();
        var triggers = new TriggerManager(new MajikStack(bus), bus);

        var chandra = ChandraTorchOfDefianceFactory.Create(
            _alice,
            allPlayersResolver: null,
            targetCreatureResolver: null,
            anyTargetResolver: () => _bob, // "any target" — a player here
            triggers: triggers);

        var ult = chandra.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -7);
        ult.CanActivate().Should().BeFalse("4 loyalty is not enough for −7");

        chandra.AddLoyalty(3); // 4 + 3 = 7
        ult.CanActivate().Should().BeTrue();
        ult.Activate();

        chandra.Loyalty.Should().Be(0); // 7 - 7

        _alice.Emblems.Should().HaveCount(1);
        var emblem = _alice.Emblems.Single();
        emblem.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        // Fire the emblem's cast trigger effect — deals 5 to the any target.
        var castTrigger = emblem.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in castTrigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(15); // 20 - 5
    }
}
