using System.Linq;
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
/// Tests for <see cref="DrownyardTempleFactory"/>.
///
/// Drownyard Temple (Shadows over Innistrad):
///   Land.
///   "{T}: Add {C}.
///    {3}: Return this card from your graveyard to the battlefield tapped."
///
/// Covers:
///   - Identity (Land, owner/controller).
///   - {T}: Add {C} colourless mana ability (CR 605.1).
///   - {3} graveyard-recursion activated ability shape: a {3} ManaCostCost,
///     no targets (CR 602).
///   - Resolution: returns the Temple from the graveyard to the battlefield
///     TAPPED (CR 110.1 / 400.7 / 701.21).
///   - Guard: once the Temple has left the graveyard, the body is a no-op
///     (CR 608.2b).
///
/// Dispatch + well-formedness are covered automatically by
/// CardFactoryContractTests; this file asserts only the unique behaviour.
/// </summary>
[Trait("Color", "C")]
public class DrownyardTempleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static ActivatedAbility GraveyardAbility(Land temple) =>
        temple.Abilities.OfType<ActivatedAbility>().Single();

    private void PutInGraveyard(Card card)
    {
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DrownyardTemple_Identity()
    {
        var land = DrownyardTempleFactory.Create(_alice);

        land.Name.Should().Be("Drownyard Temple");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} mana ability (CR 605.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void DrownyardTemple_HasColorlessManaAbility()
    {
        var land = DrownyardTempleFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle("land produces exactly {C}")
            .Which.ManaGenerated.Generic.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // {3} graveyard-recursion activated ability shape (CR 602)
    // -----------------------------------------------------------------------

    [Fact]
    public void DrownyardTemple_HasGraveyardRecursionAbility()
    {
        var land = DrownyardTempleFactory.Create(_alice);

        var ability = GraveyardAbility(land);
        ability.Source.Should().BeSameAs(land);
        ability.TargetRequests.Should().BeEmpty(
            "the recursion ability has no targets");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed {3} mana cost is a cost-layer ManaCostCost");
    }

    // -----------------------------------------------------------------------
    // Resolution (CR 110.1 / 400.7 / 701.21 — return tapped)
    // -----------------------------------------------------------------------

    [Fact]
    public void DrownyardTemple_Resolve_ReturnsToBattlefieldTapped()
    {
        var land = DrownyardTempleFactory.Create(_alice);
        PutInGraveyard(land);

        GraveyardAbility(land).Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(land,
            "the Temple returns from the graveyard to the battlefield");
        land.Zone.Should().Be(ZoneType.Battlefield);
        land.Controller.Should().BeSameAs(_alice);
        land.IsTapped.Should().BeTrue(
            "the printed rider returns it TAPPED (CR 701.21)");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(land);
    }

    [Fact]
    public void DrownyardTemple_Resolve_NotInGraveyard_IsNoOp()
    {
        var land = DrownyardTempleFactory.Create(_alice);
        // On the battlefield, not in the graveyard.
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        GraveyardAbility(land).Resolve();

        // Still untapped on the battlefield — the effect did nothing
        // (CR 608.2b: the source isn't in the graveyard).
        land.IsTapped.Should().BeFalse(
            "the Temple wasn't in the graveyard, so the return does nothing");
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(land);
    }
}
