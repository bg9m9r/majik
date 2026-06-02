using System.Collections.Generic;
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
/// Tests for <see cref="ThundermawHellkiteFactory"/> (Magic 2013, {3}{R}{R}).
/// Creature — Dragon, 5/5:
///   "Flying
///    Haste
///    When this creature enters, it deals 1 damage to each creature with
///    flying your opponents control. Tap those creatures."
///
/// Covers:
/// - Identity (Creature, Dragon subtype, {3}{R}{R}, 5/5, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying + Haste keyword markers (CR 702.9 / 702.10).
/// - ETB trigger fires on self-entering (CR 603.6a).
/// - ETB resolution: 1 damage + tap to each opponent flyer; own creatures and
///   non-flyers untouched.
/// </summary>
[Trait("Color", "R")]
public class ThundermawHellkiteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CardMovedEvent>);

    private static Creature Flyer(string name, Player controller, int toughness = 2)
    {
        var c = new Creature(name, "{1}{U}", 2, toughness);
        c.SetOwner(controller);
        c.SetController(controller);
        c.AddAbility(new KeywordAbility("Flying", c, controller));
        return c;
    }

    private static Creature Grounder(string name, Player controller)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ThundermawHellkite_Identity()
    {
        var c = ThundermawHellkiteFactory.Create(_alice);

        c.Name.Should().Be("Thundermaw Hellkite");
        c.ManaCost.Should().Be("{3}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThundermawHellkite_DispatchesViaNamedFactory()
    {
        var c = NamedCardFactory.Create("Thundermaw Hellkite", _alice);

        c.Should().NotBeNull();
        c!.Name.Should().Be("Thundermaw Hellkite");
    }

    [Fact]
    public void ThundermawHellkite_HasFlyingAndHaste()
    {
        var c = ThundermawHellkiteFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue("CR 702.9 — Thundermaw has Flying.");
        CombatAbilities.HasHaste(c).Should().BeTrue("CR 702.10 — Thundermaw has Haste.");
    }

    // -----------------------------------------------------------------------
    // ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void ThundermawHellkite_EtbTrigger_FiresOnSelfEntering()
    {
        var c = ThundermawHellkiteFactory.Create(_alice);

        var trigger = GetEtbTrigger(c);
        var cond = (EventTriggerCondition<CardMovedEvent>)trigger.Condition;

        cond.Matches(
            new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeTrue("CR 603.6a — 'when this creature enters' self-ETB.");

        var other = new Creature("Bear", "G", 2, 2);
        cond.Matches(
            new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeFalse("only Thundermaw's own ETB fires this trigger.");
    }

    [Fact]
    public void ThundermawHellkite_Etb_DamagesAndTapsOpponentFlyersOnly()
    {
        var opponentFlyer = Flyer("Opp Flyer", _bob);   // 2/2 flyer, opponent
        var opponentGround = Grounder("Opp Ground", _bob); // no flying, opponent
        var ownFlyer = Flyer("Own Flyer", _alice);       // 2/2 flyer, controller's own

        var pool = new List<Creature> { opponentFlyer, opponentGround, ownFlyer };

        var card = ThundermawHellkiteFactory.Create(
            _alice,
            triggers: null,
            opponentCreaturesResolver: () => pool);

        var trigger = GetEtbTrigger(card);
        foreach (var e in trigger.Effects) e.Execute();

        // Opponent flyer: 1 damage + tapped.
        opponentFlyer.Damage.Should().Be(1,
            "CR 119.3 — 1 damage to each opponent creature with flying.");
        opponentFlyer.IsTapped.Should().BeTrue("'Tap those creatures.'");

        // Opponent non-flyer: untouched.
        opponentGround.Damage.Should().Be(0,
            "only creatures with flying are affected.");
        opponentGround.IsTapped.Should().BeFalse();

        // Own flyer: untouched ("your opponents control").
        ownFlyer.Damage.Should().Be(0,
            "CR 109.2 — only opponent-controlled flyers are affected.");
        ownFlyer.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void ThundermawHellkite_Etb_AlreadyTappedFlyer_NoThrow()
    {
        var opponentFlyer = Flyer("Opp Flyer", _bob);
        opponentFlyer.Tap(); // already tapped before the trigger resolves

        var card = ThundermawHellkiteFactory.Create(
            _alice,
            triggers: null,
            opponentCreaturesResolver: () => new List<Creature> { opponentFlyer });

        var trigger = GetEtbTrigger(card);
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };

        act.Should().NotThrow("CR 701.21a — tapping an already-tapped flyer is a no-op.");
        opponentFlyer.Damage.Should().Be(1, "it still takes 1 damage.");
        opponentFlyer.IsTapped.Should().BeTrue();
    }
}
