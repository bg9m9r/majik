using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SerraAscendantFactory"/>.
///
/// Serra Ascendant (Magic 2011, {W}) is a Creature — Human Monk 1/1. Oracle
/// text (verified against Scryfall 2026-06):
///   "Lifelink (Damage dealt by this creature also causes you to gain that
///    much life.)
///    As long as you have 30 or more life, this creature gets +5/+5 and has
///    flying."
///
/// A conditional-self-buff sibling of Loam Lion / Inventor's Apprentice — the
/// same "+X/+Y as long as &lt;cond&gt;" Layer-7c self-pump shape, extended with
/// a printed Lifelink keyword marker and a Layer-6 conditional Flying grant
/// sharing the same life-threshold predicate. These tests mirror
/// <see cref="InventorsApprenticeFactoryTests"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 1/1, Human + Monk subtypes, {W},
///   owner/controller).
/// - Always-present Lifelink keyword marker.
/// - Life-threshold buff (Layer 7c + Layer 6):
///   - &lt; 30 life → 1/1, no flying.
///   - == 30 life → 6/6, flying.
///   - &gt; 30 life → 6/6, flying.
///   - Dynamically re-evaluates as life crosses the threshold.
///   - Reads the controller's life, not the opponent's.
/// - Helper predicate (HasLifeThreshold).
/// </summary>
[Trait("Color", "W")]
public class SerraAscendantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SerraAscendant_Identity()
    {
        var c = SerraAscendantFactory.Create(_alice);

        c.Name.Should().Be("Serra Ascendant");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SerraAscendant_HasLifelinkKeywordMarker()
    {
        var c = SerraAscendantFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Lifelink",
                System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Serra Ascendant always has Lifelink (CR 702.15)");
    }

    private Creature NewAscendantOnBattlefield(Player owner)
    {
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var c = SerraAscendantFactory.Create(owner, effects, bus);
        zones.MoveCard(c, ZoneType.Library, ZoneType.Battlefield, owner);
        c.ActiveEffects = effects;
        return c;
    }

    [Fact]
    public void Buff_BelowThreshold_StaysOneOne_NoFlying()
    {
        var c = NewAscendantOnBattlefield(_alice); // Alice starts at 20 life.

        c.Power.Should().Be(1, "controller has fewer than 30 life");
        c.Toughness.Should().Be(1);
        CombatAbilities.HasFlying(c).Should().BeFalse(
            "no flying below 30 life");
        CombatAbilities.HasLifelink(c).Should().BeTrue(
            "Lifelink is unconditional and survives the layer system");
    }

    [Fact]
    public void Buff_ExactlyThirtyLife_ActivatesBuff_SixSix_Flying()
    {
        _alice.LifeTotal = 30;
        var c = NewAscendantOnBattlefield(_alice);
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(6, "1 + 5 at exactly 30 life (>= 30)");
        c.Toughness.Should().Be(6, "1 + 5 at exactly 30 life");
        CombatAbilities.HasFlying(c).Should().BeTrue(
            "flying granted at 30 or more life");
        CombatAbilities.HasLifelink(c).Should().BeTrue();
    }

    [Fact]
    public void Buff_AboveThirtyLife_ActivatesBuff_SixSix_Flying()
    {
        _alice.LifeTotal = 45;
        var c = NewAscendantOnBattlefield(_alice);
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        CombatAbilities.HasFlying(c).Should().BeTrue();
    }

    [Fact]
    public void Buff_DynamicallyReevaluates_AsLifeCrossesThreshold()
    {
        var c = NewAscendantOnBattlefield(_alice); // 20 life.

        c.Power.Should().Be(1);
        CombatAbilities.HasFlying(c).Should().BeFalse();

        // Cross above the threshold → buff flips on.
        _alice.LifeTotal = 31;
        c.ActiveEffects!.Clear();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        CombatAbilities.HasFlying(c).Should().BeTrue();

        // Drop below the threshold → buff flips off.
        _alice.LifeTotal = 29;
        c.ActiveEffects!.Clear();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        CombatAbilities.HasFlying(c).Should().BeFalse();
    }

    [Fact]
    public void Buff_ReadsControllerLife_NotOpponentLife()
    {
        // Alice (controller) below 30; Bob (opponent) above 30. The buff
        // reads the controller's life only.
        _alice.LifeTotal = 20;
        _bob.LifeTotal = 50;
        var c = NewAscendantOnBattlefield(_alice);
        c.ActiveEffects!.Clear();

        c.Power.Should().Be(1, "the buff reads YOUR life, not the opponent's");
        CombatAbilities.HasFlying(c).Should().BeFalse();
    }

    [Fact]
    public void HasLifeThreshold_HelperPredicate()
    {
        _alice.LifeTotal = 29;
        SerraAscendantFactory.HasLifeThreshold(_alice).Should().BeFalse();

        _alice.LifeTotal = 30;
        SerraAscendantFactory.HasLifeThreshold(_alice).Should().BeTrue(
            "30 or more (>= 30) satisfies the threshold");

        _alice.LifeTotal = 31;
        SerraAscendantFactory.HasLifeThreshold(_alice).Should().BeTrue();
    }
}
