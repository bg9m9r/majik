using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SerumVisionsFactory"/>.
///
/// Serum Visions (Fifth Dawn, {U}, Sorcery): "Draw a card. Scry 2."
///
/// Covers:
///   - Card identity (name, sorcery type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve with default scry (no agent registered) — draws the top
///     card first, then both peeked cards hit the bottom.
///   - Resolve when the controller's agent KEEPS BOTH peeked cards on top
///     — draw still happens first; the peeked window is the POST-draw top.
///   - Resolve on empty library — draw flags the player; scry no-ops.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class SerumVisionsTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    [Fact]
    public void SerumVisions_HasExpectedShape()
    {
        var card = SerumVisionsFactory.Create(_alice);

        card.Name.Should().Be("Serum Visions");
        card.ManaCost.Should().Be("{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SerumVisions()
    {
        var card = NamedCardFactory.Create("Serum Visions", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Serum Visions");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SerumVisions_Resolve_DrawsFirst_ThenDefaultScry_BottomsBoth()
    {
        // Library: [a, b, c, d, e]. Draw pulls `a`. Scry sees [b, c]; default
        // sends both to bottom. Final library: [d, e, b, c]. Hand: [a].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");
        var e = SeedLibraryCard("E");

        var effect = SerumVisionsFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { d, e, b, c });
        a.Zone.Should().Be(ZoneType.Hand);
        b.Zone.Should().Be(ZoneType.Library);
        c.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void SerumVisions_Resolve_AgentKeepsBothOnTop_DrawHappensFirst()
    {
        // Library: [a, b, c]. Draw pulls `a`. Scry window is now [b, c];
        // agent keeps both on top in original order. Final library: [b, c].
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { b, c }));
        AgentRegistry.Set(_alice, agent);

        var effect = SerumVisionsFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b, c });
        a.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void SerumVisions_Resolve_EmptyLibrary_FlagsDrawFromEmpty_ScryNoOp()
    {
        var effect = SerumVisionsFactory.BuildResolveEffect(_alice).Single();
        Action act = () => effect.Execute();

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
