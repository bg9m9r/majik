using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AftermathAnalystFactory"/> — Creature — Elf Detective
/// {2}{G} 1/1 (Bloomburrow):
///   "When this creature enters, mill three cards.
///    {3}{G}, Sacrifice this creature: Return all land cards from your
///    graveyard to the battlefield tapped."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: name, {2}{G}, 1/1, Creature — Elf Detective.
/// - ETB trigger (CR 603.6a / 701.13): mills 3 from the controller's library
///   when Aftermath Analyst enters via ZoneService.
/// - Sacrifice activated ability (CR 602): cost is {3}{G} + sacrifice-self
///   (CR 701.16), and the effect returns ALL land cards from the controller's
///   graveyard to the battlefield tapped (CR 701.18), leaving non-land cards
///   in the graveyard.
///
/// (CardFactoryContractTests already asserts NamedCardFactory dispatch +
/// well-formedness for every implemented card, so no dispatch test here.)
/// </summary>
[Trait("Color", "G")]
public class AftermathAnalystFactoryTests
{
    private static void StackLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature($"Pile {i}", "{0}", 1, 1);
            c.SetOwner(owner);
            owner.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    private static Land MakeLandInGraveyard(Player owner, string name)
    {
        var land = new Land(name);
        land.SetOwner(owner);
        owner.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard);
        return land;
    }

    // ------------------------------------------------------------------
    // Identity
    // ------------------------------------------------------------------

    [Fact]
    public void AftermathAnalyst_Identity()
    {
        var alice = new Player("Alice", 20);
        var c = AftermathAnalystFactory.Create(alice);

        c.Name.Should().Be("Aftermath Analyst");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Detective).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(alice);
        c.Controller.Should().BeSameAs(alice);
    }

    // ------------------------------------------------------------------
    // ETB mill
    // ------------------------------------------------------------------

    [Fact]
    public void AftermathAnalyst_EntersBattlefield_MillsThree()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var zones = new ZoneService(bus);

        StackLibrary(alice, 10);

        var c = AftermathAnalystFactory.Create(alice, triggers);
        c.SetZone(ZoneType.Hand);
        alice.Zones.Hand.AddCard(c);

        zones.MoveCard(c, ZoneType.Hand, ZoneType.Battlefield, alice);

        triggers.PendingCount.Should().Be(1,
            "the ETB trigger must queue when Aftermath Analyst enters");
        triggers.PutPendingTriggersOnStack(alice);
        stack.Pop()!.Resolve();

        alice.Zones.Library.GetCards().Should().HaveCount(7,
            "ETB mill of 3 takes the library from 10 → 7");
        alice.Zones.Graveyard.GetCards().Should().HaveCount(3);
    }

    // ------------------------------------------------------------------
    // Sacrifice activated ability — cost shape
    // ------------------------------------------------------------------

    [Fact]
    public void AftermathAnalyst_SacAbility_CostsThreeGreenAndSacrificesSelf()
    {
        var alice = new Player("Alice", 20);
        var c = AftermathAnalystFactory.Create(alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "Aftermath Analyst has exactly one activated ability");

        var ability = activated[0];
        // ManaCostCost.Description renders symbol-stripped (e.g. "3G").
        ability.Costs.OfType<Majik.Core.Costs.ManaCostCost>()
            .Should().ContainSingle()
            .Which.Description.Should().Be("3G");
        ability.Costs.OfType<Majik.Core.Costs.SacrificeSelfCost>()
            .Should().ContainSingle("the printed cost sacrifices this creature (CR 701.16)")
            .Which.Self.Should().BeSameAs(c);
    }

    // ------------------------------------------------------------------
    // Sacrifice activated ability — return-all-lands effect
    // ------------------------------------------------------------------

    [Fact]
    public void AftermathAnalyst_ReturnAllLands_ReturnsEveryLandTapped_LeavesNonlands()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        ZoneServiceRegistry.Set(alice, zones);
        try
        {
            // Three land cards + one non-land card in the graveyard.
            var forest = MakeLandInGraveyard(alice, "Forest");
            var island = MakeLandInGraveyard(alice, "Island");
            var swamp = MakeLandInGraveyard(alice, "Swamp");

            var spell = new Creature("Bog Witch", "{B}", 2, 2);
            spell.SetOwner(alice);
            alice.Zones.Graveyard.AddCard(spell);
            spell.SetZone(ZoneType.Graveyard);

            AftermathAnalystFactory.ReturnAllLands(alice);

            var battlefield = alice.Zones.Battlefield.GetCards().ToList();
            battlefield.Should().Contain(new ICard[] { forest, island, swamp });
            forest.IsTapped.Should().BeTrue("returned lands enter tapped (CR 701.18)");
            island.IsTapped.Should().BeTrue("returned lands enter tapped (CR 701.18)");
            swamp.IsTapped.Should().BeTrue("returned lands enter tapped (CR 701.18)");

            // The non-land stays in the graveyard.
            alice.Zones.Graveyard.GetCards().Should().Contain(spell);
            alice.Zones.Graveyard.GetCards().Should().NotContain(new ICard[] { forest, island, swamp });
        }
        finally
        {
            ZoneServiceRegistry.Remove(alice);
        }
    }
}
