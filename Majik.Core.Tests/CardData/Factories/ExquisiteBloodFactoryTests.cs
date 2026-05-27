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
/// Unit tests for <see cref="ExquisiteBloodFactory"/>.
///
/// Card: Exquisite Blood — Enchantment {4}{B}{B} (Avacyn Restored).
///   "Whenever an opponent loses life, you gain that much life."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trigger condition fires on opponent life-loss, NOT controller loss,
///     NOT opponent gain, NOT zero delta.
///   - Resolution: controller gains N (= loss delta).
/// </summary>
public class ExquisiteBloodFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ExquisiteBlood_Identity()
    {
        var c = ExquisiteBloodFactory.Create(_alice);

        c.Name.Should().Be("Exquisite Blood");
        c.ManaCost.Should().Be("{4}{B}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesExquisiteBlood()
    {
        var card = NamedCardFactory.Create("Exquisite Blood", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Exquisite Blood");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void Trigger_FiresOnOpponentLifeLoss_NotControllerLoss_NotOpponentGain_NotZero()
    {
        var eb = ExquisiteBloodFactory.Create(_alice);
        eb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(eb);

        var trigger = eb.Abilities.OfType<TriggeredAbility>().First();

        // Opponent loses life — matches.
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
        var eb = ExquisiteBloodFactory.Create(_alice);
        eb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(eb);

        var trigger = eb.Abilities.OfType<TriggeredAbility>().First();

        // Simulate Bob losing 4 life — drives the holder via the matcher.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 16), trigger).Should().BeTrue();

        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(24); // 20 + 4
        _bob.LifeTotal.Should().Be(20);   // event was synthetic; only test the gain side
    }

    [Fact]
    public void Trigger_OnResolve_NoOp_WhenControllerHasLost()
    {
        // CR 614 — can't gain life after losing the game.
        var eb = ExquisiteBloodFactory.Create(_alice);
        eb.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(eb);
        _alice.LoseLife(20);
        _alice.HasLost.Should().BeTrue();

        var trigger = eb.Abilities.OfType<TriggeredAbility>().First();
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 18), trigger);

        var act = () =>
        {
            foreach (var effect in trigger.Effects) effect.Execute();
        };
        act.Should().NotThrow();
    }
}
