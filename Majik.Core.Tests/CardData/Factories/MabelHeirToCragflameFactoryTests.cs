using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MabelHeirToCragflameFactory"/>.
///
/// Mabel, Heir to Cragflame — {1}{R}{W} Legendary Creature — Mouse Soldier 3/3.
/// Oracle text (verified against Scryfall, 2026-06-24):
///   "Other Mice you control get +1/+1.
///    When Mabel enters, create Cragflame, a legendary colorless Equipment
///    artifact token with 'Equipped creature gets +1/+1 and has vigilance,
///    trample, and haste' and equip {2}."
///
/// Covers:
///   - Identity: {1}{R}{W} Legendary R/W Mouse Soldier 3/3, mana value 3.
///   - Lord static: "Other Mice you control get +1/+1" (CR 613.7c) — buffs
///     other Mice, NOT non-Mice, and NOT Mabel herself ("Other"); opponent
///     Mice are unaffected.
///   - ETB trigger: "When Mabel enters …" creates the Cragflame Equipment token
///     (legendary colourless Equipment artifact, equip {2}, +1/+1 & granted
///     vigilance/trample/haste to the equipped creature).
/// </summary>
[Trait("Color", "M")]
public class MabelHeirToCragflameFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewMouse(Player controller, string name, int p = 2, int t = 2)
    {
        var c = new Creature(name, "{1}", p, t, subtypes: new[] { CardSubtype.Mouse });
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    private static Creature NewNonMouse(Player controller, string name, int p = 2, int t = 2)
    {
        var c = new Creature(name, "{1}", p, t, subtypes: new[] { CardSubtype.Soldier });
        c.SetOwner(controller);
        c.SetController(controller);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Mabel_Identity_LegendaryRedWhiteMouseSoldier_AtCost1RW()
    {
        var card = MabelHeirToCragflameFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Mabel, Heir to Cragflame");
        card.ManaCost.Should().Be("{1}{R}{W}");
        card.ManaCostValue.TotalValue.Should().Be(3, "{1}{R}{W} is mana value 3");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mouse).Should().BeTrue();
        card.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.White);
    }

    // -----------------------------------------------------------------------
    // Lord static — "Other Mice you control get +1/+1" (CR 613.7c).
    // -----------------------------------------------------------------------

    [Fact]
    public void Mabel_BuffsOtherMiceYouControl_NotHerselfNotNonMiceNotOpponents()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        var card = MabelHeirToCragflameFactory.Create(_alice, effects, triggers: null, zones: null);
        card.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Another Mouse you control — gets +1/+1.
        var myMouse = NewMouse(_alice, "Scurry", 2, 2);
        myMouse.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(myMouse);
        myMouse.SetZone(ZoneType.Battlefield);

        // A non-Mouse you control — unaffected.
        var mySoldier = NewNonMouse(_alice, "Footman", 2, 2);
        mySoldier.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(mySoldier);
        mySoldier.SetZone(ZoneType.Battlefield);

        // An opponent's Mouse — unaffected (controller-scoped).
        var oppMouse = NewMouse(_bob, "Vermin", 2, 2);
        oppMouse.ActiveEffects = effects;
        _bob.Zones.Battlefield.AddCard(oppMouse);
        oppMouse.SetZone(ZoneType.Battlefield);

        myMouse.Power.Should().Be(3, "other Mouse you control gets +1/+1");
        myMouse.Toughness.Should().Be(3);

        mySoldier.Power.Should().Be(2, "non-Mouse is unaffected");
        mySoldier.Toughness.Should().Be(2);

        oppMouse.Power.Should().Be(2, "opponent's Mouse is unaffected");
        oppMouse.Toughness.Should().Be(2);

        card.Power.Should().Be(3, "Mabel does not buff herself ('Other')");
        card.Toughness.Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — "When Mabel enters, create Cragflame …" (CR 603.1).
    // -----------------------------------------------------------------------

    [Fact]
    public void Mabel_HasEnterTriggeredAbility()
    {
        var card = MabelHeirToCragflameFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Etb_CreatesCragflame_LegendaryColorlessEquipmentArtifactToken_WithEquip2()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        var card = MabelHeirToCragflameFactory.Create(_alice, effects, triggers: null, zones: null);
        card.ActiveEffects = effects;

        // Place on battlefield so the trigger's active-zone guard is satisfied
        // and the ETB effect runs against the live controller's zones (same
        // direct-resolve pattern as the Esika's Chariot ETB test).
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var etb = card.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CardMovedEvent(
                card, ZoneType.Hand, ZoneType.Battlefield)));
        foreach (var effect in etb.Effects) effect.Execute();

        var cragflame = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .SingleOrDefault(a => a.IsToken && a.Name == "Cragflame");

        cragflame.Should().NotBeNull("the ETB mints the Cragflame token");
        cragflame!.HasSupertype(CardSupertype.Legendary).Should().BeTrue("legendary Equipment token");
        cragflame.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
        cragflame.HasType(CardType.Artifact).Should().BeTrue();
        CardColors.GetColors(cragflame).Should().BeEmpty("colourless artifact token");

        cragflame.Abilities.OfType<EquipActivatedAbility>().Should().ContainSingle(
            "Cragflame has equip {2}");
    }

    [Fact]
    public void Cragflame_EquippedCreature_GetsPlus1Plus1AndVigilanceTrampleHaste()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        // Mint a Cragflame directly (the same token the ETB creates) and attach
        // it to a creature, then assert the equipped-creature buff + keywords.
        var cragflame = MabelHeirToCragflameFactory.CreateCragflame(_alice, effects, zones: null);

        var bearer = NewNonMouse(_alice, "Bearer", 2, 2);
        bearer.ActiveEffects = effects;
        _alice.Zones.Battlefield.AddCard(bearer);
        bearer.SetZone(ZoneType.Battlefield);

        // Attach Cragflame to the bearer (the equip resolution outcome).
        cragflame.AttachTo(bearer);

        bearer.Power.Should().Be(3, "equipped creature gets +1/+1");
        bearer.Toughness.Should().Be(3);
        bearer.HasEffectiveKeyword("Vigilance").Should().BeTrue();
        bearer.HasEffectiveKeyword("Trample").Should().BeTrue();
        bearer.HasEffectiveKeyword("Haste").Should().BeTrue();
    }
}
