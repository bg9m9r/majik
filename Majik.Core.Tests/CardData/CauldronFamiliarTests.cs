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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CauldronFamiliarFactory"/> (Throne of
/// Eldraine, {B}).
///
/// Oracle text (Scryfall, verified):
///   "When this creature enters, each opponent loses 1 life and you gain
///    1 life.
///    Sacrifice a Food: Return this card from your graveyard to the
///    battlefield."
///
/// Covers:
/// - Identity (Creature, Cat subtype, 1/1, {B}, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB trigger fires on this card's own ETB; drains each opponent +
///   gains 1 (with resolver) and gains-only without resolver.
/// - ETB trigger does NOT fire for another permanent entering.
/// - Graveyard-return ability: a single Sacrifice-a-Food activated
///   ability with no mana cost; CanPay gates on Food; resolution sacks
///   the Food and moves Cauldron Familiar Graveyard → Battlefield.
/// </summary>
public class CauldronFamiliarTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CauldronFamiliar_Identity()
    {
        var c = CauldronFamiliarFactory.Create(_alice);

        c.Name.Should().Be("Cauldron Familiar");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Cat);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Cauldron Familiar has a single ETB drain trigger");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Cauldron Familiar has a single Sacrifice-a-Food graveyard-return ability");
    }

    [Fact]
    public void CauldronFamiliar_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Cauldron Familiar", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Cauldron Familiar");
    }

    [Fact]
    public void CauldronFamiliar_Etb_DrainsEachOpponentAndGains()
    {
        var familiar = CauldronFamiliarFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            zoneService: null,
            triggers: null);

        // CR 603.6a — the ETB CardMovedEvent fires after the card lands on
        // the battlefield, so the trigger's source is already in its active
        // zone at IsTriggered time.
        _alice.Zones.Battlefield.AddCard(familiar);
        familiar.SetZone(ZoneType.Battlefield);

        var etbEvent = new CardMovedEvent(familiar, ZoneType.Hand, ZoneType.Battlefield);

        var trigger = familiar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(etbEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life on ETB");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life on ETB");
    }

    [Fact]
    public void CauldronFamiliar_Etb_WithoutResolver_GainsLifeOnly()
    {
        var familiar = CauldronFamiliarFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(familiar);
        familiar.SetZone(ZoneType.Battlefield);

        var etbEvent = new CardMovedEvent(familiar, ZoneType.Hand, ZoneType.Battlefield);

        var trigger = familiar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(etbEvent));

        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(21, "lifegain side fires unconditionally");
        _bob.LifeTotal.Should().Be(20, "no opponentResolver ⇒ opponent-drain silently no-ops");
    }

    [Fact]
    public void CauldronFamiliar_Etb_DoesNotFireForAnotherPermanent()
    {
        var familiar = CauldronFamiliarFactory.Create(_alice);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);

        var moveEvent = new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield);

        var trigger = familiar.Abilities.OfType<TriggeredAbility>().Single();
        trigger.IsTriggered(moveEvent).Should().BeFalse(
            "the ETB trigger reads 'this creature' — only Cauldron Familiar's own ETB fires it");
    }

    [Fact]
    public void GraveyardReturn_NoManaCost_OnlyFoodSacrifice()
    {
        var familiar = CauldronFamiliarFactory.Create(_alice);

        var ability = familiar.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(1,
            "the printed cost is the Food sacrifice alone — no mana");
        ability.Costs.OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Should().HaveCount(1, "the only cost is Sacrifice a Food");
    }

    [Fact]
    public void GraveyardReturn_CanPay_FailsWithoutFood()
    {
        var familiar = CauldronFamiliarFactory.Create(_alice);

        var sacCost = familiar.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<UnderworldCookbookFactory.SacrificeAFoodCost>().Single();

        sacCost.CanPay(_alice).Should().BeFalse(
            "Sacrifice a Food cannot be paid without a Food on the battlefield (CR 117.1)");
    }

    [Fact]
    public void GraveyardReturn_SacrificesFoodAndReturnsSelfToBattlefield()
    {
        var familiar = CauldronFamiliarFactory.Create(_alice);

        // Seat Cauldron Familiar in Alice's graveyard.
        _alice.Zones.Graveyard.AddCard(familiar);
        familiar.SetZone(ZoneType.Graveyard);

        // Mint a Food-shaped artifact on the battlefield (same fixture
        // shape as the Underworld Cookbook tests).
        var food = new Artifact("Food", "", subtypes: new[] { CardSubtype.Food })
        {
            Owner = _alice,
            Controller = _alice,
            IsToken = true,
        };
        _alice.Zones.Battlefield.AddCard(food);
        food.SetZone(ZoneType.Battlefield);

        var ability = familiar.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Single().Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        // Food was sacrificed.
        food.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(food);

        // Cauldron Familiar returned from graveyard to the battlefield.
        familiar.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(familiar);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(familiar);
    }

    [Fact]
    public void GraveyardReturn_NoOp_WhenNotInGraveyard()
    {
        var familiar = CauldronFamiliarFactory.Create(_alice);

        // On the battlefield, not the graveyard.
        _alice.Zones.Battlefield.AddCard(familiar);
        familiar.SetZone(ZoneType.Battlefield);

        var ability = familiar.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        familiar.Zone.Should().Be(ZoneType.Battlefield,
            "CR 608.2b — the return is a clean no-op when the card isn't in the graveyard");
    }
}
