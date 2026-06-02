using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Artifact = Majik.Core.Cards.Artifact;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WardenOfTheInnerSkyFactory"/> (Murders at Karlov
/// Manor, {W}).
///
/// Card: Warden of the Inner Sky — Creature — Human Soldier 1/2.
/// Oracle (verified against Scryfall):
///   "As long as this creature has three or more counters on it, it has flying
///    and vigilance.
///    Tap three untapped artifacts and/or creatures you control: Put a +1/+1
///    counter on this creature. Scry 1. Activate only as a sorcery."
///
/// Covers:
/// - Identity (Creature — Human Soldier, {W}, 1/2).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Activated ability shape: single tap-three cost, sorcery speed, no target.
/// - Tap-three cost: can't pay with fewer than three untapped artifacts/
///   creatures; pays by tapping a mix of artifacts and creatures.
/// - Resolution: +1/+1 counter placed on Warden.
/// - Counter-threshold Flying + Vigilance static: neither below 3 counters;
///   both at >= 3 counters; lift again when counters drop below 3.
/// </summary>
public class WardenOfTheInnerSkyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WardenOfTheInnerSky_Identity()
    {
        var w = WardenOfTheInnerSkyFactory.Create(_alice);

        w.Name.Should().Be("Warden of the Inner Sky");
        w.ManaCost.Should().Be("{W}");
        w.HasType(CardType.Creature).Should().BeTrue();
        w.HasSubtype(CardSubtype.Human).Should().BeTrue();
        w.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        w.BasePower.Should().Be(1);
        w.BaseToughness.Should().Be(2);
        w.Owner.Should().BeSameAs(_alice);
        w.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WardenOfTheInnerSky_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Warden of the Inner Sky", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Warden of the Inner Sky");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WardenOfTheInnerSky_HasExactlyOneSorcerySpeedActivatedAbility()
    {
        var w = WardenOfTheInnerSkyFactory.Create(_alice);

        var activated = w.Abilities.OfType<ActivatedAbility>().Single();
        activated.IsSorcerySpeed.Should().BeTrue(
            "\"Activate only as a sorcery\" (CR 117.1a / 307.5)");
        activated.TargetRequests.Should().BeEmpty(
            "the ability has no target — it only affects Warden itself + scry");
    }

    [Fact]
    public void WardenOfTheInnerSky_ActivatedAbility_HasTapThreeCost()
    {
        var w = WardenOfTheInnerSkyFactory.Create(_alice);
        var activated = w.Abilities.OfType<ActivatedAbility>().Single();

        var cost = activated.Costs.OfType<TapUntappedArtifactsOrCreaturesCost>().Single();
        cost.Count.Should().Be(3, "Tap three untapped artifacts and/or creatures you control");
    }

    // -----------------------------------------------------------------------
    // Cost payability — artifacts and/or creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void WardenOfTheInnerSky_Cost_CannotPay_WithTwoPermanents()
    {
        var w = WardenOfTheInnerSkyFactory.Create(_alice);
        AddToBattlefield(w);

        // Warden (creature) + one artifact = only two untapped permanents.
        var art = MakeArtifact("Ornithopter");
        AddToBattlefield(art);

        var cost = w.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<TapUntappedArtifactsOrCreaturesCost>().Single();

        cost.CanPay(_alice).Should().BeFalse(
            "only two untapped artifacts/creatures — can't pay tap-three");
    }

    [Fact]
    public void WardenOfTheInnerSky_Cost_CanPay_WithMixOfArtifactsAndCreatures_AndTapsThree()
    {
        var w = WardenOfTheInnerSkyFactory.Create(_alice);
        AddToBattlefield(w);

        var art = MakeArtifact("Memnite");
        var bear = MakeCreature("Grizzly Bears", 2, 2);
        AddToBattlefield(art);
        AddToBattlefield(bear);

        var cost = w.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<TapUntappedArtifactsOrCreaturesCost>().Single();

        cost.CanPay(_alice).Should().BeTrue(
            "Warden + one artifact + one creature = three eligible untapped permanents");

        // Explicitly choose the mix so the cost taps the artifact AND the
        // creature (and Warden itself).
        cost.Targets = new[] { (Majik.Core.Cards.Permanent)w, art, bear };
        cost.Pay(_alice);

        w.IsTapped.Should().BeTrue("Warden may tap itself as one of the three");
        art.IsTapped.Should().BeTrue("the artifact was tapped");
        bear.IsTapped.Should().BeTrue("the creature was tapped");
    }

    // -----------------------------------------------------------------------
    // Resolution — +1/+1 counter on Warden
    // -----------------------------------------------------------------------

    [Fact]
    public void WardenOfTheInnerSky_Resolve_AddsPlusOnePlusOneCounter()
    {
        var w = WardenOfTheInnerSkyFactory.Create(_alice);
        AddToBattlefield(w);

        var activated = w.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in activated.Effects) fx.Execute();

        w.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "resolution puts one +1/+1 counter on Warden");
    }

    // -----------------------------------------------------------------------
    // Counter-threshold Flying + Vigilance static (CR 613.1f / 702.9 / 702.20)
    // -----------------------------------------------------------------------

    [Fact]
    public void WardenOfTheInnerSky_FlyingVigilanceStatic_AppearsAtThreeCounters_LiftsBelow()
    {
        var svc = new ContinuousEffectsService();
        var w = WardenOfTheInnerSkyFactory.Create(
            _alice, replacements: null, eventBus: null, continuousEffects: svc);
        w.ActiveEffects = svc;
        AddToBattlefield(w);

        // 0 counters — neither keyword.
        CombatAbilities.HasFlying(w).Should().BeFalse(
            "below 3 counters Warden has no flying (CR 702.9)");
        CombatAbilities.HasVigilance(w).Should().BeFalse(
            "below 3 counters Warden has no vigilance (CR 702.20)");

        // 2 counters — still neither.
        w.Counters.Add(CounterType.PlusOnePlusOne, 2);
        CombatAbilities.HasFlying(w).Should().BeFalse("2 counters is below the threshold");
        CombatAbilities.HasVigilance(w).Should().BeFalse("2 counters is below the threshold");

        // 3rd counter — both keywords appear.
        w.Counters.Add(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasFlying(w).Should().BeTrue(
            "at 3 counters Warden has flying (CR 613.1f / 702.9)");
        CombatAbilities.HasVigilance(w).Should().BeTrue(
            "at 3 counters Warden has vigilance (CR 613.1f / 702.20)");

        // Drop below threshold — both lift.
        w.Counters.Remove(CounterType.PlusOnePlusOne, 1);
        CombatAbilities.HasFlying(w).Should().BeFalse(
            "flying lifts once the count drops below 3 (CR 122.6)");
        CombatAbilities.HasVigilance(w).Should().BeFalse(
            "vigilance lifts once the count drops below 3 (CR 122.6)");
    }

    [Fact]
    public void WardenOfTheInnerSky_FlyingVigilanceStatic_CountsAnyCounterType()
    {
        // Oracle reads "three or more counters" (any kind) — three Charge
        // counters trip the threshold just as +1/+1 counters would (CR 122.1).
        var svc = new ContinuousEffectsService();
        var w = WardenOfTheInnerSkyFactory.Create(
            _alice, replacements: null, eventBus: null, continuousEffects: svc);
        w.ActiveEffects = svc;
        AddToBattlefield(w);

        w.Counters.Add(CounterType.Charge, 3);
        CombatAbilities.HasFlying(w).Should().BeTrue(
            "three counters of ANY kind satisfy \"three or more counters\" (CR 122.1)");
        CombatAbilities.HasVigilance(w).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void AddToBattlefield(Majik.Core.Cards.Permanent p)
    {
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
    }

    private Artifact MakeArtifact(string name)
    {
        var a = new Artifact(name, "{0}");
        a.SetOwner(_alice);
        a.SetController(_alice);
        return a;
    }

    private Creature MakeCreature(string name, int power, int toughness)
    {
        var c = new Creature(name, "{1}{G}", power, toughness);
        c.SetOwner(_alice);
        c.SetController(_alice);
        return c;
    }
}
