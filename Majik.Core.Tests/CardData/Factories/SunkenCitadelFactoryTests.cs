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
/// Unit tests for <see cref="SunkenCitadelFactory"/> — Sunken Citadel
/// (Tarkir: Dragonstorm). Land — Cave. Oracle text:
///   "This land enters tapped. As it enters, choose a color.
///    {T}: Add one mana of the chosen color.
///    {T}: Add two mana of the chosen color. Spend this mana only to
///    activate abilities of land sources."
///
/// Modelled after <see cref="ColdsteelHeartFactory"/> (JSON identity + up-front
/// "choose a color as this enters" resolution, CR 614.12, plus an unconditional
/// ETB-tapped replacement registered when a <see cref="ReplacementBus"/> is
/// supplied, CR 614.1c), with the restricted double-mana ability following the
/// <see cref="EldraziTempleFactory"/> spend-restriction posture (CR 106.4 data,
/// payment-gate deferred).
///
/// Covers:
/// - Identity (Land — Cave, owner/controller, non-Basic).
/// - The shape-only single-arg path produces no mana abilities (chosen color
///   isn't known yet) and registers no replacement.
/// - {T}: Add one mana of the chosen color — one ManaAbility of the chosen
///   color producing a single pip (CR 605.1a).
/// - {T}: Add two mana of the chosen color — one ManaAbility of the chosen
///   color producing two pips, carrying the land-ability spend rider.
/// - Unconditional ETB-tapped (CR 614.1c): always taps on entry when a bus is
///   supplied; the shape-only path registers nothing.
/// - Args validation + dispatcher routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class SunkenCitadelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SunkenCitadel_Identity()
    {
        var land = SunkenCitadelFactory.Create(_alice);

        land.Name.Should().Be("Sunken Citadel");
        land.HasType(CardType.Land).Should().BeTrue("Sunken Citadel is a Land");
        land.HasSubtype(CardSubtype.Cave).Should().BeTrue("Sunken Citadel is a Cave");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SunkenCitadel_IsNotBasic()
    {
        var land = SunkenCitadelFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Fact]
    public void SunkenCitadel_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sunken Citadel", _alice);

        card.Should().BeOfType<Land>("Sunken Citadel is a Land");
        card.Name.Should().Be("Sunken Citadel");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SunkenCitadel_SingleArgPath_HasNoManaAbilitiesYet_AndNoOtherAbilities()
    {
        // No color chosen yet => no {T}: Add abilities; nothing else either.
        var land = SunkenCitadelFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "the chosen color isn't known on the shape-only path");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of the chosen color (CR 605.1a)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(ManaColor.White, "W")]
    [InlineData(ManaColor.Blue, "U")]
    [InlineData(ManaColor.Black, "B")]
    [InlineData(ManaColor.Red, "R")]
    [InlineData(ManaColor.Green, "G")]
    public void SunkenCitadel_HasSinglePipAbility_OfChosenColor(ManaColor chosen, string pip)
    {
        var land = SunkenCitadelFactory.Create(
            _alice, chosenColor: chosen, replacements: null);

        var expected = ManaCost.Parse(pip);

        // The single-pip ability: exactly one coloured pip of the chosen color.
        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m =>
                m.ManaGenerated.White == expected.White &&
                m.ManaGenerated.Blue == expected.Blue &&
                m.ManaGenerated.Black == expected.Black &&
                m.ManaGenerated.Red == expected.Red &&
                m.ManaGenerated.Green == expected.Green,
                "{T}: Add one mana of the chosen color");
    }

    [Fact]
    public void SunkenCitadel_HasTwoManaAbilities()
    {
        var land = SunkenCitadelFactory.Create(
            _alice, chosenColor: ManaColor.Blue, replacements: null);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one {T}: Add one + one {T}: Add two of the chosen color");
    }

    // -----------------------------------------------------------------------
    // {T}: Add two mana of the chosen color. Spend this mana only to activate
    // abilities of land sources (CR 605.1a / 106.4 — rider data; the payment
    // gate is ENFORCED by ManaPaymentResolver, see
    // SpendRestrictionProvenanceGateTests + OracleManaBinderTests prod path).
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(ManaColor.White, "WW")]
    [InlineData(ManaColor.Blue, "UU")]
    [InlineData(ManaColor.Black, "BB")]
    [InlineData(ManaColor.Red, "RR")]
    [InlineData(ManaColor.Green, "GG")]
    public void SunkenCitadel_HasDoublePipRestrictedAbility_OfChosenColor(ManaColor chosen, string pips)
    {
        var land = SunkenCitadelFactory.Create(
            _alice, chosenColor: chosen, replacements: null);

        var expected = ManaCost.Parse(pips);

        var doubleAbility = land.Abilities.OfType<ManaAbility>()
            .Single(m =>
                m.ManaGenerated.White == expected.White &&
                m.ManaGenerated.Blue == expected.Blue &&
                m.ManaGenerated.Black == expected.Black &&
                m.ManaGenerated.Red == expected.Red &&
                m.ManaGenerated.Green == expected.Green);

        doubleAbility.SpendRestriction.Should().NotBeNull(
            "the double-mana ability carries the \"only to activate abilities of " +
            "land sources\" spend rider (CR 106.4)");
        doubleAbility.SpendRestriction!.Description.Should().Be("land source ability");
    }

    [Fact]
    public void SunkenCitadel_SinglePipAbility_IsUnrestricted()
    {
        var land = SunkenCitadelFactory.Create(
            _alice, chosenColor: ManaColor.Blue, replacements: null);

        // The {T}: Add one ability (1 total pip) carries no restriction —
        // only the second mana ability is restricted (matches printed oracle).
        var singleAbility = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.TotalValue == 1);

        singleAbility.SpendRestriction.Should().BeNull(
            "only the double-mana ability carries the land-source spend rider");
    }

    [Fact]
    public void SunkenCitadel_DoubleAbility_ActivatesAsTwoOfChosenColor()
    {
        // CR 605.3 / 605.4 — mana abilities produce mana when activated. v1:
        // the spend-restriction rider is deferred, so the activated mana is
        // coloured and can pay any cost; when the gate lands, production will
        // additionally tag the entries with the land-source-only predicate.
        var land = SunkenCitadelFactory.Create(
            _alice, chosenColor: ManaColor.Blue, replacements: null);

        var doubleAbility = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.TotalValue == 2);

        var produced = doubleAbility.Activate();

        produced.Blue.Should().Be(2, "{T}: Add two mana of the chosen color (blue)");
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("activating a {T}-cost mana ability taps the source");
    }

    // -----------------------------------------------------------------------
    // Enters tapped (CR 614.1c) — unconditional
    // -----------------------------------------------------------------------

    [Fact]
    public void SunkenCitadel_AlwaysEntersTapped_WhenBusSupplied()
    {
        var bus = new ReplacementBus();
        var land = SunkenCitadelFactory.Create(
            _alice, chosenColor: ManaColor.Blue, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "\"This land enters tapped.\" is unconditional");
    }

    [Fact]
    public void SunkenCitadel_SingleArgPath_DoesNotRegisterReplacement()
    {
        // Shape-only path: a fresh bus must remain inert.
        var bus = new ReplacementBus();
        var land = SunkenCitadelFactory.Create(_alice);

        var after = ApplyEtb(bus, land, _alice);
        after.EntersTapped.Should().BeFalse(
            "no replacement registered on the shape-only path");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void SunkenCitadel_Create_ThrowsOnNullOwner()
    {
        var act = () => SunkenCitadelFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SunkenCitadel_Create_ThrowsOnColorlessChosenColor()
    {
        var act = () => SunkenCitadelFactory.Create(
            _alice, chosenColor: ManaColor.Colorless, replacements: null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ZoneMoveIntent ApplyEtb(ReplacementBus bus, Land land, Player controller)
    {
        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        return after!;
    }
}
