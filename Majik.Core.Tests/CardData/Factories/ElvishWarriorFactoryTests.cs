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
/// Unit tests for <see cref="ElvishWarriorFactory"/>.
///
/// Card: Elvish Warrior — Creature — Elf Warrior {G}{G} 2/3
/// (Onslaught). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class ElvishWarriorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ElvishWarrior_Identity()
    {
        var c = ElvishWarriorFactory.Create(_alice);

        c.Name.Should().Be("Elvish Warrior");
        c.ManaCost.Should().Be("{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ElvishWarrior_ManaValue_Is2()
    {
        var c = ElvishWarriorFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(2, "two green pips = converted mana cost 2");
    }

    [Fact]
    public void ElvishWarrior_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Elvish Warrior", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Elvish Warrior");
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    [Fact]
    public void ElvishWarrior_IsVanilla_NoAbilities()
    {
        var c = ElvishWarriorFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Elvish Warrior is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
