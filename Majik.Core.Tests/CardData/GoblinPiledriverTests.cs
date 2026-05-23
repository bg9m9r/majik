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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="GoblinPiledriverFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Goblin + Warrior subtypes, 1/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Protection-from-blue rider (CR 702.16) attached.
/// - Attack trigger condition matches CreatureAttacksEvent for this card
///   only.
/// - Attack-trigger pump body (CR 508.1f): "+2/+0 for each other attacking
///   Goblin" — sources the attackers list via the injected closure,
///   excludes self, filters non-Goblins, registers a
///   PumpUntilEndOfTurnEffect against the layers service.
/// - Zero other attacking Goblins → no pump.
/// - Two other attacking Goblins → +4/+0.
/// - Non-Goblin attackers don't count.
/// - Self is excluded from the count.
/// - Single-arg dispatcher path is a no-op pump (no attackers source).
/// </summary>
public class GoblinPiledriverTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeAttackingGoblin(Player owner, string name = "Mogg Fanatic")
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
    public void GoblinPiledriver_Identity()
    {
        var c = GoblinPiledriverFactory.Create(_alice);

        c.Name.Should().Be("Goblin Piledriver");
        c.ManaCost.Should().Be("{1}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GoblinPiledriver_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Goblin Piledriver", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Goblin Piledriver");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void GoblinPiledriver_HasProtectionFromBlue()
    {
        var c = GoblinPiledriverFactory.Create(_alice);

        var prot = c.Abilities.OfType<ProtectionAbility>().ToList();
        prot.Should().ContainSingle(
            "CR 702.16 — Protection from blue is the printed first line.");
        prot[0].Quality.Should().Be("blue",
            "Quality is stored normalised lowercase.");
    }

    [Fact]
    public void GoblinPiledriver_HasAttackTrigger_MatchesSelfOnly()
    {
        var c = GoblinPiledriverFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(c);

        // Matches when Piledriver attacks.
        trigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "CR 508.1f per-attacker self-match.");

        // Doesn't match other attackers.
        var other = MakeAttackingGoblin(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the per-attacker trigger only fires for Piledriver itself.");
    }

    [Fact]
    public void GoblinPiledriver_AttackTrigger_PumpsForEachOtherAttackingGoblin()
    {
        var svc = new ContinuousEffectsService();

        var pile = GoblinPiledriverFactory.Create(_alice);
        pile.SetZone(ZoneType.Battlefield);
        pile.ActiveEffects = svc;

        // 3 other attacking goblins.
        var g1 = MakeAttackingGoblin(_alice, "Mogg Fanatic");
        var g2 = MakeAttackingGoblin(_alice, "Goblin Bushwhacker");
        var g3 = MakeAttackingGoblin(_alice, "Skirk Prospector");
        g1.ActiveEffects = svc;
        g2.ActiveEffects = svc;
        g3.ActiveEffects = svc;

        // Re-create Piledriver with the source closure wired — first build
        // was for identity-only assertions; this rebuild attaches the
        // attackers-source.
        IReadOnlyList<Creature> attackerSnapshot = new[] { pile, g1, g2, g3 };
        var pileWired = GoblinPiledriverFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackerSnapshot);
        pileWired.SetZone(ZoneType.Battlefield);
        pileWired.ActiveEffects = svc;

        // Patch the attacker snapshot to reference the wired Piledriver so
        // the self-exclusion check hits the right object.
        attackerSnapshot = new[] { pileWired, g1, g2, g3 };
        // Rewire the closure to read the patched snapshot — rebuild once
        // more with the updated list (closure captures by reference).
        var listBox = new List<Creature> { pileWired, g1, g2, g3 };
        var pile2 = GoblinPiledriverFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => listBox);
        pile2.SetZone(ZoneType.Battlefield);
        pile2.ActiveEffects = svc;
        listBox[0] = pile2; // self-reference correct now.

        // Resolve the attack trigger directly.
        var trigger = GetAttackTrigger(pile2);
        foreach (var e in trigger.Effects) e.Execute();

        pile2.GetPower().Should().Be(1 + 3 * 2,
            "3 other attacking Goblins → +6/+0 EOT → 1 + 6 = 7 power.");
        pile2.GetToughness().Should().Be(2, "+0 toughness from the rider.");
    }

    [Fact]
    public void GoblinPiledriver_AttackTrigger_ZeroOtherGoblins_NoPump()
    {
        var svc = new ContinuousEffectsService();

        Creature? pile = null;
        var attackers = new List<Creature>();
        pile = GoblinPiledriverFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        pile.SetZone(ZoneType.Battlefield);
        pile.ActiveEffects = svc;

        attackers.Add(pile); // only Piledriver itself attacking.

        var trigger = GetAttackTrigger(pile);
        foreach (var e in trigger.Effects) e.Execute();

        pile.GetPower().Should().Be(1, "no other attacking Goblins → no pump.");
        pile.GetToughness().Should().Be(2);
    }

    [Fact]
    public void GoblinPiledriver_AttackTrigger_NonGoblinAttackersDontCount()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        bear.ActiveEffects = svc;

        var goblin = MakeAttackingGoblin(_alice);
        goblin.ActiveEffects = svc;

        Creature? pile = null;
        var attackers = new List<Creature>();
        pile = GoblinPiledriverFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        pile.SetZone(ZoneType.Battlefield);
        pile.ActiveEffects = svc;

        attackers.Add(pile);
        attackers.Add(bear);   // non-Goblin — should NOT count.
        attackers.Add(goblin); // 1 other attacking Goblin → +2/+0.

        var trigger = GetAttackTrigger(pile);
        foreach (var e in trigger.Effects) e.Execute();

        pile.GetPower().Should().Be(1 + 2,
            "only 1 other attacking Goblin (the bear is excluded) → +2/+0.");
    }

    [Fact]
    public void GoblinPiledriver_AttackTrigger_ExcludesSelfFromCount()
    {
        // Sanity-check: if Piledriver were ITSELF a Goblin and weren't
        // self-excluded, a solo attack would yield +2/+0. The factory must
        // exclude self.
        var svc = new ContinuousEffectsService();

        Creature? pile = null;
        var attackers = new List<Creature>();
        pile = GoblinPiledriverFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        pile.SetZone(ZoneType.Battlefield);
        pile.ActiveEffects = svc;

        attackers.Add(pile); // self only — Goblin subtype but excluded.

        var trigger = GetAttackTrigger(pile);
        foreach (var e in trigger.Effects) e.Execute();

        pile.GetPower().Should().Be(1,
            "self is excluded from the 'other attacking Goblin' count.");
    }

    [Fact]
    public void GoblinPiledriver_SingleArgDispatcher_NoOpPumpBody()
    {
        // The single-arg path doesn't wire an attackers source — the pump
        // body short-circuits and Piledriver stays at base P/T even after
        // the trigger effect runs.
        var svc = new ContinuousEffectsService();

        var pile = GoblinPiledriverFactory.Create(_alice);
        pile.SetZone(ZoneType.Battlefield);
        pile.ActiveEffects = svc;

        var trigger = GetAttackTrigger(pile);
        foreach (var e in trigger.Effects) e.Execute();

        pile.GetPower().Should().Be(1,
            "no attackers source — pump body is a no-op (shape-only path).");
        pile.GetToughness().Should().Be(2);
    }
}
