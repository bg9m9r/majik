using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Atarka, World Render (Dragons of Tarkir, {5}{R}{R},
/// Legendary Creature — Elder Dragon 6/4).
///
/// Covers:
/// - Identity (name, type, cost, P/T, supertype, subtypes).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying + Trample keyword markers wired.
/// - Attack trigger condition matches CreatureAttacksEvent for ANY Dragon
///   controlled by Atarka's controller (including Atarka itself).
/// - Attack trigger does NOT fire for non-Dragon attackers or
///   opponent-controlled Dragon attackers.
/// - Resolution grants Double strike EOT to every attacking Dragon
///   Atarka's controller controls.
/// </summary>
[Trait("Color", "R")]
public class AtarkaWorldRenderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeDragon(Player owner, string name = "Shivan Dragon")
    {
        var c = new Creature(name, "{4}{R}{R}", 5, 5, subtypes: new[] { CardSubtype.Dragon });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Creature MakeNonDragon(Player owner, string name = "Grizzly Bears")
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
    public void Identity_NameTypeCostPT()
    {
        var c = AtarkaWorldRenderFactory.Create(_alice);

        c.Name.Should().Be("Atarka, World Render");
        c.ManaCost.Should().Be("{5}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elder).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void HasFlyingAndTrampleKeywords()
    {
        var c = AtarkaWorldRenderFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Trample");
    }

    [Fact]
    public void AttackTrigger_MatchesDragonControllerYouControl()
    {
        var atarka = AtarkaWorldRenderFactory.Create(_alice);
        atarka.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(atarka);

        // Self-attack: fires (Atarka is a Dragon Alice controls).
        trigger.IsTriggered(new CreatureAttacksEvent(atarka, _bob)).Should().BeTrue(
            "Atarka's own attack matches 'a Dragon you control attacks'.");

        // Another Dragon Alice controls: fires.
        var alliedDragon = MakeDragon(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(alliedDragon, _bob)).Should().BeTrue(
            "Another Dragon you control matches the trigger.");
    }

    [Fact]
    public void AttackTrigger_DoesNotMatchNonDragonAttacker()
    {
        var atarka = AtarkaWorldRenderFactory.Create(_alice);
        atarka.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(atarka);

        var bear = MakeNonDragon(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(bear, _bob)).Should().BeFalse(
            "non-Dragon attackers do not match the trigger.");
    }

    [Fact]
    public void AttackTrigger_DoesNotMatchOpponentControlledDragon()
    {
        var atarka = AtarkaWorldRenderFactory.Create(_alice);
        atarka.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(atarka);

        var oppDragon = MakeDragon(_bob);
        trigger.IsTriggered(new CreatureAttacksEvent(oppDragon, _alice)).Should().BeFalse(
            "an opponent-controlled Dragon attacking does not match (CR 109.5 'you').");
    }

    [Fact]
    public void Resolution_GrantsDoubleStrikeToAttackingDragonsYouControl()
    {
        var svc = new ContinuousEffectsService();
        var attackers = new List<Creature>();

        var atarka = AtarkaWorldRenderFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        atarka.SetZone(ZoneType.Battlefield);
        atarka.ActiveEffects = svc;

        var alliedDragon = MakeDragon(_alice);
        alliedDragon.ActiveEffects = svc;

        var alliedBear = MakeNonDragon(_alice);
        alliedBear.ActiveEffects = svc;

        var oppDragon = MakeDragon(_bob);
        oppDragon.ActiveEffects = svc;

        attackers.AddRange(new[] { atarka, alliedDragon, alliedBear, oppDragon });

        var trigger = GetAttackTrigger(atarka);
        foreach (var e in trigger.Effects) e.Execute();

        CombatAbilities.HasDoubleStrike(atarka).Should().BeTrue(
            "Atarka itself is an attacking Dragon you control — gains Double strike.");
        CombatAbilities.HasDoubleStrike(alliedDragon).Should().BeTrue(
            "Allied attacking Dragon gains Double strike.");
        CombatAbilities.HasDoubleStrike(alliedBear).Should().BeFalse(
            "Non-Dragon attackers are not granted Double strike.");
        CombatAbilities.HasDoubleStrike(oppDragon).Should().BeFalse(
            "Opponent-controlled Dragons are not granted Double strike (CR 109.5).");
    }

    [Fact]
    public void Resolution_IsNoOp_WhenNoAttackersSourceWired()
    {
        var svc = new ContinuousEffectsService();
        var atarka = AtarkaWorldRenderFactory.Create(_alice);
        atarka.SetZone(ZoneType.Battlefield);
        atarka.ActiveEffects = svc;

        var trigger = GetAttackTrigger(atarka);

        // No attackers-source supplied — the effect body is a no-op.
        var act = () =>
        {
            foreach (var e in trigger.Effects) e.Execute();
        };
        act.Should().NotThrow();
        CombatAbilities.HasDoubleStrike(atarka).Should().BeFalse(
            "no source → no grants registered.");
    }
}
