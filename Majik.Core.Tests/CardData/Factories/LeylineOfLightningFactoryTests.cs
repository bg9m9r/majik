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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Leyline of Lightning (Modern Horizons 3, {2}{R}{R}).
///
/// Oracle text (verified against Scryfall + the embedded seed):
///   "If this card is in your opening hand, you may begin the game with it
///    on the battlefield.
///    Whenever you cast a spell, you may pay {1}. If you do, this enchantment
///    deals 1 damage to target player or planeswalker."
///
/// (NB: the implementation tracks the SHIPPED oracle — "whenever you cast a
/// spell" / "target player or planeswalker" — not a "first spell each turn" /
/// "any target" variant. Rules authority: the embedded seed matches current
/// Scryfall.)
///
/// Covers:
///   - Identity / dispatch.
///   - Opening-hand Leyline marker preserved.
///   - Cast-a-spell trigger fires for EVERY spell the controller casts.
///   - An opponent's spell does NOT trigger ("you cast").
///   - Optional {1} payment: paid (mana available) → 1 damage to chosen
///     player; declined (no mana) → no damage.
///   - Damage routes to a planeswalker target as loyalty removal.
/// </summary>
public class LeylineOfLightningFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Leyline_Identity()
    {
        var c = LeylineOfLightningFactory.Create(_alice);

        c.Name.Should().Be("Leyline of Lightning");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches()
    {
        var card = NamedCardFactory.Create("Leyline of Lightning", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Leyline of Lightning");
        card.ManaCost.Should().Be("{2}{R}{R}");
    }

    [Fact]
    public void CarriesOpeningHandLeylineMarker()
    {
        var c = LeylineOfLightningFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == OpeningHandLeylineAlternativeCost.LeylineKeyword);
    }

    [Fact]
    public void ControllerCastsSpell_TriggerFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfLightningFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        CastSpell(_alice, bus);

        triggers.PendingCount.Should().Be(1,
            "Leyline of Lightning triggers whenever its controller casts a spell");
    }

    [Fact]
    public void EverySpellTriggers_NotJustTheFirst()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfLightningFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        CastSpell(_alice, bus);
        CastSpell(_alice, bus);

        triggers.PendingCount.Should().Be(2,
            "the trigger fires on EVERY cast — 'whenever you cast a spell', not 'first spell each turn'");
    }

    [Fact]
    public void OpponentCastsSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfLightningFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        CastSpell(_bob, bus);

        triggers.PendingCount.Should().Be(0,
            "only the controller's own casts trigger ('whenever YOU cast a spell')");
    }

    [Fact]
    public void PayingOne_DealsOneDamageToChosenPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfLightningFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        CastSpell(_alice, bus);

        // Alice floats {1} so the optional payment goes through.
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var trigger = leyline.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        _bob.LifeTotal.Should().Be(20);
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19,
            "paid {1} → Leyline deals 1 damage to the chosen player");
    }

    [Fact]
    public void CannotPayOne_NoDamage()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfLightningFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        CastSpell(_alice, bus);

        // Alice has no mana — the optional {1} cannot be paid.
        var trigger = leyline.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20,
            "no {1} available → no payment, no damage ('you MAY pay {1}. If you do, ...')");
    }

    [Fact]
    public void PayingOne_DealsOneToPlaneswalkerAsLoyaltyRemoval()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var leyline = LeylineOfLightningFactory.Create(_alice, bus, triggers);
        PlaceOnBattlefield(leyline, _alice);

        var pw = new Planeswalker("Some Walker", "{4}", startingLoyalty: 4);
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        CastSpell(_alice, bus);
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var trigger = leyline.Abilities.OfType<TriggeredAbility>().Single();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { pw } });

        foreach (var e in trigger.Effects) e.Execute();

        pw.Loyalty.Should().Be(3,
            "1 damage to a planeswalker removes 1 loyalty (CR 306.7)");
    }

    private static void CastSpell(Player caster, EventBus bus)
    {
        var card = new Instant("Bolt", "R") { Owner = caster };
        var spell = new Majik.Core.Spells.Spell(card, caster);
        bus.Publish(new SpellCastEvent(spell));
    }

    private static void PlaceOnBattlefield(Enchantment leyline, Player owner)
    {
        owner.Zones.Battlefield.AddCard(leyline);
        leyline.SetZone(ZoneType.Battlefield);
    }
}
