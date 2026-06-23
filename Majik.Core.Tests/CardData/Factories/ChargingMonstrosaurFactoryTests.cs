using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ChargingMonstrosaurFactory"/> (Ixalan, {4}{R}).
///
/// Charging Monstrosaur's BODY is a vanilla 5/5 Dinosaur with Trample
/// (CR 702.19) and Haste (CR 702.10) — the entire oracle text. We assert
/// identity and that both keywords are honoured by the combat layer.
/// </summary>
[Trait("Color", "R")]
public class ChargingMonstrosaurFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ChargingMonstrosaur_Identity()
    {
        var card = ChargingMonstrosaurFactory.Create(_alice);

        card.Name.Should().Be("Charging Monstrosaur");
        card.ManaCost.Should().Be("{4}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(5);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ChargingMonstrosaur_HasTrampleAndHaste()
    {
        var card = ChargingMonstrosaurFactory.Create(_alice);

        CombatAbilities.HasTrample((Permanent)card).Should().BeTrue(
            "Charging Monstrosaur has Trample (CR 702.19)");
        CombatAbilities.HasHaste((Permanent)card).Should().BeTrue(
            "Charging Monstrosaur has Haste (CR 702.10)");
    }
}
