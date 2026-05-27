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
/// Unit tests for <see cref="GlimpseTheUnthinkableFactory"/> (Ravnica: City
/// of Guilds, {U}{B}).
///
/// Covers:
/// - Identity (Sorcery, {U}{B}, Blue + Black, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="GlimpseTheUnthinkableFactory.BuildDefinition"/> shape (single
///   "target player" TargetRequest, no modes, no X).
/// - Mill 10 to the chosen target player (CR 701.13).
/// - Short library fully mills without losing the game (CR 701.13a).
/// - Illegal target (resolver returns non-Player) → no-op (CR 608.2b).
/// </summary>
public class GlimpseTheUnthinkableFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimpse_Identity_SorceryUBCost()
    {
        var card = GlimpseTheUnthinkableFactory.Create(_alice);

        card.Name.Should().Be("Glimpse the Unthinkable");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{U}{B}");
        card.ManaCostValue.TotalValue.Should().Be(2);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Glimpse_DispatchesViaNamedCardFactory()
    {
        var dispatched = NamedCardFactory.Create("Glimpse the Unthinkable", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Glimpse the Unthinkable");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{U}{B}");
    }

    // -----------------------------------------------------------------------
    // BuildDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimpse_BuildDefinition_Shape()
    {
        var def = GlimpseTheUnthinkableFactory.BuildDefinition(raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target player");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolve — mill 10 (CR 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimpse_Resolve_MillsTenFromTargetPlayer()
    {
        for (int i = 0; i < 15; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = GlimpseTheUnthinkableFactory.BuildDefinition(raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.Count.Should().Be(GlimpseTheUnthinkableFactory.MillCount);
        _bob.Zones.Library.Count.Should().Be(5);
    }

    [Fact]
    public void Glimpse_Resolve_ShortLibrary_MillsAllRemaining()
    {
        // Only 4 cards — fewer than MillCount=10.
        for (int i = 0; i < 4; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = GlimpseTheUnthinkableFactory.BuildDefinition(raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 701.13a — milling more than library has just mills all remaining");
        _bob.Zones.Library.Count.Should().Be(0);
        _bob.Zones.Graveyard.Count.Should().Be(4);
    }

    [Fact]
    public void Glimpse_Resolve_IllegalTarget_NoOps()
    {
        // Resolver returns a non-Player (e.g. a stale Card reference).
        var stale = new Instant("Stale", "{U}");
        var def = GlimpseTheUnthinkableFactory.BuildDefinition(_ => stale);

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
