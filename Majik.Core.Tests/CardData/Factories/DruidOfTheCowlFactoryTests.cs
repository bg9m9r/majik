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
/// Tests for <see cref="DruidOfTheCowlFactory"/> — Creature — Elf Druid {1}{G}
/// 1/3 with a single mana ability:
///   "{T}: Add {G}."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ManaAbility"/> producing {G}.
///   - Mana ability produces {G} and taps the creature.
///   - Re-activation gate while tapped.
/// </summary>
public class DruidOfTheCowlFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void DruidOfTheCowl_IsElfDruid_At1G_OneThree()
    {
        var c = (Creature)NamedCardFactory.Create("Druid of the Cowl", _alice);

        c.Name.Should().Be("Druid of the Cowl");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DruidOfTheCowl()
    {
        var card = NamedCardFactory.Create("Druid of the Cowl", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Druid of the Cowl");
        card.HasType(CardType.Creature).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    [Fact]
    public void DruidOfTheCowl_HasSingleGreenManaAbility()
    {
        var c = (Creature)NamedCardFactory.Create("Druid of the Cowl", _alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "Druid of the Cowl prints only {T}: Add {G}.");

        // ManaCost.ToString() returns the bare letter "G" — see ManaCost.cs.
        mana[0].ManaGenerated?.ToString().Should().Be("G");
    }

    [Fact]
    public void DruidOfTheCowl_Activate_ProducesGreenMana_AndTapsItself()
    {
        var c = (Creature)NamedCardFactory.Create("Druid of the Cowl", _alice);
        c.SetZone(ZoneType.Battlefield);
        // CR 302.6 — the {T} mana ability is only legal once the creature has
        // shed summoning sickness; clear it so this test exercises the
        // mana-production behaviour rather than the sickness gate.
        c.ClearSummoningSickness();

        var ability = c.Abilities.OfType<ManaAbility>().Single();
        ability.CanActivate().Should().BeTrue("Druid of the Cowl is untapped.");

        var produced = ability.Activate();
        produced.ToString().Should().Be("G",
            "activating Druid of the Cowl yields one green mana.");
        c.IsTapped.Should().BeTrue("the {T} cost taps the creature.");
    }

    [Fact]
    public void DruidOfTheCowl_CannotActivate_WhileTapped()
    {
        var c = (Creature)NamedCardFactory.Create("Druid of the Cowl", _alice);
        c.SetZone(ZoneType.Battlefield);
        // CR 302.6 — clear summoning sickness so we can activate at all and
        // then assert the !IsTapped re-activation gate specifically.
        c.ClearSummoningSickness();

        var ability = c.Abilities.OfType<ManaAbility>().Single();

        // First activation taps it.
        ability.Activate();
        c.IsTapped.Should().BeTrue();

        // Second activation gate must reject — IsTapped is true.
        ability.CanActivate().Should().BeFalse(
            "the {T} cost can't be paid while already tapped.");
    }
}
