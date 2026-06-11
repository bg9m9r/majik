using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DredgersInsightFactory"/>.
///
/// Covers:
/// - Card identity (name, Enchantment type, owner/controller)
/// - Two triggered abilities: ETB mill-and-pick, and lifegain-on-graveyard-leave
/// - ETB effect (CR 116.1b "you may"): mills 4, then PROMPTS the controller to
///   put one matching (artifact/creature/land) milled card into hand — or decline
/// - ETB effect: the no-agent fallback auto-picks the first matching card so
///   bot self-play / agentless harnesses stay deterministic
/// - ETB effect: non-matching cards remain in graveyard
/// - ETB effect: empty library is a no-op
/// - Lifegain trigger fires on artifact/creature leaving controller's graveyard
/// - Lifegain trigger does NOT fire for non-artifact/creature cards leaving
/// - Lifegain trigger does NOT fire for opponent's graveyard
/// </summary>
public class DredgersInsightTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public DredgersInsightTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DredgersInsight_IsEnchantment()
    {
        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", _alice);

        enchant.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void DredgersInsight_NameIsCorrect()
    {
        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", _alice);

        enchant.Name.Should().Be("Dredger's Insight");
    }

    [Fact]
    public void DredgersInsight_OwnerAndControllerAreSet()
    {
        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", _alice);

        enchant.Owner.Should().BeSameAs(_alice);
        enchant.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DredgersInsight_HasExactlyTwoTriggeredAbilities()
    {
        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", _alice);

        enchant.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one ETB mill-and-pick trigger and one lifegain-on-graveyard-leave trigger");
    }

    [Fact]
    public void DredgersInsight_HasNoManaAbilities()
    {
        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", _alice);

        enchant.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Dredger's Insight produces no mana");
    }

    // -----------------------------------------------------------------------
    // ETB trigger: mill 4, then prompt to put one matching card into hand.
    //
    // The legacy synchronous Execute() path used below registers no agent, so
    // it exercises the DETERMINISTIC FALLBACK (auto-pick the first matching
    // milled card). The interactive, agent-driven behaviour — the actual CR
    // 116.1b "you may" choice — is covered in the agent section further down.
    // -----------------------------------------------------------------------

    [Fact]
    public void DredgersInsight_EtbEffect_MillsFourCards()
    {
        var alice = new Player("Alice", 20);
        for (var i = 0; i < 6; i++)
        {
            var c = new Card($"Card{i}", "");
            c.SetOwner(alice);
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        // 4 milled; if none is a/c/l, all 4 stay in graveyard
        alice.Zones.Graveyard.GetCards().Should().HaveCount(4,
            "mill 4 moves exactly 4 cards to the graveyard (none are a/c/l here)");
        alice.Zones.Library.GetCards().Should().HaveCount(2,
            "2 cards remain in the library after milling 4 of 6");
    }

    [Fact]
    public void DredgersInsight_EtbEffect_PutsFirstCreatureIntoHand()
    {
        var alice = new Player("Alice", 20);
        var instant = new Instant("Shock", "R");
        instant.SetOwner(alice);
        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(alice);
        alice.Zones.Library.AddCard(instant);
        instant.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(creature);
        creature.SetZone(ZoneType.Library);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(creature,
            "first creature milled goes to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(instant,
            "the non-creature milled card remains in graveyard");
        alice.Zones.Graveyard.GetCards().Should().NotContain(creature,
            "the picked creature is removed from the graveyard and moved to hand");
        creature.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_PutsFirstArtifactIntoHand()
    {
        var alice = new Player("Alice", 20);
        var artifact = new Artifact("Sol Ring", "1");
        artifact.SetOwner(alice);
        alice.Zones.Library.AddCard(artifact);
        artifact.SetZone(ZoneType.Library);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(artifact,
            "artifact milled from top of library goes to hand");
        artifact.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_PutsFirstLandIntoHand()
    {
        var alice = new Player("Alice", 20);
        var land = new Land("Forest");
        land.SetOwner(alice);
        alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(land,
            "land milled from library goes to hand");
        land.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_NoQualifyingCard_NothingGoesToHand()
    {
        var alice = new Player("Alice", 20);
        var instant = new Instant("Counterspell", "UU");
        instant.SetOwner(alice);
        alice.Zones.Library.AddCard(instant);
        instant.SetZone(ZoneType.Library);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no artifact/creature/land was milled, so nothing goes to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(instant);
    }

    [Fact]
    public void DredgersInsight_EtbEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("milling an empty library is a no-op");
    }

    // -----------------------------------------------------------------------
    // ETB trigger: agent-driven "you may put one … into your hand" (CR 116.1b)
    // -----------------------------------------------------------------------

    private static async Task RunEtbAsync(Enchantment enchant, Player controller, IPlayerAgent agent)
    {
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        var ctx = ResolutionContext.For(
            controller, agent, game: null, chosenTargets: null);
        foreach (var effect in etb.Effects)
        {
            await effect.ExecuteAsync(ctx);
        }
    }

    [Fact]
    public async Task DredgersInsight_EtbEffect_ScriptedAgentPicksSpecificCard_GoesToHand()
    {
        var alice = new Player("Alice", 20);
        // Two creatures milled — the agent must be able to pick the SECOND
        // one, proving a real choice rather than the auto-pick-first default.
        var bear1 = new Creature("Bear 1", "1G", 2, 2);
        var bear2 = new Creature("Bear 2", "1G", 2, 2);
        bear1.SetOwner(alice);
        bear2.SetOwner(alice);
        alice.Zones.Library.AddCard(bear1);
        bear1.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(bear2);
        bear2.SetZone(ZoneType.Library);

        var agent = new ScriptedAgent();
        agent.QueueFromRevealed(bear2);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        await RunEtbAsync(enchant, alice, agent);

        alice.Zones.Hand.GetCards().Should().Contain(bear2,
            "the agent's chosen card goes to hand, not the first matching one");
        alice.Zones.Hand.GetCards().Should().NotContain(bear1);
        alice.Zones.Graveyard.GetCards().Should().Contain(bear1,
            "the unchosen milled card stays in the graveyard");
    }

    [Fact]
    public async Task DredgersInsight_EtbEffect_AgentDeclines_AllMilledStayInGraveyard()
    {
        var alice = new Player("Alice", 20);
        // Two matching cards milled; the player still declines (it's a "may").
        var artifact = new Artifact("Sol Ring", "1");
        var land = new Land("Forest");
        artifact.SetOwner(alice);
        land.SetOwner(alice);
        alice.Zones.Library.AddCard(artifact);
        artifact.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);

        var agent = new ScriptedAgent();
        agent.QueueFromRevealed((ICard?)null); // CR 116.1b — decline.

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        await RunEtbAsync(enchant, alice, agent);

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the controller declined the 'you may' clause — nothing to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { artifact, land },
            "all milled cards remain in the graveyard when the player declines");
    }

    [Fact]
    public async Task DredgersInsight_EtbEffect_PromptsAgentEvenWithNoMatch_NothingToHand()
    {
        var alice = new Player("Alice", 20);
        // Four non-matching cards (instants) — no eligible pick, but the
        // player is still prompted so they see the milled pile.
        var insts = new[] { "A", "B", "C", "D" }
            .Select(n => { var c = new Instant(n, ""); c.SetOwner(alice); return c; })
            .ToList();
        foreach (var c in insts) { alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library); }

        var prompted = false;
        var agent = new ScriptedAgent();
        agent.QueueFromRevealed((revealed, eligible) =>
        {
            prompted = true;
            eligible.Should().BeEmpty("no artifact/creature/land was milled");
            revealed.Should().HaveCount(4, "the player still sees the milled pile");
            return null;
        });

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        await RunEtbAsync(enchant, alice, agent);

        prompted.Should().BeTrue(
            "the controller is prompted even when no milled card is eligible");
        alice.Zones.Hand.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().Contain(insts);
    }

    [Fact]
    public async Task DredgersInsight_EtbEffect_AgentFromRegistry_IsPromptedOnSyncPath()
    {
        // Even the legacy synchronous Execute() path must prompt a REGISTERED
        // agent (resolved via AgentRegistry when ctx.Agent is null) — this is
        // the live-match posture where the effect runs context-free but the
        // player must still choose.
        var alice = new Player("Alice", 20);
        var bear1 = new Creature("Bear 1", "1G", 2, 2);
        var bear2 = new Creature("Bear 2", "1G", 2, 2);
        bear1.SetOwner(alice);
        bear2.SetOwner(alice);
        alice.Zones.Library.AddCard(bear1);
        bear1.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(bear2);
        bear2.SetZone(ZoneType.Library);

        var agent = new ScriptedAgent();
        agent.QueueFromRevealed(bear2);
        AgentRegistry.Set(alice, agent);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var etb = enchant.Abilities.OfType<TriggeredAbility>().First();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(bear2,
            "the registered agent's choice is honoured on the sync path too");
        alice.Zones.Graveyard.GetCards().Should().Contain(bear1);
    }

    // -----------------------------------------------------------------------
    // Lifegain trigger condition checks
    // -----------------------------------------------------------------------

    [Fact]
    public void DredgersInsight_LifegainTrigger_FiresForCreatureLeavingOwnersGraveyard()
    {
        var alice = new Player("Alice", 20);
        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(alice);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        // The second trigger is the lifegain trigger (ETB first, lifegain second).
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: creature,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Hand);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeTrue(
            "a creature card leaving the controller's graveyard should trigger the lifegain");
    }

    [Fact]
    public void DredgersInsight_LifegainTrigger_FiresForArtifactLeavingOwnersGraveyard()
    {
        var alice = new Player("Alice", 20);
        var artifact = new Artifact("Sol Ring", "1");
        artifact.SetOwner(alice);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: artifact,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Exile);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeTrue(
            "an artifact card leaving the controller's graveyard triggers the lifegain");
    }

    [Fact]
    public void DredgersInsight_LifegainTrigger_DoesNotFireForInstantLeavingGraveyard()
    {
        var alice = new Player("Alice", 20);
        var instant = new Instant("Counterspell", "UU");
        instant.SetOwner(alice);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: instant,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Hand);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeFalse(
            "an instant (non-artifact, non-creature) leaving graveyard should NOT trigger");
    }

    [Fact]
    public void DredgersInsight_LifegainTrigger_DoesNotFireForOpponentsGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        // Creature owned by Bob — its Owner is Bob, not alice
        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(bob);

        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        var moveEvent = new Majik.Core.Events.CardMovedEvent(
            card: creature,
            fromZone: ZoneType.Graveyard,
            toZone: ZoneType.Hand);

        lifegainTrigger.Condition.Matches(moveEvent, lifegainTrigger).Should().BeFalse(
            "card leaving an opponent's graveyard should NOT trigger Dredger's Insight");
    }

    [Fact]
    public void DredgersInsight_LifegainEffect_GainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var enchant = (Enchantment)NamedCardFactory.Create("Dredger's Insight", alice);
        var lifegainTrigger = enchant.Abilities.OfType<TriggeredAbility>().Skip(1).First();

        foreach (var effect in lifegainTrigger.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(21, "lifegain trigger adds exactly 1 life");
    }
}
