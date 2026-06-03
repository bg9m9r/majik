using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Vivien, Champion of the Wilds (War of the Spark, {2}{G}) — the
/// cast-creature-spells-as-though-flash static + the +1 vigilance/reach grant
/// + the −2 exile-and-cast-if-creature dig.
/// </summary>
public class VivienChampionOfTheWildsFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => FlashGrantRegistry.Clear();

    private static (ZoneService zones, ContinuousEffectsService effects) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var effects = new ContinuousEffectsService(bus);
        return (zones, effects);
    }

    private static void EnterBattlefield(ZoneService zones, Player owner, ICard card)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller: owner);
    }

    [Fact]
    public void Identity_LegendaryPlaneswalker_Loyalty4_At2G()
    {
        var vivien = VivienChampionOfTheWildsFactory.Create(_alice);

        vivien.Name.Should().Be("Vivien, Champion of the Wilds");
        vivien.ManaCost.Should().Be("{2}{G}");
        vivien.HasType(CardType.Planeswalker).Should().BeTrue();
        vivien.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        vivien.Loyalty.Should().Be(4);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_VivienChampion()
    {
        var card = NamedCardFactory.Create("Vivien, Champion of the Wilds", _alice);
        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Vivien, Champion of the Wilds");
    }

    [Fact]
    public void HasTwoLoyaltyAbilities()
    {
        var vivien = VivienChampionOfTheWildsFactory.Create(_alice);
        vivien.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void OnBattlefield_GrantsFlashToControllersCreatureSpells()
    {
        var (zones, effects) = BuildEngine();
        var vivien = VivienChampionOfTheWildsFactory.Create(_alice, effects);

        var creatureInHand = new Creature("Goblin Bear", "{2}{R}", 2, 2);
        creatureInHand.SetOwner(_alice);
        var sorceryInHand = new Sorcery("Divination", "{2}{U}");
        sorceryInHand.SetOwner(_alice);

        FlashGrantRegistry.HasGrantedFlash(creatureInHand).Should().BeFalse();

        EnterBattlefield(zones, _alice, vivien);

        FlashGrantRegistry.HasGrantedFlash(creatureInHand).Should().BeTrue(
            "creature spells may be cast as though they had flash");
        FlashGrantRegistry.HasGrantedFlash(sorceryInHand).Should().BeFalse(
            "only CREATURE spells get the flash grant");
    }

    [Fact]
    public void Minus2_ExilesACreature_AndGrantsCastFromExile()
    {
        var vivien = VivienChampionOfTheWildsFactory.Create(_alice);
        vivien.SetController(_alice);

        var creature = new Creature("Goblin Bear", "{2}{R}", 2, 2);
        creature.SetOwner(_alice);
        _alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var minus2 = vivien.Abilities.OfType<LoyaltyAbility>()
            .First(a => a.LoyaltyChange == -2);
        minus2.Activate();

        creature.Zone.Should().Be(ZoneType.Exile);
        creature.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "a creature pick may be cast from exile (CR 601.3e)");
        creature.RuntimeExileCastCost.Should().NotBeNull();
    }
}
