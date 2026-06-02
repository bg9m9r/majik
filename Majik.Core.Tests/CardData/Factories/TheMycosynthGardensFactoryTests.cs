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
/// Tests for <see cref="TheMycosynthGardensFactory"/>.
///
/// Oracle (Scryfall-confirmed 2026-06-02):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color.
///    {X}, {T}: This land becomes a copy of target nontoken artifact you
///    control with mana value X."
///
/// Covers:
/// - Identity: Land — Sphere, name, non-basic, non-legendary.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} vanilla mana ability (from JSON).
/// - {1}: Add one mana of any color — five WUBRG mana abilities (from JSON).
/// - Copy ability shape: cost {X} + tap, 1..1 target-artifact target request.
/// - Target candidates: nontoken artifacts you control with mana value X
///   (tokens / non-artifacts / wrong-mv / opponent's filtered out).
/// - Copy resolution: becomes a PERMANENT copy (no end-of-turn expiry) of the
///   chosen target artifact via <see cref="CopyCharacteristicsEffect"/>.
/// - The land retains its own ability instances after copying.
/// </summary>
[Trait("Color", "C")]
public class TheMycosynthGardensFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // Place The Mycosynth Gardens (with effects service + X provider) on
    // Alice's battlefield.
    private (Land land, ContinuousEffectsService effects) PlaceWithEffects(int x)
    {
        var effects = new ContinuousEffectsService();
        var land = TheMycosynthGardensFactory.Create(_alice, effects, () => x);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return (land, effects);
    }

    private static Artifact AddArtifactToBattlefield(
        Player controller, string name, string manaCost, bool token = false)
    {
        var artifact = new Artifact(name, manaCost)
            { Owner = controller, Controller = controller };
        if (token) artifact.MarkAsToken();
        artifact.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(artifact);
        return artifact;
    }

    private static ActivatedAbility CopyAbility(Land land) =>
        land.Abilities.OfType<ActivatedAbility>().Single();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_Sphere_NamedTheMycosynthGardens()
    {
        var land = TheMycosynthGardensFactory.Create(_alice);
        land.Name.Should().Be("The Mycosynth Gardens");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Sphere).Should().BeTrue("Land — Sphere");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary()
    {
        var land = TheMycosynthGardensFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("not legendary");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TheMycosynthGardens()
    {
        var card = NamedCardFactory.Create("The Mycosynth Gardens", _alice);
        card.Should().BeOfType<Land>();
        card!.Name.Should().Be("The Mycosynth Gardens");
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasSixManaAbilities_AndOneCopyActivatedAbility()
    {
        var land = TheMycosynthGardensFactory.Create(_alice);
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "{T}: Add {C} plus the five {1} any-color mana abilities");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {X}, {T} copy ability is a stack-using activated ability");
    }

    [Fact]
    public void AnyColorManaAbilities_CoverAllFiveColors()
    {
        var land = TheMycosynthGardensFactory.Create(_alice);
        var produced = land.Abilities.OfType<ManaAbility>()
            .Select(m => m.ManaGenerated)
            .ToList();

        // {T}: Add {C} produces a generic/colorless pip; the five {1}
        // abilities each produce one colored pip (W/U/B/R/G).
        produced.Should().Contain(c => c.White == 1);
        produced.Should().Contain(c => c.Blue == 1);
        produced.Should().Contain(c => c.Black == 1);
        produced.Should().Contain(c => c.Red == 1);
        produced.Should().Contain(c => c.Green == 1);
    }

    // -----------------------------------------------------------------------
    // Copy ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CopyAbility_HasXManaCostAndTargetArtifactRequest()
    {
        var land = TheMycosynthGardensFactory.Create(_alice);
        var ability = CopyAbility(land);

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Description.Should().Contain("X");
        ability.TargetRequests.Should().ContainSingle();
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("artifact");
    }

    // -----------------------------------------------------------------------
    // Target candidates — nontoken artifacts you control with mana value X
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetArtifacts_AreNontokenArtifactsYouControlWithManaValueX()
    {
        // X = 2.
        var match = AddArtifactToBattlefield(_alice, "Mycosynth Lattice", "{2}"); // mv 2, match
        AddArtifactToBattlefield(_alice, "Sol Ring", "{1}");                       // mv 1, wrong mv
        AddArtifactToBattlefield(_alice, "Construct", "{2}", token: true);         // token excluded
        var bobArtifact = AddArtifactToBattlefield(_bob, "Foreign Ring", "{2}");   // opponent's
        // A non-artifact mv-2 permanent must NOT be a candidate.
        var bear = new Creature("Bear", "{1}{G}", 2, 2)
            { Owner = _alice, Controller = _alice };
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var candidates = TheMycosynthGardensFactory.TargetArtifacts(_alice, 2);

        candidates.Should().Contain(match);
        candidates.Should().NotContain(bear, "creatures are not artifacts");
        candidates.Should().NotContain(bobArtifact, "you don't control it");
        candidates.Should().HaveCount(1, "only the nontoken artifact you control with mv 2");
    }

    // -----------------------------------------------------------------------
    // Copy resolution — becomes a copy of the chosen target artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void CopyAbility_Resolve_BecomesCopyOfTargetArtifact()
    {
        var (land, effects) = PlaceWithEffects(x: 2);
        var artifact = new Artifact("Mind Stone", "{2}")
            { Owner = _alice, Controller = _alice };
        artifact.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(artifact);

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { artifact } });
        ability.Resolve();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Artifact,
            "a copy of an artifact is now an Artifact (CR 707.2)");
    }

    [Fact]
    public void CopyAbility_Resolve_CopyIsPermanent_DoesNotExpireAtEndOfTurn()
    {
        var (land, effects) = PlaceWithEffects(x: 2);
        var artifact = new Artifact("Mind Stone", "{2}")
            { Owner = _alice, Controller = _alice };
        artifact.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(artifact);

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { artifact } });
        ability.Resolve();

        // CR 707.2 — the copy is NOT "until end of turn"; it persists. The
        // cleanup-step expiry must not drop it.
        effects.ExpireEndOfTurn();

        effects.Compute(land).Types.Should().Contain(CardType.Artifact,
            "the copy is permanent — surviving the cleanup step");
    }

    [Fact]
    public void CopyAbility_Resolve_RetainsItsOwnAbilities()
    {
        var (land, _) = PlaceWithEffects(x: 2);
        var artifact = new Artifact("Mind Stone", "{2}")
            { Owner = _alice, Controller = _alice };
        artifact.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(artifact);

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { artifact } });
        ability.Resolve();

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "retains its own mana abilities — copy rewrites characteristics only");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "retains the copy ability instance");
    }

    [Fact]
    public void CopyAbility_Resolve_NoOp_WhenManaValueDoesNotMatchX()
    {
        // X = 2 but the chosen target has mv 1 — became illegal (CR 608.2b).
        var (land, effects) = PlaceWithEffects(x: 2);
        var artifact = new Artifact("Sol Ring", "{1}")
            { Owner = _alice, Controller = _alice };
        artifact.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(artifact);

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { artifact } });
        ability.Resolve();

        effects.Compute(land).Types.Should().NotContain(CardType.Artifact,
            "target mana value != X — copy does nothing");
    }

    [Fact]
    public void CopyAbility_Resolve_NoOp_WhenTargetLeftBattlefield()
    {
        var (land, effects) = PlaceWithEffects(x: 2);
        var artifact = new Artifact("Mind Stone", "{2}")
            { Owner = _alice, Controller = _alice };
        artifact.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(artifact);

        // Target leaves the battlefield before resolution (CR 608.2b illegal).
        _alice.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);

        var ability = CopyAbility(land);
        ability.SetChosenTargets(new[] { new object[] { artifact } });
        ability.Resolve();

        effects.Compute(land).Types.Should().NotContain(CardType.Artifact,
            "the target left the battlefield — copy does nothing");
    }
}
