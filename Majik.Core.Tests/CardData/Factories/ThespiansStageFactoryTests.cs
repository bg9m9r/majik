using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ThespiansStageFactory"/>.
///
/// Oracle (Scryfall-confirmed 2026-06-02):
///   "{T}: Add {C}.
///    {2}, {T}: This land becomes a copy of target land, except it has this
///    ability."
///
/// Covers:
/// - Identity: Land, name, non-basic, non-legendary.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} vanilla mana ability (from JSON).
/// - Copy ability shape: cost {2} + tap, 1..1 target-land target request.
/// - Copy resolution: becomes a PERMANENT copy (no end-of-turn expiry) of the
///   chosen target land via <see cref="CopyCharacteristicsEffect"/>.
/// - "except it has this ability" — the land retains its own ability instances
///   (the mana ability + the copy ability are runtime instances on the Land
///   and are NOT stripped by the characteristics copy).
/// </summary>
[Trait("Color", "C")]
public class ThespiansStageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // Place Thespian's Stage (with effects service) on Alice's battlefield.
    private (Land land, ContinuousEffectsService effects) PlaceWithEffects()
    {
        var effects = new ContinuousEffectsService();
        var land = ThespiansStageFactory.Create(_alice, effects);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return (land, effects);
    }

    private static Land AddLandToBattlefield(Player controller, string name,
        IEnumerable<CardSubtype>? subtypes = null)
    {
        var land = new Land(name, subtypes: subtypes)
            { Owner = controller, Controller = controller };
        land.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(land);
        return land;
    }

    private static ActivatedAbility CopyAbility(Land land) =>
        land.Abilities.OfType<ActivatedAbility>().Single();

    private static ManaAbility ColorlessManaAbility(Land land) =>
        land.Abilities.OfType<ManaAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedThespiansStage()
    {
        var land = ThespiansStageFactory.Create(_alice);
        land.Name.Should().Be("Thespian's Stage");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary()
    {
        var land = ThespiansStageFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Thespian's Stage is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("not legendary");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasOneManaAbility_AndOneCopyActivatedAbility()
    {
        var land = ThespiansStageFactory.Create(_alice);
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1, "{T}: Add {C}");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2}, {T} copy ability is a stack-using activated ability, not a mana ability");
    }

    [Fact]
    public void ColorlessManaAbility_Activate_ProducesOneColorless_AndTaps()
    {
        var (land, _) = PlaceWithEffects();
        var mana = (IManaAbility)ColorlessManaAbility(land);

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        // {C} buckets into the Generic slot in ManaCost.Parse today (same as
        // Karn's Bastion / Strip Mine — no dedicated Colorless slot yet).
        produced.Generic.Should().Be(1, "{T}: Add {C} produces one colorless");
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void CopyAbility_HasManaCostAndTargetLandRequest()
    {
        var land = ThespiansStageFactory.Create(_alice);
        var ability = CopyAbility(land);

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Description.Should().Contain("2");
        ability.TargetRequests.Should().ContainSingle();
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("land");
    }

    // -----------------------------------------------------------------------
    // Target candidates (any land — CR 109.2, gathered from the controller's
    // battlefield in the no-Game shape-only posture)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetLands_AreLandsOnTheBattlefield()
    {
        var forest = AddLandToBattlefield(_alice, "Forest", new[] { CardSubtype.Forest });
        AddLandToBattlefield(_alice, "Island", new[] { CardSubtype.Island });
        // A non-land permanent must NOT be a candidate.
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
            { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var candidates = ThespiansStageFactory.TargetLands(_alice);

        candidates.Should().Contain(forest);
        candidates.Should().NotContain(bear);
        candidates.Should().HaveCount(2, "two lands on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Copy resolution — becomes a copy of the chosen target land
    // -----------------------------------------------------------------------

    [Fact]
    public void CopyAbility_Resolve_BecomesCopyOfTargetLand()
    {
        var (land, effects) = PlaceWithEffects();
        var island = AddLandToBattlefield(_alice, "Island", new[] { CardSubtype.Island });

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { island } });
        ability.Resolve();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "a copy of a land is still a Land");
        chars.Subtypes.Should().Contain(CardSubtype.Island, "copied the Island subtype");
    }

    [Fact]
    public void CopyAbility_Resolve_CopyIsPermanent_DoesNotExpireAtEndOfTurn()
    {
        var (land, effects) = PlaceWithEffects();
        var island = AddLandToBattlefield(_alice, "Island", new[] { CardSubtype.Island });

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { island } });
        ability.Resolve();

        // CR 707.2 — Thespian's Stage's copy is NOT "until end of turn"; it
        // persists. The cleanup-step expiry must not drop it.
        effects.ExpireEndOfTurn();

        effects.Compute(land).Subtypes.Should().Contain(CardSubtype.Island,
            "the copy is permanent — surviving the cleanup step");
    }

    [Fact]
    public void CopyAbility_Resolve_RetainsItsOwnAbilities()
    {
        // "except it has this ability" — after copying a vanilla land, Thespian's
        // Stage keeps its own runtime ability instances (the {C} mana ability +
        // the copy ability). CopyCharacteristicsEffect rewrites the
        // characteristics row only; it never strips Land.Abilities.
        var (land, _) = PlaceWithEffects();
        var island = AddLandToBattlefield(_alice, "Island", new[] { CardSubtype.Island });

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { island } });
        ability.Resolve();

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "retains its own {T}: Add {C}");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "retains 'this ability' — the copy ability itself");
    }

    [Fact]
    public void CopyAbility_Resolve_NoOp_WhenTargetNoLongerALand()
    {
        var (land, effects) = PlaceWithEffects();
        var island = AddLandToBattlefield(_alice, "Island", new[] { CardSubtype.Island });

        // Target leaves the battlefield before resolution (CR 608.2b illegal).
        _alice.Zones.Battlefield.RemoveCard(island);
        island.SetZone(ZoneType.Graveyard);

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { island } });
        ability.Resolve();

        effects.Compute(land).Subtypes.Should().NotContain(CardSubtype.Island,
            "the target left the battlefield — copy does nothing");
    }
}
