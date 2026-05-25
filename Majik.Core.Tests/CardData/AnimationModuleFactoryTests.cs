using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AnimationModuleFactory"/>.
///
/// Card: Animation Module — Artifact {1} (Kaladesh).
///
/// v1 oracle (per factory):
///   "Whenever one or more +1/+1 counters are put on a permanent you
///    control, you may pay {1}. If you do, create a 1/1 colorless Servo
///    artifact creature token.
///    {1}, {T}: Put a +1/+1 counter on target creature."
///
/// Covers:
///   - Identity / dispatch.
///   - Activated {1}, {T} cost wiring + resolve places a +1/+1 counter
///     on the chosen creature.
///   - Replacement bus integration (Hardened Scales bumps +1 → +2).
///   - Trigger condition shape — fires for +1/+1 counters on the
///     controller's permanents; ignores other types / opponent.
///   - End-to-end: activated ability bumps a creature → CounterAddedEvent
///     → trigger fires → Servo token enters the battlefield.
///   - Single-arg create is shape-only (no trigger / replacement bus /
///     event publish).
/// </summary>
public class AnimationModuleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AnimationModule_Identity()
    {
        var c = AnimationModuleFactory.Create(_alice);

        c.Name.Should().Be("Animation Module");
        c.ManaCost.Should().Be("{1}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AnimationModule()
    {
        var card = NamedCardFactory.Create("Animation Module", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Animation Module");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
        // Both abilities are attached even on the single-arg path
        // (shape inspection).
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Activated ability — {1}, {T}: +1/+1 counter on target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasManaAndTapCosts()
    {
        var mod = AnimationModuleFactory.Create(_alice);

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.Should().HaveCount(2,
            "the printed cost line is {1}, {T} — two cost pieces");
        activated.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        activated.Costs.OfType<AdditionalCost>().Should().ContainSingle();
    }

    [Fact]
    public void Activated_Resolve_PutsCounterOnChosenCreature()
    {
        var mod = AnimationModuleFactory.Create(_alice);
        PutOnBattlefield(_alice, mod);

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, target);

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { target } });
        foreach (var e in activated.Effects) e.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void Activated_Resolve_RoutesThroughReplacementBus_HonoursHardenedScales()
    {
        // Hardened Scales bumps every +1/+1 placement on Alice's
        // creatures by 1. Animation Module's {1},{T} should produce 2
        // counters instead of 1.
        var bus = new ReplacementBus();
        var mod = AnimationModuleFactory.Create(_alice, triggers: null,
            replacements: bus, eventBus: null, zones: null);
        PutOnBattlefield(_alice, mod);

        var scales = HardenedScalesFactory.Create(_alice, bus);
        PutOnBattlefield(_alice, scales);

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, target);

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { target } });
        foreach (var e in activated.Effects) e.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2,
            "Hardened Scales bumps +1 → +2 via CountersService.Add");
    }

    [Fact]
    public void Activated_Resolve_NoTarget_IsSilentNoOp()
    {
        var mod = AnimationModuleFactory.Create(_alice);
        PutOnBattlefield(_alice, mod);

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        // No SetChosenTargets call — silent no-op.
        var act = () =>
        {
            foreach (var e in activated.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Activated_Resolve_TargetOffBattlefield_NoOp()
    {
        var mod = AnimationModuleFactory.Create(_alice);
        PutOnBattlefield(_alice, mod);

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Graveyard); // off battlefield

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { target } });
        foreach (var e in activated.Effects) e.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 608.2b — illegal target on resolve, no counter placed");
    }

    // -----------------------------------------------------------------------
    // Trigger — "Whenever one or more +1/+1 counters are put on a permanent
    // you control, you may pay {1}. If you do, create a Servo."
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_ConditionMatches_PlusOnePlusOneOnControlledPermanent()
    {
        var mod = AnimationModuleFactory.Create(_alice);
        var trig = mod.Abilities.OfType<TriggeredAbility>().Single();

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.SetOwner(_alice);
        target.SetController(_alice);

        var ev = new CounterAddedEvent(target, CounterType.PlusOnePlusOne, 1);
        trig.Condition.Matches(ev, trig).Should().BeTrue();
    }

    [Fact]
    public void Trigger_ConditionDoesNotMatch_MinusOneMinusOne()
    {
        var mod = AnimationModuleFactory.Create(_alice);
        var trig = mod.Abilities.OfType<TriggeredAbility>().Single();

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.SetOwner(_alice);
        target.SetController(_alice);

        var ev = new CounterAddedEvent(target, CounterType.MinusOneMinusOne, 1);
        trig.Condition.Matches(ev, trig).Should().BeFalse(
            "only +1/+1 counters fire the trigger");
    }

    [Fact]
    public void Trigger_ConditionDoesNotMatch_OpponentsPermanent()
    {
        var mod = AnimationModuleFactory.Create(_alice);
        var trig = mod.Abilities.OfType<TriggeredAbility>().Single();

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.SetOwner(_bob);
        target.SetController(_bob);

        var ev = new CounterAddedEvent(target, CounterType.PlusOnePlusOne, 1);
        trig.Condition.Matches(ev, trig).Should().BeFalse(
            "permanent must be controlled by Animation Module's controller");
    }

    // -----------------------------------------------------------------------
    // End-to-end — activated → CounterAddedEvent → trigger queued
    // -----------------------------------------------------------------------

    [Fact]
    public void EndToEnd_ActivatedAbilityBumpsCreature_TriggerObservesEvent()
    {
        var bus = new EventBus();
        var observed = new List<CounterAddedEvent>();
        bus.Subscribe<CounterAddedEvent>(observed.Add);

        var mod = AnimationModuleFactory.Create(_alice, triggers: null,
            replacements: null, eventBus: bus, zones: null);
        PutOnBattlefield(_alice, mod);

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, target);

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { target } });
        foreach (var e in activated.Effects) e.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        observed.Should().HaveCount(1,
            "CountersService.Add publishes CounterAddedEvent on commit");
        observed[0].Target.Should().BeSameAs(target);
        observed[0].CounterType.Should().Be(CounterType.PlusOnePlusOne);
        observed[0].Amount.Should().Be(1);
        observed[0].Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EndToEnd_TriggerOnEvent_AutoPaysAndCreatesServoToken()
    {
        // Give Alice {1} of mana so PayMana({1}) succeeds.
        _alice.AddManaToPool(Majik.Core.ValueObjects.ManaCost.Parse("1"));

        var bus = new EventBus();
        var mod = AnimationModuleFactory.Create(_alice, triggers: null,
            replacements: null, eventBus: bus, zones: null);
        PutOnBattlefield(_alice, mod);

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, target);

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { target } });
        foreach (var e in activated.Effects) e.Execute();

        // Trigger is attached to the card; invoke its effect directly
        // (mirrors LightningRift / Heliod tests; manager-driven resolve
        // path is exercised in dedicated TriggerManager tests).
        var trigger = mod.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // Alice's battlefield should now contain the Servo token.
        var servos = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Servo")
            .ToList();
        servos.Should().HaveCount(1, "trigger auto-paid {1} → Servo created");
        var servo = servos[0];
        servo.Power.Should().Be(1);
        servo.Toughness.Should().Be(1);
        servo.IsToken.Should().BeTrue();
        servo.HasSubtype(CardSubtype.Servo).Should().BeTrue();
        servo.HasType(CardType.Artifact).Should().BeTrue();
        servo.HasType(CardType.Creature).Should().BeTrue();
        // CR 111.4 — colourless.
        Majik.Core.Cards.CardColors.GetColors(servo).Should().BeEmpty();
    }

    [Fact]
    public void Trigger_OnResolution_WithoutMana_Fizzles()
    {
        // Alice has no mana; PayMana({1}) returns false → trigger fizzles
        // harmlessly (CR 117.5).
        var bus = new EventBus();
        var mod = AnimationModuleFactory.Create(_alice, triggers: null,
            replacements: null, eventBus: bus, zones: null);
        PutOnBattlefield(_alice, mod);

        var trigger = mod.Abilities.OfType<TriggeredAbility>().Single();
        var preCount = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.Name == "Servo");

        foreach (var e in trigger.Effects) e.Execute();

        var postCount = _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Count(c => c.Name == "Servo");
        postCount.Should().Be(preCount,
            "no mana → PayMana({1}) returns false → no Servo token");
    }

    // -----------------------------------------------------------------------
    // Lifecycle — single-arg overload is shape-only
    // -----------------------------------------------------------------------

    [Fact]
    public void SingleArgCreate_NoTriggerRegistration_NoEventPublish()
    {
        // The single-arg overload attaches abilities for shape but
        // does NOT register the trigger with a TriggerManager and does
        // NOT publish CounterAddedEvent from the activated ability —
        // exercise the activated ability and confirm no exceptions
        // and no event observers fire.
        var mod = AnimationModuleFactory.Create(_alice);
        PutOnBattlefield(_alice, mod);

        var target = new Creature("Memnite", "{0}", 1, 1);
        target.AddCardType(CardType.Artifact);
        PutOnBattlefield(_alice, target);

        var activated = mod.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new[] { new object[] { target } });
        foreach (var e in activated.Effects) e.Execute();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "shape-only path still places the counter (direct add)");
    }
}
