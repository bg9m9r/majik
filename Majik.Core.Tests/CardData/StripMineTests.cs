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
/// Tests for <see cref="StripMineFactory"/> — Land with
/// {T}: Add {C} and {T}, Sacrifice Strip Mine: destroy target land.
///
/// Covers:
/// - Card identity (Land, name, nonbasic) + <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability taps the land and produces colorless.
/// - Activated ability: target ANY land (basic or nonbasic) → graveyard +
///   Strip Mine sac'd. This is the key behavioural diff from Wasteland.
/// - Activated ability: illegal target (non-land) → no-op + still sac.
/// </summary>
public class StripMineTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void StripMine_IsNonbasicLand()
    {
        var land = StripMineFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.Name.Should().Be("Strip Mine");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_StripMine()
    {
        var card = NamedCardFactory.Create("Strip Mine", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Strip Mine");
        card.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void StripMine_HasColorlessManaAbility_AndActivationTapsLandAndProducesC()
    {
        var land = StripMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var manaAbility = land.Abilities.OfType<ManaAbility>().Single();

        manaAbility.CanActivate().Should().BeTrue();
        var produced = manaAbility.Activate();

        // {C} parses into the Generic slot today (no dedicated Colorless
        // property on ManaCost — mirrors Wasteland / Phyrexian Tower).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Black.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}, Sacrifice Strip Mine: Destroy target land (ANY land)
    // -----------------------------------------------------------------------

    [Fact]
    public void StripMine_HasDestroyActivatedAbility_WithSingleTargetRequest()
    {
        var land = StripMineFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        // Note: "target land" — not restricted to nonbasic (that's
        // Wasteland's signature).
        activated.TargetRequests[0].Description.Should().Contain("land");
        activated.TargetRequests[0].Description.Should().NotContain("nonbasic");
    }

    [Fact]
    public void StripMine_Destroy_NonbasicLand_TargetGoesToGraveyard_StripMineSacrificed()
    {
        var target = new Land(
            name: "Karakas",
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: null);
        target.SetOwner(_bob);
        target.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var stripMine = StripMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(stripMine);
        stripMine.SetZone(ZoneType.Battlefield);

        var activated = stripMine.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        activated.Resolve();

        _bob.Zones.Graveyard.GetCards().Should().Contain(target);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(target);
        target.Zone.Should().Be(ZoneType.Graveyard);

        _alice.Zones.Graveyard.GetCards().Should().Contain(stripMine);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(stripMine);
        stripMine.Zone.Should().Be(ZoneType.Graveyard);

        stripMine.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void StripMine_Destroy_BasicLand_Works_UnlikeWasteland()
    {
        // The signature difference vs. Wasteland: Strip Mine's target is
        // "target land" (not "target nonbasic land"). A basic Mountain
        // is a legal target.
        var basicLand = new Land(
            name: "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        basicLand.SetOwner(_bob);
        basicLand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(basicLand);
        basicLand.SetZone(ZoneType.Battlefield);

        var stripMine = StripMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(stripMine);
        stripMine.SetZone(ZoneType.Battlefield);

        var activated = stripMine.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { basicLand },
        });
        activated.Resolve();

        // Basic land destroyed.
        _bob.Zones.Graveyard.GetCards().Should().Contain(basicLand);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(basicLand);
        basicLand.Zone.Should().Be(ZoneType.Graveyard);

        // Strip Mine self-sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(stripMine);
        stripMine.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void StripMine_Destroy_NonLandTarget_IsNoOp_ButStillSacrifices()
    {
        // CR 608.2b — an illegal target makes the part of the effect that
        // involves the target do nothing. The sacrifice cost is still
        // paid, so Strip Mine still goes to the graveyard.
        var notALand = new Creature("Llanowar Elves", manaCost: "{G}", power: 1, toughness: 1);
        notALand.SetOwner(_bob);
        notALand.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(notALand);
        notALand.SetZone(ZoneType.Battlefield);

        var stripMine = StripMineFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(stripMine);
        stripMine.SetZone(ZoneType.Battlefield);

        var activated = stripMine.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var c in activated.Costs) c.Pay(_alice);

        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { notALand },
        });
        activated.Resolve();

        // Creature stays put — illegal target.
        _bob.Zones.Battlefield.GetCards().Should().Contain(notALand);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(notALand);

        // Strip Mine still sacrificed.
        _alice.Zones.Graveyard.GetCards().Should().Contain(stripMine);
        stripMine.Zone.Should().Be(ZoneType.Graveyard);
    }
}
