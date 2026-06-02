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
/// Unit tests for <see cref="DistressFactory"/> (Mirrodin / various, {B}{B}).
/// "Target player reveals their hand. You choose a nonland card from it.
/// That player discards that card."
/// Duress-shape targeted discard with a nonland-only filter (no creature
/// exclusion — creatures are fair game), targeting any player.
/// </summary>
[Trait("Color", "B")]
public class DistressFactoryTests
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
    public void Identity_SorceryAtBB()
    {
        var card = DistressFactory.Create(_alice);
        card.Name.Should().Be("Distress");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}{B}");
    }
    [Fact]
    public void BuildSpellDefinition_SingleTargetPlayerRequest()
    {
        var def = DistressFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("player");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void Resolve_DiscardsChosenNonland()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt");
        var counter = SeedCard(_bob, "Counterspell");

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Counterspell"));

        var def = DistressFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        counter.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_CreatureIsLegalTarget_Distress_DiscardsCreature()
    {
        // Unlike Duress, Distress can take creatures — only lands are excluded.
        var goyf = SeedCreature(_bob, "Tarmogoyf");
        var swamp = SeedLand(_bob, "Swamp");

        var def = DistressFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        // Only the nonland (Tarmogoyf) is a legal pick; first-legal fallback.
        goyf.Zone.Should().Be(ZoneType.Graveyard);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_LandsOnlyHand_NoDiscard()
    {
        var swamp = SeedLand(_bob, "Swamp");
        var mountain = SeedLand(_bob, "Mountain");

        var def = DistressFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        swamp.Zone.Should().Be(ZoneType.Hand);
        mountain.Zone.Should().Be(ZoneType.Hand);
    }
}
