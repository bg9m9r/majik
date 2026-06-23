using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BlightMambaFactory"/> (New Phyrexia, {1}{G}).
///
/// Creature — Phyrexian Snake 1/1. Oracle text:
///   "Infect (This creature deals damage to creatures in the form of -1/-1
///    counters and to players in the form of poison counters.)
///    {1}{G}: Regenerate this creature."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (name, cost, P/T, Phyrexian + Snake subtypes, owner/controller).
///   - Infect keyword marker (CR 702.90).
///   - {1}{G}: Regenerate activated-ability shape + resolve adds a
///     <see cref="Permanent.AddRegenerationShield"/> shield (CR 701.18).
/// (NamedCardFactory dispatch + well-formedness are asserted for every
/// implemented card by CardFactoryContractTests, so no dispatch test here.)
/// </summary>
[Trait("Color", "G")]
public class BlightMambaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BlightMamba_Identity()
    {
        var c = BlightMambaFactory.Create(_alice);

        c.Name.Should().Be("Blight Mamba");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue("Blight Mamba is Phyrexian");
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue("Blight Mamba is a Snake");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BlightMamba_HasInfectKeywordMarker()
    {
        var c = BlightMambaFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Infect").Should().BeTrue(
                "CR 702.90 — Infect marker is attached so the damage pipeline " +
                "routes -1/-1 counters / poison counters once that primitive lands.");
    }

    [Fact]
    public void BlightMamba_HasExactlyOneRegenerateActivatedAbility()
    {
        var c = BlightMambaFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Blight Mamba prints one activated ability: {1}{G}: regenerate self");
    }

    [Fact]
    public void BlightMamba_RegenerateActivatedAbility_CostIsOneGeneric_OneGreen()
    {
        var c = BlightMambaFactory.Create(_alice);
        var regen = c.Abilities.OfType<ActivatedAbility>().Single();

        regen.Costs.Should().HaveCount(1);
        var manaCost = regen.Costs[0].Should().BeOfType<ManaCostCost>().Subject;
        manaCost.Cost.Green.Should().Be(1);
        manaCost.Cost.Generic.Should().Be(1);
    }

    [Fact]
    public void BlightMamba_RegenerateAbility_Resolve_AddsRegenerationShield()
    {
        var c = BlightMambaFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        c.HasRegenerationShield.Should().BeFalse();
        c.RegenerationShieldCount.Should().Be(0);

        var regen = c.Abilities.OfType<ActivatedAbility>().Single();
        regen.Resolve();

        c.HasRegenerationShield.Should().BeTrue();
        c.RegenerationShieldCount.Should().Be(1);
    }
}
