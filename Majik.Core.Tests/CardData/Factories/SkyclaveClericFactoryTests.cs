using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for the modal double-faced card
/// Skyclave Cleric // Skyclave Basilica (Zendikar Rising).
///
/// Oracle text (verified against Scryfall):
///   Front — Skyclave Cleric, Creature — Kor Cleric, {1}{W}, 1/3:
///     "When this creature enters, you gain 2 life."
///   Back — Skyclave Basilica, Land:
///     "This land enters tapped."
///     "{T}: Add {W}."
///
/// The front face is a 1/3 body with an ETB gain-2-life trigger; the back
/// face is an unconditional enters-tapped mana land. MDFC cast-either-face
/// wiring mirrors <see cref="AkoumWarriorFactory"/> (front carries a castable
/// <see cref="Majik.Core.CardData.MDFCs.MdfcFace.Land"/> back-face descriptor).
/// </summary>
[Trait("Color", "W")]
public class SkyclaveClericFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Front face — Skyclave Cleric identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveCleric_Identity_CreatureKorCleric_1_3_White1W()
    {
        var cleric = SkyclaveClericFactory.Create(_alice);

        cleric.Name.Should().Be("Skyclave Cleric");
        cleric.HasType(CardType.Creature).Should().BeTrue();
        cleric.HasType(CardType.Land).Should().BeFalse();
        cleric.ManaCost.Should().Be("{1}{W}");
        cleric.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(cleric).Should().Contain(ManaColor.White);
        cleric.Power.Should().Be(1);
        cleric.Toughness.Should().Be(3);
        cleric.Subtypes.Should().Contain(CardSubtype.Kor);
        cleric.Subtypes.Should().Contain(CardSubtype.Cleric);
        cleric.Owner.Should().BeSameAs(_alice);
        cleric.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SkyclaveCleric_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Skyclave Cleric", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Skyclave Cleric");
    }

    [Fact]
    public void SkyclaveCleric_HasMdfcState_WithCastableLandBackFace()
    {
        var cleric = SkyclaveClericFactory.Create(_alice);

        // CR 712.3 — front-face card carries the castable back-face descriptor.
        cleric.MdfcState.Should().NotBeNull();
        cleric.MdfcState!.FrontFaceName.Should().Be("Skyclave Cleric");
        cleric.MdfcState.BackFaceName.Should().Be("Skyclave Basilica");
        cleric.MdfcState.IsBackFace.Should().BeFalse("the creature is the front face");
        cleric.MdfcState.CastableBackFace.Should().NotBeNull();
        cleric.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        cleric.MdfcState.CastableBackFace.Name.Should().Be("Skyclave Basilica");
    }

    // -----------------------------------------------------------------------
    // Front face — ETB gain-2-life trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveCleric_HasSingleEtbTrigger_BattlefieldActive()
    {
        var cleric = SkyclaveClericFactory.Create(_alice);

        var trigger = cleric.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void SkyclaveCleric_EtbEffect_GainsExactlyTwoLife()
    {
        // CR 119.3 — "you gain 2 life" raises the controller's life total by 2.
        var alice = new Player("Alice", 20);
        var cleric = SkyclaveClericFactory.Create(alice);
        var etb = cleric.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(22, "Skyclave Cleric's ETB gains its controller 2 life");
    }

    // -----------------------------------------------------------------------
    // Back face — Skyclave Basilica identity + mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveBasilica_Identity_Land_TapsForWhite_BackFace()
    {
        var basilica = SkyclaveBasilicaFactory.Create(_alice);

        basilica.Name.Should().Be("Skyclave Basilica");
        basilica.HasType(CardType.Land).Should().BeTrue();
        basilica.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Skyclave Basilica is a non-Basic land");
        basilica.Owner.Should().BeSameAs(_alice);
        basilica.Controller.Should().BeSameAs(_alice);

        // Pre-flipped to the back face — the land is the back face that exists.
        basilica.MdfcState.Should().NotBeNull();
        basilica.MdfcState!.IsBackFace.Should().BeTrue();
        basilica.MdfcState.ActiveFaceName.Should().Be("Skyclave Basilica");

        // {T}: Add {W} — single mana ability producing one white.
        var mana = basilica.Abilities.OfType<ManaAbility>().Should().ContainSingle().Subject;
        mana.ManaGenerated.White.Should().Be(1);
        mana.ManaGenerated.TotalValue.Should().Be(1);
    }

    [Fact]
    public void SkyclaveBasilica_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Skyclave Basilica", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Skyclave Basilica");
    }

    [Fact]
    public void SkyclaveBasilica_EntersTapped_ViaReplacementBus()
    {
        var bus = new ReplacementBus();
        var basilica = SkyclaveBasilicaFactory.Create(_alice, bus);

        // CR 614.1c — unconditional "this land enters tapped" replacement is
        // registered on the bus. Drive the ETB intent through it and confirm
        // EntersTapped is set.
        var intent = new ZoneMoveIntent(
            Card: basilica,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var replaced = bus.Apply(intent);
        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue(
            "Skyclave Basilica always enters tapped (CR 614.1c)");
    }
}
