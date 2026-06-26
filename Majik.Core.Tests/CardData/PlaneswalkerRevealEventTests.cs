using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 701.16 — "reveal" makes a card public until the revealing effect stops
/// applying; observers (opponents) and the wire delta see the revealed card.
/// CR 701.15 — "look at" / "peek" is private (only the looking player sees it)
/// and fires NO public reveal.
///
/// This file locks the hidden-info-reveal-event-surface pay-down for the
/// planeswalker look-at/reveal family: loyalty abilities that print
/// "<b>reveal</b> a [match] from among them" now publish a
/// <see cref="CardRevealedEvent"/> (tagged <see cref="ZoneType.Library"/>) for
/// the card the player elects to reveal, via the same
/// <see cref="LibrarySearch.PublishRevealIfRequested"/> + <see cref="EventBusRegistry"/>
/// seam the tutor reveals use. Pure "look at" peeks (Jace +2) stay private.
///
/// Cards covered:
///   - Tezzeret, Agent of Bolas +1 ("You may reveal an artifact card …").
///   - Narset, Parter of Veils -2 ("You may reveal a noncreature, nonland card …").
///   - Jace, the Mind Sculptor +2 ("Look at the top card …" — peek, no reveal).
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class PlaneswalkerRevealEventTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly List<CardRevealedEvent> _reveals = new();

    public PlaneswalkerRevealEventTests()
    {
        AgentRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 0));

        _bus.Subscribe<CardRevealedEvent>(_reveals.Add);
        EventBusRegistry.SetDefault(_bus);
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    // ---------------------------------------------------------------------
    // Tezzeret, Agent of Bolas +1 — "You may reveal an artifact card from
    // among them and put it into your hand."  CR 701.16.
    // ---------------------------------------------------------------------

    [Fact]
    public void Tezzeret_Plus1_RevealsTheArtifactItPutsIntoHand()
    {
        var l1 = new Instant("l1", "{U}") { Owner = _alice };
        var art = new Artifact("Mox", "{0}") { Owner = _alice };
        var l3 = new Instant("l3", "{U}") { Owner = _alice };
        foreach (var c in new ICard[] { l1, art, l3 })
        {
            _alice.Zones.Library.AddCard(c);
            ((Card)c).SetZone(ZoneType.Library);
        }

        var tezz = TezzeretAgentOfBolasFactory.Create(_alice);
        tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        _alice.Zones.Hand.GetCards().Should().Contain(art);
        _reveals.Should().ContainSingle("CR 701.16 — the revealed artifact is made public");
        var ev = _reveals[0];
        ev.Card.InstanceId.Should().Be(art.InstanceId);
        ev.Player.Should().Be(_alice);
        ev.From.Should().Be(ZoneType.Library);
        ev.Reason.Should().Be(TezzeretAgentOfBolasFactory.CardName);
    }

    [Fact]
    public void Tezzeret_Plus1_NoArtifact_PublishesNoReveal()
    {
        foreach (var n in new[] { "a", "b", "c" })
        {
            var c = new Instant(n, "{U}") { Owner = _alice };
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var tezz = TezzeretAgentOfBolasFactory.Create(_alice);
        tezz.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        _reveals.Should().BeEmpty("nothing was revealed — no artifact among the looked-at cards");
    }

    // ---------------------------------------------------------------------
    // Narset, Parter of Veils -2 — "You may reveal a noncreature, nonland
    // card from among them and put it into your hand."  CR 701.16.
    // ---------------------------------------------------------------------

    [Fact]
    public void Narset_Minus2_RevealsThePickedCard()
    {
        var counter = new Instant("Counterspell", "UU") { Owner = _alice };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var forest = new Land("Forest") { Owner = _alice };
        foreach (var c in new ICard[] { counter, bear, forest })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 42));
        var agent = new ScriptedAgent();
        agent.QueueFromRevealed((_, eligible) => eligible[0]); // reveal Counterspell
        AgentRegistry.Set(_alice, agent);

        var narset = NarsetParterOfVeilsFactory.Create(_alice);
        narset.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        _alice.Zones.Hand.GetCards().Should().Contain(counter);
        _reveals.Should().ContainSingle("CR 701.16 — the elected reveal is made public");
        var ev = _reveals[0];
        ev.Card.InstanceId.Should().Be(counter.InstanceId);
        ev.Player.Should().Be(_alice);
        ev.From.Should().Be(ZoneType.Library);
        ev.Reason.Should().Be(NarsetParterOfVeilsFactory.CardName);
    }

    [Fact]
    public void Narset_Minus2_AgentDeclines_PublishesNoReveal()
    {
        var counter = new Instant("Counterspell", "UU") { Owner = _alice };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        foreach (var c in new ICard[] { counter, bear })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 5));
        var agent = new ScriptedAgent();
        agent.QueueFromRevealed((ICard?)null); // decline the "may reveal"
        AgentRegistry.Set(_alice, agent);

        var narset = NarsetParterOfVeilsFactory.Create(_alice);
        narset.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _reveals.Should().BeEmpty("the player declined the optional reveal — nothing is made public");
    }

    // ---------------------------------------------------------------------
    // Jace, the Mind Sculptor +2 — "Look at the top card …" is a PRIVATE
    // peek (CR 701.15), NOT a reveal — it must NOT publish a CardRevealedEvent.
    // ---------------------------------------------------------------------

    [Fact]
    public void Jace_Plus2_LookAtTopCard_IsPeek_PublishesNoReveal()
    {
        var top = new Instant("Brainstorm", "U") { Owner = _bob };
        _bob.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var jace = JaceTheMindSculptorFactory.Create(
            _alice,
            targetPlayerResolver: () => new[] { _bob },
            targetCreatureResolver: null);
        jace.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +2).Activate();

        _reveals.Should().BeEmpty("CR 701.15 — \"look at\" is a private peek, not a public reveal");
    }
}
