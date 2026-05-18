using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Cards;

public class CardAbilitiesTests
{
    [Fact]
    public void NewCard_HasEmptyAbilities()
    {
        var card = new Creature("Bear", "1G", 2, 2);

        card.Abilities.Should().BeEmpty();
    }

    [Fact]
    public void AddAbility_AppendsToAbilities()
    {
        var card = new Creature("Bear", "1G", 2, 2);
        var ability = Mock.Of<IAbility>();

        card.AddAbility(ability);

        card.Abilities.Should().ContainSingle().Which.Should().BeSameAs(ability);
    }

    [Fact]
    public void AddAbility_PreservesInsertionOrder()
    {
        var card = new Creature("Bear", "1G", 2, 2);
        var a = Mock.Of<IAbility>();
        var b = Mock.Of<IAbility>();
        var c = Mock.Of<IAbility>();

        card.AddAbility(a);
        card.AddAbility(b);
        card.AddAbility(c);

        card.Abilities.Should().Equal(a, b, c);
    }

    [Fact]
    public void AddAbility_Null_Throws()
    {
        var card = new Creature("Bear", "1G", 2, 2);

        var act = () => card.AddAbility(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
