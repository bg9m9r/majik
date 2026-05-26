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
/// Tests for <see cref="SoaringThoughtThiefFactory"/> (Zendikar Rising, {1}{U}).
///
/// Covers:
/// - Identity (name, type, mana cost, Faerie + Rogue subtypes, 1/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Flying keyword marker on self.
/// - Lord rider: "Other Rogues you control have flying" via
///   <see cref="LordStaticEffect"/> — keyword-only grant, includeSelf:false,
///   controller-scoped.
/// - Attack-with-Rogues trigger condition:
///     * Fires on AttackersDeclaredEvent when controller is the attacker
///       AND any attacker is a Rogue.
///     * Does NOT fire when none of the attackers are Rogues.
///     * Does NOT fire when an opponent (not the controller) declares
///       attackers.
/// - Trigger body: target opponent mills 2 (CR 701.13b).
/// - Stocking the library short — empty-library tolerated by MillAction.
/// </summary>
public class SoaringThoughtThiefTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeRogue(Player owner, string name = "Royal Assassin")
    {
        var c = new Creature(name, "{1}{B}", 1, 1, subtypes: new[] { CardSubtype.Rogue });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeBear(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void StockLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var card = new Card($"Filler {i}", "");
            card.SetOwner(p);
            card.SetController(p);
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<AttackersDeclaredEvent>);

    [Fact]
    public void SoaringThoughtThief_Identity()
    {
        var c = SoaringThoughtThiefFactory.Create(_alice);

        c.Name.Should().Be("Soaring Thought-Thief");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SoaringThoughtThief_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Soaring Thought-Thief", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Soaring Thought-Thief");
        card.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    [Fact]
    public void SoaringThoughtThief_HasFlying()
    {
        var c = SoaringThoughtThiefFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void SoaringThoughtThief_GrantsFlyingToOtherRoguesYouControl()
    {
        var svc = new ContinuousEffectsService();

        var rogue = MakeRogue(_alice);
        rogue.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(rogue);

        var thief = SoaringThoughtThiefFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: null);
        thief.SetZone(ZoneType.Battlefield);
        thief.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(thief);

        svc.Compute(rogue).Keywords.Should().Contain("Flying",
            "CR 613.1f — other Rogues you control gain Flying via the lord static.");
    }

    [Fact]
    public void SoaringThoughtThief_DoesNotGrantFlyingToOpponentRogues()
    {
        var svc = new ContinuousEffectsService();

        var oppRogue = MakeRogue(_bob);
        oppRogue.ActiveEffects = svc;
        _bob.Zones.Battlefield.AddCard(oppRogue);

        var thief = SoaringThoughtThiefFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: null);
        thief.SetZone(ZoneType.Battlefield);
        thief.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(thief);

        svc.Compute(oppRogue).Keywords.Should().NotContain("Flying",
            "Lord static is scoped to the controller (CR 109.5 — 'you').");
    }

    [Fact]
    public void SoaringThoughtThief_DoesNotGrantFlyingToNonRogues()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeBear(_alice);
        bear.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(bear);

        var thief = SoaringThoughtThiefFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: null);
        thief.SetZone(ZoneType.Battlefield);
        thief.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(thief);

        svc.Compute(bear).Keywords.Should().NotContain("Flying",
            "Only Rogues match the lord static's subtype filter.");
    }

    [Fact]
    public void SoaringThoughtThief_AttackTrigger_FiresWhenRogueAttacks()
    {
        var thief = SoaringThoughtThiefFactory.Create(_alice);
        thief.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(thief);

        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(thief, _bob));

        var evt = new AttackersDeclaredEvent(combat);
        trigger.IsTriggered(evt).Should().BeTrue(
            "Thought-Thief is a Rogue and Alice is the attacking player.");
    }

    [Fact]
    public void SoaringThoughtThief_AttackTrigger_DoesNotFireWithoutRogues()
    {
        var thief = SoaringThoughtThiefFactory.Create(_alice);
        thief.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(thief);

        var bear = MakeBear(_alice);
        var combat = new Majik.Core.Combat.Combat(_alice, _bob);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(bear, _bob));

        var evt = new AttackersDeclaredEvent(combat);
        trigger.IsTriggered(evt).Should().BeFalse(
            "No Rogues are attacking — the 'one or more Rogues' gate fails.");
    }

    [Fact]
    public void SoaringThoughtThief_AttackTrigger_DoesNotFireOnOpponentAttacks()
    {
        var thief = SoaringThoughtThiefFactory.Create(_alice);
        thief.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(thief);

        // Bob attacks Alice with a Rogue.
        var bobRogue = MakeRogue(_bob);
        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        combat.AddAttacker(new Majik.Core.Combat.Attacker(bobRogue, _alice));

        var evt = new AttackersDeclaredEvent(combat);
        trigger.IsTriggered(evt).Should().BeFalse(
            "CR 109.5 — 'you attack' = the controller of the trigger source is the attacking player.");
    }

    [Fact]
    public void SoaringThoughtThief_TriggerBody_MillsTwoFromTargetOpponent()
    {
        StockLibrary(_bob, 5);

        var thief = SoaringThoughtThiefFactory.Create(
            _alice,
            continuousEffects: null,
            triggers: null,
            allPlayersResolver: () => new List<Player> { _alice, _bob });
        thief.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(thief);
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Count().Should().Be(2,
            "CR 701.13b — target opponent mills two cards.");
        _bob.Zones.Library.GetCards().Count().Should().Be(3);
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(0,
            "controller is never the mill target.");
    }

    [Fact]
    public void SoaringThoughtThief_TriggerBody_NoOp_WithoutResolver()
    {
        StockLibrary(_bob, 5);

        var thief = SoaringThoughtThiefFactory.Create(_alice);
        thief.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(thief);
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Count().Should().Be(0,
            "no players resolver → mill body short-circuits cleanly.");
    }
}
