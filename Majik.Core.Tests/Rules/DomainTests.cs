using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using DomainRule = Majik.Core.Rules.Domain;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// CR 702.16 — <b>Domain</b>. Tests for the unified
/// <see cref="Majik.Core.Rules.Domain"/> primitive + the declarative
/// <see cref="DomainCostReductionAbility"/> wrapper that pure
/// Domain-scaling cards (Tribal Flames, Leyline Binding, Scion of Draco
/// and the WAR / Coalition cycle) compose on top of.
///
/// Covers:
/// - 5 distinct basic types controlled → <see cref="DomainRule.CountTypes(Player)"/> = 5.
/// - Shockland-style nonbasic with two basic subtypes (Hallowed Fountain
///   → Plains + Island) contributes both.
/// - Fetchland (Bloodstained Mire — no basic land subtype) contributes 0.
/// - Duplicates collapse (CR 702.16 — "number of basic land TYPES").
/// - Wastes is a basic LAND without a basic LAND TYPE (CR 305.6) → 0.
/// - <see cref="DomainCostReductionAbility"/> reduces generic cost by
///   domain count × multiplier.
/// </summary>
public class DomainTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutLand(
        Player owner,
        string name,
        IEnumerable<CardSubtype> subtypes,
        IEnumerable<CardSupertype>? supertypes = null)
    {
        var land = new Land(name, supertypes: supertypes, subtypes: subtypes)
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(land);
    }

    // -------------------------------------------------------------------
    // Domain.CountTypes (CR 702.16)
    // -------------------------------------------------------------------

    [Fact]
    public void CountTypes_NoLands_ReturnsZero()
    {
        DomainRule.CountTypes(_alice).Should().Be(0);
    }

    [Fact]
    public void CountTypes_FiveDistinctBasics_ReturnsFive()
    {
        PutLand(_alice, "Plains",   new[] { CardSubtype.Plains   }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Island",   new[] { CardSubtype.Island   }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Swamp",    new[] { CardSubtype.Swamp    }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Mountain", new[] { CardSubtype.Mountain }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Forest",   new[] { CardSubtype.Forest   }, new[] { CardSupertype.Basic });

        DomainRule.CountTypes(_alice).Should().Be(5,
            "all five basic land types are present (CR 702.16)");
    }

    [Fact]
    public void CountTypes_ShocklandContributesBothBasicTypes()
    {
        // Hallowed Fountain — Land — Plains Island. Non-basic, but its
        // two printed basic land subtypes BOTH contribute to Domain
        // (CR 305.6 / 702.16: count is over basic land TYPES, not lands).
        PutLand(
            _alice,
            "Hallowed Fountain",
            subtypes: new[] { CardSubtype.Plains, CardSubtype.Island });

        DomainRule.CountTypes(_alice).Should().Be(2,
            "Hallowed Fountain prints Plains + Island — both basic land " +
            "types contribute even though the land itself is nonbasic");
    }

    [Fact]
    public void CountTypes_FetchlandContributesZero()
    {
        // Bloodstained Mire — Land, no basic land subtype. Contributes
        // nothing to Domain regardless of mana ability.
        PutLand(
            _alice,
            "Bloodstained Mire",
            subtypes: Array.Empty<CardSubtype>());

        DomainRule.CountTypes(_alice).Should().Be(0,
            "Bloodstained Mire has no basic land subtype — Domain ignores it");
    }

    [Fact]
    public void CountTypes_Duplicates_CollapseToDistinctCount()
    {
        PutLand(_alice, "Mountain", new[] { CardSubtype.Mountain }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Mountain", new[] { CardSubtype.Mountain }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Mountain", new[] { CardSubtype.Mountain }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Forest",   new[] { CardSubtype.Forest   }, new[] { CardSupertype.Basic });

        DomainRule.CountTypes(_alice).Should().Be(2,
            "duplicates of the same basic type collapse — only distinct types count");
    }

    [Fact]
    public void CountTypes_Wastes_DoesNotContribute()
    {
        // CR 305.6 — Wastes is a basic LAND but has no basic LAND TYPE.
        PutLand(_alice, "Wastes", new[] { CardSubtype.Wastes }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Forest", new[] { CardSubtype.Forest }, new[] { CardSupertype.Basic });

        DomainRule.CountTypes(_alice).Should().Be(1,
            "Wastes is excluded — only Forest's basic land type counts");
    }

    [Fact]
    public void BasicLandTypes_IsExactlyTheFiveCoreTypes()
    {
        DomainRule.BasicLandTypes.Should().BeEquivalentTo(new[]
        {
            CardSubtype.Plains,
            CardSubtype.Island,
            CardSubtype.Swamp,
            CardSubtype.Mountain,
            CardSubtype.Forest,
        });
        DomainRule.BasicLandTypes.Should().NotContain(CardSubtype.Wastes,
            "Wastes is a basic land without a basic land type (CR 305.6)");
    }

    // -------------------------------------------------------------------
    // DomainCostReductionAbility (CR 702.16 / CR 117.7)
    // -------------------------------------------------------------------

    [Fact]
    public void DomainCostReductionAbility_ReducesByDomainCountTimesMultiplier()
    {
        // Synthetic card with {6} printed; DomainCostReductionAbility
        // multiplier 2 → with 3 basic types, reduction = 6.
        var card = new Sorcery("Synthetic Domain Spell", "{6}");
        card.SetOwner(_alice);
        card.SetController(_alice);
        card.AddAbility(new DomainCostReductionAbility(multiplier: 2));

        PutLand(_alice, "Plains",   new[] { CardSubtype.Plains   }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Island",   new[] { CardSubtype.Island   }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Mountain", new[] { CardSubtype.Mountain }, new[] { CardSupertype.Basic });

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(0,
            "6 - 3 × 2 = 0; Domain reduction consumes the printed generic");
    }

    [Fact]
    public void DomainCostReductionAbility_FloorsAtZero()
    {
        // {4} printed but reduction = 10 — clamp at the printed generic.
        // Floor-at-zero is enforced by CostReduction.GetEffectiveCost.
        var card = new Sorcery("Synthetic Domain Spell", "{4}");
        card.SetOwner(_alice);
        card.SetController(_alice);
        card.AddAbility(new DomainCostReductionAbility(multiplier: 2));

        PutLand(_alice, "Plains",   new[] { CardSubtype.Plains   }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Island",   new[] { CardSubtype.Island   }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Swamp",    new[] { CardSubtype.Swamp    }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Mountain", new[] { CardSubtype.Mountain }, new[] { CardSupertype.Basic });
        PutLand(_alice, "Forest",   new[] { CardSubtype.Forest   }, new[] { CardSupertype.Basic });

        var effective = CostReduction.GetEffectiveCost(card, _alice);

        effective.Generic.Should().Be(0,
            "Domain reduction of 10 clamps to the printed generic floor (CR 117.7)");
    }

    [Fact]
    public void DomainCostReductionAbility_RejectsNonPositiveMultiplier()
    {
        Action act = () => new DomainCostReductionAbility(multiplier: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DomainCostReductionAbility_IsACostReductionAbility()
    {
        // Must derive from CostReductionAbility so CostReduction.GetEffectiveCost's
        // OfType<CostReductionAbility>() pick-up keeps working.
        var ability = new DomainCostReductionAbility(multiplier: 1);
        ability.Should().BeAssignableTo<CostReductionAbility>();
        ability.TotalReducer.Should().NotBeNull("Domain is a whole-reducer shape");
    }
}
