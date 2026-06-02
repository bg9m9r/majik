using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for the modal double-faced card
/// Akoum Warrior // Akoum Teeth (Zendikar Rising).
///
/// Oracle text (verified against Scryfall):
///   Front — Akoum Warrior, Creature — Minotaur Warrior, {5}{R}, 4/5:
///     "Trample"
///   Back — Akoum Teeth, Land:
///     "This land enters tapped."
///     "{T}: Add {R}."
///
/// The front face is a vanilla-with-Trample body; the back face is an
/// unconditional enters-tapped mana land. MDFC cast-either-face wiring mirrors
/// <see cref="KazanduMammothFactory"/> (front carries a castable
/// <see cref="Majik.Core.CardData.MDFCs.MdfcFace.Land"/> back-face descriptor).
/// </summary>
[Trait("Color", "R")]
public class AkoumWarriorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Front face — Akoum Warrior identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AkoumWarrior_Identity_CreatureMinotaurWarrior_4_5_Red5R()
    {
        var warrior = AkoumWarriorFactory.Create(_alice);

        warrior.Name.Should().Be("Akoum Warrior");
        warrior.HasType(CardType.Creature).Should().BeTrue();
        warrior.ManaCost.Should().Be("{5}{R}");
        warrior.ManaCostValue.TotalValue.Should().Be(6);
        CardColors.GetColors(warrior).Should().Contain(ManaColor.Red);
        warrior.Power.Should().Be(4);
        warrior.Toughness.Should().Be(5);
        warrior.Subtypes.Should().Contain(CardSubtype.Minotaur);
        warrior.Subtypes.Should().Contain(CardSubtype.Warrior);
        warrior.Owner.Should().BeSameAs(_alice);
        warrior.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AkoumWarrior_NamedCardFactory_Dispatch_ProducesCreature()
    {
        var card = NamedCardFactory.Create("Akoum Warrior", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Akoum Warrior");
    }

    [Fact]
    public void AkoumWarrior_HasTrample_KeywordMarker()
    {
        var warrior = AkoumWarriorFactory.Create(_alice);

        // CR 702.19 — Trample is present as a KeywordAbility marker and read
        // by the combat-keyword lookup.
        warrior.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Trample");
        CombatAbilities.HasTrample(warrior).Should().BeTrue();
    }

    [Fact]
    public void AkoumWarrior_HasMdfcState_WithCastableLandBackFace()
    {
        var warrior = AkoumWarriorFactory.Create(_alice);

        // CR 712.3 — front-face card carries the castable back-face descriptor.
        warrior.MdfcState.Should().NotBeNull();
        warrior.MdfcState!.FrontFaceName.Should().Be("Akoum Warrior");
        warrior.MdfcState.BackFaceName.Should().Be("Akoum Teeth");
        warrior.MdfcState.IsBackFace.Should().BeFalse("the creature is the front face");
        warrior.MdfcState.CastableBackFace.Should().NotBeNull();
        warrior.MdfcState.CastableBackFace!.IsLand.Should().BeTrue();
        warrior.MdfcState.CastableBackFace.Name.Should().Be("Akoum Teeth");
    }

    // -----------------------------------------------------------------------
    // Back face — Akoum Teeth identity + mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void AkoumTeeth_Identity_Land_TapsForRed_BackFace()
    {
        var teeth = AkoumTeethFactory.Create(_alice);

        teeth.Name.Should().Be("Akoum Teeth");
        teeth.HasType(CardType.Land).Should().BeTrue();
        teeth.Owner.Should().BeSameAs(_alice);
        teeth.Controller.Should().BeSameAs(_alice);

        // Pre-flipped to the back face — the land is the back face that exists.
        teeth.MdfcState.Should().NotBeNull();
        teeth.MdfcState!.IsBackFace.Should().BeTrue();
        teeth.MdfcState.ActiveFaceName.Should().Be("Akoum Teeth");

        // {T}: Add {R} — single mana ability producing one red.
        teeth.Abilities.OfType<ManaAbility>().Should().ContainSingle();
    }

    [Fact]
    public void AkoumTeeth_NamedCardFactory_Dispatch_ProducesLand()
    {
        var card = NamedCardFactory.Create("Akoum Teeth", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Akoum Teeth");
    }

    [Fact]
    public void AkoumTeeth_EntersTapped_ViaReplacementBus()
    {
        var bus = new ReplacementBus();
        var teeth = AkoumTeethFactory.Create(_alice, bus);

        // CR 614.1c — unconditional "this land enters tapped" replacement is
        // registered on the bus. Drive the ETB intent through it and confirm
        // EntersTapped is set.
        var intent = new ZoneMoveIntent(
            Card: teeth,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var replaced = bus.Apply(intent);
        replaced.Should().NotBeNull();
        replaced!.EntersTapped.Should().BeTrue(
            "Akoum Teeth always enters tapped (CR 614.1c)");
    }
}
