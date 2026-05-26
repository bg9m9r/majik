using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="GhostQuarterFactory"/> — Land with
/// {T}: Add {C} and {T}, Sacrifice Ghost Quarter: destroy target land;
/// its controller may search their library for a basic land card, put it
/// onto the battlefield, then shuffle.
///
/// Covers:
/// - Card identity (Land, name, nonbasic) + <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability taps + produces colorless.
/// - Activated ability: target ANY land (basic or nonbasic) is legal.
/// - Activated ability: destroyed land's controller (not the activator!)
///   tutors a basic land — no live agent in tests = legacy auto-accept
///   posture, so Bob's basic Forest comes off his library to his side.
/// - Activated ability: destroyed land's controller with no basic in
///   library — still shuffles, Ghost Quarter still sac'd.
/// - Activated ability: illegal target (non-land) → no-op, no search, but
///   Ghost Quarter still sacrificed.
/// </summary>
public class GhostQuarterTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostQuarter_IsNonbasicLand()
    {
        var land = GhostQuarterFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Name.Should().Be("Ghost Quarter");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GhostQuarter()
    {
        var card = NamedCardFactory.Create("Ghost Quarter", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Ghost Quarter");
        card.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostQuarter_HasColorlessManaAbility_AndActivationTapsAndProducesC()
    {
        var land = GhostQuarterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostQuarter_HasDestroyActivatedAbility_WithSingleTargetRequest()
    {
        var land = GhostQuarterFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("land");
        activated.TargetRequests[0].Description.Should().NotContain("nonbasic");
    }

    // -----------------------------------------------------------------------
    // {T}, Sacrifice Ghost Quarter: Destroy target land; controller may tutor
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostQuarter_Destroy_NonbasicLand_DestroyedControllerTutorsBasic()
    {
        // Bob controls a nonbasic land. He has a basic Forest + a nonbasic
        // dual in library. The destroyed land's controller (Bob) tutors
        // the basic Forest to his side (legacy auto-accept posture — no
        // live agent wired in this test).
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var basicForest = new Land(
            "Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        basicForest.SetOwner(_bob);
        _bob.Zones.Library.AddCard(basicForest);
        basicForest.SetZone(ZoneType.Library);

        var nonbasicDual = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        nonbasicDual.SetOwner(_bob);
        _bob.Zones.Library.AddCard(nonbasicDual);
        nonbasicDual.SetZone(ZoneType.Library);

        var gq = GhostQuarterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gq);
        gq.SetZone(ZoneType.Battlefield);

        var activated = gq.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        // Target destroyed (Bob's graveyard).
        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        target.Zone.Should().Be(ZoneType.Graveyard);

        // Bob tutored the basic Forest (CR 205.4a — basic supertype + land).
        // Dual stays in library.
        _bob.Zones.Battlefield.GetCards().Should().Contain(basicForest);
        basicForest.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Library.GetCards().Should().Contain(nonbasicDual);
        _bob.Zones.Library.GetCards().Should().NotContain(basicForest);

        // Ghost Quarter self-sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(gq);
        gq.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void GhostQuarter_Destroy_BasicLand_IsLegal_AndControllerTutors()
    {
        // Unlike Wasteland, Ghost Quarter targets ANY land — a basic
        // Mountain is a legal target.
        var basicMountain = new Land(
            name: "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        basicMountain.SetOwner(_bob);
        basicMountain.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(basicMountain);
        basicMountain.SetZone(ZoneType.Battlefield);

        // Bob has another basic in library to fetch.
        var libraryIsland = new Land(
            "Island",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Island });
        libraryIsland.SetOwner(_bob);
        _bob.Zones.Library.AddCard(libraryIsland);
        libraryIsland.SetZone(ZoneType.Library);

        var gq = GhostQuarterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gq);
        gq.SetZone(ZoneType.Battlefield);

        var activated = gq.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { basicMountain },
        });
        activated.Resolve();

        // Basic Mountain destroyed.
        _bob.Zones.Graveyard.GetCards().Should().Contain(basicMountain);
        basicMountain.Zone.Should().Be(ZoneType.Graveyard);

        // Bob fetched the basic Island.
        _bob.Zones.Battlefield.GetCards().Should().Contain(libraryIsland);

        // Ghost Quarter sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(gq);
    }

    [Fact]
    public void GhostQuarter_Destroy_NoBasicInLibrary_DestroysButNoTutor()
    {
        // Bob has no basic land in library — search finds nothing.
        // Ghost Quarter still resolves: target destroyed, GQ sacrificed.
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        // Library contains only a nonbasic — search finds no basic.
        var dual = new Land(
            "Stomping Ground",
            supertypes: null,
            new[] { CardSubtype.Mountain, CardSubtype.Forest });
        dual.SetOwner(_bob);
        _bob.Zones.Library.AddCard(dual);
        dual.SetZone(ZoneType.Library);

        var gq = GhostQuarterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gq);
        gq.SetZone(ZoneType.Battlefield);

        var activated = gq.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        target.Zone.Should().Be(ZoneType.Graveyard);

        // Dual untouched in library.
        _bob.Zones.Library.GetCards().Should().Contain(dual);

        // Ghost Quarter sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(gq);
    }

    [Fact]
    public void GhostQuarter_Destroy_NonLandTarget_IsNoOp_ButStillSacrifices()
    {
        // CR 608.2b — illegal target makes the target-dependent part of
        // the effect do nothing. The sacrifice cost is still paid, so
        // Ghost Quarter still ends in the graveyard. No tutor occurs
        // (there's no destroyed-land controller to reference).
        var notALand = new Creature("Llanowar Elves", manaCost: "{G}", power: 1, toughness: 1);
        notALand.SetOwner(_bob);
        notALand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(notALand);
        notALand.SetZone(ZoneType.Battlefield);

        // Stage a basic in Bob's library — it should NOT be tutored
        // because the destroy never happens.
        var basicForest = new Land(
            "Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        basicForest.SetOwner(_bob);
        _bob.Zones.Library.AddCard(basicForest);
        basicForest.SetZone(ZoneType.Library);

        var gq = GhostQuarterFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(gq);
        gq.SetZone(ZoneType.Battlefield);

        var activated = gq.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { notALand },
        });
        activated.Resolve();

        // Creature stays.
        _bob.Zones.Battlefield.GetCards().Should().Contain(notALand);

        // Basic Forest stays in library — no tutor triggered.
        _bob.Zones.Library.GetCards().Should().Contain(basicForest);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(basicForest);

        // Ghost Quarter still sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(gq);
        gq.Zone.Should().Be(ZoneType.Graveyard);
    }
}
