using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using CombatAbilities = Majik.Core.Combat.CombatAbilities;
using MtgCombat = Majik.Core.Combat.Combat;
using Attacker = Majik.Core.Combat.Attacker;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PhoenixChickFactory"/>.
///
/// Card: Phoenix Chick (Dominaria United, {R}) — Creature — Phoenix 1/1.
///   "Flying, haste
///    This creature can't block.
///    Whenever you attack with three or more creatures, you may pay {R}{R}.
///    If you do, return this card from your graveyard to the battlefield
///    tapped and attacking with a +1/+1 counter on it."
/// (Oracle text verified against Scryfall 2026-06-02.)
/// </summary>
[Trait("Color", "R")]
public class PhoenixChickFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature Vanilla(Player owner, string name)
    {
        var c = new Creature(name, "{1}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = new ContinuousEffectsService();
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

    // --- Identity --------------------------------------------------------

    [Fact]
    public void PhoenixChick_Identity()
    {
        var c = PhoenixChickFactory.Create(_alice);

        c.Name.Should().Be("Phoenix Chick");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phoenix).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PhoenixChick_IsRed()
    {
        var c = PhoenixChickFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Red,
            "Phoenix Chick has an {R} pip in its mana cost");
    }

    [Fact]
    public void PhoenixChick_ManaValueIsOne()
    {
        var c = PhoenixChickFactory.Create(_alice);

        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(1);
    }

    // --- Evergreen keyword markers (CR 702.9 / 702.10) -------------------

    [Fact]
    public void PhoenixChick_HasFlyingAndHasteMarkers()
    {
        var c = PhoenixChickFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Phoenix Chick has Flying (CR 702.9)");
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste").Should().BeTrue(
                "Phoenix Chick has Haste (CR 702.10)");
    }

    // --- "This creature can't block" (CR 509.1b / 509.1c) ----------------

    [Fact]
    public void PhoenixChick_CantBlock()
    {
        var c = PhoenixChickFactory.Create(_alice);

        c.ActiveEffects.Should().NotBeNull(
            "the factory wires a ContinuousEffectsService so the can't-block restriction is queryable");
        c.ActiveEffects!.HasRestriction(c, CombatRestriction.CannotBlock).Should().BeTrue(
            "Phoenix Chick can't block (CR 509.1b)");
    }

    // --- Attack trigger condition (CR 508.1f / CR 603.6d graveyard) ------

    [Fact]
    public void AttackTrigger_Fires_WhenYouAttackWithThreeCreatures()
    {
        var chick = PhoenixChickFactory.Create(_alice);
        chick.SetZone(ZoneType.Graveyard);
        var trigger = GetAttackTrigger(chick);

        // Three of Alice's creatures attack. The Chick is in the graveyard
        // and is NOT among the attackers — the trigger keys on the controller
        // attacking with 3+ creatures, not on the Chick attacking.
        var combat = new MtgCombat(_alice, _bob);
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally One"), _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally Two"), _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally Three"), _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeTrue(
            "three or more attacking creatures you control satisfies the trigger");
    }

    [Fact]
    public void AttackTrigger_DoesNotFire_WithOnlyTwoAttackers()
    {
        var chick = PhoenixChickFactory.Create(_alice);
        chick.SetZone(ZoneType.Graveyard);
        var trigger = GetAttackTrigger(chick);

        var combat = new MtgCombat(_alice, _bob);
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally One"), _bob));
        combat.AddAttacker(new Attacker(Vanilla(_alice, "Ally Two"), _bob));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "the trigger needs three or more attacking creatures");
    }

    [Fact]
    public void AttackTrigger_DoesNotFire_OnOpponentAttack()
    {
        var chick = PhoenixChickFactory.Create(_alice);
        chick.SetZone(ZoneType.Graveyard);
        var trigger = GetAttackTrigger(chick);

        // Bob attacks with three creatures; this is not "you attack".
        var combat = new MtgCombat(_bob, _alice);
        combat.AddAttacker(new Attacker(Vanilla(_bob, "Goblin A"), _alice));
        combat.AddAttacker(new Attacker(Vanilla(_bob, "Goblin B"), _alice));
        combat.AddAttacker(new Attacker(Vanilla(_bob, "Goblin C"), _alice));

        trigger.IsTriggered(new AttackersDeclaredEvent(combat)).Should().BeFalse(
            "CR 109.5 — 'you attack' keys on the Chick's controller being the attacking player");
    }

    [Fact]
    public void AttackTrigger_LivesInGraveyard()
    {
        var chick = PhoenixChickFactory.Create(_alice);
        var trigger = GetAttackTrigger(chick);

        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard,
            "the recursion trigger functions from the graveyard (CR 603.6d)");
    }

    // --- Resolution body: return from graveyard tapped & attacking -------

    [Fact]
    public void Resolve_ReturnsChick_TappedAndAttacking_WithCounter()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var eventBus = new EventBus();
        var combat = new CombatManager(eventBus);

        var chick = PhoenixChickFactory.Create(alice, triggers: null, combat: combat);

        // Chick starts in the graveyard.
        alice.Zones.Graveyard.AddCard(chick);
        chick.SetZone(ZoneType.Graveyard);

        // Alice's combat is in progress with three attackers.
        var attacker = new Creature("Ally One", "{1}", 2, 2);
        attacker.SetOwner(alice);
        attacker.SetController(alice);
        attacker.SetZone(ZoneType.Battlefield);
        attacker.ClearSummoningSickness();
        alice.Zones.Battlefield.AddCard(attacker);

        combat.StartCombat(alice);
        combat.DeclareAttackers(alice, new[]
        {
            new AttackerDeclaration(attacker, targetPlayer: bob),
        });

        var trigger = GetAttackTrigger(chick);
        foreach (var e in trigger.Effects) e.Execute();

        chick.Zone.Should().Be(ZoneType.Battlefield, "the Chick returns to the battlefield");
        alice.Zones.Battlefield.GetCards().Should().Contain(chick);
        alice.Zones.Graveyard.GetCards().Should().NotContain(chick);
        chick.IsTapped.Should().BeTrue("it returns tapped");
        chick.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "it returns with a +1/+1 counter on it");

        var attackingCreatures = combat.CurrentCombat!.Attackers
            .Select(a => a.Creature)
            .ToList();
        attackingCreatures.Should().Contain(chick, "the Chick returns attacking");
    }

    [Fact]
    public void Resolve_NoOp_WhenChickNotInGraveyard()
    {
        // If the Chick has already left the graveyard, the return no-ops
        // (CR 603.6d / CR 608.2 — re-check the zone at resolution).
        var chick = PhoenixChickFactory.Create(_alice);
        chick.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(chick);

        var trigger = GetAttackTrigger(chick);
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };

        act.Should().NotThrow();
        chick.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "no counter is added when the Chick was not returned from the graveyard");
    }
}
