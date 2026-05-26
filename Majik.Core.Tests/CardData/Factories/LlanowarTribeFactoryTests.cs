using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LlanowarTribeFactory"/> — Creature — Elf Druid
/// {G}{G}{G} 3/3 with a single batched mana ability:
///   "{T}: Add {G}{G}{G}."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ManaAbility"/> attached.
///   - Mana ability produces three green pips in one activation and taps
///     the creature.
///   - <c>canActivateCheck</c> gate prevents re-activation while tapped.
/// </summary>
public class LlanowarTribeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LlanowarTribe_IsElfDruid_AtGGG_ThreeThree()
    {
        var c = LlanowarTribeFactory.Create(_alice);

        c.Name.Should().Be("Llanowar Tribe");
        c.ManaCost.Should().Be("{G}{G}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LlanowarTribe()
    {
        var card = NamedCardFactory.Create("Llanowar Tribe", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Llanowar Tribe");
        card.HasType(CardType.Creature).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    [Fact]
    public void LlanowarTribe_HasSingleTripleGreenManaAbility()
    {
        var c = LlanowarTribeFactory.Create(_alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1,
            "Llanowar Tribe prints only {T}: Add {G}{G}{G}.");

        var produced = mana[0].ManaGenerated!;
        produced.Green.Should().Be(3);
        produced.Generic.Should().Be(0);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
    }

    [Fact]
    public void LlanowarTribe_Activate_ProducesThreeGreen_AndTapsItself()
    {
        var c = LlanowarTribeFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ManaAbility>().Single();
        ability.CanActivate().Should().BeTrue("Llanowar Tribe is untapped.");

        var produced = ability.Activate();
        produced.Green.Should().Be(3,
            "activating Llanowar Tribe yields {G}{G}{G}.");
        c.IsTapped.Should().BeTrue("the {T} cost taps the creature.");
    }

    [Fact]
    public void LlanowarTribe_CannotActivate_WhileTapped()
    {
        var c = LlanowarTribeFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ManaAbility>().Single();

        ability.Activate();
        c.IsTapped.Should().BeTrue();

        ability.CanActivate().Should().BeFalse(
            "canActivateCheck = !IsTapped — duplicate activations are prevented.");
    }
}
