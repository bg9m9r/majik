using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BattlefieldRaptorFactory"/>.
///
/// Card: Battlefield Raptor — {W} Creature — Bird 1/2.
///   "Flying, first strike"
/// </summary>
[Trait("Color", "W")]
public class BattlefieldRaptorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BattlefieldRaptor_Identity()
    {
        var c = BattlefieldRaptorFactory.Create(_alice);

        c.Name.Should().Be("Battlefield Raptor");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BattlefieldRaptor_HasFlyingKeywordMarker()
    {
        var c = BattlefieldRaptorFactory.Create(_alice);

        // CR 702.9 — Flying. Block restrictions enforced by the combat path,
        // which reads the marker directly.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Battlefield Raptor has Flying (CR 702.9)");
    }

    [Fact]
    public void BattlefieldRaptor_HasFirstStrikeKeywordMarker()
    {
        var c = BattlefieldRaptorFactory.Create(_alice);

        // CR 702.7 — First strike. CombatAbilities.HasFirstStrike consumes
        // this marker to give the creature its combat damage in the
        // first-strike step.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "First strike").Should().BeTrue(
                "Battlefield Raptor has First strike (CR 702.7)");
    }

    [Fact]
    public void BattlefieldRaptor_HasExactlyTwoKeywords()
    {
        var c = BattlefieldRaptorFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying and First strike are the only printed keywords");
    }

    [Fact]
    public void BattlefieldRaptor_NoTriggeredOrActivatedAbilities()
    {
        var c = BattlefieldRaptorFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
