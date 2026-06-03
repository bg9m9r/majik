using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AbzanFalconerFactory"/> — Creature — Human Soldier
/// {2}{W} 2/3 (Khans of Tarkir). Oracle text (verified against Scryfall):
///   "Outlast {W} ({W}, {T}: Put a +1/+1 counter on this creature. Outlast
///    only as a sorcery.)
///    Each creature you control with a +1/+1 counter on it has flying."
///
/// Covers:
/// - Identity (name, {2}{W}, Human + Soldier subtypes, 2/3, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Team flying static: every creature the controller controls that has a
///   +1/+1 counter on it gains flying (CR 702.9 / 613.1f) — including the
///   Falconer itself.
/// - Creatures you control WITHOUT a +1/+1 counter do NOT gain flying.
/// - Opponents' creatures with +1/+1 counters do NOT gain flying
///   (controller-scoped "creature you control").
/// - Outlast activated ability: a sorcery-speed {W}, {T} cost that puts a
///   +1/+1 counter on the Falconer (CR 702.85).
/// </summary>
[Trait("Color", "W")]
public class AbzanFalconerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void AbzanFalconer_Identity()
    {
        var c = AbzanFalconerFactory.Create(_alice);

        c.Name.Should().Be("Abzan Falconer");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AbzanFalconer()
    {
        var card = NamedCardFactory.Create("Abzan Falconer", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Abzan Falconer");
        card.HasType(CardType.Creature).Should().BeTrue();
    }

    // ── Team flying static ──────────────────────────────────────────────

    [Fact]
    public void GrantsFlying_ToControllerCreatureWithPlusOneCounter()
    {
        var svc = new ContinuousEffectsService();

        var ally = MakeCreature(_alice, "Grizzly Bears");
        ally.ActiveEffects = svc;
        ally.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var falconer = AbzanFalconerFactory.Create(_alice, svc);
        falconer.SetZone(ZoneType.Battlefield);
        falconer.ActiveEffects = svc;

        ally.HasEffectiveKeyword("Flying").Should().BeTrue(
            "a creature you control with a +1/+1 counter on it has flying.");
    }

    [Fact]
    public void GrantsFlying_ToItself_WhenItHasAPlusOneCounter()
    {
        var svc = new ContinuousEffectsService();

        var falconer = AbzanFalconerFactory.Create(_alice, svc);
        falconer.SetZone(ZoneType.Battlefield);
        falconer.ActiveEffects = svc;
        falconer.Counters.Add(CounterType.PlusOnePlusOne, 1);

        falconer.HasEffectiveKeyword("Flying").Should().BeTrue(
            "the Falconer itself is a creature you control with a +1/+1 counter.");
    }

    [Fact]
    public void DoesNotGrantFlying_WithoutAPlusOneCounter()
    {
        var svc = new ContinuousEffectsService();

        var ally = MakeCreature(_alice, "Grizzly Bears");
        ally.ActiveEffects = svc;
        // No +1/+1 counter added.

        var falconer = AbzanFalconerFactory.Create(_alice, svc);
        falconer.SetZone(ZoneType.Battlefield);
        falconer.ActiveEffects = svc;

        ally.HasEffectiveKeyword("Flying").Should().BeFalse(
            "no +1/+1 counter → the creature does not gain flying.");
        falconer.HasEffectiveKeyword("Flying").Should().BeFalse(
            "the Falconer itself has no +1/+1 counter so it does not fly.");
    }

    [Fact]
    public void DoesNotGrantFlying_ToOpponentCreatureWithCounter()
    {
        var svc = new ContinuousEffectsService();

        var bobCreature = MakeCreature(_bob, "Bear Cub");
        bobCreature.ActiveEffects = svc;
        bobCreature.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var falconer = AbzanFalconerFactory.Create(_alice, svc);
        falconer.SetZone(ZoneType.Battlefield);
        falconer.ActiveEffects = svc;

        bobCreature.HasEffectiveKeyword("Flying").Should().BeFalse(
            "the static is scoped to 'each creature you control' — opponents' " +
            "creatures are not granted flying (CR 109.5).");
    }

    [Fact]
    public void FlyingLifts_WhenCountersRemoved()
    {
        var svc = new ContinuousEffectsService();

        var ally = MakeCreature(_alice, "Grizzly Bears");
        ally.ActiveEffects = svc;
        ally.Counters.Add(CounterType.PlusOnePlusOne, 1);

        var falconer = AbzanFalconerFactory.Create(_alice, svc);
        falconer.SetZone(ZoneType.Battlefield);
        falconer.ActiveEffects = svc;

        ally.HasEffectiveKeyword("Flying").Should().BeTrue();

        ally.Counters.Remove(CounterType.PlusOnePlusOne, 1);
        ally.HasEffectiveKeyword("Flying").Should().BeFalse(
            "once the +1/+1 counter is gone the creature no longer has flying.");
    }

    // ── Outlast activated ability ───────────────────────────────────────

    [Fact]
    public void HasOutlastActivatedAbility_SorcerySpeed()
    {
        var falconer = AbzanFalconerFactory.Create(_alice);

        var outlast = falconer.Abilities.OfType<ActivatedAbility>().ToList();
        outlast.Should().HaveCount(1,
            "Abzan Falconer has exactly one activated ability: Outlast {W}.");
        outlast[0].IsSorcerySpeed.Should().BeTrue(
            "Outlast can be activated only as a sorcery (CR 702.85b).");
    }

    [Fact]
    public void OutlastResolution_PutsPlusOneCounterOnSelf()
    {
        var falconer = AbzanFalconerFactory.Create(_alice);
        falconer.SetZone(ZoneType.Battlefield);

        var outlast = falconer.Abilities.OfType<ActivatedAbility>().Single();

        falconer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);

        // Resolve the effects directly (cost payment / tap exercised elsewhere).
        outlast.Resolve();

        falconer.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "Outlast puts a +1/+1 counter on this creature (CR 702.85a).");
    }
}
