using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Verifies AgentRegistry round-trip and that the OracleSpellBinder / factory
/// effect closures consult the registered agent instead of hard-coding the
/// default decision.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class AgentRegistryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public AgentRegistryTests()
    {
        // Always start each test with a clean registry.
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // -------------------------------------------------------------------------
    // Registry round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void Set_Get_ReturnsRegisteredAgent()
    {
        var agent = new DeterministicBotAgent();

        AgentRegistry.Set(_alice, agent);

        AgentRegistry.Get(_alice).Should().BeSameAs(agent);
    }

    [Fact]
    public void Get_UnregisteredPlayer_ReturnsNull()
    {
        var stranger = new Player("Stranger", 20);

        AgentRegistry.Get(stranger).Should().BeNull();
    }

    [Fact]
    public void Clear_RemovesAllRegistrations()
    {
        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        AgentRegistry.Clear();

        AgentRegistry.Get(_alice).Should().BeNull();
    }

    [Fact]
    public void Set_OverwritesSamePlayer()
    {
        var first  = new DeterministicBotAgent();
        var second = new DeterministicBotAgent();

        AgentRegistry.Set(_alice, first);
        AgentRegistry.Set(_alice, second);

        AgentRegistry.Get(_alice).Should().BeSameAs(second);
    }

    // -------------------------------------------------------------------------
    // OracleSpellBinder.ScryNSpell — agent consulted when registered
    // -------------------------------------------------------------------------

    [Fact]
    public void ScryNSpell_ConsultsAgent_WhenRegistered()
    {
        // Set up alice's library: A is on top.
        var cardA = new Land("A") { Owner = _alice, Zone = ZoneType.Library };
        var cardB = new Land("B") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(cardA);
        _alice.Zones.Library.AddCard(cardB);

        // Register a ScriptedAgent that keeps the top card on top (TopOrder=[A]).
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { cardB },
            TopOrder: new ICard[] { cardA }));
        AgentRegistry.Set(_alice, agent);

        // "Scry 2" via the binder
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Test Scry", ManaCost = "{1}", OracleText = "Scry 2." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // Agent said: A on top, B on bottom.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib[0].Name.Should().Be("A");
        lib[1].Name.Should().Be("B");
    }

    [Fact]
    public void ScryNSpell_DefaultsAllToBottom_WhenNoAgentRegistered()
    {
        // No AgentRegistry.Set — should use the old default (all-to-bottom).
        var cardA = new Land("A") { Owner = _alice, Zone = ZoneType.Library };
        var cardB = new Land("B") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(cardA);
        _alice.Zones.Library.AddCard(cardB);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Test Scry", ManaCost = "{1}", OracleText = "Scry 2." },
            _alice, raw => raw, null);

        var chosen = new ChosenSpellParams(null, null,
            Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // All peeked cards go to bottom; library is empty at the "front" then A, B.
        // Since the whole 2-card library was peeked, they both go to bottom in
        // the same order (ToBottom=[A,B], TopOrder=[]).
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Select(c => c.Name).Should().Equal("A", "B");
    }

    // -------------------------------------------------------------------------
    // OracleSpellBinder.SurveilSelfSpell — agent consulted when registered
    // -------------------------------------------------------------------------

    [Fact]
    public void SurveilSelfSpell_ConsultsAgent_WhenRegistered()
    {
        var cardA = new Land("A") { Owner = _alice, Zone = ZoneType.Library };
        var cardB = new Land("B") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(cardA);
        _alice.Zones.Library.AddCard(cardB);

        // Agent keeps A on top, sends B to graveyard.
        var agent = new ScriptedAgent();
        agent.QueueSurveilDecision(new SurveilAction.SurveilDecision(
            ToGraveyard: new ICard[] { cardB },
            TopOrder: new ICard[] { cardA }));
        AgentRegistry.Set(_alice, agent);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Test Surveil", ManaCost = "{1}", OracleText = "Surveil 2." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Library.GetCards().Select(c => c.Name).Should().Equal("A");
        _alice.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Equal("B");
    }

    [Fact]
    public void SurveilSelfSpell_DefaultsAllToGraveyard_WhenNoAgentRegistered()
    {
        var cardA = new Land("A") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(cardA);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Test Surveil", ManaCost = "{1}", OracleText = "Surveil 1." },
            _alice, raw => raw, null);

        var chosen = new ChosenSpellParams(null, null,
            Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Equal("A");
    }

    // -------------------------------------------------------------------------
    // UndergroundMortuaryFactory ETB surveil — agent consulted when registered
    // -------------------------------------------------------------------------

    [Fact]
    public void UndergroundMortuary_ETBSurveil_ConsultsAgent_WhenRegistered()
    {
        var cardA = new Land("A") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(cardA);

        // Agent keeps A on top (doesn't send to graveyard).
        var agent = new ScriptedAgent();
        agent.QueueSurveilDecision(new SurveilAction.SurveilDecision(
            ToGraveyard: Array.Empty<ICard>(),
            TopOrder: new ICard[] { cardA }));
        AgentRegistry.Set(_alice, agent);

        var mortuary = NamedCardFactory.Create("Underground Mortuary", _alice);

        // Fire the ETB trigger effect directly.
        var etbTrigger = mortuary.Abilities
            .OfType<TriggeredAbility>()
            .First();
        foreach (var e in etbTrigger.Effects) e.Execute();

        // Agent chose to keep A on top.
        _alice.Zones.Library.GetCards().Select(c => c.Name).Should().Equal("A");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void UndergroundMortuary_ETBSurveil_DefaultsToGraveyard_WhenNoAgentRegistered()
    {
        var cardA = new Land("A") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(cardA);

        var mortuary = NamedCardFactory.Create("Underground Mortuary", _alice);

        var etbTrigger = mortuary.Abilities
            .OfType<TriggeredAbility>()
            .First();
        foreach (var e in etbTrigger.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Select(c => c.Name).Should().Equal("A");
    }
}
