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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ReassemblingSkeletonFactory"/>.
///
/// Reassembling Skeleton (Magic 2010, {1}{B}):
///   Creature — Skeleton Warrior 1/1.
///   "{1}{B}: Return this card from your graveyard to the battlefield
///    tapped."
///
/// Covers (UNIQUE behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness):
///   - Identity (Skeleton Warrior 1/1 at {1}{B}, owner/controller).
///   - Activated ability shape: {1}{B} mana cost, no target requests
///     (CR 602).
///   - Resolution: returns the Skeleton from the graveyard to the
///     battlefield TAPPED (CR 110.1 / 400.7 / 701.21).
///   - Guard: once the Skeleton has left the graveyard, the body is a no-op
///     (CR 608.2b).
/// </summary>
[Trait("Color", "B")]
public class ReassemblingSkeletonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutInGraveyard(Player owner, Card card)
    {
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    private static ActivatedAbility GraveyardAbility(Creature skeleton) =>
        skeleton.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void ReassemblingSkeleton_Identity()
    {
        var c = ReassemblingSkeletonFactory.Create(_alice);

        c.Name.Should().Be("Reassembling Skeleton");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Skeleton).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ReassemblingSkeleton_HasGraveyardRecursionAbility()
    {
        var c = ReassemblingSkeletonFactory.Create(_alice);

        var ability = GraveyardAbility(c);
        ability.Source.Should().BeSameAs(c);
        ability.TargetRequests.Should().BeEmpty(
            "the recursion ability has no targets");
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the printed {1}{B} mana cost is a cost-layer ManaCostCost");
    }

    [Fact]
    public void ReassemblingSkeleton_Resolve_ReturnsFromGraveyardToBattlefield_Tapped()
    {
        var c = ReassemblingSkeletonFactory.Create(_alice);
        PutInGraveyard(_alice, c);

        GraveyardAbility(c).Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(c,
            "the Skeleton returns from the graveyard to the battlefield");
        c.Zone.Should().Be(ZoneType.Battlefield);
        c.Controller.Should().BeSameAs(_alice);
        c.IsTapped.Should().BeTrue(
            "CR 701.21 — Reassembling Skeleton returns to the battlefield TAPPED");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c);
    }

    [Fact]
    public void ReassemblingSkeleton_Resolve_NotInGraveyard_IsNoOp()
    {
        var c = ReassemblingSkeletonFactory.Create(_alice);
        // The Skeleton is on the battlefield, not in the graveyard.
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        GraveyardAbility(c).Resolve();

        // Still on the battlefield, untapped — the graveyard-return body was a
        // no-op because the source isn't in the graveyard (CR 608.2b).
        _alice.Zones.Battlefield.GetCards().Should().Contain(c);
        c.IsTapped.Should().BeFalse(
            "the body short-circuits when the source isn't in the graveyard, " +
            "so it does not tap a battlefield permanent");
    }
}
