using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MentorOfTheMeekFactory"/> (Innistrad, {2}{W}).
///
/// Card: Mentor of the Meek — Creature — Human Soldier 2/2.
/// Oracle: "Whenever another creature with power 2 or less enters under
/// your control, you may pay {1}. If you do, draw a card."
///
/// Covers:
/// - Identity (Creature — Human Soldier, {2}{W}, 2/2, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Trigger attached (single TriggeredAbility).
/// - Trigger condition predicate: a 1/1 token under the controller's
///   control entering matches; a 3/3 doesn't; the opponent's 1/1
///   doesn't; Mentor's own ETB doesn't trigger itself.
/// - Resolve effect: with the mana pool funded, pays {1} and draws a
///   card from the top of the controller's library.
/// - Resolve effect: with the mana pool empty, the trigger fizzles
///   (CR 117.5) without drawing.
/// </summary>
public class MentorOfTheMeekFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    [Fact]
    public void Mentor_Identity()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);

        m.Name.Should().Be("Mentor of the Meek");
        m.ManaCost.Should().Be("{2}{W}");
        m.HasType(CardType.Creature).Should().BeTrue();
        m.HasSubtype(CardSubtype.Human).Should().BeTrue();
        m.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        m.BasePower.Should().Be(2);
        m.BaseToughness.Should().Be(2);
        m.Owner.Should().BeSameAs(_alice);
        m.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Mentor_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mentor of the Meek", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Mentor of the Meek");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    [Fact]
    public void Mentor_HasExactlyOneTriggeredAbility()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);
        m.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Mentor_TriggerCondition_MatchesSmallCreatureEteringUnderControl()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);
        PutOnBattlefield(_alice, m);

        var trigger = m.Abilities.OfType<TriggeredAbility>().Single();

        // A 1/1 entering under Alice's control matches.
        var pawn = new Creature("Token", "{0}", 1, 1);
        pawn.SetOwner(_alice);
        pawn.SetController(_alice);
        var evt = new Majik.Core.Events.CardMovedEvent(
            pawn, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger)
            .Should().BeTrue("a 1/1 creature entering under controller's control matches");
    }

    [Fact]
    public void Mentor_TriggerCondition_DoesNotMatch_HighPower()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);
        PutOnBattlefield(_alice, m);

        var trigger = m.Abilities.OfType<TriggeredAbility>().Single();

        var bruiser = new Creature("Bruiser", "{2}{G}", 3, 3);
        bruiser.SetOwner(_alice);
        bruiser.SetController(_alice);
        var evt = new Majik.Core.Events.CardMovedEvent(
            bruiser, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger)
            .Should().BeFalse("BasePower 3 > 2 — printed gate is power 2 or less");
    }

    [Fact]
    public void Mentor_TriggerCondition_DoesNotMatch_OpponentsCreature()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);
        PutOnBattlefield(_alice, m);

        var trigger = m.Abilities.OfType<TriggeredAbility>().Single();

        var enemy = new Creature("Enemy", "{1}", 1, 1);
        enemy.SetOwner(_bob);
        enemy.SetController(_bob);
        var evt = new Majik.Core.Events.CardMovedEvent(
            enemy, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger)
            .Should().BeFalse("printed text gates on 'under YOUR control'");
    }

    [Fact]
    public void Mentor_TriggerCondition_DoesNotMatch_SelfEtb()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);
        // Don't put on battlefield yet — simulating Mentor's own ETB.

        var trigger = m.Abilities.OfType<TriggeredAbility>().Single();
        var evt = new Majik.Core.Events.CardMovedEvent(
            m, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(evt, trigger)
            .Should().BeFalse("'another creature' (CR 109.5) excludes the source itself");
    }

    [Fact]
    public void Mentor_Resolve_PaysOneAndDraws_WhenManaAvailable()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);
        PutOnBattlefield(_alice, m);

        // Seed Alice's library with a card to draw.
        var top = new Sorcery("Top Card", "{0}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Fund Alice's mana pool with {1} (generic).
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var trigger = m.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "trigger paid {1} and drew the top of the library");
        _alice.ManaPool.Total.Should().Be(0,
            "Mentor consumed the funded generic {1}");
    }

    [Fact]
    public void Mentor_Resolve_NoMana_FizzlesNoDraw()
    {
        var m = MentorOfTheMeekFactory.Create(_alice);
        PutOnBattlefield(_alice, m);

        var top = new Sorcery("Top Card", "{0}");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);
        // No mana funded.

        var trigger = m.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().NotContain(top,
            "CR 117.5 — optional may-pay fizzles when the mana can't be paid; no draw");
        _alice.Zones.Library.GetCards().Should().Contain(top,
            "top of library stays put when the trigger fizzled");
    }
}
