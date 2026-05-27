using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
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
/// Unit tests for <see cref="DivestFactory"/> ({B} Sorcery).
/// "Target player reveals their hand. You choose an artifact or creature card
/// from it. That player discards that card."
/// Duress-shape targeted discard with an artifact-or-creature filter, no life cost.
/// </summary>
public class DivestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    private static ICard SeedCard(Player p, string name, string cost = "")
    {
        var c = new Card(name, cost);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedCreature(Player p, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedArtifact(Player p, string name)
    {
        var c = new Artifact(name, "{1}");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ICard SeedLand(Player p, string name)
    {
        var c = new Land(name);
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ChosenSpellParams Chosen(Player target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    [Fact]
    public void Identity_SorceryAtB()
    {
        var card = DivestFactory.Create(_alice);
        card.Name.Should().Be("Divest");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
    }

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Divest", _alice);
        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Divest");
    }

    [Fact]
    public void Resolve_DiscardsChosenArtifact()
    {
        var solRing  = SeedArtifact(_bob, "Sol Ring");
        var creature = SeedCreature(_bob, "Tarmogoyf");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Sol Ring"));

        var def = DivestFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        solRing.Zone.Should().Be(ZoneType.Graveyard);
        creature.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(20, "Divest has no life cost");
    }

    [Fact]
    public void Resolve_DiscardsChosenCreature()
    {
        var solRing  = SeedArtifact(_bob, "Sol Ring");
        var creature = SeedCreature(_bob, "Tarmogoyf");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Tarmogoyf"));

        var def = DivestFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        creature.Zone.Should().Be(ZoneType.Graveyard);
        solRing.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_ExcludesNoncreatureNonartifact_FallbackFirstLegal()
    {
        var bolt    = SeedCard(_bob, "Lightning Bolt", "{R}");  // instant — excluded
        var swamp   = SeedLand(_bob, "Swamp");                  // land — excluded
        var solRing = SeedArtifact(_bob, "Sol Ring");           // artifact — legal

        var def = DivestFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Only the artifact is legal; fallback picks it.
        solRing.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_FallbackPicksFirstArtifactOrCreature()
    {
        var creature = SeedCreature(_bob, "Tarmogoyf");
        var artifact = SeedArtifact(_bob, "Sol Ring");

        // No agent → deterministic first-legal fallback (creature was added first).
        var def = DivestFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // One of the two legal cards must be discarded.
        var discarded = new[] { creature, artifact }.Count(c => c.Zone == ZoneType.Graveyard);
        discarded.Should().Be(1);
    }

    [Fact]
    public void Resolve_NoLegalCard_NoDiscard()
    {
        var bolt  = SeedCard(_bob, "Lightning Bolt", "{R}");
        var swamp = SeedLand(_bob, "Swamp");

        var def = DivestFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }
}
