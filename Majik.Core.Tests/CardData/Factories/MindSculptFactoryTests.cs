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
/// Unit tests for <see cref="MindSculptFactory"/> (Magic 2013, {1}{U}).
///
/// Covers:
/// - Identity (Sorcery, {1}{U}, Blue, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="MindSculptFactory.BuildDefinition"/> shape (single
///   "target opponent" TargetRequest, no modes, no X).
/// - Mill 7 to the chosen opponent (CR 701.13).
/// - Short library fully mills without throwing (CR 701.13a).
/// - Illegal target (resolver returns non-Player) → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class MindSculptFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MindSculpt_Identity_SorceryOneBlueCost()
    {
        var card = MindSculptFactory.Create(_alice);

        card.Name.Should().Be("Mind Sculpt");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}");
        card.ManaCostValue.TotalValue.Should().Be(2);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().NotContain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // BuildDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MindSculpt_BuildDefinition_Shape()
    {
        var def = MindSculptFactory.BuildDefinition(raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target opponent");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolve — mill 7 (CR 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void MindSculpt_Resolve_MillsSevenFromTargetOpponent()
    {
        for (int i = 0; i < 10; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = MindSculptFactory.BuildDefinition(raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.Count.Should().Be(MindSculptFactory.MillCount,
            "top 7 cards move to graveyard (CR 701.13)");
        _bob.Zones.Library.Count.Should().Be(3,
            "remaining 3 stay in library");
    }

    [Fact]
    public void MindSculpt_Resolve_ShortLibrary_MillsAllRemaining()
    {
        // Only 4 cards — fewer than MillCount=7.
        for (int i = 0; i < 4; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = MindSculptFactory.BuildDefinition(raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 701.13a — milling more than library has just mills all remaining");
        _bob.Zones.Library.Count.Should().Be(0,
            "all 4 cards moved to graveyard");
        _bob.Zones.Graveyard.Count.Should().Be(4);
    }

    [Fact]
    public void MindSculpt_Resolve_IllegalTarget_NoOps()
    {
        // Resolver returns a non-Player (e.g. a stale Card reference).
        var stale = new Instant("Stale", "{U}");
        var def = MindSculptFactory.BuildDefinition(_ => stale);

        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 608.2b — illegal target at resolution is a clean no-op");
        _bob.Zones.Graveyard.Count.Should().Be(0);
    }
}
