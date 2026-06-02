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
/// Unit tests for <see cref="DuressFactory"/> (various sets, {B}).
/// "Target opponent reveals their hand. You choose a noncreature, nonland
/// card from it. That player discards that card."
/// Thoughtseize-shape discard with a noncreature+nonland filter, no life loss.
/// </summary>
[Trait("Color", "B")]
public class DuressFactoryTests
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

    [Fact]
    public void Identity_SorceryAtB()
    {
        var card = DuressFactory.Create(_alice);
        card.Name.Should().Be("Duress");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
    }
    [Fact]
    public void Resolve_DiscardsChosenNoncreatureNonland()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var counter = SeedCard(_bob, "Counterspell");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Counterspell"));

        var def = DuressFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        counter.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(20, "Duress has no life cost");
    }

    [Fact]
    public void Resolve_ExcludesCreatureAndLand_FallbackFirstNoncreatureNonland()
    {
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var swamp = SeedLand(_bob, "Swamp");
        var duress2 = SeedCard(_bob, "Duress");

        var def = DuressFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Only the noncreature nonland (Duress) is a legal pick.
        duress2.Zone.Should().Be(ZoneType.Graveyard);
        goyf.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_CreatureAndLandOnlyHand_NoDiscard()
    {
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var swamp = SeedLand(_bob, "Swamp");

        var def = DuressFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        goyf.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }
}
