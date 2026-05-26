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
/// Unit tests for <see cref="VitoThornOfTheDuskRoseFactory"/> (Core Set
/// 2021, {1}{B}{B}).
///
/// Covers:
/// - Identity (name, type Creature, supertype Legendary, subtypes
///   Vampire + Knight, P/T 1/3, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Lifelink keyword marker (CR 702.15) — direct + via CombatAbilities.
/// - Lifegain trigger (CR 119.3 / 603.6a / 603.7): condition matches
///   controller's strictly-positive deltas only.
/// - Resolution drains "that much" life from each opponent — amount
///   captured via SetPendingGainAmount test hook.
/// - Event-bus subscription stamps the amount automatically when wired.
/// - Trigger active only on the battlefield (CR 113.6).
/// </summary>
public class VitoThornOfTheDuskRoseTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Vito_Identity()
    {
        var c = VitoThornOfTheDuskRoseFactory.Create(_alice);

        c.Name.Should().Be("Vito, Thorn of the Dusk Rose");
        c.ManaCost.Should().Be("{1}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Vito_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Vito, Thorn of the Dusk Rose", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Vito, Thorn of the Dusk Rose");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lifelink (CR 702.15)
    // -----------------------------------------------------------------------

    [Fact]
    public void Vito_HasLifelinkKeyword()
    {
        var c = VitoThornOfTheDuskRoseFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Lifelink",
            "CR 702.15 — Lifelink is printed on Vito");

        CombatAbilities.HasLifelink(c).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lifegain trigger condition (CR 119.3 / 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void LifegainTrigger_FiresForController_NotOpponent()
    {
        var c = VitoThornOfTheDuskRoseFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 23), trigger).Should().BeTrue();
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 23), trigger).Should().BeFalse();
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger).Should().BeFalse();
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Drain resolution — "that much"
    // -----------------------------------------------------------------------

    [Fact]
    public void Vito_ControllerGainsThree_EachOpponentLosesThree()
    {
        var c = VitoThornOfTheDuskRoseFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // Without a bus the amount slot is empty — stamp manually via the
        // test hook (shape-only path).
        VitoThornOfTheDuskRoseFactory.SetPendingGainAmount(c, 3);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "each opponent loses 3 — 'that much'");
    }

    [Fact]
    public void Vito_BusWiring_StampsAmountAutomatically()
    {
        var bus = new EventBus();
        var c = VitoThornOfTheDuskRoseFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: bus,
            triggers: null);

        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        // Fire a LifeChangedEvent on the bus — Vito's subscription should
        // stamp the "that much" amount slot (NewLife - PreviousLife = 5).
        bus.Publish(new LifeChangedEvent(_alice, 20, 25));

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(15, "opponent loses 5 — controller gained 5 life");
    }

    [Fact]
    public void Vito_NoAmountStamp_DrainNoOps()
    {
        var c = VitoThornOfTheDuskRoseFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            eventBus: null,
            triggers: null);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        // No amount stamped — the drain clause must no-op without
        // touching opponent life totals.
        foreach (var e in trigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Vito_LifegainTrigger_OnlyActiveOnBattlefield()
    {
        var c = VitoThornOfTheDuskRoseFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
        trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
    }
}
