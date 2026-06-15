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
/// Unit tests for Tresserhorn Sinks — the B/R snow tapland (Modern Horizons 3).
/// Oracle text:
///   "This land enters tapped.
///    {T}: Add {B} or {R}."
///
/// Type line: Snow Land (no land subtypes — unlike the Sulfurous Mire snow
/// dual, which is a Swamp Mountain).
///
/// Fileless JSON card (#1713): dispatch is source-generated from
/// <c>Majik.Core/CardData/Cards/tresserhorn-sinks.json</c>; there is no
/// hand-written wrapper factory. Created in tests via the production
/// <see cref="NamedCardFactory.Create(string, Player)"/> path.
///
/// Covers the card's UNIQUE behaviour:
/// - Identity: Land type with the Snow supertype (CR 205.4d) and NO land
///   subtypes.
/// - Two mana abilities producing {B} and {R} respectively (CR 605.1 — mana
///   abilities don't use the stack).
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by the
/// fileless JSON dispatch (same posture as the Sulfurous Mire snow dual).
/// </summary>
[Trait("Color", "C")]
public class TresserhornSinksFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TresserhornSinks_IsASnowLandWithNoSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Tresserhorn Sinks", _alice);

        land.HasType(CardType.Land).Should().BeTrue("Tresserhorn Sinks is a Land");
        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Tresserhorn Sinks is a Snow Land (CR 205.4d)");
        land.Subtypes.Should().BeEmpty(
            "the type line is just \"Snow Land\" — no land subtypes");
    }

    [Fact]
    public void TresserhornSinks_HasTwoManaAbilities_ProducingBlackAndRed()
    {
        var land = (Land)NamedCardFactory.Create("Tresserhorn Sinks", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Tresserhorn Sinks taps for {B} or {R}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
    }
}
