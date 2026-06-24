using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MostValuableSlayerFactory"/>.
///
/// Most Valuable Slayer ({3}{R}). Creature — Human Warrior 2/4. Oracle text
/// (verified against Scryfall 2026-06-24):
///   "Whenever you attack, target attacking creature gets +1/+0 and gains first
///    strike until end of turn."
///
/// Covers (UNIQUE behaviour only — dispatch + well-formedness are covered by
/// CardFactoryContractTests for every implemented card):
/// - Identity ({3}{R} Creature — Human Warrior, 2/4, mono-R).
/// - Attack trigger condition (CR 508.1 / 109.5): fires when the controller is
///   the attacking player; not when an opponent attacks.
/// - Resolution gives a target attacking creature +1/+0 and First Strike until
///   end of turn (CR 613.7c / 613.1c / 702.7), landing on the controller's
///   attacker (the v1 default target).
/// - The pump + First Strike grant expire at end of turn (CR 514.2).
/// </summary>
[Trait("Color", "R")]
public class MostValuableSlayerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility AttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

    private static Creature MakeAttacker(Player owner, ContinuousEffectsService? svc = null)
    {
        var c = new Creature("Goblin", "{R}", 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        if (svc != null) c.ActiveEffects = svc;
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MostValuableSlayer_Identity()
    {
        var c = MostValuableSlayerFactory.Create(_alice);

        c.Name.Should().Be("Most Valuable Slayer");
        c.ManaCost.Should().Be("{3}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().HaveCount(1);
    }

    [Fact]
    public void MostValuableSlayer_HasOneAttackTrigger()
    {
        var c = MostValuableSlayerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the 'whenever you attack' trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Attack trigger condition (CR 508.1 / 109.5)
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_FiresWhenControllerAttacks()
    {
        var c = MostValuableSlayerFactory.Create(_alice);
        c.SetController(_alice);
        c.SetZone(ZoneType.Battlefield);
        var trigger = AttackTrigger(c);

        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Attacker(MakeAttacker(_alice), _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue(
            "CR 508.1 / 109.5 — 'whenever you attack' fires when the controller is the attacking player.");
    }

    [Fact]
    public void AttackTrigger_DoesNotFireWhenOpponentAttacks()
    {
        var c = MostValuableSlayerFactory.Create(_alice);
        c.SetController(_alice);
        c.SetZone(ZoneType.Battlefield);
        var trigger = AttackTrigger(c);

        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        combat.AddAttacker(new Attacker(MakeAttacker(_bob), _alice));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "CR 109.5 — 'you attack' = the trigger source's controller is the attacking player.");
    }

    // -----------------------------------------------------------------------
    // Resolution — "+1/+0 and gains first strike until end of turn"
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_GivesAttackerPlus1Power_AndFirstStrike()
    {
        var service = new ContinuousEffectsService();
        var c = MostValuableSlayerFactory.Create(_alice, service);
        c.SetController(_alice);
        c.SetZone(ZoneType.Battlefield);

        var attacker = MakeAttacker(_alice, service);
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Attacker(attacker, _bob));

        var trigger = AttackTrigger(c);

        // Fire the trigger condition so the resolve body captures the combat,
        // then resolve.
        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        attacker.Power.Should().Be(2, "the target attacking creature gets +1/+0 (CR 613.7c)");
        attacker.Toughness.Should().Be(1, "toughness is unchanged (+1/+0, not +1/+1)");
        CombatAbilities.HasFirstStrike(attacker).Should().BeTrue(
            "the target attacking creature gains first strike until end of turn (CR 613.1c / 702.7)");
    }

    [Fact]
    public void AttackTrigger_PumpAndFirstStrike_ExpireAtEndOfTurn()
    {
        var service = new ContinuousEffectsService();
        var c = MostValuableSlayerFactory.Create(_alice, service);
        c.SetController(_alice);
        c.SetZone(ZoneType.Battlefield);

        var attacker = MakeAttacker(_alice, service);
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Attacker(attacker, _bob));

        var trigger = AttackTrigger(c);
        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        attacker.Power.Should().Be(2);
        CombatAbilities.HasFirstStrike(attacker).Should().BeTrue();

        // CR 514.2 — cleanup wipes "until end of turn" effects.
        service.ExpireEndOfTurn();

        attacker.Power.Should().Be(1, "the +1/+0 expires at end of turn (CR 514.2)");
        CombatAbilities.HasFirstStrike(attacker).Should().BeFalse(
            "the first strike grant expires at end of turn (CR 514.2)");
    }
}
