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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="AshZealotFactory"/> (Return to Ravnica, {R}{R}).
/// Oracle: "First strike, haste. Whenever a player casts a spell from a
/// graveyard, this creature deals 3 damage to that player."
///
/// Covers:
/// - Identity (Human Warrior 2/2, mana cost {R}{R}, First strike + Haste).
/// - NamedCardFactory dispatch.
/// - Trigger fires (and deals 3 to the caster) when a player casts a spell
///   from a graveyard — for ANY player (controller's own cast included).
/// - Trigger does NOT fire on a spell cast from hand (the common case).
/// - The damage feeds <see cref="Player.LifeLostThisTurn"/> (Spectacle /
///   Revolt / lifegain observers).
/// - Trigger only active on the battlefield.
/// </summary>
public class AshZealotTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewSpell(
        Player controller, string name, string manaCost, bool fromGraveyard)
    {
        var c = new Instant(name, manaCost) { Owner = controller };
        var spell = new Majik.Core.Spells.Spell(c, controller);
        spell.WasCastFromGraveyard = fromGraveyard;
        return spell;
    }

    private static void PlaceOnBattlefield(Player controller, Creature card)
    {
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------

    [Fact]
    public void AshZealot_Identity_HumanWarrior_2_2_AtCostRR_FirstStrikeHaste()
    {
        var card = AshZealotFactory.Create(_alice);

        card.Name.Should().Be("Ash Zealot");
        card.ManaCost.Should().Be("{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        var keywords = card.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("First strike");
        keywords.Should().Contain("Haste");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AshZealot_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ash Zealot", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Ash Zealot");
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void AshZealot_HasSingleTriggeredAbility()
    {
        var card = AshZealotFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------
    // Trigger behaviour
    // -------------------------------------------------------------------

    [Fact]
    public void OpponentCastsFromGraveyard_TriggersAndDeals3ToOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ashZealot = AshZealotFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, ashZealot);

        // Bob flashes back / escapes a spell from his graveyard.
        bus.Publish(new SpellCastEvent(
            NewSpell(_bob, "Flashbacked Bolt", "{R}", fromGraveyard: true)));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _bob.LifeTotal.Should().Be(17, "Ash Zealot deals 3 to the graveyard-caster");
        _bob.LifeLostThisTurn.Should().Be(3, "the loss feeds Spectacle / Revolt");
    }

    [Fact]
    public void ControllerCastsFromGraveyard_TriggersAndDamagesController()
    {
        // Oracle is "a player" — no controller exclusion. Ash Zealot's own
        // controller casting from THEIR graveyard still bounces the damage.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ashZealot = AshZealotFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, ashZealot);

        bus.Publish(new SpellCastEvent(
            NewSpell(_alice, "Flashbacked Bolt", "{R}", fromGraveyard: true)));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(17, "the controller's own graveyard cast still triggers");
    }

    [Fact]
    public void SpellCastFromHand_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ashZealot = AshZealotFactory.Create(_alice, triggers);
        PlaceOnBattlefield(_alice, ashZealot);

        // A normal cast from hand — Ash Zealot only punishes graveyard casts.
        bus.Publish(new SpellCastEvent(
            NewSpell(_bob, "Lightning Bolt", "{R}", fromGraveyard: false)));

        triggers.PendingCount.Should().Be(0, "only graveyard casts trigger Ash Zealot");
        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var card = AshZealotFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
