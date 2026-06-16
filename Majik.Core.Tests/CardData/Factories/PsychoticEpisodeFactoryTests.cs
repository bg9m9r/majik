using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PsychoticEpisodeFactory"/> (Shadowmoor, {1}{B}{B}).
/// "Target player reveals their hand and the top card of their library. You
/// choose a card revealed this way. That player puts the chosen card on the
/// bottom of their library."
///
/// The novel mechanic (deferral <c>reveal-hand-opponent-bottoms-chosen-card</c>):
/// the reveal pile = hand + top of library; the CHOOSER is the spell's
/// controller (the target player's OPPONENT, CR 608.2g); the pick moves to the
/// bottom of the target player's library (CR 701.21).
/// </summary>
[Trait("Color", "B")]
public class PsychoticEpisodeFactoryTests
{
    private readonly Player _alice = new("Alice", 20); // controller (chooser)
    private readonly Player _bob = new("Bob", 20);     // target player

    private static ICard SeedHand(Player p, string name, string cost = "")
    {
        var c = new Card(name, cost);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedLibraryTop(Player p, string name, string cost = "")
    {
        var c = new Card(name, cost);
        c.SetOwner(p);
        // Library index 0 is the top.
        p.Zones.Library.InsertCardAt(0, c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ChosenSpellParams Chosen(Player target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    [Fact]
    public void Identity_SorceryAt1BB()
    {
        var card = PsychoticEpisodeFactory.Create(_alice);
        card.Name.Should().Be("Psychotic Episode");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{1}{B}{B}");
    }

    [Fact]
    public void Resolve_ControllerPicksHandCard_GoesToBottomOfLibrary()
    {
        var bolt = SeedHand(_bob, "Lightning Bolt", "{R}");
        var goyf = SeedHand(_bob, "Tarmogoyf", "{1}{G}");
        var libTop = SeedLibraryTop(_bob, "Brainstorm", "{U}");
        // Existing bottom-of-library card so we can assert ordering.
        var libBottom = new Card("Forest", "");
        libBottom.SetOwner(_bob);
        _bob.Zones.Library.AddCard(libBottom); // appends after Brainstorm

        // Alice (the controller / opponent) chooses Tarmogoyf from the pile.
        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Tarmogoyf"));

        var def = PsychoticEpisodeFactory.BuildSpellDefinition(
            resolver: o => o!, chooserAgent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // The chosen hand card is now the BOTTOM of Bob's library.
        goyf.Zone.Should().Be(ZoneType.Library);
        var lib = _bob.Zones.Library.GetCards().ToList();
        lib.Last().Should().BeSameAs(goyf, "the chosen card goes on the bottom (CR 701.21)");
        lib.First().Should().BeSameAs(libTop, "the revealed library top stays on top when a HAND card is chosen");

        // Untouched cards stay put.
        bolt.Zone.Should().Be(ZoneType.Hand);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty("the pick is bottomed, never discarded");
    }

    [Fact]
    public void Resolve_ControllerPicksLibraryTopCard_GoesToBottomOfLibrary()
    {
        var bolt = SeedHand(_bob, "Lightning Bolt", "{R}");
        var libTop = SeedLibraryTop(_bob, "Brainstorm", "{U}");
        var second = new Card("Forest", "");
        second.SetOwner(_bob);
        _bob.Zones.Library.AddCard(second); // now library = [Brainstorm, Forest]

        // Alice picks the revealed top-of-library card.
        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Brainstorm"));

        var def = PsychoticEpisodeFactory.BuildSpellDefinition(
            resolver: o => o!, chooserAgent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        var lib = _bob.Zones.Library.GetCards().ToList();
        lib.First().Should().BeSameAs(second, "Forest is the new top after Brainstorm is bottomed");
        lib.Last().Should().BeSameAs(libTop, "the chosen library-top card is moved to the bottom");
        lib.Count.Should().Be(2, "no cards are added or lost");
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_NoAgent_FallsBackToFirstRevealedHandCard()
    {
        var bolt = SeedHand(_bob, "Lightning Bolt", "{R}");
        var goyf = SeedHand(_bob, "Tarmogoyf", "{1}{G}");
        SeedLibraryTop(_bob, "Brainstorm", "{U}");

        var def = PsychoticEpisodeFactory.BuildSpellDefinition(
            resolver: o => o!, chooserAgent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Deterministic fallback = first revealed card = first hand card.
        bolt.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Last().Should().BeSameAs(bolt);
        goyf.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_EmptyHand_ChoosesRevealedLibraryTop()
    {
        // No cards in hand — the only revealed card is the library top.
        var libTop = SeedLibraryTop(_bob, "Brainstorm", "{U}");
        var second = new Card("Forest", "");
        second.SetOwner(_bob);
        _bob.Zones.Library.AddCard(second);

        var def = PsychoticEpisodeFactory.BuildSpellDefinition(
            resolver: o => o!, chooserAgent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        var lib = _bob.Zones.Library.GetCards().ToList();
        lib.First().Should().BeSameAs(second);
        lib.Last().Should().BeSameAs(libTop, "with an empty hand the revealed library top is bottomed");
    }

    [Fact]
    public void Resolve_EmptyHandAndEmptyLibrary_NoOp()
    {
        var def = PsychoticEpisodeFactory.BuildSpellDefinition(
            resolver: o => o!, chooserAgent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Library.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    /// <summary>
    /// Prod path: the embedded oracle text binds to
    /// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Bespoke.RevealHandTopBottomChosenTemplate"/>
    /// via <see cref="OracleSpellBinder"/>, and that bound definition resolves
    /// the cross-player choice through the CONTROLLER's registered agent.
    /// </summary>
    [Fact]
    public void ProdPath_TemplateBinds_AndControllerAgentChoosesFromTargetReveal()
    {
        var repo = new EmbeddedCardRepository();
        var entity = repo.GetByName("Psychotic Episode");
        entity.Should().NotBeNull();

        // Alice is the caster/controller; Bob is the target.
        var bolt = SeedHand(_bob, "Lightning Bolt", "{R}");
        var goyf = SeedHand(_bob, "Tarmogoyf", "{1}{G}");
        SeedLibraryTop(_bob, "Brainstorm", "{U}");

        // Register Alice's agent — the prod template reads AgentRegistry.Get(caster).
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueFromHand(cs => cs.First(c => c.Name == "Tarmogoyf"));
        using var scope = AgentRegistry.PushScope();
        AgentRegistry.Set(_alice, aliceAgent);

        var def = OracleSpellBinder.Bind(
            entity!, _alice, raw => raw,
            effects: null, stack: null, replacements: null,
            triggers: null, eventBus: null, zones: null);
        def.Should().NotBeNull("Psychotic Episode's oracle text must bind a runnable spell template");

        foreach (var e in def!.EffectFactory(Chosen(_bob))) e.Execute();

        // Alice's choice (Tarmogoyf) is bottomed in Bob's library.
        goyf.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Last().Should().BeSameAs(goyf);
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void IsImplemented_FlipsOn()
    {
        ImplementedCardNames.All.Should().Contain("Psychotic Episode");
    }
}
