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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SamwiseGamgeeFactory"/> (The Lord of the Rings:
/// Tales of Middle-earth, {1}{W}).
///
/// Oracle text (Scryfall, verified):
///   "Whenever another nontoken creature you control enters, create a Food
///    token. (It's an artifact with "{2}, {T}, Sacrifice this token: You gain
///    3 life.")
///    Sacrifice three Foods: Return target historic card from your graveyard
///    to your hand. (Artifacts, legendaries, and Sagas are historic.)"
///
/// Covers:
/// - Identity (Legendary Creature, Halfling + Peasant subtypes, 2/1, {1}{W}).
/// - NamedCardFactory dispatch.
/// - ETB Food trigger fires for ANOTHER nontoken creature you control; does
///   NOT fire for Samwise's own ETB, for a token creature, or for an
///   opponent's creature.
/// - Sacrifice-three-Foods graveyard-return ability: three Sacrifice-a-Food
///   costs; CanPay gating on Food count; resolution sacks three Foods and
///   returns a historic card from the graveyard to hand (and only a historic
///   card).
/// </summary>
public class SamwiseGamgeeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Bears(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    [Fact]
    public void SamwiseGamgee_Identity()
    {
        var c = SamwiseGamgeeFactory.Create(_alice);

        c.Name.Should().Be("Samwise Gamgee");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.Subtypes.Should().Contain(CardSubtype.Halfling);
        c.Subtypes.Should().Contain(CardSubtype.Peasant);
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Samwise has a single ETB-Food trigger");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Samwise has a single Sacrifice-three-Foods graveyard-return ability");
    }

    [Fact]
    public void SamwiseGamgee_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Samwise Gamgee", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Samwise Gamgee");
    }

    [Fact]
    public void EtbTrigger_FiresForAnotherNontokenCreatureYouControl_CreatesFood()
    {
        var sam = SamwiseGamgeeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sam);
        sam.SetZone(ZoneType.Battlefield);

        var bears = Bears(_alice);
        _alice.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);

        var trigger = sam.Abilities.OfType<TriggeredAbility>().Single();
        var etbEvent = new CardMovedEvent(bears, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(etbEvent).Should().BeTrue(
            "another nontoken creature you control entering fires the Food trigger");

        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Count(a => a.HasSubtype(CardSubtype.Food))
            .Should().Be(1, "the trigger creates one Food token (CR 111.10)");
    }

    [Fact]
    public void EtbTrigger_DoesNotFireForSamwisesOwnEtb()
    {
        var sam = SamwiseGamgeeFactory.Create(_alice);

        var trigger = sam.Abilities.OfType<TriggeredAbility>().Single();
        var selfEtb = new CardMovedEvent(sam, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(selfEtb).Should().BeFalse(
            "the trigger reads 'ANOTHER ... creature' — Samwise's own ETB does not fire it");
    }

    [Fact]
    public void EtbTrigger_DoesNotFireForTokenCreature()
    {
        var sam = SamwiseGamgeeFactory.Create(_alice);

        var token = Bears(_alice);
        token.MarkAsToken();

        var trigger = sam.Abilities.OfType<TriggeredAbility>().Single();
        var etbEvent = new CardMovedEvent(token, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(etbEvent).Should().BeFalse(
            "the trigger reads 'NONTOKEN creature' — a token entering does not fire it");
    }

    [Fact]
    public void EtbTrigger_DoesNotFireForOpponentsCreature()
    {
        var sam = SamwiseGamgeeFactory.Create(_alice);

        var oppBears = Bears(_bob);

        var trigger = sam.Abilities.OfType<TriggeredAbility>().Single();
        var etbEvent = new CardMovedEvent(oppBears, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(etbEvent).Should().BeFalse(
            "the trigger reads 'creature YOU control' — an opponent's creature does not fire it");
    }

    [Fact]
    public void GraveyardReturn_NoManaCost_ThreeFoodSacrifices()
    {
        var sam = SamwiseGamgeeFactory.Create(_alice);

        var ability = sam.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3,
            "the printed cost is sacrificing three Foods — no mana");
        ability.Costs.OfType<UnderworldCookbookFactory.SacrificeAFoodCost>()
            .Should().HaveCount(3, "all three costs are Sacrifice a Food");
    }

    [Fact]
    public void GraveyardReturn_CanPay_FailsWithFewerThanThreeFoods()
    {
        var sam = SamwiseGamgeeFactory.Create(_alice);
        var ability = sam.Abilities.OfType<ActivatedAbility>().Single();

        // Two Foods on the battlefield — three distinct sac costs cannot all
        // be paid (CR 117.1 — the whole cost must be payable).
        for (var i = 0; i < 2; i++)
        {
            var food = new Artifact("Food", "", subtypes: new[] { CardSubtype.Food })
            {
                Owner = _alice,
                Controller = _alice,
                IsToken = true,
            };
            _alice.Zones.Battlefield.AddCard(food);
            food.SetZone(ZoneType.Battlefield);
        }

        var canPayAll = SamwiseGamgeeFactory.CanPayAllFoodCosts(ability, _alice);
        canPayAll.Should().BeFalse(
            "three Foods are required; only two are present");
    }

    [Fact]
    public void GraveyardReturn_SacrificesThreeFoods_ReturnsHistoricCardToHand()
    {
        var sam = SamwiseGamgeeFactory.Create(_alice);

        // Three Food tokens on the battlefield.
        for (var i = 0; i < 3; i++)
        {
            var food = new Artifact("Food", "", subtypes: new[] { CardSubtype.Food })
            {
                Owner = _alice,
                Controller = _alice,
                IsToken = true,
            };
            _alice.Zones.Battlefield.AddCard(food);
            food.SetZone(ZoneType.Battlefield);
        }

        // A non-historic card + a historic (Legendary) card in the graveyard.
        var vanilla = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        vanilla.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(vanilla);
        vanilla.SetZone(ZoneType.Graveyard);

        var legend = new Creature(
            "Tarmogoyf-Legend", "{1}{G}", 0, 1,
            supertypes: new[] { CardSupertype.Legendary });
        legend.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(legend);
        legend.SetZone(ZoneType.Graveyard);

        var ability = sam.Abilities.OfType<ActivatedAbility>().Single();

        foreach (var cost in ability.Costs) cost.Pay(_alice);
        foreach (var e in ability.Effects) e.Execute();

        // All three Foods sacrificed.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Count(a => a.HasSubtype(CardSubtype.Food))
            .Should().Be(0, "all three Foods were sacrificed");

        // The historic (Legendary) card was returned; the vanilla was not.
        _alice.Zones.Hand.GetCards().Should().Contain(legend,
            "only a HISTORIC card may be returned (CR 205.4 — Legendary is historic)");
        _alice.Zones.Hand.GetCards().Should().NotContain(vanilla,
            "a non-historic card is not a legal target");
        _alice.Zones.Graveyard.GetCards().Should().Contain(vanilla);
    }
}
