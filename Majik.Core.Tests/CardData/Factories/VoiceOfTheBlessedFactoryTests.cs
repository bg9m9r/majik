using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="VoiceOfTheBlessedFactory"/> (Innistrad: Midnight
/// Hunt, {W}{W}).
///
/// Card: Voice of the Blessed — Creature — Spirit Cleric 2/2.
/// Oracle (verified against the embedded Scryfall-sourced seed):
///   "Whenever you gain life, put a +1/+1 counter on this creature.
///    As long as this creature has four or more +1/+1 counters on it, it has
///    flying and vigilance.
///    As long as this creature has ten or more +1/+1 counters on it, it has
///    indestructible."
///
/// Covers:
/// - Identity (Creature — Spirit Cleric, {W}{W}, 2/2).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Lifegain trigger condition: controller gain → matches; opponent gain →
///   does not; controller life loss → does not; zero delta → does not.
/// - Resolution: one +1/+1 counter per resolution regardless of amount.
/// - Flying + Vigilance static gated at four +1/+1 counters (appears at 4,
///   absent below, lifts when dropped below 4, counts only +1/+1 counters).
/// - Indestructible static gated at ten +1/+1 counters.
/// </summary>
[Trait("Color", "W")]
public class VoiceOfTheBlessedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VoiceOfTheBlessed_Identity()
    {
        var c = VoiceOfTheBlessedFactory.Create(_alice);

        c.Name.Should().Be("Voice of the Blessed");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VoiceOfTheBlessed_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Voice of the Blessed", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Voice of the Blessed");
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Lifegain trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void VoiceOfTheBlessed_LifegainTrigger_FiresForController_NotOpponent()
    {
        var voice = VoiceOfTheBlessedFactory.Create(_alice);
        var trigger = voice.Abilities.OfType<TriggeredAbility>().Single();

        // Controller gains life — match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 22), trigger)
            .Should().BeTrue("Voice's trigger fires on controller life gain");
        // Opponent gains life — no match.
        trigger.Condition.Matches(new LifeChangedEvent(_bob, 20, 25), trigger)
            .Should().BeFalse("Voice ignores opponent life gains");
        // Controller loses life — no match (strict positive delta).
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 17), trigger)
            .Should().BeFalse("life LOSS is not life gain");
        // Zero delta — no match.
        trigger.Condition.Matches(new LifeChangedEvent(_alice, 20, 20), trigger)
            .Should().BeFalse("zero life delta is not a gain");
    }

    [Fact]
    public void VoiceOfTheBlessed_OnResolve_PlacesOnePlusOnePlusOneCounter()
    {
        var voice = VoiceOfTheBlessedFactory.Create(_alice);
        voice.SetZone(ZoneType.Battlefield);

        var trigger = voice.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        voice.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Voice gains one +1/+1 counter on lifegain (CR 122.1)");
    }

    [Fact]
    public void VoiceOfTheBlessed_MultipleLifeGains_AccumulateCounters()
    {
        // CR 603.2 / 122.1 — each separate life-gain event triggers the ability
        // once, placing one counter per resolution regardless of the amount.
        var voice = VoiceOfTheBlessedFactory.Create(_alice);
        voice.SetZone(ZoneType.Battlefield);

        var trigger = voice.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();
        foreach (var effect in trigger.Effects) effect.Execute();
        foreach (var effect in trigger.Effects) effect.Execute();

        voice.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Flying + Vigilance static (four or more +1/+1 counters)
    // -----------------------------------------------------------------------

    [Fact]
    public void VoiceOfTheBlessed_FlyingVigilanceStatic_AppearsAtFourCounters_LiftsBelow()
    {
        var svc = new ContinuousEffectsService();
        var voice = VoiceOfTheBlessedFactory.Create(
            _alice, triggers: null, replacements: null, eventBus: null, continuousEffects: svc);
        voice.ActiveEffects = svc;
        AddToBattlefield(voice);

        // 0 counters — neither keyword.
        CombatAbilities.HasFlying(voice).Should().BeFalse(
            "below 4 +1/+1 counters Voice has no flying (CR 702.9)");
        CombatAbilities.HasVigilance(voice).Should().BeFalse(
            "below 4 +1/+1 counters Voice has no vigilance (CR 702.20)");

        // 3 counters — still neither.
        voice.Counters.Add(CounterType.PlusOnePlusOne, 3);
        CombatAbilities.HasFlying(voice).Should().BeFalse("3 counters is below the threshold");
        CombatAbilities.HasVigilance(voice).Should().BeFalse("3 counters is below the threshold");

        // 4th counter — both keywords appear.
        voice.Counters.Add(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasFlying(voice).Should().BeTrue(
            "at 4 +1/+1 counters Voice has flying (CR 613.1f / 702.9)");
        CombatAbilities.HasVigilance(voice).Should().BeTrue(
            "at 4 +1/+1 counters Voice has vigilance (CR 613.1f / 702.20)");

        // Drop below threshold — both lift.
        voice.Counters.Remove(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasFlying(voice).Should().BeFalse(
            "flying lifts once the count drops below 4 (CR 122.6)");
        CombatAbilities.HasVigilance(voice).Should().BeFalse(
            "vigilance lifts once the count drops below 4 (CR 122.6)");
    }

    [Fact]
    public void VoiceOfTheBlessed_FlyingVigilanceStatic_CountsOnlyPlusOnePlusOneCounters()
    {
        // Oracle reads "four or more +1/+1 counters" specifically — four Charge
        // counters do NOT satisfy the gate (unlike Warden's "any counter").
        var svc = new ContinuousEffectsService();
        var voice = VoiceOfTheBlessedFactory.Create(
            _alice, triggers: null, replacements: null, eventBus: null, continuousEffects: svc);
        voice.ActiveEffects = svc;
        AddToBattlefield(voice);

        voice.Counters.Add(CounterType.Charge, 4);
        CombatAbilities.HasFlying(voice).Should().BeFalse(
            "Charge counters do not count toward \"four or more +1/+1 counters\"");
        CombatAbilities.HasVigilance(voice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Indestructible static (ten or more +1/+1 counters)
    // -----------------------------------------------------------------------

    [Fact]
    public void VoiceOfTheBlessed_IndestructibleStatic_AppearsAtTenCounters_LiftsBelow()
    {
        var svc = new ContinuousEffectsService();
        var voice = VoiceOfTheBlessedFactory.Create(
            _alice, triggers: null, replacements: null, eventBus: null, continuousEffects: svc);
        voice.ActiveEffects = svc;
        AddToBattlefield(voice);

        // 9 counters — flying/vigilance yes, but no indestructible.
        voice.Counters.Add(CounterType.PlusOnePlusOne, 9);
        CombatAbilities.HasFlying(voice).Should().BeTrue("9 >= 4 so flying is on");
        CombatAbilities.HasIndestructible(voice).Should().BeFalse(
            "below 10 +1/+1 counters Voice is not indestructible (CR 702.12)");

        // 10th counter — indestructible appears (flying/vigilance still on).
        voice.Counters.Add(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasIndestructible(voice).Should().BeTrue(
            "at 10 +1/+1 counters Voice has indestructible (CR 613.1f / 702.12)");
        CombatAbilities.HasFlying(voice).Should().BeTrue("flying remains at 10 counters");
        CombatAbilities.HasVigilance(voice).Should().BeTrue("vigilance remains at 10 counters");

        // Drop below 10 — indestructible lifts; flying/vigilance remain.
        voice.Counters.Remove(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasIndestructible(voice).Should().BeFalse(
            "indestructible lifts once the count drops below 10 (CR 122.6)");
        CombatAbilities.HasFlying(voice).Should().BeTrue("flying still on at 9 counters");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void AddToBattlefield(Majik.Core.Cards.Permanent p)
    {
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
    }
}
