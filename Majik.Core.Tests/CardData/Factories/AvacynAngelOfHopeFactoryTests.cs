using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AvacynAngelOfHopeFactory"/> (Avacyn
/// Restored).
///
/// Covers:
/// - Identity ({5}{W}{W}{W} Legendary Creature — Angel 8/8).
/// - Personal Flying + Vigilance + Indestructible keyword markers
///   (CR 702.9 / 702.20 / 702.12).
/// - <see cref="CombatAbilities.HasIndestructible"/> sees Avacyn's own
///   Indestructible (the SBA-relevant lookup).
/// - <see cref="NamedCardFactory"/> dispatch.
///
/// The printed "Other permanents you control have indestructible." rider
/// is intentionally NOT covered — see factory class summary for the
/// deferred anthem-primitive note.
/// </summary>
[Trait("Color", "W")]
public class AvacynAngelOfHopeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Avacyn_Identity_LegendaryAngel88()
    {
        var c = AvacynAngelOfHopeFactory.Create(_alice);

        c.Name.Should().Be("Avacyn, Angel of Hope");
        c.ManaCost.Should().Be("{5}{W}{W}{W}");
        c.Power.Should().Be(8);
        c.Toughness.Should().Be(8);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Avacyn_HasFlyingVigilanceIndestructibleKeywords()
    {
        var c = AvacynAngelOfHopeFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Should().Contain(k => k.Keyword == "Flying");
        keywords.Should().Contain(k => k.Keyword == "Vigilance");
        keywords.Should().Contain(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void Avacyn_CombatAbilities_HasIndestructible()
    {
        var c = AvacynAngelOfHopeFactory.Create(_alice);

        CombatAbilities.HasIndestructible(c).Should().BeTrue(
            "Avacyn's own indestructible is the SBA-relevant lookup");
        CombatAbilities.HasFlying(c).Should().BeTrue();
        CombatAbilities.HasVigilance(c).Should().BeTrue();
    }
    [Fact]
    public void Avacyn_NullOwner_Throws()
    {
        var act = () => AvacynAngelOfHopeFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
