using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LordSkitterSewerKingFactory"/> (Bloomburrow, {2}{B}).
/// Legendary Creature — Rat Noble, 3/3:
///   "Whenever another Rat you control enters, exile up to one target card
///    from an opponent's graveyard.
///    At the beginning of combat on your turn, create a 1/1 black Rat creature
///    token with "This token can't block.""
///
/// Covers:
/// - Identity (Legendary Creature, Rat + Noble subtypes, {2}{B}, 3/3,
///   owner/controller).
/// - Another-Rat-enters trigger: fires on another Rat the controller controls
///   entering (NOT on a non-Rat, NOT on Lord Skitter itself); a 0..1
///   "target card in an opponent's graveyard" TargetRequest; resolution exiles
///   the chosen graveyard card (and no-ops on up-to-zero / illegal target).
/// - Begin-combat trigger: fires only on the controller's combat step; creates
///   a 1/1 black Rat token carrying the "CantBlock" marker.
/// </summary>
[Trait("Color", "B")]
public class LordSkitterSewerKingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(
        Player owner, string name, CardSubtype subtype)
    {
        var c = new Creature(name, "B", 1, 1, subtypes: new[] { subtype });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetEntersTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static TriggeredAbility GetBeginCombatTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void LordSkitter_Identity()
    {
        var c = LordSkitterSewerKingFactory.Create(_alice);

        c.Name.Should().Be("Lord Skitter, Sewer King");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Another-Rat-enters trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void LordSkitter_EntersTrigger_FiresOnAnotherRatYouControl_Only()
    {
        var c = LordSkitterSewerKingFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetEntersTrigger(c);

        var rat = MakeCreature(_alice, "Sewer Rat", CardSubtype.Rat);
        trigger.IsTriggered(new CardMovedEvent(rat, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeTrue("another Rat the controller controls entering triggers it.");

        // Not a Rat → no trigger.
        var goblin = MakeCreature(_alice, "Goblin", CardSubtype.Goblin);
        trigger.IsTriggered(new CardMovedEvent(goblin, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeFalse("only another *Rat* entering triggers it.");

        // Lord Skitter itself → no trigger ("another" Rat, CR 603.6e).
        trigger.IsTriggered(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeFalse("'another' Rat — Lord Skitter entering does not trigger itself.");

        // An opponent's Rat → no trigger ("you control").
        var enemyRat = MakeCreature(_bob, "Enemy Rat", CardSubtype.Rat);
        trigger.IsTriggered(new CardMovedEvent(enemyRat, ZoneType.Hand, ZoneType.Battlefield))
            .Should().BeFalse("'you control' — an opponent's Rat does not trigger it.");
    }

    [Fact]
    public void LordSkitter_EntersTrigger_HasUpToOneGraveyardTargetRequest()
    {
        var c = LordSkitterSewerKingFactory.Create(_alice);
        var trigger = GetEntersTrigger(c);

        trigger.TargetRequests.Should().ContainSingle();
        var req = trigger.TargetRequests[0];
        req.MinTargets.Should().Be(0, "'up to one target' — optional target (CR 115.1a).");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("graveyard");
    }

    [Fact]
    public void LordSkitter_EntersTrigger_ExilesChosenGraveyardCard()
    {
        var c = LordSkitterSewerKingFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        // A card sitting in Bob's graveyard.
        var dead = new Creature("Grave Rat", "B", 1, 1);
        dead.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(dead);
        dead.SetZone(ZoneType.Graveyard);

        var trigger = GetEntersTrigger(c);
        trigger.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
            { new object[] { dead } });
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().NotContain(dead,
            "the chosen card is exiled from the opponent's graveyard.");
        _bob.Zones.Exile.GetCards().Should().Contain(dead);
        dead.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void LordSkitter_EntersTrigger_UpToZero_NoOps()
    {
        var c = LordSkitterSewerKingFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var dead = new Creature("Grave Rat", "B", 1, 1);
        dead.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(dead);
        dead.SetZone(ZoneType.Graveyard);

        // "up to one" — chose zero targets; nothing is exiled.
        var trigger = GetEntersTrigger(c);
        trigger.SetChosenTargets(new System.Collections.Generic.IReadOnlyList<object>[]
            { System.Array.Empty<object>() });
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().Contain(dead,
            "choosing no target leaves the graveyard untouched.");
        dead.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Begin-combat token trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void LordSkitter_BeginCombatTrigger_FiresOnControllerCombatStepOnly()
    {
        var c = LordSkitterSewerKingFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetBeginCombatTrigger(c);

        trigger.IsTriggered(new StepStartedEvent(StepStateType.BeginningOfCombat, _alice))
            .Should().BeTrue("fires at the beginning of combat on the controller's turn.");
        trigger.IsTriggered(new StepStartedEvent(StepStateType.BeginningOfCombat, _bob))
            .Should().BeFalse("'on your turn' — not on the opponent's combat.");
        trigger.IsTriggered(new StepStartedEvent(StepStateType.Upkeep, _alice))
            .Should().BeFalse("only the beginning-of-combat step triggers it.");
    }

    [Fact]
    public void LordSkitter_BeginCombatTrigger_CreatesBlackRatTokenThatCantBlock()
    {
        var c = LordSkitterSewerKingFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var trigger = GetBeginCombatTrigger(c);
        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken && t.HasSubtype(CardSubtype.Rat))
            .ToList();

        tokens.Should().HaveCount(1,
            "CR 111 — the begin-combat trigger creates exactly one 1/1 Rat token.");
        var token = tokens[0];
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(token).Should().Contain(Majik.Core.ValueObjects.ManaColor.Black,
            "'1/1 black Rat creature token'.");

        // "This token can't block." — CantBlock marker enforced by CombatValidator.
        CombatAbilities.HasCantBlock(token).Should().BeTrue(
            "the token has \"This token can't block.\" (CR 509.1a).");
    }
}
