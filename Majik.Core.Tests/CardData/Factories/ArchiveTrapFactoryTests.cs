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
/// Unit tests for <see cref="ArchiveTrapFactory"/> (Zendikar, {3}{U}{U}).
///
/// Covers:
/// - Identity (Instant, {3}{U}{U}, Blue, CMC 5, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="ArchiveTrapFactory.BuildDefinition"/> shape (single
///   "target opponent" TargetRequest, no modes, no X).
/// - Mill 13 to chosen opponent (CR 701.13).
/// - Targeting the caster is rejected at resolution (CR 109.1 — "opponent"
///   excludes self).
/// - Short library mills all remaining without losing the game (CR 701.13a).
///
/// NOTE: The "if an opponent searched their library this turn, you may
/// cast without paying its mana cost" alternative cost (CR 118.9) is NOT
/// yet wired — the engine has no library-search tracking surface. See
/// <see cref="ArchiveTrapFactory"/> xmldoc for the deferred gap.
/// </summary>
public class ArchiveTrapFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchiveTrap_Identity_Instant3UUCost()
    {
        var card = ArchiveTrapFactory.Create(_alice);

        card.Name.Should().Be("Archive Trap");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{U}{U}");
        card.ManaCostValue.TotalValue.Should().Be(5);
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArchiveTrap_DispatchesViaNamedCardFactory()
    {
        var dispatched = NamedCardFactory.Create("Archive Trap", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Archive Trap");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{3}{U}{U}");
    }

    // -----------------------------------------------------------------------
    // BuildDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchiveTrap_BuildDefinition_Shape()
    {
        var def = ArchiveTrapFactory.BuildDefinition(_alice, raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target opponent");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolve — mill 13 (CR 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchiveTrap_Resolve_MillsThirteenFromTargetOpponent()
    {
        for (int i = 0; i < 20; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = ArchiveTrapFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.Zones.Graveyard.Count.Should().Be(ArchiveTrapFactory.MillCount);
        _bob.Zones.Library.Count.Should().Be(7);
    }

    [Fact]
    public void ArchiveTrap_Resolve_TargetingSelf_NoOps()
    {
        // Should not be a legal target (CR 109.1 — "opponent" excludes
        // self), but defend at resolution.
        for (int i = 0; i < 20; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = ArchiveTrapFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _alice } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Graveyard.Count.Should().Be(0,
            "CR 109.1 — caster is not an opponent; resolution no-ops");
    }

    [Fact]
    public void ArchiveTrap_Resolve_ShortLibrary_MillsAllRemaining()
    {
        // 5 cards — fewer than MillCount=13.
        for (int i = 0; i < 5; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = ArchiveTrapFactory.BuildDefinition(_alice, raw => raw);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow(
            "CR 701.13a — milling more than library has just mills all remaining");
        _bob.Zones.Library.Count.Should().Be(0);
        _bob.Zones.Graveyard.Count.Should().Be(5);
    }
}
