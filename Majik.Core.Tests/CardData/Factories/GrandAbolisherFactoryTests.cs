using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Grand Abolisher (Magic 2012, {W}{W}).
///
/// Covers:
///   - Card shape: name, type, Human + Cleric subtypes, P/T 2/2, mana cost,
///     owner / controller wiring.
///   - NamedCardFactory dispatch.
///
/// The printed static ("During your turn, your opponents can't cast spells
/// or activate abilities of artifacts, creatures, or enchantments.") is a
/// documented v1 gap — see the factory's class xmldoc. No "total cast
/// block, gated on active-player == controller" primitive exists in
/// <see cref="Majik.Core.Rules.CastingRestrictions"/> yet, and the
/// activated-ability suppression rail
/// (<see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/>) has the
/// predicate shape but no turn-gating helper. Tests are shape-only until
/// both gaps close.
/// </summary>
[Trait("Color", "W")]
public class GrandAbolisherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void GrandAbolisher_IsCreature_HumanCleric_2_2_AtCostWW()
    {
        var c = GrandAbolisherFactory.Create(_alice);

        c.Name.Should().Be("Grand Abolisher");
        c.ManaCost.Should().Be("{W}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
}
