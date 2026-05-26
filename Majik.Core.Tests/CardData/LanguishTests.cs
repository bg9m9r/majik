using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="LanguishFactory"/>.
///
/// Card: Languish — Sorcery {2}{B}{B} (Magic Origins).
///   "All creatures get -4/-4 until end of turn."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve registers a -4/-4 PumpUntilEndOfTurnEffect per creature
///     on every supplied player's battlefield (symmetric sweep — CR 109.5).
///   - Creatures with toughness ≤ 4 reach IsDead() (toughness 0 / negative).
///   - Creatures with toughness > 4 survive but lose 4 power/toughness.
/// </summary>
public class LanguishTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Languish_Identity()
    {
        var c = LanguishFactory.Create(_alice);

        c.Name.Should().Be("Languish");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Languish()
    {
        var card = NamedCardFactory.Create("Languish", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Languish");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — symmetric -4/-4 sweep
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_AppliesSweep_SymmetricallyAcrossBothPlayers()
    {
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobBig = NewCreatureOnBattlefield(_bob, "Serra Angel", "{3}{W}{W}", 4, 4);

        var effects = LanguishFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // -4/-4 hits BOTH players' creatures (CR 109.5 — symmetric sweep).
        aliceBear.Toughness.Should().Be(-2, "2 - 4 = -2");
        bobBig.Toughness.Should().Be(0, "4 - 4 = 0");
        aliceBear.IsDead().Should().BeTrue();
        bobBig.IsDead().Should().BeTrue("toughness 0 is lethal (CR 704.5f)");
    }

    [Fact]
    public void Resolve_LeavesLargeCreatures_Alive_ButReducesStats()
    {
        var wall = NewCreatureOnBattlefield(_alice, "Wall of Doubt", "{2}{U}", 0, 5);
        var djinn = NewCreatureOnBattlefield(_bob, "Mahamoti Djinn", "{4}{U}{U}", 5, 6);

        var effects = LanguishFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        wall.IsDead().Should().BeFalse("0/5 → -4/1 — toughness 1 > 0, alive");
        wall.Toughness.Should().Be(1);
        wall.Power.Should().Be(-4);

        djinn.IsDead().Should().BeFalse("5/6 → 1/2 — toughness 2 > 0, alive");
        djinn.Toughness.Should().Be(2);
        djinn.Power.Should().Be(1);
    }

    [Fact]
    public void Resolve_Kills_AnyCreatureWithToughness_FourOrLess()
    {
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var giant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);
        var fourFour = NewCreatureOnBattlefield(_bob, "Air Elemental", "{3}{U}{U}", 4, 4);

        var effects = LanguishFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        bear.IsDead().Should().BeTrue();
        giant.IsDead().Should().BeTrue();
        fourFour.IsDead().Should().BeTrue("4 - 4 = 0 — toughness 0 is lethal (CR 704.5f)");
    }

    [Fact]
    public void Resolve_EmptyBattlefield_IsNoOp()
    {
        var act = () =>
        {
            var effects = LanguishFactory.BuildResolveEffect(new[] { _alice, _bob });
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.ActiveEffects = new ContinuousEffectsService();
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
