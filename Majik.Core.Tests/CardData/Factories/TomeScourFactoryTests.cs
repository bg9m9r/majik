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
/// Unit tests for <see cref="TomeScourFactory"/> (Modern Horizons,
/// {U}).
///
/// Covers:
/// - Identity (Sorcery, {U}, Blue, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="TomeScourFactory.BuildDefinition"/> shape (single
///   "target player" TargetRequest, no modes, no X).
/// - Mill 5 to the chosen target player (CR 701.13).
/// - Short library fully mills without losing the game (CR 701.13a).
/// - Illegal target (resolver returns non-Player) → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "U")]
public class TomeScourFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TomeScour_Identity_SorceryUCost()
    {
        var card = TomeScourFactory.Create(_alice);

        card.Name.Should().Be("Tome Scour");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{U}");
        card.ManaCostValue.TotalValue.Should().Be(1);
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
    public void TomeScour_BuildDefinition_Shape()
    {
        var def = TomeScourFactory.BuildDefinition(raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target player");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolve — mill 5 (CR 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void TomeScour_Resolve_MillsFiveFromTargetPlayer()
    {
        for (int i = 0; i < 10; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = TomeScourFactory.BuildDefinition(raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.Count.Should().Be(TomeScourFactory.MillCount,
            "top 5 cards move to graveyard (CR 701.13)");
        _bob.Zones.Library.Count.Should().Be(5,
            "remaining 5 stay in library");
    }

    [Fact]
    public void TomeScour_Resolve_ShortLibrary_MillsAllRemaining()
    {
        // Only 3 cards — fewer than MillCount=5.
        for (int i = 0; i < 3; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = TomeScourFactory.BuildDefinition(raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 701.13a — milling more than library has just mills all remaining");
        _bob.Zones.Library.Count.Should().Be(0,
            "all 3 cards moved to graveyard");
        _bob.Zones.Graveyard.Count.Should().Be(3);
    }

    [Fact]
    public void TomeScour_Resolve_IllegalTarget_NoOps()
    {
        // Resolver returns a non-Player (e.g. a stale Card reference).
        var stale = new Instant("Stale", "{U}");
        var def = TomeScourFactory.BuildDefinition(_ => stale);

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
