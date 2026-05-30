using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Death's Shadow — Creature — Avatar {B},
/// "Death's Shadow gets -X/-X, where X is your life total." Modeled here
/// as the CDA shape: P/T = clamp(13 - controller life, 0, 13). CR 604.3 /
/// 613.2 — Layer 7a characteristic-defining P/T, sharing the
/// <see cref="CdaPowerToughnessEffect"/> primitive with Tarmogoyf (PR #173).
///
/// Validates:
///   * Card identity + dispatch.
///   * 7a tracks controller's life total live across every Compute.
///   * Clamp endpoints: life ≥ 13 floors to 0/0; life ≤ 0 caps at 13/13.
/// </summary>
public class DeathsShadowTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects;
    private readonly ZoneService _zones;

    public DeathsShadowTests()
    {
        // Wire the effects service to the bus so its CR-613 memoization cache
        // invalidates on game events (matches production GameDependencies).
        _effects = new ContinuousEffectsService(_bus);
        _zones = new ZoneService(_bus);
    }

    private Creature WireShadow(Player owner)
    {
        var shadow = DeathsShadowFactory.Create(owner, _effects, _bus);
        shadow.ActiveEffects = _effects;
        return shadow;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DeathsShadow_IsAvatarCreature_AtCostB()
    {
        var shadow = DeathsShadowFactory.Create(_alice);

        shadow.Name.Should().Be("Death's Shadow");
        shadow.HasType(CardType.Creature).Should().BeTrue();
        shadow.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        shadow.ManaCost.Should().Be("{B}");
        shadow.BasePower.Should().Be(13);
        shadow.BaseToughness.Should().Be(13);
        shadow.Owner.Should().BeSameAs(_alice);
        shadow.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DeathsShadow()
    {
        var shadow = NamedCardFactory.Create("Death's Shadow", _alice);

        shadow.Should().BeOfType<Creature>();
        shadow.Name.Should().Be("Death's Shadow");
        shadow.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Layer 7a — CDA P/T tracks controller's life live
    // -----------------------------------------------------------------------

    [Fact]
    public void DeathsShadow_LifeTwenty_Is_0_0()
    {
        // Life 20 → 13 - 20 = -7, clamped to 0. State-based actions would
        // kill it (0 toughness), but we're just asserting P/T values here.
        _alice.LifeTotal = 20;
        var shadow = WireShadow(_alice);
        _zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(0);
        shadow.Toughness.Should().Be(0);
    }

    [Fact]
    public void DeathsShadow_LifeThirteen_Is_0_0()
    {
        // Boundary: life == 13 → 13 - 13 = 0. Still floored at the lower
        // clamp (which is also the natural value here).
        _alice.LifeTotal = 13;
        var shadow = WireShadow(_alice);
        _zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(0);
        shadow.Toughness.Should().Be(0);
    }

    [Fact]
    public void DeathsShadow_LifeTwelve_Is_1_1()
    {
        // Life 12 → 13 - 12 = 1.
        _alice.LifeTotal = 12;
        var shadow = WireShadow(_alice);
        _zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(1);
        shadow.Toughness.Should().Be(1);
    }

    [Fact]
    public void DeathsShadow_LifeOne_Is_12_12()
    {
        // Life 1 → 13 - 1 = 12.
        _alice.LifeTotal = 1;
        var shadow = WireShadow(_alice);
        _zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(12);
        shadow.Toughness.Should().Be(12);
    }

    [Fact]
    public void DeathsShadow_LifeNegative_CapsAt_13_13()
    {
        // Life -3 → 13 - (-3) = 16, clamped to 13 (printed P/T cap).
        _alice.LifeTotal = -3;
        var shadow = WireShadow(_alice);
        _zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(13);
        shadow.Toughness.Should().Be(13);
    }

    [Fact]
    public void DeathsShadow_TracksLifeChanges_Live()
    {
        // CDA re-evaluates every Compute, so life-total changes are picked
        // up without re-registering the effect or moving the card.
        _alice.LifeTotal = 20;
        var shadow = WireShadow(_alice);
        _zones.MoveCard(shadow, ZoneType.Library, ZoneType.Battlefield, _alice);

        shadow.Power.Should().Be(0);

        // In production life loss flows through PlayerService, which fires a
        // LifeChangedEvent that invalidates the memoization cache. This test
        // pokes Player.LoseLife directly (no service/bus), so publish the
        // event the production path would emit so the CDA re-reads.
        var beforeFirst = _alice.LifeTotal;
        _alice.LoseLife(15); // 20 -> 5
        _bus.Publish(new LifeChangedEvent(_alice, beforeFirst, _alice.LifeTotal));
        shadow.Power.Should().Be(8);
        shadow.Toughness.Should().Be(8);

        var beforeSecond = _alice.LifeTotal;
        _alice.LoseLife(5); // 5 -> 0
        _bus.Publish(new LifeChangedEvent(_alice, beforeSecond, _alice.LifeTotal));
        shadow.Power.Should().Be(13);
        shadow.Toughness.Should().Be(13);
    }

    // -----------------------------------------------------------------------
    // Pure helper sanity
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(20, 0)]
    [InlineData(13, 0)]
    [InlineData(12, 1)]
    [InlineData(1, 12)]
    [InlineData(0, 13)]
    [InlineData(-3, 13)]
    [InlineData(-100, 13)]
    public void ComputePT_ClampsToZeroAndThirteen(int life, int expected)
    {
        DeathsShadowFactory.ComputePT(life).Should().Be(expected);
    }
}
