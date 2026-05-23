using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinRabblemasterFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Goblin + Warrior subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Lord-style Haste grant to other controller-Goblins via
///   <see cref="LordStaticEffect"/> (Goblin Chieftain shape, keyword-only).
/// - Attack trigger condition matches CreatureAttacksEvent for this card
///   only (CR 508.1f per-attacker self-match).
/// - Attack-trigger pump body:
///     - 0 other attacking Goblins → +1/+0 (Rabblemaster itself counts;
///       no "other" qualifier on this rider — contrast Goblin Piledriver).
///     - 3 other attacking Goblins → +4/+0 (Rabblemaster + 3 = 4).
///     - Token is created on the controller's battlefield.
/// </summary>
public class GoblinRabblemasterTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeGoblin(Player owner, string name = "Mogg Fanatic")
    {
        var c = new Creature(name, "R", 1, 1, subtypes: new[] { CardSubtype.Goblin });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    [Fact]
    public void GoblinRabblemaster_Identity()
    {
        var c = GoblinRabblemasterFactory.Create(_alice);

        c.Name.Should().Be("Goblin Rabblemaster");
        c.ManaCost.Should().Be("{2}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinRabblemaster_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Rabblemaster", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Rabblemaster");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void GoblinRabblemaster_GrantsHasteToOtherControllerGoblins()
    {
        var svc = new ContinuousEffectsService();

        var otherGoblin = MakeGoblin(_alice);
        otherGoblin.ActiveEffects = svc;

        var rab = GoblinRabblemasterFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            attackingCreaturesSource: null);
        rab.SetZone(ZoneType.Battlefield);
        rab.ActiveEffects = svc;

        CombatAbilities.HasHaste(otherGoblin).Should().BeTrue(
            "Other Goblins you control gain Haste from Rabblemaster's static.");
        // Keyword-only grant — no +1/+1.
        otherGoblin.GetPower().Should().Be(1, "Rabblemaster doesn't pump P/T.");
        otherGoblin.GetToughness().Should().Be(1);
    }

    [Fact]
    public void GoblinRabblemaster_DoesNotGrantHasteToOpponentGoblin()
    {
        var svc = new ContinuousEffectsService();

        var oppGoblin = MakeGoblin(_bob);
        oppGoblin.ActiveEffects = svc;

        var rab = GoblinRabblemasterFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            attackingCreaturesSource: null);
        rab.SetZone(ZoneType.Battlefield);
        rab.ActiveEffects = svc;

        CombatAbilities.HasHaste(oppGoblin).Should().BeFalse(
            "Lord static is scoped to the controller (CR 109.5 — 'you').");
    }

    [Fact]
    public void GoblinRabblemaster_HasAttackTrigger_MatchesSelfOnly()
    {
        var c = GoblinRabblemasterFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(c);

        trigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "CR 508.1f per-attacker self-match.");

        var other = MakeGoblin(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the per-attacker trigger only fires for Rabblemaster itself.");
    }

    [Fact]
    public void GoblinRabblemaster_AttackTrigger_ZeroOtherGoblins_PumpsOne_AndCreatesToken()
    {
        var svc = new ContinuousEffectsService();

        var attackers = new List<Creature>();
        var rab = GoblinRabblemasterFactory.Create(
            _alice,
            continuousEffects: null,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        rab.SetZone(ZoneType.Battlefield);
        rab.ActiveEffects = svc;
        // Place Rabblemaster on Alice's battlefield zone so token creation
        // happens against her zone manager.
        _alice.Zones.Battlefield.AddCard(rab);

        attackers.Add(rab); // only Rabblemaster attacking.

        var goblinsBefore = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken);

        var trigger = GetAttackTrigger(rab);
        foreach (var e in trigger.Effects) e.Execute();

        // Token: 1 new 1/1 Goblin token under Alice's control.
        var goblinTokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken)
            .ToList();
        goblinTokens.Should().HaveCount(goblinsBefore + 1,
            "CR 111 — Rabblemaster's attack trigger creates exactly one 1/1 Goblin token.");
        var token = goblinTokens.Last();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);

        // Pump: only Rabblemaster is attacking (Goblin, you-controlled) →
        // +1/+0 EOT. No "other" qualifier, so Rabblemaster itself counts.
        rab.GetPower().Should().Be(3,
            "1 attacking Goblin you control (Rabblemaster itself) → +1/+0 EOT → 2 + 1 = 3.");
        rab.GetToughness().Should().Be(2, "+0 toughness from the rider.");
    }

    [Fact]
    public void GoblinRabblemaster_AttackTrigger_ThreeOtherGoblins_PumpsFour()
    {
        var svc = new ContinuousEffectsService();

        var g1 = MakeGoblin(_alice, "Mogg Fanatic");
        var g2 = MakeGoblin(_alice, "Goblin Bushwhacker");
        var g3 = MakeGoblin(_alice, "Skirk Prospector");
        g1.ActiveEffects = svc;
        g2.ActiveEffects = svc;
        g3.ActiveEffects = svc;

        var attackers = new List<Creature>();
        var rab = GoblinRabblemasterFactory.Create(
            _alice,
            continuousEffects: null,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        rab.SetZone(ZoneType.Battlefield);
        rab.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(rab);

        attackers.AddRange(new[] { rab, g1, g2, g3 });

        var trigger = GetAttackTrigger(rab);
        foreach (var e in trigger.Effects) e.Execute();

        // 4 attacking Goblins Alice controls (Rabblemaster + 3 others) →
        // +4/+0 EOT → 2 + 4 = 6 power.
        rab.GetPower().Should().Be(6,
            "4 attacking Goblins you control (Rabblemaster + 3 others) → +4/+0 EOT → 2 + 4 = 6.");
        rab.GetToughness().Should().Be(2);

        // Token still created.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken)
            .Should().Be(1, "the attack trigger creates exactly one 1/1 Goblin token.");
    }

    [Fact]
    public void GoblinRabblemaster_AttackTrigger_OpponentGoblinAttackerDoesNotCount()
    {
        var svc = new ContinuousEffectsService();

        // Opponent's attacking Goblin — should NOT contribute to Alice's
        // Rabblemaster pump ("each attacking Goblin you control" — CR 109.5).
        var oppGoblin = MakeGoblin(_bob);
        oppGoblin.ActiveEffects = svc;

        var ownGoblin = MakeGoblin(_alice, "Goblin Guide");
        ownGoblin.ActiveEffects = svc;

        var attackers = new List<Creature>();
        var rab = GoblinRabblemasterFactory.Create(
            _alice,
            continuousEffects: null,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        rab.SetZone(ZoneType.Battlefield);
        rab.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(rab);

        attackers.AddRange(new[] { rab, ownGoblin, oppGoblin });

        var trigger = GetAttackTrigger(rab);
        foreach (var e in trigger.Effects) e.Execute();

        // 2 attacking Goblins Alice controls (Rabblemaster + ownGoblin) —
        // opponent's Goblin is excluded by the controller filter.
        rab.GetPower().Should().Be(4,
            "2 attacking Goblins Alice controls (Rabblemaster + ownGoblin); opponent's Goblin excluded → 2 + 2 = 4.");
    }

    [Fact]
    public void GoblinRabblemaster_SingleArgDispatcher_NoOpPumpBody()
    {
        // The single-arg path doesn't wire an attackers source — the pump
        // body short-circuits and Rabblemaster stays at base P/T even
        // after the trigger effect runs. Token creation still happens but
        // requires a controller battlefield zone; with no zoneService it
        // routes through the Library sentinel + direct add, which is fine.
        var svc = new ContinuousEffectsService();

        var rab = GoblinRabblemasterFactory.Create(_alice);
        rab.SetZone(ZoneType.Battlefield);
        rab.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(rab);

        var trigger = GetAttackTrigger(rab);
        foreach (var e in trigger.Effects) e.Execute();

        rab.GetPower().Should().Be(2,
            "no attackers source — pump body is a no-op (shape-only path).");
        rab.GetToughness().Should().Be(2);

        // Token is still created — the token half is unconditional.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken)
            .Should().Be(1, "the token half runs regardless of attackers-source wiring.");
    }
}
