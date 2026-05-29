using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KorFirewalkerFactory"/> (Worldwake, {W}).
///
/// Creature — Kor Soldier 2/2. Oracle text:
///   "Protection from red
///    Whenever a player casts a red spell, you may gain 1 life."
///
/// Covers:
///   - Identity (name, cost {W}, P/T 2/2, subtypes Kor / Soldier,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Protection from red (CR 702.16) via a single
///     <see cref="ProtectionAbility"/> ("red") readable by
///     <see cref="Protection.HasProtectionFromColor"/>; not protected
///     from other colours.
///   - Cast-trigger (CR 603.1) over <see cref="SpellCastEvent"/> fires
///     for ANY player's RED spell and gains the controller 1 life.
///   - Non-red spell does NOT fire the trigger.
///   - Trigger only active on the battlefield.
/// </summary>
public class KorFirewalkerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewSpell(Player controller, string name, string manaCost)
    {
        var c = new Instant(name, manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static void PlaceOnBattlefield(Player controller, Creature card)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void KorFirewalker_Identity_KorSoldier_2_2_AtCostW()
    {
        var c = KorFirewalkerFactory.Create(_alice);

        c.Name.Should().Be("Kor Firewalker");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KorFirewalker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kor Firewalker", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Kor Firewalker");
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Protection from red
    // -------------------------------------------------------------------------

    [Fact]
    public void KorFirewalker_HasProtectionFromRed_NotOtherColors()
    {
        var c = KorFirewalkerFactory.Create(_alice);

        c.Abilities.OfType<ProtectionAbility>().Should().ContainSingle()
            .Which.Quality.Should().Be("red");

        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeTrue(
            "CR 702.16 — Protection from red");
        Protection.HasProtectionFromColor(c, ManaColor.White).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Black).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Green).Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // Cast-trigger lifegain
    // -------------------------------------------------------------------------

    [Fact]
    public void KorFirewalker_HasSingleTriggeredAbility()
    {
        var card = KorFirewalkerFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void OpponentCastsRedSpell_TriggersAndGainsControllerOneLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fw = KorFirewalkerFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, fw);

        // Bob casts a red spell (Lightning Bolt, {R}).
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Lightning Bolt", "{R}")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(21,
            "Kor Firewalker's controller gains 1 life when a player casts a red spell");
    }

    [Fact]
    public void ControllerCastsRedSpell_TriggersToo()
    {
        // Oracle is "a player" — no controller exclusion. The controller's
        // own red spells trigger the lifegain too.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fw = KorFirewalkerFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, fw);

        bus.Publish(new SpellCastEvent(NewSpell(_alice, "Shock", "{R}")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void MultiColorRedSpell_StillTriggers()
    {
        // A spell with a red pip counts as a red spell even if it has
        // other colours (CR 105.2 — a card is each colour of its pips).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fw = KorFirewalkerFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, fw);

        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Lightning Helix", "{R}{W}")));

        triggers.PendingCount.Should().Be(1, "a {R}{W} spell is a red spell");
    }

    [Fact]
    public void NonRedSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var fw = KorFirewalkerFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, fw);

        // Bob casts a blue spell — not red, no trigger.
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Counterspell", "{U}{U}")));

        triggers.PendingCount.Should().Be(0, "Counterspell is not a red spell");
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var card = KorFirewalkerFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
