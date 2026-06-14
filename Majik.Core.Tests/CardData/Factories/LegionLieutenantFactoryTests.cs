using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LegionLieutenantFactory"/>.
///
/// Legion Lieutenant (Rivals of Ixalan, {W}{B}). Creature — Vampire Knight
/// 2/2. Oracle (verified against Scryfall):
///   "Other Vampires you control get +1/+1."
///
/// Coverage (unique behaviour — the contract test already asserts dispatch
/// + well-formedness):
/// - Identity (name, cost, subtypes, P/T, colours).
/// - Lord static (CR 613.7c): other controller-Vampires get +1/+1.
/// - The Lieutenant does NOT buff itself (printed "Other", CR 109.5 self).
/// - Opponent's Vampire is NOT pumped (controller-scoped, CR 109.5).
/// - Non-Vampire creature is NOT pumped (subtype gate).
/// - LTB lifts the bonus (effect's IsActive gate falls on zone change).
/// - Two Lieutenants stack +1/+1.
/// </summary>
[Trait("Color", "M")]
public class LegionLieutenantFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeVampire(Player owner, string name = "Vampire Nighthawk")
    {
        var c = new Creature(name, "{1}{B}{B}", 2, 3, subtypes: new[] { CardSubtype.Vampire });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    [Fact]
    public void LegionLieutenant_Identity()
    {
        var c = LegionLieutenantFactory.Create(_alice);

        c.Name.Should().Be("Legion Lieutenant");
        c.ManaCost.Should().Be("{W}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White);
        colors.Should().Contain(ManaColor.Black);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LegionLieutenant_BuffsOtherControllerVampire_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherVamp = MakeVampire(_alice);
        otherVamp.ActiveEffects = svc;

        var lt = LegionLieutenantFactory.Create(_alice, svc);
        lt.SetZone(ZoneType.Battlefield);
        lt.ActiveEffects = svc;

        otherVamp.GetPower().Should().Be(3,
            "other Vampires controlled by the Lieutenant's controller get +1/+1 (2 → 3 power).");
        otherVamp.GetToughness().Should().Be(4,
            "the +1/+1 raises the 3-toughness Vampire to 4.");
    }

    [Fact]
    public void LegionLieutenant_DoesNotBuffItself()
    {
        // The Lieutenant is a Vampire, but the printed "Other" (includeSelf:
        // false) keeps it out of its own +1/+1 buff.
        var svc = new ContinuousEffectsService();

        var lt = LegionLieutenantFactory.Create(_alice, svc);
        lt.SetZone(ZoneType.Battlefield);
        lt.ActiveEffects = svc;

        lt.GetPower().Should().Be(2, "the printed 'Other' excludes the Lieutenant from its own buff.");
        lt.GetToughness().Should().Be(2);
    }

    [Fact]
    public void LegionLieutenant_DoesNotBuff_OpponentVampire()
    {
        var svc = new ContinuousEffectsService();

        var oppVamp = MakeVampire(_bob);
        oppVamp.ActiveEffects = svc;

        var lt = LegionLieutenantFactory.Create(_alice, svc);
        lt.SetZone(ZoneType.Battlefield);
        lt.ActiveEffects = svc;

        oppVamp.GetPower().Should().Be(2,
            "the anthem is scoped to its controller's Vampires (CR 109.5 — 'you').");
        oppVamp.GetToughness().Should().Be(3);
    }

    [Fact]
    public void LegionLieutenant_DoesNotBuff_NonVampire()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var lt = LegionLieutenantFactory.Create(_alice, svc);
        lt.SetZone(ZoneType.Battlefield);
        lt.ActiveEffects = svc;

        bear.GetPower().Should().Be(2, "the anthem only buffs creatures matching the Vampire subtype.");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void LegionLieutenant_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var otherVamp = MakeVampire(_alice);
        otherVamp.ActiveEffects = svc;

        var lt = LegionLieutenantFactory.Create(_alice, svc);
        lt.SetZone(ZoneType.Battlefield);
        lt.ActiveEffects = svc;

        otherVamp.GetPower().Should().Be(3);

        // Lieutenant dies — LordStaticEffect.IsActive() short-circuits when
        // the source isn't on the battlefield (CR 613).
        lt.SetZone(ZoneType.Graveyard);

        otherVamp.GetPower().Should().Be(2, "the +1/+1 lifts on LTB.");
        otherVamp.GetToughness().Should().Be(3);
    }

    [Fact]
    public void TwoLieutenants_StackPower()
    {
        var svc = new ContinuousEffectsService();

        var otherVamp = MakeVampire(_alice);
        otherVamp.ActiveEffects = svc;

        var lt1 = LegionLieutenantFactory.Create(_alice, svc);
        lt1.SetZone(ZoneType.Battlefield);
        lt1.ActiveEffects = svc;

        var lt2 = LegionLieutenantFactory.Create(_alice, svc);
        lt2.SetZone(ZoneType.Battlefield);
        lt2.ActiveEffects = svc;

        otherVamp.GetPower().Should().Be(4,
            "two Lieutenants stack +1/+1 — 2 base + 2 from two lords = 4.");
        otherVamp.GetToughness().Should().Be(5);
    }
}
