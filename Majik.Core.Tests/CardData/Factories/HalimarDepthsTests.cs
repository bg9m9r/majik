using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HalimarDepthsFactory"/> (Worldwake).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, look at the top three cards of your library,
///    then put them back in any order.
///    {T}: Add {U}."
///
/// Loaded from the embedded JSON definition (Land + {T}: Add {U}) via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>; the ETB
/// look-3-and-reorder triggered ability is wired in C# off the shared
/// <see cref="ScryAction"/> reorder primitive (mirroring Sensei's Divining
/// Top's peek path).
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - Single blue mana ability — {U} (CR 605.1a).
/// - One battlefield-active ETB triggered ability (CR 603.6e).
/// - ETB reorder: default keeps the top three in place; an agent-supplied
///   reverse reorders the library front; short / empty libraries are handled.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the Refuge tapland cycle.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class HalimarDepthsTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        // Tests register agents on the global AgentRegistry; clear so cross-
        // test ordering can't leak scry/reorder decisions into other tests.
        AgentRegistry.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HalimarDepths_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);

        land.Name.Should().Be("Halimar Depths");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Halimar Depths is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HalimarDepths_HasManaAbility_ForBlue()
    {
        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void HalimarDepths_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // ETB: look at top 3, put back in any order
    // -----------------------------------------------------------------------

    [Fact]
    public void HalimarDepths_Etb_DefaultReorder_KeepsTopThreeInPlace()
    {
        // Library: [a, b, c, d]. No agent registered → default keeps the
        // peeked window [a, b, c] in original order on top.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b, c, d });
        _alice.Zones.Hand.GetCards().Should().BeEmpty("the look effect never draws");
    }

    [Fact]
    public void HalimarDepths_Etb_AgentReversesTop_ReordersLibraryFront()
    {
        // Library: [a, b, c, d]. ScriptedAgent reverses → [c, b, a, d].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { c, b, a }));
        AgentRegistry.Set(_alice, agent);

        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Equal(new[] { c, b, a, d });
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void HalimarDepths_Etb_BottomBoundPicks_FoldBackOntoTop_NeverBottoms()
    {
        // Halimar Depths is reorder-only (CR 701.20 scry does NOT apply): any
        // agent that mistakenly returns bottom-bound cards still puts every
        // peeked card back on top. Library [a, b, c, d]; agent sends a to the
        // "bottom" → it is folded onto the top, leaving the window on top.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new ICard[] { a },
            TopOrder: new ICard[] { b, c }));
        AgentRegistry.Set(_alice, agent);

        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in etb.Effects) e.Execute();

        // d is untouched at the back; every peeked card stayed on top.
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c, a, d });
    }

    [Fact]
    public void HalimarDepths_Etb_ShortLibrary_PeeksWhatExists_DoesNotThrow()
    {
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");

        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b });
    }

    [Fact]
    public void HalimarDepths_Etb_EmptyLibrary_NoOp()
    {
        var land = (Land)NamedCardFactory.Create("Halimar Depths", _alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        Action act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should()
            .BeFalse("the look effect does not draw");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
