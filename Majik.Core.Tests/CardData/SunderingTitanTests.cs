using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sundering Titan (Mirrodin, {8}).
///
/// Covers:
///   - Identity: 7/10 Artifact Creature — Phyrexian Golem at {8}, owner /
///     controller, NamedCardFactory dispatch.
///   - Ability list: two TriggeredAbilities (ETB + LTB), no others.
///   - ETB destroys one land of each basic land type across players'
///     battlefields.
///   - ETB skips basic types with no matching land.
///   - ETB on dual-typed land (Tundra = Plains+Island) destroys at most
///     once via deterministic first-found scan; the second-type pass falls
///     through to a different land.
///   - LTB has the same destroy shape as ETB.
///   - Indestructible lands survive (CR 702.12 via Fx.MoveToGraveyard
///     gating).
/// </summary>
public class SunderingTitanTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Func<IReadOnlyList<Player>> AllPlayers => () => new[] { _alice, _bob };

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SunderingTitan_Identity()
    {
        var titan = SunderingTitanFactory.Create(_alice);

        titan.Name.Should().Be("Sundering Titan");
        titan.ManaCost.Should().Be("{8}");
        titan.HasType(CardType.Creature).Should().BeTrue();
        titan.HasType(CardType.Artifact).Should().BeTrue("Artifact Creature (CR 301.1 / 302.1)");
        titan.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        titan.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        titan.BasePower.Should().Be(7);
        titan.BaseToughness.Should().Be(10);
        titan.Owner.Should().BeSameAs(_alice);
        titan.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SunderingTitan_AbilityShape_TwoTriggers()
    {
        var titan = SunderingTitanFactory.Create(_alice);

        titan.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB destroy + LTB destroy");
    }

    [Fact]
    public void SunderingTitan_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sundering Titan", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sundering Titan");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(7);
        ((Creature)card).BaseToughness.Should().Be(10);
    }

    // -----------------------------------------------------------------------
    // ETB destroy — one land of each basic type
    // -----------------------------------------------------------------------

    [Fact]
    public void SunderingTitan_Etb_DestroysOneLandOfEachBasicType()
    {
        // Setup: Alice and Bob each have several basic-typed lands.
        var titan = SunderingTitanFactory.Create(_alice, AllPlayers, triggers: null);
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var plains = MakeBasic(_alice, "Plains", CardSubtype.Plains);
        var island = MakeBasic(_bob, "Island", CardSubtype.Island);
        var swamp = MakeBasic(_alice, "Swamp", CardSubtype.Swamp);
        var mountain = MakeBasic(_bob, "Mountain", CardSubtype.Mountain);
        var forest = MakeBasic(_alice, "Forest", CardSubtype.Forest);

        // Drive the ETB trigger directly — Snapcaster pattern. Order of
        // abilities: index 0 = ETB (OnEnterBattlefieldSelf), index 1 = LTB.
        var etb = titan.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in etb.Effects) e.Execute();

        plains.Zone.Should().Be(ZoneType.Graveyard, "ETB destroys a Plains");
        island.Zone.Should().Be(ZoneType.Graveyard, "ETB destroys an Island");
        swamp.Zone.Should().Be(ZoneType.Graveyard, "ETB destroys a Swamp");
        mountain.Zone.Should().Be(ZoneType.Graveyard, "ETB destroys a Mountain");
        forest.Zone.Should().Be(ZoneType.Graveyard, "ETB destroys a Forest");
    }

    [Fact]
    public void SunderingTitan_Etb_SkipsMissingBasicTypes()
    {
        // Only Mountains on the table. ETB should destroy one Mountain and
        // skip the other four basic types cleanly.
        var titan = SunderingTitanFactory.Create(_alice, AllPlayers, triggers: null);
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var mountain1 = MakeBasic(_alice, "Mountain-1", CardSubtype.Mountain);
        var mountain2 = MakeBasic(_bob, "Mountain-2", CardSubtype.Mountain);

        var etb = titan.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in etb.Effects) e.Execute();

        // One Mountain destroyed (the first found — Alice's), the other
        // survives.
        (mountain1.Zone == ZoneType.Graveyard ^ mountain2.Zone == ZoneType.Graveyard)
            .Should().BeTrue("exactly one Mountain is destroyed (one per basic type)");
    }

    [Fact]
    public void SunderingTitan_Etb_DualLand_DestroyedOnFirstTypePass()
    {
        // Tundra = Plains + Island. With only a Tundra on the table:
        //   Plains pass: destroys Tundra.
        //   Island pass: Tundra is now in the graveyard, no Island target →
        //     skipped.
        // Other three types have no candidates → skipped.
        var titan = SunderingTitanFactory.Create(_alice, AllPlayers, triggers: null);
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var tundra = new Land(
            "Tundra",
            subtypes: new[] { CardSubtype.Plains, CardSubtype.Island });
        tundra.SetOwner(_alice);
        tundra.SetController(_alice);
        tundra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tundra);

        var etb = titan.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in etb.Effects) e.Execute();

        tundra.Zone.Should().Be(ZoneType.Graveyard,
            "the dual-typed Tundra is destroyed on the Plains pass");
    }

    [Fact]
    public void SunderingTitan_Etb_DualLand_SecondPassFindsDifferentLand()
    {
        // Tundra (Plains+Island) + a plain Island on the table.
        //   Plains pass: first Plains-typed land = Tundra → destroyed.
        //   Island pass: Tundra gone, the plain Island is destroyed.
        var titan = SunderingTitanFactory.Create(_alice, AllPlayers, triggers: null);
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var tundra = new Land(
            "Tundra",
            subtypes: new[] { CardSubtype.Plains, CardSubtype.Island });
        tundra.SetOwner(_alice);
        tundra.SetController(_alice);
        tundra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tundra);

        var island = MakeBasic(_bob, "Island", CardSubtype.Island);

        var etb = titan.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in etb.Effects) e.Execute();

        tundra.Zone.Should().Be(ZoneType.Graveyard,
            "Tundra destroyed on Plains pass (dual-typed land)");
        island.Zone.Should().Be(ZoneType.Graveyard,
            "Island pass falls through to the plain Island after Tundra is gone");
    }

    // -----------------------------------------------------------------------
    // LTB destroy
    // -----------------------------------------------------------------------

    [Fact]
    public void SunderingTitan_Ltb_DestroysOneLandOfEachBasicType()
    {
        var titan = SunderingTitanFactory.Create(_alice, AllPlayers, triggers: null);
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        var plains = MakeBasic(_alice, "Plains", CardSubtype.Plains);
        var island = MakeBasic(_bob, "Island", CardSubtype.Island);
        var swamp = MakeBasic(_alice, "Swamp", CardSubtype.Swamp);
        var mountain = MakeBasic(_bob, "Mountain", CardSubtype.Mountain);
        var forest = MakeBasic(_alice, "Forest", CardSubtype.Forest);

        // Run the LTB effect (index 1 — registered after the ETB).
        var ltb = titan.Abilities.OfType<TriggeredAbility>().Last();
        foreach (var e in ltb.Effects) e.Execute();

        plains.Zone.Should().Be(ZoneType.Graveyard);
        island.Zone.Should().Be(ZoneType.Graveyard);
        swamp.Zone.Should().Be(ZoneType.Graveyard);
        mountain.Zone.Should().Be(ZoneType.Graveyard);
        forest.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Indestructible — CR 702.12 (gated through Fx.MoveToGraveyard).
    // -----------------------------------------------------------------------

    [Fact]
    public void SunderingTitan_Etb_IndestructibleLand_Survives()
    {
        var titan = SunderingTitanFactory.Create(_alice, AllPlayers, triggers: null);
        _alice.Zones.Battlefield.AddCard(titan);
        titan.SetZone(ZoneType.Battlefield);

        // An indestructible Mountain (e.g. Darksteel-style basic — not real
        // print, but the engine's indestructible gate is keyword-based).
        var mountain = MakeBasic(_alice, "Darksteel Mountain", CardSubtype.Mountain);
        mountain.AddAbility(new KeywordAbility("Indestructible", mountain, _alice));

        var etb = titan.Abilities.OfType<TriggeredAbility>().First();
        foreach (var e in etb.Effects) e.Execute();

        mountain.Zone.Should().Be(ZoneType.Battlefield,
            "Indestructible (CR 702.12) cancels the destroy");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Land MakeBasic(Player owner, string name, CardSubtype basicType)
    {
        var land = new Land(
            name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { basicType });
        land.SetOwner(owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }
}
