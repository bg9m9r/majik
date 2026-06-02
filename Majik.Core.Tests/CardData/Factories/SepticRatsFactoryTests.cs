using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SepticRatsFactory"/>.
///
/// Card (New Phyrexia, {1}{B}{B}), Creature — Phyrexian Rat 2/2. Oracle text
/// (verified against Scryfall 2026-06-02):
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)
///    Whenever this creature attacks, if defending player is poisoned, it gets
///    +1/+1 until end of turn."
///
/// Covers:
/// - Identity (name, {1}{B}{B}, Phyrexian + Rat subtypes, 2/2, owner/controller).
/// - Infect keyword marker (CR 702.90).
/// - NamedCardFactory dispatch.
/// - Attack trigger present, keyed on this creature attacking.
/// - Intervening-if (CR 603.4): defender poisoned -> stack; not poisoned -> no.
/// - Resolution registers a +1/+1-until-EOT pump when the defender is poisoned,
///   and the pump expires at end of turn.
/// </summary>
[Trait("Color", "B")]
public class SepticRatsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void SepticRats_Identity()
    {
        var c = SepticRatsFactory.Create(_alice);

        c.Name.Should().Be("Septic Rats");
        c.ManaCost.Should().Be("{1}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SepticRats_HasInfect()
    {
        var c = SepticRatsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Infect",
                "Infect (CR 702.90) marker routes combat damage to poison / " +
                "-1/-1 counters once that primitive lands.");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SepticRats()
    {
        var card = NamedCardFactory.Create("Septic Rats", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Septic Rats");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack-trigger pump is attached for inspection.");
    }

    // ── Attack trigger ───────────────────────────────────────────────────

    [Fact]
    public void AttackTrigger_FiresOnSelfAttack()
    {
        var rats = SepticRatsFactory.Create(_alice);
        rats.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(rats);
        trigger.IsTriggered(new CreatureAttacksEvent(rats, _bob)).Should().BeTrue(
            "the pump triggers when Septic Rats itself attacks.");
    }

    [Fact]
    public void AttackTrigger_DoesNotFireOnOtherAttacker()
    {
        var rats = SepticRatsFactory.Create(_alice);
        rats.SetZone(ZoneType.Battlefield);

        var other = new Creature("Grizzly Bears", "1G", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        other.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(rats);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the pump keys on Septic Rats attacking, not other creatures.");
    }

    // ── Intervening-if (CR 603.4) ────────────────────────────────────────

    [Fact]
    public void InterveningIf_DefenderPoisoned_AllowsStack()
    {
        _bob.AddPoisonCounters(1); // CR 122.3 — one poison counter = poisoned.

        var rats = SepticRatsFactory.Create(
            _alice, triggers: null, defendingPlayerSource: () => _bob, effects: null);
        rats.SetZone(ZoneType.Battlefield);

        GetAttackTrigger(rats).CanBePutOnStack().Should().BeTrue(
            "the defending player is poisoned, so the intervening-if is met (CR 603.4).");
    }

    [Fact]
    public void InterveningIf_DefenderNotPoisoned_BlocksStack()
    {
        // Bob has zero poison counters.
        var rats = SepticRatsFactory.Create(
            _alice, triggers: null, defendingPlayerSource: () => _bob, effects: null);
        rats.SetZone(ZoneType.Battlefield);

        GetAttackTrigger(rats).CanBePutOnStack().Should().BeFalse(
            "the defending player is not poisoned, so the intervening-if fails (CR 603.4).");
    }

    [Fact]
    public void InterveningIf_NoDefender_BlocksStack()
    {
        var rats = SepticRatsFactory.Create(_alice);
        rats.SetZone(ZoneType.Battlefield);

        GetAttackTrigger(rats).CanBePutOnStack().Should().BeFalse(
            "with no defending player snapshot the condition cannot be met.");
    }

    // ── Resolution — +1/+1 until end of turn ─────────────────────────────

    [Fact]
    public void Resolution_DefenderPoisoned_PumpsPlusOnePlusOneUntilEndOfTurn()
    {
        _bob.AddPoisonCounters(3); // poisoned (CR 122.3).

        var svc = new ContinuousEffectsService();
        var rats = SepticRatsFactory.Create(
            _alice, triggers: null, defendingPlayerSource: () => _bob, effects: svc);
        rats.SetZone(ZoneType.Battlefield);
        rats.ActiveEffects = svc;

        var trigger = GetAttackTrigger(rats);
        foreach (var e in trigger.Effects) e.Execute();

        rats.GetPower().Should().Be(3, "2/2 + 1/+1 = 3 power until end of turn.");
        rats.GetToughness().Should().Be(3, "2/2 + 1/+1 = 3 toughness until end of turn.");

        svc.ExpireEndOfTurn();

        rats.GetPower().Should().Be(2, "the +1/+1 pump expires in the cleanup step (CR 514.2).");
        rats.GetToughness().Should().Be(2, "the +1/+1 pump expires in the cleanup step (CR 514.2).");
    }

    [Fact]
    public void Resolution_DefenderNotPoisoned_IsNoOp()
    {
        // Bob has zero poison counters: intervening-if re-checked on resolution
        // (CR 603.4) — no pump.
        var svc = new ContinuousEffectsService();
        var rats = SepticRatsFactory.Create(
            _alice, triggers: null, defendingPlayerSource: () => _bob, effects: svc);
        rats.SetZone(ZoneType.Battlefield);
        rats.ActiveEffects = svc;

        var trigger = GetAttackTrigger(rats);
        foreach (var e in trigger.Effects) e.Execute();

        rats.GetPower().Should().Be(2, "no pump when the defender isn't poisoned at resolution.");
        rats.GetToughness().Should().Be(2, "no pump when the defender isn't poisoned at resolution.");
    }
}
