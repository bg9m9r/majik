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
/// Tests for Read the Bones (Theros, {2}{B}, Sorcery).
///
/// Oracle: "Scry 2, then draw two cards. You lose 2 life."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Resolve order: scry 2 → draw 2 → lose 2 life. Default scry sends
///     both peeked cards to the bottom (no agent registered).
///   - Agent-driven scry keeps cards on top; draw inspects the post-scry
///     library top.
///   - Empty library at resolve flags TriedToDrawFromEmpty + still ticks
///     2 life.
///   - SpellDefinition shape: no target requests, no modes, no X.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class ReadTheBonesTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void ReadTheBones_IsSorcery_At2B()
    {
        var s = ReadTheBonesFactory.Create(_alice);

        s.Name.Should().Be("Read the Bones");
        s.ManaCost.Should().Be("{2}{B}");
        s.HasType(CardType.Sorcery).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ReadTheBones()
    {
        var card = NamedCardFactory.Create("Read the Bones", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Read the Bones");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}");
    }

    // ── Resolve — scry, draw, life-loss ─────────────────────────────────

    [Fact]
    public void Resolve_DefaultScry_BottomsBoth_ThenDraws_ThenLoses2Life()
    {
        // Library [a, b, c, d, e, f].
        // Scry 2 peeks [a, b]; default sends both to bottom.
        //   Library after scry: [c, d, e, f, a, b].
        // Draw 2 pulls [c, d].
        //   Library after draw: [e, f, a, b]. Hand: [c, d].
        // Lose 2 life (20 → 18).
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");
        var e = SeedLibraryCard("E");
        var f = SeedLibraryCard("F");

        var startingLife = _alice.LifeTotal;
        var effects = ReadTheBonesFactory.BuildResolveEffect(_alice);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { c, d });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { e, f, a, b });
        _alice.LifeTotal.Should().Be(startingLife - 2);
    }

    [Fact]
    public void Resolve_AgentKeepsBothOnTop_DrawSeesSameTwo()
    {
        // Library [a, b, c, d]. Agent keeps [a, b] on top.
        // Library after scry: [a, b, c, d]. Draw pulls [a, b].
        // Hand: [a, b]. Lose 2 life.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");

        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: Array.Empty<ICard>(),
            TopOrder: new ICard[] { a, b }));
        AgentRegistry.Set(_alice, agent);

        var startingLife = _alice.LifeTotal;
        var effects = ReadTheBonesFactory.BuildResolveEffect(_alice);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { c, d });
        _alice.LifeTotal.Should().Be(startingLife - 2);
    }

    [Fact]
    public void Resolve_EmptyLibrary_ScryNoOp_DrawFlagsLoss_LifeStillTicks()
    {
        var startingLife = _alice.LifeTotal;
        var effects = ReadTheBonesFactory.BuildResolveEffect(_alice);
        Action act = () => { foreach (var fx in effects) fx.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        _alice.LifeTotal.Should().Be(startingLife - 2);
    }

    [Fact]
    public void Resolve_OneCardLibrary_ScrySeesOne_DrawTakesIt_LosesLife()
    {
        // Library [a]. Scry sees [a]; default sends to bottom — library
        // unchanged for a 1-card library. Draw pulls [a]. Lose 2.
        var a = SeedLibraryCard("A");

        var startingLife = _alice.LifeTotal;
        var effects = ReadTheBonesFactory.BuildResolveEffect(_alice);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        _alice.LifeTotal.Should().Be(startingLife - 2);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the second draw scrapes the empty library and stamps the loss flag");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasNoTargetRequests_NoModes_NoX()
    {
        var def = ReadTheBonesFactory.BuildSpellDefinition(_alice);

        def.TargetRequests.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    [Fact]
    public void Constants_MatchPrintedNumbers()
    {
        ReadTheBonesFactory.ScryAmount.Should().Be(2);
        ReadTheBonesFactory.DrawAmount.Should().Be(2);
        ReadTheBonesFactory.LifeLoss.Should().Be(2);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
