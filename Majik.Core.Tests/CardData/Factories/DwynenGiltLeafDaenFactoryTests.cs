using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Dwynen, Gilt-Leaf Daen (Magic Origins, {2}{G}{G},
/// Legendary Creature — Elf Warrior 3/4). Oracle text (verified against
/// Scryfall 2026-06-14):
///   "Reach (This creature can block creatures with flying.)
///    Other Elf creatures you control get +1/+1.
///    Whenever Dwynen attacks, you gain 1 life for each attacking Elf you control."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, cost, P/T, supertype + subtypes).
/// - Reach keyword marker (CR 702.9) materialised from the JSON definition.
/// - Lord static (CR 613.7c): other controller-Elves get +1/+1; Dwynen itself
///   is NOT pumped (includeSelf: false); opponent Elves / non-Elves unaffected.
/// - Attack trigger (CR 508.1f): fires only on Dwynen's own attack.
/// - Resolution gains 1 life per attacking Elf the controller controls
///   (including Dwynen itself), ignoring non-Elves and opponent Elves (CR 109.5).
/// </summary>
[Trait("Color", "G")]
public class DwynenGiltLeafDaenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeElf(Player owner, string name = "Llanowar Elves")
    {
        var c = new Creature(name, "{G}", 1, 1, subtypes: new[] { CardSubtype.Elf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonElf(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2, subtypes: new[] { CardSubtype.Bear });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    [Fact]
    public void Dwynen_Identity()
    {
        var c = DwynenGiltLeafDaenFactory.Create(_alice);

        c.Name.Should().Be("Dwynen, Gilt-Leaf Daen");
        c.ManaCost.Should().Be("{2}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Dwynen_HasReachKeyword()
    {
        var c = DwynenGiltLeafDaenFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Reach",
                "Reach rides on the JSON definition and is materialised as a KeywordAbility marker (CR 702.9).");
    }

    // ── Lord static ─────────────────────────────────────────────────────

    [Fact]
    public void Dwynen_BuffsOtherControllerElf_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherElf = MakeElf(_alice, "Llanowar Elves");
        otherElf.ActiveEffects = svc;

        var dwynen = DwynenGiltLeafDaenFactory.Create(_alice, svc, triggers: null, attackingCreaturesSource: null);
        dwynen.SetZone(ZoneType.Battlefield);
        dwynen.ActiveEffects = svc;

        otherElf.GetPower().Should().Be(2,
            "other Elves controlled by Dwynen's controller get +1/+1 (1 → 2 power).");
        otherElf.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Dwynen_DoesNotBuffItself()
    {
        var svc = new ContinuousEffectsService();

        var dwynen = DwynenGiltLeafDaenFactory.Create(_alice, svc, triggers: null, attackingCreaturesSource: null);
        dwynen.SetZone(ZoneType.Battlefield);
        dwynen.ActiveEffects = svc;

        dwynen.GetPower().Should().Be(3,
            "printed 'Other Elf creatures' excludes Dwynen itself (CR 613.1g).");
        dwynen.GetToughness().Should().Be(4);
    }

    [Fact]
    public void Dwynen_DoesNotBuffOpponentElf()
    {
        var svc = new ContinuousEffectsService();

        var bobElf = MakeElf(_bob, "Heritage Druid");
        bobElf.ActiveEffects = svc;

        var dwynen = DwynenGiltLeafDaenFactory.Create(_alice, svc, triggers: null, attackingCreaturesSource: null);
        dwynen.SetZone(ZoneType.Battlefield);
        dwynen.ActiveEffects = svc;

        bobElf.GetPower().Should().Be(1,
            "controller-scoped lord — Bob's Elves are unaffected (allPlayers: false).");
        bobElf.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Dwynen_DoesNotBuffNonElfCreature()
    {
        var svc = new ContinuousEffectsService();

        var bears = MakeNonElf(_alice);
        bears.ActiveEffects = svc;

        var dwynen = DwynenGiltLeafDaenFactory.Create(_alice, svc, triggers: null, attackingCreaturesSource: null);
        dwynen.SetZone(ZoneType.Battlefield);
        dwynen.ActiveEffects = svc;

        bears.GetPower().Should().Be(2,
            "matching subtype = Elf only; non-Elf creatures aren't buffed.");
        bears.GetToughness().Should().Be(2);
    }

    // ── Attack trigger ──────────────────────────────────────────────────

    [Fact]
    public void AttackTrigger_FiresOnlyOnDwynensOwnAttack()
    {
        var dwynen = DwynenGiltLeafDaenFactory.Create(_alice);
        dwynen.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(dwynen);

        trigger.IsTriggered(new CreatureAttacksEvent(dwynen, _bob)).Should().BeTrue(
            "'Whenever Dwynen attacks' fires on Dwynen's own attack (CR 508.1f).");

        var otherElf = MakeElf(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(otherElf, _bob)).Should().BeFalse(
            "the trigger keys on Dwynen itself — another Elf attacking does not fire it.");
    }

    [Fact]
    public void Resolution_GainsOneLifePerAttackingElfYouControl()
    {
        var attackers = new List<Creature>();

        var dwynen = DwynenGiltLeafDaenFactory.Create(
            _alice,
            continuousEffects: null,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        dwynen.SetZone(ZoneType.Battlefield);

        var alliedElf = MakeElf(_alice, "Llanowar Elves");
        var alliedBear = MakeNonElf(_alice);
        var oppElf = MakeElf(_bob, "Heritage Druid");

        // Attacking set: Dwynen + 1 allied Elf (count), 1 allied non-Elf (skip),
        // 1 opponent Elf (skip) → 2 attacking Elves Alice controls.
        attackers.AddRange(new[] { dwynen, alliedElf, alliedBear, oppElf });

        var trigger = GetAttackTrigger(dwynen);
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(22,
            "1 life per attacking Elf Alice controls — Dwynen + Llanowar = 2 (CR 119.3); "
            + "non-Elf and opponent Elf excluded (CR 109.5).");
    }

    [Fact]
    public void Resolution_IsNoOp_WhenNoAttackersSourceWired()
    {
        var dwynen = DwynenGiltLeafDaenFactory.Create(_alice);
        dwynen.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(dwynen);

        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };
        act.Should().NotThrow();
        _alice.LifeTotal.Should().Be(20, "no attackers source → no life gained.");
    }
}
