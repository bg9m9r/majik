using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SkyTerrorFactory"/>.
///
/// Sky Terror (Ixalan, {R}{W}) is a 2/2 Creature — Dinosaur whose entire
/// printed body is two evergreen keywords (Scryfall verified 2026-06-23):
///   "Flying, menace"
///
/// Both keywords come straight from the JSON <c>keywords</c> array via the
/// build pipeline (CardDefRuntime emits a <c>KeywordAbility</c> marker per
/// entry), so the unique behaviour to assert is simply that combat reads both
/// keywords off the materialised creature:
///   - Flying (CR 702.9) — <see cref="CombatAbilities.HasFlying"/>.
///   - Menace (CR 702.111) — <see cref="CombatAbilities.HasMenace"/>.
///
/// Dispatch + well-formedness is covered for every implemented card by
/// CardFactoryContractTests, so this file asserts only the keyword markers plus
/// a single Identity check for the non-vanilla stats (multicoloured {R}{W},
/// 2/2, Dinosaur).
/// </summary>
[Trait("Color", "M")]
public class SkyTerrorFactoryTests
{
    private readonly Majik.Core.Players.Player _alice = new("Alice", 20);

    [Fact]
    public void SkyTerror_Identity()
    {
        var terror = SkyTerrorFactory.Create(_alice);

        terror.Name.Should().Be("Sky Terror");
        terror.ManaCost.Should().Be("{R}{W}");
        terror.HasType(CardType.Creature).Should().BeTrue();
        terror.HasSubtype(CardSubtype.Dinosaur).Should().BeTrue();
        terror.BasePower.Should().Be(2);
        terror.BaseToughness.Should().Be(2);
        terror.Owner.Should().BeSameAs(_alice);
        terror.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SkyTerror_HasFlying_ReadByCombat()
    {
        var terror = SkyTerrorFactory.Create(_alice);

        // CR 702.9 — only a creature with flying or reach can block a flier.
        CombatAbilities.HasFlying(terror).Should().BeTrue();
    }

    [Fact]
    public void SkyTerror_HasMenace_ReadByCombat()
    {
        var terror = SkyTerrorFactory.Create(_alice);

        // CR 702.111 — can't be blocked except by two or more creatures.
        CombatAbilities.HasMenace(terror).Should().BeTrue();
    }
}
