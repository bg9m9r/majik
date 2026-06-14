using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WeirdedVampireFactory"/> (Shadows over
/// Innistrad, {3}{B}).
///
/// Weirded Vampire's only printed text is the madness keyword, so its BODY is
/// a vanilla 3/3 Vampire Horror. We assert the identity (the whole observable
/// body) and that the card is catalogued for intrinsic madness (CR 702.35,
/// which is exercised by <see cref="MadnessDiscardFunnelTests"/> — not
/// re-tested per card).
/// </summary>
[Trait("Color", "B")]
public class WeirdedVampireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WeirdedVampire_Identity()
    {
        var card = WeirdedVampireFactory.Create(_alice);

        card.Name.Should().Be("Weirded Vampire");
        card.ManaCost.Should().Be("{3}{B}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        card.BasePower.Should().Be(3);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WeirdedVampire_IsCataloguedForMadness()
    {
        var card = WeirdedVampireFactory.Create(_alice);

        MadnessCatalog.HasMadness(card).Should().BeTrue(
            "madness {2}{B} is intrinsic via MadnessCatalog (CR 702.35)");
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{2}{B}"));
    }
}
