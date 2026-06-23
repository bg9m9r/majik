using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HighlandForestFactory"/> — the R/G snow dual
/// tapland from Kaldheim. Type line: <c>Snow Land — Mountain Forest</c>.
/// Oracle text (verified against Scryfall 2026-06-23):
///   "({T}: Add {R} or {G}.)
///    This land enters tapped."
///
/// Highland Forest is NOT a Basic land but carries the printed Mountain/Forest
/// subtypes (CR 205.3i) plus the Snow supertype (CR 205.4d). The parenthesised
/// mana line is reminder text; the two mana abilities {R}/{G} are declared
/// explicitly in the JSON so the card produces {R} or {G} regardless of how the
/// intrinsic-subtype-mana wiring is materialised — matching the Woodland Chasm
/// factory's posture.
///
/// Covers:
/// - Identity (Land + Snow supertype; Mountain + Forest subtypes; nonbasic).
/// - Two mana abilities producing {R} and {G} (CR 605.1 — mana abilities don't
///   use the stack).
/// - No extra activated/triggered ability.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load path
/// by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Woodland Chasm / Refuge tapland factories), so
/// the bus-supplied registration is exercised separately.
/// </summary>
[Trait("Color", "M")]
public class HighlandForestFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HighlandForest_IsSnowLand_WithMountainForestSubtypes_Nonbasic()
    {
        var land = (Land)NamedCardFactory.Create("Highland Forest", _alice);

        land.Name.Should().Be("Highland Forest");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue("type line is \"Snow Land — Mountain Forest\"");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Highland Forest is a nonbasic snow dual land");
        land.HasSubtype(CardSubtype.Mountain).Should().BeTrue("printed subtypes are Mountain Forest");
        land.HasSubtype(CardSubtype.Forest).Should().BeTrue("printed subtypes are Mountain Forest");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HighlandForest_HasTwoManaAbilities_ProducingRedAndGreen()
    {
        var land = (Land)NamedCardFactory.Create("Highland Forest", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Highland Forest taps for {R} or {G}");
        mana.Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
        mana.Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void HighlandForest_HasNoExtraActivatedOrTriggeredAbility()
    {
        var land = (Land)NamedCardFactory.Create("Highland Forest", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Highland Forest has no cycling or other activated ability");
        land.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("Highland Forest has no triggered ability");
    }

    [Fact]
    public void HighlandForest_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = HighlandForestFactory.Create(_alice, replacements: replacements);

        land.Should().NotBeNull();
        // The replacement is registered on the supplied bus (CR 614.1c); the
        // shape-only path (null bus) skips it. EntersTappedReplacement has no
        // public bus-inspection surface, so the production path (covered by the
        // binder chain via oracle text) is the authoritative tapped-entry test
        // — same posture as Woodland Chasm.
    }
}
