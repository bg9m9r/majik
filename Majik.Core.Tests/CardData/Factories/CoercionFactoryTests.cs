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
/// Unit tests for <see cref="CoercionFactory"/> ({2}{B} Sorcery).
/// Oracle text:
///   "Target opponent reveals their hand. You choose a card from it.
///    That player discards that card."
///
/// Coercion is a Duress-shape with NO filter (any card — creature, land,
/// spell) and NO life cost. Tests mirror DuressFactoryTests, dropping the
/// noncreature/nonland filter and confirming no life loss.
/// </summary>
[Trait("Color", "B")]
public class CoercionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

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

    // ── Identity ────────────────────────────────────────────────────────

    [Fact]
    public void Identity_Sorcery_At_2B()
    {
        var card = CoercionFactory.Create(_alice);
        card.Name.Should().Be("Coercion");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{B}");
    }
    // ── Core discard behaviour ───────────────────────────────────────────

    [Fact]
    public void Resolve_DiscardsAgentChosenCard()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var counter = SeedCard(_bob, "Counterspell");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Counterspell"));

        var def = CoercionFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        counter.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_CreatureIsLegalPick()
    {
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var bolt = SeedCard(_bob, "Lightning Bolt");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Tarmogoyf"));

        var def = CoercionFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        goyf.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_LandIsLegalPick()
    {
        var swamp = SeedLand(_bob, "Swamp");
        var bolt = SeedCard(_bob, "Lightning Bolt");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Swamp"));

        var def = CoercionFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        swamp.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_FallbackToFirstCard_WhenNoAgent()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var counter = SeedCard(_bob, "Counterspell");

        var def = CoercionFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Deterministic fallback = first card returned by GetCards().
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(1);
        _bob.Zones.Hand.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Resolve_EmptyHand_NoDiscard()
    {
        // Bob has nothing in hand.
        var def = CoercionFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ── No life loss ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CasterLifeTotalUnchanged()
    {
        SeedCard(_bob, "Lightning Bolt");

        var def = CoercionFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _alice.LifeTotal.Should().Be(20, "Coercion has no life cost");
        _bob.LifeTotal.Should().Be(20, "Coercion only discards, no damage");
    }
}
