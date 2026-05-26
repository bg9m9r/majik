using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LotusCobraFactory"/> (Zendikar, {1}{G},
/// Creature — Snake 2/1).
///
/// Covers:
/// - Card identity (name, type, subtype, P/T, mana cost, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch hands back the correct shape.
/// - Landfall trigger fires on a land entering under controller's control
///   (CR 614 / CR 603.1) — IsTriggered returns true on the relevant
///   CardMovedEvent.
/// - Landfall trigger does NOT fire when an opponent's land enters.
/// - Landfall trigger does NOT fire when a non-land card enters.
/// - Resolve effect adds one mana of any color to the controller's pool;
///   default is Green when no picker supplied.
/// - Resolve effect honours the colorPicker callback (Red selected →
///   {R} added).
/// </summary>
public class LotusCobraTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LotusCobra_Identity_Snake_2_1_AtCost1G()
    {
        var c = LotusCobraFactory.Create(_alice);

        c.Name.Should().Be("Lotus Cobra");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LotusCobra_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Lotus Cobra", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Lotus Cobra");
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}");
    }

    // -----------------------------------------------------------------------
    // Landfall trigger (CR 614 / CR 603.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void LotusCobra_Landfall_FiresOnLandEnteringUnderControllerControl()
    {
        var c = LotusCobraFactory.Create(_alice);
        // Lotus Cobra must be on the battlefield for its trigger to fire
        // (ActiveZones default = Battlefield, CR 603.6).
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var land = new Land("Forest", supertypes: null, subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(_alice);
        land.SetController(_alice);

        var moved = new CardMovedEvent(land, ZoneType.Library, ZoneType.Battlefield);
        trigger.IsTriggered(moved).Should().BeTrue(
            "CR 614 — a land entering under the controller's control fires landfall");
    }

    [Fact]
    public void LotusCobra_Landfall_DoesNotFireForOpponentLand()
    {
        var bob = new Player("Bob", 20);
        var c = LotusCobraFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var land = new Land("Mountain", supertypes: null, subtypes: new[] { CardSubtype.Mountain });
        land.SetOwner(bob);
        land.SetController(bob);

        var moved = new CardMovedEvent(land, ZoneType.Library, ZoneType.Battlefield);
        trigger.IsTriggered(moved).Should().BeFalse(
            "landfall is gated to lands entering under YOUR control (CR 614)");
    }

    [Fact]
    public void LotusCobra_Landfall_DoesNotFireForNonLandCard()
    {
        var c = LotusCobraFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var moved = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);
        trigger.IsTriggered(moved).Should().BeFalse(
            "landfall only triggers off lands — a creature ETB doesn't qualify (CR 614)");
    }

    // -----------------------------------------------------------------------
    // Resolve effect — add one mana of any color
    // -----------------------------------------------------------------------

    [Fact]
    public void LotusCobra_Resolve_DefaultColor_AddsGreenManaToPool()
    {
        var alice = new Player("Alice", 20);
        var c = LotusCobraFactory.Create(alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var before = alice.ManaPool.Green;

        foreach (var effect in trigger.Effects) effect.Execute();

        alice.ManaPool.Green.Should().Be(before + 1,
            "default colour is Green (Lotus Cobra's printed colour)");
    }

    [Fact]
    public void LotusCobra_Resolve_HonoursColorPicker()
    {
        var alice = new Player("Alice", 20);
        var c = LotusCobraFactory.Create(alice, triggers: null, colorPicker: () => ManaColor.Red);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var redBefore = alice.ManaPool.Red;
        var greenBefore = alice.ManaPool.Green;

        foreach (var effect in trigger.Effects) effect.Execute();

        alice.ManaPool.Red.Should().Be(redBefore + 1,
            "colorPicker returned Red — pool gains one {R}");
        alice.ManaPool.Green.Should().Be(greenBefore,
            "Green pool unchanged when picker selected Red");
    }

    [Fact]
    public void LotusCobra_BuildOneManaOfColor_CoercesNonColoredToDefault()
    {
        // Generic / Colorless aren't WUBRG; CR 106.1b — "any color" is a
        // WUBRG colour, not generic/colourless. The helper coerces to Green.
        var cost = LotusCobraFactory.BuildOneManaOfColor(ManaColor.Generic);

        cost.Green.Should().Be(1,
            "Generic is coerced to the Green default per the helper's CR 106.1b guard");
    }
}
