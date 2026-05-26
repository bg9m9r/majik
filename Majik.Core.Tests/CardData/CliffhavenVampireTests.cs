using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CliffhavenVampireFactory"/> (Battle for
/// Zendikar, {1}{W}{B}).
///
/// Covers:
/// - Identity (name, type Creature, subtypes Vampire + Cleric, P/T 2/3,
///   mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Flying keyword marker (CR 702.9) — directly on the abilities
///   collection and via CombatAbilities.
/// - Lifegain trigger (CR 119.3 / 603.6a): controller gains life ⇒ each
///   opponent loses 1 life. Filtered to controller-only + strictly-
///   positive deltas.
/// - Multiple controller life-gains stack — each triggering event
///   resolves independently (CR 603.2c).
/// - Trigger active only on the battlefield (CR 113.6).
/// </summary>
public class CliffhavenVampireTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Cliffhaven_Identity()
    {
        var c = CliffhavenVampireFactory.Create(_alice);

        c.Name.Should().Be("Cliffhaven Vampire");
        c.ManaCost.Should().Be("{1}{W}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Cliffhaven_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Cliffhaven Vampire", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Cliffhaven Vampire");
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Flying (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void Cliffhaven_HasFlyingKeyword()
    {
        var c = CliffhavenVampireFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying",
            "CR 702.9 — Flying is printed on Cliffhaven Vampire");

        CombatAbilities.HasFlying(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lifegain trigger (CR 119.3 / 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void LifegainTrigger_FiresForController_NotOpponent()
    {
        var c = CliffhavenVampireFactory.Create(_alice);
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
    public void Cliffhaven_ControllerGainsLife_EachOpponentLosesOne()
    {
        var c = CliffhavenVampireFactory.Create(
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
            "Cliffhaven Vampire: each opponent loses 1 life when controller gains life");
    }

    [Fact]
    public void Cliffhaven_MultipleGains_StackOnePerOpponentEach()
    {
        // CR 603.2c — the triggered ability fires once per life-gain event.
        var c = CliffhavenVampireFactory.Create(
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
    public void Cliffhaven_NoOpponentResolver_DrainNoOps()
    {
        var c = CliffhavenVampireFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no resolver wired ⇒ drain clause no-ops");
    }

    [Fact]
    public void Cliffhaven_LifegainTrigger_OnlyActiveOnBattlefield()
    {
        var c = CliffhavenVampireFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
        trigger.ActiveZones.Should().NotContain(ZoneType.Hand);
    }
}
