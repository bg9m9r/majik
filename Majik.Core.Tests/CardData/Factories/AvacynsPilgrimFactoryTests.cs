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
/// Unit tests for <see cref="AvacynsPilgrimFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Human + Monk subtypes, 1/1,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - {T}: Add {W} mana ability is present and produces one white mana.
/// - Activating the mana ability taps Avacyn's Pilgrim.
/// </summary>
public class AvacynsPilgrimFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AvacynsPilgrim_Identity()
    {
        var c = AvacynsPilgrimFactory.Create(_alice);

        c.Name.Should().Be("Avacyn's Pilgrim");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AvacynsPilgrim_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Avacyn's Pilgrim", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Avacyn's Pilgrim");
        ((Creature)c).HasSubtype(CardSubtype.Human).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Monk).Should().BeTrue();
    }

    [Fact]
    public void AvacynsPilgrim_HasWhiteManaAbility()
    {
        var c = AvacynsPilgrimFactory.Create(_alice);

        var manaAbilities = c.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "Avacyn's Pilgrim has one mana ability: {T}: Add {W}.");
    }

    [Fact]
    public void AvacynsPilgrim_ManaAbility_ProducesWhiteMana_AndTaps()
    {
        var c = AvacynsPilgrimFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue("creature is untapped.");

        var mana = manaAbility.Activate();
        // ManaCost.ToString() emits bare letters: "W" for one white pip.
        mana.ToString().Should().Be("W",
            "activating {T}: Add {W} produces one white mana (ManaCost.ToString omits braces).");
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps Avacyn's Pilgrim.");
    }

    [Fact]
    public void AvacynsPilgrim_CannotActivateWhileTapped()
    {
        var c = AvacynsPilgrimFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        c.Tap();

        var manaAbility = c.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid while already tapped (CR 602.5a).");
    }
}
