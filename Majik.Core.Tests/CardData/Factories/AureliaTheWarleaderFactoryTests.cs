using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AureliaTheWarleaderFactory"/>.
///
/// Aurelia, the Warleader — {2}{R}{R}{W}{W} Legendary Creature — Angel 3/4:
///   "Flying, vigilance, haste
///    Whenever Aurelia attacks for the first time each turn, untap all
///    creatures you control. After this phase, there is an additional combat
///    phase."
///
/// Covers the card's UNIQUE behaviour (the first-attack-each-turn trigger:
/// untap ALL creatures you control + an additional combat phase) plus a single
/// identity assert for the non-vanilla stats / keywords. The contract test
/// (<c>CardFactoryContractTests</c>) already asserts dispatch + well-formedness.
/// </summary>
[Trait("Color", "M")]
public class AureliaTheWarleaderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Aurelia_Identity_IsRedWhiteLegendaryAngel_3_4_WithKeywords()
    {
        var card = AureliaTheWarleaderFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Aurelia, the Warleader");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(4);
        card.ManaCostValue.TotalValue.Should().Be(6, "{2}{R}{R}{W}{W} is mana value 6");

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.White);

        // Flying, vigilance, haste keyword markers (CR 702.9 / 702.21 / 702.10).
        var keywords = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Vigilance");
        keywords.Should().Contain("Haste");
    }

    [Fact]
    public void FirstAttack_UntapsAllCreaturesYouControl_AndEnqueuesAdditionalCombat()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(0);

        var aurelia = AureliaTheWarleaderFactory.Create(_alice, triggers: null, eventBus: null);
        _alice.Zones.Battlefield.AddCard(aurelia);
        aurelia.SetZone(ZoneType.Battlefield);
        aurelia.Tap(); // attacking without vigilance modelling here → tapped

        // Another tapped creature Alice controls — must be untapped.
        var other = NonAurelia("Grizzly Bears", _alice, 2, 2);
        _alice.Zones.Battlefield.AddCard(other);
        other.SetZone(ZoneType.Battlefield);
        other.Tap();

        // An opponent's tapped creature — must NOT be touched.
        var enemy = NonAurelia("Goblin", _bob, 1, 1);
        _bob.Zones.Battlefield.AddCard(enemy);
        enemy.SetZone(ZoneType.Battlefield);
        enemy.Tap();

        Fire(aurelia, AttackWith(aurelia));

        other.IsTapped.Should().BeFalse("untap ALL creatures you control (CR 701.20a)");
        aurelia.IsTapped.Should().BeFalse(
            "Aurelia untaps ALL creatures you control, including herself (printed \"all\")");
        enemy.IsTapped.Should().BeTrue("opponent's creature is not untapped");
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1,
            "after this phase there is an additional combat phase (CR 506.4)");
    }

    [Fact]
    public void OnlyAttackingControllerTriggers_OpponentAttackDoesNothing()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var aurelia = AureliaTheWarleaderFactory.Create(_alice, triggers: null, eventBus: null);
        _alice.Zones.Battlefield.AddCard(aurelia);
        aurelia.SetZone(ZoneType.Battlefield);

        // Bob is the attacking player — Aurelia's controller (Alice) is not
        // attacking, so the trigger must not fire.
        var combat = new Majik.Core.Combat.Combat(attackingPlayer: _bob, defendingPlayer: _alice);
        Fire(aurelia, combat);

        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(0,
            "trigger fires only when Aurelia's controller attacks");
    }

    [Fact]
    public void FirstTimeEachTurn_SecondAttackDoesNotTriggerAgain()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var aurelia = AureliaTheWarleaderFactory.Create(_alice, triggers: null, eventBus: null);
        _alice.Zones.Battlefield.AddCard(aurelia);
        aurelia.SetZone(ZoneType.Battlefield);

        // First attack — enqueues one additional combat.
        Fire(aurelia, AttackWith(aurelia));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1);

        // Second attack the same turn (the additional combat it just made) — the
        // trigger only fires for the FIRST attack each turn (CR 603.2), so no
        // second additional combat is enqueued.
        Fire(aurelia, AttackWith(aurelia));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1,
            "attacks for the first time each turn only (CR 603.2)");
    }

    [Fact]
    public void FirstAttackGate_ResetsOnNewTurn()
    {
        using var scope = AdditionalCombatRegistryProvider.PushScope();

        var bus = new EventBus();
        var aurelia = AureliaTheWarleaderFactory.Create(_alice, triggers: null, eventBus: bus);
        _alice.Zones.Battlefield.AddCard(aurelia);
        aurelia.SetZone(ZoneType.Battlefield);

        Fire(aurelia, AttackWith(aurelia));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(1);

        // New turn resets the "first time each turn" gate (CR 603.2).
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 3));

        Fire(aurelia, AttackWith(aurelia));
        AdditionalCombatRegistryProvider.Current.Pending.Should().Be(2,
            "the first-attack-each-turn gate reset on the new turn");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Creature NonAurelia(string name, Player controller, int p, int t)
    {
        var c = new Creature(name, "{1}", p, t);
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    private Majik.Core.Combat.Combat AttackWith(Creature attacker)
    {
        var combat = new Majik.Core.Combat.Combat(attackingPlayer: _alice, defendingPlayer: _bob);
        combat.AddAttacker(new Attacker(attacker, targetPlayer: _bob));
        return combat;
    }

    private void Fire(Creature aurelia, Majik.Core.Combat.Combat combat)
    {
        var trigger = aurelia.Abilities.OfType<TriggeredAbility>().Single();
        var fired = trigger.Condition.Matches(
            new AttackersDeclaredEvent(combat), trigger);
        if (!fired) return;
        foreach (var e in trigger.Effects) e.Execute();
    }
}
