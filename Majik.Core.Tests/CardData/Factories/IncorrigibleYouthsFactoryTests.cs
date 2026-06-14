using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="IncorrigibleYouthsFactory"/> (Shadows over
/// Innistrad, {3}{R}{R}).
///
/// Incorrigible Youths' BODY is a vanilla 4/3 Vampire with Haste (CR 702.10);
/// its only other printed line is the madness keyword. We assert identity, that
/// Haste is honoured by the combat layer, and that the card is catalogued for
/// intrinsic madness (CR 702.35).
/// </summary>
[Trait("Color", "R")]
public class IncorrigibleYouthsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void IncorrigibleYouths_Identity()
    {
        var card = IncorrigibleYouthsFactory.Create(_alice);

        card.Name.Should().Be("Incorrigible Youths");
        card.ManaCost.Should().Be("{3}{R}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IncorrigibleYouths_HasHaste()
    {
        var card = IncorrigibleYouthsFactory.Create(_alice);

        CombatAbilities.HasHaste((Permanent)card).Should().BeTrue(
            "Incorrigible Youths has Haste (CR 702.10)");
    }

    [Fact]
    public void IncorrigibleYouths_IsCataloguedForMadness()
    {
        var card = IncorrigibleYouthsFactory.Create(_alice);

        MadnessCatalog.HasMadness(card).Should().BeTrue(
            "madness {2}{R} is intrinsic via MadnessCatalog (CR 702.35)");
        MadnessCatalog.CostFor(card).Should().Be(ManaCost.Parse("{2}{R}"));
    }
}
