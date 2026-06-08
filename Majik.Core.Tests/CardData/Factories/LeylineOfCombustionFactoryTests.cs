using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Leyline of Combustion (Core Set 2020, {2}{R}{R}).
///
/// Oracle text (verified against Scryfall + the embedded seed):
///   "If this card is in your opening hand, you may begin the game with it
///    on the battlefield.
///    Whenever you and/or at least one permanent you control becomes the
///    target of a spell or ability an opponent controls, this enchantment
///    deals 2 damage to that player."
///
/// Covers:
///   - Identity / dispatch.
///   - Opening-hand Leyline marker preserved.
///   - Trigger fires when an opponent's spell targets a permanent you control.
///   - Trigger fires when an opponent's spell targets you (the player).
///   - Resolution deals 2 damage to the opponent (the spell/ability's controller).
///   - Negative: your own spell targeting your own permanent does NOT trigger.
/// </summary>
public class LeylineOfCombustionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature NewCreature(Player controller, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void Leyline_Identity()
    {
        var c = LeylineOfCombustionFactory.Create(_alice);

        c.Name.Should().Be("Leyline of Combustion");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches()
    {
        var card = NamedCardFactory.Create("Leyline of Combustion", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Leyline of Combustion");
        card.ManaCost.Should().Be("{2}{R}{R}");
    }

    [Fact]
    public void CarriesOpeningHandLeylineMarker()
    {
        var c = LeylineOfCombustionFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == OpeningHandLeylineAlternativeCost.LeylineKeyword);
    }

    [Fact]
    public void OpponentSpellTargetsYourPermanent_Triggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfCombustionFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        var bear = NewCreature(_alice);

        // Bob targets Alice's bear.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting a permanent Alice controls triggers Combustion");
    }

    [Fact]
    public void OpponentSpellTargetsYouThePlayer_Triggers()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfCombustionFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Player(_alice) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(1,
            "an opponent's spell targeting Alice herself triggers Combustion");
    }

    [Fact]
    public void Resolution_DealsTwoDamageToOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfCombustionFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        var bear = NewCreature(_alice);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var spell = new Majik.Core.Spells.Spell(bolt, _bob, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        var trigger = leyline.Abilities.OfType<TriggeredAbility>().Single();
        _bob.LifeTotal.Should().Be(20);

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(18,
            "Combustion deals 2 damage to the controller of the targeting spell (Bob)");
    }

    [Fact]
    public void YourOwnSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfCombustionFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        var bear = NewCreature(_alice);

        // Alice's own spell targeting her own creature — not "an opponent controls".
        var growth = new Instant("Giant Growth", "G") { Owner = _alice };
        var spell = new Majik.Core.Spells.Spell(growth, _alice, new[] { Target.Permanent(bear) });
        bus.Publish(new TargetsChosenEvent(spell, spell.Targets));

        triggers.PendingCount.Should().Be(0,
            "Combustion only fires for a spell or ability an OPPONENT controls");
    }

    private static void PlaceOnBattlefield(Enchantment leyline, Player owner)
    {
        owner.Zones.Battlefield.AddCard(leyline);
        leyline.SetZone(ZoneType.Battlefield);
    }
}
