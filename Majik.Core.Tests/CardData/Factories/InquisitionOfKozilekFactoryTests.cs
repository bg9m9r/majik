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
/// Unit tests for <see cref="InquisitionOfKozilekFactory"/> (Rise of the
/// Eldrazi, {B}). "Target player reveals their hand. You choose a nonland
/// card from it with mana value 3 or less. That player discards that card."
/// Thoughtseize-shape discard with a nonland + mana-value≤3 filter, no life
/// loss.
/// </summary>
public class InquisitionOfKozilekFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ICard SeedCard(Player p, string name, string cost)
    {
        var c = new Card(name, cost);
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
        var card = InquisitionOfKozilekFactory.Create(_alice);
        card.Name.Should().Be("Inquisition of Kozilek");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{B}");
    }

    [Fact]
    public void DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Inquisition of Kozilek", _alice);
        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Inquisition of Kozilek");
    }

    [Fact]
    public void Resolve_DiscardsChosenLowMvNonland()
    {
        var bolt = SeedCard(_bob, "Lightning Bolt", "{R}");          // mv 1
        var thoughtseize = SeedCard(_bob, "Thoughtseize", "{B}");    // mv 1

        var agent = new ScriptedAgent();
        agent.QueueFromHand(cs => cs.First(c => c.Name == "Thoughtseize"));

        var def = InquisitionOfKozilekFactory.BuildSpellDefinition(resolver: o => o!, agent: agent, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        thoughtseize.Zone.Should().Be(ZoneType.Graveyard);
        bolt.Zone.Should().Be(ZoneType.Hand);
        _alice.LifeTotal.Should().Be(20, "Inquisition has no life cost");
    }

    [Fact]
    public void Resolve_ExcludesHighManaValueAndLand()
    {
        var titan = SeedCard(_bob, "Primeval Titan", "{4}{G}{G}");   // mv 6 — excluded
        var swamp = SeedLand(_bob, "Swamp");                          // land — excluded
        var bolt = SeedCard(_bob, "Lightning Bolt", "{R}");          // mv 1 — only legal pick

        var def = InquisitionOfKozilekFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard);
        titan.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_OnlyHighMvAndLands_NoDiscard()
    {
        var titan = SeedCard(_bob, "Primeval Titan", "{4}{G}{G}");
        var swamp = SeedLand(_bob, "Swamp");

        var def = InquisitionOfKozilekFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        titan.Zone.Should().Be(ZoneType.Hand);
        swamp.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_ManaValueExactlyThree_IsIncluded()
    {
        var goyf = SeedCard(_bob, "Kalitas", "{2}{B}");  // mv 3 — included (≤ 3)

        var def = InquisitionOfKozilekFactory.BuildSpellDefinition(resolver: o => o!, agent: null, eventBus: null);
        foreach (var e in def.EffectFactory(Chosen(_bob))) e.Execute();

        goyf.Zone.Should().Be(ZoneType.Graveyard, "mana value 3 is within the ≤3 cap");
    }
}
