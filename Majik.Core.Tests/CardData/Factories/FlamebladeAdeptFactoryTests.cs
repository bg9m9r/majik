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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FlamebladeAdeptFactory"/> (Amonkhet, {R}).
/// Creature — Jackal Warrior 1/2. Oracle text (verified against Scryfall):
///   "Menace
///    Whenever you cycle or discard a card, this creature gets +1/+0 until
///    end of turn."
///
/// Covers:
/// - Identity ({R} Creature — Jackal Warrior 1/2) materialised from JSON.
/// - Menace keyword marker (CR 702.110) observed by CombatAbilities.HasMenace.
/// - "Whenever you cycle ... a card" trigger shape — subscribes to
///   <see cref="CardCycledEvent"/>, gated to controller, battlefield-only
///   (same posture as <see cref="HorrorOfTheBrokenLandsFactory"/>).
/// - "you cycle" gate (opponent-cycle no-op).
/// - Pump resolution: controller cycling a card pumps Flameblade Adept +1/+0
///   until end of turn through the layers pipeline.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "R")]
public class FlamebladeAdeptFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FlamebladeAdept_Identity_JackalWarrior12()
    {
        var card = FlamebladeAdeptFactory.Create(_alice);

        card.Name.Should().Be("Flameblade Adept");
        card.ManaCost.ToString().Should().Be("{R}");
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(2);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Jackal).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlamebladeAdept_IsDispatchedByName()
    {
        var card = NamedCardFactory.Create("Flameblade Adept", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Flameblade Adept");
        ((Creature)card).HasSubtype(CardSubtype.Jackal).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Menace — CR 702.110
    // -----------------------------------------------------------------------

    [Fact]
    public void FlamebladeAdept_HasMenace()
    {
        var card = FlamebladeAdeptFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Menace");
        CombatAbilities.HasMenace(card).Should().BeTrue(
            "Flameblade Adept is printed with Menace (CR 702.110)");
    }

    // -----------------------------------------------------------------------
    // Cycle trigger shape — CR 603.1 over CardCycledEvent
    // -----------------------------------------------------------------------

    [Fact]
    public void FlamebladeAdept_TriggerSubscribesToCardCycledEvent()
    {
        var card = FlamebladeAdeptFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.Condition.Should().BeOfType<EventTriggerCondition<CardCycledEvent>>();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "Flameblade Adept's trigger functions only from the battlefield");
        trigger.TargetRequests.Should().BeEmpty("the self-pump has no targets");
    }

    // -----------------------------------------------------------------------
    // "you cycle" gate
    // -----------------------------------------------------------------------

    [Fact]
    public void FlamebladeAdept_TriggerCondition_DoesNotFire_OnOpponentCycle()
    {
        var card = FlamebladeAdeptFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var opponentEvent = new CardCycledEvent(otherCard, _bob);
        trigger.Condition.Matches(opponentEvent, trigger).Should().BeFalse(
            "Bob cycling does NOT trigger Flameblade Adept — 'you cycle' gate");
    }

    [Fact]
    public void FlamebladeAdept_TriggerCondition_Fires_OnControllerCycling()
    {
        var card = FlamebladeAdeptFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var otherCard = new Card("Some Cycler", "");
        var aliceCycles = new CardCycledEvent(otherCard, _alice);
        trigger.Condition.Matches(aliceCycles, trigger).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Pump resolution — +1/+0 until end of turn (CR 613 Layer 7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void FlamebladeAdept_Resolve_Pumps_Plus1Plus0_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var card = FlamebladeAdeptFactory.Create(_alice, effects: effects, triggers: null);

        card.Power.Should().Be(1, "base power before the pump");
        card.Toughness.Should().Be(2, "base toughness before the pump");

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        card.Power.Should().Be(2, "+1 from the cycle/discard pump");
        card.Toughness.Should().Be(2, "+0 toughness from the cycle/discard pump");
    }
}
