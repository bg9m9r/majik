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
/// Tests for <see cref="ElvishMysticFactory"/> — Creature — Elf Druid {G}
/// 1/1 with a single mana ability:
///   "{T}: Add {G}."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ManaAbility"/> attached.
///   - Mana ability produces {G} and taps Elvish Mystic.
///   - <c>canActivateCheck</c> gate prevents re-activation while tapped.
/// </summary>
public class ElvishMysticFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ElvishMystic_IsElfDruid_AtG_OneOne()
    {
        var c = ElvishMysticFactory.Create(_alice);

        c.Name.Should().Be("Elvish Mystic");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ElvishMystic()
    {
        var card = NamedCardFactory.Create("Elvish Mystic", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Elvish Mystic");
        card.HasType(CardType.Creature).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    [Fact]
    public void ElvishMystic_HasSingleGreenManaAbility()
    {
        var c = ElvishMysticFactory.Create(_alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "Elvish Mystic prints only {T}: Add {G}.");

        // ManaCost.ToString() returns the bare letter "G" — see ManaCost.cs.
        mana[0].ManaGenerated?.ToString().Should().Be("G");
    }

    [Fact]
    public void ElvishMystic_Activate_ProducesGreenMana_AndTapsItself()
    {
        var c = ElvishMysticFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ManaAbility>().Single();
        ability.CanActivate().Should().BeTrue("Elvish Mystic is untapped.");

        var produced = ability.Activate();
        produced.ToString().Should().Be("G",
            "activating Elvish Mystic yields one green mana.");
        c.IsTapped.Should().BeTrue("the {T} cost taps the creature.");
    }

    [Fact]
    public void ElvishMystic_CannotActivate_WhileTapped()
    {
        var c = ElvishMysticFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ManaAbility>().Single();

        // First activation taps it.
        ability.Activate();
        c.IsTapped.Should().BeTrue();

        // Second activation gate must reject — IsTapped is true.
        ability.CanActivate().Should().BeFalse(
            "canActivateCheck = !IsTapped — duplicate activations are prevented.");
    }
}
