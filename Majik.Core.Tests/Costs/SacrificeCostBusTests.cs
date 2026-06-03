using System.Collections.Generic;
using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Pays down ztd-amount-additional-cost-sacrifice-edict-bus-thread-bulk: the
/// <c>Sacrifice*Cost</c> additional-cost picker family (Fling/Thud, Deadly
/// Dispute, Arcbound Ravager, Bolas's Citadel, Scavenger Grounds, …) now
/// threads an optional <see cref="IEventBus"/> and publishes a
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
/// cost-payer for every permanent sacrificed as a cost, so "whenever a/an
/// [player/opponent] sacrifices …" aristocrat payoffs fire on the cost path.
/// A null bus preserves the legacy publish-nothing posture.
/// </summary>
public class SacrificeCostBusTests
{
    private static Creature Bear(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static Artifact Relic(Player owner, string name)
    {
        var a = new Artifact(name, "{1}");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    private static (EventBus, List<PermanentSacrificedEvent>) WiredBus()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, seen);
    }

    [Fact]
    public void SacrificeCreatureCost_WithBus_Publishes()
    {
        var alice = new Player("Alice", 20);
        var self = Bear(alice, "Source");
        var victim = Bear(alice, "Runeclaw Bear");
        var (bus, seen) = WiredBus();

        var cost = new SacrificeCreatureCost(victim, bus);
        cost.Pay(alice).Should().BeTrue();

        victim.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == victim && ev.SacrificingPlayer == alice && !ev.WasToken);
    }

    [Fact]
    public void SacrificeAnotherCreatureCost_WithBus_Publishes()
    {
        var alice = new Player("Alice", 20);
        var self = Bear(alice, "Source");
        var victim = Bear(alice, "Runeclaw Bear");
        var (bus, seen) = WiredBus();

        var cost = new SacrificeAnotherCreatureCost(self, bus) { Target = victim };
        cost.Pay(alice);

        victim.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.SacrificingPlayer.Should().Be(alice);
        seen[0].SacrificedCard.Should().Be(victim);
    }

    [Fact]
    public void SacrificeAnArtifactCost_WithBus_Publishes()
    {
        var alice = new Player("Alice", 20);
        var relic = Relic(alice, "Mishra's Bauble");
        var (bus, seen) = WiredBus();

        var cost = new SacrificeAnArtifactCost(eventBus: bus);
        cost.Pay(alice);

        relic.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.SacrificedCard.Should().Be(relic);
    }

    [Fact]
    public void SacrificeFilteredCost_WithBus_Publishes_AndSnapshotsToken()
    {
        var alice = new Player("Alice", 20);
        var token = new Creature("Treasure", "", 0, 0);
        token.SetOwner(alice);
        token.SetController(alice);
        token.MarkAsToken();
        alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);
        var (bus, seen) = WiredBus();

        var cost = SacrificeFilteredCost.ForToken(bus);
        cost.Pay(alice);

        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == token && ev.WasToken);
    }

    [Fact]
    public void SacrificeNNonlandPermanentsCost_WithBus_PublishesPerSacrifice()
    {
        var alice = new Player("Alice", 20);
        var a = Bear(alice, "A");
        var b = Bear(alice, "B");
        var (bus, seen) = WiredBus();

        var cost = new SacrificeNNonlandPermanentsCost(2, bus);
        cost.Pay(alice);

        seen.Should().HaveCount(2);
        seen.Should().OnlyContain(ev => ev.SacrificingPlayer == alice);
    }

    [Fact]
    public void SacrificeTwoArtifactsCost_WithBus_PublishesPerSacrifice()
    {
        var alice = new Player("Alice", 20);
        var a = Relic(alice, "A");
        var b = Relic(alice, "B");
        var (bus, seen) = WiredBus();

        var cost = new SacrificeTwoArtifactsCost(eventBus: bus);
        cost.Pay(alice);

        seen.Should().HaveCount(2);
    }

    [Fact]
    public void SacrificeAnArtifactOrCreatureAdditionalCost_WithBus_Publishes()
    {
        var alice = new Player("Alice", 20);
        var relic = Relic(alice, "Bauble");
        var (bus, seen) = WiredBus();

        var cost = new SacrificeAnArtifactOrCreatureAdditionalCost(bus);
        cost.Pay(alice).Should().BeTrue();

        seen.Should().ContainSingle().Which.SacrificedCard.Should().Be(relic);
    }

    [Fact]
    public void NullBus_PreservesLegacyPosture_StillSacrifices_NoPublish()
    {
        var alice = new Player("Alice", 20);
        var victim = Bear(alice, "Runeclaw Bear");

        // No bus passed — legacy publish-nothing posture, but the move happens.
        var cost = new SacrificeCreatureCost(victim);
        cost.Pay(alice).Should().BeTrue();

        victim.Zone.Should().Be(ZoneType.Graveyard);
    }
}
