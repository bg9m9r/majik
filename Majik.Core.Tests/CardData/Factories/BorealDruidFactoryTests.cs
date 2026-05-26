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
/// Tests for <see cref="BorealDruidFactory"/> — Creature — Elf Druid {G}
/// 1/1 with a single mana ability:
///   "{T}: Add {C}."
///
/// Covers:
///   - Card identity (name, cost, types, subtypes, P/T, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single <see cref="ManaAbility"/> attached.
///   - Mana ability produces {C} (bucketed as +1 generic in
///     <see cref="ValueObjects.ManaCost.Parse"/>) and taps Boreal Druid.
///   - <c>canActivateCheck</c> gate prevents re-activation while tapped.
/// </summary>
public class BorealDruidFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BorealDruid_IsElfDruid_AtG_OneOne()
    {
        var c = BorealDruidFactory.Create(_alice);

        c.Name.Should().Be("Boreal Druid");
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
    public void NamedCardFactory_Dispatches_BorealDruid()
    {
        var card = NamedCardFactory.Create("Boreal Druid", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Boreal Druid");
        card.HasType(CardType.Creature).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)card).HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    [Fact]
    public void BorealDruid_HasSingleColorlessManaAbility()
    {
        var c = BorealDruidFactory.Create(_alice);

        var mana = c.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "Boreal Druid prints only {T}: Add {C}.");

        // {C} bucketed as +1 generic; ToString renders as "1".
        var produced = mana[0].ManaGenerated!;
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
    }

    [Fact]
    public void BorealDruid_Activate_ProducesColorlessMana_AndTapsItself()
    {
        var c = BorealDruidFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ManaAbility>().Single();
        ability.CanActivate().Should().BeTrue("Boreal Druid is untapped.");

        var produced = ability.Activate();

        // {C} is bucketed as +1 generic in ValueObjects.ManaCost today
        // (same convention as Inkmoth Nexus / Plague Myr).
        produced.Generic.Should().Be(1);
        produced.Green.Should().Be(0);
        c.IsTapped.Should().BeTrue("the {T} cost taps the creature.");
    }

    [Fact]
    public void BorealDruid_CannotActivate_WhileTapped()
    {
        var c = BorealDruidFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var ability = c.Abilities.OfType<ManaAbility>().Single();

        ability.Activate();
        c.IsTapped.Should().BeTrue();

        ability.CanActivate().Should().BeFalse(
            "canActivateCheck = !IsTapped — duplicate activations are prevented.");
    }
}
