using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HarshScrutinyFactory"/> (Amonkhet, {B}).
/// "Target opponent reveals their hand. You choose a creature card from it.
/// That player discards that card. Scry 1."
/// Despise-shape targeted discard (creature filter, no life loss) with a
/// Scry 1 tail. The caster (Alice) makes the pick and the scry.
/// </summary>
[Trait("Color", "B")]
public class HarshScrutinyFactoryTests
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

    private static ICard SeedLibrary(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ChosenSpellParams Chosen(Player target) =>
        new(ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

    [Fact]
    public void Identity_SorceryAtB()
    {
        var card = HarshScrutinyFactory.Create(_alice);
        card.Name.Should().Be("Harsh Scrutiny");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
    }

    [Fact]
    public void Resolve_DiscardsChosenCreature_AndScries()
    {
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var bolt = SeedCard(_bob, "Lightning Bolt");

        // Alice's library: top card we expect to be bottomed by the scry.
        var top = SeedLibrary(_alice, "Top Card");
        var second = SeedLibrary(_alice, "Second Card");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Tarmogoyf"));
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { top },
            TopOrder: System.Array.Empty<ICard>()));

        var def = HarshScrutinyFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Discard: the chosen creature went to Bob's graveyard.
        goyf.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(20, "Harsh Scrutiny has no life cost");

        // Scry 1: the top card was sent to the bottom, so "Second Card"
        // is now on top and "Top Card" is last.
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.First().Should().BeSameAs(second);
        lib.Last().Should().BeSameAs(top);
    }

    [Fact]
    public void Resolve_ExcludesNoncreature_FallbackFirstCreature()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var swamp = SeedLand(_bob, "Swamp");
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        SeedLibrary(_alice, "Top Card");

        var def = HarshScrutinyFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Only the creature (Tarmogoyf) is a legal pick.
        goyf.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_NoCreatureInHand_NoDiscard_StillScries()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var swamp = SeedLand(_bob, "Swamp");
        var top = SeedLibrary(_alice, "Top Card");

        // No agent → scry default sends the peeked card to the bottom, but
        // with a single-card library the top stays the same card.
        var def = HarshScrutinyFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
        // Single-card library: bottoming the only card leaves it on top.
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top);
    }

    [Fact]
    public void Resolve_EmptyLibrary_ScryNoOps()
    {
        SeedCreature(_bob, "Tarmogoyf");

        var def = HarshScrutinyFactory.BuildSpellDefinition(
            caster: _alice, resolver: o => o!, agent: null, eventBus: null);
        // Should not throw on an empty caster library.
        var act = () => { foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute(); };
        act.Should().NotThrow();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
