using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BloodthirstyConquerorFactory"/>.
///
/// Card: Bloodthirsty Conqueror — Creature — Vampire Knight {3}{B}{B} 5/5
/// (March of the Machine).
///   "Flying, deathtouch
///    Whenever an opponent loses life, you gain that much life."
///
/// Covers ONLY the card's unique behaviour + a single identity assert:
///   - Identity (name, type, supertype/subtypes, P/T 5/5, mana cost).
///   - Flying (CR 702.9) + Deathtouch (CR 702.2) keyword markers — direct
///     + via <see cref="CombatAbilities"/>.
///   - Trigger condition fires on opponent life-loss, NOT controller loss,
///     NOT opponent gain, NOT zero delta (CR 119.3 / 603.6a / 102.2).
///   - Resolution: controller gains N (= loss delta).
///   - Trigger active only on the battlefield (CR 113.6).
/// (Dispatch + well-formedness are covered automatically by
/// CardFactoryContractTests.)
/// </summary>
[Trait("Color", "B")]
public class BloodthirstyConquerorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BloodthirstyConqueror_Identity()
    {
        var c = BloodthirstyConquerorFactory.Create(_alice);

        c.Name.Should().Be("Bloodthirsty Conqueror");
        c.ManaCost.Should().Be("{3}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BloodthirstyConqueror_HasFlyingAndDeathtouch()
    {
        var c = BloodthirstyConquerorFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying", "CR 702.9 — Flying is printed");
        keywords.Should().Contain("Deathtouch", "CR 702.2 — Deathtouch is printed");

        CombatAbilities.HasFlying(c).Should().BeTrue();
        CombatAbilities.HasDeathtouch(c).Should().BeTrue();
    }

    [Fact]
    public void Trigger_FiresOnOpponentLifeLoss_NotControllerLoss_NotOpponentGain_NotZero()
    {
        var c = BloodthirstyConquerorFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // Opponent loses life — matches (CR 102.2 — every other player is an
        // opponent).
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 17), trigger).Should().BeTrue();
        // Controller loses life — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger).Should().BeFalse();
        // Opponent GAINS life — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 25), trigger).Should().BeFalse();
        // Zero delta — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 20), trigger).Should().BeFalse();
    }

    [Fact]
    public void Trigger_OnResolve_ControllerGainsAmountOpponentLost()
    {
        var c = BloodthirstyConquerorFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // Bob loses 4 life — drives the holder via the matcher (CR 603.2a).
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 16), trigger).Should().BeTrue();

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(24, "controller gains 'that much' — 4 life");
    }

    [Fact]
    public void Trigger_OnlyActiveOnBattlefield()
    {
        var c = BloodthirstyConquerorFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
