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
/// Unit tests for <see cref="MaraudingBlightPriestFactory"/> (Zendikar Rising,
/// {2}{B}).
///
/// Covers:
/// - Identity (name, type Creature, subtypes Vampire + Cleric, P/T 3/2, mana
///   cost {2}{B}, owner/controller) — loaded from the embedded JSON definition.
/// - NamedCardFactory dispatch.
/// - Lifegain trigger (CR 119.3 / 603.6a): controller gains life ⇒ each
///   opponent loses 1 life. Filtered to controller-only + strictly-positive
///   deltas.
/// - Multiple controller life-gains stack — each triggering event resolves
///   independently (CR 603.2c).
/// - Trigger active only on the battlefield (CR 113.6).
/// </summary>
public class MaraudingBlightPriestTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BlightPriest_Identity()
    {
        var c = MaraudingBlightPriestFactory.Create(_alice);

        c.Name.Should().Be("Marauding Blight-Priest");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BlightPriest_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Marauding Blight-Priest", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Marauding Blight-Priest");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lifegain trigger (CR 119.3 / 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void LifegainTrigger_FiresForController_NotOpponent()
    {
        var c = MaraudingBlightPriestFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // Controller gains life — matches.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 22), trigger).Should().BeTrue();
        // Opponent gains life — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 22), trigger).Should().BeFalse();
        // Controller LOSES life — does NOT match (strictly-positive delta).
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger).Should().BeFalse();
        // Zero delta — does NOT match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger).Should().BeFalse();
    }

    [Fact]
    public void BlightPriest_ControllerGainsLife_EachOpponentLosesOne()
    {
        var c = MaraudingBlightPriestFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // Simulate the trigger resolving (controller gained life).
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19,
            "Marauding Blight-Priest: each opponent loses 1 life when controller gains life");
    }

    [Fact]
    public void BlightPriest_MultipleGains_StackOnePerOpponentEach()
    {
        // CR 603.2c — the triggered ability fires once per life-gain event.
        var c = MaraudingBlightPriestFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        for (var i = 0; i < 4; i++)
        {
            foreach (var e in trigger.Effects) e.Execute();
        }

        _bob.LifeTotal.Should().Be(16, "four resolutions ⇒ -4 life for Bob");
    }

    [Fact]
    public void BlightPriest_NoOpponentResolver_DrainNoOps()
    {
        var c = MaraudingBlightPriestFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no resolver wired ⇒ drain clause no-ops");
    }

    [Fact]
    public void BlightPriest_LifegainTrigger_OnlyActiveOnBattlefield()
    {
        var c = MaraudingBlightPriestFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }
}
