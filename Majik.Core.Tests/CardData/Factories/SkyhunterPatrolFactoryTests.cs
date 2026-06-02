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
/// Unit tests for <see cref="SkyhunterPatrolFactory"/> (Mirrodin, {2}{W}{W}).
///
/// Covers:
/// - Identity ({2}{W}{W} Creature — Cat Knight 2/3, mana value 4, white).
/// - Keyword markers: Flying (CR 702.9) and First Strike (CR 702.7).
/// - <see cref="CombatAbilities"/> lookups for both combat keywords.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "W")]
public class SkyhunterPatrolFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SkyhunterPatrol_Identity_CatKnight23()
    {
        var c = SkyhunterPatrolFactory.Create(_alice);

        c.Name.Should().Be("Skyhunter Patrol");
        c.ManaCost.Should().Be("{2}{W}{W}");
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        c.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SkyhunterPatrol_HasFlyingAndFirstStrike()
    {
        var c = SkyhunterPatrolFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("First Strike");
    }

    [Fact]
    public void SkyhunterPatrol_CombatAbilities_FlyingAndFirstStrikeTrue()
    {
        var c = SkyhunterPatrolFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue();
        CombatAbilities.HasFirstStrike(c).Should().BeTrue();
    }
    [Fact]
    public void SkyhunterPatrol_NullOwner_Throws()
    {
        var act = () => SkyhunterPatrolFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
