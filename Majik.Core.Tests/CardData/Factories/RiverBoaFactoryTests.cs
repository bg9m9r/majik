using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RiverBoaFactory"/> (Visions, {1}{G}).
///
/// Creature — Snake 2/1. Oracle text:
///   "Islandwalk (This creature can't be blocked as long as defending player
///    controls an Island.)
///    {G}: Regenerate this creature."
///
/// Covers:
///   - Identity (name, cost, P/T, Snake subtype, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Islandwalk keyword marker (CR 702.14).
///   - {G}: Regenerate activated ability shape + resolve adds a
///     <see cref="Permanent.AddRegenerationShield"/> shield (CR 701.18).
/// </summary>
[Trait("Color", "G")]
public class RiverBoaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RiverBoa_Identity()
    {
        var c = RiverBoaFactory.Create(_alice);

        c.Name.Should().Be("River Boa");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue("River Boa is a Snake");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RiverBoa_DispatchesThroughNamedCardFactory()
    {
        var c = NamedCardFactory.Create("River Boa", _alice);

        c.Should().NotBeNull();
        c!.Name.Should().Be("River Boa");
        c.Should().BeOfType<Creature>();
    }

    [Fact]
    public void RiverBoa_HasIslandwalkKeywordMarker()
    {
        var c = RiverBoaFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(k =>
                string.Equals(k, "Islandwalk", System.StringComparison.OrdinalIgnoreCase),
                "CR 702.14 — Islandwalk keyword marker is wired");
    }

    [Fact]
    public void RiverBoa_HasExactlyOneRegenerateActivatedAbility()
    {
        var c = RiverBoaFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "River Boa prints one activated ability: {G}: regenerate self");
    }

    [Fact]
    public void RiverBoa_RegenerateActivatedAbility_CostIsSingleGreen()
    {
        var c = RiverBoaFactory.Create(_alice);
        var regen = c.Abilities.OfType<ActivatedAbility>().Single();

        regen.Costs.Should().HaveCount(1);
        var manaCost = regen.Costs[0].Should().BeOfType<ManaCostCost>().Subject;
        manaCost.Cost.Green.Should().Be(1);
        manaCost.Cost.Generic.Should().Be(0);
    }

    [Fact]
    public void RiverBoa_RegenerateAbility_Resolve_AddsRegenerationShield()
    {
        var c = RiverBoaFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        c.HasRegenerationShield.Should().BeFalse();
        c.RegenerationShieldCount.Should().Be(0);

        var regen = c.Abilities.OfType<ActivatedAbility>().Single();
        regen.Resolve();

        c.HasRegenerationShield.Should().BeTrue();
        c.RegenerationShieldCount.Should().Be(1);
    }
}
