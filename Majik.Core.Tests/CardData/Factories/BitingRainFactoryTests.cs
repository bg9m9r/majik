using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BitingRainFactory"/>.
///
/// Card: Biting Rain — Sorcery {2}{B}{B} (Torment).
///   "All creatures get -2/-2 until end of turn."
///   Madness {2}{B}
///
/// Madness is intrinsic (CR 702.35 — MadnessCatalog + Fx.DiscardCard funnel)
/// and is NOT tested here. These tests cover only the spell body — the fixed
/// symmetric -2/-2 sweep, the fixed-magnitude sibling of Languish (-4/-4).
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller) — built from the
///     embedded JSON via CardDefinitionLoader/CardDefinitionFactory.
///   - Resolve registers a -2/-2 PumpUntilEndOfTurnEffect per creature on every
///     supplied player's battlefield (symmetric sweep — CR 109.5).
///   - Creatures with toughness ≤ 2 reach IsDead() (toughness 0 / negative,
///     CR 704.5f).
///   - Creatures with toughness > 2 survive but lose 2 power/toughness.
///   - Empty battlefield is a no-op.
/// </summary>
[Trait("Color", "B")]
public class BitingRainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BitingRain_Identity()
    {
        var c = BitingRainFactory.Create(_alice);

        c.Name.Should().Be("Biting Rain");
        c.ManaCost.Should().Be("{2}{B}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_AppliesSweep_SymmetricallyAcrossBothPlayers()
    {
        var aliceBear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobBig = NewCreatureOnBattlefield(_bob, "Serra Angel", "{3}{W}{W}", 4, 4);

        var effects = BitingRainFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // -2/-2 hits BOTH players' creatures (CR 109.5 — symmetric sweep).
        aliceBear.Toughness.Should().Be(0, "2 - 2 = 0");
        bobBig.Toughness.Should().Be(2, "4 - 2 = 2");
        aliceBear.IsDead().Should().BeTrue("toughness 0 is lethal (CR 704.5f)");
        bobBig.IsDead().Should().BeFalse("toughness 2 > 0, alive");
        bobBig.Power.Should().Be(2, "4 - 2 = 2");
    }

    [Fact]
    public void Resolve_LeavesLargeCreatures_Alive_ButReducesStats()
    {
        var djinn = NewCreatureOnBattlefield(_bob, "Mahamoti Djinn", "{4}{U}{U}", 5, 6);

        var effects = BitingRainFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        djinn.IsDead().Should().BeFalse("5/6 → 3/4 — toughness 4 > 0, alive");
        djinn.Toughness.Should().Be(4);
        djinn.Power.Should().Be(3);
    }

    [Fact]
    public void Resolve_Kills_AnyCreatureWithToughness_TwoOrLess()
    {
        var token = NewCreatureOnBattlefield(_alice, "Goblin", "", 1, 1);
        var bear = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = BitingRainFactory.BuildResolveEffect(new[] { _alice });
        foreach (var e in effects) e.Execute();

        token.IsDead().Should().BeTrue("1 - 2 = -1 (CR 704.5f)");
        bear.IsDead().Should().BeTrue("2 - 2 = 0 — toughness 0 is lethal (CR 704.5f)");
    }

    [Fact]
    public void Resolve_EmptyBattlefield_IsNoOp()
    {
        var act = () =>
        {
            var effects = BitingRainFactory.BuildResolveEffect(new[] { _alice, _bob });
            foreach (var e in effects) e.Execute();
        };

        act.Should().NotThrow();
    }

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
