using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WoodlandChasmFactory"/> — the B/G snow dual
/// tapland from Kaldheim. Type line: <c>Snow Land — Swamp Forest</c>.
/// Oracle text:
///   "({T}: Add {B} or {G}.)
///    This land enters tapped."
///
/// Woodland Chasm is NOT a Basic land, so the printed Swamp/Forest subtypes
/// do NOT grant intrinsic mana (CR 305.6 only applies the intrinsic basic-land
/// mana ability to lands with the corresponding subtype that are also... no —
/// CR 305.6 grants the {T}: Add ability to ANY land with the basic land
/// subtype). The parenthesised mana line is reminder text for that intrinsic
/// ability. Here we declare the two mana abilities explicitly in the JSON so
/// the card produces {B} or {G} regardless of how the intrinsic-subtype-mana
/// wiring is materialised — matching the Guildgate factories' posture.
///
/// Covers:
/// - Identity (Land + the Snow supertype, CR 205.4d; Swamp + Forest subtypes).
/// - Two mana abilities producing {B} and {G} (CR 605.1 — mana abilities don't
///   use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate / Refuge tapland factories).
/// </summary>
public class WoodlandChasmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WoodlandChasm_Dispatch_ReturnsSnowLandWithSwampForestSubtypes()
    {
        var card = NamedCardFactory.Create("Woodland Chasm", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Woodland Chasm");
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is Snow Land");
        card.HasSubtype(CardSubtype.Swamp).Should().BeTrue();
        card.HasSubtype(CardSubtype.Forest).Should().BeTrue();
    }

    [Fact]
    public void WoodlandChasm_IsNotBasic()
    {
        var card = NamedCardFactory.Create("Woodland Chasm", _alice);

        card.HasSupertype(CardSupertype.Basic)
            .Should().BeFalse("Woodland Chasm is a nonbasic snow dual land");
    }

    [Fact]
    public void WoodlandChasm_HasTwoManaAbilities_ProducingBlackAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Woodland Chasm", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Woodland Chasm taps for {B} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void WoodlandChasm_HasNoActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Woodland Chasm", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Woodland Chasm has no cycling or other activated ability");
    }
}
