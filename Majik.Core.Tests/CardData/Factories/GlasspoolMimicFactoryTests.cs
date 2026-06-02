using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for the modal double-faced card
/// Glasspool Mimic // Glasspool Shore (Zendikar Rising).
///
/// Oracle text (verified against Scryfall):
///   Front — Glasspool Mimic, Creature — Shapeshifter Rogue, {2}{U}, 0/0:
///     "You may have this creature enter as a copy of a creature you control,
///      except it's a Shapeshifter Rogue in addition to its other types."
///   Back — Glasspool Shore, Land:
///     "This land enters tapped."
///     "{T}: Add {U}."
///
/// The front face is a clone (enters as a copy of a creature you control,
/// CR 706.10) with a Layer-4 Shapeshifter-Rogue type-adding rider
/// (CR 613.1d); the back face is an unconditional enters-tapped mana land.
/// MDFC cast-either-face wiring mirrors <see cref="AkoumWarriorFactory"/>
/// (front carries a castable
/// <see cref="Majik.Core.CardData.MDFCs.MdfcFace.Land"/> back-face descriptor);
/// the clone wiring mirrors <see cref="PhantasmalImageFactory"/> (pool tightened
/// to "you control").
/// </summary>
[Trait("Color", "U")]
public class GlasspoolMimicFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Front face — Glasspool Mimic identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GlasspoolMimic_Identity_ShapeshifterRogue_0_0_Blue2U()
    {
        var mimic = GlasspoolMimicFactory.Create(_alice);

        mimic.Name.Should().Be("Glasspool Mimic");
        mimic.HasType(CardType.Creature).Should().BeTrue();
        mimic.HasType(CardType.Land).Should().BeFalse();
        mimic.ManaCost.Should().Be("{2}{U}");
        mimic.ManaCostValue.TotalValue.Should().Be(3);
        CardColors.GetColors(mimic).Should().Contain(ManaColor.Blue);
        mimic.BasePower.Should().Be(0, "printed 0/0 per CR 706.10 — copy overwrites P/T at ETB");
        mimic.BaseToughness.Should().Be(0);
        mimic.Subtypes.Should().Contain(CardSubtype.Shapeshifter);
        mimic.Subtypes.Should().Contain(CardSubtype.Rogue);
        mimic.Owner.Should().BeSameAs(_alice);
        mimic.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GlasspoolMimic_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Glasspool Mimic", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Glasspool Mimic");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        card.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
    }

    [Fact]
    public void GlasspoolMimic_HasMdfcState_WithCastableLandBackFace()
    {
        var mimic = GlasspoolMimicFactory.Create(_alice);

        // CR 712.3 — front-face card carries the castable back-face descriptor.
        mimic.MdfcState.Should().NotBeNull();
        mimic.MdfcState!.FrontFaceName.Should().Be("Glasspool Mimic");
        mimic.MdfcState.BackFaceName.Should().Be("Glasspool Shore");
        mimic.MdfcState.IsBackFace.Should().BeFalse("the creature is the front face");
        mimic.MdfcState.CastableBackFace.Should().NotBeNull();
        mimic.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        mimic.MdfcState.CastableBackFace.Name.Should().Be("Glasspool Shore");
    }

    // -----------------------------------------------------------------------
    // Front face — enters-as-copy of a creature you control (CR 706.10)
    // -----------------------------------------------------------------------

    [Fact]
    public void GlasspoolMimic_EntersAsCopyOfBear_StatCopiedTo_2_2()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // A vanilla Bear the controller controls as the copy source.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var mimic = GlasspoolMimicFactory.Create(_alice, replacements: bus, effects: effects);
        mimic.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mimic);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(mimic, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CopyEffect copied the bear's printed P/T onto the mimic.
        mimic.Power.Should().Be(2, "Glasspool Mimic enters as a copy of Grizzly Bears");
        mimic.Toughness.Should().Be(2);
        mimic.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void GlasspoolMimic_EntersAsCopy_ShapeshifterRogueSubtypesPresent()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // Copy source: a Bear that is neither Shapeshifter nor Rogue.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var mimic = GlasspoolMimicFactory.Create(_alice, replacements: bus, effects: effects);
        mimic.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mimic);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(mimic, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CR 613.1d — "except it's a Shapeshifter Rogue in addition to its
        // other types". The printed subtypes survive (CopyEffect at v1 does
        // not overwrite subtypes); the Layer-4 AddSubtypeEffect riders also
        // keep both subtypes in the computed characteristics.
        mimic.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        mimic.HasSubtype(CardSubtype.Rogue).Should().BeTrue();

        var computed = effects.Compute(mimic);
        computed.Subtypes.Should().Contain(CardSubtype.Shapeshifter,
            "Layer 4 AddSubtypeEffect adds Shapeshifter to the working characteristics");
        computed.Subtypes.Should().Contain(CardSubtype.Rogue,
            "Layer 4 AddSubtypeEffect adds Rogue to the working characteristics");
    }

    [Fact]
    public void GlasspoolMimic_NoCopyCandidates_EntersAsPrintedZeroZero()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // No creature the controller controls → EntersAsCopyReplacement's
        // PickSource returns null → no CopyEffect registered → mimic enters as
        // its printed 0/0. v1 stand-in for declining the "may" (the
        // replacement is auto-yes-when-able — see EntersAsCopyReplacement).
        var mimic = GlasspoolMimicFactory.Create(_alice, replacements: bus, effects: effects);
        mimic.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mimic);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(mimic, ZoneType.Hand, ZoneType.Battlefield, _alice);

        mimic.Power.Should().Be(0, "no copy source available → printed 0/0");
        mimic.Toughness.Should().Be(0);
        mimic.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        mimic.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        mimic.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Back face — Glasspool Shore identity + mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void GlasspoolShore_Identity_Land_TapsForBlue_BackFace()
    {
        var shore = GlasspoolShoreFactory.Create(_alice);

        shore.Name.Should().Be("Glasspool Shore");
        shore.HasType(CardType.Land).Should().BeTrue();
        shore.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Glasspool Shore is a non-Basic land");
        shore.Owner.Should().BeSameAs(_alice);
        shore.Controller.Should().BeSameAs(_alice);

        // Pre-flipped to the back face — the land is the back face that exists.
        shore.MdfcState.Should().NotBeNull();
        shore.MdfcState!.IsBackFace.Should().BeTrue();
        shore.MdfcState.ActiveFaceName.Should().Be("Glasspool Shore");

        // {T}: Add {U} — single mana ability producing one blue.
        var mana = shore.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().ContainSingle();
        mana[0].ManaGenerated.Blue.Should().BeGreaterThan(0, "produces blue mana");
        mana[0].ManaGenerated.TotalValue.Should().Be(1);
    }

    [Fact]
    public void GlasspoolShore_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Glasspool Shore", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Glasspool Shore");
    }

    [Fact]
    public void GlasspoolShore_EntersTapped_ViaReplacementBus()
    {
        var bus = new ReplacementBus();
        var shore = GlasspoolShoreFactory.Create(_alice, bus);

        // CR 614.1c — unconditional "this land enters tapped" replacement is
        // registered on the bus. Drive the ETB intent through it and confirm
        // EntersTapped is set.
        var intent = new ZoneMoveIntent(
            Card: shore,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var replaced = bus.Apply(intent);
        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue(
            "Glasspool Shore always enters tapped (CR 614.1c)");
    }
}
