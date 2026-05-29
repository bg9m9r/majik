using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="IngotChewerFactory"/> — Ingot Chewer (Lorwyn,
/// {4}{R}). Creature — Elemental 3/3. Oracle text (verified against
/// Scryfall):
///   "When this creature enters, destroy target artifact.
///    Evoke {R}"
///
/// Covers:
///   - Card identity (Creature, {4}{R}, 3/3, Elemental subtype, red,
///     owner / controller) sourced from the embedded JSON definition.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Evoke keyword marker (CR 702.74) + evoke sacrifice trigger (CR 702.74b).
///   - Single ETB destroy <see cref="TriggeredAbility"/> shape: 1..1
///     "target artifact" request, battlefield active zone, gated to this card.
///   - Resolve: agent-set artifact target → destroyed.
///   - Resolve: agent-set creature target (illegal pick) → no destroy (CR 608.2b).
///   - Resolve: target left the battlefield → no destroy (CR 608.2b).
///   - Resolve: no agent target + no legal candidate → clean no-op.
///   - Resolve: no agent target + own artifact on battlefield → deterministic
///     fallback destroys it (single-arg dispatcher posture).
/// </summary>
public class IngotChewerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void IngotChewer_Identity_Creature_Elemental_3_3_At4R()
    {
        var chewer = IngotChewerFactory.Create(_alice);

        chewer.Name.Should().Be("Ingot Chewer");
        chewer.ManaCost.Should().Be("{4}{R}");
        chewer.HasType(CardType.Creature).Should().BeTrue();
        chewer.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        chewer.BasePower.Should().Be(3);
        chewer.BaseToughness.Should().Be(3);
        CardColors.GetColors(chewer).Should().Contain(ManaColor.Red);
        chewer.Owner.Should().BeSameAs(_alice);
        chewer.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IngotChewer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Ingot Chewer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Ingot Chewer");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{4}{R}");
        ((Creature)card).HasSubtype(CardSubtype.Elemental).Should().BeTrue();
    }

    // ── Evoke ───────────────────────────────────────────────────────────

    [Fact]
    public void IngotChewer_HasEvokeKeyword()
    {
        var chewer = IngotChewerFactory.Create(_alice);

        chewer.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Evoke")
            .Should().HaveCount(1, "CR 702.74 — Evoke is attached as a keyword marker.");
    }

    [Fact]
    public void IngotChewer_HasTwoTriggers_EtbDestroyAndEvokeSacrifice()
    {
        var chewer = IngotChewerFactory.Create(_alice);

        var triggers = chewer.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "the printed ETB destroy + the Evoke sacrifice (CR 702.74b) triggers.");

        // Exactly one of them targets (the destroy); the other is the
        // targetless evoke sacrifice.
        triggers.Count(t => t.TargetRequests.Count > 0).Should().Be(1);
        triggers.Count(t => t.TargetRequests.Count == 0).Should().Be(1);
    }

    // ── ETB destroy trigger — structural ────────────────────────────────

    [Fact]
    public void EtbDestroyTrigger_HasOneArtifactTarget_OnBattlefield()
    {
        var chewer = IngotChewerFactory.Create(_alice);

        var etb = chewer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("artifact");
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.Condition.Should().BeOfType<EventTriggerCondition<CardMovedEvent>>();
    }

    [Fact]
    public void EtbDestroyTrigger_Matches_OnlyThisCardEnteringBattlefield()
    {
        var chewer = IngotChewerFactory.Create(_alice);
        var etb = chewer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        var cond = (EventTriggerCondition<CardMovedEvent>)etb.Condition;

        cond.Matches(
            new CardMovedEvent(chewer, ZoneType.Stack, ZoneType.Battlefield), etb)
            .Should().BeTrue("this card entering the battlefield triggers the ability.");

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        cond.Matches(
            new CardMovedEvent(other, ZoneType.Stack, ZoneType.Battlefield), etb)
            .Should().BeFalse("another creature entering does not trigger this ability.");

        cond.Matches(
            new CardMovedEvent(chewer, ZoneType.Battlefield, ZoneType.Graveyard), etb)
            .Should().BeFalse("leaving the battlefield does not trigger the ETB.");
    }

    // ── ETB destroy — resolution ────────────────────────────────────────

    [Fact]
    public void Resolve_AgentSetArtifactTarget_DestroysIt()
    {
        var trinket = new Artifact("Bob's Trinket", "{2}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var chewer = IngotChewerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chewer);
        chewer.SetZone(ZoneType.Battlefield);

        var etb = chewer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });
        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(trinket);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(trinket);
    }

    [Fact]
    public void Resolve_AgentSetCreatureTarget_DestroyNoOp()
    {
        // A creature is not an artifact — resolution-time gate makes the
        // destroy a no-op (CR 608.2b).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var chewer = IngotChewerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chewer);
        chewer.SetZone(ZoneType.Battlefield);

        var etb = chewer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void Resolve_TargetLeftBattlefield_DestroyNoOp()
    {
        var trinket = new Artifact("Trinket", "{1}");
        trinket.SetOwner(_bob);
        trinket.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(trinket);
        trinket.SetZone(ZoneType.Battlefield);

        var chewer = IngotChewerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chewer);
        chewer.SetZone(ZoneType.Battlefield);

        var etb = chewer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { trinket } });

        // Trinket leaves the battlefield between trigger pick and resolution.
        _bob.Zones.Battlefield.RemoveCard(trinket);
        _bob.Zones.Hand.AddCard(trinket);
        trinket.SetZone(ZoneType.Hand);

        foreach (var effect in etb.Effects) effect.Execute();

        trinket.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(trinket);
    }

    [Fact]
    public void Resolve_NoTarget_NoCandidate_IsCleanNoOp()
    {
        var chewer = IngotChewerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chewer);
        chewer.SetZone(ZoneType.Battlefield);

        var etb = chewer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };
        act.Should().NotThrow();

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoTarget_OwnArtifactOnBattlefield_FallbackDestroysIt()
    {
        // No agent set ChosenTargets. The deterministic fallback should pick
        // the first legal artifact on the controller's battlefield (single-arg
        // dispatcher posture).
        var ownArtifact = new Artifact("Alice's Trinket", "{1}");
        ownArtifact.SetOwner(_alice);
        ownArtifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ownArtifact);
        ownArtifact.SetZone(ZoneType.Battlefield);

        var chewer = IngotChewerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(chewer);
        chewer.SetZone(ZoneType.Battlefield);

        var etb = chewer.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        foreach (var effect in etb.Effects) effect.Execute();

        ownArtifact.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(ownArtifact);
    }
}
