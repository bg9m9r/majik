using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="OrnithopterFactory"/>.
///
/// Card: Ornithopter — Artifact Creature — Thopter {0} 0/2 (Antiquities).
///   "Flying"
/// </summary>
[Trait("Color", "C")]
public class OrnithopterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Ornithopter_Identity()
    {
        var c = OrnithopterFactory.Create(_alice);

        c.Name.Should().Be("Ornithopter");
        c.ManaCost.Should().Be("{0}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Ornithopter_HasFlyingKeywordMarker()
    {
        var c = OrnithopterFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Ornithopter ships with Flying as a KeywordAbility marker");
    }

    [Fact]
    public void Ornithopter_NoOtherAbilities()
    {
        var c = OrnithopterFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }
}
