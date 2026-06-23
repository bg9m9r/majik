using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="StormfrontPegasusFactory"/>.
///
/// Stormfront Pegasus (Magic Origins, {1}{W}):
///   Creature — Pegasus 2/1.
///   "Flying"
///
/// Covers:
///   - Identity (Pegasus 2/1, {1}{W}, owner/controller).
///   - The card's only printed ability: the Flying keyword (CR 702.9).
///
/// (NamedCardFactory dispatch + well-formedness are asserted for every
/// implemented card by CardFactoryContractTests, so no dispatch test here.)
/// </summary>
[Trait("Color", "W")]
public class StormfrontPegasusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void StormfrontPegasus_Identity()
    {
        var s = StormfrontPegasusFactory.Create(_alice);

        s.Name.Should().Be("Stormfront Pegasus");
        s.ManaCost.Should().Be("{1}{W}");
        s.HasType(CardType.Creature).Should().BeTrue();
        s.HasSubtype(CardSubtype.Pegasus).Should().BeTrue();
        s.BasePower.Should().Be(2);
        s.BaseToughness.Should().Be(1);
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StormfrontPegasus_HasFlyingKeyword()
    {
        var s = StormfrontPegasusFactory.Create(_alice);

        s.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "Stormfront Pegasus has Flying (CR 702.9)");
    }
}
