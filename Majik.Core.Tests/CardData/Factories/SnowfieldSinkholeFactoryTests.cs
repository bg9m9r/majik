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
/// Unit tests for <see cref="SnowfieldSinkholeFactory"/> — the W/B Kaldheim
/// snow dual land. Type line: <c>Snow Land — Plains Swamp</c>. Oracle text:
///   "({T}: Add {W} or {B}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + Snow supertype, CR 205.4d, + the printed Plains and
///   Swamp subtypes, CR 205.3i).
/// - Two mana abilities producing {W} and {B} respectively (CR 605.1 — mana
///   abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by this
/// factory (same posture as the Guildgate factories).
/// </summary>
[Trait("Color", "C")]
public class SnowfieldSinkholeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    [Fact]
    public void SnowfieldSinkhole_HasTwoManaAbilities_ProducingWhiteAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Snowfield Sinkhole", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Snowfield Sinkhole taps for {W} or {B}");
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void SnowfieldSinkhole_HasNoNonManaActivatedAbility()
    {
        var land = (Land)NamedCardFactory.Create("Snowfield Sinkhole", _alice);

        land.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("the snow dual lands have no activated abilities");
    }
}
