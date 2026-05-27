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
/// Unit tests for <see cref="DespiseFactory"/> (various sets, {B}).
/// "Target opponent reveals their hand. You choose a creature or planeswalker
/// card from it. That player discards that card."
/// Duress-shape targeted discard with a creature-or-planeswalker filter, no life loss.
/// </summary>
public class DespiseFactoryTests
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

    private static ICard SeedPlaneswalker(Player p, string name)
    {
        var c = new Planeswalker(name, "{2}{B}", 3);
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
        var card = DespiseFactory.Create(_alice);
        card.Name.Should().Be("Despise");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
    }

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Despise", _alice);
        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Despise");
    }

    [Fact]
    public void Resolve_DiscardsChosenCreature()
    {
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var bolt = SeedCard(_bob, "Lightning Bolt");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Tarmogoyf"));

        var def = DespiseFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        goyf.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(20, "Despise has no life cost");
    }

    [Fact]
    public void Resolve_DiscardsChosenPlaneswalker()
    {
        var lili = SeedPlaneswalker(_bob, "Liliana of the Veil");
        var bolt = SeedCard(_bob, "Lightning Bolt");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Liliana of the Veil"));

        var def = DespiseFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        lili.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(20, "Despise has no life cost");
    }

    [Fact]
    public void Resolve_ExcludesNoncreatureNonplaneswalker_FallbackFirstLegal()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var swamp = SeedLand(_bob, "Swamp");
        var goyf = SeedCreature(_bob, "Tarmogoyf");

        var def = DespiseFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Only the creature (Tarmogoyf) is a legal pick.
        goyf.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_NoCreatureOrPlaneswalkerInHand_NoDiscard()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var swamp = SeedLand(_bob, "Swamp");

        var def = DespiseFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        bolt.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }
}
