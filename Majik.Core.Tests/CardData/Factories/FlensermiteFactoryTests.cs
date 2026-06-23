using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FlensermiteFactory"/>.
///
/// Card: Flensermite — {1}{B} Creature — Phyrexian Gremlin 1/1.
///   "Infect
///    Lifelink"
/// </summary>
[Trait("Color", "B")]
public class FlensermiteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Flensermite_Identity()
    {
        var c = FlensermiteFactory.Create(_alice);

        c.Name.Should().Be("Flensermite");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Gremlin).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Flensermite_HasInfectKeywordMarker()
    {
        var c = FlensermiteFactory.Create(_alice);

        // CR 702.90 — Infect. The damage pipeline consults this marker to
        // route -1/-1 counters (creatures) / poison counters (players).
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Infect").Should().BeTrue(
                "Flensermite has Infect (CR 702.90)");
    }

    [Fact]
    public void Flensermite_HasLifelinkKeywordMarker()
    {
        var c = FlensermiteFactory.Create(_alice);

        // CR 702.15 — Lifelink. CombatAbilities.HasLifelink consumes this
        // marker to gain the controller life equal to damage dealt.
        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Lifelink").Should().BeTrue(
                "Flensermite has Lifelink (CR 702.15)");
    }

    [Fact]
    public void Flensermite_HasExactlyTwoKeywords()
    {
        var c = FlensermiteFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Infect and Lifelink are the only printed keywords");
    }

    [Fact]
    public void Flensermite_NoTriggeredOrActivatedAbilities()
    {
        var c = FlensermiteFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
