using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AerialResponderFactory"/>.
///
/// Card: Aerial Responder — {1}{W}{W} Creature — Dwarf Soldier 2/3.
///   "Flying, vigilance, lifelink"
/// </summary>
[Trait("Color", "W")]
public class AerialResponderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AerialResponder_Identity()
    {
        var c = AerialResponderFactory.Create(_alice);

        c.Name.Should().Be("Aerial Responder");
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dwarf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AerialResponder_HasFlyingKeywordMarker()
    {
        var c = AerialResponderFactory.Create(_alice);

        // CR 702.9 — Flying. CombatAbilities.HasFlying consumes this marker to
        // enforce block restrictions (only flying/reach may block it).
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Aerial Responder has Flying (CR 702.9)");
    }

    [Fact]
    public void AerialResponder_HasVigilanceKeywordMarker()
    {
        var c = AerialResponderFactory.Create(_alice);

        // CR 702.20 — Vigilance. CombatAbilities.HasVigilance consumes this
        // marker so the creature does not tap when attacking (CR 508.1f).
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Vigilance").Should().BeTrue(
                "Aerial Responder has Vigilance (CR 702.20)");
    }

    [Fact]
    public void AerialResponder_HasLifelinkKeywordMarker()
    {
        var c = AerialResponderFactory.Create(_alice);

        // CR 702.15 — Lifelink. CombatAbilities.HasLifelink consumes this
        // marker to gain the controller life equal to damage dealt.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Aerial Responder has Lifelink (CR 702.15)");
    }

    [Fact]
    public void AerialResponder_HasExactlyThreeKeywords()
    {
        var c = AerialResponderFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(3,
            "Flying, Vigilance and Lifelink are the only printed keywords");
    }

    [Fact]
    public void AerialResponder_NoTriggeredOrActivatedAbilities()
    {
        var c = AerialResponderFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
