using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DragonsClawFactory"/> (8th Edition, {2}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "Whenever a player casts a red spell, you may gain 1 life."
///
/// Dragon's Claw is the artifact member of the Claw cycle — same
/// red-spell-cast lifegain line as <see cref="KorFirewalkerFactory"/>
/// (which carries the identical clause), minus Kor Firewalker's
/// protection-from-red and creature body. The trigger is mirrored from
/// Kor Firewalker; the base shape is materialised from the embedded JSON
/// definition.
///
/// Covers:
///   - Identity (name, cost {2}, Artifact type, colourless, owner /
///     controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Cast-trigger (CR 603.1) over <see cref="SpellCastEvent"/> fires
///     for ANY player's RED spell and gains the controller 1 life.
///   - Multi-colour spell with a red pip still triggers.
///   - Non-red spell does NOT fire the trigger.
///   - Trigger only active on the battlefield.
/// </summary>
public class DragonsClawFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewSpell(Player controller, string name, string manaCost)
    {
        var c = new Instant(name, manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static void PlaceOnBattlefield(Player controller, Artifact card)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void DragonsClaw_Identity_Artifact_AtCost2_Colorless()
    {
        var c = DragonsClawFactory.Create(_alice);

        c.Name.Should().Be("Dragon's Claw");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeFalse();
        CardColors.GetColors(c).Should().BeEmpty("Dragon's Claw is a colourless artifact");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DragonsClaw_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Dragon's Claw", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Dragon's Claw");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one red-spell-cast lifegain trigger is attached");
    }

    // -------------------------------------------------------------------------
    // Cast-trigger lifegain
    // -------------------------------------------------------------------------

    [Fact]
    public void DragonsClaw_HasSingleTriggeredAbility()
    {
        var card = DragonsClawFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void OpponentCastsRedSpell_TriggersAndGainsControllerOneLife()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var claw = DragonsClawFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, claw);

        // Bob casts a red spell (Lightning Bolt, {R}).
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Lightning Bolt", "{R}")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(21,
            "Dragon's Claw's controller gains 1 life when a player casts a red spell");
    }

    [Fact]
    public void ControllerCastsRedSpell_TriggersToo()
    {
        // Oracle is "a player" — no controller exclusion. The controller's
        // own red spells trigger the lifegain too.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var claw = DragonsClawFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, claw);

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

        var claw = DragonsClawFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, claw);

        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Lightning Helix", "{R}{W}")));

        triggers.PendingCount.Should().Be(1, "a {R}{W} spell is a red spell");
    }

    [Fact]
    public void NonRedSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var claw = DragonsClawFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, claw);

        // Bob casts a blue spell — not red, no trigger.
        bus.Publish(new SpellCastEvent(NewSpell(_bob, "Counterspell", "{U}{U}")));

        triggers.PendingCount.Should().Be(0, "Counterspell is not a red spell");
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var card = DragonsClawFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
