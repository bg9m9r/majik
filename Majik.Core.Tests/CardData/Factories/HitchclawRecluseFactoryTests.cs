using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HitchclawRecluseFactory"/>.
///
/// Card: Hitchclaw Recluse — {2}{G} Creature — Spider 1/4.
///   "Reach" (CR 702.17)
/// </summary>
[Trait("Color", "G")]
public class HitchclawRecluseFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HitchclawRecluse_Identity()
    {
        var c = HitchclawRecluseFactory.Create(_alice);

        c.Name.Should().Be("Hitchclaw Recluse");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HitchclawRecluse_IsGreen()
    {
        var c = HitchclawRecluseFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Green,
            "Hitchclaw Recluse has a {G} pip in its mana cost");
    }

    [Fact]
    public void HitchclawRecluse_ManaValueIsThree()
    {
        var c = HitchclawRecluseFactory.Create(_alice);

        // {2}{G} → generic 2 + one coloured pip = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void HitchclawRecluse_HasReachKeywordMarker()
    {
        var c = HitchclawRecluseFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Reach").Should().BeTrue(
                "Hitchclaw Recluse ships with Reach as a KeywordAbility marker (CR 702.17)");
    }

    [Fact]
    public void HitchclawRecluse_NoOtherAbilities()
    {
        var c = HitchclawRecluseFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Reach is the only printed keyword");
    }
}
