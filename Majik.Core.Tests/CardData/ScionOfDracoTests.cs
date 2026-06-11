using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ScionOfDracoFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Dragon subtype, both Artifact + Creature
///   card types, P/T, owner/controller).
/// - NamedCardFactory dispatch returns a Creature shell with Artifact
///   card type stamped on (mirrors Wurmcoil Engine).
/// - Domain cost reduction (CR 702.16 / CR 117.7):
///     * No basics → full {12}.
///     * 3 distinct basic types → {6} (12 - 3×2).
///     * All 5 distinct basic types → {2} (12 - 5×2).
///     * Wastes doesn't contribute (CR 305.6 — basic land without a basic
///       land type).
/// </summary>
public class ScionOfDracoTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void AddBasic(Player owner, CardSubtype subtype, string name)
    {
        // CountDomain reads subtypes from cards on the controller's
        // battlefield (printed-subtypes mode, since no ContinuousEffectsService
        // is supplied at cost-calc time). Construct a basic land directly so
        // the subtype is present from the start.
        var land = new Land(name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype })
        {
            Owner = owner,
            Controller = owner,
            Zone = ZoneType.Battlefield,
        };
        owner.Zones.Battlefield.AddCard(land);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void ScionOfDraco_Identity()
    {
        var c = ScionOfDracoFactory.Create(_alice);

        c.Name.Should().Be("Scion of Draco");
        c.HasType(CardType.Creature).Should().BeTrue("Scion of Draco is a Creature");
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Scion of Draco is an Artifact Creature (CR 301.1 / 302.1)");
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue("Dragon is its printed subtype");
        c.Power.Should().Be(4);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ScionOfDraco_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Scion of Draco", _alice);

        c.Should().BeOfType<Creature>(
            "Scion of Draco is a Creature shell with Artifact stamped on top");
        c.Name.Should().Be("Scion of Draco");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Domain cost reduction (CR 702.16 / CR 117.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void ScionOfDraco_NoBasicTypes_CostsFullTwelve()
    {
        var c = ScionOfDracoFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(12,
            "with zero basic land types in play, Domain reduces by 0 — full {12}");
    }

    [Fact]
    public void ScionOfDraco_ThreeBasicTypes_CostsSix()
    {
        var c = ScionOfDracoFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(6, "12 - 3 × 2 = 6 (CR 702.16 — Domain)");
    }

    [Fact]
    public void ScionOfDraco_AllFiveBasicTypes_CostsTwo()
    {
        var c = ScionOfDracoFactory.Create(_alice);

        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Swamp, "Swamp");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");
        AddBasic(_alice, CardSubtype.Forest, "Forest");

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(2, "12 - 5 × 2 = 2 — Domain at full count");
    }

    [Fact]
    public void ScionOfDraco_DuplicatesAndWastes_DoNotInflateDomain()
    {
        var c = ScionOfDracoFactory.Create(_alice);

        // Two extra Mountains beyond the first — still only one distinct
        // basic type contributed (CR 702.16: "number of basic land TYPES",
        // not lands).
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");

        // Forest — second distinct type.
        AddBasic(_alice, CardSubtype.Forest, "Forest");

        // Wastes is a basic LAND but has no basic LAND TYPE (CR 305.6) so
        // it must NOT contribute to Domain.
        AddBasic(_alice, CardSubtype.Wastes, "Wastes");

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(8,
            "only 2 distinct basic types (Mountain + Forest) contribute; " +
            "duplicates collapse and Wastes is excluded → 12 - 2 × 2 = 8");
    }

    [Fact]
    public void ScionOfDraco_DomainCapsAtFiveDistinctTypes()
    {
        // The five basic types max out Domain at 5 × {2} = 10; extra
        // duplicate basics on top must NOT push the reduction past the cap.
        var c = ScionOfDracoFactory.Create(_alice);

        // All five basics, plus duplicate Plains + Island — distinct-type
        // count maxes at 5 regardless of how many lands sit on top.
        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Plains, "Plains");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Island, "Island");
        AddBasic(_alice, CardSubtype.Swamp, "Swamp");
        AddBasic(_alice, CardSubtype.Mountain, "Mountain");
        AddBasic(_alice, CardSubtype.Forest, "Forest");

        var effective = CostReduction.GetEffectiveCost(c, _alice);

        effective.Generic.Should().Be(2,
            "Domain caps at 5 distinct basic types → 12 - 10 = 2; duplicates don't over-reduce");
    }
}
