using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="ThievesGuildEnforcerFactory"/> (Core Set 2021, {B}).
///
/// Covers:
/// - Identity (name, type, mana cost, Human + Rogue subtypes, 1/1,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Flash keyword marker.
/// - Disjoint ETB-or-attacks trigger condition: matches both
///   CardMovedEvent (self → Battlefield) and CreatureAttacksEvent (self).
/// - Trigger body: each opponent mills 2 (CR 701.13b) per fire.
/// - Conditional self-buff predicate: opponent's graveyard count threshold.
///     * Below threshold → base 1/1, no deathtouch.
///     * At or above threshold → +2/+1 (=3/2) and gains Deathtouch.
///     * Lifts dynamically when the opponent's graveyard shrinks back
///       below the threshold (no manual SBA hook required).
///     * Honours "any opponent" — own controller's graveyard is ignored.
/// </summary>
public class ThievesGuildEnforcerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void StockGraveyard(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var card = new Card($"GraveFiller {i}", "");
            card.SetOwner(p);
            card.SetController(p);
            p.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }
    }

    private static void StockLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var card = new Card($"LibFiller {i}", "");
            card.SetOwner(p);
            card.SetController(p);
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }

    private static TriggeredAbility GetMillTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is ThievesGuildEnforcerFactory.EtbOrAttacksSelfCondition);

    [Fact]
    public void ThievesGuildEnforcer_Identity()
    {
        var c = ThievesGuildEnforcerFactory.Create(_alice);

        c.Name.Should().Be("Thieves' Guild Enforcer");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThievesGuildEnforcer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Thieves' Guild Enforcer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Thieves' Guild Enforcer");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    [Fact]
    public void ThievesGuildEnforcer_HasFlash()
    {
        var c = ThievesGuildEnforcerFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
    }

    [Fact]
    public void ThievesGuildEnforcer_TriggerCondition_FiresOnEtbSelf()
    {
        var c = ThievesGuildEnforcerFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetMillTrigger(c);
        var evt = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(evt).Should().BeTrue("self → battlefield matches the ETB arm.");
    }

    [Fact]
    public void ThievesGuildEnforcer_TriggerCondition_FiresOnAttacksSelf()
    {
        var c = ThievesGuildEnforcerFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetMillTrigger(c);
        var evt = new CreatureAttacksEvent(c, _bob);

        trigger.IsTriggered(evt).Should().BeTrue("self attacks matches the attack arm.");
    }

    [Fact]
    public void ThievesGuildEnforcer_TriggerCondition_DoesNotFireOnOtherCreatureEtb()
    {
        var c = ThievesGuildEnforcerFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var other = new Creature("Bob's Rogue", "{B}", 1, 1,
            subtypes: new[] { CardSubtype.Rogue });
        other.SetOwner(_bob);
        other.SetController(_bob);

        var trigger = GetMillTrigger(c);
        var evt = new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(evt).Should().BeFalse(
            "the condition is self-only; other creatures entering do not trigger.");
    }

    [Fact]
    public void ThievesGuildEnforcer_TriggerBody_MillsEachOpponent_Two()
    {
        var carol = new Player("Carol", 20);
        StockLibrary(_bob, 5);
        StockLibrary(carol, 5);
        StockLibrary(_alice, 5); // controller — should NOT be milled.

        var c = ThievesGuildEnforcerFactory.Create(
            _alice,
            continuousEffects: null,
            triggers: null,
            allPlayersResolver: () => new List<Player> { _alice, _bob, carol });
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetMillTrigger(c);
        foreach (var e in trigger.Effects) e.Execute();

        _bob.Zones.Graveyard.GetCards().Count().Should().Be(2);
        carol.Zones.Graveyard.GetCards().Count().Should().Be(2);
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(0,
            "controller is never milled by 'each opponent mills two'.");
    }

    [Fact]
    public void ThievesGuildEnforcer_ConditionalBuff_BelowThreshold_NoBonus()
    {
        var svc = new ContinuousEffectsService();
        StockGraveyard(_bob, 7); // one below threshold.

        var c = ThievesGuildEnforcerFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: () => new List<Player> { _alice, _bob });
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(c);

        c.GetPower().Should().Be(1, "below 8-card threshold → base 1/1.");
        c.GetToughness().Should().Be(1);
        svc.Compute(c).Keywords.Should().NotContain("Deathtouch",
            "below threshold → no deathtouch grant.");
    }

    [Fact]
    public void ThievesGuildEnforcer_ConditionalBuff_AtThreshold_Activates()
    {
        var svc = new ContinuousEffectsService();
        StockGraveyard(_bob, 8); // exactly threshold.

        var c = ThievesGuildEnforcerFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: () => new List<Player> { _alice, _bob });
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(c);

        c.GetPower().Should().Be(3, "+2/+1 while opponent has ≥8 cards in graveyard → 3/2.");
        c.GetToughness().Should().Be(2);
        svc.Compute(c).Keywords.Should().Contain("Deathtouch",
            "and gains deathtouch under the same predicate.");
    }

    [Fact]
    public void ThievesGuildEnforcer_ConditionalBuff_DynamicallyLiftsBelowThreshold()
    {
        var svc = new ContinuousEffectsService();
        StockGraveyard(_bob, 8);

        var c = ThievesGuildEnforcerFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: () => new List<Player> { _alice, _bob });
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(c);

        c.GetPower().Should().Be(3);
        svc.Compute(c).Keywords.Should().Contain("Deathtouch");

        // Bob's graveyard drops below threshold — predicate re-evaluates on
        // next Compute. The graveyard remove bypasses the event bus, so
        // invalidate the layer-system cache explicitly via Clear() —
        // production's CardMovedEvent would do this.
        var top = _bob.Zones.Graveyard.GetCards().First();
        _bob.Zones.Graveyard.RemoveCard(top);
        svc.Clear();

        c.GetPower().Should().Be(1, "predicate re-reads each Compute — bonus lifts dynamically.");
        c.GetToughness().Should().Be(1);
        svc.Compute(c).Keywords.Should().NotContain("Deathtouch");
    }

    [Fact]
    public void ThievesGuildEnforcer_ConditionalBuff_IgnoresOwnGraveyard()
    {
        var svc = new ContinuousEffectsService();
        StockGraveyard(_alice, 10); // controller's own graveyard — does NOT count.

        var c = ThievesGuildEnforcerFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: () => new List<Player> { _alice, _bob });
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(c);

        c.GetPower().Should().Be(1, "predicate scans OPPONENTS' graveyards only.");
        svc.Compute(c).Keywords.Should().NotContain("Deathtouch");
    }

    [Fact]
    public void ThievesGuildEnforcer_ConditionalBuff_LiftsWhenLeavingBattlefield()
    {
        var svc = new ContinuousEffectsService();
        StockGraveyard(_bob, 8);

        var c = ThievesGuildEnforcerFactory.Create(
            _alice,
            continuousEffects: svc,
            triggers: null,
            allPlayersResolver: () => new List<Player> { _alice, _bob });
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(c);

        c.GetPower().Should().Be(3);

        // Move off battlefield — IsActive's zone gate flips false.
        c.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(c);
        _alice.Zones.Graveyard.AddCard(c);

        // Re-read characteristics off the layers service directly.
        var chars = svc.Compute(c);
        chars.Power.Should().Be(1);
        chars.Toughness.Should().Be(1);
        chars.Keywords.Should().NotContain("Deathtouch");
    }
}
