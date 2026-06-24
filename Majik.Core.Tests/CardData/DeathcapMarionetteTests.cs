using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="DeathcapMarionetteFactory"/> (Bloomburrow, {1}{B}).
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (name, {1}{B}, Creature — Fungus, 1/1, owner/controller).
/// - Deathtouch keyword marker (CR 702.2) — gameplay-effective via
///   <c>CombatAbilities.HasDeathtouch</c>.
/// - Optional ETB trigger "you may mill two":
///     * Agent says YES → controller mills 2 from their own library (CR 701.13b).
///     * Agent says NO  → no mill (the "may" is declined, CR 601.2b).
///     * Self-mill — an opponent is never milled.
/// </summary>
[Trait("Color", "B")]
public class DeathcapMarionetteTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

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

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().Single();

    [Fact]
    public void DeathcapMarionette_Identity()
    {
        var c = DeathcapMarionetteFactory.Create(_alice);

        c.Name.Should().Be("Deathcap Marionette");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Fungus).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DeathcapMarionette_HasDeathtouch()
    {
        var c = DeathcapMarionetteFactory.Create(_alice);

        CombatAbilities.HasDeathtouch(c).Should().BeTrue(
            "the JSON 'Deathtouch' keyword marker is gameplay-effective via combat.");
    }

    [Fact]
    public void DeathcapMarionette_EtbTrigger_FiresOnSelfEnter()
    {
        var c = DeathcapMarionetteFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var trigger = GetEtbTrigger(c);
        var evt = new Majik.Core.Events.CardMovedEvent(
            c, ZoneType.Hand, ZoneType.Battlefield);

        trigger.IsTriggered(evt).Should().BeTrue("self → battlefield is the ETB trigger.");
    }

    [Fact]
    public async Task DeathcapMarionette_EtbTrigger_AgentYes_MillsTwo()
    {
        StockLibrary(_alice, 5);

        var c = DeathcapMarionetteFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var trigger = GetEtbTrigger(c);
        await trigger.ResolveAsync(agent, ContextResolve.Game(_alice, _alice, _bob));

        _alice.Zones.Graveyard.GetCards().Count().Should().Be(2,
            "agent accepted the 'you may mill two' → controller mills 2.");
        _alice.Zones.Library.GetCards().Count().Should().Be(3);
    }

    [Fact]
    public async Task DeathcapMarionette_EtbTrigger_AgentNo_DoesNotMill()
    {
        StockLibrary(_alice, 5);

        var c = DeathcapMarionetteFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        var trigger = GetEtbTrigger(c);
        await trigger.ResolveAsync(agent, ContextResolve.Game(_alice, _alice, _bob));

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty(
            "agent declined the optional mill → no cards milled.");
        _alice.Zones.Library.GetCards().Count().Should().Be(5);
    }

    [Fact]
    public async Task DeathcapMarionette_EtbTrigger_MillsSelf_NotOpponent()
    {
        StockLibrary(_alice, 5);
        StockLibrary(_bob, 5);

        var c = DeathcapMarionetteFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var trigger = GetEtbTrigger(c);
        await trigger.ResolveAsync(agent, ContextResolve.Game(_alice, _alice, _bob));

        _alice.Zones.Graveyard.GetCards().Count().Should().Be(2, "self-mill mills the controller.");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty("the opponent is never milled.");
    }
}
