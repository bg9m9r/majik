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
/// Tests for <see cref="LlanowarElvesFactory"/> — Creature — Elf Druid {G}
/// 1/1 with a single mana ability:
///   "{T}: Add {G}."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ManaAbility"/> attached.
///   - Mana ability produces {G} and taps Llanowar Elves.
///   - <c>canActivateCheck</c> gate prevents re-activation while tapped.
/// </summary>
[Trait("Color", "G")]
public class LlanowarElvesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LlanowarElves_IsElfDruid_AtG_OneOne()
    {
        var c = LlanowarElvesFactory.Create(_alice);

        c.Name.Should().Be("Llanowar Elves");
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
    public void LlanowarElves_HasSingleGreenManaAbility()
    {
        var c = LlanowarElvesFactory.Create(_alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "Llanowar Elves prints only {T}: Add {G}.");

        // ManaCost.ToString() returns the bare letter "G" — see ManaCost.cs.
        mana[0].ManaGenerated?.ToString().Should().Be("G");
    }

    [Fact]
    public void LlanowarElves_Activate_ProducesGreenMana_AndTapsItself()
    {
        var c = LlanowarElvesFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        // CR 302.6 — clear summoning sickness so this test exercises mana
        // production rather than the {T} sickness gate.
        c.ClearSummoningSickness();

        var ability = c.Abilities.OfType<ManaAbility>().Single();
        ability.CanActivate().Should().BeTrue("Llanowar Elves is untapped.");

        var produced = ability.Activate();
        produced.ToString().Should().Be("G",
            "activating Llanowar Elves yields one green mana.");
        c.IsTapped.Should().BeTrue("the {T} cost taps the creature.");
    }

    [Fact]
    public void LlanowarElves_CannotActivate_WhileTapped()
    {
        var c = LlanowarElvesFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        // CR 302.6 — clear summoning sickness so we can activate and then
        // assert the !IsTapped re-activation gate specifically.
        c.ClearSummoningSickness();

        var ability = c.Abilities.OfType<ManaAbility>().Single();

        // First activation taps it.
        ability.Activate();
        c.IsTapped.Should().BeTrue();

        // Second activation gate must reject — IsTapped is true.
        ability.CanActivate().Should().BeFalse(
            "canActivateCheck = !IsTapped — duplicate activations are prevented.");
    }
}
