using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for Emblem (CR 114): an object with no characteristics other than
/// abilities that lives in the command zone for the rest of the game.
/// </summary>
public sealed class EmblemTests
{
    [Fact]
    public void Emblem_HoldsAbilities()
    {
        var alice = new Player("Alice", 20);
        var ability = new KeywordAbility("Lifelink", null, alice);

        var emblem = new Emblem(alice, "Test Emblem", new IAbility[] { ability });

        emblem.Abilities.Should().HaveCount(1);
        emblem.Abilities[0].Should().BeSameAs(ability);
        emblem.Controller.Should().BeSameAs(alice);
        emblem.SourceName.Should().Be("Test Emblem");
    }

    [Fact]
    public void Emblem_WithNoAbilities_HasEmptyAbilityList()
    {
        var alice = new Player("Alice", 20);

        var emblem = new Emblem(alice, "Empty Emblem", Array.Empty<IAbility>());

        emblem.Abilities.Should().BeEmpty();
    }

    [Fact]
    public void Emblem_WithNullAbilities_HasEmptyAbilityList()
    {
        var alice = new Player("Alice", 20);

        var emblem = new Emblem(alice, "Null Abilities Emblem", null!);

        emblem.Abilities.Should().BeEmpty();
    }

    [Fact]
    public void Emblem_NullController_Throws()
    {
        var act = () => new Emblem(null!, "Test", Array.Empty<IAbility>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("controller");
    }

    [Fact]
    public void Emblem_NullSourceName_TreatedAsEmpty()
    {
        var alice = new Player("Alice", 20);

        var emblem = new Emblem(alice, null!, Array.Empty<IAbility>());

        emblem.SourceName.Should().BeEmpty();
    }

    [Fact]
    public void Emblem_HasUniqueId()
    {
        var alice = new Player("Alice", 20);

        var a = new Emblem(alice, "Emblem A", Array.Empty<IAbility>());
        var b = new Emblem(alice, "Emblem B", Array.Empty<IAbility>());

        a.Id.Should().NotBe(b.Id);
    }

    [Fact]
    public void Player_AddEmblem_StoresInCollection()
    {
        var alice = new Player("Alice", 20);
        var emblem = new Emblem(alice, "Test", Array.Empty<IAbility>());

        alice.AddEmblem(emblem);

        alice.Emblems.Should().HaveCount(1);
        alice.Emblems[0].Should().BeSameAs(emblem);
    }

    [Fact]
    public void Player_AddEmblem_SupportsMultipleEmblems()
    {
        var alice = new Player("Alice", 20);
        var emblem1 = new Emblem(alice, "Emblem 1", Array.Empty<IAbility>());
        var emblem2 = new Emblem(alice, "Emblem 2", Array.Empty<IAbility>());

        alice.AddEmblem(emblem1);
        alice.AddEmblem(emblem2);

        alice.Emblems.Should().HaveCount(2);
    }

    [Fact]
    public void Player_AddEmblem_NullEmblem_Throws()
    {
        var alice = new Player("Alice", 20);

        var act = () => alice.AddEmblem(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Player_Emblems_StartsEmpty()
    {
        var alice = new Player("Alice", 20);

        alice.Emblems.Should().BeEmpty();
    }

    [Fact]
    public void Emblem_MultipleAbilities_AllStored()
    {
        var alice = new Player("Alice", 20);
        var abilities = new IAbility[]
        {
            new KeywordAbility("Flying", null, alice),
            new KeywordAbility("Trample", null, alice),
        };

        var emblem = new Emblem(alice, "Multi-ability Emblem", abilities);

        emblem.Abilities.Should().HaveCount(2);
    }
}
