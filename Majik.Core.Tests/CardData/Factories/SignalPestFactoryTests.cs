using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SignalPestFactory"/>.
///
/// Card: Signal Pest — Artifact Creature — Pest {1} 0/1 (Mirrodin Besieged).
///   "Signal Pest can't be blocked except by creatures with flying or reach.
///    Signal Pest gets +1/+0 for each other attacking creature."
///
/// Covers:
/// - Identity (name, mana cost, Artifact + Creature types, Pest subtype,
///   0/1, owner/controller).
/// - NamedCardFactory dispatch.
/// - Attack trigger condition matches CreatureAttacksEvent for this card
///   only.
/// - Attack-trigger pump body: "+1/+0 for each other attacking creature"
///   — sources the attackers list via the injected closure, excludes self,
///   includes non-Pest / non-artifact attackers (the oracle has no subtype
///   filter), registers a PumpUntilEndOfTurnEffect against the layers
///   service.
/// - Zero other attackers → no pump.
/// - Three other attackers → +3/+0.
/// - Single-arg dispatcher path is a no-op pump (no attackers source).
/// </summary>
public class SignalPestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeAttacker(Player owner, string name, CardSubtype subtype)
    {
        var c = new Creature(name, "R", 2, 2, subtypes: new[] { subtype });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    [Fact]
    public void SignalPest_Identity()
    {
        var c = SignalPestFactory.Create(_alice);

        c.Name.Should().Be("Signal Pest");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Signal Pest is an Artifact Creature — both types must be flagged.");
        c.HasSubtype(CardSubtype.Pest).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SignalPest_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Signal Pest", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Signal Pest");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Pest).Should().BeTrue();
    }

    [Fact]
    public void SignalPest_HasAttackTrigger_MatchesSelfOnly()
    {
        var c = SignalPestFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(c);

        trigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "Signal Pest's boost is wired as an OnAttackSelf trigger.");

        var other = MakeAttacker(_alice, "Mogg Fanatic", CardSubtype.Goblin);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the per-attacker trigger only fires for Signal Pest itself.");
    }

    [Fact]
    public void SignalPest_AttackTrigger_PumpsPerOtherAttacker()
    {
        var svc = new ContinuousEffectsService();
        var attackers = new List<Creature>();
        var pest = SignalPestFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        pest.SetZone(ZoneType.Battlefield);
        pest.ActiveEffects = svc;

        var g1 = MakeAttacker(_alice, "Memnite", CardSubtype.Construct);
        var g2 = MakeAttacker(_alice, "Ornithopter", CardSubtype.Thopter);
        var g3 = MakeAttacker(_alice, "Vault Skirge", CardSubtype.Imp);

        attackers.AddRange(new[] { pest, g1, g2, g3 });

        var trigger = GetAttackTrigger(pest);
        foreach (var e in trigger.Effects) e.Execute();

        pest.GetPower().Should().Be(0 + 3,
            "3 other attackers → +3/+0 EOT → 0 + 3 = 3 power.");
        pest.GetToughness().Should().Be(1, "+0 toughness from the rider.");
    }

    [Fact]
    public void SignalPest_AttackTrigger_ZeroOtherAttackers_NoPump()
    {
        var svc = new ContinuousEffectsService();
        var attackers = new List<Creature>();
        var pest = SignalPestFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        pest.SetZone(ZoneType.Battlefield);
        pest.ActiveEffects = svc;

        attackers.Add(pest); // solo attacker.

        var trigger = GetAttackTrigger(pest);
        foreach (var e in trigger.Effects) e.Execute();

        pest.GetPower().Should().Be(0, "no other attackers → no pump.");
        pest.GetToughness().Should().Be(1);
    }

    [Fact]
    public void SignalPest_AttackTrigger_ExcludesSelfFromCount()
    {
        var svc = new ContinuousEffectsService();
        var attackers = new List<Creature>();
        var pest = SignalPestFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        pest.SetZone(ZoneType.Battlefield);
        pest.ActiveEffects = svc;

        attackers.Add(pest);
        attackers.Add(pest); // duplicated reference — still must be excluded twice.

        var trigger = GetAttackTrigger(pest);
        foreach (var e in trigger.Effects) e.Execute();

        pest.GetPower().Should().Be(0,
            "self is excluded from the 'other attacking creature' count.");
    }

    [Fact]
    public void SignalPest_AttackTrigger_AnySubtypeCounts()
    {
        // No subtype filter — Bears, Goblins, Constructs all count.
        var svc = new ContinuousEffectsService();
        var attackers = new List<Creature>();
        var pest = SignalPestFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        pest.SetZone(ZoneType.Battlefield);
        pest.ActiveEffects = svc;

        var bear = MakeAttacker(_alice, "Grizzly Bears", CardSubtype.Bear);
        var goblin = MakeAttacker(_alice, "Mogg Fanatic", CardSubtype.Goblin);

        attackers.AddRange(new[] { pest, bear, goblin });

        var trigger = GetAttackTrigger(pest);
        foreach (var e in trigger.Effects) e.Execute();

        pest.GetPower().Should().Be(0 + 2,
            "2 other attackers (any subtype) → +2/+0.");
    }

    [Fact]
    public void SignalPest_SingleArgDispatcher_NoOpPumpBody()
    {
        var svc = new ContinuousEffectsService();
        var pest = SignalPestFactory.Create(_alice);
        pest.SetZone(ZoneType.Battlefield);
        pest.ActiveEffects = svc;

        var trigger = GetAttackTrigger(pest);
        foreach (var e in trigger.Effects) e.Execute();

        pest.GetPower().Should().Be(0,
            "no attackers source — pump body is a no-op (shape-only path).");
        pest.GetToughness().Should().Be(1);
    }
}
