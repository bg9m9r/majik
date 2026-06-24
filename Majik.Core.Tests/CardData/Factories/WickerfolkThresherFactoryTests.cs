using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WickerfolkThresherFactory"/> (Modern Horizons 3,
/// {3}{G}). Artifact Creature — Scarecrow 5/4.
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({3}{G}, 5/4, Artifact + Creature, Scarecrow subtype).
/// - Exactly one battlefield-active attack trigger WITH a CR 603.4
///   intervening-if (delirium — CR 702.105).
/// - Intervening-if reflects the graveyard: false at &lt;4 card types, true at
///   4+ card types.
/// - Resolve with delirium: land on top → controller MAY put it onto the
///   battlefield (CR 305.1, not a land drop) when they accept; a declined land
///   goes to hand; a nonland goes to hand ("if you don't put the card onto the
///   battlefield, put it into your hand").
/// - Empty library → no-op (CR 701.16).
/// </summary>
[Trait("Color", "G")]
public class WickerfolkThresherFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WickerfolkThresher_Identity()
    {
        var c = WickerfolkThresherFactory.Create(_alice);

        c.Name.Should().Be("Wickerfolk Thresher");
        c.HasType(CardType.Artifact).Should().BeTrue("Artifact Creature — Scarecrow");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scarecrow).Should().BeTrue("Wickerfolk Thresher is a Scarecrow");
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(4);
        c.ManaCost.Should().Be("{3}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Trigger shape — one attack trigger, battlefield-active, with the
    // delirium intervening-if (CR 603.4 / 702.105).
    // -----------------------------------------------------------------------

    [Fact]
    public void WickerfolkThresher_HasOneAttackTrigger_WithDeliriumInterveningIf()
    {
        var c = WickerfolkThresherFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one attack trigger");

        var attack = triggers.Single();
        attack.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "the attacker must be on the battlefield (CR 508.1f)");
        attack.InterveningIf.Should().NotBeNull(
            "\"Delirium —\" is a CR 603.4 intervening-if condition");
    }

    [Fact]
    public void WickerfolkThresher_InterveningIf_FalseWithoutDelirium_TrueWithDelirium()
    {
        var c = WickerfolkThresherFactory.Create(_alice);
        var attack = c.Abilities.OfType<TriggeredAbility>().Single();

        // Empty graveyard — 0 card types < 4. Intervening-if false (CR 702.105).
        attack.CanBePutOnStack().Should().BeFalse(
            "delirium not met with an empty graveyard");

        // Build four distinct card types in the graveyard:
        // Land, Creature, Instant, Sorcery.
        SeedGraveyardWithFourCardTypes(_alice);

        attack.CanBePutOnStack().Should().BeTrue(
            "delirium met at four card types in the graveyard (CR 702.105)");
    }

    // -----------------------------------------------------------------------
    // Resolve — land on top, controller accepts → battlefield (CR 305.1).
    // -----------------------------------------------------------------------

    [Fact]
    public void WickerfolkThresher_Resolve_LandOnTop_Accept_GoesToBattlefield()
    {
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // put it onto the battlefield
        AgentRegistry.Set(_alice, agent);

        ResolveAttack(WickerfolkThresherFactory.Create(_alice));

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "the controller chose to put the looked-at land onto the battlefield (CR 305.1)");
        forest.Controller.Should().BeSameAs(_alice,
            "the land enters under the controller's control (CR 110.2a)");
        (forest as Permanent)!.IsTapped.Should().BeFalse(
            "no text says tapped — the land enters untapped (CR 303.4)");
        _alice.Zones.Hand.GetCards().Should().BeEmpty("the land went to the battlefield, not hand");
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — land on top, controller declines → hand ("if you don't put
    // the card onto the battlefield, put it into your hand").
    // -----------------------------------------------------------------------

    [Fact]
    public void WickerfolkThresher_Resolve_LandOnTop_Decline_GoesToHand()
    {
        var forest = new Land("Forest");
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline — keep it in hand
        AgentRegistry.Set(_alice, agent);

        ResolveAttack(WickerfolkThresherFactory.Create(_alice));

        forest.Zone.Should().Be(ZoneType.Hand,
            "a declined land goes to hand — \"if you don't put the card onto the battlefield...\"");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(forest);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — nonland on top → hand (no agent prompt; the "may" only applies
    // to lands).
    // -----------------------------------------------------------------------

    [Fact]
    public void WickerfolkThresher_Resolve_NonLandOnTop_GoesToHand()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        // No yes/no queued: a nonland never asks the agent (the land-only "may").
        var agent = new ScriptedAgent();
        AgentRegistry.Set(_alice, agent);

        ResolveAttack(WickerfolkThresherFactory.Create(_alice));

        bolt.Zone.Should().Be(ZoneType.Hand,
            "a nonland card goes to hand (it can't be put onto the battlefield)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bolt);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve — empty library → no-op (CR 701.16).
    // -----------------------------------------------------------------------

    [Fact]
    public void WickerfolkThresher_Resolve_EmptyLibrary_IsNoOp()
    {
        var thresher = WickerfolkThresherFactory.Create(_alice);
        var attack = thresher.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in attack.Effects) effect.Execute();
        };

        act.Should().NotThrow("empty library is a legal no-op (CR 701.16 — nothing to look at)");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedGraveyardWithFourCardTypes(Player player)
    {
        void Bury(ICard c)
        {
            c.SetOwner(player);
            player.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        Bury(new Land("Forest"));
        Bury(new Creature("Bear", "{1}{G}", 2, 2));
        Bury(new Instant("Bolt", "{R}"));
        Bury(new Sorcery("Divination", "{2}{U}"));
    }

    private static void ResolveAttack(Creature thresher)
    {
        // CR 508.1f / 603.6a — the trigger is battlefield-active; place the
        // attacker on the battlefield so IsTriggered's zone gate passes.
        var controller = thresher.Controller!;
        controller.Zones.Battlefield.AddCard(thresher);
        thresher.SetZone(ZoneType.Battlefield);

        var attack = thresher.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(
                new CreatureAttacksEvent(thresher, controller)));

        foreach (var effect in attack.Effects) effect.Execute();
    }
}
