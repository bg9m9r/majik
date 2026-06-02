using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SanguineBondFactory"/>.
///
/// Card: Sanguine Bond — Enchantment {4}{B}{B} (Magic 2010).
///   "Whenever you gain life, target opponent loses that much life."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trigger condition fires on controller life-gain, NOT opponent gain,
///     NOT life-loss, NOT no-op (gain 0).
///   - Resolution: target opponent loses N (= gain delta).
///   - Resolution no-ops when target is the controller (CR 608.2b).
///   - Resolution no-ops when target is already-lost player.
/// </summary>
[Trait("Color", "B")]
public class SanguineBondFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SanguineBond_Identity()
    {
        var c = SanguineBondFactory.Create(_alice);

        c.Name.Should().Be("Sanguine Bond");
        c.ManaCost.Should().Be("{4}{B}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesSanguineBond()
    {
        var card = NamedCardFactory.Create("Sanguine Bond", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sanguine Bond");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void Trigger_FiresOnControllerLifeGain_NotOpponentGain_NotLoss_NotZeroDelta()
    {
        var sb = SanguineBondFactory.Create(_alice);
        sb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sb);

        var trigger = sb.Abilities.OfType<TriggeredAbility>().First();

        // Controller gains life — matches.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 25), trigger).Should().BeTrue();
        // Opponent gains life — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 25), trigger).Should().BeFalse();
        // Controller LOSES life — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger).Should().BeFalse();
        // No-op delta — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger).Should().BeFalse();
    }

    [Fact]
    public void Trigger_OnResolve_TargetOpponentLosesAmountGained()
    {
        var sb = SanguineBondFactory.Create(_alice);
        sb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sb);

        var trigger = sb.Abilities.OfType<TriggeredAbility>().First();

        // Simulate Alice gaining 5 life — drives the closure-mutable
        // amount holder via the condition matcher.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 25), trigger).Should().BeTrue();
        trigger.SetChosenTargets(new[] { new object[] { _bob } });

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(15); // 20 - 5
        _alice.LifeTotal.Should().Be(20); // unchanged
    }

    [Fact]
    public void Trigger_OnResolve_NoOp_WhenTargetIsController()
    {
        // CR 608.2b — "target opponent" filters out self at resolve.
        var sb = SanguineBondFactory.Create(_alice);
        sb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sb);

        var trigger = sb.Abilities.OfType<TriggeredAbility>().First();
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 23), trigger);
        // Illegal: choose self.
        trigger.SetChosenTargets(new[] { new object[] { _alice } });

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Trigger_OnResolve_NoOp_WhenAmountNotPrimed()
    {
        // If Resolve runs without the condition matching first (defensive),
        // the holder is 0 → drain is a no-op rather than throwing.
        var sb = SanguineBondFactory.Create(_alice);
        sb.SetZone(ZoneType.Battlefield);
        var trigger = sb.Abilities.OfType<TriggeredAbility>().First();
        trigger.SetChosenTargets(new[] { new object[] { _bob } });

        foreach (var effect in trigger.Effects) effect.Execute();

        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Trigger_OnResolve_NoOp_WhenTargetHasAlreadyLost()
    {
        var sb = SanguineBondFactory.Create(_alice);
        sb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sb);

        // Drive Bob to 0 — he's now "lost" and LoseLife would throw.
        _bob.LoseLife(20);
        _bob.HasLost.Should().BeTrue();

        var trigger = sb.Abilities.OfType<TriggeredAbility>().First();
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 23), trigger);
        trigger.SetChosenTargets(new[] { new object[] { _bob } });

        // Should not throw — resolution is no-op on a lost player.
        var act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };
        act.Should().NotThrow();
    }
}
