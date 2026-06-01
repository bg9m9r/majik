using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GraveTitanFactory"/> (Magic 2011 / commander staple,
/// {4}{B}{B}). Creature — Giant, 6/6:
///   "Deathtouch
///    Whenever this creature enters or attacks, create two 2/2 black Zombie
///    creature tokens."
///
/// Covers:
/// - Identity (Creature, Giant subtype, {4}{B}{B}, 6/6, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Deathtouch keyword marker (CR 702.2).
/// - ETB trigger: fires when Grave Titan enters the battlefield (CR 603.6a);
///   resolution creates two 2/2 black Zombie tokens.
/// - Attack trigger: matches Grave Titan only (CR 508.1f self-match);
///   resolution creates two 2/2 black Zombie tokens.
/// </summary>
public class GraveTitanFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GraveTitan_Identity()
    {
        var c = GraveTitanFactory.Create(_alice);

        c.Name.Should().Be("Grave Titan");
        c.ManaCost.Should().Be("{4}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GraveTitan_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Grave Titan", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Grave Titan");
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
    }

    [Fact]
    public void GraveTitan_HasDeathtouch()
    {
        var c = GraveTitanFactory.Create(_alice);

        CombatAbilities.HasDeathtouch(c).Should().BeTrue(
            "CR 702.2 — Grave Titan has Deathtouch.");
    }

    // -----------------------------------------------------------------------
    // ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void GraveTitan_EtbTrigger_FiresOnSelfEntering()
    {
        var c = GraveTitanFactory.Create(_alice);

        var trigger = GetEtbTrigger(c);
        var cond = (EventTriggerCondition<CardMovedEvent>)trigger.Condition;

        // CR 603.6a — the ETB ability's existence is checked as the source
        // moves onto the battlefield (the source's zone has not yet been
        // gated to Battlefield), so the condition is matched directly here
        // (same posture as IngotChewerFactoryTests).
        cond.Matches(
            new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeTrue("CR 603.6a — 'whenever this creature enters' self-ETB.");

        var other = new Creature("Bear", "G", 2, 2);
        cond.Matches(
            new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeFalse("only Grave Titan's own ETB fires this trigger.");

        cond.Matches(
            new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard), trigger)
            .Should().BeFalse("leaving the battlefield is not an enter event.");
    }

    [Fact]
    public void GraveTitan_EtbTrigger_CreatesTwoBlackZombieTokens()
    {
        var titan = GraveTitanFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(titan);

        var trigger = GetEtbTrigger(titan);
        foreach (var e in trigger.Effects) e.Execute();

        AssertTwoZombieTokens(_alice);
    }

    // -----------------------------------------------------------------------
    // Attack trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void GraveTitan_AttackTrigger_MatchesSelfOnly()
    {
        var c = GraveTitanFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetAttackTrigger(c);

        trigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "CR 508.1f — 'whenever this creature attacks' self-match.");

        var other = new Creature("Bear", "G", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        trigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "the attack trigger only fires for Grave Titan itself.");
    }

    [Fact]
    public void GraveTitan_AttackTrigger_CreatesTwoBlackZombieTokens()
    {
        var titan = GraveTitanFactory.Create(_alice);
        titan.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(titan);

        var trigger = GetAttackTrigger(titan);
        foreach (var e in trigger.Effects) e.Execute();

        AssertTwoZombieTokens(_alice);
    }

    private static void AssertTwoZombieTokens(Player controller)
    {
        var tokens = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken && t.HasSubtype(CardSubtype.Zombie))
            .ToList();

        tokens.Should().HaveCount(2,
            "CR 111 — Grave Titan's trigger creates two 2/2 Zombie tokens.");
        foreach (var token in tokens)
        {
            token.BasePower.Should().Be(2);
            token.BaseToughness.Should().Be(2);
            token.Controller.Should().BeSameAs(controller);
            CardColors.GetColors(token).Should().Contain(
                Majik.Core.ValueObjects.ManaColor.Black,
                "CR 111.4 — '2/2 black Zombie creature token'.");
        }
    }
}
